using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Baballonia.Contracts;
using Baballonia.Services.Inference;
using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Models;

namespace Baballonia.Services;

public class ProcessingLoopService : IDisposable
{
    public record struct Bitmaps(WriteableBitmap? FaceBitmap, WriteableBitmap? LeftBitmap, WriteableBitmap? RightBitmap);

    public record struct Expressions(float[]? FaceExpression, float[]? EyeExpression);

    public event Action<Bitmaps> BitmapUpdateEvent;
    public event Action<Expressions> ExpressionUpdateEvent;
    public CameraController LeftCameraController { get; set; }
    public CameraController RightCameraController { get; set; }
    public CameraController FaceCameraController { get; set; }

    public IInferenceService EyeInferenceService { get; private set; }

    private readonly ILocalSettingsService _localSettingsService;
    private readonly IFaceInferenceService _faceInferenceService;
    private IServiceProvider _serviceProvider;

    private readonly DispatcherTimer _drawTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(10)
    };

    public ProcessingLoopService(
        ILocalSettingsService localSettingsService,
        IFaceInferenceService faceInferenceService,
        IServiceProvider serviceProvider)
    {
        _localSettingsService = localSettingsService;
        _faceInferenceService = faceInferenceService;
        _serviceProvider = serviceProvider;

        _drawTimer.Tick += TimerEvent;
    }

    public async Task SetupCameraSettings(Dictionary<Camera, string>? cameraUrls)
    {
        var leftSettings = await _localSettingsService.ReadSettingAsync<CameraSettings>("LeftCamera",
            new CameraSettings { Camera = Camera.Left });
        var rightSettings = await _localSettingsService.ReadSettingAsync<CameraSettings>("RightCamera",
            new CameraSettings { Camera = Camera.Right });
        var faceSettings = await _localSettingsService.ReadSettingAsync<CameraSettings>("FaceCamera",
            new CameraSettings { Camera = Camera.Face });

        // Create the appropriate eye inference service based on camera configuration
        EyeInferenceService =
            EyeInferenceServiceFactory.Create(_serviceProvider, cameraUrls, leftSettings, rightSettings);

        LeftCameraController = new CameraController(
            EyeInferenceService,
            Camera.Left,
            leftSettings
        );

        RightCameraController = new CameraController(
            EyeInferenceService,
            Camera.Right,
            rightSettings
        );

        FaceCameraController = new CameraController(
            _faceInferenceService,
            Camera.Face,
            faceSettings
        );

        _drawTimer.Start();
    }

    private async void TimerEvent(object? s, EventArgs e)
    {
        try
        {
            Bitmaps bitmaps = new Bitmaps();
            Expressions expressions = new Expressions();

            var leftSettings = await _localSettingsService.ReadSettingAsync<CameraSettings>("LeftCamera",
                new CameraSettings { Camera = Camera.Left });
            var rightSettings = await _localSettingsService.ReadSettingAsync<CameraSettings>("RightCamera",
                new CameraSettings { Camera = Camera.Right });
            var faceSettings = await _localSettingsService.ReadSettingAsync<CameraSettings>("FaceCamera",
                new CameraSettings { Camera = Camera.Face });

            var isDualCamera = EyeInferenceService is DualCameraEyeInferenceService;
            bitmaps.LeftBitmap = await LeftCameraController.UpdateImage(leftSettings, rightSettings, faceSettings, isDualCamera);
            bitmaps.RightBitmap = await RightCameraController.UpdateImage(leftSettings, rightSettings, faceSettings, isDualCamera);
            bitmaps.FaceBitmap = await FaceCameraController.UpdateImage(leftSettings, rightSettings, faceSettings, false);

            expressions.FaceExpression = CameraController.FaceExpressions;
            expressions.EyeExpression = CameraController.EyeExpressions;

            ExpressionUpdateEvent?.Invoke(expressions);
            BitmapUpdateEvent?.Invoke(bitmaps);
        }
        catch (Exception ex)
        {
            _drawTimer.Stop();
        }
    }

    public void Dispose()
    {
        _drawTimer.Stop();
    }
}
