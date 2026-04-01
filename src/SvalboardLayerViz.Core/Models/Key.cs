namespace SvalboardLayerViz.Core.Models;

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
}
