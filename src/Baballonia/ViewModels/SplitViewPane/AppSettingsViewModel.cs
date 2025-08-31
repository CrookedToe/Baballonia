using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Baballonia.Contracts;
using Baballonia.Services;
using Baballonia.Services.Inference.Filters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class AppSettingsViewModel : ViewModelBase
{
    public IOscTarget OscTarget { get; private set;}
    public ILocalSettingsService SettingsService { get; }
    public GithubService GithubService { get; private set;}
    public ParameterSenderService ParameterSenderService { get; private set;}

    [ObservableProperty]
    [property: SavedSetting("AppSettings_RecalibrateAddress", "/avatar/parameters/etvr_recalibrate")]
    private string _recalibrateAddress;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_RecenterAddress", "/avatar/parameters/etvr_recenter")]
    private string _recenterAddress;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_UseOSCQuery", false)]
    private bool _useOscQuery;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_OSCPrefix", "")]
    private string _oscPrefix;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_UseGPU", true)]
    private bool _useGPU;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_CheckForUpdates", false)]
    private bool _checkForUpdates;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_LogLevel", "Debug")]
    private string _logLevel;

    public List<string> LowestLogLevel { get; } =
    [
        "Debug",
        "Information",
        "Warning",
        "Error"
    ];

    [ObservableProperty] private bool _onboardingEnabled;

    private ProcessingLoopService _processingLoopService;
    
    public AppSettingsViewModel()
    {
        OscTarget = Ioc.Default.GetService<IOscTarget>()!;
        GithubService = Ioc.Default.GetService<GithubService>()!;
        SettingsService = Ioc.Default.GetService<ILocalSettingsService>()!;
        _processingLoopService = Ioc.Default.GetService<ProcessingLoopService>()!;
        SettingsService.Load(this);
        if (OscTarget.OutPort == 0)
        {
            const int Port = 8888;
            OscTarget.OutPort = Port;
            Task.Run(async () => await SettingsService.SaveSettingAsync("OSCOutPort", Port));
        }

        ParameterSenderService = Ioc.Default.GetService<ParameterSenderService>()!;

        OnboardingEnabled = Utils.IsSupportedDesktopOS;

        PropertyChanged += (_, _) =>
        {
            SettingsService.Save(this);
        };
    }

    partial void OnUseGPUChanged(bool value)
    {
        Task.Run(async () =>
        {
            await SettingsService.SaveSettingAsync("AppSettings_UseGPU", value);
            await _processingLoopService.SetupFaceInference();
            await _processingLoopService.SetupEyeInference();
        });
    }
}
