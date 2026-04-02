using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

/// <summary>
/// Tests for the auto-layer-switch logic: building caches from base layer keys,
/// resolving active layers from momentary holds, and tracking toggle (TG) state via edge detection.
/// These test the same algorithms used by MainWindowViewModel without requiring USB access.
/// </summary>
public class AutoLayerSwitchTests
{
    private static Key MakeKey(int row, int col, bool isLayerSwitch = false,
        int? targetLayer = null, LayerSwitchType switchType = LayerSwitchType.None) => new()
    {
        Row = row,
        Col = col,
        RawKeycode = 0x0004,
        DisplayLabel = isLayerSwitch ? $"{switchType}({targetLayer})" : "A",
        IsLayerSwitch = isLayerSwitch,
        TargetLayer = targetLayer,
        SwitchType = switchType,
        X = 0,
        Y = 0,
    };

    /// <summary>
    /// Builds momentary + toggle caches from keys, same logic as MainWindowViewModel.BuildLayerSwitchCache.
    /// </summary>
    private static (Dictionary<(int Row, int Col), int> Momentary, Dictionary<(int Row, int Col), int> Toggle) BuildCaches(
        IEnumerable<Key> baseLayerKeys)
    {
        var momentary = new Dictionary<(int Row, int Col), int>();
        var toggle = new Dictionary<(int Row, int Col), int>();
        foreach (var key in baseLayerKeys)
        {
            if (!key.IsLayerSwitch || !key.TargetLayer.HasValue) continue;
            switch (key.SwitchType)
            {
                case LayerSwitchType.Momentary:
                    momentary[(key.Row, key.Col)] = key.TargetLayer.Value;
                    break;
                case LayerSwitchType.Toggle:
                    toggle[(key.Row, key.Col)] = key.TargetLayer.Value;
                    break;
            }
        }
        return (momentary, toggle);
    }

    /// <summary>
    /// Resolves active layer from momentary holds + toggled layers.
    /// Same logic as the auto-switch block in OnMatrixStateChanged.
    /// </summary>
    private static int ResolveActiveLayer(
        Dictionary<(int Row, int Col), int> momentaryCache, HashSet<int> toggledLayers, bool[,] state)
    {
        var targetLayer = 0;
        foreach (var (pos, layer) in momentaryCache)
        {
            if (state[pos.Row, pos.Col] && layer > targetLayer)
                targetLayer = layer;
        }
        foreach (var layer in toggledLayers)
        {
            if (layer > targetLayer)
                targetLayer = layer;
        }
        return targetLayer;
    }

    /// <summary>
    /// Detects rising edges on toggle keys and flips the toggled set.
    /// Same logic as edge detection in OnMatrixStateChanged.
    /// </summary>
    private static void ProcessToggleEdges(
        Dictionary<(int Row, int Col), int> toggleCache, bool[,]? previous, bool[,] current,
        HashSet<int> toggledLayers)
    {
        if (previous is null) return;
        foreach (var (pos, layer) in toggleCache)
        {
            var wasPressed = previous[pos.Row, pos.Col];
            var isPressed = current[pos.Row, pos.Col];
            if (isPressed && !wasPressed)
            {
                if (!toggledLayers.Remove(layer))
                    toggledLayers.Add(layer);
            }
        }
    }

    // --- Cache building ---

    [Fact]
    public void Cache_SeparatesMomentaryAndToggle()
    {
        var keys = new[]
        {
            MakeKey(0, 0),
            MakeKey(1, 0, isLayerSwitch: true, targetLayer: 2, switchType: LayerSwitchType.Momentary),
            MakeKey(2, 0, isLayerSwitch: true, targetLayer: 3, switchType: LayerSwitchType.Toggle),
            MakeKey(3, 0, isLayerSwitch: true, targetLayer: 4, switchType: LayerSwitchType.Activate),
            MakeKey(4, 0),
        };

        var (momentary, toggle) = BuildCaches(keys);

        Assert.Single(momentary);
        Assert.Equal(2, momentary[(1, 0)]);
        Assert.Single(toggle);
        Assert.Equal(3, toggle[(2, 0)]);
    }

