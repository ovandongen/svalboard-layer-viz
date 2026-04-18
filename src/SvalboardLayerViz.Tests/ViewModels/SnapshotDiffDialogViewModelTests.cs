using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.History;
using SvalboardLayerViz.Core.Keymap;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

public class SnapshotDiffDialogViewModelTests
{
    private static readonly KeyboardId TestKbId = new("1234", "5678", "ABCDEF0123456789");

    private static KeymapSnapshot MakeSnapshot(
        ushort[,,] keymap,
        KeyboardId? kbId = null,
        string? userLabel = null)
    {
        return new KeymapSnapshot
        {
            CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Reason = SnapshotReason.PreSave,
            KeyboardId = kbId ?? TestKbId,
            DeviceName = "TestBoard",
            UserLabel = userLabel,
            LayerCount = keymap.GetLength(0),
            MatrixRows = keymap.GetLength(1),
            MatrixCols = keymap.GetLength(2),
            Layers = KeymapSnapshot.StructureKeymap(keymap),
        };
    }

    private static ushort[,,] MakeKeymap(int layers, int rows, int cols, ushort fill = 0)
    {
        var km = new ushort[layers, rows, cols];
        for (var l = 0; l < layers; l++)
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                    km[l, r, c] = fill;
        return km;
    }

    [Fact]
    public void Constructor_ComputesCorrectDiffCount()
    {
        var device = MakeKeymap(2, 10, 6);
        var snapshotKm = MakeKeymap(2, 10, 6);
        snapshotKm[0, 1, 0] = 0x0004; // KC_A
        snapshotKm[0, 2, 1] = 0x0005; // KC_B
        snapshotKm[1, 3, 2] = 0x0006; // KC_C
        var snapshot = MakeSnapshot(snapshotKm);

        var vm = new SnapshotDiffDialogViewModel(
            snapshot, device, TestKbId, new KeycodeService());

        Assert.Equal(3, vm.ChangeCount);
        Assert.Equal(3, vm.Diffs.Count);
        Assert.Equal(3, vm.ChangeLines.Count);
        Assert.True(vm.HasChanges);
    }

    [Fact]
    public void Constructor_IdenticalKeymaps_NoChanges()
    {
        var keymap = MakeKeymap(2, 10, 6, fill: 0x0004);
        var snapshot = MakeSnapshot(keymap);

        var vm = new SnapshotDiffDialogViewModel(
            snapshot, keymap, TestKbId, new KeycodeService());

        Assert.Equal(0, vm.ChangeCount);
        Assert.False(vm.HasChanges);
    }

    [Fact]
    public void Constructor_NullDeviceKeymap_EmptyDiff()
    {
        var snapshotKm = MakeKeymap(2, 10, 6, fill: 0x0004);
        var snapshot = MakeSnapshot(snapshotKm);

        var vm = new SnapshotDiffDialogViewModel(
            snapshot, null, null, new KeycodeService(),
            isDeviceConnected: false);

        Assert.Equal(0, vm.ChangeCount);
        Assert.False(vm.CanRestore);
    }

    [Fact]
    public void LayerSync_OnByDefault_SyncsSelectors()
    {
        var device = MakeKeymap(4, 10, 6);
        var snapshotKm = MakeKeymap(4, 10, 6);
        snapshotKm[2, 1, 0] = 0x0004;
        var snapshot = MakeSnapshot(snapshotKm);

        var vm = new SnapshotDiffDialogViewModel(
            snapshot, device, TestKbId, new KeycodeService());

        Assert.True(vm.SyncLayers);
        vm.SnapshotSelectedLayerIndex = 2;
        Assert.Equal(2, vm.DeviceSelectedLayerIndex);
    }

    [Fact]
    public void LayerSync_Off_DoesNotSync()
    {
        var device = MakeKeymap(4, 10, 6);
        var snapshot = MakeSnapshot(MakeKeymap(4, 10, 6));

        var vm = new SnapshotDiffDialogViewModel(
            snapshot, device, TestKbId, new KeycodeService());

        vm.SyncLayers = false;
        vm.SnapshotSelectedLayerIndex = 3;
        Assert.Equal(0, vm.DeviceSelectedLayerIndex);
    }

    [Fact]
    public void MiniBoard_HighlightsChangedKeys()
    {
        var device = MakeKeymap(1, 10, 6);
        var snapshotKm = MakeKeymap(1, 10, 6);
        snapshotKm[0, 1, 0] = 0x0004;
        var snapshot = MakeSnapshot(snapshotKm);

        var vm = new SnapshotDiffDialogViewModel(
            snapshot, device, TestKbId, new KeycodeService());

        // At least one key should be highlighted in both boards
        Assert.Contains(vm.SnapshotKeys, k => k.IsChanged);
        Assert.Contains(vm.DeviceKeys, k => k.IsChanged);
        // Non-changed keys exist too
        Assert.Contains(vm.SnapshotKeys, k => !k.IsChanged);
    }

