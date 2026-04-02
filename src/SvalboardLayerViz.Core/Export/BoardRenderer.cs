using SkiaSharp;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.Core.Export;

/// <summary>
/// Renders a single keyboard layer to an SKCanvas using SkiaSharp.
/// Replicates the visual output of KeyView.axaml for export to PNG/PDF/SVG.
/// </summary>
public static class BoardRenderer
{
    private const float Scale = (float)SvalboardLayout.Scale;
    public const float BoardWidth = 24.3f * Scale;  // 1458px
    public const float BoardHeight = 7f * Scale;     // 420px
    public const float HeaderHeight = 40f;
    public const float Spacing = 20f;
    public const float LayerBlockHeight = HeaderHeight + BoardHeight + Spacing; // 480px

    private static readonly HashSet<string> BottomClusters = ["L-Thumb", "R-Thumb", "L-Mod"];

    private static readonly Dictionary<(int, int), KeyPosition> _positionLookup;

    static BoardRenderer()
    {
        _positionLookup = SvalboardLayout.GetKeyPositions()
            .ToDictionary(p => (p.Row, p.Col));
    }

    /// <summary>
    /// Computes the height of a layer block, accounting for hidden thumb clusters.
    /// </summary>
    public static float GetLayerBlockHeight(bool hideThumbClusters)
    {
        if (!hideThumbClusters)
            return LayerBlockHeight;

        return HeaderHeight + GetBoardHeight(hideThumbClusters: true) + Spacing;
    }

    private static float GetBoardHeight(bool hideThumbClusters)
    {
        if (!hideThumbClusters)
            return BoardHeight;

        float maxY = 0;
        foreach (var pos in SvalboardLayout.GetKeyPositions())
        {
            if (BottomClusters.Contains(pos.Cluster))
                continue;
            var bottom = (float)(pos.Y + pos.Height);
            if (bottom > maxY) maxY = bottom;
        }
        return maxY * Scale;
    }

    /// <summary>
    /// Renders a single layer (header + keys) to the given canvas at the specified Y offset.
    /// </summary>
    public static void RenderLayer(SKCanvas canvas, Layer layer, int totalLayers,
        Dictionary<int, string>? userLayerColors, float yOffset,
        bool hideThumbClusters = false, bool printFriendly = true)
    {
        var userColor = userLayerColors?.GetValueOrDefault(layer.Index);
        var colors = LayerColorService.GetLayerColors(layer.Index, totalLayers,
            layer.ColorHue, layer.ColorSat, layer.ColorVal, userColor, printFriendly);

        RenderHeader(canvas, layer, colors, yOffset);

        var keysY = yOffset + HeaderHeight;
        foreach (var key in layer.Keys)
        {
            if (hideThumbClusters && IsThumbKey(key))
                continue;

            LayerColors? targetColors = null;
            if (key.IsLayerSwitch && key.TargetLayer.HasValue)
            {
                var targetColor = userLayerColors?.GetValueOrDefault(key.TargetLayer.Value);
                targetColors = LayerColorService.GetLayerColors(key.TargetLayer.Value, totalLayers,
                    userHexColor: targetColor, printFriendly: printFriendly);
            }

            RenderKey(canvas, key, layer.Index, colors, targetColors, keysY);
        }
    }

    private static bool IsThumbKey(Key key)
    {
        return _positionLookup.TryGetValue((key.Row, key.Col), out var pos)
               && BottomClusters.Contains(pos.Cluster);
    }

    private static void RenderHeader(SKCanvas canvas, Layer layer, LayerColors colors, float yOffset)
    {
        using var font = new SKFont(SKTypeface.Default, 20) { Embolden = true };
        using var paint = new SKPaint { Color = ParseColor(colors.Accent), IsAntialias = true };

        canvas.DrawText(layer.DisplayName, BoardWidth / 2, yOffset + 28,
            SKTextAlign.Center, font, paint);
    }

    private static void RenderKey(SKCanvas canvas, Key key, int layerIndex,
        LayerColors colors, LayerColors? targetColors, float keysY)
    {
        var style = KeyStyleResolver.Resolve(key, layerIndex, colors, targetColors);

        var x = (float)key.X * Scale;
        var y = keysY + (float)key.Y * Scale;
        var w = (float)key.Width * Scale;
        var h = (float)key.Height * Scale;
        var rect = new SKRect(x, y, x + w, y + h);
        var rrect = new SKRoundRect(rect, 6);

        var bgColor = ParseColor(style.Background);
        var borderColor = ParseColor(style.Border);
        var textColor = ParseColor(style.Text);
        if (style.Opacity < 1.0)
        {
            var alpha = (byte)(style.Opacity * 255);
            bgColor = bgColor.WithAlpha(alpha);
            borderColor = borderColor.WithAlpha(alpha);
            textColor = textColor.WithAlpha(alpha);
        }

        // Fill
        using (var fillPaint = new SKPaint { Color = bgColor, IsAntialias = true, Style = SKPaintStyle.Fill })
            canvas.DrawRoundRect(rrect, fillPaint);

        // Border
        using (var strokePaint = new SKPaint
        {
            Color = borderColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f
        })
            canvas.DrawRoundRect(rrect, strokePaint);

        // Labels
        var displayLabel = key.IsTransparent ? (key.EffectiveLabel ?? "___") : key.DisplayLabel;
        var centerX = x + w / 2;
        var centerY = y + h / 2;

        // Secondary label (modifier prefix)
        if (key.SecondaryLabel is not null)
        {
            using var secFont = new SKFont(SKTypeface.Default, 11);
            using var secPaint = new SKPaint
            {
                Color = textColor.WithAlpha((byte)(textColor.Alpha * 0.8)),
                IsAntialias = true,
            };
            canvas.DrawText(key.SecondaryLabel, centerX, centerY - 6,
                SKTextAlign.Center, secFont, secPaint);
        }

        // Primary label
        {
            var fontSize = 16f;
            using var labelFont = new SKFont(SKTypeface.Default, fontSize) { Embolden = true };
            using var labelPaint = new SKPaint { Color = textColor, IsAntialias = true };

            // Shrink font if text is too wide for key
            var textWidth = labelFont.MeasureText(displayLabel);
            if (textWidth > w - 6)
            {
                labelFont.Size = fontSize * ((w - 6) / textWidth);
            }

            var textY = key.SecondaryLabel is not null ? centerY + 10 : centerY + 6;
            canvas.DrawText(displayLabel, centerX, textY,
                SKTextAlign.Center, labelFont, labelPaint);
        }

        // Shifted label (top-right corner)
        if (key.ShiftedLabel is not null)
        {
            using var shiftFont = new SKFont(SKTypeface.Default, 12);
            using var shiftPaint = new SKPaint
            {
                Color = textColor.WithAlpha((byte)(textColor.Alpha * 0.7)),
                IsAntialias = true,
            };
            canvas.DrawText(key.ShiftedLabel, x + w - 4, y + 14,
                SKTextAlign.Right, shiftFont, shiftPaint);
        }
    }

    internal static SKColor ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6)
            return SKColors.White;

        var r = Convert.ToByte(hex[..2], 16);
        var g = Convert.ToByte(hex[2..4], 16);
        var b = Convert.ToByte(hex[4..6], 16);
        return new SKColor(r, g, b);
    }
}
