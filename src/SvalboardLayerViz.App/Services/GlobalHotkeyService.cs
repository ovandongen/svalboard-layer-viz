using SharpHook;
using SharpHook.Data;

namespace SvalboardLayerViz.App.Services;

/// <summary>
/// Listens for a global hotkey using SharpHook and fires a callback.
/// Works cross-platform (Windows, macOS, Linux). On macOS, requires
/// Accessibility permission (OS prompts automatically on first use).
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    private SimpleGlobalHook? _hook;
    private Task? _hookTask;

    /// <summary>Fired on the hook thread when the hotkey is pressed.</summary>
    public Action? HotkeyPressed { get; set; }

    /// <summary>The key to listen for (default: F12).</summary>
    public KeyCode Key { get; set; } = KeyCode.VcF12;

    /// <summary>Required modifier mask (default: Ctrl).</summary>
    public EventMask Modifiers { get; set; } = EventMask.None;

    /// <summary>
    /// Starts listening for the global hotkey. Non-blocking.
    /// </summary>
    public void Start()
    {
        if (_hook is not null) return;

        _hook = new SimpleGlobalHook();
        _hook.KeyPressed += OnKeyPressed;
        _hookTask = _hook.RunAsync();
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode == Key &&
            (Modifiers == EventMask.None || (e.RawEvent.Mask & Modifiers) == Modifiers))
        {
            HotkeyPressed?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_hook is not null)
        {
            _hook.KeyPressed -= OnKeyPressed;
            _hook.Dispose();
            _hook = null;
        }
    }
}
