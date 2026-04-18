using SvalboardLayerViz.Core.History;
using Xunit;

namespace SvalboardLayerViz.Tests.History;

public class SnapshotServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotService _service;

    private static readonly KeyboardId TestKbId = new("1234", "5678", "ABCDEF0123456789");

    public SnapshotServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "slv-test-" + Guid.NewGuid().ToString("N")[..8]);
        _service = new SnapshotService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static ushort[,,] MakeKeymap(int layers = 2, int rows = 2, int cols = 2)
    {
        var km = new ushort[layers, rows, cols];
        for (var l = 0; l < layers; l++)
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                    km[l, r, c] = (ushort)(l * 100 + r * 10 + c);
        return km;
    }

    private static void AssertKeymapsEqual(ushort[,,] expected, ushort[,,] actual)
    {
        Assert.Equal(expected.GetLength(0), actual.GetLength(0));
        Assert.Equal(expected.GetLength(1), actual.GetLength(1));
        Assert.Equal(expected.GetLength(2), actual.GetLength(2));
        for (var l = 0; l < expected.GetLength(0); l++)
            for (var r = 0; r < expected.GetLength(1); r++)
                for (var c = 0; c < expected.GetLength(2); c++)
                    Assert.Equal(expected[l, r, c], actual[l, r, c]);
    }

    // --- CaptureAsync ---

    [Fact]
    public async Task CaptureAsync_CreatesFile()
    {
        var km = MakeKeymap();
        var snapshot = await _service.CaptureAsync(SnapshotReason.Manual, TestKbId, "TestDevice", km);

        Assert.NotNull(snapshot);
        Assert.Equal(SnapshotReason.Manual, snapshot.Reason);
        Assert.Equal(TestKbId, snapshot.KeyboardId);
        Assert.Equal("TestDevice", snapshot.DeviceName);
        Assert.Equal(2, snapshot.LayerCount);
        Assert.Equal(2, snapshot.MatrixRows);
        Assert.Equal(2, snapshot.MatrixCols);

        var files = Directory.GetFiles(_tempDir, "snapshot-*.json");
        Assert.Single(files);
    }

    [Fact]
    public async Task CaptureAsync_RoundTripsKeycodes()
    {
        var km = MakeKeymap();
        await _service.CaptureAsync(SnapshotReason.Manual, TestKbId, "TestDevice", km);

        var files = Directory.GetFiles(_tempDir, "snapshot-*.json");
        var loaded = await _service.LoadAsync(files[0]);

        var reconstituted = loaded.ToKeymap();
        for (var l = 0; l < 2; l++)
            for (var r = 0; r < 2; r++)
                for (var c = 0; c < 2; c++)
                    Assert.Equal(km[l, r, c], reconstituted[l, r, c]);
    }

    [Fact]
    public async Task CaptureAsync_SetsTimestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var snapshot = await _service.CaptureAsync(SnapshotReason.PreSave, TestKbId, "Dev", MakeKeymap());
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(snapshot.CapturedAt, before, after);
    }

    // --- ListAsync ---

    [Fact]
    public async Task ListAsync_EmptyDir_ReturnsEmpty()
    {
        var list = await _service.ListAsync();
        Assert.Empty(list);
    }

    [Fact]
    public async Task ListAsync_ReturnsCapturedSnapshots()
    {
        await _service.CaptureAsync(SnapshotReason.Manual, TestKbId, "Dev", MakeKeymap());
        await _service.CaptureAsync(SnapshotReason.PreSave, TestKbId, "Dev", MakeKeymap());

        var list = await _service.ListAsync();
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task ListAsync_OrderedByDateDescending()
    {
        await _service.CaptureAsync(SnapshotReason.Manual, TestKbId, "Dev", MakeKeymap());
        await Task.Delay(50); // ensure different timestamps
        await _service.CaptureAsync(SnapshotReason.PreSave, TestKbId, "Dev", MakeKeymap());

        var list = await _service.ListAsync();
        Assert.True(list[0].CapturedAt >= list[1].CapturedAt);
    }

    [Theory]
    // Exact match on VID/PID/UID.
    [InlineData("1234", "5678", "ABCDEF0123456789", 1)]
    // Same VID/PID, different UID → Firmware-tier match, still included.
    [InlineData("1234", "5678", "DIFFERENT0000000", 1)]
    // Different VID/PID → Compatibility.None, excluded.
    [InlineData("AAAA", "BBBB", "0000000000000000", 0)]
    public async Task ListAsync_FilterByKeyboardId(string vid, string pid, string uid, int expectedCount)
    {
        var kb = new KeyboardId(vid, pid, uid);
        await _service.CaptureAsync(SnapshotReason.Manual, kb, "Dev", MakeKeymap());

        var filtered = await _service.ListAsync(TestKbId);
        Assert.Equal(expectedCount, filtered.Count);
    }

    // --- LoadAsync ---

    [Fact]
    public async Task LoadAsync_DeserializesCorrectly()
    {
        await _service.CaptureAsync(SnapshotReason.PostSave, TestKbId, "Dev", MakeKeymap());
        var files = Directory.GetFiles(_tempDir, "snapshot-*.json");
        var loaded = await _service.LoadAsync(files[0]);

        Assert.Equal(SnapshotReason.PostSave, loaded.Reason);
        Assert.Equal(TestKbId, loaded.KeyboardId);
        Assert.Equal(KeymapSnapshot.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    // --- DeleteAsync ---

    [Fact]
    public async Task DeleteAsync_RemovesFile()
    {
        await _service.CaptureAsync(SnapshotReason.Manual, TestKbId, "Dev", MakeKeymap());
        var files = Directory.GetFiles(_tempDir, "snapshot-*.json");
        Assert.Single(files);

        await _service.DeleteAsync(files[0]);
        Assert.Empty(Directory.GetFiles(_tempDir, "snapshot-*.json"));
    }

    [Fact]
    public async Task DeleteAsync_NonExistentFile_DoesNotThrow()
    {
        await _service.DeleteAsync(Path.Combine(_tempDir, "nonexistent.json"));
    }

    // --- ExportAsync ---

    [Fact]
    public async Task ExportAsync_WritesFile()
    {
        var snapshot = await _service.CaptureAsync(SnapshotReason.Manual, TestKbId, "Dev", MakeKeymap());
        var exportPath = Path.Combine(_tempDir, "export.json");

        await _service.ExportAsync(snapshot, exportPath);
        Assert.True(File.Exists(exportPath));

        var reimported = await _service.LoadAsync(exportPath);
        Assert.Equal(snapshot.KeyboardId, reimported.KeyboardId);
    }

    // --- PruneAsync ---

    [Fact]
    public async Task PruneAsync_KeepsManualSnapshots()
    {
        for (var i = 0; i < 25; i++)
            await _service.CaptureAsync(SnapshotReason.Manual, TestKbId, "Dev", MakeKeymap());

        var pruned = await _service.PruneAsync(maxSaveSnapshots: 5);
        Assert.Equal(0, pruned);

        var remaining = await _service.ListAsync();
        Assert.Equal(25, remaining.Count);
    }

    [Fact]
    public async Task PruneAsync_TrimsSaveSnapshots_KeepsNewestByTimestamp()
    {
        // Stagger captures so each lands in a distinct millisecond; the filename
        // timestamp is fff-precision and CapturedAt inherits that. Without the
        // delay, several snapshots could share a tick and the "newest" assertion
        // would be order-sensitive to the skip-order inside PruneAsync.
        var captured = new List<DateTimeOffset>();
        for (var i = 0; i < 10; i++)
        {
            var s = await _service.CaptureAsync(SnapshotReason.PreSave, TestKbId, "Dev", MakeKeymap());
            captured.Add(s.CapturedAt);
            await Task.Delay(5);
        }

        var pruned = await _service.PruneAsync(maxSaveSnapshots: 3);
        Assert.Equal(7, pruned);

        // The three newest (last captured) must survive.
        var expectedSurvivors = captured.OrderByDescending(t => t).Take(3).ToList();
        var remaining = await _service.ListAsync();
        Assert.Equal(3, remaining.Count);
        Assert.Equal(expectedSurvivors, remaining.Select(m => m.CapturedAt).ToList());
    }

    [Fact]
    public async Task PruneAsync_FirstConnectSnapshot_RecentOne_Survives()
    {
        // FirstConnect is age-gated (default: keep all newer than 30 days) and
        // is NOT part of the save-bucket count cap. A freshly-captured
        // FirstConnect should survive even if save buckets are at capacity.
        await _service.CaptureAsync(SnapshotReason.FirstConnect, TestKbId, "Dev", MakeKeymap());
        for (var i = 0; i < 10; i++)
            await _service.CaptureAsync(SnapshotReason.PreSave, TestKbId, "Dev", MakeKeymap());

        await _service.PruneAsync(maxSaveSnapshots: 2);

        var remaining = await _service.ListAsync();
        Assert.Single(remaining, m => m.Reason == SnapshotReason.FirstConnect);
    }

    [Fact]
    public async Task PruneAsync_TrimsPostSaveSeparately()
    {
        for (var i = 0; i < 5; i++)
            await _service.CaptureAsync(SnapshotReason.PreSave, TestKbId, "Dev", MakeKeymap());
        for (var i = 0; i < 5; i++)
            await _service.CaptureAsync(SnapshotReason.PostSave, TestKbId, "Dev", MakeKeymap());

        var pruned = await _service.PruneAsync(maxSaveSnapshots: 2);
        Assert.Equal(6, pruned); // 3 PreSave + 3 PostSave

        var remaining = await _service.ListAsync();
        Assert.Equal(4, remaining.Count);
    }

    // --- KeymapSnapshot ---

    [Fact]
    public void StructureKeymap_RoundTrips()
    {
        var km = MakeKeymap(3, 4, 5);
        var layers = KeymapSnapshot.StructureKeymap(km);
        Assert.Equal(3, layers.Length);

        var snapshot = new KeymapSnapshot
        {
            KeyboardId = TestKbId,
            Layers = layers,
            LayerCount = 3,
            MatrixRows = 4,
            MatrixCols = 5,
        };

        var reconstituted = snapshot.ToKeymap();
        for (var l = 0; l < 3; l++)
            for (var r = 0; r < 4; r++)
                for (var c = 0; c < 5; c++)
                    Assert.Equal(km[l, r, c], reconstituted[l, r, c]);
    }

    // --- SnapshotMetadata ---

    [Fact]
    public async Task ListAsync_MetadataHasCorrectFields()
    {
        await _service.CaptureAsync(SnapshotReason.FirstConnect, TestKbId, "TestDevice", MakeKeymap(3, 4, 5));
        var list = await _service.ListAsync();
        var meta = list[0];

        Assert.Equal(SnapshotReason.FirstConnect, meta.Reason);
        Assert.Equal(TestKbId, meta.KeyboardId);
        Assert.Equal("TestDevice", meta.DeviceName);
        Assert.Equal(3, meta.LayerCount);
        Assert.Equal(4, meta.MatrixRows);
        Assert.Equal(5, meta.MatrixCols);
        Assert.True(File.Exists(meta.FilePath));
    }

    // --- UserLabel ---

    [Fact]
    public async Task CaptureAsync_WithUserLabel_Persists()
    {
        await _service.CaptureAsync(
            SnapshotReason.Manual, TestKbId, "Dev", MakeKeymap(),
            userLabel: "before refactor");

        var list = await _service.ListAsync();
        var loaded = await _service.LoadAsync(list[0].FilePath);

        Assert.Equal("before refactor", loaded.UserLabel);
    }

    [Fact]
    public async Task ListAsync_MetadataIncludesUserLabel()
    {
        await _service.CaptureAsync(
            SnapshotReason.Manual, TestKbId, "Dev", MakeKeymap(),
            userLabel: "tagged snapshot");
        await _service.CaptureAsync(
            SnapshotReason.PreSave, TestKbId, "Dev", MakeKeymap());

        var list = await _service.ListAsync();

        var manual = list.Single(m => m.Reason == SnapshotReason.Manual);
        Assert.Equal("tagged snapshot", manual.UserLabel);

        var preSave = list.Single(m => m.Reason == SnapshotReason.PreSave);
        Assert.Null(preSave.UserLabel);
    }

    // --- ImportAsync ---

    private string MakeExportDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "slv-export-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task ImportAsync_ValidMatchingKeyboard_Imports()
    {
        // Capture in a separate service so the source file lives outside _tempDir.
        var sourceDir = MakeExportDir();
        try
        {
            var source = new SnapshotService(sourceDir);
            var captured = await source.CaptureAsync(SnapshotReason.Manual, TestKbId, "Dev", MakeKeymap());
            var sourcePath = Directory.GetFiles(sourceDir, "snapshot-*.json").Single();

            var result = await _service.ImportAsync(sourcePath, TestKbId);

            Assert.Equal(1, result.Imported);
            Assert.Equal(0, result.SkippedDuplicates);
            Assert.Equal(0, result.RefusedWrongKeyboard);
            Assert.Empty(result.Warnings);

            var imported = Directory.GetFiles(_tempDir, "snapshot-*.json").Single();
            var loaded = await _service.LoadAsync(imported);
            Assert.Equal(captured.CapturedAt, loaded.CapturedAt);
            AssertKeymapsEqual(captured.ToKeymap(), loaded.ToKeymap());
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    [Fact]
    public async Task ImportAsync_WrongKeyboard_Refused()
    {
        var sourceDir = MakeExportDir();
        try
        {
            var otherKb = new KeyboardId("AAAA", "BBBB", "0000000000000000");
            var source = new SnapshotService(sourceDir);
            await source.CaptureAsync(SnapshotReason.Manual, otherKb, "OtherDev", MakeKeymap());
            var sourcePath = Directory.GetFiles(sourceDir, "snapshot-*.json").Single();

            var result = await _service.ImportAsync(sourcePath, TestKbId);

            Assert.Equal(0, result.Imported);
            Assert.Equal(1, result.RefusedWrongKeyboard);
            Assert.Empty(result.Warnings);
            Assert.False(Directory.Exists(_tempDir) && Directory.GetFiles(_tempDir).Any());
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    [Fact]
    public async Task ImportAsync_FirmwareMismatch_ImportsCleanly()
    {
        var sourceDir = MakeExportDir();
        try
        {
            // Same VID/PID, different UID — Compatibility.Firmware, not None.
            var sameModel = new KeyboardId("1234", "5678", "DIFFERENT0000000");
            var source = new SnapshotService(sourceDir);
            await source.CaptureAsync(SnapshotReason.Manual, sameModel, "OldFirmware", MakeKeymap());
            var sourcePath = Directory.GetFiles(sourceDir, "snapshot-*.json").Single();

            var result = await _service.ImportAsync(sourcePath, TestKbId);

            Assert.Equal(1, result.Imported);
            Assert.Equal(0, result.RefusedWrongKeyboard);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    [Fact]
    public async Task ImportAsync_DuplicateFilename_Skipped()
    {
        var sourceDir = MakeExportDir();
        try
        {
            var source = new SnapshotService(sourceDir);
            await source.CaptureAsync(SnapshotReason.Manual, TestKbId, "Dev", MakeKeymap());
            var sourcePath = Directory.GetFiles(sourceDir, "snapshot-*.json").Single();
            var fileName = Path.GetFileName(sourcePath);

            // Pre-create a sentinel file at the target name with distinct content.
            Directory.CreateDirectory(_tempDir);
            var targetPath = Path.Combine(_tempDir, fileName);
            await File.WriteAllTextAsync(targetPath, "SENTINEL");

            var result = await _service.ImportAsync(sourcePath, TestKbId);

            Assert.Equal(0, result.Imported);
            Assert.Equal(1, result.SkippedDuplicates);
            Assert.Equal("SENTINEL", await File.ReadAllTextAsync(targetPath));
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    [Fact]
    public async Task ImportAsync_FutureSchemaVersion_Refused()
    {
        var sourceDir = MakeExportDir();
        try
        {
            var sourcePath = Path.Combine(sourceDir, "snapshot-future.json");
            // Hand-crafted JSON with schemaVersion = 99 — everything else valid enough to deserialize.
            var futureJson = """
                {
                  "schemaVersion": 99,
                  "capturedAt": "2026-04-12T12:00:00+00:00",
                  "reason": "Manual",
                  "keyboardId": { "vendorId": "1234", "productId": "5678", "uid": "ABCDEF0123456789" },
                  "deviceName": "Dev",
                  "userLabel": null,
                  "layerCount": 1,
                  "matrixRows": 1,
                  "matrixCols": 1,
                  "layers": [{ "index": 0, "keycodes": [[0]] }]
                }
                """;
            await File.WriteAllTextAsync(sourcePath, futureJson);

            var result = await _service.ImportAsync(sourcePath, TestKbId);

            Assert.Equal(0, result.Imported);
            Assert.Single(result.Warnings);
            Assert.Contains("schema version", result.Warnings[0]);
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    [Fact]
    public async Task ImportAsync_MalformedJson_Refused()
    {
        var sourceDir = MakeExportDir();
        try
        {
            var sourcePath = Path.Combine(sourceDir, "snapshot-garbage.json");
            await File.WriteAllTextAsync(sourcePath, "{ this is not json");

            var result = await _service.ImportAsync(sourcePath, TestKbId);

            Assert.Equal(0, result.Imported);
            Assert.Single(result.Warnings);
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    // --- MacroBuffer ---

    [Fact]
    public async Task CaptureAsync_WithMacroBuffer_RoundTrips()
    {
        var km = MakeKeymap();
        var macroBuffer = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x00 };
        await _service.CaptureAsync(SnapshotReason.Manual, TestKbId, "Dev", km, macroBuffer);

        var files = Directory.GetFiles(_tempDir, "snapshot-*.json");
        var loaded = await _service.LoadAsync(files[0]);

        Assert.Equal(4, loaded.SchemaVersion);
        Assert.NotNull(loaded.MacroBuffer);
        Assert.Equal(macroBuffer, loaded.MacroBuffer);
    }

    [Fact]
    public async Task CaptureAsync_NullMacroBuffer_StillCurrentSchema()
    {
        await _service.CaptureAsync(SnapshotReason.Manual, TestKbId, "Dev", MakeKeymap());

        var files = Directory.GetFiles(_tempDir, "snapshot-*.json");
        var loaded = await _service.LoadAsync(files[0]);

        Assert.Equal(4, loaded.SchemaVersion);
        Assert.Null(loaded.MacroBuffer);
    }

    [Fact]
    public async Task LoadAsync_V2Snapshot_MacroBufferIsNull()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "snapshot-v2-test.json");
        var v2Json = """
            {
              "schemaVersion": 2,
              "capturedAt": "2026-04-12T12:00:00+00:00",
              "reason": "Manual",
              "keyboardId": { "vendorId": "1234", "productId": "5678", "uid": "ABCDEF0123456789" },
              "deviceName": "Dev",
              "layerCount": 1, "matrixRows": 1, "matrixCols": 1,
              "layers": [{ "index": 0, "keycodes": [[42]] }]
            }
            """;
        await File.WriteAllTextAsync(path, v2Json);

        var loaded = await _service.LoadAsync(path);

        Assert.Equal(2, loaded.SchemaVersion);
        Assert.Null(loaded.MacroBuffer);
        Assert.Equal(42, loaded.ToKeymap()[0, 0, 0]);
    }

    [Fact]
    public async Task CaptureAsync_MacroBuffer_IsDefensivelyCopied()
    {
        var km = MakeKeymap();
        var macroBuffer = new byte[] { 0x01, 0x02, 0x03 };
        await _service.CaptureAsync(SnapshotReason.Manual, TestKbId, "Dev", km, macroBuffer);

        macroBuffer[0] = 0xFF;

        var files = Directory.GetFiles(_tempDir, "snapshot-*.json");
        var loaded = await _service.LoadAsync(files[0]);
        Assert.Equal(0x01, loaded.MacroBuffer![0]);
    }

    // --- Dynamic entries (v4) ---

    [Fact]
    public async Task CaptureAsync_WithCombosAndTapDances_RoundTrips()
    {
        var km = MakeKeymap();
        var combos = new byte[][] { [1, 2, 3, 4, 5, 6, 7, 8, 9, 10], new byte[10] };
        var tds = new byte[][] { [11, 12, 13, 14, 15, 16, 17, 18, 19, 20] };
        await _service.CaptureAsync(SnapshotReason.Manual, TestKbId, "Dev", km,
            combos: combos, tapDances: tds);

        var files = Directory.GetFiles(_tempDir, "snapshot-*.json");
        var loaded = await _service.LoadAsync(files[0]);

        Assert.Equal(4, loaded.SchemaVersion);
        Assert.NotNull(loaded.Combos);
        Assert.Equal(2, loaded.Combos!.Length);
        Assert.Equal(combos[0], loaded.Combos[0]);
        Assert.NotNull(loaded.TapDances);
        Assert.Equal(tds[0], loaded.TapDances![0]);
    }

    [Fact]
    public async Task CaptureAsync_NullCombosTapDances_FieldsAreNull()
    {
        await _service.CaptureAsync(SnapshotReason.Manual, TestKbId, "Dev", MakeKeymap());

        var files = Directory.GetFiles(_tempDir, "snapshot-*.json");
        var loaded = await _service.LoadAsync(files[0]);

        Assert.Null(loaded.Combos);
        Assert.Null(loaded.TapDances);
    }

    [Fact]
    public async Task LoadAsync_V3Snapshot_CombosAndTapDancesNull()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "snapshot-v3-test.json");
        var v3Json = """
            {
              "schemaVersion": 3,
              "capturedAt": "2026-04-12T12:00:00+00:00",
              "reason": "Manual",
              "keyboardId": { "vendorId": "1234", "productId": "5678", "uid": "ABCDEF0123456789" },
              "deviceName": "Dev",
              "layerCount": 1, "matrixRows": 1, "matrixCols": 1,
              "layers": [{ "index": 0, "keycodes": [[42]] }],
              "macroBuffer": "AQID"
            }
            """;
        await File.WriteAllTextAsync(path, v3Json);

        var loaded = await _service.LoadAsync(path);

        Assert.Equal(3, loaded.SchemaVersion);
        Assert.NotNull(loaded.MacroBuffer);
        Assert.Null(loaded.Combos);
        Assert.Null(loaded.TapDances);
    }

    [Fact]
    public async Task CaptureAsync_Combos_DefensivelyCopied()
    {
        var combos = new byte[][] { [1, 2, 3, 4, 5, 6, 7, 8, 9, 10] };
        await _service.CaptureAsync(SnapshotReason.Manual, TestKbId, "Dev", MakeKeymap(),
            combos: combos);
        combos[0][0] = 0xFF;

        var files = Directory.GetFiles(_tempDir, "snapshot-*.json");
        var loaded = await _service.LoadAsync(files[0]);
        Assert.Equal(0x01, loaded.Combos![0][0]);
    }

    // --- Concurrency ---

    [Fact]
    public async Task CaptureAsync_FiveParallel_AllLandDistinct()
    {
        // Smoke-test the write gate: five concurrent captures must not collide
        // on directory creation, AtomicFile rename, or filename entropy.
        var km = MakeKeymap();
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => _service.CaptureAsync(SnapshotReason.Manual, TestKbId, "Dev", km))
            .ToArray();

        await Task.WhenAll(tasks);

        var files = Directory.GetFiles(_tempDir, "snapshot-*.json");
        Assert.Equal(5, files.Length);
        Assert.Equal(5, files.Select(Path.GetFileName).Distinct().Count());
    }
}
