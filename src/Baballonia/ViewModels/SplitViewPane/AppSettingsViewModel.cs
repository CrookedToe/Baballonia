using System;
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
    [property: SavedSetting("AppSettings_RecalibrateAddress", "/avatar/parameters/etvr_recenter")]
    private string _recenterAddress;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_UseOSCQuery", false)]
    private bool _useOscQuery;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_OSCPrefix", "")]
    private string _oscPrefix;

    // Face tracking filter settings
    [ObservableProperty]
    [property: SavedSetting("AppSettings_FaceOneEuroEnabled", false)]
    private bool _faceOneEuroEnabled;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_FaceOneEuroMinFreqCutoff", 1f)]
    private float _faceOneEuroMinFreqCutoff;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_FaceOneEuroSpeedCutoff", 1f)]
    private float _faceOneEuroSpeedCutoff;

    // Eye tracking filter settings
    [ObservableProperty]
    [property: SavedSetting("AppSettings_EyeOneEuroEnabled", false)]
    private bool _eyeOneEuroEnabled;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_EyeOneEuroMinFreqCutoff", 0.1f)]
    private float _eyeOneEuroMinFreqCutoff;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_EyeOneEuroSpeedCutoff", 0.1f)]
    private float _eyeOneEuroSpeedCutoff;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_UseGPU", true)]
    private bool _useGPU;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_CheckForUpdates", false)]
    private bool _checkForUpdates;

    [ObservableProperty] private bool _onboardingEnabled;

    private ProcessingLoopService _processingLoopService;
    public AppSettingsViewModel()
    {
        // General/Calibration Settings
        OscTarget = Ioc.Default.GetService<IOscTarget>()!;
        GithubService = Ioc.Default.GetService<GithubService>()!;
        SettingsService = Ioc.Default.GetService<ILocalSettingsService>()!;
        _processingLoopService = Ioc.Default.GetService<ProcessingLoopService>()!;
        SettingsService.Load(this);

        // Handle edge case where OSC port is used and the system freaks out
        if (OscTarget.OutPort == 0)
        {
            const int Port = 8888;
            OscTarget.OutPort = Port;
            Task.Run(async () => await SettingsService.SaveSettingAsync("OSCOutPort", Port));
        }

        // Risky Settings
        ParameterSenderService = Ioc.Default.GetService<ParameterSenderService>()!;

        OnboardingEnabled = Utils.IsSupportedDesktopOS;

        PropertyChanged += (_, _) =>
        {
            // Update face tracking filter
            if (!_faceOneEuroEnabled)
            {
                _processingLoopService.FaceProcessingPipeline.Filter = null;
            }
            else
            {
                float[] faceArray = new float[Utils.FaceRawExpressions];
                var faceFilter = new OneEuroFilter(
                    faceArray,
                    minCutoff: _faceOneEuroMinFreqCutoff,
                    beta: _faceOneEuroSpeedCutoff
                );
                _processingLoopService.FaceProcessingPipeline.Filter = faceFilter;
            }

            // Update eye tracking filter
            if (!_eyeOneEuroEnabled)
            {
                _processingLoopService.EyesProcessingPipeline.Filter = null;
            }
            else
            {
                float[] eyeArray = new float[Utils.EyeRawExpressions];
                var eyeFilter = new OneEuroFilter(
                    eyeArray,
                    minCutoff: _eyeOneEuroMinFreqCutoff,
                    beta: _eyeOneEuroSpeedCutoff
                );
                _processingLoopService.EyesProcessingPipeline.Filter = eyeFilter;
            }

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
