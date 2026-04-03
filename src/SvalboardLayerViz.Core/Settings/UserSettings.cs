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

    /// <summary>Whether live key highlighting (matrix state polling) is enabled.</summary>
    public bool LiveKeyHighlighting { get; init; } = true;

    /// <summary>Whether to auto-switch the displayed layer based on held layer-switch keys.</summary>
    public bool AutoLayerSwitch { get; init; } = true;

    /// <summary>
    /// Minimum hold time (ms) before a momentary layer key triggers a layer switch in the visualization.
    /// Prevents brief taps on dual-function keys (e.g., LT — tap for Enter, hold for layer) from
    /// causing flicker. Default 200ms matches QMK's typical TAPPING_TERM.
    /// </summary>
    public int LayerHoldThresholdMs { get; init; } = 200;

    /// <summary>Background solidity behind the board and tabs (0.0 = transparent, 1.0 = solid dark). Default: 0.5.</summary>
    public double BackgroundOpacity { get; init; } = 0.5;

    /// <summary>Whether the user has seen the help/welcome window. Controls first-launch auto-open.</summary>
    public bool HasSeenHelp { get; init; } = false;
}
