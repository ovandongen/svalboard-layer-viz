using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.History;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Models;
using SvalboardLayerViz.Core.Protocol;
using SvalboardLayerViz.Core.Settings;
using SvalboardLayerViz.Tests.Protocol;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

/// <summary>
/// End-to-end restore flow tests: snapshot → diff → edit session → MainWindowViewModel.
/// </summary>
public class SnapshotRestoreFlowTests : IDisposable
{
    private readonly FakeVialProtocolService _protocol = new();
    private readonly ISettingsService _settings;
    private readonly string _snapshotDir;
    private readonly SnapshotService _snapshots;

    public SnapshotRestoreFlowTests()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"svz-restore-test-{Guid.NewGuid():N}.json");
        _settings = new SettingsService(settingsPath);
        _snapshotDir = Path.Combine(Path.GetTempPath(), $"svz-restore-snapshots-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_snapshotDir);
        _snapshots = new SnapshotService(_snapshotDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_snapshotDir)) Directory.Delete(_snapshotDir, recursive: true); }
        catch { /* best effort */ }
    }

    private MainWindowViewModel CreateVmWithConfig(int layerCount = 2)
    {
        _protocol.LayerCount = layerCount;
        var km = new ushort[layerCount, 10, 6];
        for (var l = 0; l < layerCount; l++)
            for (var r = 0; r < 10; r++)
                for (var c = 0; c < 6; c++)
                    km[l, r, c] = (ushort)(0x04 + l);
        if (layerCount > 1) km[0, 0, 0] = 0x5101; // MO(1)
        _protocol.Keymap = km;

        var vm = new MainWindowViewModel(_settings, _protocol, _snapshots);
        vm.KeyboardConfig = BuildConfig(layerCount);
        vm.BuildLayerViewModels(_settings.Load());
        vm.IsConnected = true;
        return vm;
    }

    private static KeyboardConfig BuildConfig(int layerCount)
    {
        var positions = SvalboardLayout.GetKeyPositions();
        var layers = new List<Layer>();
        for (var i = 0; i < layerCount; i++)
        {
            var baseCode = (ushort)(0x04 + i);
            var keys = positions.Select(p => new Key
            {
                Row = p.Row,
                Col = p.Col,
                RawKeycode = baseCode,
                DisplayLabel = $"K{baseCode:X}",
                X = p.X,
                Y = p.Y,
                Width = p.Width,
                Height = p.Height,
            }).ToList();
            layers.Add(new Layer { Index = i, Keys = keys });
        }

        return new KeyboardConfig
        {
            DeviceName = "Test",
            VendorId = 0xCAFE,
            ProductId = 0xBABE,
            KeyboardId = 0,
            MatrixRows = 10,
            MatrixCols = 6,
            Layers = layers,
        };
    }

    private static KeymapEditSession BuildRestoreSession(ushort[,,] deviceKeymap, ushort[,,] snapshotKeymap)
    {
        var diffs = SnapshotDiffComputer.Diff(deviceKeymap, snapshotKeymap);
        var session = new KeymapEditSession(deviceKeymap);
        foreach (var op in SnapshotDiffComputer.ToEditOps(diffs))
            session.Apply(op);
        return session;
    }

    [Fact]
    public void RestoreFromSnapshot_SetsEditModeAndDirtyCount()
    {
        var vm = CreateVmWithConfig();
        Assert.False(vm.IsEditMode);

        // Build a snapshot keymap with 2 changes on layer 0
        var deviceKm = vm.GetDeviceKeymapForDiff()!;
        var snapshotKm = (ushort[,,])deviceKm.Clone();
        snapshotKm[0, 1, 0] = 0x0010;
        snapshotKm[0, 2, 1] = 0x0011;
        var session = BuildRestoreSession(deviceKm, snapshotKm);

        vm.RestoreFromSnapshot(session);

        Assert.True(vm.IsEditMode);
        Assert.Equal(2, vm.DirtyCount);
    }

    [Fact]
    public void RestoreFromSnapshot_IdenticalKeymap_ZeroDirtyCount()
    {
        var vm = CreateVmWithConfig();
        var deviceKm = vm.GetDeviceKeymapForDiff()!;
        var session = BuildRestoreSession(deviceKm, deviceKm);

        vm.RestoreFromSnapshot(session);

        Assert.True(vm.IsEditMode);
        Assert.Equal(0, vm.DirtyCount);
    }

    [Fact]
    public async Task RestoreFromSnapshot_ReplacesExistingEditSession()
    {
        var vm = CreateVmWithConfig();

        // Enter edit mode first via normal path
        _protocol.NextUnlockStatus = new UnlockStatus(true, false, []);
        await vm.EnterEditCommand.ExecuteAsync(null);
        Assert.True(vm.IsEditMode);
        Assert.Equal(0, vm.DirtyCount);

        // Restore should replace the existing session, not be blocked
        var deviceKm = vm.GetDeviceKeymapForDiff()!;
        var snapshotKm = (ushort[,,])deviceKm.Clone();
        snapshotKm[0, 1, 0] = 0x0010;
        var session = BuildRestoreSession(deviceKm, snapshotKm);

        vm.RestoreFromSnapshot(session);

        Assert.True(vm.IsEditMode);
        Assert.Equal(1, vm.DirtyCount);
    }

    [Fact]
    public void RestoreSession_UndoStackMatchesDiffCount()
    {
        var deviceKm = new ushort[2, 10, 6];
        var snapshotKm = new ushort[2, 10, 6];
        snapshotKm[0, 1, 0] = 0x0004;
        snapshotKm[0, 2, 1] = 0x0005;
        snapshotKm[1, 3, 2] = 0x0006;

        var session = BuildRestoreSession(deviceKm, snapshotKm);

        Assert.Equal(3, session.UndoCount);
        Assert.Equal(3, session.PendingChanges.Count);

        // Undo all
        session.Undo();
        session.Undo();
        session.Undo();
        Assert.False(session.HasPendingChanges);
    }

    [Fact]
    public void LayerTruncation_DiffsOnlyWithinDeviceRange()
    {
        // Device has 2 layers, snapshot has 4
        var deviceKm = new ushort[2, 10, 6];
        var snapshotKm = new ushort[4, 10, 6];
        snapshotKm[0, 1, 0] = 0x0004; // within device range
        snapshotKm[3, 1, 0] = 0x0005; // beyond device range

        // Truncate to device layer count before diff
        var minLayers = Math.Min(deviceKm.GetLength(0), snapshotKm.GetLength(0));
        var truncDevice = new ushort[minLayers, 10, 6];
        var truncSnapshot = new ushort[minLayers, 10, 6];
        for (var l = 0; l < minLayers; l++)
            for (var r = 0; r < 10; r++)
                for (var c = 0; c < 6; c++)
                {
                    truncDevice[l, r, c] = deviceKm[l, r, c];
                    truncSnapshot[l, r, c] = snapshotKm[l, r, c];
                }

        var diffs = SnapshotDiffComputer.Diff(truncDevice, truncSnapshot);
        Assert.All(diffs, d => Assert.True(d.Layer < 2));
        Assert.Single(diffs); // only the layer 0 change
    }

    [Fact]
    public void GetDeviceKeymapForDiff_ReturnsNull_WhenDisconnected()
    {
        var vm = CreateVmWithConfig();
        vm.IsConnected = false;
        Assert.Null(vm.GetDeviceKeymapForDiff());
    }

    [Fact]
    public void GetDeviceKeymapForDiff_ReturnsKeymap_WhenConnected()
    {
        var vm = CreateVmWithConfig();
        var km = vm.GetDeviceKeymapForDiff();
        Assert.NotNull(km);
        Assert.Equal(2, km!.GetLength(0)); // 2 layers
        Assert.Equal(10, km.GetLength(1)); // 10 rows
        Assert.Equal(6, km.GetLength(2)); // 6 cols
    }
}
