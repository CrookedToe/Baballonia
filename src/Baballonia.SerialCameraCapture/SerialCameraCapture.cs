using System.Buffers.Binary;
using System.IO.Ports;
using System.Numerics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.SerialCameraCapture;

/// <summary>
/// Serial Camera capture class intended for use on Desktop platforms
/// Babble-board specific implementation, assumes a fixed camera size of 240x240
/// </summary>
public sealed class SerialCameraCapture(string portName, object? logger = null) : Capture(portName), IDisposable
{
    public override HashSet<Regex> Connections { get; set; } =
    [
        new(@"^com", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"^/dev/ttyacm", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"^/dev/tty\.usb", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"^/dev/cu\.usb", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
    ];

    private const int BaudRate = 3000000;
    private const ulong EtvrHeader = 0xd8ff0000a1ffa0ff, EtvrHeaderMask = 0xffff0000ffffffff;
    private bool _isDisposed;

    private readonly ILogger? _logger = logger as ILogger;
    private readonly SerialPort _serialPort = new()
    {
        PortName = portName,
        BaudRate = BaudRate,
        ReadTimeout = SerialPort.InfiniteTimeout,
    };


    public override Task<bool> StartCapture()
    {
        LogDebug("=== STARTING SERIAL CAMERA CAPTURE ===");
        LogDebug("Port Name: '" + portName + "'");
        LogDebug("Baud Rate: " + BaudRate);
        LogDebug("Operating System: " + Environment.OSVersion);
        
        try
        {
            LogDebug("Opening serial port '" + portName + "'");
            _serialPort.Open();
            LogDebug("Serial port opened successfully");
            
            IsReady = true;
            LogDebug("Starting data loop for serial camera");
            DataLoop();
            
            LogDebug("=== SERIAL CAMERA CAPTURE STARTED ===");
        }
        catch (Exception ex)
        {
            LogError(ex, "Failed to start serial camera capture on port '" + portName + "'");
            IsReady = false;
        }

        return Task.FromResult(IsReady);
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

    private async void DataLoop()
    {
        LogDebug("Serial camera data loop started");
        byte[] buffer = new byte[2048];
        int frameCount = 0;
        
        try
        {
            while (_serialPort.IsOpen)
            {
                Stream stream = _serialPort.BaseStream;
                
                // Read header
                for (int bufferPosition = 0; bufferPosition < sizeof(ulong);)
                    bufferPosition += await stream.ReadAsync(buffer, bufferPosition, sizeof(ulong) - bufferPosition);
                ulong header = BinaryPrimitives.ReadUInt64LittleEndian(buffer);
                
                // Search for valid header
                for (; (header & EtvrHeaderMask) != EtvrHeader; header = header >> 8 | (ulong)buffer[0] << 56)
                    while (await stream.ReadAsync(buffer, 0, 1) == 0) /**/;

                ushort jpegSize = (ushort)(header >> BitOperations.TrailingZeroCount(~EtvrHeaderMask));
                if (buffer.Length < jpegSize)
                {
                    LogDebug($"Resizing buffer from {buffer.Length} to {jpegSize} bytes");
                    Array.Resize(ref buffer, jpegSize);
                }

                BinaryPrimitives.WriteUInt16LittleEndian(buffer, 0xd8ff);
                for (int bufferPosition = 2; bufferPosition < jpegSize;)
                    bufferPosition += await stream.ReadAsync(buffer, bufferPosition, jpegSize - bufferPosition);
                
                var newFrame = Mat.FromImageData(buffer);
                // Only update the frame count if the image data has actually changed
                if (newFrame.Width > 0 && newFrame.Height > 0)
                {
                    newFrame.CopyTo(RawMat);
                    frameCount++;
                    
                    if (frameCount % 100 == 0) // Log every 100 frames
                    {
                        LogDebug($"Processed {frameCount} frames from serial camera");
                    }
                }
                else
                {
                    LogWarning("Received invalid frame from serial camera (width or height is 0)");
                }
            }
        }
        catch (ObjectDisposedException)
        {
            LogWarning("Serial port was disposed - device likely unplugged");
            // Handle when the device is unplugged
            await StopCapture();
            Dispose();
        }
        catch (Exception ex)
        {
            LogError(ex, "Error in serial camera data loop");
            await StopCapture();
        }
        
        LogDebug($"Serial camera data loop ended after processing {frameCount} frames");
    }

    public override Task<bool> StopCapture()
    {
        LogDebug("Stopping serial camera capture");
        try
        {
            _serialPort.Close();
            IsReady = false;
            LogDebug("Serial camera capture stopped successfully");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            LogError(ex, "Error stopping serial camera capture");
            return Task.FromResult(false);
        }
    }

    private void Dispose(bool disposing)
    {
        if (_isDisposed) return;

        if (disposing)
        {
            LogDebug("Disposing serial camera capture resources");
            StopCapture(); // xlinka 11/8/24: Ensure capture stops before disposing resources
            _serialPort?.Dispose(); // xlinka 11/8/24: Dispose of serial port if initialized
            LogDebug("Serial camera capture resources disposed");
        }
        _isDisposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this); // xlinka 11/8/24: Suppress finalization as resources are now disposed
    }

    ~SerialCameraCapture()
    {
        Dispose(false);
    }
}
