namespace SvalboardLayerViz.Core.Models;

/// <summary>
/// How a layer-switch key activates its target layer.
/// Used by auto-layer-switch to decide hold-tracking vs edge-tracking.
/// </summary>
public enum LayerSwitchType
{
    /// <summary>Not a layer-switch key.</summary>
    None,

    /// <summary>Momentary: layer active while key is held (MO, LT, TT).</summary>
    Momentary,

    /// <summary>Toggle: each press flips the layer on/off (TG).</summary>
    Toggle,

    /// <summary>Activate: turns on layer permanently until another layer change (TO, DF).</summary>
    Activate,

    /// <summary>One-shot: layer active for the next keypress only (OSL).</summary>
    OneShot,
}

/// <summary>
/// Represents a single key on the board with its physical position and current keycode.
/// </summary>
public record Key
{
    /// <summary>Row index in the Vial matrix (0-9 for Svalboard).</summary>
    public required int Row { get; init; }

    /// <summary>Column index in the Vial matrix (0-5 for Svalboard).</summary>
    public required int Col { get; init; }

    /// <summary>Raw 16-bit keycode value from the device.</summary>
    public required ushort RawKeycode { get; init; }

    /// <summary>Human-readable label for this key (e.g., "A", "Ctrl", "MO(2)").</summary>
    public string DisplayLabel { get; init; } = "";

    /// <summary>Secondary label for modifier combinations (e.g., "Shift" for Shift+A).</summary>
    public string? SecondaryLabel { get; init; }

    /// <summary>True if this key is KC_TRNS (transparent, falls through to layer below).</summary>
    public bool IsTransparent { get; init; }

    /// <summary>If transparent, the resolved effective label from the layer below.</summary>
    public string? EffectiveLabel { get; init; }

    /// <summary>Physical X position in layout coordinates.</summary>
    public double X { get; init; }

    /// <summary>Physical Y position in layout coordinates.</summary>
    public double Y { get; init; }

    /// <summary>Key width in layout units (typically 1.0).</summary>
    public double Width { get; init; } = 1.0;

    /// <summary>Key height in layout units (typically 1.0).</summary>
    public double Height { get; init; } = 1.0;

    /// <summary>True if this key activates or switches to another layer (MO, TG, LT, etc.).</summary>
    public bool IsLayerSwitch { get; init; }

    /// <summary>The target layer index for layer-switching keys.</summary>
    public int? TargetLayer { get; init; }

    /// <summary>How this layer-switch key activates its target (Momentary, Toggle, etc.).</summary>
    public LayerSwitchType SwitchType { get; init; }

    /// <summary>True if the keycode could not be resolved to a known label.</summary>
    public bool IsUnknown { get; init; }

    /// <summary>Shifted symbol for this key (e.g., "@" for "2", ":" for ";"). US ANSI layout.</summary>
    public string? ShiftedLabel { get; init; }

    /// <summary>
    /// For a transparent key whose label was resolved, the layer the effective
    /// label was pulled from. Null on layer 0, on unresolved TRNS, and on
    /// non-transparent keys. Used by the tooltip to explain the fallthrough.
    /// </summary>
    public int? ResolvedFromLayer { get; init; }
}
