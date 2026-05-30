using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SvalboardLayerViz.App.Localization;
using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.App.Views;
using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.Export;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Keymap.Builders;
using SvalboardLayerViz.Core.Settings;

namespace SvalboardLayerViz.App.Services;

/// <summary>
/// Production <see cref="IDialogService"/>: opens real Avalonia dialogs owned by
/// the main window. Bodies relocated verbatim from the callback lambdas that
/// previously lived in App.OnFrameworkInitializationCompleted, including the
/// per-editor re-entry guards (formerly captured locals).
/// </summary>
internal sealed class DesktopDialogService : IDialogService
{
    private readonly MainWindowViewModel _vm;
    private readonly Window _owner;
    private readonly ISettingsService _settings;

    // Task/window guards prevent duplicate dialogs when an Open* fires twice
    // before ShowDialog takes the modal lock (rapid clicks, key repeat).
    private Task? _macroEditorTask;
    private Task? _comboEditorTask;
    private Task? _tapDanceEditorTask;
    private DiagnosticsWindow? _diagnosticsWindow;
    private HelpWindow? _helpWindow;

    public DesktopDialogService(MainWindowViewModel vm, Window owner, ISettingsService settings)
    {
        _vm = vm;
        _owner = owner;
        _settings = settings;
    }

    public void OpenSettings(int? initialTabIndex)
    {
        var totalLayers = _vm.KeyboardConfig?.Layers.Count ?? 8;
        var unknowns = _vm.GetUnknownKeycodes();

        QmkSettingsTabViewModel? qmkTab = null;
        if (_vm.KeyboardConfig?.QmkSettings is { Count: > 0 } qmkSettings)
        {
            qmkTab = new QmkSettingsTabViewModel(
                qmkSettings,
                _vm.IsEditMode,
                _vm.IsEditMode ? _vm.ApplySettingEdit : null);
        }

        var settingsVm = new SettingsViewModel(_settings, totalLayers, unknowns,
            _vm.DeviceTappingTermMs, _vm.KeyboardConfig?.Layers, qmkTab);
        if (initialTabIndex is int tab)
            settingsVm.SelectedTabIndex = tab;

        var settingsWindow = new SettingsWindow { DataContext = settingsVm, Topmost = _vm.IsAlwaysOnTop };
        settingsVm.SettingsSaved = () =>
        {
            try
            {
                _vm.ApplySettings();
            }
            catch (Exception ex)
            {
                _vm.StatusMessage = Loc.Instance.Format("Status_SettingsErrorFormat", ex.Message);
            }
            settingsWindow.Close();
        };
        settingsVm.Cancelled = () => settingsWindow.Close();

        settingsWindow.ShowDialog(_owner);
    }

    public async void OpenHistory()
    {
        DiagnosticLog.Info("History", "Open history window requested");
        var historyVm = new HistoryWindowViewModel(
            _vm.SnapshotService,
            _vm.CurrentKeyboardId,
            _vm.GetDeviceKeymapForDiff(),
            new KeycodeService(),
            _vm.GetDeviceMacroBufferForDiff(),
            _vm.GetDeviceCombosForDiff(),
            _vm.GetDeviceTapDancesForDiff());
        historyVm.IsDeviceConnected = _vm.IsConnected;
        historyVm.HasPendingChanges = _vm.DirtyCount > 0;
        var historyWindow = new HistoryWindow { DataContext = historyVm, Topmost = _vm.IsAlwaysOnTop };

        historyVm.RequestExportFilePath = async suggested =>
        {
            var file = await _owner.StorageProvider.SaveFilePickerAsync(
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
            var folders = await _owner.StorageProvider.OpenFolderPickerAsync(
                new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = Loc.Instance["History_ExportFolderTitle"],
                    AllowMultiple = false,
                });
            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        };

        historyVm.RequestImportFolderPath = async () =>
        {
            var folders = await _owner.StorageProvider.OpenFolderPickerAsync(
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
                Topmost = _vm.IsAlwaysOnTop,
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
                Topmost = _vm.IsAlwaysOnTop,
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
                Topmost = _vm.IsAlwaysOnTop,
            };

            // Reuse the same file picker pattern for export
            diffVm.RequestExportFilePath = async suggested =>
            {
                var file = await _owner.StorageProvider.SaveFilePickerAsync(
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
                    await _vm.SnapshotService.ExportAsync(diffVm.Snapshot, path);
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
                    Topmost = _vm.IsAlwaysOnTop,
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
                    await _vm.SnapshotService.DeleteAsync(diffVm.Snapshot.CapturedAt.ToString("o"));
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
                    Topmost = _vm.IsAlwaysOnTop,
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
            _vm.RestoreFromSnapshot(session);
        };

        historyVm.CloseRequested = () => historyWindow.Close();
        await historyVm.LoadAsync();
        await historyWindow.ShowDialog(_owner);
    }

    public void OpenMacroEditor()
    {
        if (_macroEditorTask is { IsCompleted: false }) return;
        _macroEditorTask = EditorDialogFactory.ShowMacroEditorAsync(_vm, _owner);
        ObserveFaults(_macroEditorTask, "Macro editor");
    }

    public void OpenComboEditor()
    {
        if (_comboEditorTask is { IsCompleted: false }) return;
        _comboEditorTask = EditorDialogFactory.ShowComboEditorAsync(_vm, _owner);
        ObserveFaults(_comboEditorTask, "Combo editor");
    }

    public void OpenTapDanceEditor()
    {
        if (_tapDanceEditorTask is { IsCompleted: false }) return;
        _tapDanceEditorTask = EditorDialogFactory.ShowTapDanceEditorAsync(_vm, _owner);
        ObserveFaults(_tapDanceEditorTask, "Tap-dance editor");
    }

    public async void OpenExport()
    {
        if (_vm.KeyboardConfig is null) return;

        var exportVm = new ExportDialogViewModel(_vm.Layers.ToList());
        var exportDialog = new ExportDialog { DataContext = exportVm, Topmost = _vm.IsAlwaysOnTop };

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

            var storageProvider = _owner.StorageProvider;
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
                var userSettings = _settings.Load();
                var userColors = userSettings.LayerColors.Count > 0
                    ? userSettings.LayerColors
                    : null;

                var options = exportVm.BuildOptions(path);
                ExportService.Export(options, _vm.KeyboardConfig.Layers,
                    _vm.KeyboardConfig.Layers.Count, userColors);

                _vm.StatusMessage = Loc.Instance.Format("Status_ExportedFormat", Path.GetFileName(path));
                exportDialog.Close();
            }
            catch (Exception ex)
            {
                _vm.StatusMessage = Loc.Instance.Format("Status_ExportErrorFormat", ex.Message);
            }
        };

        await exportDialog.ShowDialog(_owner);
    }

