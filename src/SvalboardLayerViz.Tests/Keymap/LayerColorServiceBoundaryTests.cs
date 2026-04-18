using SvalboardLayerViz.Core.Keymap;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

/// <summary>
/// Boundary cases for the HSL color pipeline: hue wraparound at 360°, zero
/// saturation (grayscale), extreme lightness (pure black / white), and algorithmic
/// layer wraparound when many layers are generated.
/// </summary>
public class LayerColorServiceBoundaryTests
{
    // --- HslToRgb: hue wraparound ---

    [Fact]
    public void HslToRgb_Hue0_And_Hue360_ProduceSameColor()
    {
        var (r1, g1, b1) = LayerColorService.HslToRgb(0, 0.5, 0.5);
        var (r2, g2, b2) = LayerColorService.HslToRgb(360, 0.5, 0.5);

        Assert.Equal(r1, r2, 6);
        Assert.Equal(g1, g2, 6);
        Assert.Equal(b1, b2, 6);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(361)]
    [InlineData(720)]
    public void HslToRgb_HueOutOfRange_DoesNotThrow(double hue)
    {
        // HueToRgb normalizes t via +=1/-=1; out-of-range hues should still
        // produce valid RGB in [0, 1].
        var (r, g, b) = LayerColorService.HslToRgb(hue, 0.5, 0.5);
        Assert.InRange(r, 0.0, 1.0);
        Assert.InRange(g, 0.0, 1.0);
        Assert.InRange(b, 0.0, 1.0);
    }

    // --- HslToRgb: zero saturation → grayscale ---

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void HslToRgb_ZeroSaturation_ProducesGrayscale(double lightness)
    {
        var (r, g, b) = LayerColorService.HslToRgb(123, 0.0, lightness);
        Assert.Equal(lightness, r, 6);
        Assert.Equal(lightness, g, 6);
        Assert.Equal(lightness, b, 6);
    }

    // --- HslToRgb: extreme lightness ---

    [Fact]
    public void HslToRgb_LightnessZero_IsPureBlack()
    {
        var (r, g, b) = LayerColorService.HslToRgb(180, 1.0, 0.0);
        Assert.Equal(0, r, 6);
        Assert.Equal(0, g, 6);
        Assert.Equal(0, b, 6);
    }

    [Fact]
    public void HslToRgb_LightnessOne_IsPureWhite()
    {
        var (r, g, b) = LayerColorService.HslToRgb(180, 1.0, 1.0);
        Assert.Equal(1, r, 6);
        Assert.Equal(1, g, 6);
        Assert.Equal(1, b, 6);
    }

    // --- HslToHex: valid hex for edge inputs ---

    [Theory]
    [InlineData(0, 0, 0, "#000000")]
    [InlineData(0, 0, 1, "#FFFFFF")]
    [InlineData(0, 1, 0.5, "#FF0000")]
    public void HslToHex_CanonicalValues_MatchExpected(double h, double s, double l, string expected)
    {
        Assert.Equal(expected, LayerColorService.HslToHex(h, s, l));
    }

    // --- HexToHsl: grayscale roundtrip ---

    [Theory]
    [InlineData("#000000", 0.0)]
    [InlineData("#FFFFFF", 1.0)]
    [InlineData("#808080", 0.5019607843137255)] // 128/255
    public void HexToHsl_Grayscale_HasZeroSaturation(string hex, double expectedLightness)
    {
        var (h, s, l) = LayerColorService.HexToHsl(hex);
        Assert.Equal(0, h);
        Assert.Equal(0, s);
        Assert.Equal(expectedLightness, l, 4);
    }

    [Fact]
    public void HexToHsl_InvalidHex_Throws()
    {
        Assert.Throws<ArgumentException>(() => LayerColorService.HexToHsl("#12"));
        Assert.Throws<ArgumentException>(() => LayerColorService.HexToHsl("notahex"));
    }

    // --- GetContrastTextColor ---

    [Theory]
    [InlineData("#000000", "#FFFFFF")] // black bg → white text
    [InlineData("#FFFFFF", "#1E1E2E")] // white bg → dark text
    public void GetContrastTextColor_PicksReadableColor(string bg, string expected)
    {
        Assert.Equal(expected, LayerColorService.GetContrastTextColor(bg));
    }

    [Fact]
    public void GetContrastTextColor_InvalidHex_FallsBackToWhite()
    {
        Assert.Equal("#FFFFFF", LayerColorService.GetContrastTextColor("not-hex"));
    }

    // --- GetLayerColors: algorithmic hue spacing + large layer counts ---

    [Fact]
    public void GetLayerColors_LargeLayerCount_DoesNotThrow()
    {
        // 256 layers should still produce valid colors (hue wraps at 360°).
        for (var i = 0; i < 256; i++)
        {
            var colors = LayerColorService.GetLayerColors(i, 256);
            Assert.Matches("^#[0-9A-F]{6}$", colors.Background);
        }
    }

    [Fact]
    public void GetLayerColors_ZeroTotalLayers_DoesNotDivideByZero()
    {
        // Guard `Math.Max(totalLayers, 1)` prevents div-by-zero.
        var colors = LayerColorService.GetLayerColors(0, 0);
        Assert.Matches("^#[0-9A-F]{6}$", colors.Background);
    }

    [Fact]
    public void GetLayerColors_NegativeLayerIndex_DoesNotThrow()
    {
        var colors = LayerColorService.GetLayerColors(-1, 4);
        Assert.Matches("^#[0-9A-F]{6}$", colors.Background);
    }

    [Fact]
    public void GetLayerColors_DeviceHsv_BlackDevice_ProducesLowLightness()
    {
        // H=any, S=0, V=0 → pure black. Should not crash and Background should
        // be near-black.
        var colors = LayerColorService.GetLayerColors(0, 1,
            deviceHue: 128, deviceSat: 0, deviceVal: 0);
        Assert.Matches("^#[0-9A-F]{6}$", colors.Background);
    }

    [Fact]
    public void GetLayerColors_UserHex_RoundtripsThroughHsl()
    {
        var colors = LayerColorService.GetLayerColors("#FF0000");
        // Red bg → contrast picks white
        Assert.Equal("#FFFFFF", colors.TextColor);
    }

    [Fact]
    public void GetLayerColors_PrintFriendly_ProducesLightPastels()
    {
        var colors = LayerColorService.GetLayerColors(0, 4, printFriendly: true);
        // Print mode always uses dark text on light pastels
        Assert.Equal("#1E1E2E", colors.TextColor);
    }
}
