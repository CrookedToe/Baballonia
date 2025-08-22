using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Baballonia.Helpers;
using Baballonia.ViewModels.SplitViewPane;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Baballonia.Views;

public partial class HomePageView : UserControl
{
    private bool _isLayoutUpdating;

    public HomePageView()
    {
        InitializeComponent();

        if (Utils.IsSupportedDesktopOS)
        {
            SizeChanged += (_, _) =>
            {
                if (this.GetVisualRoot() is not Window window) return;

                var grid = this.FindControl<Grid>("CameraControlsGrid");
                var isVertical = window.ClientSize.Width < Utils.MobileWidth;

                // Clear existing row/column definitions
                grid!.RowDefinitions.Clear();
                grid.ColumnDefinitions.Clear();

                if (isVertical)
                {
                    // Vertical layout - one column, three rows
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

                    // Set grid positions for all children
                    for (var i = 0; i < grid.Children.Count; i++)
                    {
                        var child = grid.Children[i];
                        Grid.SetRow(child, i);
                        Grid.SetColumn(child, 0);
                        child.Margin = new Avalonia.Thickness(0, 0, 0, 16);
                    }
                }
                else
                {
                    // Horizontal layout - three columns, one row
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

                    // Set grid positions for all children
                    for (var i = 0; i < grid.Children.Count; i++)
                    {
                        var child = grid.Children[i];
                        Grid.SetRow(child, 0);
                        Grid.SetColumn(child, i);
                        child.Margin = new Avalonia.Thickness(0, 0, i < 2 ? 12 : 0, 0);
                    }
                }
            };
        }
        Loaded += (_, _) =>
        {
            if (DataContext is not HomePageViewModel vm) return;

            SetupCropEvents(vm.LeftCamera, LeftMouthWindow);
            SetupCropEvents(vm.RightCamera, RightMouthWindow);
            SetupCropEvents(vm.FaceCamera, FaceWindow);
            EyeAddressEntry_OnTextChanged(null, null!);
            FaceAddressEntry_OnTextChanged(null, null!);

            vm.SelectedCalibrationText = "Eye Calibration";
        };
    }

    private void SetupCropEvents(HomePageViewModel.CameraControllerModel model, Image image)
    {
        // in theory should be cleaned up by the GC so no need to manually unsubscribe
        image.PointerPressed += (sender, e) =>
        {
            if (model.Controller.CamViewMode != CamViewMode.Cropping) return;
            var pos = e.GetPosition(image);
            model.Controller.CropManager.StartCrop(pos);
            model.OverlayRectangle = model.Controller.CropManager.CropZone.GetRect();
        };
        image.PointerMoved += (sender, e) =>
        {
            if (model.Controller.CamViewMode != CamViewMode.Cropping) return;

            var pos = e.GetPosition(image);
            model.Controller.CropManager.UpdateCrop(pos);
            model.OverlayRectangle = model.Controller.CropManager.CropZone.GetRect();
        };
        image.PointerReleased += (sender, e) =>
        {
            if (model.Controller.CamViewMode != CamViewMode.Cropping) return;

            model.Controller.CropManager.EndCrop();
            model.OverlayRectangle = model.Controller.CropManager.CropZone.GetRect();
        };
    }

    private void EyeAddressEntry_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is not HomePageViewModel vm) return;

        if (!string.IsNullOrEmpty(vm.LeftCamera.DisplayAddress) &&
            !string.IsNullOrEmpty(vm.RightCamera.DisplayAddress))
        {
            if (vm.LeftCamera.DisplayAddress.Length == 0)
            {
                LeftAddressHint.Text = "Please enter addresses for both eyes before starting!";
                vm.LeftCamera.HintEnabled = true;
                vm.LeftCamera.InferEnabled = false;
            }

            if (vm.RightCamera.DisplayAddress.Length == 0)
            {
                RightAddressHint.Text = "Please enter addresses for both eyes before starting!";
                vm.RightCamera.HintEnabled = true;
                vm.RightCamera.InferEnabled = false;
            }

            if (vm.LeftCamera.DisplayAddress.Length > 0 && vm.RightCamera.DisplayAddress.Length > 0)
            {
                vm.LeftCamera.HintEnabled = false;
                vm.RightCamera.HintEnabled = false;
                vm.LeftCamera.InferEnabled = true;
                vm.RightCamera.InferEnabled = true;
            }
        }
    }

    private void FaceAddressEntry_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (this.DataContext is not HomePageViewModel vm) return;

        if (vm.FaceCamera.DisplayAddress != null)
        {
            vm.FaceCamera.InferEnabled = vm.FaceCamera.DisplayAddress.Length > 0;
        }
    }

    private void OnCalibrationMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && DataContext is HomePageViewModel vm)
        {
            vm.SelectedCalibrationText = menuItem.Header?.ToString() ?? "";
        }
    }

    private void OnExpanderCollapsed(object? sender, RoutedEventArgs e)
    {
        if (_isLayoutUpdating) return;
        _isLayoutUpdating = true;

        // Force layout update
        InvalidateArrange();
        InvalidateMeasure();

        _isLayoutUpdating = false;
    }

    private void OnExpanderExpanded(object? sender, RoutedEventArgs e)
    {
        if (_isLayoutUpdating) return;
        _isLayoutUpdating = true;

        // Force layout update
        InvalidateArrange();
        InvalidateMeasure();

        _isLayoutUpdating = false;
    }
}
