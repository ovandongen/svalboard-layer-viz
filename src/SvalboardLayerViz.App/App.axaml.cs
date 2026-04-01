using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.App.Views;

namespace SvalboardLayerViz.App;

public partial class App : Application
{
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
                if (mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
                    mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
            };

            // Bind tray icon commands to the main view model
            DataContext = viewModel;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
