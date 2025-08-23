using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Baballonia.Views;

public partial class FirmwareView : UserControl
{
    public FirmwareView()
    {
        InitializeComponent();
        WifiNameAutoComplete.MinimumPrefixLength = 0;
        WifiNameAutoComplete.MinimumPopulateDelay = TimeSpan.Zero;
    }
}
