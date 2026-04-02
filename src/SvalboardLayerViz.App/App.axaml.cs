using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SvalboardLayerViz.App.Services;
using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.App.Views;
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
            var viewModel = new MainWindowViewModel(settingsService);
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

            viewModel.OpenSettingsRequested = () =>
            {
                var totalLayers = viewModel.KeyboardConfig?.Layers.Count ?? 8;
                var unknowns = viewModel.GetUnknownKeycodes();
                var settingsVm = new SettingsViewModel(settingsService, totalLayers, unknowns,
                    viewModel.DeviceTappingTermMs);

                var settingsWindow = new SettingsWindow { DataContext = settingsVm };
                settingsVm.SettingsSaved = () =>
                {
                    try
                    {
                        viewModel.ApplySettings();
                    }
                    catch (Exception ex)
                    {
                        viewModel.StatusMessage = $"Settings error: {ex.Message}";
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
                    Title = $"Set Label — {keyVm.HexKeycode}",
                    Width = 350,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                };

                var textBox = new TextBox
                {
                    Watermark = "Custom label",
                    Text = keyVm.Key.IsUnknown ? "" : keyVm.DisplayLabel,
                    Margin = new Thickness(0, 0, 0, 8),
                };

                var saveBtn = new Button { Content = "Save", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
                var info = new TextBlock
                {
                    Text = $"Keycode: {keyVm.HexKeycode}  Current: {keyVm.DisplayLabel}",
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

                diagnosticsWindow = new DiagnosticsWindow { DataContext = viewModel.Diagnostics };
                diagnosticsWindow.Closed += (_, _) => diagnosticsWindow = null;
                diagnosticsWindow.Show(mainWindow);
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

            // Global hotkey from settings
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

            // Bind tray icon commands to the main view model
            DataContext = viewModel;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