    public void OpenHelp()
    {
        if (_helpWindow is { IsVisible: true })
        {
            _helpWindow.Activate();
            return;
        }

        var helpVm = new HelpWindowViewModel();
        _helpWindow = new HelpWindow { DataContext = helpVm, Topmost = _vm.IsAlwaysOnTop };
        helpVm.Closed = () =>
        {
            if (helpVm.DontShowAgain)
                _settings.Save(_settings.Load() with { HasSeenHelp = true });
            _helpWindow.Close();
            _helpWindow = null;
        };
        _helpWindow.Closed += (_, _) => _helpWindow = null;
        _helpWindow.Show(_owner);
    }

    public void OpenDiagnostics()
    {
        if (_diagnosticsWindow is { IsVisible: true })
        {
            _diagnosticsWindow.Activate();
            return;
        }

        _vm.Diagnostics.IsActive = true;
        _diagnosticsWindow = new DiagnosticsWindow { DataContext = _vm.Diagnostics, Topmost = _vm.IsAlwaysOnTop };
        _diagnosticsWindow.Closed += (_, _) =>
        {
            _vm.Diagnostics.IsActive = false;
            _diagnosticsWindow = null;
        };
        _diagnosticsWindow.Show(_owner);
    }

    public void OpenKeyPicker(KeyEditRequest request)
    {
        var pickerVm = new PickerSessionViewModel(
            BuilderRegistry.CreateAll(),
            new KeycodeService(),
            request.CustomKeycodes,
            request.LayerOptions,
            macroCount: _vm.KeyboardConfig?.Macros?.Macros.Count ?? 0,
            tapDanceCount: _vm.KeyboardConfig?.TapDances.Count ?? 0);
        var pickerDialog = new KeyPickerDialog { DataContext = pickerVm, Topmost = _vm.IsAlwaysOnTop };
        pickerVm.Applied = code => { request.OnApply(code); pickerDialog.Close(); };
        pickerVm.Cancelled = () => pickerDialog.Close();
        _ = pickerDialog.ShowDialog(_owner);
    }

    public void ShowKeyLabelEditor(KeyViewModel keyVm) =>
        ObserveFaults(EditorDialogFactory.PromptKeyLabelAsync(_vm, _owner, keyVm), "Key label prompt");

    public void OpenUnlock(Action onUnlocked)
    {
        async void Run()
        {
            var unlockVm = new UnlockDialogViewModel(
                _vm.ProtocolService,
                (interval, tick) =>
                {
                    var timer = new DispatcherTimer { Interval = interval };
                    timer.Tick += (_, _) => tick();
                    timer.Start();
                    return new ActionDisposable(() => timer.Stop());
                });

            var unlockDialog = new UnlockDialog { DataContext = unlockVm, Topmost = _vm.IsAlwaysOnTop };
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

            await unlockDialog.ShowDialog(_owner);
            if (unlocked)
                onUnlocked();
        }
        Run();
    }

    public async Task<bool> ConfirmSafetyWarningsAsync(IReadOnlyList<SafetyWarning> warnings)
    {
        var dialogVm = new SafetyConfirmViewModel(warnings);
        var dialog = new SafetyConfirmDialog
        {
            DataContext = dialogVm,
            Topmost = _vm.IsAlwaysOnTop,
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
        await dialog.ShowDialog(_owner);
        return await tcs.Task;
    }

    public Task<string?> PromptSnapshotLabelAsync() =>
        EditorDialogFactory.PromptSnapshotLabelAsync(_vm, _owner);

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
}
