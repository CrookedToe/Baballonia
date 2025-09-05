using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Helpers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OscCore;
using VRC.OSCQuery;

namespace Baballonia.Services;

/// <summary>
/// Unified OSC services for VRChat communication including query discovery, message receiving, and sending
/// </summary>

// Shared socket utilities to reduce code duplication across OSC services
internal static class OscSocketHelper
{
    public static Socket CreateUdpSocket() => new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    
    public static IPEndPoint? TryParseEndpoint(string? address, int port)
    {
        if (string.IsNullOrEmpty(address)) return null;
        try { return new IPEndPoint(IPAddress.Parse(address), port); }
        catch { return null; }
    }
}

// VRChat OSC Query service for automatic client discovery and settings synchronization
public sealed class OscQueryServiceWrapper(ILogger<OscQueryServiceWrapper> logger, ILocalSettingsService localSettingsService)
    : BackgroundService
{
    private readonly HashSet<OSCQueryServiceProfile> _profiles = [];
    private OSCQueryService? _serviceWrapper;
    private static readonly Regex VrChatClientRegex = new(@"VRChat-Client-[A-Za-z0-9]{6}$", RegexOptions.Compiled);
    private const int VrcPort = 9000;
    private const int RefreshIntervalMs = 5000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ipString = await localSettingsService.ReadSettingAsync<string>("OSCAddress");
        if (!IPAddress.TryParse(ipString, out var hostIp))
        {
            logger.LogError("Invalid OSC address configuration: {Address}", ipString);
            return;
        }

        await InitializeOscQueryService(hostIp);
        await RunRefreshLoop(stoppingToken);
    }

    private Task InitializeOscQueryService(IPAddress hostIp)
    {
        var tcpPort = Extensions.GetAvailableTcpPort();
        var udpPort = Extensions.GetAvailableUdpPort();

        _serviceWrapper = new OSCQueryServiceBuilder()
            .WithDiscovery(new MeaModDiscovery())
            .WithHostIP(hostIp)
            .WithTcpPort(tcpPort)
            .WithUdpPort(udpPort)
            .WithServiceName($"VRChat-Client-BabbleApp-{Utils.RandomString()}")
            .StartHttpServer()
            .AdvertiseOSCQuery()
            .AdvertiseOSC()
            .Build();

        logger.LogInformation("Started OSCQueryService {ServiceName} at TCP {TcpPort}, UDP {UdpPort}, HTTP http://{HostIP}:{TcpPort}",
            _serviceWrapper.ServerName, tcpPort, udpPort, _serviceWrapper.HostIP, tcpPort);

        _serviceWrapper.AddEndpoint<string>("/avatar/change", Attributes.AccessValues.ReadWrite, ["default"]);
        _serviceWrapper.OnOscQueryServiceAdded += AddProfileToList;
        
        return Task.CompletedTask;
    }

    private async Task RunRefreshLoop(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting OSCQuery auto-refresh services");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var useOscQuery = await localSettingsService.ReadSettingAsync<bool>("UseOSCQuery");
                if (useOscQuery && _serviceWrapper != null)
                {
                    _serviceWrapper.RefreshServices();
                    await UpdateVrChatSettings();
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error during OSC refresh cycle");
            }

            await Task.Delay(RefreshIntervalMs, stoppingToken);
        }
    }

    private void AddProfileToList(OSCQueryServiceProfile profile)
    {
        if (_profiles.Contains(profile) || (_serviceWrapper != null && profile.port == _serviceWrapper.TcpPort))
            return;

        _profiles.Add(profile);
        logger.LogInformation("Added OSCQuery profile {ProfileName} at http://{Address}:{Port}",
            profile.name, profile.address, profile.port);
    }

    private async Task UpdateVrChatSettings()
    {
        if (_profiles.Count == 0) return;

        try
        {
            var vrcProfile = _profiles.FirstOrDefault(p => VrChatClientRegex.IsMatch(p.name));
            if (vrcProfile == null) return;

            // Batch settings updates for better performance
            var tasks = new List<Task>();
            
            var hostIp = await localSettingsService.ReadSettingAsync<string>("OSCAddress");
            var vrcIp = vrcProfile.address.ToString();
            if (hostIp != vrcIp)
            {
                tasks.Add(localSettingsService.SaveSettingAsync("OSCAddress", vrcIp));
            }

            var hostPort = await localSettingsService.ReadSettingAsync<int>("OSCOutPort");
            if (hostPort != VrcPort)
            {
                tasks.Add(localSettingsService.SaveSettingAsync("OSCOutPort", VrcPort));
            }

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating VRChat settings from OSCQuery");
        }
    }

    public override void Dispose()
    {
        logger.LogInformation("OSCQuery teardown initiated");
        _serviceWrapper?.Dispose();
        logger.LogInformation("OSCQuery teardown completed");
        base.Dispose();
    }
}

// Background service for receiving and processing OSC messages with optimized packet handling
public sealed class OscRecvService : BackgroundService
{
    private readonly ILogger<OscRecvService> _logger;
    private readonly IOscTarget _oscTarget;
    private readonly ILocalSettingsService _settingsService;
    private readonly byte[] _recvBuffer = new byte[4096];

    private Socket? _recvSocket;
    private CancellationTokenSource _cts = new();
    private const int RetryDelayMs = 100;
    private const int ErrorDelayMs = 1000;

    public event Action<OscMessage> OnMessageReceived = _ => { };

