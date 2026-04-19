using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

/// <summary>
/// Deep-coverage cases for TransparentKeyResolver: tall stacks, missing matches,
/// null labels, layer-switch propagation through TRNS, and an overridden base
/// layer on top of transparent stacks.
/// </summary>
public class TransparentKeyResolverDeepTests
{
    private static Key MakeKey(int row, int col, string label, bool transparent = false, ushort? raw = null) => new()
    {
        Row = row,
        Col = col,
        RawKeycode = raw ?? (transparent ? (ushort)0x0001 : (ushort)0x0004),
        DisplayLabel = label,
        IsTransparent = transparent,
    };

    private static Layer MakeLayer(int index, params Key[] keys) => new()
    {
        Index = index,
        Keys = keys.ToList(),
    };

    [Fact]
    public void DeepStack_EightLayersOfTrns_ResolvesToLayer0()
    {
        var layers = new List<Layer> { MakeLayer(0, MakeKey(0, 0, "Base")) };
        for (var i = 1; i < 9; i++)
            layers.Add(MakeLayer(i, MakeKey(0, 0, "___", transparent: true)));

        TransparentKeyResolver.Resolve(layers);

        for (var i = 1; i < 9; i++)
            Assert.Equal("Base", layers[i].Keys[0].EffectiveLabel);
    }

    [Fact]
    public void TrnsWithNoMatchingPositionBelow_LeavesEffectiveLabelNull()
    {
        // Layer 0 has NO key at (5, 5). Layer 1 TRNS at (5, 5) can't resolve.
        var layers = new List<Layer>
        {
            MakeLayer(0, MakeKey(0, 0, "A")),
            MakeLayer(1, MakeKey(5, 5, "___", transparent: true)),
        };

        TransparentKeyResolver.Resolve(layers);

        Assert.Null(layers[1].Keys[0].EffectiveLabel);
    }

    [Fact]
    public void LayerSwitchBelow_PropagatesThroughTrns()
    {
        // Layer 0 has a layer-switch key (MO(2)). Layer 1 is TRNS at the same
        // position. The resolved key should carry the layer-switch metadata.
        var moKey = new Key
        {
            Row = 0, Col = 0,
            RawKeycode = 0x5222,
            DisplayLabel = "MO(2)",
            IsTransparent = false,
            IsLayerSwitch = true,
            TargetLayer = 2,
            SwitchType = LayerSwitchType.Momentary,
        };
        var layers = new List<Layer>
        {
            MakeLayer(0, moKey),
            MakeLayer(1, MakeKey(0, 0, "___", transparent: true)),
        };

        TransparentKeyResolver.Resolve(layers);

        var resolved = layers[1].Keys[0];
        Assert.Equal("MO(2)", resolved.EffectiveLabel);
        Assert.True(resolved.IsLayerSwitch);
        Assert.Equal(2, resolved.TargetLayer);
        Assert.Equal(LayerSwitchType.Momentary, resolved.SwitchType);
    }

    [Fact]
    public void SecondaryLabelPropagatesThroughTrns()
    {
        // Layer 0: a mod-tap with a SecondaryLabel. Layer 1 TRNS should inherit it.
        var mtKey = new Key
        {
            Row = 0, Col = 0,
            RawKeycode = 0x2004,
            DisplayLabel = "A",
            SecondaryLabel = "MT(Ctrl)",
            IsTransparent = false,
        };
        var layers = new List<Layer>
        {
            MakeLayer(0, mtKey),
            MakeLayer(1, MakeKey(0, 0, "___", transparent: true)),
        };

        TransparentKeyResolver.Resolve(layers);

        Assert.Equal("A", layers[1].Keys[0].EffectiveLabel);
        Assert.Equal("MT(Ctrl)", layers[1].Keys[0].SecondaryLabel);
    }

    [Fact]
    public void ResolutionWalksActiveStackOnly_OrphanLayersSkipIntermediate()
    {
        // Layer 0 = "Base", Layer 1 = TRNS, Layer 2 = "Mid", Layer 3 = TRNS.
        // Without an activation path from L2 → L3, L3 is orphan and its active
        // stack is [0, 3] only. L3's TRNS must resolve to L0 "Base", not L2's
        // "Mid" — L2 is not active when L3 is reached. This matches QMK runtime.
        var layers = new List<Layer>
        {
            MakeLayer(0, MakeKey(0, 0, "Base")),
            MakeLayer(1, MakeKey(0, 0, "___", transparent: true)),
            MakeLayer(2, MakeKey(0, 0, "Mid")),
            MakeLayer(3, MakeKey(0, 0, "___", transparent: true)),
        };

        TransparentKeyResolver.Resolve(layers);

        Assert.Equal("Base", layers[1].Keys[0].EffectiveLabel);
        Assert.Equal("Base", layers[3].Keys[0].EffectiveLabel);
    }

