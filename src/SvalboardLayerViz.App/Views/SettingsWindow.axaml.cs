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
    }

    private void OnUpdateLinkClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && vm.UpdateUrl is { } url)
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