    [Fact]
    public async Task RestoreCommand_CreatesSessionWithCorrectPendingChanges()
    {
        var device = MakeKeymap(2, 10, 6);
        var snapshotKm = MakeKeymap(2, 10, 6);
        snapshotKm[0, 1, 0] = 0x0004;
        snapshotKm[0, 2, 1] = 0x0005;
        var snapshot = MakeSnapshot(snapshotKm);

        var vm = new SnapshotDiffDialogViewModel(
            snapshot, device, TestKbId, new KeycodeService(),
            isDeviceConnected: true, hasPendingChanges: false);

        KeymapEditSession? capturedSession = null;
        vm.RestoreCompleted = s => capturedSession = s;
        vm.ConfirmRestore = () => Task.FromResult(true);
        var closeCalled = false;
        vm.CloseRequested = () => closeCalled = true;

        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.NotNull(capturedSession);
        Assert.Equal(2, capturedSession!.PendingChanges.Count);
        Assert.True(closeCalled);
    }

    [Fact]
    public async Task RestoreCommand_NotExecutable_WhenCanRestoreIsFalse()
    {
        var device = MakeKeymap(1, 10, 6);
        var snapshot = MakeSnapshot(MakeKeymap(1, 10, 6));

        var vm = new SnapshotDiffDialogViewModel(
            snapshot, device, TestKbId, new KeycodeService(),
            isDeviceConnected: false, hasPendingChanges: false);

        Assert.False(vm.CanRestore);

        // Should not fire RestoreCompleted
        KeymapEditSession? capturedSession = null;
        vm.RestoreCompleted = s => capturedSession = s;
        vm.ConfirmRestore = () => Task.FromResult(true);

        await vm.RestoreCommand.ExecuteAsync(null);
        Assert.Null(capturedSession);
    }

    [Fact]
    public void FirmwareMismatch_ShowsWarning()
    {
        var device = MakeKeymap(1, 10, 6);
        var differentKbId = new KeyboardId("1234", "5678", "DIFFERENT_UID");
        var snapshotKm = MakeKeymap(1, 10, 6);
        snapshotKm[0, 1, 0] = 0x0004;
        var snapshot = MakeSnapshot(snapshotKm, kbId: differentKbId);

        var vm = new SnapshotDiffDialogViewModel(
            snapshot, device, TestKbId, new KeycodeService());

        Assert.True(vm.HasWarning);
        Assert.NotEmpty(vm.WarningBanner);
    }

    [Fact]
    public void LayerCountMismatch_Truncates()
    {
        // Snapshot has 16 layers, device has 8
        var device = MakeKeymap(8, 10, 6);
        var snapshotKm = MakeKeymap(16, 10, 6);
        // Change on layer 5 (within device range)
        snapshotKm[5, 1, 0] = 0x0004;
        // Change on layer 12 (beyond device range — should be ignored)
        snapshotKm[12, 1, 0] = 0x0005;
        var snapshot = MakeSnapshot(snapshotKm);

        var vm = new SnapshotDiffDialogViewModel(
            snapshot, device, TestKbId, new KeycodeService());

        Assert.Equal(8, vm.LayerCount);
        Assert.True(vm.HasWarning);
        // Only the layer 5 change should show up
        Assert.Equal(1, vm.ChangeCount);
        Assert.All(vm.Diffs, d => Assert.True(d.Layer < 8));
    }

    [Fact]
    public void ChangeLines_FormattedCorrectly()
    {
        var device = MakeKeymap(1, 10, 6);
        var snapshotKm = MakeKeymap(1, 10, 6);
        snapshotKm[0, 1, 0] = 0x0004; // KC_A
        var snapshot = MakeSnapshot(snapshotKm);

        var vm = new SnapshotDiffDialogViewModel(
            snapshot, device, TestKbId, new KeycodeService());

        Assert.Single(vm.ChangeLines);
        var line = vm.ChangeLines[0];
        Assert.Equal(0, line.Layer);
        Assert.Equal(1, line.Row);
        Assert.Equal(0, line.Col);
        // Check format contains the arrow
        Assert.Contains("→", line.FormattedText);
    }

    [Fact]
    public void RestoreDisabledReason_PendingChanges()
    {
        var device = MakeKeymap(1, 10, 6);
        var snapshot = MakeSnapshot(MakeKeymap(1, 10, 6));

        var vm = new SnapshotDiffDialogViewModel(
            snapshot, device, TestKbId, new KeycodeService(),
            isDeviceConnected: true, hasPendingChanges: true);

        Assert.False(vm.CanRestore);
        Assert.NotEmpty(vm.RestoreDisabledReason);
    }

