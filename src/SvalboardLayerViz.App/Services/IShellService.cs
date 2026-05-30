using SvalboardLayerViz.Core.Keymap;

namespace SvalboardLayerViz.App.Services;

/// <summary>
/// Host window-lifecycle and OS-integration operations that
/// <see cref="ViewModels.MainWindowViewModel"/> drives: show/hide/quit the main
/// window, clipboard, global hotkey, and the save-completed notification.
/// Injected for the same reason as <see cref="IDialogService"/> — the headless
/// <see cref="NoopShellService"/> keeps the VM testable without a windowing
/// system.
/// </summary>
public interface IShellService
{
    void ShowWindow();
    void ToggleWindow();
    void Quit();
    void CopyDiagnostics();
    void HotkeyChanged(string key, string modifiers);

    /// <summary>Fired at the end of every save with the final outcome so the host can surface a modal for partials / errors.</summary>
    void SaveCompleted(SaveResult result);
}

/// <summary>Headless default: every shell operation is a no-op. Used by tests and any VM constructed without a host.</summary>
internal sealed class NoopShellService : IShellService
{
    public void ShowWindow() { }
    public void ToggleWindow() { }
    public void Quit() { }
    public void CopyDiagnostics() { }
    public void HotkeyChanged(string key, string modifiers) { }
    public void SaveCompleted(SaveResult result) { }
}
