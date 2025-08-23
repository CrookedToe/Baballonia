using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Filters;
using Baballonia.Services.Inference.Models;
using Baballonia.Services.Inference.Platforms;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace Baballonia.Services;

/// <summary>
/// Base class for eye inference services that handles common functionality
/// </summary>
public abstract class BaseEyeInferenceService(ILogger<InferenceService> logger, ILocalSettingsService settingsService) : InferenceService(logger, settingsService)
{
    protected readonly Stopwatch sw = Stopwatch.StartNew();

    protected const int ExpectedRawExpressions = 6;
    protected const int FramesForInference = 4;

    protected int[] _combinedDimensions;
    protected DenseTensor<float> _combinedTensor;
    protected byte[] _matBytes = [];
    protected readonly ConcurrentQueue<FrameData> _frameQueues = new();

    public abstract EyeInferenceType Type { get; }
    public abstract IReadOnlyDictionary<Camera, string> CameraUrls { get; }

    protected abstract Task InitializeModel();

    protected bool CaptureFrame(CameraSettings cameraSettings, Mat leftEyeMat, Mat rightEyeMat)
    {
        // Process left eye
        var platformConnectorLeft = PlatformConnectors[(int)Camera.Left].Item2;
        platformConnectorLeft.TransformRawImage(leftEyeMat, cameraSettings);
        if (leftEyeMat.Empty()) return false;

        // Process right eye
        var platformConnectorRight = PlatformConnectors[(int)Camera.Right].Item2;
        platformConnectorRight.TransformRawImage(rightEyeMat, cameraSettings);
        if (rightEyeMat.Empty()) return false;

        /*using var testMat = new Mat<byte>(
            PlatformConnectors[(int)Camera.Right].Item1.InputSize.Height,
            PlatformConnectors[(int)Camera.Right].Item1.InputSize.Width);
        Cv2.Compare(leftEyeMat, rightEyeMat, testMat, CmpType.EQ);
        Console.WriteLine(Cv2.CountNonZero(testMat));*/

        // Combine the eye mats into a single mat
        using var histMatLeft = new Mat();
        using var histMatRight = new Mat();
        Cv2.EqualizeHist(leftEyeMat, histMatLeft);
        Cv2.EqualizeHist(rightEyeMat, histMatRight);

        var matCombined = new Mat();
        Cv2.Merge([histMatLeft, histMatRight], matCombined);

        var frameDataCombined = new FrameData
        {
            Timestamp = sw.Elapsed.TotalSeconds,
            Mat = matCombined,
        };

        // Maintain frame queue size
        if (_frameQueues.Count > FramesForInference)
        {
            _frameQueues.TryDequeue(out _);
        }

        _frameQueues.Enqueue(frameDataCombined);
        return true;
    }

    protected bool CaptureFrame(CameraSettings leftSetting, CameraSettings rightSettings, Mat leftEyeMat, Mat rightEyeMat)
    {
        // Process left eye
        var platformConnectorLeft = PlatformConnectors[(int)Camera.Left].Item2;
        platformConnectorLeft.TransformRawImage(leftEyeMat, leftSetting);
        if (leftEyeMat.Empty()) return false;

        // Process right eye
        var platformConnectorRight = PlatformConnectors[(int)Camera.Right].Item2;
        platformConnectorRight.TransformRawImage(rightEyeMat, rightSettings);
        if (rightEyeMat.Empty()) return false;

        /*using var testMat = new Mat<byte>(
            PlatformConnectors[(int)Camera.Right].Item1.InputSize.Height,
            PlatformConnectors[(int)Camera.Right].Item1.InputSize.Width);
        Cv2.Compare(leftEyeMat, rightEyeMat, testMat, CmpType.EQ);
        Console.WriteLine(Cv2.CountNonZero(testMat));*/

        // Combine the eye mats into a single mat
        using var histMatLeft = new Mat();
        using var histMatRight = new Mat();
        Cv2.EqualizeHist(leftEyeMat, histMatLeft);
        Cv2.EqualizeHist(rightEyeMat, histMatRight);

        var matCombined = new Mat();
        Cv2.Merge([histMatLeft, histMatRight], matCombined);

        var frameDataCombined = new FrameData
        {
            Timestamp = sw.Elapsed.TotalSeconds,
            Mat = matCombined,
        };

        // Maintain frame queue size
        if (_frameQueues.Count > FramesForInference)
        {
            _frameQueues.TryDequeue(out _);
        }

        _frameQueues.Enqueue(frameDataCombined);
        return true;
    }

    protected void ConvertMatsArrayToDenseTensor(Mat[] mats)
    {
        if (mats.Length != FramesForInference)
        {
            throw new ArgumentException($"Expected {FramesForInference} mats, but got: {mats.Length}");
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

        int batchSize = _combinedDimensions[0];
        int tensorChannels = _combinedDimensions[1];

        if (tensorChannels != 8) // 2 channels * 4 frames
        {
            throw new ArgumentException($"Tensor channels dimension should be 8 (2 channels * 4 frames), but got: {tensorChannels}");
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
                        int matOffset = (y * width + x) * 2; // 2 channels per pixel
                        float normalizedValue0 = _matBytes[matOffset] / 255.0f;
                        float normalizedValue1 = _matBytes[matOffset + 1] / 255.0f;

                        _combinedTensor[b, matIndex * 2, y, x] = normalizedValue1;
                        _combinedTensor[b, matIndex * 2 + 1, y, x] = normalizedValue0;
                    }
                }
            }

            if (!ReferenceEquals(continuousMat, mats[matIndex]))
                continuousMat.Dispose();
        }
    }

    protected class FrameData
    {
        public Mat Mat { get; init; }
        public double Timestamp { get; set; }
    }
}
