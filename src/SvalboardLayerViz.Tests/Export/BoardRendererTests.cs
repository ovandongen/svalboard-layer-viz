using SkiaSharp;
using SvalboardLayerViz.Core.Export;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.Export;

public class BoardRendererTests
{
    private static Layer MakeLayer(int index = 0, string? name = null) => new()
    {
        Index = index,
        Name = name,
        Keys =
        [
            new Key { Row = 1, Col = 0, RawKeycode = 0x04, DisplayLabel = "A", X = 9.5, Y = 3.5 },
            new Key { Row = 2, Col = 2, RawKeycode = 0x05, DisplayLabel = "B", X = 7.0, Y = 1.0 },
            new Key { Row = 5, Col = 0, RawKeycode = 0x2C, DisplayLabel = "Space", X = 12.5, Y = 6.0 },
        ],
    };

    [Fact]
    public void RenderLayer_DrawsToCanvas_WithoutError()
    {
        using var bitmap = new SKBitmap(1458, 480);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        var layer = MakeLayer(name: "Base");
        BoardRenderer.RenderLayer(canvas, layer, totalLayers: 8, userLayerColors: null, yOffset: 0);

        // Verify the canvas was drawn to (not all black)
        // Check key area instead of header text (text may not render on headless CI runners)
        // Key B at (7.0, 1.0) → pixel center (7.0*60+30, 1.0*60+30+40) = (450, 130)
        var pixel = bitmap.GetPixel(450, 130);
        Assert.NotEqual(SKColors.Black, pixel);
    }

    [Fact]
    public void RenderLayer_HideThumbClusters_SkipsThumbKeys()
    {
        using var bitmap = new SKBitmap(1458, 480);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        var layer = MakeLayer();
        BoardRenderer.RenderLayer(canvas, layer, totalLayers: 8, userLayerColors: null,
            yOffset: 0, hideThumbClusters: true);

        // Thumb key at (5,0) = position (12.5, 6.0) * 60 = pixel (750, 360) + header offset 40 = (750, 400)
        // With hide thumbs, this area should remain black (not drawn)
        var thumbPixel = bitmap.GetPixel(750, 400);
        Assert.Equal(SKColors.Black, thumbPixel);
    }

    [Fact]
    public void GetLayerBlockHeight_FullHeight()
    {
        Assert.Equal(BoardRenderer.LayerBlockHeight, BoardRenderer.GetLayerBlockHeight(false));
    }

    [Fact]
    public void GetLayerBlockHeight_HiddenThumbs_HeightAccountsForRemainingKeys()
    {
        var hidden = BoardRenderer.GetLayerBlockHeight(true);
        // L-Mod keys (non-thumb) also occupy Y=5-6, so height may be same or slightly less.
        // The key assertion is that it doesn't exceed full height.
        Assert.True(hidden <= BoardRenderer.LayerBlockHeight);
        Assert.True(hidden > 0);
    }

    [Fact]
    public void ParseColor_ValidHex_ReturnsSKColor()
    {
        var color = BoardRenderer.ParseColor("#FF8800");
        Assert.Equal(0xFF, color.Red);
        Assert.Equal(0x88, color.Green);
        Assert.Equal(0x00, color.Blue);
    }

    [Fact]
    public void ParseColor_InvalidHex_ReturnsWhite()
    {
        Assert.Equal(SKColors.White, BoardRenderer.ParseColor("#ZZZ"));
    }

    [Fact]
    public void RenderLayer_WithUserColors_AppliesCustomColor()
    {
        using var bitmap = new SKBitmap(1458, 480);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        var layer = MakeLayer(index: 0, name: "Custom");
        var userColors = new Dictionary<int, string> { { 0, "#FF0000" } };
        BoardRenderer.RenderLayer(canvas, layer, totalLayers: 8, userLayerColors: userColors, yOffset: 0);

        // Key B at (7.0, 1.0) → pixel center (7.0*60+30, 1.0*60+30) = (450, 90) + header 40 = (450, 130)
        var keyPixel = bitmap.GetPixel(450, 130);
        // Should have red-ish hue from user color (not black)
        Assert.NotEqual(SKColors.Black, keyPixel);
        Assert.True(keyPixel.Red > 0);
    }
}
