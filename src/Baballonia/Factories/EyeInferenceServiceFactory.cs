using System;
using System.Collections.Generic;
using Baballonia.Contracts;
using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Baballonia.Services
{
    public static class EyeInferenceServiceFactory
    {
        public static IInferenceService Create(IServiceProvider serviceProvider, Dictionary<Camera, string>? cameraUrls, CameraSettings leftCameraSettings, CameraSettings rightCameraSettings)
        {
            if (cameraUrls == null) return serviceProvider.GetRequiredService<IDualCameraEyeInferenceService>();

            var leftCameraUrl = cameraUrls.GetValueOrDefault(Camera.Left);
            var rightCameraUrl = cameraUrls.GetValueOrDefault(Camera.Right);

            // If either camera URL is not set or if they're the same, use single camera mode
            if (!string.IsNullOrEmpty(leftCameraUrl) && !string.IsNullOrEmpty(rightCameraUrl))
            {
                if (leftCameraUrl == rightCameraUrl)
                {
                    /*var leftRoi = leftCameraSettings.Roi;
                    var rightRoi = rightCameraSettings.Roi;

                    if ((leftRoi.X != rightRoi.X || leftRoi.Y != rightRoi.Y ||
                         leftRoi.Width != rightRoi.Width || leftRoi.Height != rightRoi.Height))
                    {
                        return serviceProvider.GetRequiredService<ISingleCameraEyeInferenceService>();
                    }*/

                    return serviceProvider.GetRequiredService<ISingleCameraEyeInferenceService>();
                }
            }

            return serviceProvider.GetRequiredService<IDualCameraEyeInferenceService>();
        }
    }
}
