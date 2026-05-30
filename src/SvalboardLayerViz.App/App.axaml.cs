using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using SvalboardLayerViz.App.Localization;
using SvalboardLayerViz.App.Services;
using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.App.Views;
using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.Keymap.Builders;
using SvalboardLayerViz.Core.Settings;

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
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                DiagnosticLog.Error("Unhandled", $"AppDomain unhandled: {e.ExceptionObject}");
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                DiagnosticLog.Error("Unhandled", $"Unobserved task: {e.Exception}");
                e.SetObserved();
            };
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                DiagnosticLog.Error("Unhandled", $"UI dispatcher: {e.Exception}");
                e.Handled = true;
            };

            var settingsService = new SettingsService();
            var initialSettings = settingsService.Load();
            Loc.Instance.SetCulture(initialSettings.Language);

            // Point Core's builder localization at App's ResourceManager so Mod-Tap,
            // Layer-Tap, etc. read their display names and help text from Strings.resx.
            // Delegates through Loc so culture changes at runtime are honored.
            BuilderLocalization.Resolve = key => Loc.Instance[key];

            // Apply log level from settings (env var overrides saved setting)
            var envLevel = Environment.GetEnvironmentVariable("SVAL_LOG_LEVEL");
            if (!string.IsNullOrEmpty(envLevel) && Enum.TryParse<LogLevel>(envLevel, true, out var envLogLevel))
                DiagnosticLog.SetMinimumLevel(envLogLevel);
            else if (Enum.TryParse<LogLevel>(initialSettings.LogLevel, true, out var logLevel))
                DiagnosticLog.SetMinimumLevel(logLevel);

            DiagnosticLog.Info("Startup", "Creating MainWindowViewModel...");
            var viewModel = new MainWindowViewModel(settingsService);
            DiagnosticLog.Info("Startup", "MainWindowViewModel created");
            var mainWindow = new MainWindow { DataContext = viewModel };
            DiagnosticLog.Info("Startup", "MainWindow created");
            desktop.MainWindow = mainWindow;

            // Host services own all dialog + window-lifecycle wiring; the VM only
            // talks to the two interfaces (IDialogService / IShellService). The
            // hotkey is reached through a late accessor — it's created below.
            var dialogs = new DesktopDialogService(viewModel, mainWindow, settingsService);
            var shell = new DesktopShellService(viewModel, mainWindow, settingsService, () => _hotkeyService);
            viewModel.AttachHost(dialogs, shell);

            // Restore saved window position/size (or center on first launch)
            if (initialSettings.WindowX.HasValue && initialSettings.WindowY.HasValue)
            {
                mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                mainWindow.Position = new PixelPoint((int)initialSettings.WindowX.Value, (int)initialSettings.WindowY.Value);
            }
            else
            {
                mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            if (initialSettings.WindowWidth.HasValue)
                mainWindow.Width = initialSettings.WindowWidth.Value;
            if (initialSettings.WindowHeight.HasValue)
                mainWindow.Height = initialSettings.WindowHeight.Value;

            DiagnosticLog.Info("Startup", $"Window position: {mainWindow.Position.X},{mainWindow.Position.Y} size: {mainWindow.Width}x{mainWindow.Height} startup: {mainWindow.WindowStartupLocation}");

            // Validate restored position is on a visible screen
            mainWindow.Opened += (_, _) =>
            {
                if (mainWindow.Screens.ScreenFromWindow(mainWindow) is null)
                {
                    mainWindow.Position = new PixelPoint(0, 0);
                }
            };

            // Save on normal window close, and tear down device-facing resources.
            // Without the Shutdown call here, closing via the title-bar X bypasses
            // the Quit command path and leaves polling threads + the HID stream
            // alive against a disposed VM. QuitCommand uses Environment.Exit (which
            // skips Closing) and persists geometry itself — see DesktopShellService.
            var closingHandled = false;
            mainWindow.Closing += async (sender, e) =>
            {
                if (closingHandled) return;
                closingHandled = true;
                e.Cancel = true;
                shell.SaveWindowState();
                await viewModel.ShutdownAsync();
                ((Window)sender!).Close();
            };

            // Set tray icon from embedded PNG and localize tray menu
            var trayIcons = TrayIcon.GetIcons(this);
            if (trayIcons?.Count > 0)
            {
                trayIcons[0].Icon = new WindowIcon(
                    AssetLoader.Open(new Uri("avares://SvalboardLayerViz.App/Assets/icon.png")));
                LocalizeTrayMenu(trayIcons[0]);
                Loc.CultureChanged += () =>
                    Dispatcher.UIThread.Post(() => LocalizeTrayMenu(trayIcons[0]));
            }

            // Global hotkey — not supported on Linux (Wayland blocks hooks from unfocused windows)
            DiagnosticLog.Info("Startup", "Starting global hotkey service...");
            if (!OperatingSystem.IsLinux())
            {
                _hotkeyService = new GlobalHotkeyService();
                try
                {
                    _hotkeyService.Key = GlobalHotkeyService.ParseKey(initialSettings.HotkeyKey);
                    _hotkeyService.Modifiers = GlobalHotkeyService.ParseModifiers(initialSettings.HotkeyModifiers);
                }
                catch
                {
                    // Invalid saved hotkey — use defaults
                }
                _hotkeyService.HotkeyPressed = () =>
                {
                    // SharpHook fires on its own thread — dispatch to UI thread
                    Dispatcher.UIThread.Post(() => shell.ToggleWindow());
                };
                _hotkeyService.Start();

                desktop.Exit += (_, _) => _hotkeyService.Dispose();
            }

            // Show help on first launch
            if (!initialSettings.HasSeenHelp)
            {
                Dispatcher.UIThread.Post(() => dialogs.OpenHelp(),
                    DispatcherPriority.Background);
            }

            // Connect to device after the window is shown — HID enumeration can hang
            // on some Windows systems, so it must not block window creation.
            Dispatcher.UIThread.Post(() => viewModel.InitializeDeviceConnection(),
                DispatcherPriority.Background);

            // Bind tray icon commands to the main view model
            DataContext = viewModel;
            DiagnosticLog.Info("Startup", "OnFrameworkInitializationCompleted done");
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Maps tray menu keys to resource string keys for localization.</summary>
    private static readonly (string Key, string ResKey)[] TrayMenuKeys =
    [
        ("Show", "Tray_ShowLayers"),
        ("Quit", "Tray_Quit"),
    ];

    private static void LocalizeTrayMenu(TrayIcon trayIcon)
    {
        trayIcon.ToolTipText = Loc.Instance["Tray_Tooltip"];
        if (trayIcon.Menu is not { } menu) return;

        var menuItems = menu.Items.OfType<NativeMenuItem>().ToList();
        for (var i = 0; i < menuItems.Count && i < TrayMenuKeys.Length; i++)
            menuItems[i].Header = Loc.Instance[TrayMenuKeys[i].ResKey];
    }
}
