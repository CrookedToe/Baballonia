using System;
using System.Collections.Generic;
using Baballonia.Contracts;
using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baballonia.Services
{
    public static class EyeInferenceServiceFactory
    {
        public static IInferenceService Create(IServiceProvider serviceProvider, Dictionary<Camera, string>? cameraUrls, CameraSettings leftCameraSettings, CameraSettings rightCameraSettings)
        {
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("Baballonia.Factories.EyeInferenceServiceFactory");
            
            logger.LogDebug("Creating eye inference service with camera configuration");
            logger.LogDebug("Left camera settings: Camera={Camera}, ROI=({X},{Y},{Width},{Height})", 
                leftCameraSettings.Camera, leftCameraSettings.Roi.X, leftCameraSettings.Roi.Y, 
                leftCameraSettings.Roi.Width, leftCameraSettings.Roi.Height);
            logger.LogDebug("Right camera settings: Camera={Camera}, ROI=({X},{Y},{Width},{Height})", 
                rightCameraSettings.Camera, rightCameraSettings.Roi.X, rightCameraSettings.Roi.Y, 
                rightCameraSettings.Roi.Width, rightCameraSettings.Roi.Height);

            if (cameraUrls == null) 
            {
                logger.LogDebug("Camera URLs dictionary is null, defaulting to DualCameraEyeInferenceService");
                return serviceProvider.GetRequiredService<IDualCameraEyeInferenceService>();
            }

            var leftCameraUrl = cameraUrls.GetValueOrDefault(Camera.Left);
            var rightCameraUrl = cameraUrls.GetValueOrDefault(Camera.Right);
            
            logger.LogDebug("Camera URL resolution: Left='{LeftUrl}', Right='{RightUrl}'", 
                leftCameraUrl ?? "null", rightCameraUrl ?? "null");

            // If either camera URL is not set or if they're the same, use single camera mode
            if (!string.IsNullOrEmpty(leftCameraUrl) && !string.IsNullOrEmpty(rightCameraUrl))
            {
                if (leftCameraUrl == rightCameraUrl)
                {
                    logger.LogDebug("Both cameras use the same URL '{CameraUrl}', selecting SingleCameraEyeInferenceService", leftCameraUrl);
                    
                    /*var leftRoi = leftCameraSettings.Roi;
                    var rightRoi = rightCameraSettings.Roi;

                    if ((leftRoi.X != rightRoi.X || leftRoi.Y != rightRoi.Y ||
                         leftRoi.Width != rightRoi.Width || leftRoi.Height != rightRoi.Height))
                    {
                        logger.LogDebug("Different ROI configurations detected - Left: ({X1},{Y1},{W1},{H1}), Right: ({X2},{Y2},{W2},{H2})", 
                            leftRoi.X, leftRoi.Y, leftRoi.Width, leftRoi.Height,
                            rightRoi.X, rightRoi.Y, rightRoi.Width, rightRoi.Height);
                        return serviceProvider.GetRequiredService<ISingleCameraEyeInferenceService>();
                    }*/

                    return serviceProvider.GetRequiredService<ISingleCameraEyeInferenceService>();
                }
                else
                {
                    logger.LogDebug("Different camera URLs detected, selecting DualCameraEyeInferenceService");
                }
            }
            else
            {
                if (string.IsNullOrEmpty(leftCameraUrl))
                    logger.LogDebug("Left camera URL is empty or null");
                if (string.IsNullOrEmpty(rightCameraUrl))
                    logger.LogDebug("Right camera URL is empty or null");
                logger.LogDebug("Missing camera URL(s), defaulting to DualCameraEyeInferenceService");
            }

            return serviceProvider.GetRequiredService<IDualCameraEyeInferenceService>();
        }
    }
}
