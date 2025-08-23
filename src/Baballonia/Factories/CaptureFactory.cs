using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Baballonia.Services.Inference.Platforms;
using Microsoft.Extensions.Logging;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.Factories;

public class CaptureFactory(IPlatformConnector platformConnector, ILogger logger) : ICaptureFactory
{
    public Capture? CreateAndStart(string url)
    {
        Capture? c = null;
        try
        {
            foreach (var capture in platformConnector.Captures)
            {
                if (capture.Key.Any(regex => regex.IsMatch(url)))
                {
                    c = (Capture)Activator.CreateInstance(capture.Value, url)!;
                    break;
                }
            }

            if (c is not null)
            {
                logger.LogDebug($"Creating capture {url}");
                c.StartCapture();
                return c;
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to create capture");
            return null;
        }

        return null;
    }
}

public interface ICaptureFactory
{
    public Capture? CreateAndStart(string url);
}
