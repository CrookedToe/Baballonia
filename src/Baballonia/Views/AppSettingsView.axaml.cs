using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Baballonia.Contracts;
using Baballonia.Services;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Baballonia.Views;

public partial class AppSettingsView : UserControl
{
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ILanguageSelectorService _languageSelectorService;
    private readonly IMainService _mainService;
    private readonly ComboBox _themeComboBox;
    private readonly ComboBox _langComboBox;
    private readonly ComboBox _faceSelectedMinFreqCutoffComboBox;
    private readonly ComboBox _faceSelectedSpeedCutoffComboxBox;
    private readonly NumericUpDown _faceSelectedMinFreqCutoffUpDown;
    private readonly NumericUpDown _faceSelectedSpeedCutoffUpDown;
    private readonly ComboBox _eyeSelectedMinFreqCutoffComboBox;
    private readonly ComboBox _eyeSelectedSpeedCutoffComboxBox;
    private readonly NumericUpDown _eyeSelectedMinFreqCutoffUpDown;
    private readonly NumericUpDown _eyeSelectedSpeedCutoffUpDown;

    public AppSettingsView()
    {
        InitializeComponent();

        _themeSelectorService = Ioc.Default.GetService<IThemeSelectorService>()!;
        _themeComboBox = this.Find<ComboBox>("ThemeCombo")!;
        _themeComboBox.SelectionChanged += ThemeComboBox_SelectionChanged;

        _languageSelectorService = Ioc.Default.GetService<ILanguageSelectorService>()!;
        _langComboBox = this.Find<ComboBox>("LangCombo")!;
        _langComboBox.SelectionChanged += LangComboBox_SelectionChanged;

        _faceSelectedMinFreqCutoffComboBox = this.Find<ComboBox>("FaceSelectedMinFreqCutoffComboBox")!;
        _faceSelectedSpeedCutoffComboxBox = this.Find<ComboBox>("FaceSelectedSpeedCutoffComboBox")!;
        _faceSelectedMinFreqCutoffUpDown = this.Find<NumericUpDown>("FaceSelectedMinFreqCutoffUpDown")!;
        _faceSelectedSpeedCutoffUpDown = this.Find<NumericUpDown>("FaceSelectedSpeedCutoffUpDown")!;
        
        _eyeSelectedMinFreqCutoffComboBox = this.Find<ComboBox>("EyeSelectedMinFreqCutoffComboBox")!;
        _eyeSelectedSpeedCutoffComboxBox = this.Find<ComboBox>("EyeSelectedSpeedCutoffComboBox")!;
        _eyeSelectedMinFreqCutoffUpDown = this.Find<NumericUpDown>("EyeSelectedMinFreqCutoffUpDown")!;
        _eyeSelectedSpeedCutoffUpDown = this.Find<NumericUpDown>("EyeSelectedSpeedCutoffUpDown")!;

        UpdateThemes();

        _mainService = Ioc.Default.GetService<IMainService>()!;

        if (_themeSelectorService.Theme is null)
        {
            _themeSelectorService.SetThemeAsync(ThemeVariant.Default);
            return;
        }

        if (string.IsNullOrEmpty(_languageSelectorService.Language))
        {
            _languageSelectorService.SetLanguageAsync(LanguageSelectorService.DefaultLanguage);
            return;
        }

        int index = _themeSelectorService.Theme.ToString() switch
        {
            "DefaultTheme" => 0,
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };
        _themeComboBox.SelectedIndex = index;

        index = _languageSelectorService.Language switch
        {
            "DefaultLanguage" => 0,
            "en" => 1,
            "es" => 2,
            "ja" => 3,
            "pl" => 4,
            "zh" => 5,
            _ => 0
        };
        _langComboBox.SelectedIndex = index;
    }

    ~AppSettingsView()
    {
        _themeComboBox.SelectionChanged -= ThemeComboBox_SelectionChanged;
    }

    private void ThemeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_themeComboBox.SelectedItem is not ComboBoxItem comboBoxItem)
            return;

        ThemeVariant variant = ThemeVariant.Default;
        variant = comboBoxItem!.Name switch
        {
            "DefaultTheme" => ThemeVariant.Default,
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => variant
        };
        Dispatcher.UIThread.InvokeAsync(async () => await _themeSelectorService.SetThemeAsync(variant));
    }

    private void LangComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var item = _langComboBox.SelectedItem as ComboBoxItem;
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await _languageSelectorService.SetLanguageAsync(item!.Name!);
        });
    }

    // Workaround for https://github.com/AvaloniaUI/Avalonia/issues/4460
    private void UpdateThemes()
    {
        var selectedIndex = _themeComboBox.SelectedIndex;
        _themeComboBox.Items.Clear();
        _themeComboBox.Items.Add(new ComboBoxItem { Content=Assets.Resources.Settings_Theme_Default_Content, Name="DefaultTheme" });
        _themeComboBox.Items.Add(new ComboBoxItem { Content=Assets.Resources.Settings_Theme_Light_Content, Name="Light" });
        _themeComboBox.Items.Add(new ComboBoxItem { Content=Assets.Resources.Settings_Theme_Dark_Content, Name="Dark" });
        _themeComboBox.SelectedIndex = selectedIndex;
    }

    private void LaunchFirstTimeSetUp(object? sender, RoutedEventArgs e)
    {
        switch (Application.Current?.ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                OnboardingView.ShowOnboarding(desktop.MainWindow!);
                break;
        }
    }

    // Face tracking filter event handlers
    private void FaceSelectedSpeedCutoffComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox) return;

        _faceSelectedSpeedCutoffUpDown.Value = comboBox.SelectedIndex switch
        {
            0 => 0.5m,
            1 => 1,
            2 => 2,
            _ => _faceSelectedSpeedCutoffUpDown.Value
        };
    }

    private void FaceSelectedMinFreqCutoffComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox) return;

        _faceSelectedMinFreqCutoffUpDown.Value = comboBox.SelectedIndex switch
        {
            0 => 0.5m,
            1 => 1,
            2 => 2,
            _ => _faceSelectedMinFreqCutoffUpDown.Value
        };
    }

    // Eye tracking filter event handlers
    private void EyeSelectedSpeedCutoffComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox) return;

        _eyeSelectedSpeedCutoffUpDown.Value = comboBox.SelectedIndex switch
        {
            0 => 0.1m,  // Very Low for eye tracking
            1 => 0.5m,  // Low
            2 => 1m,    // Medium
            _ => _eyeSelectedSpeedCutoffUpDown.Value
        };
    }

    private void EyeSelectedMinFreqCutoffComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox) return;

        _eyeSelectedMinFreqCutoffUpDown.Value = comboBox.SelectedIndex switch
        {
            0 => 0.1m,  // Very Low for eye tracking
            1 => 0.5m,  // Low
            2 => 1m,    // Medium
            _ => _eyeSelectedMinFreqCutoffUpDown.Value
        };
    }
}

