using CommunityToolkit.Mvvm.ComponentModel;
using Baballonia.Models;
using Baballonia.Contracts;
using CommunityToolkit.Mvvm.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Baballonia.Helpers;
using Baballonia.Services;
using CommunityToolkit.Mvvm.Input;
using Baballonia.Services.Inference.Filters;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class CalibrationViewModel : ViewModelBase, IDisposable
{
    // Parameter groups with integrated filter settings
    public ParameterGroupCollection EyeMovementSettings { get; set; }
    public ParameterGroupCollection EyeBlinkingSettings { get; set; }
    public ParameterGroupCollection JawSettings { get; set; }
    public ParameterGroupCollection MouthSettings { get; set; }
    public ParameterGroupCollection TongueSettings { get; set; }
    public ParameterGroupCollection NoseSettings { get; set; }
    public ParameterGroupCollection CheekSettings { get; set; }

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


        // Create integrated parameter groups with filter settings
        EyeMovementSettings = new ParameterGroupCollection("EyeMovement", new EyeMovementFilterSettings(_settingsService),
        [
            new("LeftEyeX", -1f, 1f, -1f, 1f),
            new("LeftEyeY", -1f, 1f, -1f, 1f),
            new("RightEyeX", -1f, 1f, -1f, 1f),
            new("RightEyeY", -1f, 1f, -1f, 1f)
        ]);

        EyeBlinkingSettings = new ParameterGroupCollection("EyeBlinking", new EyeBlinkingFilterSettings(_settingsService),
        [
            new("LeftEyeLid"),
            new("RightEyeLid")
        ]);

        JawSettings = new ParameterGroupCollection("Jaw", new JawFilterSettings(_settingsService),
        [
            new("JawOpen"),
            new("JawForward"),
            new("JawLeft"),
            new("JawRight")
        ]);

        CheekSettings = new ParameterGroupCollection("Cheek", new CheekFilterSettings(_settingsService),
        [
            new("CheekPuffLeft"),
            new("CheekPuffRight"),
            new("CheekSuckLeft"),
            new("CheekSuckRight")
        ]);

        NoseSettings = new ParameterGroupCollection("Nose", new NoseFilterSettings(_settingsService),
        [
            new("NoseSneerLeft"),
            new("NoseSneerRight")
        ]);

        MouthSettings = new ParameterGroupCollection("Mouth", new MouthFilterSettings(_settingsService),
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
        ]);

        TongueSettings = new ParameterGroupCollection("Tongue", new TongueFilterSettings(_settingsService),
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
        ]);

        foreach (var setting in EyeMovementSettings.Concat(EyeBlinkingSettings).Concat(JawSettings).Concat(CheekSettings)
                     .Concat(NoseSettings).Concat(MouthSettings).Concat(TongueSettings))
        {
            setting.PropertyChanged += OnSettingChanged;
        }

        // Set up filter setting change handlers for processing pipeline updates
        var allParameterGroups = new[] { EyeMovementSettings, EyeBlinkingSettings, JawSettings, 
                                        MouthSettings, TongueSettings, NoseSettings, CheekSettings };
        foreach (var group in allParameterGroups)
        {
            group.FilterSettings.PropertyChanged += OnFilterSettingChanged;
        }

        // Convert dictionary order into index mapping
        _eyeKeyIndexMap = new Dictionary<string, int>()
        {
            { "LeftEyeX", 0 },
            { "LeftEyeY", 1 },
            { "RightEyeX", 3 },
            { "RightEyeY", 4 },
            { "LeftEyeLid", 2 },
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

        _processingLoopService.ExpressionChangeEvent += ExpressionUpdateHandler;

        LoadInitialSettings();
        UpdateProcessingFilters();
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
                ApplyCurrentEyeExpressionValues(expressions.EyeExpression, EyeMovementSettings);
                ApplyCurrentEyeExpressionValues(expressions.EyeExpression, EyeBlinkingSettings);
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
        LoadInitialSettings(EyeMovementSettings);
        LoadInitialSettings(EyeBlinkingSettings);
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

    private void OnFilterSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateProcessingFilters();
    }



    private void UpdateProcessingFilters()
    {
        var faceGroupFilter = new ParameterGroupFilter();
        var eyeGroupFilter = new ParameterGroupFilter();

        // Configure eye filters
        if (EyeMovementSettings.FilterSettings.Enabled)
        {
            int[] eyeMovementIndices = { 0, 1, 3, 4 }; // LeftEyeX, LeftEyeY, RightEyeX, RightEyeY
            eyeGroupFilter.ConfigureGroupFilter("EyeMovement", eyeMovementIndices, 
                EyeMovementSettings.FilterSettings.MinFreqCutoff, EyeMovementSettings.FilterSettings.SpeedCutoff);
        }

        if (EyeBlinkingSettings.FilterSettings.Enabled)
        {
            int[] eyeBlinkingIndices = { 2, 5 }; // LeftEyeLid, RightEyeLid
            eyeGroupFilter.ConfigureGroupFilter("EyeBlinking", eyeBlinkingIndices, 
                EyeBlinkingSettings.FilterSettings.MinFreqCutoff, EyeBlinkingSettings.FilterSettings.SpeedCutoff);
        }

        // Configure face filters
        var faceParameterGroups = new[] { JawSettings, MouthSettings, TongueSettings, NoseSettings, CheekSettings };
        foreach (var group in faceParameterGroups)
        {
            if (group.FilterSettings.Enabled)
            {
                var indices = GetFaceParameterIndices(group);
                faceGroupFilter.ConfigureGroupFilter(group.GroupName, indices, 
                    group.FilterSettings.MinFreqCutoff, group.FilterSettings.SpeedCutoff);
            }
        }

        _processingLoopService.FaceProcessingPipeline.Filter = faceGroupFilter;
        _processingLoopService.EyesProcessingPipeline.Filter = eyeGroupFilter;
    }

    private int[] GetFaceParameterIndices(ParameterGroupCollection group)
    {
        return group.Select(setting => _faceKeyIndexMap.TryGetValue(setting.Name, out var index) ? index : -1)
                   .Where(index => index >= 0)
                   .ToArray();
    }

    public void Dispose()
    {
        _processingLoopService.ExpressionChangeEvent -= ExpressionUpdateHandler;
        
        var allParameterGroups = new[] { EyeMovementSettings, EyeBlinkingSettings, JawSettings, 
                                        MouthSettings, TongueSettings, NoseSettings, CheekSettings };
        foreach (var group in allParameterGroups)
        {
            group.FilterSettings.PropertyChanged -= OnFilterSettingChanged;
        }
    }
}
