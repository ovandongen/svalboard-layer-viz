using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SvalboardLayerViz.App.Services;
using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.App.Views;

namespace SvalboardLayerViz.App;

public partial class App : Application
{
    private GlobalHotkeyService? _hotkeyService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();
            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            desktop.MainWindow = mainWindow;

            viewModel.ShowWindowRequested = () =>
            {
                mainWindow.Show();
                mainWindow.Activate();
                if (mainWindow.WindowState == WindowState.Minimized)
                    mainWindow.WindowState = WindowState.Normal;
            };

            viewModel.ToggleWindowRequested = () =>
            {
                if (mainWindow.IsVisible)
                {
                    mainWindow.Hide();
                }
                else
                {
                    mainWindow.Show();
                    mainWindow.Activate();
                    if (mainWindow.WindowState == WindowState.Minimized)
                        mainWindow.WindowState = WindowState.Normal;
                }
            };

            // Global hotkey: Ctrl+F12 toggles the overlay
            _hotkeyService = new GlobalHotkeyService();
            _hotkeyService.HotkeyPressed = () =>
            {
                // SharpHook fires on its own thread — dispatch to UI thread
                Dispatcher.UIThread.Post(() => viewModel.ToggleWindowRequested?.Invoke());
            };
            _hotkeyService.Start();

            desktop.Exit += (_, _) => _hotkeyService.Dispose();

            // Bind tray icon commands to the main view model
            DataContext = viewModel;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
