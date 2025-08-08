using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using Baballonia.Contracts;
using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Filters;
using Baballonia.Services.Inference.Models;
using Baballonia.Services.Inference.Platforms;

namespace Baballonia.Services;

public class EyeInferenceService(ILogger<InferenceService> logger, ILocalSettingsService settingsService)
    : InferenceService(logger, settingsService), IEyeInferenceService
{
    public override (PlatformSettings, PlatformConnector)[] PlatformConnectors { get; }
        = new (PlatformSettings settings, PlatformConnector connector)[3];

    private int[] _combinedDimensions;
    private DenseTensor<float> _combinedTensor;
    private byte[] _matBytes = [];

    // Queue for each camera to hold incoming frames
    private readonly ConcurrentQueue<FrameData> _frameQueues = new();
    private readonly ILogger<InferenceService> _logger = logger;
    private readonly ILocalSettingsService _settingsService = settingsService;

    // Tracks if we're using a single stereo camera for both eyes
    private bool _usingStereoCamera = false;
    private Camera _stereoCamera = Camera.Left; // Which camera is providing the stereo feed

    // Minimum number of frames required before processing
    private const int ExpectedRawExpressions = 6;
    private const int FramesForInference = 4;

    /// <summary>
    /// Loads/reloads the ONNX model for a specified camera
    /// </summary>
    public override void SetupInference(Camera camera, string cameraAddress)
    {
        Task.Run(async () =>
        {
            _logger.LogInformation("Starting Eye Inference Service...");

            // Check if we're using a stereo camera (same address for both eyes)
            if (camera == Camera.Left &&
                PlatformConnectors[(int)Camera.Right].Item2 != null &&
                PlatformConnectors[(int)Camera.Right].Item2.Capture != null &&
                PlatformConnectors[(int)Camera.Right].Item2.Capture.Url == cameraAddress)
            {
                _usingStereoCamera = true;
                _stereoCamera = Camera.Left;
                _logger.LogInformation("Stereo camera detected. Using single camera feed for both eyes.");
                return; // Skip setup for right eye as it's the same camera
            }

            _usingStereoCamera = false;
            SessionOptions sessionOptions = SetupSessionOptions();
            await ConfigurePlatformSpecificGpu(sessionOptions);

            var minCutoff = await _settingsService.ReadSettingAsync<float>("AppSettings_OneEuroMinFreqCutoff");
            if (minCutoff == 0f) minCutoff = 1f;
            var speedCoeff = await _settingsService.ReadSettingAsync<float>("AppSettings_OneEuroSpeedCutoff");
            if (speedCoeff == 0f) speedCoeff = 1f;
            var eyeModel = await _settingsService.ReadSettingAsync<string>("EyeHome_EyeModel") ?? "eyeModel.onnx";

            float[] noisy_point = new float[ExpectedRawExpressions];
            var filter = new OneEuroFilter(
                x0: noisy_point,
                minCutoff: minCutoff,
                beta: speedCoeff
            );

            var session = new InferenceSession(Path.Combine(AppContext.BaseDirectory, eyeModel), sessionOptions);
            var inputName = session.InputMetadata.Keys.First();
            var dimensions = session.InputMetadata.Values.First().Dimensions;
            var inputSize = new Size(dimensions[2], dimensions[3]);
            const int magicAiNumberSize = 4;

            DenseTensor<float> tensor = new DenseTensor<float>([1, magicAiNumberSize, dimensions[2], dimensions[3]]);

            // This is set up twice but I do not care
            _combinedDimensions = [1, magicAiNumberSize * 2, dimensions[2], dimensions[3]];
            _combinedTensor = new DenseTensor<float>(_combinedDimensions);

            var platformSettings = new PlatformSettings(inputSize, session, tensor, filter, 0f, inputName, eyeModel);
            PlatformConnectors[(int)camera] = (platformSettings, null)!;
            ConfigurePlatformConnectors(camera, cameraAddress);

            _logger.LogInformation("Eye Inference started!");
        });
    }

    /// <summary>
    /// Captures a frame and adds it to the queue for later processing
    /// </summary>
    /// <param name="cameraSettings"></param>
    /// <returns>True if frame was successfully captured and added to queue</returns>
    private bool CaptureFrame(CameraSettings cameraSettings)
    {
        // Check if we're using a stereo camera
        if (!_usingStereoCamera)
        {
            // Original dual-camera check
            if (PlatformConnectors[(int)Camera.Left].Item2 is null ||
                PlatformConnectors[(int)Camera.Left].Item2.Capture is null ||
                !PlatformConnectors[(int)Camera.Left].Item2.Capture!.IsReady ||
                PlatformConnectors[(int)Camera.Right].Item2 is null ||
                PlatformConnectors[(int)Camera.Right].Item2.Capture is null ||
                !PlatformConnectors[(int)Camera.Right].Item2.Capture!.IsReady)
            {
                return false;
            }
        }
        else
        {
            // Only check the stereo camera
            if (PlatformConnectors[(int)_stereoCamera].Item2 is null ||
                PlatformConnectors[(int)_stereoCamera].Item2.Capture is null ||
                !PlatformConnectors[(int)_stereoCamera].Item2.Capture!.IsReady)
            {
                return false;
            }
        }

        Mat matCombined;

        if (!_usingStereoCamera)
        {
            // Original dual-camera processing
            Mat matLeft = new Mat<byte>(PlatformConnectors[(int)Camera.Left].Item1.InputSize.Height,
                PlatformConnectors[(int)Camera.Left].Item1.InputSize.Width);
            PlatformConnectors[(int)Camera.Left].Item2.TransformRawImage(matLeft, cameraSettings);
            if (matLeft.Empty()) return false;
            Mat histMatLeft = new();
            Cv2.EqualizeHist(matLeft, histMatLeft);

            Mat matRight = new Mat<byte>(PlatformConnectors[(int)Camera.Right].Item1.InputSize.Height,
                PlatformConnectors[(int)Camera.Right].Item1.InputSize.Width);
            PlatformConnectors[(int)Camera.Right].Item2.TransformRawImage(matRight, cameraSettings);
            if (matRight.Empty()) return false;
            Mat histMatRight = new();
            Cv2.EqualizeHist(matRight, histMatRight);

            matCombined = new Mat();
            Cv2.Merge([histMatLeft, histMatRight], matCombined);
        }
        else
        {
            // Stereo camera processing - the camera feed already contains both eyes
            Mat stereoMat = new Mat<byte>(PlatformConnectors[(int)_stereoCamera].Item1.InputSize.Height,
                PlatformConnectors[(int)_stereoCamera].Item1.InputSize.Width);
            PlatformConnectors[(int)_stereoCamera].Item2.TransformRawImage(stereoMat, cameraSettings);
            if (stereoMat.Empty()) return false;

            // The stereo mat should already be in the correct format (CV_8UC2)
            matCombined = stereoMat.Clone();
        }

        var frameDataCombined = new FrameData
        {
            CameraSettings = cameraSettings,
            Timestamp = sw.Elapsed.TotalSeconds,
            Mat = matCombined,
        };

        if (_frameQueues.Count > FramesForInference)
        {
            _frameQueues.TryDequeue(out _);
        }

        _frameQueues.Enqueue(frameDataCombined);

        return true;
    }

    /// <summary>
    /// Poll expression data, frames
    /// </summary>
    /// <param name="cameraSettings"></param>
    /// <param name="arKitExpressions"></param>
    /// <returns></returns>
    public override bool GetExpressionData(CameraSettings cameraSettings, out float[] arKitExpressions)
    {
        arKitExpressions = null!;

        if (PlatformConnectors[(int)Camera.Left].Item2 is null || PlatformConnectors[(int)Camera.Left].Item2.Capture is null || !PlatformConnectors[(int)Camera.Left].Item2.Capture!.IsReady ||
            PlatformConnectors[(int)Camera.Right].Item2 is null || PlatformConnectors[(int)Camera.Right].Item2.Capture is null || !PlatformConnectors[(int)Camera.Right].Item2.Capture!.IsReady)
        {
            return false;
        }
        var index = (int)cameraSettings.Camera;
        var platformSettings = PlatformConnectors[index].Item1;

        // First capture the current frame
        if (!CaptureFrame(cameraSettings))
        {
            return false;
        }

        // Check if we have enough frames in the queue
        if (_frameQueues.Count < FramesForInference)
        {
            return false;
        }

        // Pop old frames until we have FramesForInference
        while (_frameQueues.Count != FramesForInference)
        {
            _frameQueues.TryDequeue(out _);
        }

        // This will update _combinedTensor
        ConvertMatsArrayToDenseTensor(_frameQueues.Select(fd => fd.Mat).ToArray());

        // Run inference on the dequeued frame's tensor
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(PlatformConnectors[(int)Camera.Left].Item1.InputName, _combinedTensor)
        };

        // Run inference!
        using var results = PlatformConnectors[(int)Camera.Left].Item1.Session!.Run(inputs);
        arKitExpressions = results[0].AsEnumerable<float>().ToArray();
        arKitExpressions = platformSettings.Filter.Filter(arKitExpressions);

        var MUL_V = 2.0f;
        var MUL_Y = 2.0f;

        var left_pitch = arKitExpressions[0] * MUL_Y - (MUL_Y/2);
        var left_yaw = arKitExpressions[1] * MUL_V - (MUL_V/2);
        var left_lid = 1 - arKitExpressions[2];

        var right_pitch = arKitExpressions[3] * MUL_Y - (MUL_Y/2);
        var right_yaw = arKitExpressions[4] * MUL_V - (MUL_V/2);
        var right_lid = 1 - arKitExpressions[5];

        var eye_Y = ((left_pitch * left_lid) + (right_pitch * right_lid)) / (left_lid + right_lid);

        var left_eye_yaw_corrected = (right_yaw * (1 - left_lid)) + (left_yaw * left_lid);
        var right_eye_yaw_corrected = (left_yaw * (1 - right_lid)) + (right_yaw * right_lid);

        // [left pitch, left yaw, left lid...
        float[] convertedExpressions = new float[ExpectedRawExpressions];

        // swap eyes at this point
        convertedExpressions[0] /*left pitch*/  = right_eye_yaw_corrected;
        convertedExpressions[1] /*left yaw*/    = eye_Y;
        convertedExpressions[2] /*left lid*/    = right_lid;

        convertedExpressions[3] /*right pitch*/ = left_eye_yaw_corrected;
        convertedExpressions[4] /*right yaw*/   = eye_Y;
        convertedExpressions[5] /*right lid*/   = left_lid;

        arKitExpressions = convertedExpressions;

        float time = (float)sw.Elapsed.TotalSeconds;
        var delta = time - PlatformConnectors[(int)Camera.Left].Item1.LastTime;
        PlatformConnectors[(int)Camera.Left].Item1.Ms = delta * 1000f;
        PlatformConnectors[(int)Camera.Right].Item1.Ms = delta * 1000f;

        PlatformConnectors[(int)Camera.Left].Item1.LastTime = time;
        PlatformConnectors[(int)Camera.Right].Item1.LastTime = time;
        return true;
    }

    private void ConvertMatsArrayToDenseTensor(Mat[] mats)
    {
        // Verify the input array has exactly 4 mats
        if (mats.Length != 4)
        {
            throw new ArgumentException($"Expected 4 mats, but got: {mats.Length}");
        }

        // Verify each mat is CV_8UC2
        foreach (var mat in mats)
        {
            if (mat.Type() != MatType.CV_8UC2)
            {
                throw new ArgumentException($"Expected CV_8UC2 mat, but got: {mat.Type()}");
            }
        }

        // Ensure all mats have the same dimensions
        int width = mats[0].Width;
        int height = mats[0].Height;

        foreach (var mat in mats)
        {
            if (mat.Width != width || mat.Height != height)
            {
                throw new ArgumentException("All mats must have the same dimensions");
            }
        }

        // Create new tensor with specified dimensions
        // The tensor will have shape [batchSize, channels, height, width]
        // where channels = 2 channels per mat * 4 mats = 8 total channels

        int batchSize = _combinedDimensions[0];
        int tensorChannels = _combinedDimensions[1];

        // We expect tensorChannels to be 8 (2 channels * 4 mats)
        if (tensorChannels != 8)
        {
            throw new ArgumentException($"Tensor channels dimension should be 8 (2 channels * 4 mats), but got: {tensorChannels}");
        }

        // Process each mat
        for (int matIndex = 0; matIndex < mats.Length; matIndex++)
        {
            Mat continuousMat = mats[matIndex].IsContinuous() ? mats[matIndex] : mats[matIndex].Clone();

            // Get the raw data bytes from the Mat
            var size = continuousMat.Total() * continuousMat.ElemSize();
            if (_matBytes.Length != size)
                Array.Resize(ref _matBytes, (int)size);

            Marshal.Copy(continuousMat.Data, _matBytes, 0, _matBytes.Length);

            // Process each pixel's two channels
            for (int b = 0; b < batchSize; b++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // Get the byte offset for the current pixel in the Mat
                        int matOffset = (y * width + x) * 2; // 2 channels per pixel

                        float normalizedValue0 = _matBytes[matOffset] / 255.0f;
                        float normalizedValue1 = _matBytes[matOffset + 1] / 255.0f;

                        _combinedTensor[b, matIndex * 2, y, x] = normalizedValue1;
                        _combinedTensor[b, matIndex * 2 + 1, y, x] = normalizedValue0;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets the pre-transform lip image for this frame
    /// This image will be (dimensions.width)px * (dimensions.height)px in provided ColorType
    /// </summary>
    /// <param name="color"></param>
    /// <param name="image"></param>
    /// <param name="cameraSettings"></param>
    /// <returns></returns>
    public override bool GetRawImage(CameraSettings cameraSettings, ColorType color, out Mat image)
    {
        var index = (int)cameraSettings.Camera;
        var platformConnector = PlatformConnectors[index].Item2;
        image = new Mat();

        if (platformConnector is null)
            return false;

        if (platformConnector.Capture is null)
            return false;

        if (!platformConnector.Capture.IsReady)
            return false;

        if (platformConnector.Capture.RawMat is null)
            return false;

        if (color == (platformConnector.Capture!.RawMat.Channels() == 1 ? ColorType.Gray8 : ColorType.Bgr24))
        {
            image = platformConnector.Capture!.RawMat;
        }
        else
        {
            var convertedMat = new Mat();
            Cv2.CvtColor(platformConnector.Capture!.RawMat, convertedMat, (platformConnector.Capture!.RawMat.Channels() == 1) ? color switch
            {
                ColorType.Bgr24 => ColorConversionCodes.GRAY2BGR,
                ColorType.Rgb24 => ColorConversionCodes.GRAY2RGB,
                ColorType.Rgba32 => ColorConversionCodes.GRAY2RGBA,
            } : color switch
            {
                ColorType.Gray8 => ColorConversionCodes.BGR2GRAY,
                ColorType.Rgb24 => ColorConversionCodes.BGR2RGB,
                ColorType.Rgba32 => ColorConversionCodes.BGR2RGBA,
            });
            image = convertedMat;
        }

        return true;
    }

    /// <summary>
    /// Gets the post-transform lip image for this frame
    /// This image will be 256*256px, single-channel
    /// </summary>
    /// <param name="cameraSettings"></param>
    /// <param name="image"></param>
    /// <returns></returns>
    public override bool GetImage(CameraSettings cameraSettings, out Mat? image)
    {
        image = null;
        var platformSettings = PlatformConnectors[(int)cameraSettings.Camera].Item1;
        var platformConnector = PlatformConnectors[(int)cameraSettings.Camera].Item2;
        if (platformConnector is null) return false;

        var imageMat = new Mat<byte>(platformSettings.InputSize.Height, platformSettings.InputSize.Width);

        if (platformConnector.TransformRawImage(imageMat, cameraSettings) != true)
        {
            imageMat.Dispose();
            return false;
        }

        image = imageMat;
        return true;
    }

    /// <summary>
    /// Class to store frame data in the queue
    /// </summary>
    private class FrameData
    {
        public CameraSettings CameraSettings { get; set; }
        public Mat Mat { get; init; }
        public double Timestamp { get; set; }
    }
}
