using CommunityToolkit.Mvvm.ComponentModel;
using Baballonia.Contracts;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Baballonia.Models;

public partial class SliderBindableSetting : ObservableObject
{
    public string Name { get; set; }

    [ObservableProperty] private float _lower;
    [ObservableProperty] private float _currentExpression;

    [ObservableProperty] private float _upper;
    [ObservableProperty] private float _min;
    [ObservableProperty] private float _max;

    public SliderBindableSetting(string name, float lower = 0f, float upper = 1f, float min = 0f, float max = 1f)
    {
        Name = name;
        Lower = lower;
        Upper = upper;
        Min = min;
        Max = max;
    }
}

// Enhanced collection that includes filter settings
public partial class ParameterGroupCollection : ObservableCollection<SliderBindableSetting>
{
    public string GroupName { get; }
    public IFilterSettings FilterSettings { get; }

    public ParameterGroupCollection(string groupName, IFilterSettings filterSettings, IEnumerable<SliderBindableSetting> items) 
        : base(items)
    {
        GroupName = groupName;
        FilterSettings = filterSettings;
    }
}


// Base interface for filter settings
public interface IFilterSettings : INotifyPropertyChanged
{
    bool Enabled { get; set; }
    float MinFreqCutoff { get; set; }
    float SpeedCutoff { get; set; }
}

// Individual filter setting classes with automatic persistence
public partial class EyeMovementFilterSettings : ObservableObject, IFilterSettings
{
    [ObservableProperty]
    [property: SavedSetting("Filter_EyeMovement_Enabled", false)]
    private bool _enabled;

    [ObservableProperty]
    [property: SavedSetting("Filter_EyeMovement_MinFreq", 0.1f)]
    private float _minFreqCutoff = 0.1f;

    [ObservableProperty]
    [property: SavedSetting("Filter_EyeMovement_Speed", 0.1f)]
    private float _speedCutoff = 0.1f;

    public EyeMovementFilterSettings(ILocalSettingsService localSettingsService)
    {
        localSettingsService.Load(this);
        PropertyChanged += (_, _) => localSettingsService.Save(this);
    }
}

public partial class EyeBlinkingFilterSettings : ObservableObject, IFilterSettings
{
    [ObservableProperty]
    [property: SavedSetting("Filter_EyeBlinking_Enabled", false)]
    private bool _enabled;

    [ObservableProperty]
    [property: SavedSetting("Filter_EyeBlinking_MinFreq", 0.5f)]
    private float _minFreqCutoff = 0.5f;

    [ObservableProperty]
    [property: SavedSetting("Filter_EyeBlinking_Speed", 0.5f)]
    private float _speedCutoff = 0.5f;

    public EyeBlinkingFilterSettings(ILocalSettingsService localSettingsService)
    {
        localSettingsService.Load(this);
        PropertyChanged += (_, _) => localSettingsService.Save(this);
    }
}

public partial class JawFilterSettings : ObservableObject, IFilterSettings
{
    [ObservableProperty]
    [property: SavedSetting("Filter_Jaw_Enabled", false)]
    private bool _enabled;

    [ObservableProperty]
    [property: SavedSetting("Filter_Jaw_MinFreq", 1.0f)]
    private float _minFreqCutoff = 1.0f;

    [ObservableProperty]
    [property: SavedSetting("Filter_Jaw_Speed", 1.0f)]
    private float _speedCutoff = 1.0f;

    public JawFilterSettings(ILocalSettingsService localSettingsService)
    {
        localSettingsService.Load(this);
        PropertyChanged += (_, _) => localSettingsService.Save(this);
    }
}

public partial class MouthFilterSettings : ObservableObject, IFilterSettings
{
    [ObservableProperty]
    [property: SavedSetting("Filter_Mouth_Enabled", false)]
    private bool _enabled;

    [ObservableProperty]
    [property: SavedSetting("Filter_Mouth_MinFreq", 1.0f)]
    private float _minFreqCutoff = 1.0f;

    [ObservableProperty]
    [property: SavedSetting("Filter_Mouth_Speed", 1.0f)]
    private float _speedCutoff = 1.0f;

    public MouthFilterSettings(ILocalSettingsService localSettingsService)
    {
        localSettingsService.Load(this);
        PropertyChanged += (_, _) => localSettingsService.Save(this);
    }
}

public partial class TongueFilterSettings : ObservableObject, IFilterSettings
{
    [ObservableProperty]
    [property: SavedSetting("Filter_Tongue_Enabled", false)]
    private bool _enabled;

    [ObservableProperty]
    [property: SavedSetting("Filter_Tongue_MinFreq", 1.0f)]
    private float _minFreqCutoff = 1.0f;

    [ObservableProperty]
    [property: SavedSetting("Filter_Tongue_Speed", 1.0f)]
    private float _speedCutoff = 1.0f;

    public TongueFilterSettings(ILocalSettingsService localSettingsService)
    {
        localSettingsService.Load(this);
        PropertyChanged += (_, _) => localSettingsService.Save(this);
    }
}

public partial class NoseFilterSettings : ObservableObject, IFilterSettings
{
    [ObservableProperty]
    [property: SavedSetting("Filter_Nose_Enabled", false)]
    private bool _enabled;

    [ObservableProperty]
    [property: SavedSetting("Filter_Nose_MinFreq", 1.0f)]
    private float _minFreqCutoff = 1.0f;

    [ObservableProperty]
    [property: SavedSetting("Filter_Nose_Speed", 1.0f)]
    private float _speedCutoff = 1.0f;

    public NoseFilterSettings(ILocalSettingsService localSettingsService)
    {
        localSettingsService.Load(this);
        PropertyChanged += (_, _) => localSettingsService.Save(this);
    }
}

public partial class CheekFilterSettings : ObservableObject, IFilterSettings
{
    [ObservableProperty]
    [property: SavedSetting("Filter_Cheek_Enabled", false)]
    private bool _enabled;

    [ObservableProperty]
    [property: SavedSetting("Filter_Cheek_MinFreq", 1.0f)]
    private float _minFreqCutoff = 1.0f;

    [ObservableProperty]
    [property: SavedSetting("Filter_Cheek_Speed", 1.0f)]
    private float _speedCutoff = 1.0f;

    public CheekFilterSettings(ILocalSettingsService localSettingsService)
    {
        localSettingsService.Load(this);
        PropertyChanged += (_, _) => localSettingsService.Save(this);
    }
}