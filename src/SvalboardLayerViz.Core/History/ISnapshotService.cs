namespace SvalboardLayerViz.Core.History;

/// <summary>
/// Captures, stores, lists, loads, and prunes keymap snapshots.
/// </summary>
public interface ISnapshotService
{
    /// <summary>
    /// Captures the current keymap state as a snapshot and persists it.
    /// <paramref name="userLabel"/> is populated for manual snapshots; null otherwise.
    /// </summary>
    Task<KeymapSnapshot> CaptureAsync(
        SnapshotReason reason,
        KeyboardId keyboardId,
        string deviceName,
        ushort[,,] keymap,
        byte[]? macroBuffer = null,
        IReadOnlyList<byte[]>? combos = null,
        IReadOnlyList<byte[]>? tapDances = null,
        string? userLabel = null,
        CancellationToken ct = default);

    /// <summary>
    /// Lists snapshot metadata, optionally filtered to a specific keyboard.
    /// Results are ordered by CapturedAt descending (most recent first).
    /// </summary>
    Task<IReadOnlyList<SnapshotMetadata>> ListAsync(
        KeyboardId? filter = null,
        CancellationToken ct = default);

    /// <summary>
    /// Loads a full snapshot from disk.
    /// </summary>
    Task<KeymapSnapshot> LoadAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Deletes a snapshot file.
    /// </summary>
    Task DeleteAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Exports a snapshot to a user-chosen path.
    /// </summary>
    Task ExportAsync(KeymapSnapshot snapshot, string destinationPath, CancellationToken ct = default);

    /// <summary>
    /// Prunes old snapshots according to the retention policy:
    /// - Pre-save / Post-save: keep last <paramref name="maxSaveSnapshots"/> of each
    /// - First-connect: keep all from last <paramref name="keepFirstConnectDays"/> days
    /// - Manual: keep all (never pruned)
    /// </summary>
    Task<int> PruneAsync(
        int maxSaveSnapshots = 20,
        int keepFirstConnectDays = 30,
        CancellationToken ct = default);

    /// <summary>
    /// Imports a single snapshot file into the snapshot directory.
    /// Refuses files belonging to a different keyboard, files with a future
    /// schema version, and skips files whose name already exists locally.
    /// </summary>
    Task<ImportResult> ImportAsync(
        string sourceFilePath,
        KeyboardId expectedKeyboard,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the directory where snapshots are stored.
    /// </summary>
    string GetSnapshotDirectory();
}
