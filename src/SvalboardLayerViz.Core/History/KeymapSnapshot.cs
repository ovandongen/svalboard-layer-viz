using System.Text.Json.Serialization;

namespace SvalboardLayerViz.Core.History;

/// <summary>
/// A point-in-time capture of the full keymap state.
/// Serialized to JSON for persistent storage.
/// </summary>
public sealed record KeymapSnapshot
{
    /// <summary>Highest schema version this build can read.</summary>
    public const int CurrentSchemaVersion = 4;

    /// <summary>Schema version for forward compatibility.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>When the snapshot was captured (UTC).</summary>
    public DateTimeOffset CapturedAt { get; init; }

    /// <summary>Why this snapshot was taken.</summary>
    public SnapshotReason Reason { get; init; }

    /// <summary>Identity of the keyboard this snapshot came from.</summary>
    public required KeyboardId KeyboardId { get; init; }

    /// <summary>Device name at capture time.</summary>
    public string DeviceName { get; init; } = "";

    /// <summary>Optional user-supplied label (set on manual snapshots).</summary>
    public string? UserLabel { get; init; }

    /// <summary>Number of layers in this snapshot.</summary>
    public int LayerCount { get; init; }

    /// <summary>Matrix rows.</summary>
    public int MatrixRows { get; init; }

    /// <summary>Matrix columns.</summary>
    public int MatrixCols { get; init; }

    /// <summary>
    /// Structured keymap: one entry per layer, each containing a 2D [row][col] grid.
    /// </summary>
    public required SnapshotLayer[] Layers { get; init; }

    /// <summary>
    /// Encoded macro buffer captured from the device. Null for pre-v3 snapshots
    /// or devices that report zero macro capacity.
    /// </summary>
    public byte[]? MacroBuffer { get; init; }

    /// <summary>
    /// Raw combo entries captured from the device, indexed by slot.
    /// Null for pre-v4 snapshots or devices with zero combo capacity.
    /// </summary>
    public byte[][]? Combos { get; init; }

    /// <summary>
    /// Raw tap-dance entries captured from the device, indexed by slot.
    /// Null for pre-v4 snapshots or devices with zero tap-dance capacity.
    /// </summary>
    public byte[][]? TapDances { get; init; }

    /// <summary>
    /// Reconstitutes the structured layers into a 3D keymap.
    /// </summary>
    public ushort[,,] ToKeymap()
    {
        var km = new ushort[LayerCount, MatrixRows, MatrixCols];
        foreach (var layer in Layers)
            for (var r = 0; r < MatrixRows; r++)
                for (var c = 0; c < MatrixCols; c++)
                    km[layer.Index, r, c] = layer.Keycodes[r][c];
        return km;
    }

    /// <summary>
    /// Creates structured layer data from a 3D keymap.
    /// </summary>
    public static SnapshotLayer[] StructureKeymap(ushort[,,] keymap)
    {
        var layers = keymap.GetLength(0);
        var rows = keymap.GetLength(1);
        var cols = keymap.GetLength(2);
        var result = new SnapshotLayer[layers];
        for (var l = 0; l < layers; l++)
        {
            var grid = new ushort[rows][];
            for (var r = 0; r < rows; r++)
            {
                grid[r] = new ushort[cols];
                for (var c = 0; c < cols; c++)
                    grid[r][c] = keymap[l, r, c];
            }
            result[l] = new SnapshotLayer { Index = l, Keycodes = grid };
        }
        return result;
    }
}

/// <summary>
/// One layer's keycode data within a snapshot.
/// </summary>
public sealed record SnapshotLayer
{
    /// <summary>Zero-based layer index.</summary>
    public int Index { get; init; }

    /// <summary>2D keycode grid: [row][col].</summary>
    public required ushort[][] Keycodes { get; init; }
}

/// <summary>
/// Why a snapshot was captured.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SnapshotReason
{
    /// <summary>Captured before a save to device.</summary>
    PreSave,

    /// <summary>Captured after a successful save.</summary>
    PostSave,

    /// <summary>User manually requested a snapshot.</summary>
    Manual,

    /// <summary>Captured on first connection to a device.</summary>
    FirstConnect,
}
