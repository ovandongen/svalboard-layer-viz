using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using SvalboardLayerViz.App.ViewModels;

namespace SvalboardLayerViz.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Activate QMK settings sliders after UI layout is complete.
        // Avalonia Slider two-way bindings write 0 during DataContext init
        // before Min/Max apply — ActivateSliders() enables value writes
        // only after that noise has settled.
        if (DataContext is SettingsViewModel { QmkSettingsTab: { } tab })
            tab.ActivateSliders();
    }

    private void OnUpdateLinkClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && vm.UpdateUrl is { } url)
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