    public OscRecvService(ILogger<OscRecvService> logger, IOscTarget oscTarget, ILocalSettingsService settingsService)
    {
        _logger = logger;
        _oscTarget = oscTarget;
        _settingsService = settingsService;

        _oscTarget.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(IOscTarget.InPort) && _oscTarget.InPort != default)
            {
                var endpoint = OscSocketHelper.TryParseEndpoint(_oscTarget.DestinationAddress, _oscTarget.InPort);
                if (endpoint != null) UpdateTarget(endpoint);
            }
        };
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting OSC Receive Service...");
        await _settingsService.Load(_oscTarget);
        _logger.LogDebug("OSC target loaded - Address: {Address}, InPort: {InPort}", _oscTarget.DestinationAddress, _oscTarget.InPort);
        await base.StartAsync(cancellationToken);
    }

    public IPEndPoint? UpdateTarget(IPEndPoint endpoint)
    {
        _cts.Cancel();
        CloseSocket();

        _recvSocket = OscSocketHelper.CreateUdpSocket();

        try
        {
            _recvSocket.Bind(endpoint);
            _oscTarget.IsConnected = true;
            _cts = new CancellationTokenSource();
            return (IPEndPoint)_recvSocket.LocalEndPoint!;
        }
        catch (SocketException ex)
        {
            _logger.LogWarning("Could not bind to recv endpoint {Endpoint}: {Message}", endpoint, ex.Message);
            _oscTarget.IsConnected = false;
            return null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("OSC Receive Service started");
        // Proper cancellation token handling for graceful shutdown
        using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _cts.Token);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (linkedToken.IsCancellationRequested || _recvSocket is not { IsBound: true })
            {
                await Task.Delay(RetryDelayMs, stoppingToken);
                continue;
            }

            try
            {
                var bytesReceived = await _recvSocket.ReceiveAsync(_recvBuffer, linkedToken.Token);
                if (bytesReceived > 0)
                {
                    ProcessReceivedData(bytesReceived);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown or target updates
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing OSC message");
                await Task.Delay(ErrorDelayMs, stoppingToken);
            }
        }
    }

    private void ProcessReceivedData(int bytesReceived)
    {
        try
        {
            var packet = OscPacket.Read(_recvBuffer, 0, bytesReceived);
            IEnumerable<OscMessage> messages = packet switch
            {
                OscBundle bundle => OscHelper.ExtractMessages(bundle),
                OscMessage message => new[] { message },
                _ => Array.Empty<OscMessage>()
            };

            foreach (var message in messages)
            {
                OnMessageReceived(message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse OSC packet");
        }
    }

    private void CloseSocket()
    {
        _recvSocket?.Close();
        _oscTarget.IsConnected = false;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        CloseSocket();
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _cts?.Dispose();
        _recvSocket?.Dispose();
        base.Dispose();
    }
}

// High-performance OSC message sender with connection management and batch processing
public sealed class OscSendService : IDisposable
{
    private readonly ILogger<OscSendService> _logger;
    private readonly IOscTarget _oscTarget;
    private CancellationTokenSource _cts = new();
    private Socket? _sendSocket;
    private const int DefaultPort = 8888;

    public event Action<int> OnMessagesDispatched = _ => { };

    public OscSendService(ILogger<OscSendService> logger, IOscTarget oscTarget)
    {
        _logger = logger;
        _oscTarget = oscTarget;

        _oscTarget.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(IOscTarget.OutPort))
            {
                HandlePortChange();
            }
        };
    }

    private void HandlePortChange()
    {
        if (_oscTarget.OutPort == default)
            _oscTarget.OutPort = DefaultPort;

        _oscTarget.DestinationAddress ??= IPAddress.Loopback.ToString();
        
        var endpoint = OscSocketHelper.TryParseEndpoint(_oscTarget.DestinationAddress, _oscTarget.OutPort);
        if (endpoint != null) UpdateTarget(endpoint);
    }

    private void UpdateTarget(IPEndPoint endpoint)
    {
        _cts.Cancel();
        CloseSocket();

        _sendSocket = OscSocketHelper.CreateUdpSocket();

        try
        {
            _sendSocket.Connect(endpoint);
            _oscTarget.IsConnected = true;
        }
        catch (SocketException ex)
        {
            _logger.LogWarning("Failed to connect to send endpoint {Endpoint}: {Message}", endpoint, ex.Message);
            _oscTarget.IsConnected = false;
        }
        finally
        {
            _cts = new CancellationTokenSource();
        }
    }

    public async Task Send(OscMessage message, CancellationToken ct = default)
    {
        if (_sendSocket is not { Connected: true }) return;

        try
        {
            var endpoint = OscSocketHelper.TryParseEndpoint(_oscTarget.DestinationAddress, _oscTarget.OutPort);
            if (endpoint != null)
            {
                await _sendSocket.SendToAsync(message.ToByteArray(), SocketFlags.None, endpoint, ct);
                OnMessagesDispatched(1);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending OSC message");
        }
    }

    public async Task Send(OscMessage[] messages, CancellationToken ct = default)
    {
        if (_sendSocket is not { Connected: true } || messages.Length == 0) return;

        try
        {
            var messageCount = 0;
            foreach (var message in messages)
            {
                await _sendSocket.SendAsync(message.ToByteArray(), SocketFlags.None, ct);
                messageCount++;
            }

            OnMessagesDispatched(messageCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending OSC message batch");
        }
    }

    private void CloseSocket()
    {
        _sendSocket?.Close();
        _oscTarget.IsConnected = false;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _sendSocket?.Dispose();
    }
}
