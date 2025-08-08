using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Helpers;
using Baballonia.Models;
using Baballonia.Services.Inference;
using Baballonia.ViewModels.SplitViewPane;
using Microsoft.Extensions.Hosting;
using OscCore;

namespace Baballonia.Services;

public class ParameterSenderService(
    OscSendService sendService,
    ILocalSettingsService localSettingsService) : BackgroundService
{
    public void RegisterLeftCameraController(CameraController controller) => _leftCameraController = controller;
    public void RegisterRightCameraController(CameraController controller) => _rightCameraController = controller;
    public void RegisterFaceCameraController(CameraController controller) => _faceCameraController = controller;

    private readonly Queue<OscMessage> _sendQueue = new();

    // Camera controller references
    private CameraController _leftCameraController;
    private CameraController _rightCameraController;
    private CameraController _faceCameraController;

    // Cache for expression settings to avoid repeated async calls
    private readonly ConcurrentDictionary<string, (float Lower, float Upper)> _expressionSettings = new();

    // Expression parameter names
    private readonly Dictionary<string, string> _eyeExpressionMap = new()
    {
        { "LeftEyeX", "/LeftEyeX" },
        { "LeftEyeY", "/LeftEyeY" },
        { "LeftEyeLid", "/LeftEyeLid" },
        { "RightEyeX", "/RightEyeX" },
        { "RightEyeY", "/RightEyeY" },
        { "RightEyeLid", "/RightEyeLid" },
        // { "BrowRaise", "/BrowRaise" },
        // { "BrowAngry", "/BrowAngry" },
        // { "EyeWiden", "/EyeWiden" },
        // { "EyeSquint", "/EyeSquint" },
        // { "EyeDilate", "/EyeDilate" },
    };

    private readonly Dictionary<string, string> _faceExpressionMap = new()
    {
        { "CheekPuffLeft", "/cheekPuffLeft" },
        { "CheekPuffRight", "/cheekPuffRight" },
        { "CheekSuckLeft", "/cheekSuckLeft" },
        { "CheekSuckRight", "/cheekSuckRight" },
        { "JawOpen", "/jawOpen" },
        { "JawForward", "/jawForward" },
        { "JawLeft", "/jawLeft" },
        { "JawRight", "/jawRight" },
        { "NoseSneerLeft", "/noseSneerLeft" },
        { "NoseSneerRight", "/noseSneerRight" },
        { "MouthFunnel", "/mouthFunnel" },
        { "MouthPucker", "/mouthPucker" },
        { "MouthLeft", "/mouthLeft" },
        { "MouthRight", "/mouthRight" },
        { "MouthRollUpper", "/mouthRollUpper" },
        { "MouthRollLower", "/mouthRollLower" },
        { "MouthShrugUpper", "/mouthShrugUpper" },
        { "MouthShrugLower", "/mouthShrugLower" },
        { "MouthClose", "/mouthClose" },
        { "MouthSmileLeft", "/mouthSmileLeft" },
        { "MouthSmileRight", "/mouthSmileRight" },
        { "MouthFrownLeft", "/mouthFrownLeft" },
        { "MouthFrownRight", "/mouthFrownRight" },
        { "MouthDimpleLeft", "/mouthDimpleLeft" },
        { "MouthDimpleRight", "/mouthDimpleRight" },
        { "MouthUpperUpLeft", "/mouthUpperUpLeft" },
        { "MouthUpperUpRight", "/mouthUpperUpRight" },
        { "MouthLowerDownLeft", "/mouthLowerDownLeft" },
        { "MouthLowerDownRight", "/mouthLowerDownRight" },
        { "MouthPressLeft", "/mouthPressLeft" },
        { "MouthPressRight", "/mouthPressRight" },
        { "MouthStretchLeft", "/mouthStretchLeft" },
        { "MouthStretchRight", "/mouthStretchRight" },
        { "TongueOut", "/tongueOut" },
        { "TongueUp", "/tongueUp" },
        { "TongueDown", "/tongueDown" },
        { "TongueLeft", "/tongueLeft" },
        { "TongueRight", "/tongueRight" },
        { "TongueRoll", "/tongueRoll" },
        { "TongueBendDown", "/tongueBendDown" },
        { "TongueCurlUp", "/tongueCurlUp" },
        { "TongueSquish", "/tongueSquish" },
        { "TongueFlat", "/tongueFlat" },
        { "TongueTwistLeft", "/tongueTwistLeft" },
        { "TongueTwistRight", "/tongueTwistRight" }
    };

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // Subscribe to property changes
        CalibrationViewModel.ExpressionUpdated += OnCalibrationPropertyChanged;

        // Load initial settings
        await LoadInitialSettings();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var prefix = await localSettingsService.ReadSettingAsync<string>("AppSettings_OSCPrefix");

                try
                {
                    if (_leftCameraController != null) ProcessEyeExpressionData(_leftCameraController.ArExpressions, prefix);
                    if (_faceCameraController != null) ProcessFaceExpressionData(_faceCameraController.ArExpressions, prefix);

                    await SendAndClearQueue(cancellationToken);
                    await Task.Delay(10, cancellationToken);
                }
                catch (Exception)
                {
                    // ignore!
                }
            }
        }
        finally
        {
            // Unsubscribe when done
            CalibrationViewModel.ExpressionUpdated -= OnCalibrationPropertyChanged;
        }
    }

    private async Task LoadInitialSettings()
    {
        // Load all initial settings into cache
        var allParameterNames = _eyeExpressionMap.Keys.Concat(_faceExpressionMap.Keys);

        foreach (var parameterName in allParameterNames)
        {
            var lower = await localSettingsService.ReadSettingAsync<float>($"{parameterName}Lower");
            var upper = await localSettingsService.ReadSettingAsync<float>($"{parameterName}Upper");

            _expressionSettings[parameterName] = (lower, upper);
        }
    }

    private void OnCalibrationPropertyChanged(string expression, float value)
    {
        if (string.IsNullOrEmpty(expression))
            return;

        // Parse property name to extract parameter and bound type
        // Assuming property names follow pattern like "LeftEyeXLower" or "LeftEyeXUpper"
        string parameterName = string.Empty;
        bool isUpper = false;

        if (expression.EndsWith("Lower"))
        {
            parameterName = expression.Substring(0, expression.Length - 5); // Remove "Lower"
            isUpper = false;
        }
        else if (expression.EndsWith("Upper"))
        {
            parameterName = expression.Substring(0, expression.Length - 5); // Remove "Upper"
            isUpper = true;
        }

        if (_expressionSettings.TryGetValue(parameterName, out var currentSettings))
        {
            if (isUpper)
            {
                _expressionSettings[parameterName] = (currentSettings.Lower, value);
            }
            else
            {
                _expressionSettings[parameterName] = (value, currentSettings.Upper);
            }
        }
        else
        {
            // If not found, create new entry with default for the other bound
            if (isUpper)
            {
                _expressionSettings[parameterName] = (0f, value);
            }
            else
            {
                _expressionSettings[parameterName] = (value, 1f);
            }
        }
    }

    private (float Lower, float Upper) GetExpressionSettings(string parameterName)
    {
        var settings = _expressionSettings.TryGetValue(parameterName, out var s) ? s : (0f, 0f); // This never fails, instead it outputs two zeros.

        return (settings.Item1 == 0f && settings.Item2 == 0f)
            ? (0f, 1f)
            : settings;
    }


    private void ProcessEyeExpressionData(float[] expressions, string prefix = "")
    {
        if (expressions is null) return;
        if (expressions.Length == 0) return;

        // Process each expression and create OSC messages
        for (int i = 0; i < Math.Min(expressions.Length, _eyeExpressionMap.Count); i++)
        {
            var weight = expressions[i];
            var settings = GetExpressionSettings(_eyeExpressionMap.ElementAt(i).Key);

            var msg = new OscMessage(prefix + _eyeExpressionMap.ElementAt(i).Value,
                Math.Clamp(
                    weight.Remap(settings.Lower, settings.Upper),
                    -1,
                    1));
            _sendQueue.Enqueue(msg);
        }
    }

    private void ProcessFaceExpressionData(float[] expressions, string prefix = "")
    {
        if (expressions == null) return;
        if (expressions.Length == 0) return;

        // Process each expression and create OSC messages
        for (int i = 0; i < Math.Min(expressions.Length, _faceExpressionMap.Count); i++)
        {
            var weight = expressions[i];
            var settings = GetExpressionSettings(_faceExpressionMap.ElementAt(i).Key);
            var msg = new OscMessage(prefix + _faceExpressionMap.ElementAt(i).Value,
                Math.Clamp(
                    weight.Remap(settings.Lower, settings.Upper),
                    0,
                    1));
            _sendQueue.Enqueue(msg);
        }
    }

    private async Task SendAndClearQueue(CancellationToken cancellationToken)
    {
        if (_sendQueue.Count == 0)
            return;

        await sendService.Send(_sendQueue.ToArray(), cancellationToken);
        _sendQueue.Clear();
    }
}
