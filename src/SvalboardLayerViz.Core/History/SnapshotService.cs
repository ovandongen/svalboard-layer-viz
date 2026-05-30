using System.Text.Json;
using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.Persistence;

namespace SvalboardLayerViz.Core.History;

/// <summary>
/// JSON file-based snapshot storage with retention-based pruning.
/// </summary>
public sealed class SnapshotService : ISnapshotService
{
    private readonly string _snapshotDir;

    // Single-writer gate — serializes Capture / Delete / Export / Import so two
    // code paths can't race on Directory.CreateDirectory, AtomicFile rename, or
    // a Delete+List interleave. PruneAsync itself is NOT gated (it calls gated
    // DeleteAsync internally); SemaphoreSlim is not re-entrant so nesting would
    // deadlock. ListAsync and LoadAsync are read-only and already tolerate
    // concurrent file disappearance (see FileNotFoundException arm below).
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public SnapshotService(string? snapshotDir = null)
    {
        _snapshotDir = snapshotDir
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SvalboardLayerViz",
                "snapshots");
    }

    public string GetSnapshotDirectory() => _snapshotDir;

    public async Task<KeymapSnapshot> CaptureAsync(
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
        var now = DateTimeOffset.UtcNow;
        var snapshot = new KeymapSnapshot
        {
            CapturedAt = now,
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

        // 10 hex chars of GUID entropy (40 bits). 6 collides ~3% at 10k snapshots;
        // 10 drops collision below 1-in-a-million at that scale.
        var fileName = $"snapshot-{now:yyyyMMdd-HHmmss-fff}-{reason.ToString().ToLowerInvariant()}-{Guid.NewGuid().ToString("N")[..10]}.json";

        await _writeGate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_snapshotDir);
            var filePath = Path.Combine(_snapshotDir, fileName);

            var json = JsonSerializer.Serialize(snapshot, CoreJson.Default);
            await AtomicFile.WriteAllTextAsync(filePath, json, ct);
        }
        finally
        {
            _writeGate.Release();
        }

        DiagnosticLog.Info("Snapshot", $"Captured {reason} snapshot: {fileName}");
        return snapshot;
    }

    public async Task<IReadOnlyList<SnapshotMetadata>> ListAsync(
        KeyboardId? filter = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(_snapshotDir))
            return [];

        var files = Directory.GetFiles(_snapshotDir, "snapshot-*.json");
        var results = new List<SnapshotMetadata>();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var snapshot = JsonSerializer.Deserialize<KeymapSnapshot>(json, CoreJson.Default);
                if (snapshot is null) continue;

                if (filter is not null &&
                    SnapshotCompatibilityChecker.Check(snapshot.KeyboardId, filter) == SnapshotCompatibility.None)
                    continue;

                results.Add(new SnapshotMetadata
                {
                    FilePath = file,
                    CapturedAt = snapshot.CapturedAt,
                    Reason = snapshot.Reason,
                    KeyboardId = snapshot.KeyboardId,
                    DeviceName = snapshot.DeviceName,
                    UserLabel = snapshot.UserLabel,
                    LayerCount = snapshot.LayerCount,
                    MatrixRows = snapshot.MatrixRows,
                    MatrixCols = snapshot.MatrixCols,
                });
            }
            catch (FileNotFoundException)
            {
                // File vanished between GetFiles and ReadAllTextAsync — expected
                // when a concurrent DeleteAsync/PruneAsync runs against the same
                // directory. Log at Info, not Warn: the caller didn't cause this
                // and there's nothing to fix.
                DiagnosticLog.Info("Snapshot", $"Snapshot {Path.GetFileName(file)} vanished mid-list (likely concurrent prune); skipping.");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("Snapshot", $"Failed to read snapshot {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return results.OrderByDescending(m => m.CapturedAt).ToList();
    }

    public async Task<KeymapSnapshot> LoadAsync(string filePath, CancellationToken ct = default)
    {
        var json = await File.ReadAllTextAsync(filePath, ct);
        return JsonSerializer.Deserialize<KeymapSnapshot>(json, CoreJson.Default)
            ?? throw new InvalidOperationException($"Failed to deserialize snapshot: {filePath}");
    }

    public async Task DeleteAsync(string filePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _writeGate.WaitAsync(ct);
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                DiagnosticLog.Info("Snapshot", $"Deleted snapshot: {Path.GetFileName(filePath)}");
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task ExportAsync(KeymapSnapshot snapshot, string destinationPath, CancellationToken ct = default)
    {
        await _writeGate.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(snapshot, CoreJson.Default);
            await AtomicFile.WriteAllTextAsync(destinationPath, json, ct);
        }
        finally
        {
            _writeGate.Release();
        }
        DiagnosticLog.Info("Snapshot", $"Exported snapshot to: {destinationPath}");
    }

    public async Task<ImportResult> ImportAsync(
        string sourceFilePath,
        KeyboardId expectedKeyboard,
        CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(sourceFilePath);

        KeymapSnapshot? snapshot;
        try
        {
            var json = await File.ReadAllTextAsync(sourceFilePath, ct);
            snapshot = JsonSerializer.Deserialize<KeymapSnapshot>(json, CoreJson.Default);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ImportResult(0, 0, 0, [$"{fileName}: {ex.Message}"]);
        }

        if (snapshot is null)
            return new ImportResult(0, 0, 0, [$"{fileName}: deserialized to null"]);

        if (snapshot.SchemaVersion > KeymapSnapshot.CurrentSchemaVersion)
            return new ImportResult(0, 0, 0,
                [$"{fileName}: schema version {snapshot.SchemaVersion} not supported (max {KeymapSnapshot.CurrentSchemaVersion})"]);

        if (SnapshotCompatibilityChecker.Check(snapshot.KeyboardId, expectedKeyboard) == SnapshotCompatibility.None)
            return new ImportResult(0, 0, 1, []);

        await _writeGate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_snapshotDir);
            var targetPath = Path.Combine(_snapshotDir, fileName);
            if (File.Exists(targetPath))
                return new ImportResult(0, 1, 0, []);

            var normalized = JsonSerializer.Serialize(snapshot, CoreJson.Default);
            await AtomicFile.WriteAllTextAsync(targetPath, normalized, ct);
        }
        finally
        {
            _writeGate.Release();
        }

        DiagnosticLog.Info("Snapshot", $"Imported snapshot: {fileName}");
        return new ImportResult(1, 0, 0, []);
    }

    public async Task<int> PruneAsync(
        int maxSaveSnapshots = 20,
        int keepFirstConnectDays = 30,
        CancellationToken ct = default)
    {
        var all = await ListAsync(filter: null, ct);
        var pruned = 0;
        var cutoffDate = DateTimeOffset.UtcNow.AddDays(-keepFirstConnectDays);

        // Group by reason for retention logic
        var preSave = all.Where(m => m.Reason == SnapshotReason.PreSave).ToList();
        var postSave = all.Where(m => m.Reason == SnapshotReason.PostSave).ToList();
        var firstConnect = all.Where(m => m.Reason == SnapshotReason.FirstConnect).ToList();
        // Manual snapshots: never pruned

        // Pre-save: keep last N
        foreach (var excess in preSave.Skip(maxSaveSnapshots))
        {
            await DeleteAsync(excess.FilePath, ct);
            pruned++;
        }

        // Post-save: keep last N
        foreach (var excess in postSave.Skip(maxSaveSnapshots))
        {
            await DeleteAsync(excess.FilePath, ct);
            pruned++;
        }

        // First-connect: keep all from last N days
        foreach (var old in firstConnect.Where(m => m.CapturedAt < cutoffDate))
        {
            await DeleteAsync(old.FilePath, ct);
            pruned++;
        }

        if (pruned > 0)
            DiagnosticLog.Info("Snapshot", $"Pruned {pruned} snapshot(s)");

        return pruned;
    }
}
