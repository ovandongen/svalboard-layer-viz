using SvalboardLayerViz.Core.Keymap;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

public class LayerColorServiceTests
{
    [Fact]
    public void GetLayerColors_AllLayers_HaveDistinctBackgrounds()
    {
        var backgrounds = new HashSet<string>();
        for (var i = 0; i < 8; i++)
        {
            var colors = LayerColorService.GetLayerColors(i, 8);
            backgrounds.Add(colors.Background);
        }
        Assert.Equal(8, backgrounds.Count);
    }

    [Fact]
    public void GetLayerColors_WithoutDeviceHSV_ReturnsValidHexColors()
    {
        var colors = LayerColorService.GetLayerColors(0, 4);
        Assert.Matches("^#[0-9A-F]{6}$", colors.Background);
        Assert.Matches("^#[0-9A-F]{6}$", colors.Border);
        Assert.Matches("^#[0-9A-F]{6}$", colors.Accent);
        Assert.Matches("^#[0-9A-F]{6}$", colors.TransparentBackground);
        Assert.Matches("^#[0-9A-F]{6}$", colors.TransparentBorder);
    }

    [Fact]
    public void GetLayerColors_WithDeviceHSV_ConvertsCorrectly()
    {
        // Red: H=0 (0/255*360=0), S=255, V=255 → full red
        var colors = LayerColorService.GetLayerColors(0, 1, deviceHue: 0, deviceSat: 255, deviceVal: 255);
        // Background should be a reddish color (low lightness variant)
        Assert.StartsWith("#", colors.Background);
    }

    [Fact]
    public void GetLayerColors_Layer0Of1_ReturnsSaneDefaults()
    {
        var colors = LayerColorService.GetLayerColors(0, 1);
        Assert.NotNull(colors.Background);
        Assert.NotNull(colors.Border);
        Assert.NotNull(colors.Accent);
    }

    [Fact]
    public void TransparentVariant_DiffersFromNormal()
    {
        var colors = LayerColorService.GetLayerColors(0, 4);
        Assert.NotEqual(colors.Background, colors.TransparentBackground);
        Assert.NotEqual(colors.Border, colors.TransparentBorder);
    }

    [Fact]
    public void HslToRgb_Red_ReturnsCorrectValue()
    {
        var (r, g, b) = LayerColorService.HslToRgb(0, 1.0, 0.5);
        Assert.Equal(1.0, r, 2);
        Assert.Equal(0.0, g, 2);
        Assert.Equal(0.0, b, 2);
    }

    [Fact]
    public void HslToRgb_White_ReturnsCorrectValue()
    {
        var (r, g, b) = LayerColorService.HslToRgb(0, 0.0, 1.0);
        Assert.Equal(1.0, r, 2);
        Assert.Equal(1.0, g, 2);
        Assert.Equal(1.0, b, 2);
    }

    [Fact]
    public void HslToHex_Red_ReturnsFF0000()
    {
        var hex = LayerColorService.HslToHex(0, 1.0, 0.5);
        Assert.Equal("#FF0000", hex);
    }
}
