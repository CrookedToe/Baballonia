using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Baballonia.Contracts;
using Baballonia.Helpers;
using Baballonia.Services;
using Baballonia.Services.Inference;
using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using HarfBuzzSharp;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class HomePageViewModel : ViewModelBase, IDisposable
{
    // This feels unorthodox but... i kinda like it?
    public partial class CameraControllerModel : ObservableObject
    {
        public string Name;

        public CameraController Controller
        {
            get => _controller;
            set
            {
                _controller = value;
                OverlayRectangle = _controller.CameraSettings.Roi.GetRect();
                FlipHorizontally = _controller.CameraSettings.UseHorizontalFlip;
                FlipVertically = _controller.CameraSettings.UseVerticalFlip;
                Rotation = _controller.CameraSettings.RotationRadians;
                Gamma = _controller.CameraSettings.Gamma;
                IsCropMode = _controller.CropManager.IsCropping;
            }
        }

        [ObservableProperty] private WriteableBitmap _bitmap;

        [ObservableProperty] private bool _hintEnabled;
        [ObservableProperty] private bool _inferEnabled;
        [ObservableProperty] private string? _displayAddress;
        [ObservableProperty] private Rect _overlayRectangle;
        [ObservableProperty] private bool _flipHorizontally = false;
        [ObservableProperty] private bool _flipVertically = false;
        [ObservableProperty] private float _rotation = 0f;
        [ObservableProperty] private float _gamma = 0.5f;
        [ObservableProperty] private bool _isCropMode = false;
        [ObservableProperty] private bool _isCameraRunning = false;
        public ObservableCollection<string> Suggestions { get; set; } = [];

        private readonly ILocalSettingsService _localSettingsService;
        private CameraController _controller;

        public CameraControllerModel(ILocalSettingsService localSettingsService, string name)
        {
            _localSettingsService = localSettingsService;
            Name = name;

            Dispatcher.UIThread.Post(async () =>
            {
                DisplayAddress = await _localSettingsService.ReadSettingAsync<string>("LastOpened" + Name);
            });
        }

        void SaveCameraConfig()
        {
            if (Controller is null) return;

            Dispatcher.UIThread.Post(async () =>
            {
                await _localSettingsService.SaveSettingAsync(Name, Controller.CameraSettings);
            });
        }


        partial void OnDisplayAddressChanged(string? oldValue, string? newValue)
        {
            SaveCameraConfig();
        }

        partial void OnBitmapChanged(WriteableBitmap? value)
        {
            IsCameraRunning = value != null;
        }

        partial void OnOverlayRectangleChanged(Rect value)
        {
            SaveCameraConfig();
        }

        partial void OnDisplayAddressChanged(string value)
        {
            _localSettingsService.SaveSettingAsync("LastOpened" + Name, value);
        }

        partial void OnFlipHorizontallyChanged(bool value)
        {
            Controller.CameraSettings.UseHorizontalFlip = value;
            SaveCameraConfig();
        }

        partial void OnFlipVerticallyChanged(bool value)
        {
            Controller.CameraSettings.UseVerticalFlip = value;
            SaveCameraConfig();
        }

        partial void OnRotationChanged(float value)
        {
            Controller.CameraSettings.RotationRadians = value;
            SaveCameraConfig();
        }

        partial void OnGammaChanged(float value)
        {
            Controller.CameraSettings.Gamma = value;
            SaveCameraConfig();
        }

        partial void OnIsCropModeChanged(bool value)
        {
            if (value)
                Controller.SetCroppingMode();
            else
                Controller.SetTrackingMode();

            SaveCameraConfig();
        }

        public void SelectWholeFrame()
        {
            OverlayRectangle = Controller.SelectEntireFrame();
            SaveCameraConfig();
        }
    }

    private static bool _hasPerformedFirstTimeSetup = false;

    public IOscTarget OscTarget { get; }
    private OscRecvService OscRecvService { get; }
    private OscSendService OscSendService { get; }
    private ILocalSettingsService LocalSettingsService { get; }

    private int _messagesRecvd;
    [ObservableProperty] private string _messagesInPerSecCount;

    private int _messagesSent;
    [ObservableProperty] private string _messagesOutPerSecCount;

    [ObservableProperty]
    [property: SavedSetting("EyeHome_EyeModel", "eyeModel.onnx")]
    private string _eyeModel;

    [ObservableProperty] private bool _shouldShowEyeCalibration;
    [ObservableProperty] private string _selectedCalibrationText;

    [ObservableProperty] private CameraControllerModel _leftCamera;
    [ObservableProperty] private CameraControllerModel _rightCamera;
    [ObservableProperty] private CameraControllerModel _faceCamera;

    private readonly DispatcherTimer _msgCounterTimer;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ProcessingLoopService _processingLoopService;

    public HomePageViewModel()
    {
        OscTarget = Ioc.Default.GetService<IOscTarget>()!;
        OscRecvService = Ioc.Default.GetService<OscRecvService>()!;
        OscSendService = Ioc.Default.GetService<OscSendService>()!;
        LocalSettingsService = Ioc.Default.GetService<ILocalSettingsService>()!;
        _localSettingsService = Ioc.Default.GetRequiredService<ILocalSettingsService>()!;
        _processingLoopService = Ioc.Default.GetService<ProcessingLoopService>()!;
        LocalSettingsService.Load(this);

        MessagesInPerSecCount = "0";
        MessagesOutPerSecCount = "0";
        OscSendService.OnMessagesDispatched += MessageDispatched;

        _msgCounterTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _msgCounterTimer.Tick += (_, _) =>
        {
            MessagesInPerSecCount = _messagesRecvd.ToString();
            _messagesRecvd = 0;

            MessagesOutPerSecCount = _messagesSent.ToString();
            _messagesSent = 0;
        };
        _msgCounterTimer.Start();

        LeftCamera = new CameraControllerModel(_localSettingsService, "LeftCamera");
        RightCamera = new CameraControllerModel(_localSettingsService, "RightCamera");
        FaceCamera = new CameraControllerModel(_localSettingsService, "FaceCamera");

        Dispatcher.UIThread.Post(async () =>
        {
            await SetupCameraControllers();
        });
    }

    private async Task SetupCameraControllers()
    {
        Task.Run(async () =>
        {
            App.DeviceEnumerator.Cameras = await App.DeviceEnumerator.UpdateCameras();
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var camerasKey in App.DeviceEnumerator.Cameras.Keys)
                {
                    LeftCamera.Suggestions.Add(camerasKey);
                    RightCamera.Suggestions.Add(camerasKey);
                    FaceCamera.Suggestions.Add(camerasKey);
                }
            });
        });

        _processingLoopService.BitmapUpdateEvent += BitmapUpdateHandler;

        if (!_hasPerformedFirstTimeSetup)
        {
            if (!string.IsNullOrEmpty(LeftCamera.DisplayAddress) && !string.IsNullOrEmpty(RightCamera.DisplayAddress))
            {
                // This will start the left and right cameras
                await CameraStart(LeftCamera);
            }

            if (!string.IsNullOrEmpty(FaceCamera.DisplayAddress))
            {
                await CameraStart(FaceCamera);
            }

            _hasPerformedFirstTimeSetup = true;
            return;
        }

        await SetupCameraSettings();
    }

    private void BitmapUpdateHandler(ProcessingLoopService.Bitmaps bitmaps)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // a hack to force the UI refresh
            LeftCamera.Bitmap = null!;
            LeftCamera.Bitmap = bitmaps.LeftBitmap!;

            RightCamera.Bitmap = null!;
            RightCamera.Bitmap = bitmaps.RightBitmap!;

            FaceCamera.Bitmap = null!;
            FaceCamera.Bitmap = bitmaps.FaceBitmap!;
        });
    }

    private async Task SetupCameraSettings()
    {
        // Create camera URL dictionary for eye inference service
        var cameraUrls = new Dictionary<Camera, string>();
        if (!string.IsNullOrEmpty(_leftCamera.DisplayAddress))
            cameraUrls[Camera.Left] = _leftCamera.DisplayAddress;
        if (!string.IsNullOrEmpty(_rightCamera.DisplayAddress))
            cameraUrls[Camera.Right] = _rightCamera.DisplayAddress;

        await _processingLoopService.SetupCameraSettings(cameraUrls);

        LeftCamera.Controller = _processingLoopService.LeftCameraController;
        RightCamera.Controller = _processingLoopService.RightCameraController;
        FaceCamera.Controller = _processingLoopService.FaceCameraController;
    }

    private void SaveCameraSettings()
    {
        _localSettingsService.SaveSettingAsync("LeftCamera",
            _processingLoopService.LeftCameraController.CameraSettings);
        _localSettingsService.SaveSettingAsync("RightCamera",
            _processingLoopService.RightCameraController.CameraSettings);
        _localSettingsService.SaveSettingAsync("FaceCamera",
            _processingLoopService.FaceCameraController.CameraSettings);
    }

    [RelayCommand]
    private async Task CameraStart(CameraControllerModel model)
    {
        await SetupCameraSettings();
        string camera = model.DisplayAddress;
        if (string.IsNullOrEmpty(camera)) return;

        if (App.DeviceEnumerator.Cameras != null)
        {
            if (App.DeviceEnumerator.Cameras.TryGetValue(camera, out var mappedAddress))
            {
                camera = mappedAddress;
            }
        }
        else
        {
            return;
        }

        model.Controller.StartCamera(camera);

        if (model.Name != "FaceCamera")
        {
            if (_processingLoopService.EyeInferenceService is DualCameraEyeInferenceService)
            {
                switch (model.Controller.CameraSettings.Camera)
                {
                    case Camera.Left:
                    {
                        if (LeftCamera.DisplayAddress != RightCamera.DisplayAddress)
                        {
                            if (!string.IsNullOrEmpty(RightCamera.DisplayAddress))
                            {
                                if (App.DeviceEnumerator.Cameras!.TryGetValue(RightCamera.DisplayAddress, out var mappedAddress))
                                {
                                    _processingLoopService.RightCameraController.StartCamera(mappedAddress);
                                }
                            }
                        }

                        break;
                    }
                    case Camera.Right:
                    {
                        if (LeftCamera.DisplayAddress != RightCamera.DisplayAddress)
                        {
                            if (!string.IsNullOrEmpty(LeftCamera.DisplayAddress))
                            {
                                if (App.DeviceEnumerator.Cameras!.TryGetValue(LeftCamera.DisplayAddress, out var mappedAddress))
                                {
                                    _processingLoopService.LeftCameraController.StartCamera(mappedAddress);
                                }
                            }
                        }

                        break;
                    }
                }
            }
        }


        SaveCameraSettings();
    }

    [RelayCommand]
    private void CameraStop(CameraControllerModel model)
    {
        model.Controller.StopCamera();

        if (_processingLoopService.EyeInferenceService is not DualCameraEyeInferenceService) return;

        switch (model.Controller.CameraSettings.Camera)
        {
            case Camera.Left:
            {
                if (LeftCamera.DisplayAddress != RightCamera.DisplayAddress)
                {
                    _processingLoopService.RightCameraController.StopCamera();
                }

                break;
            }
            case Camera.Right:
            {
                if (LeftCamera.DisplayAddress != RightCamera.DisplayAddress)
                {
                    _processingLoopService.LeftCameraController.StopCamera();
                }

                break;
            }
        }
    }

    [RelayCommand]
    private void SelectWholeFrame(CameraControllerModel model)
    {
        model.SelectWholeFrame();
    }

    [RelayCommand]
    private async Task RequestVRCalibration()
    {
        await App.Overlay.EyeTrackingCalibrationRequested(CalibrationRoutine.QuickCalibration,
            _processingLoopService.LeftCameraController, _processingLoopService.RightCameraController,
            _localSettingsService, _processingLoopService.EyeInferenceService);
        await _localSettingsService.SaveSettingAsync("EyeHome_EyeModel", "tuned_temporal_eye_tracking.onnx");

        // This will restart the right camera, as well as the left
        CameraStop(LeftCamera);
        CameraStart(LeftCamera);
    }

    private void MessageDispatched(int msgCount) => _messagesSent += msgCount;

    public void Dispose()
    {
        CleanupResources();
    }

    private bool _disposed = false;

    private void CleanupResources()
    {
        if (_disposed) return;

        OscSendService.OnMessagesDispatched -= MessageDispatched;
        _msgCounterTimer.Stop();

        _processingLoopService.BitmapUpdateEvent -= BitmapUpdateHandler;
    }
}
