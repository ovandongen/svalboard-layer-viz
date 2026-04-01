namespace SvalboardLayerViz.Core.Models;

/// <summary>
/// Complete keyboard configuration loaded from the device.
/// This is the top-level model that the UI binds to.
/// </summary>
public record KeyboardConfig
{
    /// <summary>Device name as reported by the HID descriptor.</summary>
    public required string DeviceName { get; init; }

    /// <summary>Vial keyboard ID.</summary>
    public required ulong KeyboardId { get; init; }

    /// <summary>Number of matrix rows (10 for Svalboard).</summary>
    public required int MatrixRows { get; init; }

    /// <summary>Number of matrix columns (6 for Svalboard).</summary>
    public required int MatrixCols { get; init; }

    /// <summary>All layers with their key assignments.</summary>
    public required IReadOnlyList<Layer> Layers { get; init; }

    /// <summary>Custom keycodes defined on this keyboard.</summary>
    public IReadOnlyList<CustomKeycode> CustomKeycodes { get; init; } = [];
}

/// <summary>
/// A custom keycode defined in the keyboard's Vial configuration.
/// </summary>
public record CustomKeycode
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    public required string ShortName { get; init; }
}
