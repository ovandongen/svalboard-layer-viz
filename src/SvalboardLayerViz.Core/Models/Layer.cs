namespace SvalboardLayerViz.Core.Models;

/// <summary>
/// Represents a single layer of the keyboard, containing all keys with their assignments.
/// </summary>
public record Layer
{
    /// <summary>Layer index (0-based).</summary>
    public required int Index { get; init; }

    /// <summary>User-assigned name for this layer (stored locally, not on device).</summary>
    public string? Name { get; init; }

    /// <summary>All keys on this layer, indexed by (row, col).</summary>
    public required IReadOnlyList<Key> Keys { get; init; }

    /// <summary>Layer color hue (0-255) from Svalboard's layer_colors config.</summary>
    public byte? ColorHue { get; init; }

    /// <summary>Layer color saturation (0-255).</summary>
    public byte? ColorSat { get; init; }

    /// <summary>Layer color value/brightness (0-255).</summary>
    public byte? ColorVal { get; init; }

    /// <summary>Get the display name: user name if set, otherwise "Layer N".</summary>
    public string DisplayName => Name ?? $"L{Index}";
}
