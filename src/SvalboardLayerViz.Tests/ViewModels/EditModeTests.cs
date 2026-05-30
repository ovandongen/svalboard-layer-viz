using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Models;
using SvalboardLayerViz.Core.Protocol;
using SvalboardLayerViz.Core.Settings;
using SvalboardLayerViz.Tests.Protocol;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

/// <summary>
/// Session 10 tests — in-memory edit session wiring.
/// Exercises state transitions, pending-change propagation, disconnect
/// teardown, and Save command disable without touching the picker dialog.
/// </summary>
public class EditModeTests
{
    private readonly FakeVialProtocolService _protocol = new();
    private readonly ISettingsService _settings;

    public EditModeTests()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"svz-test-{Guid.NewGuid():N}.json");
        _settings = new SettingsService(tempPath);
    }

    private MainWindowViewModel CreateVmWithConfig(int layerCount = 2)
    {
        var vm = new MainWindowViewModel(_settings, _protocol);
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
            // Layer 0 gets real keycodes (KC_A = 0x04) so the LayerViewModel
            // doesn't skip it as "all KC_NO". Other layers use 0x05 so they
            // also stay visible but differ from the baseline.
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

    // --- Entering edit mode ---

    [Fact]
    public async Task EnterEdit_WhenAlreadyUnlocked_EntersEditModeDirectly()
    {
        _protocol.NextUnlockStatus = new UnlockStatus(Unlocked: true, InProgress: false, []);
        var vm = CreateVmWithConfig();

        await vm.EnterEditCommand.ExecuteAsync(null);

        Assert.True(vm.IsEditMode);
        Assert.Equal(0, vm.DirtyCount);
        Assert.NotNull(vm.EditSession);
    }

    [Fact]
    public async Task EnterEdit_WhenLocked_InvokesOpenUnlockRequested()
    {
        _protocol.NextUnlockStatus = new UnlockStatus(Unlocked: false, InProgress: false, [(0, 0), (1, 1)]);
        var vm = CreateVmWithConfig();

        Action? captured = null;
        var host = new FakeAppHost { OnOpenUnlock = onUnlocked => captured = onUnlocked };
        vm.AttachHost(host, host);

        await vm.EnterEditCommand.ExecuteAsync(null);

        Assert.NotNull(captured);
        Assert.False(vm.IsEditMode);

        captured!();
        Assert.True(vm.IsEditMode);
    }

    [Fact]
    public async Task EnterEdit_WhenNotConnected_DoesNothing()
    {
        _protocol.NextUnlockStatus = new UnlockStatus(Unlocked: true, InProgress: false, []);
        var vm = CreateVmWithConfig();
        vm.IsConnected = false;

        await vm.EnterEditCommand.ExecuteAsync(null);

        Assert.False(vm.IsEditMode);
    }

    // --- Apply / Discard ---

    [Fact]
    public async Task ApplyKeyEdit_MarksKeyPendingAndBumpsDirtyCount()
    {
        _protocol.NextUnlockStatus = new UnlockStatus(Unlocked: true, InProgress: false, []);
        var vm = CreateVmWithConfig();
        await vm.EnterEditCommand.ExecuteAsync(null);

        var layerVm = vm.Layers.First(l => l.Index == 0);
        var firstKey = layerVm.Keys[0];
        var row = firstKey.Key.Row;
        var col = firstKey.Key.Col;

        vm.ApplyKeyEdit(0, row, col, 0x42);

        Assert.Equal(1, vm.DirtyCount);
        Assert.True(firstKey.IsPending);
        Assert.Equal(0x42, firstKey.PendingKeycode);
        Assert.False(string.IsNullOrEmpty(firstKey.PendingLabel));
        Assert.Equal(firstKey.PendingLabel, firstKey.DisplayLabel);
    }

    [Fact]
    public async Task ApplyKeyEdit_RestoringBaseline_ClearsPending()
    {
        _protocol.NextUnlockStatus = new UnlockStatus(Unlocked: true, InProgress: false, []);
        var vm = CreateVmWithConfig();
        await vm.EnterEditCommand.ExecuteAsync(null);

        var layerVm = vm.Layers.First(l => l.Index == 0);
        var firstKey = layerVm.Keys[0];
        var row = firstKey.Key.Row;
        var col = firstKey.Key.Col;
        var baseline = firstKey.Key.RawKeycode;

        vm.ApplyKeyEdit(0, row, col, 0x42);
        vm.ApplyKeyEdit(0, row, col, baseline);

        Assert.Equal(0, vm.DirtyCount);
        Assert.False(firstKey.IsPending);
    }

    [Fact]
    public async Task DiscardEdit_ClearsPendingAndExitsEditMode()
    {
        _protocol.NextUnlockStatus = new UnlockStatus(Unlocked: true, InProgress: false, []);
        var vm = CreateVmWithConfig();
        await vm.EnterEditCommand.ExecuteAsync(null);

        var layerVm = vm.Layers.First(l => l.Index == 0);
        var firstKey = layerVm.Keys[0];
        vm.ApplyKeyEdit(0, firstKey.Key.Row, firstKey.Key.Col, 0x42);

        vm.DiscardEditCommand.Execute(null);

        Assert.False(vm.IsEditMode);
        Assert.Equal(0, vm.DirtyCount);
        Assert.Null(vm.EditSession);
        // Layer VMs are rebuilt on discard (to hide empty layers), so re-fetch
        var freshKey = vm.Layers.First(l => l.Index == 0).Keys[0];
        Assert.False(freshKey.IsPending);
    }

    [Fact]
    public async Task DiscardEdit_WithNoChanges_StillExitsEditMode()
    {
        _protocol.NextUnlockStatus = new UnlockStatus(Unlocked: true, InProgress: false, []);
        var vm = CreateVmWithConfig();
        await vm.EnterEditCommand.ExecuteAsync(null);

        vm.DiscardEditCommand.Execute(null);

        Assert.False(vm.IsEditMode);
        Assert.Null(vm.EditSession);
    }

    // --- Disconnect mid-edit ---

    [Fact]
    public async Task Disconnect_WhileEditing_DiscardsSessionAndExits()
    {
        _protocol.NextUnlockStatus = new UnlockStatus(Unlocked: true, InProgress: false, []);
        var vm = CreateVmWithConfig();
        await vm.EnterEditCommand.ExecuteAsync(null);

        var layerVm = vm.Layers.First(l => l.Index == 0);
        var firstKey = layerVm.Keys[0];
        vm.ApplyKeyEdit(0, firstKey.Key.Row, firstKey.Key.Col, 0x42);
        Assert.True(vm.IsEditMode);

        vm.IsConnected = false;

        Assert.False(vm.IsEditMode);
        Assert.Equal(0, vm.DirtyCount);
        Assert.Null(vm.EditSession);
        Assert.False(firstKey.IsPending);
    }

    // --- Polling gate ---

    [Fact]
    public async Task CanToggleLivePolling_IsFalseDuringEditMode()
    {
        _protocol.NextUnlockStatus = new UnlockStatus(Unlocked: true, InProgress: false, []);
        var vm = CreateVmWithConfig();

        Assert.True(vm.CanToggleLivePolling);

        await vm.EnterEditCommand.ExecuteAsync(null);
        Assert.False(vm.CanToggleLivePolling);

        vm.DiscardEditCommand.Execute(null);
        Assert.True(vm.CanToggleLivePolling);
    }

    // --- Save enable gating ---

    [Fact]
    public async Task SaveEditCommand_IsEnabledWhenDirtyAndNotSaving()
    {
        _protocol.NextUnlockStatus = new UnlockStatus(Unlocked: true, InProgress: false, []);
        var vm = CreateVmWithConfig();
        await vm.EnterEditCommand.ExecuteAsync(null);

        // No pending changes → disabled.
        Assert.False(vm.SaveEditCommand.CanExecute(null));

        var layerVm = vm.Layers.First(l => l.Index == 0);
        var firstKey = layerVm.Keys[0];
        vm.ApplyKeyEdit(0, firstKey.Key.Row, firstKey.Key.Col, 0x42);

        // Pending change + not saving → enabled.
        Assert.True(vm.SaveEditCommand.CanExecute(null));

        // IsSaving gate shuts it back off.
        vm.IsSaving = true;
        Assert.False(vm.SaveEditCommand.CanExecute(null));
    }

    // --- Empty layer visibility in edit mode ---

    /// <summary>
    /// Builds a config where layer 0 has real keycodes and layer 1 is all KC_NO.
    /// </summary>
    private static KeyboardConfig BuildConfigWithEmptyLayer()
    {
        var positions = SvalboardLayout.GetKeyPositions();
        var layers = new List<Layer>();
        for (var i = 0; i < 2; i++)
        {
            var code = i == 0 ? (ushort)0x04 : (ushort)0x0000;
            var keys = positions.Select(p => new Key
            {
                Row = p.Row, Col = p.Col, RawKeycode = code,
                DisplayLabel = code == 0 ? "" : $"K{code:X}",
                X = p.X, Y = p.Y, Width = p.Width, Height = p.Height,
            }).ToList();
            layers.Add(new Layer { Index = i, Keys = keys });
        }

        return new KeyboardConfig
        {
            DeviceName = "Test", VendorId = 0xCAFE, ProductId = 0xBABE,
            KeyboardId = 0, MatrixRows = 10, MatrixCols = 6, Layers = layers,
        };
    }

    [Fact]
    public void BuildLayerViewModels_HidesEmptyLayers_InViewMode()
    {
        var vm = new MainWindowViewModel(_settings, _protocol);
        vm.KeyboardConfig = BuildConfigWithEmptyLayer();
        vm.BuildLayerViewModels(_settings.Load());

        // Only layer 0 should be visible; empty layer 1 is filtered out.
        Assert.Single(vm.Layers);
        Assert.Equal(0, vm.Layers[0].Index);
    }

    [Fact]
    public async Task BuildLayerViewModels_ShowsEmptyLayers_InEditMode()
    {
        _protocol.NextUnlockStatus = new UnlockStatus(Unlocked: true, InProgress: false, []);
        var vm = new MainWindowViewModel(_settings, _protocol);
        vm.KeyboardConfig = BuildConfigWithEmptyLayer();
        vm.BuildLayerViewModels(_settings.Load());
        vm.IsConnected = true;

        await vm.EnterEditCommand.ExecuteAsync(null);

        // Both layers should be visible in edit mode, including the empty one.
        Assert.Equal(2, vm.Layers.Count);
        Assert.Contains(vm.Layers, l => l.Index == 0);
        Assert.Contains(vm.Layers, l => l.Index == 1);
    }

    [Fact]
    public async Task DiscardEdit_HidesEmptyLayers_AfterExit()
    {
        _protocol.NextUnlockStatus = new UnlockStatus(Unlocked: true, InProgress: false, []);
        var vm = new MainWindowViewModel(_settings, _protocol);
        vm.KeyboardConfig = BuildConfigWithEmptyLayer();
        vm.BuildLayerViewModels(_settings.Load());
        vm.IsConnected = true;

        await vm.EnterEditCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Layers.Count);

        vm.DiscardEditCommand.Execute(null);

        // Empty layer should be hidden again after exiting edit mode.
        Assert.Single(vm.Layers);
        Assert.Equal(0, vm.Layers[0].Index);
    }
}
