using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Models;
using Baballonia.Services.Inference.Platforms;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;

namespace Baballonia.Contracts;

public interface IInferenceService
{
    public (PlatformSettings, PlatformConnector)[] PlatformConnectors { get; }
    public bool GetExpressionData(CameraSettings cameraSettings, out float[] arKitExpressions);
    public bool GetExpressionData(CameraSettings leftCameraSettings, CameraSettings rightCameraSettings, out float[] arKitExpressions);

    public bool GetRawImage(CameraSettings cameraSettings, ColorType color, out Mat image);

    public bool GetImage(CameraSettings cameraSettings, out Mat? image);

    public Task ConfigurePlatformConnectors(Camera camera, string cameraIndex);

    public Task SetupInference(Camera camera, string cameraAddress);
    public SessionOptions SetupSessionOptions();
    public Task ConfigurePlatformSpecificGpu(SessionOptions sessionOptions);

    public void Shutdown(Camera camera);
    public void Shutdown();
}
