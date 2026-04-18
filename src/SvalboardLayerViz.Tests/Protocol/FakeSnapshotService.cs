using SvalboardLayerViz.Core.History;

namespace SvalboardLayerViz.Tests.Protocol;

/// <summary>
/// In-memory snapshot service fake for VM tests. Stores metadata + bodies in
/// dictionaries keyed by FilePath, records all calls, and supports preset
/// import outcomes.
/// </summary>
public class FakeSnapshotService : ISnapshotService
{
    private readonly List<SnapshotMetadata> _metadata = [];
    private readonly Dictionary<string, KeymapSnapshot> _snapshots = new();

    public List<string> DeleteCalls { get; } = [];
    public List<(KeymapSnapshot Snapshot, string Path)> ExportCalls { get; } = [];
    public List<(string Source, KeyboardId Expected)> ImportCalls { get; } = [];
    public int ListCallCount { get; private set; }

    /// <summary>Result returned by the next ImportAsync call.</summary>
    public ImportResult NextImportResult { get; set; } = new(1, 0, 0, []);

    /// <summary>Seeds a snapshot directly into the fake. Returns the synthetic file path.</summary>
    public string Seed(SnapshotMetadata metadata, KeymapSnapshot? body = null)
    {
        _metadata.Add(metadata);
        _snapshots[metadata.FilePath] = body ?? new KeymapSnapshot
        {
            CapturedAt = metadata.CapturedAt,
            Reason = metadata.Reason,
            KeyboardId = metadata.KeyboardId,
            DeviceName = metadata.DeviceName,
            UserLabel = metadata.UserLabel,
            LayerCount = metadata.LayerCount,
            MatrixRows = metadata.MatrixRows,
            MatrixCols = metadata.MatrixCols,
            Layers = KeymapSnapshot.StructureKeymap(new ushort[Math.Max(1, metadata.LayerCount), Math.Max(1, metadata.MatrixRows), Math.Max(1, metadata.MatrixCols)]),
        };
        return metadata.FilePath;
    }

    public Task<KeymapSnapshot> CaptureAsync(
        SnapshotReason reason,
        KeyboardId keyboardId,
        string deviceName,
        ushort[,,] keymap,
        byte[]? macroBuffer = null,
        IReadOnlyList<byte[]>? combos = null,
        IReadOnlyList<byte[]>? tapDances = null,
        string? userLabel = null,
        CancellationToken ct = default)
    {
        var snapshot = new KeymapSnapshot
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Reason = reason,
            KeyboardId = keyboardId,
            DeviceName = deviceName,
            UserLabel = userLabel,
            LayerCount = keymap.GetLength(0),
            MatrixRows = keymap.GetLength(1),
            MatrixCols = keymap.GetLength(2),
            Layers = KeymapSnapshot.StructureKeymap(keymap),
            MacroBuffer = macroBuffer is not null ? (byte[])macroBuffer.Clone() : null,
            Combos = combos?.Select(c => (byte[])c.Clone()).ToArray(),
            TapDances = tapDances?.Select(t => (byte[])t.Clone()).ToArray(),
        };
        var path = $"fake://{Guid.NewGuid():N}.json";
        Seed(new SnapshotMetadata
        {
            FilePath = path,
            CapturedAt = snapshot.CapturedAt,
            Reason = reason,
            KeyboardId = keyboardId,
            DeviceName = deviceName,
            UserLabel = userLabel,
            LayerCount = snapshot.LayerCount,
            MatrixRows = snapshot.MatrixRows,
            MatrixCols = snapshot.MatrixCols,
        }, snapshot);
        return Task.FromResult(snapshot);
    }

    public Task<IReadOnlyList<SnapshotMetadata>> ListAsync(
        KeyboardId? filter = null,
        CancellationToken ct = default)
    {
        ListCallCount++;
        IEnumerable<SnapshotMetadata> q = _metadata;
        if (filter is not null)
            q = q.Where(m => m.KeyboardId.VendorId == filter.VendorId && m.KeyboardId.ProductId == filter.ProductId);
        return Task.FromResult<IReadOnlyList<SnapshotMetadata>>(
            q.OrderByDescending(m => m.CapturedAt).ToList());
    }

    public Task<KeymapSnapshot> LoadAsync(string filePath, CancellationToken ct = default)
    {
        if (!_snapshots.TryGetValue(filePath, out var snap))
            throw new FileNotFoundException(filePath);
        return Task.FromResult(snap);
    }

    public Task DeleteAsync(string filePath, CancellationToken ct = default)
    {
        DeleteCalls.Add(filePath);
        _metadata.RemoveAll(m => m.FilePath == filePath);
        _snapshots.Remove(filePath);
        return Task.CompletedTask;
    }

    public Task ExportAsync(KeymapSnapshot snapshot, string destinationPath, CancellationToken ct = default)
    {
        ExportCalls.Add((snapshot, destinationPath));
        return Task.CompletedTask;
    }

    public Task<int> PruneAsync(int maxSaveSnapshots = 20, int keepFirstConnectDays = 30, CancellationToken ct = default)
        => Task.FromResult(0);

    public Task<ImportResult> ImportAsync(string sourceFilePath, KeyboardId expectedKeyboard, CancellationToken ct = default)
    {
        ImportCalls.Add((sourceFilePath, expectedKeyboard));
        return Task.FromResult(NextImportResult);
    }

    public string GetSnapshotDirectory() => "fake://snapshots";
}
