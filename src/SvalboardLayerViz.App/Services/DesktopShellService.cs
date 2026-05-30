using Avalonia.Controls;
using SvalboardLayerViz.App.Localization;
using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Settings;

namespace SvalboardLayerViz.App.Services;

/// <summary>
/// Production <see cref="IShellService"/>: drives the real main window, clipboard,
/// and global hotkey. Bodies relocated verbatim from
/// App.OnFrameworkInitializationCompleted's callback wiring. The hotkey is
/// reached through a late accessor because it's created after this service.
/// </summary>
internal sealed class DesktopShellService : IShellService
{
    private readonly MainWindowViewModel _vm;
    private readonly Window _window;
    private readonly ISettingsService _settings;
    private readonly Func<GlobalHotkeyService?> _hotkey;

    public DesktopShellService(
        MainWindowViewModel vm, Window window, ISettingsService settings, Func<GlobalHotkeyService?> hotkey)
    {
        _vm = vm;
        _window = window;
        _settings = settings;
        _hotkey = hotkey;
    }

    public void ShowWindow()
    {
        _window.Show();
        _window.Activate();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
    }

    public void ToggleWindow()
    {
        if (_window.IsVisible)
        {
            _window.Hide();
        }
        else
        {
            _window.Show();
            _window.Activate();
            if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;
        }
    }

    // QuitCommand uses Environment.Exit, which skips the window Closing handler —
    // so persist geometry here too before exiting.
    public void Quit()
    {
        SaveWindowState();
        Environment.Exit(0);
    }

    public async void CopyDiagnostics()
    {
        try
        {
            var report = DiagnosticLog.CollectDiagnosticReport();
            var clipboard = _window.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(report);
                _vm.StatusMessage = Loc.Instance["Status_DiagnosticsCopied"];
            }
        }
        catch (Exception ex)
        {
            _vm.StatusMessage = $"Could not copy diagnostics: {ex.Message}";
        }
    }

    public void HotkeyChanged(string key, string modifiers)
    {
        try
        {
            _hotkey()?.UpdateHotkey(
                GlobalHotkeyService.ParseKey(key),
                GlobalHotkeyService.ParseModifiers(modifiers));
        }
        catch
        {
            // Invalid key/modifier — keep existing hotkey
        }
    }

    // Unwired in production today (no modal surfaced on save); kept so the VM's
    // save pipeline has a stable notification seam and tests can observe results.
    public void SaveCompleted(SaveResult result) { }

    /// <summary>
    /// Persists current window geometry. Public (not on <see cref="IShellService"/>)
    /// because the composition root also calls it from the window Closing handler.
    /// </summary>
    public void SaveWindowState()
    {
        if (_window.WindowState != WindowState.Minimized)
        {
            var s = _settings.Load();
            _settings.Save(s with
            {
                WindowX = _window.Position.X,
                WindowY = _window.Position.Y,
                WindowWidth = _window.Width,
                WindowHeight = _window.Height,
            });
        }
    }
}
