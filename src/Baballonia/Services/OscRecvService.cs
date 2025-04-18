﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaMiaDev.Contracts;
using AvaloniaMiaDev.OSC;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AvaloniaMiaDev.Services;

public class OscRecvService : BackgroundService
{
    private readonly ILogger<OscRecvService> _logger;
    private readonly IOscTarget _oscTarget;
    private readonly ILocalSettingsService _settingsService;

    private Socket _recvSocket;
    private readonly byte[] _recvBuffer = new byte[4096];

    private CancellationTokenSource _cts, _linkedToken;
    private CancellationToken _stoppingToken;

    public event Action<OscMessage> OnMessageReceived = _ => { };

    public OscRecvService(
        ILogger<OscRecvService> logger,
        IOscTarget oscTarget,
        ILocalSettingsService settingsService
    )
    {
        _logger = logger;
        _cts = new CancellationTokenSource();

        _oscTarget = oscTarget;
        _settingsService = settingsService;

        _oscTarget.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not nameof(IOscTarget.InPort))
            {
                return;
            }

            if (_oscTarget.InPort == default)
            {
                return;
            }

            if (_oscTarget.DestinationAddress is not null)
            {
                UpdateTarget(new IPEndPoint(IPAddress.Parse(_oscTarget.DestinationAddress), _oscTarget.InPort));
            }
        };
    }

    public async override Task StartAsync(CancellationToken cancellationToken)
    {
        await _settingsService.Load(_oscTarget);

        await base.StartAsync(cancellationToken);
    }

    public IPEndPoint UpdateTarget(IPEndPoint endpoint)
    {
        _cts.Cancel();
        _recvSocket?.Close();
        _oscTarget.IsConnected = false;

        _recvSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        try
        {
            _recvSocket.Bind(endpoint);
            _oscTarget.IsConnected = true;
            return (IPEndPoint)_recvSocket.LocalEndPoint!;
        }
        catch (SocketException ex)
        {
            _logger.LogWarning($"Could not bind to recv endpoint: {endpoint}. {ex.Message}");
        }
        finally
        {
            _cts = new CancellationTokenSource();
            _linkedToken = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken, _cts.Token);
        }

        return null!;
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        _linkedToken = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken, _cts.Token);

        while (!_stoppingToken.IsCancellationRequested)
        {
            if (_linkedToken.IsCancellationRequested || _recvSocket is not { IsBound: true })
            {
                continue;
            }

            try
            {
                var bytesReceived = await _recvSocket.ReceiveAsync(_recvBuffer, _linkedToken.Token);
                var offset = 0;
                var newMsg = await Task.Run(() => OscMessage.TryParseOsc(_recvBuffer, bytesReceived, ref offset), stoppingToken);
                if (newMsg == null)
                {
                    continue;
                }

                OnMessageReceived(newMsg);
            }
            catch (Exception)
            {

            }
        }
    }
}
