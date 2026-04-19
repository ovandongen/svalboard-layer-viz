using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

/// <summary>
/// Coverage for <see cref="LayerActivationGraph"/>: traces the activator keys
/// that reach each layer from L0, handles Svalboard thumb press-through
/// coupling, and falls back gracefully for orphan / cyclic layers.
/// </summary>
public class LayerActivationGraphTests
{
    private static Key Plain(int row, int col, string label = "A") => new()
    {
        Row = row, Col = col, RawKeycode = 0x0004, DisplayLabel = label,
    };

    private static Key Activator(int row, int col, int target, LayerSwitchType type = LayerSwitchType.Momentary) => new()
    {
        Row = row, Col = col,
        RawKeycode = (ushort)(0x5200 | target),
        DisplayLabel = $"MO({target})",
        IsLayerSwitch = true,
        TargetLayer = target,
        SwitchType = type,
    };

    private static Key Trns(int row, int col) => new()
    {
        Row = row, Col = col, RawKeycode = 0x0001, DisplayLabel = "___", IsTransparent = true,
    };

    private static Layer Make(int index, params Key[] keys) => new() { Index = index, Keys = keys };

    [Fact]
    public void SingleHop_L0HasMOTowardsL2_PopulatesL2Path()
    {
        var layers = new List<Layer>
        {
            Make(0, Activator(0, 0, 2)),
            Make(1, Plain(0, 0)),
            Make(2, Plain(0, 0)),
        };

        var paths = LayerActivationGraph.Build(layers);

        Assert.Single(paths[2]);
        var hop = paths[2][0];
        Assert.Equal(0, hop.SourceLayer);
        Assert.Equal(2, hop.TargetLayer);
        Assert.Empty(paths[0]);
        Assert.Empty(paths[1]); // orphan, no activator
    }

    [Fact]
    public void ChainedHops_L0ToL2ToL3_BuildsOrderedPath()
    {
        var layers = new List<Layer>
        {
            Make(0, Activator(0, 0, 2)),
            Make(1, Plain(0, 0)),
            Make(2, Activator(1, 1, 3)),
            Make(3, Plain(0, 0)),
        };

        var paths = LayerActivationGraph.Build(layers);

        var path = paths[3];
        Assert.Equal(2, path.Count);
        Assert.Equal(0, path[0].SourceLayer);
        Assert.Equal(2, path[0].TargetLayer);
        Assert.Equal(2, path[1].SourceLayer);
        Assert.Equal(3, path[1].TargetLayer);
    }

    [Fact]
    public void GetActiveStack_ChainedPath_IsL0UpThroughHops()
    {
        var layers = new List<Layer>
        {
            Make(0, Activator(0, 0, 2)),
            Make(1, Plain(0, 0)),
            Make(2, Activator(1, 1, 3)),
            Make(3, Plain(0, 0)),
        };
        var paths = LayerActivationGraph.Build(layers);

        var stack = LayerActivationGraph.GetActiveStack(3, paths[3]);

        Assert.Equal(new[] { 0, 2, 3 }, stack);
    }

    [Fact]
    public void PressThroughCoupling_Col5Activator_AddsCol2CoHop()
    {
        // Row 0 is L-Thumb; col 2 and col 5 are coupled. Pressing col 5 hard
        // also engages col 2. Both activators on L0 target L2 and L3 → both
        // layers simultaneously active, path for L3 should include both.
        var layers = new List<Layer>
        {
            Make(0, Activator(0, 2, 2), Activator(0, 5, 3)),
            Make(1, Plain(0, 0)),
            Make(2, Plain(0, 0)),
            Make(3, Plain(0, 0)),
        };

        var paths = LayerActivationGraph.Build(layers);

        var path = paths[3];
        Assert.Equal(2, path.Count);
        Assert.Equal((0, 0, 2, 2), (path[0].SourceLayer, path[0].Row, path[0].Col, path[0].TargetLayer));
        Assert.Equal((0, 0, 5, 3), (path[1].SourceLayer, path[1].Row, path[1].Col, path[1].TargetLayer));

        var stack = LayerActivationGraph.GetActiveStack(3, path);
        Assert.Equal(new[] { 0, 2, 3 }, stack);
    }

