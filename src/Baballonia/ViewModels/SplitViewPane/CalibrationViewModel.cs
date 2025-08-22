using CommunityToolkit.Mvvm.ComponentModel;
using Baballonia.Models;
using Baballonia.Contracts;
using CommunityToolkit.Mvvm.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Baballonia.Helpers;
using Baballonia.Services;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf.WellKnownTypes;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class CalibrationViewModel : ViewModelBase, IDisposable
{
    public ObservableCollection<SliderBindableSetting> GazeSettings { get; set; }
    public ObservableCollection<SliderBindableSetting> EyeSettings { get; set; }
    public ObservableCollection<SliderBindableSetting> JawSettings { get; set; }
    public ObservableCollection<SliderBindableSetting> MouthSettings { get; set; }
    public ObservableCollection<SliderBindableSetting> TongueSettings { get; set; }
    public ObservableCollection<SliderBindableSetting> NoseSettings { get; set; }
    public ObservableCollection<SliderBindableSetting> CheekSettings { get; set; }

    private ILocalSettingsService _settingsService { get; }
    private readonly ICalibrationService _calibrationService;
    private readonly ParameterSenderService _parameterSenderService;
    private readonly ProcessingLoopService _processingLoopService;

    private readonly Dictionary<string, int> _eyeKeyIndexMap;
    private readonly Dictionary<string, int> _faceKeyIndexMap;
    public CalibrationViewModel()
    {
        _settingsService = Ioc.Default.GetService<ILocalSettingsService>()!;
        _calibrationService = Ioc.Default.GetService<ICalibrationService>()!;
        _parameterSenderService = Ioc.Default.GetService<ParameterSenderService>()!;
        _processingLoopService = Ioc.Default.GetService<ProcessingLoopService>()!;

        GazeSettings =
        [
            new("LeftEyeX", -1f, 1f, -1f, 1f),
            new("LeftEyeY", -1f, 1f, -1f, 1f),
            new("RightEyeX", -1f, 1f, -1f, 1f),
            new("RightEyeY", -1f, 1f, -1f, 1f),
        ];

        EyeSettings =
        [
            new("LeftEyeLid"),
            new("RightEyeLid")
        ];

        JawSettings =
        [
            new("JawOpen"),
            new("JawForward"),
            new("JawLeft"),
            new("JawRight")
        ];

        CheekSettings =
        [
            new("CheekPuffLeft"),
            new("CheekPuffRight"),
            new("CheekSuckLeft"),
            new("CheekSuckRight")
        ];

        NoseSettings =
        [
            new("NoseSneerLeft"),
            new("NoseSneerRight")
        ];

        MouthSettings =
        [
            new("MouthFunnel"),
            new("MouthPucker"),
            new("MouthLeft"),
            new("MouthRight"),
            new("MouthRollUpper"),
            new("MouthRollLower"),
            new("MouthShrugUpper"),
            new("MouthShrugLower"),
            new("MouthClose"),
            new("MouthSmileLeft"),
            new("MouthSmileRight"),
            new("MouthFrownLeft"),
            new("MouthFrownRight"),
            new("MouthDimpleLeft"),
            new("MouthDimpleRight"),
            new("MouthUpperUpLeft"),
            new("MouthUpperUpRight"),
            new("MouthLowerDownLeft"),
            new("MouthLowerDownRight"),
            new("MouthPressLeft"),
            new("MouthPressRight"),
            new("MouthStretchLeft"),
            new("MouthStretchRight")
        ];

        TongueSettings =
        [
            new("TongueOut"),
            new("TongueUp"),
            new("TongueDown"),
            new("TongueLeft"),
            new("TongueRight"),
            new("TongueRoll"),
            new("TongueBendDown"),
            new("TongueCurlUp"),
            new("TongueSquish"),
            new("TongueFlat"),
            new("TongueTwistLeft"),
            new("TongueTwistRight")
        ];

        foreach (var setting in GazeSettings.Concat(EyeSettings).Concat(JawSettings).Concat(CheekSettings)
                     .Concat(NoseSettings).Concat(MouthSettings).Concat(TongueSettings))
        {
            setting.PropertyChanged += OnSettingChanged;
        }

        // Convert dictionary order into index mapping
        _eyeKeyIndexMap = new Dictionary<string, int>()
        {
            { "LeftEyeX", 0 },
            { "LeftEyeY", 1 },
            { "RightEyeX", 2 },
            { "RightEyeY", 3 },
            { "LeftEyeLid", 4 },
            { "RightEyeLid", 5 }
        };

        _faceKeyIndexMap = _parameterSenderService.FaceExpressionMap.Keys
            .Select((key, index) => new { key, index })
            .ToDictionary(x => x.key, x => x.index);

        PropertyChanged += async (o, p) =>
        {
            var propertyInfo = GetType().GetProperty(p.PropertyName!);
            object value = propertyInfo?.GetValue(this)!;
            if (value is float floatValue)
            {
                if (p.PropertyName == null) return;
                await _calibrationService.SetExpression(p.PropertyName!, floatValue);
            }
        };

        _processingLoopService.ExpressionUpdateEvent += ExpressionUpdateHandler;

        LoadInitialSettings();
    }

    private void ExpressionUpdateHandler(ProcessingLoopService.Expressions expressions)
    {
        if(expressions.FaceExpression != null)
            Dispatcher.UIThread.Post(() =>
            {
                ApplyCurrentFaceExpressionValues(expressions.FaceExpression, CheekSettings);
                ApplyCurrentFaceExpressionValues(expressions.FaceExpression, MouthSettings);
                ApplyCurrentFaceExpressionValues(expressions.FaceExpression, JawSettings);
                ApplyCurrentFaceExpressionValues(expressions.FaceExpression, NoseSettings);
                ApplyCurrentFaceExpressionValues(expressions.FaceExpression, TongueSettings);
            });
        if(expressions.EyeExpression != null)
            Dispatcher.UIThread.Post(() =>
            {
                ApplyCurrentEyeExpressionValues(expressions.EyeExpression, GazeSettings);
                ApplyCurrentEyeExpressionValues(expressions.EyeExpression, EyeSettings);
            });
    }
    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SliderBindableSetting setting) return;

        Dispatcher.UIThread.Post(async () =>
        {
            if (e.PropertyName is nameof(SliderBindableSetting.Lower))
            {
                await _calibrationService.SetExpression(setting.Name + "Lower", setting.Lower);
            }

            if (e.PropertyName is nameof(SliderBindableSetting.Upper))
            {
                await _calibrationService.SetExpression(setting.Name + "Upper", setting.Upper);
            }
        });
    }

    private void ApplyCurrentEyeExpressionValues(float[] values, IEnumerable<SliderBindableSetting> settings)
    {
        foreach (var setting in settings)
        {
            if (_eyeKeyIndexMap.TryGetValue(setting.Name, out var index)
                && index < values.Length)
            {
                var weight = values[index];
                var val = Math.Clamp(
                    weight.Remap(setting.Lower, setting.Upper, setting.Min, setting.Max),
                    setting.Min,
                    setting.Max);
                setting.CurrentExpression = val;
            }
        }
    }

    private void ApplyCurrentFaceExpressionValues(float[] values, IEnumerable<SliderBindableSetting> settings)
    {
        foreach (var setting in settings)
        {
            if (_faceKeyIndexMap.TryGetValue(setting.Name, out var index)
                && index < values.Length)
            {
                var weight = values[index];
                var val = Math.Clamp(
                    weight.Remap(setting.Lower, setting.Upper, setting.Min, setting.Max),
                    setting.Min,
                    setting.Max);
                setting.CurrentExpression = val;
            }
        }
    }

    [RelayCommand]
    public void ResetMinimums()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            await _calibrationService.ResetMinimums();
            LoadInitialSettings();
        });
    }

    [RelayCommand]
    public void ResetMaximums()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            await _calibrationService.ResetMaximums();
            LoadInitialSettings();
        });
    }

    private void LoadInitialSettings()
    {
        LoadInitialSettings(GazeSettings);
        LoadInitialSettings(EyeSettings);
        LoadInitialSettings(CheekSettings);
        LoadInitialSettings(JawSettings);
        LoadInitialSettings(MouthSettings);
        LoadInitialSettings(NoseSettings);
        LoadInitialSettings(TongueSettings);
    }

    private void LoadInitialSettings(IEnumerable<SliderBindableSetting> settings)
    {
        foreach (var setting in settings)
        {
            var val = _calibrationService.GetExpressionSettings(setting.Name);
            setting.Lower = val.Lower;
            setting.Upper = val.Upper;
            setting.Min = val.Min;
            setting.Max = val.Max;
        }
    }

    public void Dispose()
    {
        _processingLoopService.ExpressionUpdateEvent -= ExpressionUpdateHandler;
    }
}
