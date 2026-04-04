using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.Layout;

public class BoardLayoutComputerTests
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

    private static Layer MakeEmptyLayer() => new() { Index = 0, Keys = [] };

    [Fact]
    public void Compute_SplitsKeysIntoTwoHands()
    {
        var layout = BoardLayoutComputer.Compute(MakeFullLayer());
        Assert.True(layout.LeftHand.AllKeys.Count > 0, "Left hand should have keys");
        Assert.True(layout.RightHand.AllKeys.Count > 0, "Right hand should have keys");
        Assert.Equal(52, layout.AllKeys.Count); // 52 total keys in Svalboard layout
    }

    [Fact]
    public void Compute_LeftHand_HasRows0Through4()
    {
        var layout = BoardLayoutComputer.Compute(MakeFullLayer());
        Assert.All(layout.LeftHand.AllKeys, pk =>
            Assert.True(pk.Key.Row <= 4, $"Left hand key has row {pk.Key.Row}, expected <= 4"));
    }

    [Fact]
    public void Compute_RightHand_HasRows5Through9()
    {
        var layout = BoardLayoutComputer.Compute(MakeFullLayer());
        Assert.All(layout.RightHand.AllKeys, pk =>
            Assert.True(pk.Key.Row >= 5, $"Right hand key has row {pk.Key.Row}, expected >= 5"));
    }

    [Fact]
    public void Compute_ThumbCluster_IdentifiedCorrectly()
    {
        var layout = BoardLayoutComputer.Compute(MakeFullLayer());
        Assert.NotNull(layout.LeftHand.ThumbCluster);
        Assert.True(layout.LeftHand.ThumbCluster.IsThumb);
        Assert.NotNull(layout.RightHand.ThumbCluster);
        Assert.True(layout.RightHand.ThumbCluster.IsThumb);
    }

    [Fact]
    public void Compute_LeftHand_Has4FingerClusters()
    {
        var layout = BoardLayoutComputer.Compute(MakeFullLayer());
        Assert.Equal(4, layout.LeftHand.FingerClusters.Count);
        Assert.All(layout.LeftHand.FingerClusters, c => Assert.False(c.IsThumb));
    }

    [Fact]
    public void Compute_RightHand_Has4FingerClusters()
    {
        var layout = BoardLayoutComputer.Compute(MakeFullLayer());
        Assert.Equal(4, layout.RightHand.FingerClusters.Count);
        Assert.All(layout.RightHand.FingerClusters, c => Assert.False(c.IsThumb));
    }

    [Fact]
    public void Compute_AllKeysHavePositiveBoardCoords()
    {
        var layout = BoardLayoutComputer.Compute(MakeFullLayer());
        Assert.All(layout.AllKeys, pk =>
        {
            Assert.True(pk.BoardX >= 0, $"BoardX must be >= 0, was {pk.BoardX}");
            Assert.True(pk.BoardY >= 0, $"BoardY must be >= 0, was {pk.BoardY}");
            Assert.True(pk.Width > 0, $"Width must be > 0, was {pk.Width}");
            Assert.True(pk.Height > 0, $"Height must be > 0, was {pk.Height}");
        });
    }

    [Fact]
    public void Compute_ClusterBoundsContainAllKeys()
    {
        var layout = BoardLayoutComputer.Compute(MakeFullLayer());
        var allClusters = layout.LeftHand.FingerClusters
            .Concat(layout.RightHand.FingerClusters)
            .Append(layout.LeftHand.ThumbCluster!)
            .Append(layout.RightHand.ThumbCluster!);

        foreach (var cluster in allClusters)
        {
            foreach (var key in cluster.Keys)
            {
                Assert.True(key.BoardX >= cluster.Left - 0.01,
                    $"Key BoardX {key.BoardX} < cluster Left {cluster.Left} in {cluster.Name}");
                Assert.True(key.BoardY >= cluster.Top - 0.01,
                    $"Key BoardY {key.BoardY} < cluster Top {cluster.Top} in {cluster.Name}");
                Assert.True(key.BoardX + key.Width <= cluster.Left + cluster.Width + 0.01,
                    $"Key right edge exceeds cluster in {cluster.Name}");
                Assert.True(key.BoardY + key.Height <= cluster.Top + cluster.Height + 0.01,
                    $"Key bottom edge exceeds cluster in {cluster.Name}");
            }
        }
    }

    [Fact]
    public void Compute_RightHandKeys_HaveLargerBoardX()
    {
        var layout = BoardLayoutComputer.Compute(MakeFullLayer());
        // Exclude thumb cluster (row 5) since it spans both sides
        var leftFingerMaxX = layout.LeftHand.FingerClusters.SelectMany(c => c.Keys).Max(pk => pk.BoardX);
        var rightFingerMinX = layout.RightHand.FingerClusters.SelectMany(c => c.Keys).Min(pk => pk.BoardX);
        Assert.True(rightFingerMinX > leftFingerMaxX,
            $"Right hand finger keys ({rightFingerMinX}) should be right of left hand ({leftFingerMaxX})");
    }

    [Fact]
    public void Compute_WithEmptyLayer_ReturnsEmptyHands()
    {
        var layout = BoardLayoutComputer.Compute(MakeEmptyLayer());
        Assert.Empty(layout.AllKeys);
        Assert.Empty(layout.LeftHand.AllKeys);
        Assert.Empty(layout.RightHand.AllKeys);
        Assert.Equal(0, layout.LeftHand.Width);
        Assert.Equal(0, layout.RightHand.Width);
    }

    [Fact]
    public void Compute_BoardAbsoluteCoords_MatchRawPositionsTimesScale()
    {
        var layer = MakeFullLayer();
        var layout = BoardLayoutComputer.Compute(layer);

        foreach (var pk in layout.AllKeys)
        {
            Assert.Equal(pk.Key.X * SvalboardLayout.Scale, pk.BoardX, 1);
            Assert.Equal(pk.Key.Y * SvalboardLayout.Scale, pk.BoardY, 1);
            Assert.Equal(pk.Key.Width * SvalboardLayout.Scale, pk.Width, 1);
            Assert.Equal(pk.Key.Height * SvalboardLayout.Scale, pk.Height, 1);
        }
    }

    [Fact]
    public void GetBoardHeightExcludingBottomClusters_IsLessThanOrEqualFullHeight()
    {
        var compactHeight = BoardLayoutComputer.GetBoardHeightExcludingBottomClusters();
        var fullHeight = SvalboardLayout.HandHeight * SvalboardLayout.Scale;
        Assert.True(compactHeight <= fullHeight,
            $"Compact height {compactHeight} should be <= full height {fullHeight}");
        Assert.True(compactHeight > 0, "Compact height must be > 0");
    }
}
