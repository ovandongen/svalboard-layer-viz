namespace SvalboardLayerViz.Core.Settings;

/// <summary>
/// User-configurable settings, persisted as JSON.
/// </summary>
public record UserSettings
{
    /// <summary>Per-layer color overrides. Key = layer index, Value = hex color "#RRGGBB".</summary>
    public Dictionary<int, string> LayerColors { get; init; } = new();

    /// <summary>Per-layer user-assigned names. Key = layer index, Value = name.</summary>
    public Dictionary<int, string> LayerNames { get; init; } = new();

    /// <summary>Custom labels for unknown keycodes. Key = hex keycode (e.g. "0x5300"), Value = label.</summary>
    public Dictionary<string, string> CustomKeyLabels { get; init; } = new();

    /// <summary>Whether the window stays on top of other windows.</summary>
    public bool AlwaysOnTop { get; init; } = true;

    /// <summary>Global hotkey key name (SharpHook KeyCode enum without "Vc" prefix, e.g. "F12").</summary>
    public string HotkeyKey { get; init; } = "F12";

    /// <summary>Global hotkey modifier (SharpHook EventMask name, e.g. "None", "Ctrl").</summary>
    public string HotkeyModifiers { get; init; } = "None";
}
