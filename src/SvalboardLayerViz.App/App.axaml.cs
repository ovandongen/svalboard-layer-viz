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
using SvalboardLayerViz.Core.Export;
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
            var settingsService = new SettingsService();
            Loc.Instance.SetCulture(settingsService.Load().Language);
            var viewModel = new MainWindowViewModel(settingsService);
            var mainWindow = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = mainWindow;

            // Restore saved window position/size (or center on first launch)
            var windowSettings = settingsService.Load();
            if (windowSettings.WindowX.HasValue && windowSettings.WindowY.HasValue)
            {
                mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                mainWindow.Position = new PixelPoint((int)windowSettings.WindowX.Value, (int)windowSettings.WindowY.Value);
            }
            else
            {
                mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            if (windowSettings.WindowWidth.HasValue)
                mainWindow.Width = windowSettings.WindowWidth.Value;
            if (windowSettings.WindowHeight.HasValue)
                mainWindow.Height = windowSettings.WindowHeight.Value;

            // Validate restored position is on a visible screen
            mainWindow.Opened += (_, _) =>
            {
                if (mainWindow.Screens.ScreenFromWindow(mainWindow) is null)
                {
                    mainWindow.Position = new PixelPoint(0, 0);
                }
            };

            // Helper to persist current window geometry
            void SaveWindowState()
            {
                if (mainWindow.WindowState != WindowState.Minimized)
                {
                    var s = settingsService.Load();
                    settingsService.Save(s with
                    {
                        WindowX = mainWindow.Position.X,
                        WindowY = mainWindow.Position.Y,
                        WindowWidth = mainWindow.Width,
                        WindowHeight = mainWindow.Height,
                    });
                }
            }

            // Save on normal window close
            mainWindow.Closing += (_, _) => SaveWindowState();

            // QuitCommand uses Environment.Exit which skips Closing — intercept it
            viewModel.QuitRequested = () =>
            {
                SaveWindowState();
                Environment.Exit(0);
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

            viewModel.OpenSettingsRequested = () =>
            {
                var totalLayers = viewModel.KeyboardConfig?.Layers.Count ?? 8;
                var unknowns = viewModel.GetUnknownKeycodes();
                var settingsVm = new SettingsViewModel(settingsService, totalLayers, unknowns,
                    viewModel.DeviceTappingTermMs);

                var settingsWindow = new SettingsWindow { DataContext = settingsVm, Topmost = viewModel.IsAlwaysOnTop };
                settingsVm.SettingsSaved = () =>
                {
                    try
                    {
                        viewModel.ApplySettings();
                    }
                    catch (Exception ex)
                    {
                        viewModel.StatusMessage = Loc.Instance.Format("Status_SettingsErrorFormat", ex.Message);
                    }
                    settingsWindow.Close();
                };
                settingsVm.Cancelled = () => settingsWindow.Close();

                settingsWindow.ShowDialog(mainWindow);
            };

            viewModel.SetKeyLabelRequested = async (keyVm) =>
            {
                var dialog = new Window
                {
                    Title = Loc.Instance.Format("Title_SetLabelFormat", keyVm.HexKeycode),
                    Width = 350,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    Topmost = viewModel.IsAlwaysOnTop,
                };

                var textBox = new TextBox
                {
                    Watermark = Loc.Instance["Key_LabelWatermark"],
                    Text = keyVm.Key.IsUnknown ? "" : keyVm.DisplayLabel,
                    Margin = new Thickness(0, 0, 0, 8),
                };

                var saveBtn = new Button { Content = Loc.Instance["Common_Save"], HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
                var info = new TextBlock
                {
                    Text = Loc.Instance.Format("Key_LabelInfoFormat", keyVm.HexKeycode, keyVm.DisplayLabel),
                    Foreground = Avalonia.Media.Brushes.Gray,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 8),
                };

                var panel = new StackPanel { Margin = new Thickness(16) };
                panel.Children.Add(info);
                panel.Children.Add(textBox);
                panel.Children.Add(saveBtn);
                dialog.Content = panel;

                saveBtn.Click += (_, _) =>
                {
                    viewModel.SaveCustomKeyLabel(keyVm.HexKeycode, textBox.Text ?? "");
                    dialog.Close();
                };

                await dialog.ShowDialog(mainWindow);
            };

            DiagnosticsWindow? diagnosticsWindow = null;
            viewModel.OpenDiagnosticsRequested = () =>
            {
                if (diagnosticsWindow is { IsVisible: true })
                {
                    diagnosticsWindow.Activate();
                    return;
                }

                viewModel.Diagnostics.IsActive = true;
                diagnosticsWindow = new DiagnosticsWindow { DataContext = viewModel.Diagnostics, Topmost = viewModel.IsAlwaysOnTop };
                diagnosticsWindow.Closed += (_, _) =>
                {
                    viewModel.Diagnostics.IsActive = false;
                    diagnosticsWindow = null;
                };
                diagnosticsWindow.Show(mainWindow);
            };

            viewModel.OpenExportRequested = async () =>
            {
                if (viewModel.KeyboardConfig is null) return;

                var exportVm = new ExportDialogViewModel(viewModel.Layers.ToList());
                var exportDialog = new ExportDialog { DataContext = exportVm, Topmost = viewModel.IsAlwaysOnTop };

                exportVm.Cancelled = () => exportDialog.Close();
                exportVm.ExportRequested = async () =>
                {
                    var format = exportVm.SelectedFormat;
                    var ext = format switch
                    {
                        ExportFormat.Png => "png",
                        ExportFormat.Pdf => "pdf",
                        ExportFormat.Svg => "svg",
                        _ => "png"
                    };
                    var formatName = format.ToString().ToUpperInvariant();

                    var storageProvider = mainWindow.StorageProvider;
                    var file = await storageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                    {
                        Title = Loc.Instance["Export_SaveDialogTitle"],
                        DefaultExtension = ext,
                        SuggestedFileName = $"svalboard-layout.{ext}",
                        FileTypeChoices =
                        [
                            new Avalonia.Platform.Storage.FilePickerFileType(formatName)
                            {
                                Patterns = [$"*.{ext}"]
                            }
                        ],
                    });

                    if (file is null) return;
                    var path = file.Path.LocalPath;

                    try
                    {
                        var userSettings = settingsService.Load();
                        var userColors = userSettings.LayerColors.Count > 0
                            ? userSettings.LayerColors
                            : null;

                        var options = exportVm.BuildOptions(path);
                        ExportService.Export(options, viewModel.KeyboardConfig.Layers,
                            viewModel.KeyboardConfig.Layers.Count, userColors);

                        viewModel.StatusMessage = Loc.Instance.Format("Status_ExportedFormat", Path.GetFileName(path));
                        exportDialog.Close();
                    }
                    catch (Exception ex)
                    {
                        viewModel.StatusMessage = Loc.Instance.Format("Status_ExportErrorFormat", ex.Message);
                    }
                };

                await exportDialog.ShowDialog(mainWindow);
            };

            HelpWindow? helpWindow = null;
            viewModel.OpenHelpRequested = () =>
            {
                if (helpWindow is { IsVisible: true })
                {
                    helpWindow.Activate();
                    return;
                }

                var helpVm = new HelpWindowViewModel();
                helpWindow = new HelpWindow { DataContext = helpVm, Topmost = viewModel.IsAlwaysOnTop };
                helpVm.Closed = () =>
                {
                    if (helpVm.DontShowAgain)
                        settingsService.Save(settingsService.Load() with { HasSeenHelp = true });
                    helpWindow.Close();
                    helpWindow = null;
                };
                helpWindow.Closed += (_, _) => helpWindow = null;
                helpWindow.Show(mainWindow);
            };

            viewModel.HotkeyChangeRequested = (key, modifiers) =>
            {
                try
                {
                    _hotkeyService?.UpdateHotkey(
                        GlobalHotkeyService.ParseKey(key),
                        GlobalHotkeyService.ParseModifiers(modifiers));
                }
                catch
                {
                    // Invalid key/modifier — keep existing hotkey
                }
            };

            // Global hotkey — not supported on Linux (Wayland blocks hooks from unfocused windows)
            if (!OperatingSystem.IsLinux())
            {
                var settings = settingsService.Load();
                _hotkeyService = new GlobalHotkeyService();
                try
                {
                    _hotkeyService.Key = GlobalHotkeyService.ParseKey(settings.HotkeyKey);
                    _hotkeyService.Modifiers = GlobalHotkeyService.ParseModifiers(settings.HotkeyModifiers);
                }
                catch
                {
                    // Invalid saved hotkey — use defaults
                }
                _hotkeyService.HotkeyPressed = () =>
                {
                    // SharpHook fires on its own thread — dispatch to UI thread
                    Dispatcher.UIThread.Post(() => viewModel.ToggleWindowRequested?.Invoke());
                };
                _hotkeyService.Start();

                desktop.Exit += (_, _) => _hotkeyService.Dispose();
            }

            // Show help on first launch
            var currentSettings = settingsService.Load();
            if (!currentSettings.HasSeenHelp)
            {
                Dispatcher.UIThread.Post(() => viewModel.OpenHelpRequested?.Invoke(),
                    DispatcherPriority.Background);
            }

            // Bind tray icon commands to the main view model
            DataContext = viewModel;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Maps tray menu keys to resource string keys for localization.</summary>
    private static readonly (string Key, string ResKey)[] TrayMenuKeys =
    [
        ("Show", "Tray_ShowLayers"),
        ("Refresh", "Tray_Refresh"),
        ("Settings", "Tray_Settings"),
        ("Help", "Tray_Help"),
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
