﻿using System;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Baballonia.Contracts;
using Baballonia.Services.Inference;
using Baballonia.Services.Inference.Filters;
using CommunityToolkit.Mvvm.DependencyInjection;
using HarfBuzzSharp;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Baballonia.Services;

public class ProcessingLoopService : IDisposable
{
    public record struct Expressions(float[]? FaceExpression, float[]? EyeExpression);

    public event Action<Expressions> ExpressionChangeEvent;

    public readonly FaceProcessingPipeline FaceProcessingPipeline = new();
    public readonly EyeProcessingPipeline EyesProcessingPipeline = new();

    private readonly ILocalSettingsService _localSettingsService;

    public event Action<Exception> PipelineExceptionEvent;

    private readonly DispatcherTimer _drawTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(10)
    };

    private readonly ILogger<ProcessingLoopService> _logger;

    public ProcessingLoopService(
        ILogger<ProcessingLoopService> logger,
        ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
        _logger = logger;

        FaceProcessingPipeline.ImageConverter = new MatToFloatTensorConverter();
        FaceProcessingPipeline.ImageTransformer = new ImageTransformer();
        EyesProcessingPipeline.ImageConverter = new MatToFloatTensorConverter();
        var dualTransformer = new DualImageTransformer();
        dualTransformer.LeftTransformer.TargetSize = new Size(128, 128);
        dualTransformer.RightTransformer.TargetSize = new Size(128, 128);
        EyesProcessingPipeline.ImageTransformer = dualTransformer;

        _ = SetupFaceInference();
        _ = SetupEyeInference();

        _drawTimer.Tick += TimerEvent;
        _drawTimer.Start();
    }



    public async Task SetupEyeInference()
    {
        var eyeModel = await _localSettingsService.ReadSettingAsync<string>("EyeHome_EyeModel", "eyeModel.onnx");
        var useGpu = await _localSettingsService.ReadSettingAsync<bool>("AppSettings_UseGPU", false);

        await Task.Run(() =>
        {
            var l = Ioc.Default.GetService<ILogger<DefaultInferenceRunner>>()!;
            var eyeInference = new DefaultInferenceRunner(l);
            eyeInference.Setup(eyeModel, useGpu);
            Dispatcher.UIThread.Post(() => { EyesProcessingPipeline.InferenceService = eyeInference; });
        });
    }

    public async Task SetupFaceInference()
    {
        var useGpu = await _localSettingsService.ReadSettingAsync<bool>("AppSettings_UseGPU", false);

        await Task.Run(() =>
        {
            var l = Ioc.Default.GetService<ILogger<DefaultInferenceRunner>>()!;
            var faceInference = new DefaultInferenceRunner(l);
            faceInference.Setup("faceModel.onnx", useGpu);

            Dispatcher.UIThread.Post(() => { FaceProcessingPipeline.InferenceService = faceInference; });
        });
    }

    private void TimerEvent(object? s, EventArgs e)
    {
        var expressions = new Expressions();

        try
        {
            var faceExpression = FaceProcessingPipeline.RunUpdate();
            if (faceExpression != null)
                expressions.FaceExpression = faceExpression;
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected exception in Face Tracking pipeline, stopping... : {}", ex);
            FaceProcessingPipeline.VideoSource?.Dispose();
            FaceProcessingPipeline.VideoSource = null;
            PipelineExceptionEvent?.Invoke(ex);
        }

        try
        {
            var eyeExpression = EyesProcessingPipeline.RunUpdate();
            if (eyeExpression != null)
                expressions.EyeExpression = eyeExpression;
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected exception in Eye Tracking pipeline, stopping... : {}", ex);
            EyesProcessingPipeline.VideoSource?.Dispose();
            EyesProcessingPipeline.VideoSource = null;
            PipelineExceptionEvent?.Invoke(ex);
        }

        if (expressions.FaceExpression != null || expressions.EyeExpression != null)
            ExpressionChangeEvent?.Invoke(expressions);
    }

    public void Start()
    {
        _drawTimer.Start();
    }

    public void Pause()
    {
        _drawTimer.Stop();
    }

    public void Dispose()
    {
        _drawTimer.Stop();
        FaceProcessingPipeline.VideoSource?.Dispose();
        EyesProcessingPipeline.VideoSource?.Dispose();
    }
}
