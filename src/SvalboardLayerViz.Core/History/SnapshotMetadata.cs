namespace SvalboardLayerViz.Core.History;

/// <summary>
/// Lightweight metadata for listing snapshots without loading the full keymap data.
/// </summary>
public sealed record SnapshotMetadata
{
    /// <summary>Full file path to the snapshot JSON.</summary>
    public required string FilePath { get; init; }

    /// <summary>When the snapshot was captured.</summary>
    public DateTimeOffset CapturedAt { get; init; }

    /// <summary>Why this snapshot was taken.</summary>
    public SnapshotReason Reason { get; init; }

    /// <summary>Identity of the keyboard.</summary>
    public required KeyboardId KeyboardId { get; init; }

    /// <summary>Device name at capture time.</summary>
    public string DeviceName { get; init; } = "";

    /// <summary>User-supplied label (manual snapshots only); null otherwise.</summary>
    public string? UserLabel { get; init; }

    /// <summary>Dimensions for compatibility checking.</summary>
    public int LayerCount { get; init; }
    public int MatrixRows { get; init; }
    public int MatrixCols { get; init; }
}
