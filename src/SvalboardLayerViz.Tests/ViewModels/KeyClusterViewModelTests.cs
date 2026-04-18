using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

public class KeyClusterViewModelTests
{
    private static Layer MakeLayer() => new() { Index = 0, Keys = [] };

    private static KeyClusterViewModel MakeVm(PositionedCluster cluster, Layer layer, double handOriginPx) =>
        new(cluster, layer, handOriginPx, LayerColorPalette.ForSingleLayer(layer, totalLayers: 8));

    /// <summary>Creates a PositionedCluster for L-Index finger keys.</summary>
    private static PositionedCluster MakeFingerCluster(double baseX, double baseY)
    {
        var keys = new List<Key>
        {
            new() { Row = 1, Col = 0, RawKeycode = 1, X = baseX, Y = baseY + 1.0 },       // South
            new() { Row = 1, Col = 1, RawKeycode = 2, X = baseX + 1.0, Y = baseY },        // East
            new() { Row = 1, Col = 2, RawKeycode = 3, X = baseX, Y = baseY },               // Down
            new() { Row = 1, Col = 3, RawKeycode = 4, X = baseX, Y = baseY - 1.0 },        // North
            new() { Row = 1, Col = 4, RawKeycode = 5, X = baseX - 1.0, Y = baseY },        // West
        };

        const double scale = 60.0;
        var posKeys = keys.Select(k => new PositionedKey(k,
            k.X * scale, k.Y * scale, k.Width * scale, k.Height * scale)).ToList();

        var left = posKeys.Min(pk => pk.BoardX);
        var top = posKeys.Min(pk => pk.BoardY);
        var right = posKeys.Max(pk => pk.BoardX + pk.Width);
        var bottom = posKeys.Max(pk => pk.BoardY + pk.Height);

        return new PositionedCluster("L-Index", false, posKeys,
            left, top, right - left, bottom - top);
    }

    /// <summary>Creates a right-hand cluster at given position.</summary>
    private static PositionedCluster MakeRightHandCluster()
    {
        var keys = new List<Key>
        {
            new() { Row = 7, Col = 0, RawKeycode = 1, X = 16.3, Y = 2.0 },
            new() { Row = 7, Col = 1, RawKeycode = 2, X = 17.3, Y = 1.0 },
            new() { Row = 7, Col = 2, RawKeycode = 3, X = 16.3, Y = 1.0 },
            new() { Row = 7, Col = 3, RawKeycode = 4, X = 16.3, Y = 0.0 },
            new() { Row = 7, Col = 4, RawKeycode = 5, X = 15.3, Y = 1.0 },
        };

        const double scale = 60.0;
        var posKeys = keys.Select(k => new PositionedKey(k,
            k.X * scale, k.Y * scale, k.Width * scale, k.Height * scale)).ToList();

        var left = posKeys.Min(pk => pk.BoardX);
        var top = posKeys.Min(pk => pk.BoardY);
        var right = posKeys.Max(pk => pk.BoardX + pk.Width);
        var bottom = posKeys.Max(pk => pk.BoardY + pk.Height);

        return new PositionedCluster("R-Middle", false, posKeys,
            left, top, right - left, bottom - top);
    }

    [Fact]
    public void Keys_Count_MatchesInput()
    {
        var cluster = MakeVm(MakeFingerCluster(9.5, 2.5), MakeLayer(), handOriginPx: 0);
        Assert.Equal(5, cluster.Keys.Count);
    }

    [Fact]
    public void BoundingBox_ComputedFromKeys()
    {
        // Keys at X: 8.5-10.5, Y: 1.5-3.5, each 1x1 → box is 3x3 units
        var cluster = MakeVm(MakeFingerCluster(9.5, 2.5), MakeLayer(), handOriginPx: 0);
        Assert.Equal(3.0 * 60, cluster.Width);
        Assert.Equal(3.0 * 60, cluster.Height);
    }

    [Fact]
    public void Left_Top_AreHandRelative()
    {
        var cluster = MakeVm(MakeFingerCluster(9.5, 2.5), MakeLayer(), handOriginPx: 0);
        // Min X = 8.5, min Y = 1.5 → board-absolute pixels (510, 90), hand origin 0
        Assert.Equal(8.5 * 60, cluster.Left);
        Assert.Equal(1.5 * 60, cluster.Top);
    }

    [Fact]
    public void Keys_AreClusterRelative()
    {
        var cluster = MakeVm(MakeFingerCluster(9.5, 2.5), MakeLayer(), handOriginPx: 0);

        // The "Down" key (col 2) is at absolute (9.5, 2.5)*60, cluster origin is (8.5, 1.5)*60
        // So cluster-relative: (1.0, 1.0)*60 → pixels (60, 60)
        var downKey = cluster.Keys[2]; // col 2 = Down
        Assert.Equal(1.0 * 60, downKey.Left);
        Assert.Equal(1.0 * 60, downKey.Top);
    }

    [Fact]
    public void Width_And_Height_ArePositive()
    {
        var cluster = MakeVm(MakeFingerCluster(9.5, 2.5), MakeLayer(), handOriginPx: 0);
        Assert.True(cluster.Width > 0, $"Cluster Width must be > 0, was {cluster.Width}");
        Assert.True(cluster.Height > 0, $"Cluster Height must be > 0, was {cluster.Height}");
    }

    [Fact]
    public void AllKeys_HaveNonNegativePositions()
    {
        var cluster = MakeVm(MakeFingerCluster(9.5, 2.5), MakeLayer(), handOriginPx: 0);
        Assert.All(cluster.Keys, k =>
        {
            Assert.True(k.Left >= 0, $"Key Left must be >= 0, was {k.Left}");
            Assert.True(k.Top >= 0, $"Key Top must be >= 0, was {k.Top}");
        });
    }

    [Fact]
    public void RightHand_HandOriginSubtracted()
    {
        // Right-hand cluster, handOriginPx = 12.5 * 60 = 750
        var posCluster = MakeRightHandCluster();
        var cluster = MakeVm(posCluster, MakeLayer(), handOriginPx: 12.5 * 60);
        // Board-absolute cluster left = 15.3*60=918, hand-relative = 918-750 = 168 = 2.8*60
        Assert.Equal(2.8 * 60, cluster.Left, 1);
        Assert.Equal(0.0, cluster.Top);
    }
}