    [Fact]
    public void Cache_IgnoresNonLayerSwitchKeys()
    {
        var keys = new[] { MakeKey(0, 0), MakeKey(1, 1) };
        var (momentary, toggle) = BuildCaches(keys);
        Assert.Empty(momentary);
        Assert.Empty(toggle);
    }

    [Fact]
    public void Cache_IgnoresActivateAndOneShot()
    {
        var keys = new[]
        {
            MakeKey(0, 0, isLayerSwitch: true, targetLayer: 1, switchType: LayerSwitchType.Activate),
            MakeKey(1, 0, isLayerSwitch: true, targetLayer: 2, switchType: LayerSwitchType.OneShot),
        };
        var (momentary, toggle) = BuildCaches(keys);
        Assert.Empty(momentary);
        Assert.Empty(toggle);
    }

    // --- Momentary resolve ---

    [Fact]
    public void Resolve_ReturnsBaseLayer_WhenNothingPressed()
    {
        var momentary = new Dictionary<(int Row, int Col), int> { [(1, 0)] = 2, [(2, 0)] = 3 };
        var state = new bool[10, 6];

        Assert.Equal(0, ResolveActiveLayer(momentary, [], state));
    }

    [Fact]
    public void Resolve_ReturnsTargetLayer_WhenMomentaryKeyHeld()
    {
        var momentary = new Dictionary<(int Row, int Col), int> { [(1, 0)] = 2 };
        var state = new bool[10, 6];
        state[1, 0] = true;

        Assert.Equal(2, ResolveActiveLayer(momentary, [], state));
    }

    [Fact]
    public void Resolve_ReturnsHighestLayer_WhenMultipleMomentaryKeysHeld()
    {
        var momentary = new Dictionary<(int Row, int Col), int> { [(1, 0)] = 2, [(2, 0)] = 5, [(3, 0)] = 3 };
        var state = new bool[10, 6];
        state[1, 0] = true;
        state[2, 0] = true;

        Assert.Equal(5, ResolveActiveLayer(momentary, [], state));
    }

    [Fact]
    public void Resolve_FallsBackToBase_WhenMomentaryKeyReleased()
    {
        var momentary = new Dictionary<(int Row, int Col), int> { [(1, 0)] = 2 };
        var state = new bool[10, 6];
        state[1, 0] = true;
        Assert.Equal(2, ResolveActiveLayer(momentary, [], state));

        state[1, 0] = false;
        Assert.Equal(0, ResolveActiveLayer(momentary, [], state));
    }

    // --- Toggle edge detection ---

    [Fact]
    public void Toggle_FirstPress_ActivatesLayer()
    {
        var toggleCache = new Dictionary<(int Row, int Col), int> { [(1, 0)] = 3 };
        var toggled = new HashSet<int>();

        var prev = new bool[10, 6]; // Not pressed
        var curr = new bool[10, 6];
        curr[1, 0] = true; // Now pressed

        ProcessToggleEdges(toggleCache, prev, curr, toggled);
        Assert.Contains(3, toggled);
    }

    [Fact]
    public void Toggle_SecondPress_DeactivatesLayer()
    {
        var toggleCache = new Dictionary<(int Row, int Col), int> { [(1, 0)] = 3 };
        var toggled = new HashSet<int> { 3 }; // Already toggled on

        var prev = new bool[10, 6]; // Released between presses
        var curr = new bool[10, 6];
        curr[1, 0] = true; // Pressed again

        ProcessToggleEdges(toggleCache, prev, curr, toggled);
        Assert.DoesNotContain(3, toggled);
    }

