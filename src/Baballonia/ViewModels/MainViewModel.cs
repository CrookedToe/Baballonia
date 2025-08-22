using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Baballonia.Views;
using Baballonia.Models;
using Baballonia.ViewModels.SplitViewPane;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Baballonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public MainViewModel(IMessenger messenger)
    {
        Items = Utils.IsSupportedDesktopOS ?
            new ObservableCollection<ListItemTemplate>(_desktopTemplates) :
            new ObservableCollection<ListItemTemplate>(_mobileTemplates);

        SelectedListItem = Items.First(vm => vm.ModelType == typeof(HomePageViewModel));
    }

    private readonly List<ListItemTemplate> _desktopTemplates =
    [
        new(typeof(HomePageViewModel), "HomeRegular", "Home"),
        new(typeof(CalibrationViewModel), "EditRegular", "Calibration"),
        new(typeof(FirmwareViewModel), "DeveloperBoardRegular", "Firmware"),
        new(typeof(OutputPageViewModel), "TextFirstLineRegular", "Output"),
        new(typeof(AppSettingsViewModel), "SettingsRegular", "Settings"),
    ];

    private readonly List<ListItemTemplate> _mobileTemplates =
    [
        new(typeof(HomePageViewModel), "HomeRegular", "Home"),
        new(typeof(CalibrationViewModel), "EditRegular", "Calibration"),
        new(typeof(OutputPageViewModel), "TextFirstLineRegular", "Output"),
        new(typeof(AppSettingsViewModel), "SettingsRegular", "Settings"),
    ];

    public MainViewModel() : this(new WeakReferenceMessenger()) { }

    [ObservableProperty]
    private bool _isPaneOpen;

    [ObservableProperty] private ViewModelBase _currentPage;

    [ObservableProperty]
    private ListItemTemplate? _selectedListItem;

    partial void OnSelectedListItemChanged(ListItemTemplate? value)
    {
        if (value is null) return;

        var vm = Design.IsDesignMode
            ? Activator.CreateInstance(value.ModelType)
            : CreateInstance(value.ModelType); // Manual creation

        if (vm is not ViewModelBase vmb) return;

        var tmp = CurrentPage;
        CurrentPage = vmb;

        if (tmp is IDisposable disposable)
            disposable.Dispose();
    }
    private object CreateInstance(Type type)
    {
        // Manually resolve dependencies without container tracking
        var constructors = type.GetConstructors();
        var constructor = constructors.First();
        var parameters = constructor.GetParameters()
            .Select(p => Ioc.Default.GetService(p.ParameterType))
            .ToArray();
        return Activator.CreateInstance(type, parameters);
    }

    public ObservableCollection<ListItemTemplate> Items { get; }

    [RelayCommand]
    private void TriggerPane()
    {
        IsPaneOpen = !IsPaneOpen;
    }
}