    [Fact]
    public void RestoreDisabledReason_NoDevice()
    {
        var device = MakeKeymap(1, 10, 6);
        var snapshot = MakeSnapshot(MakeKeymap(1, 10, 6));

        var vm = new SnapshotDiffDialogViewModel(
            snapshot, device, TestKbId, new KeycodeService(),
            isDeviceConnected: false, hasPendingChanges: false);

        Assert.False(vm.CanRestore);
        Assert.NotEmpty(vm.RestoreDisabledReason);
    }

    [Fact]
    public async Task RestoreCommand_ConfirmDeclined_DoesNotRestore()
    {
        var device = MakeKeymap(1, 10, 6);
        var snapshotKm = MakeKeymap(1, 10, 6);
        snapshotKm[0, 1, 0] = 0x0004;
        var snapshot = MakeSnapshot(snapshotKm);

        var vm = new SnapshotDiffDialogViewModel(
            snapshot, device, TestKbId, new KeycodeService(),
            isDeviceConnected: true, hasPendingChanges: false);

        KeymapEditSession? capturedSession = null;
        vm.RestoreCompleted = s => capturedSession = s;
        vm.ConfirmRestore = () => Task.FromResult(false); // user declines

        await vm.RestoreCommand.ExecuteAsync(null);
        Assert.Null(capturedSession);
    }

    // --- MacroBuffer in restore ---

    [Fact]
    public async Task RestoreCommand_WithMacroBuffer_RestoresMacros()
    {
        var device = MakeKeymap(2, 10, 6);
        var snapshotKm = MakeKeymap(2, 10, 6);
        snapshotKm[0, 1, 0] = 0x0004;
        var deviceMacro = new byte[] { 0x01, 0x02, 0x00, 0x00 };
        var snapshotMacro = new byte[] { 0x04, 0x05, 0x00, 0x00 };
        var snapshot = MakeSnapshot(snapshotKm) with { MacroBuffer = snapshotMacro };

        var vm = new SnapshotDiffDialogViewModel(
            snapshot, device, TestKbId, new KeycodeService(),
            isDeviceConnected: true, hasPendingChanges: false,
            currentDeviceMacroBuffer: deviceMacro);

        KeymapEditSession? capturedSession = null;
        vm.RestoreCompleted = s => capturedSession = s;
        vm.ConfirmRestore = () => Task.FromResult(true);
        vm.CloseRequested = () => { };

        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.NotNull(capturedSession);
        Assert.True(capturedSession!.HasPendingMacroChanges);
        Assert.Equal(snapshotMacro, capturedSession.GetCurrentMacroBuffer());
    }

    [Fact]
    public async Task RestoreCommand_NullMacroBuffers_NoMacroChanges()
    {
        var device = MakeKeymap(2, 10, 6);
        var snapshotKm = MakeKeymap(2, 10, 6);
        snapshotKm[0, 1, 0] = 0x0004;
        var snapshot = MakeSnapshot(snapshotKm);

        var vm = new SnapshotDiffDialogViewModel(
            snapshot, device, TestKbId, new KeycodeService(),
            isDeviceConnected: true, hasPendingChanges: false,
            currentDeviceMacroBuffer: null);

        KeymapEditSession? capturedSession = null;
        vm.RestoreCompleted = s => capturedSession = s;
        vm.ConfirmRestore = () => Task.FromResult(true);
        vm.CloseRequested = () => { };

        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.NotNull(capturedSession);
        Assert.False(capturedSession!.HasPendingMacroChanges);
    }

    [Fact]
    public async Task RestoreCommand_IdenticalMacroBuffers_NoMacroChanges()
    {
        var device = MakeKeymap(1, 10, 6);
        var snapshotKm = MakeKeymap(1, 10, 6);
        snapshotKm[0, 1, 0] = 0x0004;
        var macroBuffer = new byte[] { 0x01, 0x02, 0x00 };
        var snapshot = MakeSnapshot(snapshotKm) with { MacroBuffer = macroBuffer };

        var vm = new SnapshotDiffDialogViewModel(
            snapshot, device, TestKbId, new KeycodeService(),
            isDeviceConnected: true, hasPendingChanges: false,
            currentDeviceMacroBuffer: (byte[])macroBuffer.Clone());

        KeymapEditSession? capturedSession = null;
        vm.RestoreCompleted = s => capturedSession = s;
        vm.ConfirmRestore = () => Task.FromResult(true);
        vm.CloseRequested = () => { };

        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.NotNull(capturedSession);
        Assert.False(capturedSession!.HasPendingMacroChanges);
    }
}