    [Fact]
    public void Toggle_HeldAcrossPolls_DoesNotRetrigger()
    {
        var toggleCache = new Dictionary<(int Row, int Col), int> { [(1, 0)] = 3 };
        var toggled = new HashSet<int>();

        // First poll: rising edge → toggle on
        var prev = new bool[10, 6];
        var curr = new bool[10, 6];
        curr[1, 0] = true;
        ProcessToggleEdges(toggleCache, prev, curr, toggled);
        Assert.Contains(3, toggled);

        // Second poll: still held → no re-trigger
        prev = (bool[,])curr.Clone();
        curr = new bool[10, 6];
        curr[1, 0] = true;
        ProcessToggleEdges(toggleCache, prev, curr, toggled);
        Assert.Contains(3, toggled); // Still on, not toggled off
    }

    [Fact]
    public void Toggle_NoPreviousState_DoesNothing()
    {
        var toggleCache = new Dictionary<(int Row, int Col), int> { [(1, 0)] = 3 };
        var toggled = new HashSet<int>();
        var curr = new bool[10, 6];
        curr[1, 0] = true;

        ProcessToggleEdges(toggleCache, null, curr, toggled);
        Assert.Empty(toggled); // No previous state, no edge detection
    }

    // --- Combined momentary + toggle ---

    [Fact]
    public void Resolve_CombinesMomentaryAndToggle_HighestWins()
    {
        var momentary = new Dictionary<(int Row, int Col), int> { [(1, 0)] = 2 };
        var toggled = new HashSet<int> { 5 }; // Layer 5 toggled on
        var state = new bool[10, 6];
        state[1, 0] = true; // MO(2) held

        // Toggle layer 5 > momentary layer 2
        Assert.Equal(5, ResolveActiveLayer(momentary, toggled, state));
    }

    [Fact]
    public void Resolve_MomentaryWins_WhenHigherThanToggle()
    {
        var momentary = new Dictionary<(int Row, int Col), int> { [(1, 0)] = 7 };
        var toggled = new HashSet<int> { 3 }; // Layer 3 toggled on
        var state = new bool[10, 6];
        state[1, 0] = true; // MO(7) held

        Assert.Equal(7, ResolveActiveLayer(momentary, toggled, state));
    }

    [Fact]
    public void Resolve_ToggledLayerPersists_WhenNoMomentaryHeld()
    {
        var momentary = new Dictionary<(int Row, int Col), int> { [(1, 0)] = 2 };
        var toggled = new HashSet<int> { 3 };
        var state = new bool[10, 6]; // Nothing held

        Assert.Equal(3, ResolveActiveLayer(momentary, toggled, state));
    }

    [Fact]
    public void FullCycle_ToggleOnThenOff()
    {
        var toggleCache = new Dictionary<(int Row, int Col), int> { [(1, 0)] = 3 };
        var momentary = new Dictionary<(int Row, int Col), int>();
        var toggled = new HashSet<int>();

        // Poll 1: nothing pressed
        var s0 = new bool[10, 6];
        Assert.Equal(0, ResolveActiveLayer(momentary, toggled, s0));

        // Poll 2: TG(3) pressed (rising edge)
        var s1 = new bool[10, 6];
        s1[1, 0] = true;
        ProcessToggleEdges(toggleCache, s0, s1, toggled);
        Assert.Equal(3, ResolveActiveLayer(momentary, toggled, s1));

        // Poll 3: TG(3) released
        var s2 = new bool[10, 6];
        ProcessToggleEdges(toggleCache, s1, s2, toggled);
        Assert.Equal(3, ResolveActiveLayer(momentary, toggled, s2)); // Still toggled on

        // Poll 4: TG(3) pressed again (rising edge → toggle off)
        var s3 = new bool[10, 6];
        s3[1, 0] = true;
        ProcessToggleEdges(toggleCache, s2, s3, toggled);
        Assert.Equal(0, ResolveActiveLayer(momentary, toggled, s3)); // Toggled off → base
    }
}
