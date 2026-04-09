using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

public class LedColorLayerResolverTests
{
    private static Layer MakeLayer(int index, byte? h, byte? s, byte? v = 255) =>
        new()
        {
            Index = index,
            Keys = [],
            ColorHue = h,
            ColorSat = s,
            ColorVal = v,
        };

    [Fact]
    public void Resolve_ExactMatch_ReturnsLayerIndex()
    {
        var layers = new List<Layer>
        {
            MakeLayer(0, 85, 255),
            MakeLayer(1, 21, 255),
            MakeLayer(2, 149, 255),
        };

        Assert.Equal(1, LedColorLayerResolver.Resolve(layers, 21, 255, currentLayer: 0));
    }

    [Fact]
    public void Resolve_NoMatch_ReturnsNull()
    {
        var layers = new List<Layer>
        {
            MakeLayer(0, 85, 255),
            MakeLayer(1, 21, 255),
        };

        Assert.Null(LedColorLayerResolver.Resolve(layers, 200, 255, currentLayer: 0));
    }

    [Fact]
    public void Resolve_WithinHueTolerance_Matches()
    {
        var layers = new List<Layer> { MakeLayer(0, 85, 255) };
        Assert.Equal(0, LedColorLayerResolver.Resolve(layers, 88, 255, currentLayer: 0));
        Assert.Equal(0, LedColorLayerResolver.Resolve(layers, 82, 255, currentLayer: 0));
    }

    [Fact]
    public void Resolve_OutsideHueTolerance_ReturnsNull()
    {
        var layers = new List<Layer> { MakeLayer(0, 85, 255) };
        Assert.Null(LedColorLayerResolver.Resolve(layers, 100, 255, currentLayer: 0));
    }

    [Fact]
    public void Resolve_HueWrapsAroundZero()
    {
        // Layer hue is 2, LED reports 254 — circular distance is 4, should match.
        var layers = new List<Layer> { MakeLayer(0, 2, 255) };
        Assert.Equal(0, LedColorLayerResolver.Resolve(layers, 254, 255, currentLayer: 0));
    }

    [Fact]
    public void Resolve_CollidingLayers_KeepsCurrentIfMatching()
    {
        // Layers 4 and 14 both have (43, 255) — reproduces a real case from the
        // Svalboard probe output.
        var layers = new List<Layer>
        {
            MakeLayer(0, 85, 255),
            MakeLayer(4, 43, 255),
            MakeLayer(14, 43, 255),
        };

        // Current layer is 14 → keep 14.
        Assert.Equal(14, LedColorLayerResolver.Resolve(layers, 43, 255, currentLayer: 14));
        // Current layer is 4 → keep 4.
        Assert.Equal(4, LedColorLayerResolver.Resolve(layers, 43, 255, currentLayer: 4));
    }

    [Fact]
    public void Resolve_CollidingLayers_PicksLowestIfCurrentNotMatching()
    {
        var layers = new List<Layer>
        {
            MakeLayer(0, 85, 255),
            MakeLayer(4, 43, 255),
            MakeLayer(14, 43, 255),
        };

        // Current layer 0 doesn't match the LED color, so we should pick the
        // lowest-index match (4).
        Assert.Equal(4, LedColorLayerResolver.Resolve(layers, 43, 255, currentLayer: 0));
    }

    [Fact]
    public void Resolve_SkipsLayersWithNullColors()
    {
        var layers = new List<Layer>
        {
            MakeLayer(0, null, null),
            MakeLayer(1, 43, 255),
        };

        Assert.Equal(1, LedColorLayerResolver.Resolve(layers, 43, 255, currentLayer: 0));
    }

    [Fact]
    public void CircularDistance_Wraparound()
    {
        Assert.Equal(4, LedColorLayerResolver.CircularDistance(2, 254));
        Assert.Equal(4, LedColorLayerResolver.CircularDistance(254, 2));
        Assert.Equal(0, LedColorLayerResolver.CircularDistance(128, 128));
        Assert.Equal(128, LedColorLayerResolver.CircularDistance(0, 128));
    }

    [Fact]
    public void Resolve_SaturationDifference_RespectsTolerance()
    {
        var layers = new List<Layer>
        {
            MakeLayer(0, 43, 255),
            MakeLayer(1, 43, 128),
        };

        // LED reports (43, 250) — within SatTolerance (16) of layer 0 (255),
        // not of layer 1 (128).
        Assert.Equal(0, LedColorLayerResolver.Resolve(layers, 43, 250, currentLayer: 1));
    }
}
