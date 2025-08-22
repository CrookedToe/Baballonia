using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.OpenCVCapture;

/// <summary>
/// Wrapper class for OpenCV
/// </summary>
public sealed partial class OpenCvCapture : Capture
{
    // Numbers only, http or GStreamer pipeline
    [GeneratedRegex(@"^\d+$|^https?://.*|^/dev/video\d+$|\s+!\s*appsink$|\.local$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex MyRegex();

    public override HashSet<Regex> Connections { get; set; } = [MyRegex()];

    private readonly ILogger? _logger;
    private VideoCapture? _videoCapture;
    private static readonly VideoCaptureAPIs PreferredBackend;

    private Task? _updateTask;
    private readonly CancellationTokenSource _updateTaskCts = new();

    public OpenCvCapture(string url) : base(url)
    {
        _logger = null;
    }

    public OpenCvCapture(string url, object logger) : base(url)
    {
        _logger = logger as ILogger;
    }

    static OpenCvCapture()
    {
        // Choose the most appropriate backend based on the detected OS
        // This is needed to handle concurrent camera access
        if (OperatingSystem.IsWindows())
        {
            PreferredBackend = VideoCaptureAPIs.DSHOW;
        }
        else if (OperatingSystem.IsLinux())
        {
            PreferredBackend = VideoCaptureAPIs.GSTREAMER;
        }
        else if (OperatingSystem.IsMacOS())
        {
            PreferredBackend = VideoCaptureAPIs.AVFOUNDATION;
        }
        else
        {
            // Fallback to ANY which lets OpenCV choose
            PreferredBackend = VideoCaptureAPIs.ANY;
        }
    }

    public override async Task<bool> StartCapture()
    {
        LogDebug("=== STARTING CAMERA CAPTURE ===");
        LogDebug("Operating System: " + Environment.OSVersion);
        LogDebug("Camera Source URL: '" + Url + "'");
        LogDebug("Preferred Backend: " + PreferredBackend);
        LogDebug("OpenCV Version: " + Cv2.GetVersionString());
        
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            try
            {
                if (int.TryParse(Url, out var index))
                {
                    LogDebug("URL is numeric camera index: " + index);
                    LogDebug("Creating VideoCapture from camera index " + index + " with backend " + PreferredBackend);
                    _videoCapture = await Task.Run(() => VideoCapture.FromCamera(index, PreferredBackend), cts.Token);
                }
                else
                {
                    LogDebug("URL is string-based: '" + Url + "'");
                    LogDebug("Creating VideoCapture from URL string (backend will be auto-detected)");
                    _videoCapture = await Task.Run(() => new VideoCapture(Url), cts.Token);
                }
                
                LogDebug("VideoCapture instance created successfully");
            }
            catch (OperationCanceledException)
            {
                LogWarning("Camera capture initialization timed out after 5 seconds for URL: '" + Url + "'");
                IsReady = false;
                return false;
            }
            catch (Exception ex)
            {
                LogError(ex, "Error creating VideoCapture for URL: '" + Url + "'");
                IsReady = false;
                return false;
            }
        }

        // Handle edge case cameras like the Varjo Aero that send frames in YUV
        // This won't activate the IR illuminators, but it's a good idea to standardize inputs
        LogDebug("Setting ConvertRgb to true for standardized input");
        _videoCapture.ConvertRgb = true;
        IsReady = _videoCapture.IsOpened();
        
        LogDebug("VideoCapture.IsOpened(): " + IsReady);
        
        if (!IsReady)
        {
            LogWarning("Camera failed to open - checking potential issues...");
            LogWarning("Camera URL: '" + Url + "'");
            LogWarning("Backend: " + PreferredBackend);
            LogWarning("This could be due to: camera already in use, permissions, driver issues, or invalid URL");
        }
        
        if (IsReady)
        {
            var width = _videoCapture.Get(VideoCaptureProperties.FrameWidth);
            var height = _videoCapture.Get(VideoCaptureProperties.FrameHeight);
            var fps = _videoCapture.Get(VideoCaptureProperties.Fps);
            var fourcc = _videoCapture.Get(VideoCaptureProperties.FourCC);
            var bufferSize = _videoCapture.Get(VideoCaptureProperties.BufferSize);
            var brightness = _videoCapture.Get(VideoCaptureProperties.Brightness);
            var contrast = _videoCapture.Get(VideoCaptureProperties.Contrast);
            var saturation = _videoCapture.Get(VideoCaptureProperties.Saturation);
            var gain = _videoCapture.Get(VideoCaptureProperties.Gain);
            var exposure = _videoCapture.Get(VideoCaptureProperties.Exposure);
            var autoExposure = _videoCapture.Get(VideoCaptureProperties.AutoExposure);
            
            LogDebug("=== CAMERA INITIALIZATION COMPLETE ===");
            LogDebug("Camera Source: " + Url);
            LogDebug("Backend: " + PreferredBackend);
            LogDebug("Resolution: " + width + "x" + height);
            LogDebug("Frame Rate: " + fps + " FPS");
            LogDebug("FourCC Code: " + fourcc + " (hex: " + ((int)fourcc).ToString("X8") + ")");
            LogDebug("Buffer Size: " + bufferSize);
            LogDebug("Brightness: " + brightness);
            LogDebug("Contrast: " + contrast);
            LogDebug("Saturation: " + saturation);
            LogDebug("Gain: " + gain);
            LogDebug("Exposure: " + exposure);
            LogDebug("Auto Exposure: " + autoExposure);
            LogDebug("Convert RGB: " + _videoCapture.ConvertRgb);
            LogDebug("============================================");
            
            CancellationToken token = _updateTaskCts.Token;
            LogDebug("Starting video capture update loop");
            _updateTask = Task.Run(() => VideoCapture_UpdateLoop(_videoCapture, token));
        }

        return IsReady;
    }

    private void LogDebug(string message)
    {
        _logger?.LogInformation(message);
    }

    private void LogWarning(string message)
    {
        _logger?.LogWarning(message);
    }

    private void LogError(Exception ex, string message)
    {
        _logger?.LogError(ex, message);
    }

    private Task VideoCapture_UpdateLoop(VideoCapture capture, CancellationToken ct)
    {
        bool wasReady = IsReady;
        int consecutiveFailures = 0;
        const int maxFailuresBeforeWarning = 5; // Log warning after 5 consecutive failures
        
        while (!ct.IsCancellationRequested)
        {
            try
            {
                bool currentFrameSuccess = capture.Read(RawMat);
                
                if (currentFrameSuccess)
                {
                    if (!wasReady && consecutiveFailures > 0)
                    {
                        LogWarning($"Camera connection restored for URL '{Url}' after {consecutiveFailures} failed attempts");
                        consecutiveFailures = 0;
                    }
                    IsReady = true;
                    wasReady = true;
                }
                else
                {
                    consecutiveFailures++;
                    
                    if (wasReady)
                    {
                        LogWarning($"CAMERA DISCONNECTION DETECTED: Frame read failed for camera URL '{Url}' (Backend: {PreferredBackend})");
                        LogWarning("This typically indicates: USB disconnection, camera in use by another application, or hardware failure");
                        wasReady = false;
                    }
                    else if (consecutiveFailures == maxFailuresBeforeWarning)
                    {
                        LogWarning($"Camera remains disconnected after {consecutiveFailures} attempts for URL '{Url}'");
                    }
                    else if (consecutiveFailures % 50 == 0) // Log every 50 failures (about every 1 second)
                    {
                        LogWarning($"Camera still disconnected after {consecutiveFailures} attempts for URL '{Url}'");
                    }
                    
                    IsReady = false;
                }
                
                Task.Delay(20, ct).Wait(ct);
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                if (wasReady)
                {
                    LogError(ex, $"CAMERA EXCEPTION DETECTED: Exception occurred in capture loop for URL '{Url}'");
                    wasReady = false;
                }
                else if (consecutiveFailures % 50 == 0)
                {
                    LogError(ex, $"Ongoing camera exception for URL '{Url}' after {consecutiveFailures} attempts");
                }
                IsReady = false;
            }
        }

        LogDebug($"Video capture update loop terminated for URL '{Url}'");
        return Task.CompletedTask;
    }

    public override Task<bool> StopCapture()
    {
        LogDebug($"StopCapture requested for camera URL '{Url}'");
        
        if (_videoCapture is null)
        {
            LogDebug("StopCapture: VideoCapture is already null, returning false");
            return Task.FromResult(false);
        }

        LogDebug("Cancelling video capture update task...");
        if (_updateTask != null) {
            _updateTaskCts.Cancel();
            _updateTask.Wait();
            LogDebug("Video capture update task cancelled successfully");
        }

        IsReady = false;
        if (_videoCapture != null)
        {
            LogDebug("Releasing and disposing VideoCapture instance...");
            _videoCapture.Release();
            _videoCapture.Dispose();
            _videoCapture = null;
            LogDebug("VideoCapture released and disposed successfully");
        }
        
        LogDebug($"Camera capture stopped successfully for URL '{Url}'");
        return Task.FromResult(true);
    }
}
