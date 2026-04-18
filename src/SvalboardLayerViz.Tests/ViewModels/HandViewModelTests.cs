using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

public class HandViewModelTests
{
    private static Layer MakeLayer() => new() { Index = 0, Keys = [] };

    private static HandViewModel MakeVm(PositionedHand hand, bool isRightHand, Layer layer) =>
        new(hand, isRightHand, layer, LayerColorPalette.ForSingleLayer(layer, totalLayers: 8));

    /// <summary>Creates a full layer with all keys at their real layout positions.</summary>
    private static Layer MakeFullLayer()
    {
        var positions = SvalboardLayout.GetKeyPositions();
        var keys = positions.Select(p => new Key
        {
            Row = p.Row, Col = p.Col, RawKeycode = 0x04,
            X = p.X, Y = p.Y, Width = p.Width, Height = p.Height,
        }).ToList();

        return new Layer { Index = 0, Keys = keys };
    }

    private static ComputedBoardLayout ComputeLayout() => BoardLayoutComputer.Compute(MakeFullLayer());

    [Fact]
    public void LeftHand_Has4FingerClusters()
    {
        var hand = MakeVm(ComputeLayout().LeftHand, false, MakeLayer());
        Assert.Equal(4, hand.FingerClusters.Count);
    }

    [Fact]
    public void LeftHand_HasThumbCluster()
    {
        var hand = MakeVm(ComputeLayout().LeftHand, false, MakeLayer());
        Assert.NotNull(hand.ThumbCluster);
        Assert.Equal(6, hand.ThumbCluster.Keys.Count);
    }

    [Fact]
    public void LeftHand_AllKeys_Has26Keys()
    {
        var hand = MakeVm(ComputeLayout().LeftHand, false, MakeLayer());
        Assert.Equal(26, hand.AllKeys.Count);
    }

    [Fact]
    public void RightHand_Has4FingerClusters()
    {
        var hand = MakeVm(ComputeLayout().RightHand, true, MakeLayer());
        Assert.Equal(4, hand.FingerClusters.Count);
    }

    [Fact]
    public void RightHand_HasThumbCluster_With6Keys()
    {
        var hand = MakeVm(ComputeLayout().RightHand, true, MakeLayer());
        Assert.NotNull(hand.ThumbCluster);
        Assert.Equal(6, hand.ThumbCluster.Keys.Count);
    }

    [Fact]
    public void RightHand_AllKeys_Has26Keys()
    {
        var hand = MakeVm(ComputeLayout().RightHand, true, MakeLayer());
        Assert.Equal(26, hand.AllKeys.Count);
    }

    [Fact]
    public void FingerClusters_EachHave5Keys()
    {
        var hand = MakeVm(ComputeLayout().LeftHand, false, MakeLayer());
        Assert.All(hand.FingerClusters, c => Assert.Equal(5, c.Keys.Count));
    }

    [Fact]
    public void EmptyKeys_DoesNotThrow()
    {
        var emptyHand = new PositionedHand(null, [], [], 0, 0);
        var hand = MakeVm(emptyHand, false, MakeLayer());
        Assert.Empty(hand.AllKeys);
        Assert.Empty(hand.FingerClusters);
    }

    // ── Layout dimension tests (prevent blank canvas) ──

    [Fact]
    public void LeftHand_Width_IsPositive()
    {
        var hand = MakeVm(ComputeLayout().LeftHand, false, MakeLayer());
        Assert.True(hand.Width > 0, $"Hand Width must be > 0, was {hand.Width}");
    }

    [Fact]
    public void LeftHand_Height_IsPositive()
    {
        var hand = MakeVm(ComputeLayout().LeftHand, false, MakeLayer());
        Assert.True(hand.Height > 0, $"Hand Height must be > 0, was {hand.Height}");
    }

    [Fact]
    public void RightHand_Width_IsPositive()
    {
        var hand = MakeVm(ComputeLayout().RightHand, true, MakeLayer());
        Assert.True(hand.Width > 0, $"Hand Width must be > 0, was {hand.Width}");
    }

    [Fact]
    public void RightHand_Height_IsPositive()
    {
        var hand = MakeVm(ComputeLayout().RightHand, true, MakeLayer());
        Assert.True(hand.Height > 0, $"Hand Height must be > 0, was {hand.Height}");
    }

    [Fact]
    public void EmptyKeys_Width_IsZero()
    {
        var emptyHand = new PositionedHand(null, [], [], 0, 0);
        var hand = MakeVm(emptyHand, false, MakeLayer());
        Assert.Equal(0, hand.Width);
        Assert.Equal(0, hand.Height);
    }

    [Fact]
    public void AllClusters_HavePositiveDimensions()
    {
        var hand = MakeVm(ComputeLayout().LeftHand, false, MakeLayer());

        Assert.True(hand.ThumbCluster.Width > 0, "Thumb cluster Width must be > 0");
        Assert.True(hand.ThumbCluster.Height > 0, "Thumb cluster Height must be > 0");

        Assert.All(hand.FingerClusters, c =>
        {
            Assert.True(c.Width > 0, $"Cluster {c.Name} Width must be > 0");
            Assert.True(c.Height > 0, $"Cluster {c.Name} Height must be > 0");
        });
    }

    [Fact]
    public void AllClusters_FitWithinHandBounds()
    {
        var hand = MakeVm(ComputeLayout().LeftHand, false, MakeLayer());

        var allClusters = hand.FingerClusters.Append(hand.ThumbCluster);
        foreach (var c in allClusters)
        {
            Assert.True(c.Left + c.Width <= hand.Width + 0.01,
                $"Cluster {c.Name} extends beyond hand Width: {c.Left + c.Width} > {hand.Width}");
            Assert.True(c.Top + c.Height <= hand.Height + 0.01,
                $"Cluster {c.Name} extends beyond hand Height: {c.Top + c.Height} > {hand.Height}");
        }
    }

    [Fact]
    public void AllKeys_HavePositiveDimensions()
    {
        var hand = MakeVm(ComputeLayout().LeftHand, false, MakeLayer());
        Assert.All(hand.AllKeys, k =>
        {
            Assert.True(k.Width > 0, $"Key Width must be > 0");
            Assert.True(k.Height > 0, $"Key Height must be > 0");
        });
    }

    [Fact]
    public void RightHand_ClustersAreHandRelative_NotAbsolute()
    {
        var hand = MakeVm(ComputeLayout().RightHand, true, MakeLayer());
        // Right hand keys start at X ~12.5 in layout coords.
        // After subtracting handOriginX, cluster Left values should be << 12.5*60=750.
        // If we see cluster.Left > 700, coordinates were NOT made hand-relative.
        Assert.All(hand.FingerClusters, c =>
            Assert.True(c.Left < 700, $"Cluster {c.Name} Left={c.Left} — not hand-relative"));
    }
}
