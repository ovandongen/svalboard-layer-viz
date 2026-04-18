using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.Layout;

/// <summary>
/// Structural validation for cluster grouping. Every hand has one thumb cluster
/// (6 keys) + 4 finger clusters (5 keys each); bounds are positive; coords unique.
/// </summary>
public class BoardLayoutComputerValidationTests
{
    private static Layer MakeFullLayer(int index = 0)
    {
        var positions = SvalboardLayout.GetKeyPositions();
        var keys = positions.Select(p => new Key
        {
            Row = p.Row, Col = p.Col, RawKeycode = 0x04, DisplayLabel = "A",
            X = p.X, Y = p.Y, Width = p.Width, Height = p.Height,
        }).ToList();
        return new Layer { Index = index, Keys = keys };
    }

    [Fact]
    public void NoCluster_FallsBackToRowNName()
    {
        // Fallback path "Row{N}" indicates a stale layout table; full layout
        // should never trigger it.
        var layout = BoardLayoutComputer.Compute(MakeFullLayer());
        var allClusters = layout.LeftHand.FingerClusters
            .Concat(layout.RightHand.FingerClusters)
            .Append(layout.LeftHand.ThumbCluster!)
            .Append(layout.RightHand.ThumbCluster!);

        foreach (var cluster in allClusters)
            Assert.DoesNotContain("Row", cluster.Name);
    }

    [Fact]
    public void FingerClusters_Have5KeysEach()
    {
        var layout = BoardLayoutComputer.Compute(MakeFullLayer());
        foreach (var cluster in layout.LeftHand.FingerClusters)
            Assert.Equal(5, cluster.Keys.Count);
        foreach (var cluster in layout.RightHand.FingerClusters)
            Assert.Equal(5, cluster.Keys.Count);
    }

    [Fact]
    public void ClusterBounds_AreStrictlyPositive()
    {
        var layout = BoardLayoutComputer.Compute(MakeFullLayer());
        var all = layout.LeftHand.FingerClusters
            .Concat(layout.RightHand.FingerClusters)
            .Append(layout.LeftHand.ThumbCluster!)
            .Append(layout.RightHand.ThumbCluster!);

        foreach (var c in all)
        {
            Assert.True(c.Width > 0, $"Cluster {c.Name} has non-positive width {c.Width}");
            Assert.True(c.Height > 0, $"Cluster {c.Name} has non-positive height {c.Height}");
        }
    }

    [Fact]
    public void SingleFingerKey_ProducesOneFingerClusterNoThumb()
    {
        // One key on a finger row → one finger cluster, no thumb.
        var key = new Key
        {
            Row = 1, Col = 0, RawKeycode = 0x04, DisplayLabel = "A",
            X = 9.5, Y = 3.5, Width = 1, Height = 1,
        };
        var layer = new Layer { Index = 0, Keys = [key] };

        var layout = BoardLayoutComputer.Compute(layer);

        Assert.Single(layout.LeftHand.FingerClusters);
        Assert.Null(layout.LeftHand.ThumbCluster);
    }

    [Fact]
    public void OnlyThumbRowKey_ProducesThumbNoFingers()
    {
        var key = new Key
        {
            Row = 0, Col = 0, RawKeycode = 0x04, DisplayLabel = "A",
            X = 10.8, Y = 6.0, Width = 1, Height = 1,
        };
        var layer = new Layer { Index = 0, Keys = [key] };

        var layout = BoardLayoutComputer.Compute(layer);

        Assert.NotNull(layout.LeftHand.ThumbCluster);
        Assert.Empty(layout.LeftHand.FingerClusters);
    }

    [Fact]
    public void Row5_AssignedToRightHand()
    {
        var layout = BoardLayoutComputer.Compute(MakeFullLayer());

        Assert.Contains(layout.RightHand.AllKeys, pk => pk.Key.Row == 5);
        Assert.DoesNotContain(layout.LeftHand.AllKeys, pk => pk.Key.Row == 5);
    }

    [Fact]
    public void TotalKeyCount_Is52()
    {
        var layout = BoardLayoutComputer.Compute(MakeFullLayer());
        Assert.Equal(52, layout.AllKeys.Count);
    }

    [Fact]
    public void AllKeyCoordinates_Unique()
    {
        var layout = BoardLayoutComputer.Compute(MakeFullLayer());
        var coords = layout.AllKeys.Select(pk => (pk.BoardX, pk.BoardY)).ToList();
        Assert.Equal(coords.Count, coords.Distinct().Count());
    }
}
