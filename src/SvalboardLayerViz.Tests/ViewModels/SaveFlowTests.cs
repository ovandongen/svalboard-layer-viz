using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.History;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Models;
using SvalboardLayerViz.Core.Protocol;
using SvalboardLayerViz.Core.Settings;
using SvalboardLayerViz.Tests.Protocol;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

/// <summary>
/// Session 11 tests — the full save pipeline from the edit session through
/// executor, reload, and reconciliation. Uses a real <see cref="SnapshotService"/>
/// against a temporary directory and a <see cref="FakeVialProtocolService"/>
/// with its <c>SetKeycodeFailAt</c> hook for failure injection.
/// </summary>
public class SaveFlowTests : IDisposable
{
    private readonly FakeVialProtocolService _protocol = new();
    private readonly ISettingsService _settings;
    private readonly string _snapshotDir;
    private readonly SnapshotService _snapshots;

    public SaveFlowTests()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"svz-test-{Guid.NewGuid():N}.json");
        _settings = new SettingsService(settingsPath);

        _snapshotDir = Path.Combine(Path.GetTempPath(), $"svz-snapshots-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_snapshotDir);
        _snapshots = new SnapshotService(_snapshotDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_snapshotDir)) Directory.Delete(_snapshotDir, recursive: true); }
        catch { /* best effort */ }
    }

    // ----- Setup helpers -----

    private MainWindowViewModel CreateVmWithConfig(
        int layerCount = 2,
        int moRow = 0,
        int moCol = 0)
    {
        // Seed the fake's keymap so the post-save reload returns coherent data.
        // Layer 0 keys use 0x04 + layer index so the LayerViewModel doesn't skip
        // them as "all KC_NO".
        _protocol.LayerCount = layerCount;
        var km = new ushort[layerCount, 10, 6];
        for (var l = 0; l < layerCount; l++)
            for (var r = 0; r < 10; r++)
                for (var c = 0; c < 6; c++)
                    km[l, r, c] = (ushort)(0x04 + l);
        // Plant an MO(1) on L0 so L1 is reachable and can be escaped via base.
        // MO(layer) = 0x5100 | layer in the plan's encoding — see KeycodeService.
        // moRow/moCol let recovery tests prove they aren't latched to (0,0).
        if (layerCount > 1) km[0, moRow, moCol] = 0x5101; // MO(1)
        _protocol.Keymap = km;

        var vm = new MainWindowViewModel(_settings, _protocol, _snapshots);
        vm.KeyboardConfig = BuildConfig(layerCount);
        vm.BuildLayerViewModels(_settings.Load());
        vm.IsConnected = true;
        return vm;
    }

    /// <summary>
    /// Nested-loop cell-by-cell equality so a mismatch reports (layer, row, col)
    /// rather than "array != array". No existing helper in the test project.
    /// </summary>
    private static void AssertKeymapEquals(ushort[,,] expected, ushort[,,] actual)
    {
        Assert.Equal(expected.GetLength(0), actual.GetLength(0));
        Assert.Equal(expected.GetLength(1), actual.GetLength(1));
        Assert.Equal(expected.GetLength(2), actual.GetLength(2));
        for (var l = 0; l < expected.GetLength(0); l++)
            for (var r = 0; r < expected.GetLength(1); r++)
                for (var c = 0; c < expected.GetLength(2); c++)
                    Assert.True(
                        expected[l, r, c] == actual[l, r, c],
                        $"[{l},{r},{c}] expected 0x{expected[l, r, c]:X4}, got 0x{actual[l, r, c]:X4}");
    }

    /// <summary>
    /// Builds the expected EditSession baseline: the session is seeded from
    /// KeyboardConfig.Layers[l].Keys (physical positions only), so cells with
    /// no KeyPosition (e.g. col 5 on finger rows 1–4, 6–9) stay 0x0000.
    /// </summary>
    private static ushort[,,] BuildBaselineLike(int layerCount, int moRow, int moCol)
    {
        var km = new ushort[layerCount, 10, 6];
        var positions = SvalboardLayout.GetKeyPositions();
        for (var l = 0; l < layerCount; l++)
            foreach (var p in positions)
                km[l, p.Row, p.Col] = (ushort)(0x04 + l);
        if (layerCount > 1) km[0, moRow, moCol] = 0x5101;
        return km;
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

    private static async Task EnterEditAsync(MainWindowViewModel vm, bool lockedBeforeEdit = false)
    {
        // Protocol starts locked-on-entry iff the caller asks; when locked we
        // stub OpenUnlockRequested to auto-run the unlock callback so the VM
        // enters edit mode synchronously without poking real UI.
        var fake = (FakeVialProtocolService)vm.ProtocolService;
        fake.NextUnlockStatus = lockedBeforeEdit
            ? new UnlockStatus(false, false, [(0, 0)])
            : new UnlockStatus(true, false, []);
        if (lockedBeforeEdit)
            vm.OpenUnlockRequested = cb => cb();
        await vm.EnterEditCommand.ExecuteAsync(null);
    }

    private static (int row, int col) FirstKeyOnLayer(MainWindowViewModel vm, int layer)
    {
        var layerVm = vm.Layers.First(l => l.Index == layer);
        // Use a key we're sure the baseline (0x04+layer) sits at. Skip the one
        // we plant the MO(1) on (row 0 col 0 on layer 0) so we don't overwrite it.
        var k = layerVm.Keys.First(x => !(layer == 0 && x.Key.Row == 0 && x.Key.Col == 0));
        return (k.Key.Row, k.Key.Col);
    }

    // =========================================================================
    // Happy path
    // =========================================================================

    [Fact]
    public async Task Save_HappyPath_WritesAllAndClearsSession()
    {
        var vm = CreateVmWithConfig();
        await EnterEditAsync(vm);

        var (r, c) = FirstKeyOnLayer(vm, 0);
        vm.ApplyKeyEdit(0, r, c, 0x0020);
        var (r2, c2) = FirstKeyOnLayer(vm, 1);
        vm.ApplyKeyEdit(1, r2, c2, 0x0021);

        Assert.Equal(2, vm.DirtyCount);
        await vm.SaveEditCommand.ExecuteAsync(null);

        Assert.Equal(2, _protocol.SetKeycodeCalls.Count);
        Assert.True(vm.IsEditMode);            // stay in edit mode
        Assert.Equal(0, vm.DirtyCount);         // cleared
        Assert.NotNull(vm.EditSession);         // fresh session rebuilt on top of reloaded baseline
        Assert.Empty(vm.EditSession!.PendingChanges);
        Assert.False(vm.IsSaving);
    }

    [Fact]
    public async Task Save_TwoEditsOnSameKey_VmInstancesStayStable()
    {
        // After save, the existing LayerViewModel / KeyViewModel instances
        // must be updated in place rather than replaced. The creative thumb
        // layout binds Borders to specific KeyViewModel instances via
        // DataContext paths that do not re-resolve cleanly when the VM tree
        // is rebuilt, so the first post-save edit would appear frozen until
        // app restart.
        var vm = CreateVmWithConfig();
        await EnterEditAsync(vm);

        var (r, c) = FirstKeyOnLayer(vm, 0);

        vm.ApplyKeyEdit(0, r, c, 0x0020);
        var layerVmBefore = vm.Layers.First(l => l.Index == 0);
        var keyVmBefore = layerVmBefore.Keys.First(k => k.Key.Row == r && k.Key.Col == c);

        await vm.SaveEditCommand.ExecuteAsync(null);

        var layerVmAfter = vm.Layers.First(l => l.Index == 0);
        var keyVmAfter = layerVmAfter.Keys.First(k => k.Key.Row == r && k.Key.Col == c);

        Assert.Same(layerVmBefore, layerVmAfter);
        Assert.Same(keyVmBefore, keyVmAfter);
        Assert.Equal((ushort)0x0020, keyVmAfter.Key.RawKeycode);
    }

    [Fact]
    public async Task Save_TwoEditsOnSameKey_LabelReflectsSecondEdit()
    {
        // Reported bug: change key X to A and save — label shows A. Change
        // the same key to B and save — label still shows A until restart.
        // Nail down which layer the bug lives in by asserting on the key VM
        // label (rendered-side) and the session baseline (data-side).
        var vm = CreateVmWithConfig();
        await EnterEditAsync(vm);

        var (r, c) = FirstKeyOnLayer(vm, 0);

        // First edit: 0x0020 (Space)
        vm.ApplyKeyEdit(0, r, c, 0x0020);
        await vm.SaveEditCommand.ExecuteAsync(null);

        // Second edit on same cell: 0x0029 (Esc)
        vm.ApplyKeyEdit(0, r, c, 0x0029);
        await vm.SaveEditCommand.ExecuteAsync(null);

        // Session-level checks.
        Assert.Equal((ushort)0x0029, vm.EditSession!.GetCurrent(0, r, c));
        Assert.Equal((ushort)0x0029, vm.EditSession.GetBaseline(0, r, c));

        // KeyboardConfig / VM-layer checks — what the View binds to.
        var configKey = vm.KeyboardConfig!.Layers.First(l => l.Index == 0)
            .Keys.First(k => k.Row == r && k.Col == c);
        Assert.Equal((ushort)0x0029, configKey.RawKeycode);

        var keyVm = vm.Layers.First(l => l.Index == 0)
            .Keys.First(k => k.Key.Row == r && k.Key.Col == c);
        Assert.Equal((ushort)0x0029, keyVm.Key.RawKeycode);
        Assert.False(keyVm.IsPending);
        // The baseline/display label should reflect the second value, not the first.
        Assert.Equal("Esc", keyVm.DisplayLabel);
    }

    [Fact]
    public async Task Save_ThenApplyAnotherEdit_Works()
    {
        // Regression: after a successful save the user must be able to make
        // another edit without restarting the app. The bug was that
        // ApplySaveResult(SaveSuccess) nulled _editSession while leaving
        // IsEditMode = true, which caused OnKeyClicked to silently no-op.
        var vm = CreateVmWithConfig();
        await EnterEditAsync(vm);

        var (r, c) = FirstKeyOnLayer(vm, 0);
        vm.ApplyKeyEdit(0, r, c, 0x0020);
        await vm.SaveEditCommand.ExecuteAsync(null);

        var (r2, c2) = FirstKeyOnLayer(vm, 1);
        vm.ApplyKeyEdit(1, r2, c2, 0x0021);

        Assert.NotNull(vm.EditSession);
        Assert.Equal(1, vm.DirtyCount);
        Assert.Equal((ushort)0x0021, vm.EditSession!.GetCurrent(1, r2, c2));
        Assert.True(vm.SaveEditCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_HappyPath_CapturesPreAndPostSnapshots()
    {
        var vm = CreateVmWithConfig();
        await EnterEditAsync(vm);

        var (r, c) = FirstKeyOnLayer(vm, 0);
        vm.ApplyKeyEdit(0, r, c, 0x0020);

        await vm.SaveEditCommand.ExecuteAsync(null);

        var snapshots = await _snapshots.ListAsync();
        Assert.Contains(snapshots, s => s.Reason == SnapshotReason.PreSave);
        Assert.Contains(snapshots, s => s.Reason == SnapshotReason.PostSave);
    }

    [Fact]
    public async Task Save_HappyPath_RefreshesBaselineFromDevice()
    {
        var vm = CreateVmWithConfig();
        await EnterEditAsync(vm);

        var (r, c) = FirstKeyOnLayer(vm, 0);
        vm.ApplyKeyEdit(0, r, c, 0x0042);

        await vm.SaveEditCommand.ExecuteAsync(null);

        // After reload, KeyboardConfig should reflect the new value on the device.
        var layer = vm.KeyboardConfig!.Layers.First(l => l.Index == 0);
        var key = layer.Keys.First(k => k.Row == r && k.Col == c);
        Assert.Equal((ushort)0x0042, key.RawKeycode);
    }

    [Fact]
    public async Task Save_HappyPath_RelocksWhenDeviceWasLockedBeforeEdit()
    {
        var vm = CreateVmWithConfig();
        await EnterEditAsync(vm, lockedBeforeEdit: true);

        var (r, c) = FirstKeyOnLayer(vm, 0);
        vm.ApplyKeyEdit(0, r, c, 0x0020);

        Assert.Equal(0, _protocol.LockCount);
        await vm.SaveEditCommand.ExecuteAsync(null);
        Assert.Equal(1, _protocol.LockCount);
    }

    [Fact]
    public async Task Save_HappyPath_DoesNotLockWhenDeviceWasAlreadyUnlocked()
    {
        var vm = CreateVmWithConfig();
        await EnterEditAsync(vm, lockedBeforeEdit: false);

        var (r, c) = FirstKeyOnLayer(vm, 0);
        vm.ApplyKeyEdit(0, r, c, 0x0020);

        await vm.SaveEditCommand.ExecuteAsync(null);
        Assert.Equal(0, _protocol.LockCount);
    }

    // =========================================================================
    // Disabled / empty
    // =========================================================================

    [Fact]
    public async Task Save_EmptyPending_IsDisabled()
    {
        var vm = CreateVmWithConfig();
        await EnterEditAsync(vm);

        Assert.Equal(0, vm.DirtyCount);
        Assert.False(vm.SaveEditCommand.CanExecute(null));
    }

    // =========================================================================
    // Mid-batch failure → partial recovery
    // =========================================================================

    [Fact]
    public async Task Save_FirstOpFails_RecoveryPreservesAllPending()
    {
        var vm = CreateVmWithConfig();
        await EnterEditAsync(vm);

        var (r1, c1) = FirstKeyOnLayer(vm, 0);
        vm.ApplyKeyEdit(0, r1, c1, 0x0020);
        var layerVm1 = vm.Layers.First(l => l.Index == 1);
        var keyOnL1 = layerVm1.Keys[0];
        vm.ApplyKeyEdit(1, keyOnL1.Key.Row, keyOnL1.Key.Col, 0x0021);

        SavePartial? partial = null;
        vm.SaveCompletedCallback = r => partial = r as SavePartial;
        _protocol.SetKeycodeFailAt = (0, new IOException("boom"));

        await vm.SaveEditCommand.ExecuteAsync(null);

        Assert.NotNull(partial);
        Assert.Equal(0, partial!.Applied);
        Assert.Equal(2, partial.StillPending);
        Assert.Equal(0, partial.Diverged);
        Assert.Equal(2, vm.DirtyCount); // session rebuilt with both edits
        Assert.NotNull(vm.EditSession);
        Assert.True(vm.IsEditMode);

        // Zero writes landed → session current = baseline + both pending edits.
        var expected = BuildBaselineLike(2, 0, 0);
        expected[0, r1, c1] = 0x0020;
        expected[1, keyOnL1.Key.Row, keyOnL1.Key.Col] = 0x0021;
        AssertKeymapEquals(expected, vm.EditSession!.CloneCurrent());
    }

    [Fact]
    public async Task Save_MiddleOpFails_RecoveryReportsAppliedAndPending()
    {
        // Vary MO position to (2, 3) to prove the test doesn't depend on the
        // fixture's default MO(0,0) — targets filter skips that cell below.
        const int moR = 2, moC = 3;
        var vm = CreateVmWithConfig(moRow: moR, moCol: moC);
        await EnterEditAsync(vm);

        var layerVm0 = vm.Layers.First(l => l.Index == 0);
        var targets = layerVm0.Keys
            .Where(k => !(k.Key.Row == moR && k.Key.Col == moC))
            .Take(3).ToList();
        foreach (var (k, i) in targets.Select((k, i) => (k, i)))
            vm.ApplyKeyEdit(0, k.Key.Row, k.Key.Col, (ushort)(0x0030 + i));

        SavePartial? partial = null;
        vm.SaveCompletedCallback = r => partial = r as SavePartial;
        _protocol.SetKeycodeFailAt = (1, new InvalidOperationException("mid-batch"));

        await vm.SaveEditCommand.ExecuteAsync(null);

        Assert.NotNull(partial);
        Assert.Equal(1, partial!.Applied);
        Assert.Equal(2, partial.StillPending);
        Assert.Equal(2, vm.DirtyCount);

        // targets[0] landed (baseline for that cell is now 0x0030); targets[1]
        // and [2] still pending on top of unchanged baselines.
        var expected = BuildBaselineLike(2, moR, moC);
        expected[0, targets[0].Key.Row, targets[0].Key.Col] = 0x0030;
        expected[0, targets[1].Key.Row, targets[1].Key.Col] = 0x0031;
        expected[0, targets[2].Key.Row, targets[2].Key.Col] = 0x0032;
        AssertKeymapEquals(expected, vm.EditSession!.CloneCurrent());
    }

    [Fact]
    public async Task Save_LastOpFails_RecoveryReportsCorrectCounts()
    {
        var vm = CreateVmWithConfig();
        await EnterEditAsync(vm);

        var layerVm0 = vm.Layers.First(l => l.Index == 0);
        var targets = layerVm0.Keys
            .Where(k => !(k.Key.Row == 0 && k.Key.Col == 0))
            .Take(3).ToList();
        foreach (var (k, i) in targets.Select((k, i) => (k, i)))
            vm.ApplyKeyEdit(0, k.Key.Row, k.Key.Col, (ushort)(0x0030 + i));

        SavePartial? partial = null;
        vm.SaveCompletedCallback = r => partial = r as SavePartial;
        _protocol.SetKeycodeFailAt = (2, new IOException("late"));

        await vm.SaveEditCommand.ExecuteAsync(null);

        Assert.NotNull(partial);
        Assert.Equal(2, partial!.Applied);
        Assert.Equal(1, partial.StillPending);
        Assert.Equal(1, vm.DirtyCount);

        // targets[0], [1] landed; targets[2] still pending on reloaded baseline.
        var expected = BuildBaselineLike(2, 0, 0);
        expected[0, targets[0].Key.Row, targets[0].Key.Col] = 0x0030;
        expected[0, targets[1].Key.Row, targets[1].Key.Col] = 0x0031;
        expected[0, targets[2].Key.Row, targets[2].Key.Col] = 0x0032;
        AssertKeymapEquals(expected, vm.EditSession!.CloneCurrent());
    }

    [Fact]
    public async Task Save_PartialRecovery_RebuildsEditSessionFromReloadedBaseline()
    {
        var vm = CreateVmWithConfig();
        await EnterEditAsync(vm);

        var layerVm0 = vm.Layers.First(l => l.Index == 0);
        var targets = layerVm0.Keys
            .Where(k => !(k.Key.Row == 0 && k.Key.Col == 0))
            .Take(2).ToList();
        vm.ApplyKeyEdit(0, targets[0].Key.Row, targets[0].Key.Col, 0x0040);
        vm.ApplyKeyEdit(0, targets[1].Key.Row, targets[1].Key.Col, 0x0041);

        _protocol.SetKeycodeFailAt = (1, new IOException("boom"));
        await vm.SaveEditCommand.ExecuteAsync(null);

        // Session should now be sitting on top of the reloaded device baseline:
        // the first write landed, so the device value for that cell is 0x0040.
        // The session's baseline for that cell is also 0x0040 (nothing pending),
        // but the second cell is still baseline (0x04) with a pending 0x0041.
        Assert.NotNull(vm.EditSession);
        Assert.Equal((ushort)0x0040, vm.EditSession!.GetBaseline(0, targets[0].Key.Row, targets[0].Key.Col));
        Assert.Equal((ushort)0x0040, vm.EditSession.GetCurrent(0, targets[0].Key.Row, targets[0].Key.Col));
        Assert.Equal((ushort)0x0041, vm.EditSession.GetCurrent(0, targets[1].Key.Row, targets[1].Key.Col));

        // Full-keymap check: baseline for targets[0] advanced to 0x0040, targets[1] still pending.
        var expected = BuildBaselineLike(2, 0, 0);
        expected[0, targets[0].Key.Row, targets[0].Key.Col] = 0x0040;
        expected[0, targets[1].Key.Row, targets[1].Key.Col] = 0x0041;
        AssertKeymapEquals(expected, vm.EditSession!.CloneCurrent());
    }

    // =========================================================================
    // Safety warnings
    // =========================================================================

    [Fact]
    public async Task Save_SafetyWarnings_ConfirmationDenied_AbortsWithoutWrites()
    {
        // Make layer 1 become unreachable: set all its keys to something
        // non-KC_NO (triggers the "layer has content" branch) AND overwrite
        // the MO(1) on layer 0 with a plain key so nothing points to L1.
        var vm = CreateVmWithConfig();
        await EnterEditAsync(vm);

        vm.ApplyKeyEdit(0, 0, 0, 0x0020); // overwrite MO(1) → L1 unreachable
        var (r, c) = FirstKeyOnLayer(vm, 1);
        vm.ApplyKeyEdit(1, r, c, 0x0022);

        var asked = false;
        vm.ConfirmSafetyWarningsRequested = _ => { asked = true; return Task.FromResult(false); };

        SaveResult? result = null;
        vm.SaveCompletedCallback = r2 => result = r2;

        await vm.SaveEditCommand.ExecuteAsync(null);

        Assert.True(asked);
        Assert.Empty(_protocol.SetKeycodeCalls);
        Assert.IsType<SaveAborted>(result);
        Assert.True(vm.DirtyCount > 0); // session intact so user can fix the warning
    }

    [Fact]
    public async Task Save_SafetyWarnings_ConfirmationGranted_Proceeds()
    {
        var vm = CreateVmWithConfig();
        await EnterEditAsync(vm);

        vm.ApplyKeyEdit(0, 0, 0, 0x0020);
        var (r, c) = FirstKeyOnLayer(vm, 1);
        vm.ApplyKeyEdit(1, r, c, 0x0022);

        vm.ConfirmSafetyWarningsRequested = _ => Task.FromResult(true);

        SaveResult? result = null;
        vm.SaveCompletedCallback = r2 => result = r2;

        await vm.SaveEditCommand.ExecuteAsync(null);

        Assert.IsType<SaveSuccess>(result);
        Assert.Equal(2, _protocol.SetKeycodeCalls.Count);
    }

    [Fact]
    public async Task Save_SafetyWarnings_NoCallback_AutoConfirms()
    {
        var vm = CreateVmWithConfig();
        await EnterEditAsync(vm);

        vm.ApplyKeyEdit(0, 0, 0, 0x0020);
        var (r, c) = FirstKeyOnLayer(vm, 1);
        vm.ApplyKeyEdit(1, r, c, 0x0022);

        // ConfirmSafetyWarningsRequested stays null → auto-confirm.
        SaveResult? result = null;
        vm.SaveCompletedCallback = r2 => result = r2;

        await vm.SaveEditCommand.ExecuteAsync(null);

        Assert.IsType<SaveSuccess>(result);
        Assert.Equal(2, _protocol.SetKeycodeCalls.Count);
    }

    // =========================================================================
    // Divergence
    // =========================================================================

    [Fact]
    public async Task Save_DivergedDeviceState_ReportedInPartial()
    {
        var vm = CreateVmWithConfig();
        await EnterEditAsync(vm);

        var layerVm0 = vm.Layers.First(l => l.Index == 0);
        var targets = layerVm0.Keys
            .Where(k => !(k.Key.Row == 0 && k.Key.Col == 0))
            .Take(2).ToList();
        vm.ApplyKeyEdit(0, targets[0].Key.Row, targets[0].Key.Col, 0x0040);
        vm.ApplyKeyEdit(0, targets[1].Key.Row, targets[1].Key.Col, 0x0041);

        // SetKeycodeFailAt fires on index 1 (second write). Before it throws,
        // mutate the fake's keymap on the second cell directly so the reload
        // returns a value that matches neither baseline (0x04) nor intent (0x0041).
        _protocol.SetKeycodeFailAt = (1, new IOException("late"));
        // Simulate divergence: the second cell ends up holding 0x00FF on the device.
        _protocol.Keymap![0, targets[1].Key.Row, targets[1].Key.Col] = 0x00FF;

        SavePartial? partial = null;
        vm.SaveCompletedCallback = r => partial = r as SavePartial;

        await vm.SaveEditCommand.ExecuteAsync(null);

        Assert.NotNull(partial);
        Assert.Equal(1, partial!.Applied);   // first write landed
        Assert.Equal(0, partial.StillPending); // second cell isn't at baseline
        Assert.Equal(1, partial.Diverged);

        // Session sits on the reloaded baseline: targets[0]=0x0040 (applied),
        // targets[1]=0x00FF (diverged — device value neither baseline nor intent).
        var expected = BuildBaselineLike(2, 0, 0);
        expected[0, targets[0].Key.Row, targets[0].Key.Col] = 0x0040;
        expected[0, targets[1].Key.Row, targets[1].Key.Col] = 0x00FF;
        AssertKeymapEquals(expected, vm.EditSession!.CloneCurrent());
    }

    // =========================================================================
    // IsSaving gate
    // =========================================================================

    [Fact]
    public async Task Save_IsSavingToggle_DisablesCommandDuringRun()
    {
        var vm = CreateVmWithConfig();
        await EnterEditAsync(vm);

        var (r, c) = FirstKeyOnLayer(vm, 0);
        vm.ApplyKeyEdit(0, r, c, 0x0020);

        Assert.True(vm.SaveEditCommand.CanExecute(null));
        vm.IsSaving = true;
        Assert.False(vm.SaveEditCommand.CanExecute(null));
        vm.IsSaving = false;
        Assert.True(vm.SaveEditCommand.CanExecute(null));
    }
}