    [Fact]
    public void PressThroughCoupling_RightThumbRow5_AlsoCouples()
    {
        var layers = new List<Layer>
        {
            Make(0, Activator(5, 2, 4), Activator(5, 5, 6)),
            Make(1, Plain(0, 0)),
            Make(2, Plain(0, 0)),
            Make(3, Plain(0, 0)),
            Make(4, Plain(0, 0)),
            Make(5, Plain(0, 0)),
            Make(6, Plain(0, 0)),
        };

        var paths = LayerActivationGraph.Build(layers);

        var path = paths[6];
        Assert.Equal(2, path.Count);
        Assert.Equal(4, path[0].TargetLayer);
        Assert.Equal(6, path[1].TargetLayer);
    }

    [Fact]
    public void Col5WithoutMatchingCol2Activator_OnlyPrimaryHop()
    {
        var layers = new List<Layer>
        {
            Make(0, Activator(0, 5, 3)),
            Make(1, Plain(0, 0)),
            Make(2, Plain(0, 0)),
            Make(3, Plain(0, 0)),
        };

        var paths = LayerActivationGraph.Build(layers);

        Assert.Single(paths[3]); // no co-hop since col 2 isn't an activator
    }

    [Fact]
    public void MultipleParents_ShortestPathWins()
    {
        // L3 can be reached directly from L0 (1 hop) or via L2 (2 hops).
        var layers = new List<Layer>
        {
            Make(0, Activator(0, 0, 2), Activator(1, 0, 3)),
            Make(1, Plain(0, 0)),
            Make(2, Activator(0, 0, 3)),
            Make(3, Plain(0, 0)),
        };

        var paths = LayerActivationGraph.Build(layers);

        var path = paths[3];
        Assert.Single(path);
        Assert.Equal(0, path[0].SourceLayer);
    }

    [Fact]
    public void Cycle_L1AndL2PointAtEachOther_FallbackEmptyPath()
    {
        // L1 activates L2, L2 activates L1. Neither originates from L0.
        var layers = new List<Layer>
        {
            Make(0, Plain(0, 0)),
            Make(1, Activator(0, 0, 2)),
            Make(2, Activator(0, 0, 1)),
        };

        var paths = LayerActivationGraph.Build(layers);

        Assert.Empty(paths[1]);
        Assert.Empty(paths[2]);
    }

    [Fact]
    public void Orphan_LayerWithNoIncomingActivator_EmptyPath()
    {
        // Classic "mouse layer" case: L15 reached via key combo not visible in keymap.
        var layers = new List<Layer>
        {
            Make(0, Plain(0, 0)),
            Make(1, Plain(0, 0)),
            Make(2, Plain(0, 0)),
        };

        var paths = LayerActivationGraph.Build(layers);

        Assert.Empty(paths[1]);
        Assert.Empty(paths[2]);
    }

    [Fact]
    public void SelfLoopActivator_DoesNotRegisterAsIncoming()
    {
        // MO(2) on L2 is a hold-to-stay pattern and must not count as an
        // activation path for L2 itself.
        var layers = new List<Layer>
        {
            Make(0, Plain(0, 0)),
            Make(1, Plain(0, 0)),
            Make(2, Activator(0, 0, 2)),
        };

        var paths = LayerActivationGraph.Build(layers);

        Assert.Empty(paths[2]);
    }

    [Fact]
    public void DfActivator_TreatedAsRegularHop()
    {
        // DF(2) sets default layer — for v1 we treat it like any other hop.
        var layers = new List<Layer>
        {
            Make(0, Activator(0, 0, 2, LayerSwitchType.Activate)),
            Make(1, Plain(0, 0)),
            Make(2, Plain(0, 0)),
        };

        var paths = LayerActivationGraph.Build(layers);

        Assert.Single(paths[2]);
        Assert.Equal(LayerSwitchType.Activate, paths[2][0].SwitchType);
    }

    [Fact]
    public void EmptyLayerList_ReturnsEmptyMap()
    {
        var paths = LayerActivationGraph.Build(new List<Layer>());
        Assert.Empty(paths);
    }
}
