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
using SvalboardLayerViz.Core.Export;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Keymap.Builders;
using SvalboardLayerViz.Core.Macros;
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

            // Save on normal window close, and tear down device-facing resources.
            // Without the Shutdown call here, closing via the title-bar X bypasses
            // the Quit command path and leaves polling threads + the HID stream
            // alive against a disposed VM. Async cancel-then-close so future
            // async cleanup in ShutdownAsync is actually awaited (today's body
            // is sync-complete, but this keeps the contract forward-safe).
            var closingHandled = false;
            mainWindow.Closing += async (sender, e) =>
            {
                if (closingHandled) return;
                closingHandled = true;
                e.Cancel = true;
                SaveWindowState();
                await viewModel.ShutdownAsync();
                ((Window)sender!).Close();
            };

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

            viewModel.OpenSettingsRequested = (int? initialTabIndex) =>
            {
                var totalLayers = viewModel.KeyboardConfig?.Layers.Count ?? 8;
                var unknowns = viewModel.GetUnknownKeycodes();

                QmkSettingsTabViewModel? qmkTab = null;
                if (viewModel.KeyboardConfig?.QmkSettings is { Count: > 0 } qmkSettings)
                {
                    qmkTab = new QmkSettingsTabViewModel(
                        qmkSettings,
                        viewModel.IsEditMode,
                        viewModel.IsEditMode ? viewModel.ApplySettingEdit : null);
                }

                var settingsVm = new SettingsViewModel(settingsService, totalLayers, unknowns,
                    viewModel.DeviceTappingTermMs, viewModel.KeyboardConfig?.Layers, qmkTab);
                if (initialTabIndex is int tab)
                    settingsVm.SelectedTabIndex = tab;

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

            viewModel.OpenHistoryRequested = async () =>
            {
                DiagnosticLog.Info("History", "Open history window requested");
                var historyVm = new HistoryWindowViewModel(
                    viewModel.SnapshotService,
                    viewModel.CurrentKeyboardId,
                    viewModel.GetDeviceKeymapForDiff(),
                    new KeycodeService(),
                    viewModel.GetDeviceMacroBufferForDiff(),
                    viewModel.GetDeviceCombosForDiff(),
                    viewModel.GetDeviceTapDancesForDiff());
                historyVm.IsDeviceConnected = viewModel.IsConnected;
                historyVm.HasPendingChanges = viewModel.DirtyCount > 0;
                var historyWindow = new HistoryWindow { DataContext = historyVm, Topmost = viewModel.IsAlwaysOnTop };

                historyVm.RequestExportFilePath = async suggested =>
                {
                    var file = await mainWindow.StorageProvider.SaveFilePickerAsync(
                        new Avalonia.Platform.Storage.FilePickerSaveOptions
                        {
                            Title = Loc.Instance["History_ExportFileTitle"],
                            DefaultExtension = "json",
                            SuggestedFileName = suggested,
                            FileTypeChoices =
                            [
                                new Avalonia.Platform.Storage.FilePickerFileType("Snapshot JSON")
                                {
                                    Patterns = ["*.json"]
                                }
                            ],
                        });
                    return file?.Path.LocalPath;
                };

                historyVm.RequestExportFolderPath = async () =>
                {
                    var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(
                        new Avalonia.Platform.Storage.FolderPickerOpenOptions
                        {
                            Title = Loc.Instance["History_ExportFolderTitle"],
                            AllowMultiple = false,
                        });
                    return folders.Count > 0 ? folders[0].Path.LocalPath : null;
                };

                historyVm.RequestImportFolderPath = async () =>
                {
                    var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(
                        new Avalonia.Platform.Storage.FolderPickerOpenOptions
                        {
                            Title = Loc.Instance["History_ImportFolderTitle"],
                            AllowMultiple = false,
                        });
                    return folders.Count > 0 ? folders[0].Path.LocalPath : null;
                };

                historyVm.ConfirmDelete = async (label) =>
                {
                    var dialog = new Window
                    {
                        Title = Loc.Instance["Title_History"],
                        Width = 380,
                        Height = 140,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        CanResize = false,
                        Topmost = viewModel.IsAlwaysOnTop,
                    };
                    var result = false;
                    var message = new TextBlock
                    {
                        Text = Loc.Instance.Format("History_DeleteConfirm", label),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 12),
                    };
                    var cancelBtn = new Button
                    {
                        Content = Loc.Instance["Common_Cancel"],
                        Margin = new Thickness(0, 0, 8, 0),
                    };
                    var deleteBtn = new Button
                    {
                        Content = Loc.Instance["History_DeleteRow"],
                    };
                    cancelBtn.Click += (_, _) => dialog.Close();
                    deleteBtn.Click += (_, _) => { result = true; dialog.Close(); };
                    var buttons = new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    };
                    buttons.Children.Add(cancelBtn);
                    buttons.Children.Add(deleteBtn);
                    var panel = new StackPanel { Margin = new Thickness(16) };
                    panel.Children.Add(message);
                    panel.Children.Add(buttons);
                    dialog.Content = panel;
                    await dialog.ShowDialog(historyWindow);
                    return result;
                };

                historyVm.ShowImportSummary = async (result) =>
                {
                    var dialog = new Window
                    {
                        Title = Loc.Instance["Title_History"],
                        Width = 380,
                        Height = 160,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        CanResize = false,
                        Topmost = viewModel.IsAlwaysOnTop,
                    };
                    var summary = new TextBlock
                    {
                        Text = Loc.Instance.Format("History_ImportSummary",
                            result.Imported, result.SkippedDuplicates, result.RefusedWrongKeyboard),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 12),
                    };
                    var okBtn = new Button
                    {
                        Content = Loc.Instance["Common_Close"],
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    };
                    okBtn.Click += (_, _) => dialog.Close();
                    var panel = new StackPanel { Margin = new Thickness(16) };
                    panel.Children.Add(summary);
                    panel.Children.Add(okBtn);
                    dialog.Content = panel;
                    await dialog.ShowDialog(historyWindow);
                };

                historyVm.OpenDiffDialog = async (diffVm) =>
                {
                    var diffDialog = new SnapshotDiffDialog
                    {
                        DataContext = diffVm,
                        Topmost = viewModel.IsAlwaysOnTop,
                    };

                    // Reuse the same file picker pattern for export
                    diffVm.RequestExportFilePath = async suggested =>
                    {
                        var file = await mainWindow.StorageProvider.SaveFilePickerAsync(
                            new Avalonia.Platform.Storage.FilePickerSaveOptions
                            {
                                Title = Loc.Instance["History_ExportFileTitle"],
                                DefaultExtension = "json",
                                SuggestedFileName = suggested,
                                FileTypeChoices =
                                [
                                    new Avalonia.Platform.Storage.FilePickerFileType("Snapshot JSON")
                                    {
                                        Patterns = ["*.json"]
                                    }
                                ],
                            });
                        if (file?.Path.LocalPath is { } path)
                        {
                            await viewModel.SnapshotService.ExportAsync(diffVm.Snapshot, path);
                            return path;
                        }
                        return null;
                    };

                    diffVm.ConfirmDelete = async (label) =>
                    {
                        var dialog = new Window
                        {
                            Title = Loc.Instance["Diff_Title"],
                            Width = 380,
                            Height = 140,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            CanResize = false,
                            Topmost = viewModel.IsAlwaysOnTop,
                        };
                        var result = false;
                        var message = new TextBlock
                        {
                            Text = Loc.Instance.Format("History_DeleteConfirm", label),
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 0, 12),
                        };
                        var cancelBtn = new Button
                        {
                            Content = Loc.Instance["Common_Cancel"],
                            Margin = new Thickness(0, 0, 8, 0),
                        };
                        var deleteBtn = new Button
                        {
                            Content = Loc.Instance["History_DeleteRow"],
                        };
                        cancelBtn.Click += (_, _) => dialog.Close();
                        deleteBtn.Click += (_, _) => { result = true; dialog.Close(); };
                        var buttons = new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        };
                        buttons.Children.Add(cancelBtn);
                        buttons.Children.Add(deleteBtn);
                        var panel = new StackPanel { Margin = new Thickness(16) };
                        panel.Children.Add(message);
                        panel.Children.Add(buttons);
                        dialog.Content = panel;
                        await dialog.ShowDialog(diffDialog);
                        if (result)
                        {
                            await viewModel.SnapshotService.DeleteAsync(diffVm.Snapshot.CapturedAt.ToString("o"));
                        }
                        return result;
                    };

                    diffVm.ConfirmRestore = async () =>
                    {
                        var dialog = new Window
                        {
                            Title = Loc.Instance["Diff_Title"],
                            Width = 420,
                            Height = 140,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            CanResize = false,
                            Topmost = viewModel.IsAlwaysOnTop,
                        };
                        var result = false;
                        var message = new TextBlock
                        {
                            Text = Loc.Instance.Format("Diff_RestoreConfirm", diffVm.Snapshot.CapturedAt.ToString("g")),
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 0, 12),
                        };
                        var cancelBtn = new Button
                        {
                            Content = Loc.Instance["Common_Cancel"],
                            Margin = new Thickness(0, 0, 8, 0),
                        };
                        var restoreBtn = new Button
                        {
                            Content = Loc.Instance["History_RestoreRow"],
                        };
                        cancelBtn.Click += (_, _) => dialog.Close();
                        restoreBtn.Click += (_, _) => { result = true; dialog.Close(); };
                        var buttons = new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        };
                        buttons.Children.Add(cancelBtn);
                        buttons.Children.Add(restoreBtn);
                        var panel = new StackPanel { Margin = new Thickness(16) };
                        panel.Children.Add(message);
                        panel.Children.Add(buttons);
                        dialog.Content = panel;
                        await dialog.ShowDialog(diffDialog);
                        return result;
                    };

                    diffVm.CloseRequested = () => diffDialog.Close();

                    await diffDialog.ShowDialog(historyWindow);
                };

                historyVm.RestoreCompleted = session =>
                {
                    historyWindow.Close();
                    viewModel.RestoreFromSnapshot(session);
                };

                historyVm.CloseRequested = () => historyWindow.Close();
                await historyVm.LoadAsync();
                await historyWindow.ShowDialog(mainWindow);
            };

            // Task-state gates prevent duplicate editor dialogs when
            // OpenXxxRequested fires twice before ShowDialog takes the modal
            // lock (e.g. rapid toolbar clicks or key-binding repeat). Faulted
            // tasks intentionally don't block re-entry — a previously thrown
            // dialog should allow a retry on the next click — but the fault
            // is observed so the exception surfaces in logs instead of
            // vanishing through the discard.
            Task? macroEditorTask = null;
            viewModel.OpenMacrosRequested = () =>
            {
                if (macroEditorTask is { IsCompleted: false }) return;
                macroEditorTask = EditorDialogFactory.ShowMacroEditorAsync(viewModel, mainWindow);
                ObserveFaults(macroEditorTask, "Macro editor");
            };

            Task? comboEditorTask = null;
            viewModel.OpenCombosRequested = () =>
            {
                if (comboEditorTask is { IsCompleted: false }) return;
                comboEditorTask = EditorDialogFactory.ShowComboEditorAsync(viewModel, mainWindow);
                ObserveFaults(comboEditorTask, "Combo editor");
            };

            Task? tapDanceEditorTask = null;
            viewModel.OpenTapDanceRequested = () =>
            {
                if (tapDanceEditorTask is { IsCompleted: false }) return;
                tapDanceEditorTask = EditorDialogFactory.ShowTapDanceEditorAsync(viewModel, mainWindow);
                ObserveFaults(tapDanceEditorTask, "Tap-dance editor");
            };

            viewModel.RequestManualSnapshotLabel = () =>
                EditorDialogFactory.PromptSnapshotLabelAsync(viewModel, mainWindow);

            viewModel.SetKeyLabelRequested = keyVm =>
                ObserveFaults(EditorDialogFactory.PromptKeyLabelAsync(viewModel, mainWindow, keyVm), "Key label prompt");

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

            viewModel.OpenUnlockRequested = onUnlocked =>
            {
                async void Run()
                {
                    var unlockVm = new UnlockDialogViewModel(
                        viewModel.ProtocolService,
                        (interval, tick) =>
                        {
                            var timer = new DispatcherTimer { Interval = interval };
                            timer.Tick += (_, _) => tick();
                            timer.Start();
                            return new ActionDisposable(() => timer.Stop());
                        });

                    var unlockDialog = new UnlockDialog { DataContext = unlockVm, Topmost = viewModel.IsAlwaysOnTop };
                    var unlocked = false;
                    unlockVm.UnlockCompleted = () =>
                    {
                        unlocked = true;
                        unlockDialog.Close();
                    };
                    unlockVm.Cancelled = () => unlockDialog.Close();

                    unlockDialog.Opened += async (_, _) =>
                    {
                        try { await unlockVm.BeginAsync(); }
                        catch (Exception ex)
                        {
                            DiagnosticLog.Error("Unlock", $"BeginAsync failed: {ex.Message}");
                            unlockDialog.Close();
                        }
                    };

                    await unlockDialog.ShowDialog(mainWindow);
                    if (unlocked)
                        onUnlocked();
                }
                Run();
            };

            viewModel.ConfirmSafetyWarningsRequested = async warnings =>
            {
                var dialogVm = new SafetyConfirmViewModel(warnings);
                var dialog = new SafetyConfirmDialog
                {
                    DataContext = dialogVm,
                    Topmost = viewModel.IsAlwaysOnTop,
                };
                var tcs = new TaskCompletionSource<bool>();
                dialogVm.Closed = result =>
                {
                    tcs.TrySetResult(result);
                    dialog.Close();
                };
                // If the user closes the window via the title-bar X without
                // clicking either button, default to Cancel.
                dialog.Closed += (_, _) => tcs.TrySetResult(false);
                await dialog.ShowDialog(mainWindow);
                return await tcs.Task;
            };

            viewModel.OpenKeyPickerRequested = request =>
            {
                var pickerVm = new PickerSessionViewModel(
                    BuilderRegistry.CreateAll(),
                    new KeycodeService(),
                    request.CustomKeycodes,
                    request.LayerOptions,
                    macroCount: viewModel.KeyboardConfig?.Macros?.Macros.Count ?? 0,
                    tapDanceCount: viewModel.KeyboardConfig?.TapDances.Count ?? 0);
                var pickerDialog = new KeyPickerDialog { DataContext = pickerVm, Topmost = viewModel.IsAlwaysOnTop };
                pickerVm.Applied = code => { request.OnApply(code); pickerDialog.Close(); };
                pickerVm.Cancelled = () => pickerDialog.Close();
                _ = pickerDialog.ShowDialog(mainWindow);
            };

            viewModel.CopyDiagnosticsRequested = async () =>
            {
                try
                {
                    var report = DiagnosticLog.CollectDiagnosticReport();
                    var clipboard = mainWindow.Clipboard;
                    if (clipboard is not null)
                    {
                        await clipboard.SetTextAsync(report);
                        viewModel.StatusMessage = Loc.Instance["Status_DiagnosticsCopied"];
                    }
                }
                catch (Exception ex)
                {
                    viewModel.StatusMessage = $"Could not copy diagnostics: {ex.Message}";
                }
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
                    Dispatcher.UIThread.Post(() => viewModel.ToggleWindowRequested?.Invoke());
                };
                _hotkeyService.Start();

                desktop.Exit += (_, _) => _hotkeyService.Dispose();
            }

            // Show help on first launch
            if (!initialSettings.HasSeenHelp)
            {
                Dispatcher.UIThread.Post(() => viewModel.OpenHelpRequested?.Invoke(),
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

    private sealed class ActionDisposable : IDisposable
    {
        private Action? _action;
        public ActionDisposable(Action action) => _action = action;
        public void Dispose()
        {
            _action?.Invoke();
            _action = null;
        }
    }

    /// <summary>
    /// Fire-and-forget dialog tasks still need their exceptions observed —
    /// without this the fault propagates only via TaskScheduler.UnobservedTaskException
    /// on GC, which is unreliable and delayed.
    /// </summary>
    private static void ObserveFaults(Task task, string dialogName)
    {
        task.ContinueWith(
            t => DiagnosticLog.Error("EditorDialog",
                $"{dialogName} faulted: {t.Exception?.GetBaseException().Message ?? "unknown"}"),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    private static void LocalizeTrayMenu(TrayIcon trayIcon)
    {
        trayIcon.ToolTipText = Loc.Instance["Tray_Tooltip"];
        if (trayIcon.Menu is not { } menu) return;

        var menuItems = menu.Items.OfType<NativeMenuItem>().ToList();
        for (var i = 0; i < menuItems.Count && i < TrayMenuKeys.Length; i++)
            menuItems[i].Header = Loc.Instance[TrayMenuKeys[i].ResKey];
    }
}