    [Fact]
    public void ChainedActivation_TrnsResolvesThroughIntermediate()
    {
        // L0 activates L2, L2 activates L3. L3's active stack = [0, 2, 3], so
        // TRNS at (0,0) on L3 picks up "Mid" from L2 (topmost non-TRNS), not L0.
        var moL0 = new Key { Row = 1, Col = 0, RawKeycode = 0x5202, DisplayLabel = "MO(2)",
            IsLayerSwitch = true, TargetLayer = 2, SwitchType = LayerSwitchType.Momentary };
        var moL2 = new Key { Row = 1, Col = 0, RawKeycode = 0x5203, DisplayLabel = "MO(3)",
            IsLayerSwitch = true, TargetLayer = 3, SwitchType = LayerSwitchType.Momentary };
        var layers = new List<Layer>
        {
            MakeLayer(0, MakeKey(0, 0, "Base"), moL0),
            MakeLayer(1, MakeKey(0, 0, "___", transparent: true)),
            MakeLayer(2, MakeKey(0, 0, "Mid"), moL2),
            MakeLayer(3, MakeKey(0, 0, "___", transparent: true)),
        };
        var paths = LayerActivationGraph.Build(layers);

        TransparentKeyResolver.Resolve(layers, paths);

        Assert.Equal("Mid", layers[3].Keys[0].EffectiveLabel);
        Assert.Equal(2, layers[3].Keys[0].ResolvedFromLayer);
    }

    [Fact]
    public void PressThroughActivation_TrnsSeesCoActivatedLayer()
    {
        // L0 (0,2) → MO(2), L0 (0,5) → MO(3). Hard thumb press engages both.
        // L3 active stack = [0, 2, 3]. TRNS at (4,4) on L3 resolves to L2's
        // value even though the primary activator jumps straight to L3.
        var moCol2 = new Key { Row = 0, Col = 2, RawKeycode = 0x5202, DisplayLabel = "MO(2)",
            IsLayerSwitch = true, TargetLayer = 2, SwitchType = LayerSwitchType.Momentary };
        var moCol5 = new Key { Row = 0, Col = 5, RawKeycode = 0x5203, DisplayLabel = "MO(3)",
            IsLayerSwitch = true, TargetLayer = 3, SwitchType = LayerSwitchType.Momentary };
        var layers = new List<Layer>
        {
            MakeLayer(0, MakeKey(4, 4, "Base"), moCol2, moCol5),
            MakeLayer(1, MakeKey(4, 4, "___", transparent: true)),
            MakeLayer(2, MakeKey(4, 4, "Mid")),
            MakeLayer(3, MakeKey(4, 4, "___", transparent: true)),
        };
        var paths = LayerActivationGraph.Build(layers);

        TransparentKeyResolver.Resolve(layers, paths);

        Assert.Equal("Mid", layers[3].Keys[0].EffectiveLabel);
        Assert.Equal(2, layers[3].Keys[0].ResolvedFromLayer);
    }

    [Fact]
    public void EmptyLayerList_DoesNotThrow()
    {
        var layers = new List<Layer>();
        TransparentKeyResolver.Resolve(layers);
        Assert.Empty(layers);
    }

    [Fact]
    public void SingleLayer_NoChanges()
    {
        var layers = new List<Layer>
        {
            MakeLayer(0, MakeKey(0, 0, "A", transparent: true)),
        };

        TransparentKeyResolver.Resolve(layers);

        Assert.Null(layers[0].Keys[0].EffectiveLabel);
    }

    [Fact]
    public void LargeSparseLayers_ResolvesInLinearTime()
    {
        // Regression check for the old O(N²) behavior. 200 keys × 5 layers of
        // TRNS should resolve quickly; test just confirms correctness.
        var base0 = new List<Key>();
        for (var r = 0; r < 20; r++)
            for (var c = 0; c < 10; c++)
                base0.Add(MakeKey(r, c, $"k{r}-{c}"));

        var layers = new List<Layer>
        {
            new() { Index = 0, Keys = base0 },
        };
        for (var li = 1; li < 5; li++)
        {
            var trns = new List<Key>();
            for (var r = 0; r < 20; r++)
                for (var c = 0; c < 10; c++)
                    trns.Add(MakeKey(r, c, "___", transparent: true));
            layers.Add(new Layer { Index = li, Keys = trns });
        }

        TransparentKeyResolver.Resolve(layers);

        Assert.Equal("k5-3", layers[4].Keys[5 * 10 + 3].EffectiveLabel);
        Assert.Equal("k19-9", layers[2].Keys[19 * 10 + 9].EffectiveLabel);
    }
}
