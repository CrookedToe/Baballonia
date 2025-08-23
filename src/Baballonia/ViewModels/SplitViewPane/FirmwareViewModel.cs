using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Baballonia.Contracts;
using Baballonia.Models;
using Baballonia.Services;
using Baballonia.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class FirmwareViewModel : ViewModelBase, IDisposable
{
    #region Services and Dependencies

    private readonly GithubService _githubService;
    private readonly FirmwareService _firmwareService;
    private readonly ILocalSettingsService _settingsService;
    private FirmwareSession _firmwareSession;

    #endregion

    #region Properties

    [ObservableProperty]
    private bool _supportsUVCFirmware;

    [ObservableProperty]
    private bool _isDeviceSelected;

    [ObservableProperty]
    private bool _isWirelessFirmware;

    [ObservableProperty]
    private bool _wirelessWarningVisible;

    [ObservableProperty]
    private bool _isFlashing;

    [ObservableProperty]
    private bool _isFinished;

    [ObservableProperty]
    private string _wifiSsid = string.Empty;

    [ObservableProperty]
    private string _wifiPassword = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _availableFirmwareTypes = new();

    [ObservableProperty]
    private string? _selectedFirmwareType;

    [ObservableProperty]
    private ObservableCollection<string> _availableSerialPorts = new();

    [ObservableProperty]
    private ObservableCollection<string> _availableWifiNetworks = new();

    [ObservableProperty]
    private string? _selectedSerialPort;

    [ObservableProperty] private object? _deviceModeSelectedItem;

    public bool IsReadyToFlashFirmware =>
        !string.IsNullOrEmpty(SelectedSerialPort) &&
        !string.IsNullOrEmpty(SelectedFirmwareType) &&
        (!IsWirelessFirmware ||
         (!string.IsNullOrEmpty(WifiSsid) && !string.IsNullOrEmpty(WifiPassword)));

    #endregion

    public FirmwareViewModel()
    {
        // Initialize services
        _githubService = Ioc.Default.GetRequiredService<GithubService>();
        _firmwareService = Ioc.Default.GetRequiredService<FirmwareService>();
        _settingsService = Ioc.Default.GetService<ILocalSettingsService>()!;
        _settingsService.Load(this);

        // Initialize events
        _firmwareService.OnFirmwareUpdateStart += HandleFirmwareUpdateStart;
        _firmwareService.OnFirmwareUpdateError += msg => Debug.WriteLine(msg);
        _firmwareService.OnFirmwareUpdateComplete += HandleFirmwareUpdateComplete;

        // Initial state setup
        Task.Run(async () =>
        {
            RefreshSerialPorts();
            await RefreshWifiNetworks();
            LoadAvailableFirmwareTypesAsync();
        });
    }

    partial void OnSelectedSerialPortChanged(string? oldValue, string? newValue)
    {
        SelectedSerialPort = newValue;
        IsDeviceSelected = !string.IsNullOrEmpty(newValue);
        if (_firmwareSession is not null)
            _firmwareSession.Dispose();
        _firmwareSession = _firmwareService.StartSession(CommandSenderType.Serial, newValue!);
        OnPropertyChanged(nameof(IsReadyToFlashFirmware));

        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var response = await _firmwareSession.WaitForHeartbeatAsync(TimeSpan.FromSeconds(5));
            SupportsUVCFirmware = response != null;
        });
    }

    partial void OnSelectedFirmwareTypeChanged(string? oldValue, string? newValue)
    {
        IsWirelessFirmware = newValue!.Contains("Babble_USB");
        if (IsWirelessFirmware)
        {
            Task.Run(async () => await RefreshWifiNetworks());
        }
        OnPropertyChanged(nameof(IsReadyToFlashFirmware));
    }

    partial void OnWifiSsidChanged(string? oldValue, string? newValue)
    {
        if (!IsWirelessFirmware || string.IsNullOrEmpty(newValue)) return;

        OnPropertyChanged(nameof(IsReadyToFlashFirmware));
    }

    partial void OnWifiPasswordChanged(string? oldValue, string? newValue)
    {
        if (!IsWirelessFirmware || string.IsNullOrEmpty(newValue)) return;

        OnPropertyChanged(nameof(IsReadyToFlashFirmware));
    }

    #region UI Event Handlers

    [RelayCommand]
    private void OnDismissWirelessWarning()
    {
        WirelessWarningVisible = false;
    }

    private void HandleFirmwareUpdateStart()
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsFlashing = true;
            IsFinished = false;
        });
    }

    private void HandleFirmwareUpdateComplete()
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsFlashing = false;

            if (IsWirelessFirmware)
            {
                WirelessWarningVisible = true;
            }
            else
            {
                IsFinished = true;
            }
        });
    }

    #endregion

    #region Operations

    [RelayCommand]
    private async Task RefreshWifiNetworks()
    {
        AvailableWifiNetworks.Clear();

        var response = await _firmwareSession.SendCommandAsync(new FirmwareRequests.ScanWifiRequest(), TimeSpan.FromSeconds(5));
        foreach (var port in response!.Networks.OrderBy(network => network.Rssi).Select(network => network.Ssid))
        {
            AvailableWifiNetworks.Add(port);
        }
    }

    [RelayCommand]
    private async Task RefreshSerialPorts()
    {
        AvailableSerialPorts.Clear();

        var response = _firmwareService.ProbeComPorts(TimeSpan.FromSeconds(3)).ToHashSet();
        foreach (var port in response)
        {
            AvailableSerialPorts.Add(port);

            if (_firmwareSession is not null)
                _firmwareSession.Dispose();

            _firmwareSession = _firmwareService.StartSession(CommandSenderType.Serial, port);
            await _firmwareSession.SendCommandAsync(new FirmwareRequests.SetPausedRequest(true), TimeSpan.FromSeconds(5));
            _firmwareSession.Dispose();
        }

        var serialPorts = _firmwareService.FindAvailableComPorts();
        foreach (var port in serialPorts)
        {
            if (response.Contains(port)) continue;
            AvailableSerialPorts.Add(port);
        }
    }

    private async void LoadAvailableFirmwareTypesAsync()
    {
        try
        {
            var githubRelease = await _githubService.GetReleases("EyeTrackVR", "OpenIris");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AvailableFirmwareTypes.Clear();
                foreach (var asset in githubRelease.assets)
                {
                    if (asset.name.ToLower().Contains("babble"))
                    {
                        AvailableFirmwareTypes.Add(asset.name);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            // Log or handle the exception
            Debug.WriteLine($"Error loading firmware types: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SendDeviceWifiCredentials()
    {
        Dispatcher.UIThread.Post(() => IsFlashing = true);
        // await _firmwareSession.SendCommandAsync(new FirmwareRequests.ConnectWifiRequest(m));
        Dispatcher.UIThread.Post(() => IsFlashing = false);
        Dispatcher.UIThread.Post(() => IsFinished = true);
    }

    [RelayCommand]
    private async Task SetDeviceMode()
    {
        if (_deviceModeSelectedItem is not ComboBoxItem comboBoxItem)
            return;

        FirmwareRequests.Mode m = comboBoxItem.Tag switch
        {
            "auto" => FirmwareRequests.Mode.Auto,
            "wifi" => FirmwareRequests.Mode.Wifi,
            "uvc" => FirmwareRequests.Mode.UVC,
            _ => FirmwareRequests.Mode.Auto
        };

        await _firmwareSession.SendCommandAsync(new FirmwareRequests.SetModeRequest(m), TimeSpan.FromSeconds(5));
    }

    [RelayCommand]
    private async Task FlashDeviceFirmware()
    {
        if (string.IsNullOrEmpty(SelectedFirmwareType) || string.IsNullOrEmpty(SelectedSerialPort))
        {
            return;
        }

        string? tempDir = null;
        CancellationTokenSource cts = new CancellationTokenSource();

        try
        {
            await Task.Run(async () =>
            {
                var releases = await _githubService.GetReleases("EyeTrackVR", "OpenIris");
                var asset = releases.assets.FirstOrDefault(a => a.name == SelectedFirmwareType);

                if (asset == null)
                {
                    throw new Exception($"Selected firmware {SelectedFirmwareType} not found");
                }

                tempDir = Directory.CreateTempSubdirectory().FullName;
                var pathToBinary = await _githubService.DownloadAndExtractOpenIrisRelease(
                    tempDir,
                    asset.browser_download_url,
                    asset.name);

                _firmwareService.UploadFirmware(SelectedSerialPort!, pathToBinary.firmwarePath);

                if (IsWirelessFirmware)
                {
                    // Wait for user to acknowledge reconnection
                    var wirelessReconnectionEvent = new TaskCompletionSource<bool>();

                    // Set up a one-time event handler to catch when warning is dismissed
                    void WirelessWarningHandler(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
                    {
                        if (e.PropertyName == nameof(WirelessWarningVisible) && !WirelessWarningVisible)
                        {
                            wirelessReconnectionEvent.SetResult(true);
                            PropertyChanged -= WirelessWarningHandler;
                        }
                    }

                    PropertyChanged += WirelessWarningHandler;

                    // Wait for user to dismiss the warning or for timeout
                    var timeoutTask = Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
                    var completedTask = await Task.WhenAny(wirelessReconnectionEvent.Task, timeoutTask);

                    // Only send credentials if not timed out and not canceled
                    if (completedTask == wirelessReconnectionEvent.Task && !cts.Token.IsCancellationRequested)
                    {
                        SendDeviceWifiCredentials();
                    }
                }
            }, cts.Token);
        }
        catch (TaskCanceledException)
        {
            // Operation was canceled, no need for additional handling
        }
        catch (Exception ex)
        {
            // Log or display error to user
            Debug.WriteLine($"Error during firmware update: {ex.Message}");
            IsFlashing = false;
            IsFinished = false;
        }
        finally
        {
            cts.Dispose();

            // Clean up temp directory
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    #endregion

    #region Cleanup

    // Called when the ViewModel is no longer needed
    public void Dispose()
    {
        _firmwareService.OnFirmwareUpdateStart -= HandleFirmwareUpdateStart;
        _firmwareService.OnFirmwareUpdateComplete -= HandleFirmwareUpdateComplete;

        foreach (var port in AvailableSerialPorts)
        {
            _firmwareSession = _firmwareService.StartSession(CommandSenderType.Serial, port);
            _firmwareSession.SendCommand(new FirmwareRequests.SetPausedRequest(false), TimeSpan.FromSeconds(5));
            _firmwareSession.Dispose();
        }
    }

    #endregion
}
