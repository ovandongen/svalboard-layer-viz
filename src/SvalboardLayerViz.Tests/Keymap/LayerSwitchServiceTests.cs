using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

/// <summary>
/// Exercises <see cref="LayerSwitchService"/> directly — BuildCache, ResolveTargetLayer
/// (hold-threshold + edge detection), and ResetRuntimeState. Replaces the old
/// AutoLayerSwitchTests which duplicated the algorithm inline rather than
/// testing the real service.
/// </summary>
public class LayerSwitchServiceTests
{
    private const int HoldThresholdMs = 50;

    private static Key MomentaryKey(int row, int col, int target) => new()
    {
        Row = row,
        Col = col,
        RawKeycode = 0x5100,
        DisplayLabel = $"MO({target})",
        IsLayerSwitch = true,
        TargetLayer = target,
        SwitchType = LayerSwitchType.Momentary,
    };

    private static Key ToggleKey(int row, int col, int target) => new()
    {
        Row = row,
        Col = col,
        RawKeycode = 0x5600,
        DisplayLabel = $"TG({target})",
        IsLayerSwitch = true,
        TargetLayer = target,
        SwitchType = LayerSwitchType.Toggle,
    };

    private static Key PlainKey(int row, int col) => new()
    {
        Row = row, Col = col, RawKeycode = 0x0004, DisplayLabel = "A",
    };

    private static Layer BaseLayer(params Key[] keys) =>
        new() { Index = 0, Keys = [..keys] };

    private static bool[,] Matrix(params (int r, int c)[] pressed)
    {
        var m = new bool[10, 6];
        foreach (var (r, c) in pressed) m[r, c] = true;
        return m;
    }

    [Fact]
    public void ResolveTargetLayer_Momentary_BelowHoldThreshold_StaysAtBase()
    {
        var svc = new LayerSwitchService();
        svc.BuildCache(BaseLayer(MomentaryKey(1, 0, 2)));

        // Press held for less than threshold (nowTicks - pressedAt < 50ms).
        svc.ResolveTargetLayer(Matrix((1, 0)), nowTicks: 1000, HoldThresholdMs);
        var layer = svc.ResolveTargetLayer(Matrix((1, 0)), nowTicks: 1020, HoldThresholdMs);

        Assert.Equal(0, layer);
    }

    [Fact]
    public void ResolveTargetLayer_Momentary_AboveHoldThreshold_SwitchesToMomentary()
    {
        var svc = new LayerSwitchService();
        svc.BuildCache(BaseLayer(MomentaryKey(1, 0, 2)));

        svc.ResolveTargetLayer(Matrix((1, 0)), nowTicks: 1000, HoldThresholdMs);
        var layer = svc.ResolveTargetLayer(Matrix((1, 0)), nowTicks: 1100, HoldThresholdMs);

        Assert.Equal(2, layer);
    }

    [Fact]
    public void ResolveTargetLayer_ToggleRisingEdge_ActivatesLayer()
    {
        var svc = new LayerSwitchService();
        svc.BuildCache(BaseLayer(ToggleKey(2, 0, 3)));

        // Seed previous matrix so the next call can detect a rising edge.
        svc.ResolveTargetLayer(Matrix(), nowTicks: 0, HoldThresholdMs);
        var layer = svc.ResolveTargetLayer(Matrix((2, 0)), nowTicks: 10, HoldThresholdMs);

        Assert.Equal(3, layer);
    }

    [Fact]
    public void ResolveTargetLayer_ToggleRisingEdge_Twice_ReturnsToBase()
    {
        var svc = new LayerSwitchService();
        svc.BuildCache(BaseLayer(ToggleKey(2, 0, 3)));

        svc.ResolveTargetLayer(Matrix(), nowTicks: 0, HoldThresholdMs);
        svc.ResolveTargetLayer(Matrix((2, 0)), nowTicks: 10, HoldThresholdMs);   // on
        svc.ResolveTargetLayer(Matrix(), nowTicks: 20, HoldThresholdMs);         // release
        var layer = svc.ResolveTargetLayer(Matrix((2, 0)), nowTicks: 30, HoldThresholdMs); // off

        Assert.Equal(0, layer);
    }

    [Fact]
    public void ResolveTargetLayer_HighestLayerWins_MomentaryAndToggle()
    {
        var svc = new LayerSwitchService();
        svc.BuildCache(BaseLayer(
            MomentaryKey(1, 0, 2),
            ToggleKey(2, 0, 5)));

        // Turn toggle(5) on.
        svc.ResolveTargetLayer(Matrix(), nowTicks: 0, HoldThresholdMs);
        svc.ResolveTargetLayer(Matrix((2, 0)), nowTicks: 10, HoldThresholdMs);

        // Hold MO(2) past threshold. Toggle(5) still wins.
        svc.ResolveTargetLayer(Matrix((1, 0)), nowTicks: 20, HoldThresholdMs);
        var layer = svc.ResolveTargetLayer(Matrix((1, 0)), nowTicks: 100, HoldThresholdMs);

        Assert.Equal(5, layer);
    }

    [Fact]
    public void BuildCache_IgnoresNonLayerSwitchAndNonMomentaryToggleKeys()
    {
        var svc = new LayerSwitchService();
        // Plain key + Activate (not Momentary/Toggle) → neither cached.
        svc.BuildCache(BaseLayer(
            PlainKey(0, 0),
            new Key
            {
                Row = 3, Col = 0, RawKeycode = 0, DisplayLabel = "TO(4)",
                IsLayerSwitch = true, TargetLayer = 4, SwitchType = LayerSwitchType.Activate,
            }));

        // Press TO(4) matrix position — service has no momentary/toggle entries
        // at (3,0) so it doesn't swing the layer.
        svc.ResolveTargetLayer(Matrix(), nowTicks: 0, HoldThresholdMs);
        var layer = svc.ResolveTargetLayer(Matrix((3, 0)), nowTicks: 1000, HoldThresholdMs);

        Assert.Equal(0, layer);
    }

    [Fact]
    public void ResetRuntimeState_ClearsToggledLayers_KeepsCache()
    {
        var svc = new LayerSwitchService();
        svc.BuildCache(BaseLayer(ToggleKey(2, 0, 3)));

        // Toggle(3) on.
        svc.ResolveTargetLayer(Matrix(), nowTicks: 0, HoldThresholdMs);
        svc.ResolveTargetLayer(Matrix((2, 0)), nowTicks: 10, HoldThresholdMs);

        svc.ResetRuntimeState();

        // After reset: toggled layer cleared, but cache remains so a fresh
        // rising edge still activates.
        svc.ResolveTargetLayer(Matrix(), nowTicks: 20, HoldThresholdMs);
        var reactivated = svc.ResolveTargetLayer(Matrix((2, 0)), nowTicks: 30, HoldThresholdMs);
        Assert.Equal(3, reactivated);
    }
}
