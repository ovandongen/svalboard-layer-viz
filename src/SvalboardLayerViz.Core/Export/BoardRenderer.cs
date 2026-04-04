using SkiaSharp;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.Core.Export;

/// <summary>
/// Renders a single keyboard layer to an SKCanvas using SkiaSharp.
/// Replicates the visual output of KeyView.axaml for export to PNG/PDF/SVG.
/// Uses BoardLayoutComputer for positioning (single source of truth shared with UI).
/// </summary>
public static class BoardRenderer
{
    private const float Scale = (float)SvalboardLayout.Scale;
    /// <summary>Margin around content to prevent border strokes from being clipped.</summary>
    public const float Margin = 4f;
    public const float BoardWidth = 24.3f * Scale + 2 * Margin;  // content + margins
    public const float BoardHeight = 7f * Scale + 2 * Margin;  // content + margins
    public const float HeaderHeight = 40f;
    public const float Spacing = 20f;
    public const float LayerBlockHeight = HeaderHeight + BoardHeight + Spacing;

    /// <summary>
    /// Computes the height of a layer block, accounting for hidden thumb clusters.
    /// </summary>
    public static float GetLayerBlockHeight(bool hideThumbClusters)
    {
        if (!hideThumbClusters)
            return LayerBlockHeight;

        return HeaderHeight + (float)BoardLayoutComputer.GetBoardHeightExcludingBottomClusters() + 2 * Margin + Spacing;
    }

    /// <summary>
    /// Renders a single layer (header + keys) to the given canvas at the specified Y offset.
    /// </summary>
    public static void RenderLayer(SKCanvas canvas, Layer layer, int totalLayers,
        Dictionary<int, string>? userLayerColors, float yOffset,
        bool hideThumbClusters = false, bool printFriendly = true,
        bool creativeThumbLayout = false)
    {
        var userColor = userLayerColors?.GetValueOrDefault(layer.Index);
        var colors = LayerColorService.GetLayerColors(layer.Index, totalLayers,
            layer.ColorHue, layer.ColorSat, layer.ColorVal, userColor, printFriendly);

        RenderHeader(canvas, layer, colors, yOffset);

        var layout = BoardLayoutComputer.Compute(layer);
        var keysY = yOffset + HeaderHeight + Margin;

        RenderHand(canvas, layout.LeftHand, false, layer.Index, totalLayers, userLayerColors, colors, keysY, hideThumbClusters, printFriendly, creativeThumbLayout);
        RenderHand(canvas, layout.RightHand, true, layer.Index, totalLayers, userLayerColors, colors, keysY, hideThumbClusters, printFriendly, creativeThumbLayout);
    }

    private static void RenderHand(SKCanvas canvas, PositionedHand hand,
        bool isRightHand, int layerIndex, int totalLayers, Dictionary<int, string>? userLayerColors,
        LayerColors colors, float keysY, bool hideThumbClusters, bool printFriendly,
        bool creativeThumbLayout)
    {
        // Render finger clusters
        foreach (var cluster in hand.FingerClusters)
        {
            if (hideThumbClusters && BoardLayoutComputer.BottomClusters.Contains(cluster.Name))
                continue;
            RenderCluster(canvas, cluster, layerIndex, totalLayers, userLayerColors, colors, keysY, printFriendly);
        }

        // Render thumb cluster
        if (hand.ThumbCluster is not null && !hideThumbClusters)
        {
            if (creativeThumbLayout)
                RenderCreativeThumbCluster(canvas, hand.ThumbCluster, isRightHand, layerIndex, totalLayers, userLayerColors, colors, keysY, printFriendly);
            else
                RenderCluster(canvas, hand.ThumbCluster, layerIndex, totalLayers, userLayerColors, colors, keysY, printFriendly);
        }
    }

    private static void RenderCluster(SKCanvas canvas, PositionedCluster cluster,
        int layerIndex, int totalLayers, Dictionary<int, string>? userLayerColors,
        LayerColors colors, float keysY, bool printFriendly)
    {
        foreach (var posKey in cluster.Keys)
        {
            LayerColors? targetColors = null;
            if (posKey.Key.IsLayerSwitch && posKey.Key.TargetLayer.HasValue)
            {
                var targetColor = userLayerColors?.GetValueOrDefault(posKey.Key.TargetLayer.Value);
                targetColors = LayerColorService.GetLayerColors(posKey.Key.TargetLayer.Value, totalLayers,
                    userHexColor: targetColor, printFriendly: printFriendly);
            }

            RenderKey(canvas, posKey, layerIndex, colors, targetColors, keysY);
        }
    }

    private static void RenderHeader(SKCanvas canvas, Layer layer, LayerColors colors, float yOffset)
    {
        using var font = new SKFont(SKTypeface.Default, 20) { Embolden = true };
        using var paint = new SKPaint { Color = ParseColor(colors.Accent), IsAntialias = true };

        canvas.DrawText(layer.DisplayName, BoardWidth / 2, yOffset + 28,
            SKTextAlign.Center, font, paint);
    }

    private static void RenderKey(SKCanvas canvas, PositionedKey posKey, int layerIndex,
        LayerColors colors, LayerColors? targetColors, float keysY)
    {
        var key = posKey.Key;
        var style = KeyStyleResolver.Resolve(key, layerIndex, colors, targetColors);

        var x = Margin + (float)posKey.BoardX;
        var y = keysY + (float)posKey.BoardY;
        var w = (float)posKey.Width;
        var h = (float)posKey.Height;
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

    #region Creative thumb layout

    private const float CreativeCanvasW = 880f;
    private const float CreativeCanvasH = 435f;
    private const float CreativeScale = 1.4f;
    private const float C2Taper = 12f; // px per side over C:2's full height

    private record CreativeKey(int Column, float Left, float Top, float Width, float Height, int ZIndex);

    /// <summary>Which edges of a key taper (inner edges facing C:2).</summary>
    [Flags]
    private enum TaperEdge { None = 0, Left = 1, Right = 2, Both = 3 }

    /// <summary>Returns the taper edge(s) for a given key column and hand.</summary>
    private static TaperEdge GetTaperEdge(int column, bool isRightHand) => column switch
    {
        2 => TaperEdge.Both,
        // Left hand: C:3 inner=right, C:1 inner=left, C:0 inner=left
        // Right hand: mirrored
        3 => isRightHand ? TaperEdge.Left : TaperEdge.Right,
        1 => isRightHand ? TaperEdge.Right : TaperEdge.Left,
        0 => isRightHand ? TaperEdge.Right : TaperEdge.Left,
        _ => TaperEdge.None,
    };

    /// <summary>Computes the 4 corner points of a trapezoid key.</summary>
    private static (SKPoint TL, SKPoint TR, SKPoint BR, SKPoint BL) GetTrapezoidCorners(
        CreativeKey ck, TaperEdge edges)
    {
        var taper = C2Taper / 430f * ck.Height;

        if (ck.Column == 2)
        {
            // C:2 widens at bottom (flares outward)
            return (
                TL: new(ck.Left, ck.Top),
                TR: new(ck.Left + ck.Width, ck.Top),
                BR: new(ck.Left + ck.Width + C2Taper, ck.Top + ck.Height),
                BL: new(ck.Left - C2Taper, ck.Top + ck.Height));
        }

        // Adjacent keys: inner edge narrower at bottom (C:2 flares into the gap at bottom)
        return (
            TL: new(ck.Left, ck.Top),
            TR: new(ck.Left + ck.Width, ck.Top),
            BR: new(ck.Left + ck.Width - (edges.HasFlag(TaperEdge.Right) ? taper : 0), ck.Top + ck.Height),
            BL: new(ck.Left + (edges.HasFlag(TaperEdge.Left) ? taper : 0), ck.Top + ck.Height));
    }

    /// <summary>Builds an SKPath for a trapezoid key shape with rounded corners.</summary>
    private static SKPath BuildTrapezoidPath(CreativeKey ck, TaperEdge edges, float radius)
    {
        var (tl, tr, br, bl) = GetTrapezoidCorners(ck, edges);

        var path = new SKPath();
        // Start past TL corner on top edge, go clockwise using ArcTo for rounded corners
        path.MoveTo(tl.X + radius, tl.Y);
        path.ArcTo(tr, br, radius);   // TR corner
        path.ArcTo(br, bl, radius);   // BR corner
        path.ArcTo(bl, tl, radius);   // BL corner
        path.ArcTo(tl, tr, radius);   // TL corner
        path.Close();
        return path;
    }

    private static readonly CreativeKey[] LeftThumbKeys =
    [
        new(2, 350, 5, 175, 430, 0),
        new(3, 30, 30, 280, 140, 1),
        new(1, 565, 30, 285, 140, 1),
        new(4, 30, 205, 370, 100, 1),
        new(0, 570, 205, 280, 140, 1),
        new(5, 370, 30, 135, 140, 2),
    ];

    private static readonly CreativeKey[] RightThumbKeys =
    [
        new(2, 355, 5, 175, 430, 0),
        new(3, 570, 30, 280, 140, 1),
        new(1, 30, 30, 285, 140, 1),
        new(4, 480, 205, 370, 100, 1),
        new(0, 30, 205, 280, 140, 1),
        new(5, 375, 30, 135, 140, 2),
    ];

    /// <summary>Builds the C:2 clip path (EvenOdd) that cuts out C:4 bite and C:5 hole.</summary>
    private static SKPath BuildC2ClipPath(bool isRightHand)
    {
        var path = new SKPath { FillType = SKPathFillType.EvenOdd };

        // Full C:2 trapezoid — padded outward so the clip doesn't chop the stroke.
        // The drawn trapezoid path defines the actual visible shape; this outer boundary
        // only needs to be larger so the EvenOdd rule correctly subtracts the bite/hole.
        const float pad = 5f;
        path.MoveTo(-pad, -pad);
        path.LineTo(175 + pad, -pad);
        path.LineTo(175 + C2Taper + pad, 430 + pad);
        path.LineTo(-C2Taper - pad, 430 + pad);
        path.Close();

        // C:4 bite — edges must reach C:2's angled trapezoid edge
        // Left edge: (0,0)→(-C2Taper,430), at y: x = -C2Taper * y / 430
        // Right edge: (175,0)→(175+C2Taper,430), at y: x = 175 + C2Taper * y / 430
        if (!isRightHand)
        {
            // Extend bite past trapezoid edge into padded zone so stroke is fully cut
            var edgeAt190 = -C2Taper * 190f / 430f - pad;
            var edgeAt310 = -C2Taper * 310f / 430f - pad;
            path.MoveTo(edgeAt190, 190);
            path.LineTo(36, 190);
            path.ArcTo(new SKRect(36, 190, 60, 214), 270, 90, false);
            path.LineTo(60, 286);
            path.ArcTo(new SKRect(36, 286, 60, 310), 0, 90, false);
            path.LineTo(edgeAt310, 310);
            path.Close();
        }
        else
        {
            var edgeAt190 = 175 + C2Taper * 190f / 430f + pad;
            var edgeAt310 = 175 + C2Taper * 310f / 430f + pad;
            path.MoveTo(edgeAt190, 190);
            path.LineTo(139, 190);
            path.ArcTo(new SKRect(115, 190, 139, 214), 270, -90, false);
            path.LineTo(115, 286);
            path.ArcTo(new SKRect(115, 286, 139, 310), 180, -90, false);
            path.LineTo(edgeAt310, 310);
            path.Close();
        }

        // C:5 island hole (same for both hands in C:2-local coords)
        path.MoveTo(34, 15);
        path.LineTo(141, 15);
        path.ArcTo(new SKRect(141, 15, 165, 39), 270, 90, false);
        path.LineTo(165, 151);
        path.ArcTo(new SKRect(141, 151, 165, 175), 0, 90, false);
        path.LineTo(34, 175);
        path.ArcTo(new SKRect(10, 151, 34, 175), 90, 90, false);
        path.LineTo(10, 39);
        path.ArcTo(new SKRect(10, 15, 34, 39), 180, 90, false);
        path.Close();

        return path;
    }

    private static void RenderCreativeThumbCluster(SKCanvas canvas, PositionedCluster cluster,
        bool isRightHand, int layerIndex, int totalLayers,
        Dictionary<int, string>? userLayerColors, LayerColors colors, float keysY, bool printFriendly)
    {
        var keys = cluster.Keys.OrderBy(k => k.Key.Col).ToList();
        var layout = isRightHand ? RightThumbKeys : LeftThumbKeys;

        // Compute transform: creative canvas → export coordinates
        var clusterX = Margin + (float)cluster.Left;
        var clusterY = keysY + (float)cluster.Top;
        var sx = (float)cluster.Width / CreativeCanvasW * CreativeScale;
        var sy = (float)cluster.Height / CreativeCanvasH * CreativeScale;

        // Scale origin: left hand from right edge (1.0, 0.5), right hand from left edge (0.0, 0.5)
        var originX = isRightHand ? 0f : (float)cluster.Width;
        var originY = (float)cluster.Height / 2f;

        canvas.Save();
        canvas.Translate(clusterX, clusterY);
        canvas.Translate(originX, originY);
        canvas.Scale(CreativeScale, CreativeScale);
        canvas.Translate(-originX, -originY);
        // Now scale from creative canvas coords to cluster coords
        var csx = (float)cluster.Width / CreativeCanvasW;
        var csy = (float)cluster.Height / CreativeCanvasH;
        canvas.Scale(csx, csy);

        // Compensate stroke width for canvas scaling so it matches normal key strokes (1.5px output)
        var effectiveScale = csx * CreativeScale;
        var normalStroke = 1.5f / effectiveScale;
        var c2Stroke = 2.0f / effectiveScale; // slightly thicker for C:2's long angled edges

        // Render keys sorted by ZIndex
        foreach (var ck in layout.OrderBy(k => k.ZIndex))
        {
            if (ck.Column >= keys.Count) continue;
            var posKey = keys[ck.Column];

            var style = KeyStyleResolver.Resolve(posKey.Key, layerIndex, colors,
                posKey.Key.IsLayerSwitch && posKey.Key.TargetLayer.HasValue
                    ? LayerColorService.GetLayerColors(posKey.Key.TargetLayer.Value, totalLayers,
                        userHexColor: userLayerColors?.GetValueOrDefault(posKey.Key.TargetLayer.Value),
                        printFriendly: printFriendly)
                    : null);

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

            var radius = ck.Column == 2 ? 16f : 14f;
            var taperEdge = GetTaperEdge(ck.Column, isRightHand);

            // Apply clip for C:2
            if (ck.Column == 2)
            {
                canvas.Save();
                using var clipPath = BuildC2ClipPath(isRightHand);
                canvas.Translate(ck.Left, ck.Top);
                canvas.ClipPath(clipPath);
                canvas.Translate(-ck.Left, -ck.Top);
            }

            // Draw key shape (trapezoid or rounded rect)
            var strokeWidth = ck.Column == 2 ? c2Stroke : normalStroke;
            if (taperEdge != TaperEdge.None)
            {
                using var keyPath = BuildTrapezoidPath(ck, taperEdge, radius);
                using (var fillPaint = new SKPaint { Color = bgColor, IsAntialias = true, Style = SKPaintStyle.Fill })
                    canvas.DrawPath(keyPath, fillPaint);
                using (var strokePaint = new SKPaint { Color = borderColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth })
                    canvas.DrawPath(keyPath, strokePaint);
            }
            else
            {
                var rect = new SKRect(ck.Left, ck.Top, ck.Left + ck.Width, ck.Top + ck.Height);
                var rrect = new SKRoundRect(rect, radius);
                using (var fillPaint = new SKPaint { Color = bgColor, IsAntialias = true, Style = SKPaintStyle.Fill })
                    canvas.DrawRoundRect(rrect, fillPaint);
                using (var strokePaint = new SKPaint { Color = borderColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth })
                    canvas.DrawRoundRect(rrect, strokePaint);
            }

            // Labels
            var key = posKey.Key;
            var displayLabel = key.IsTransparent ? (key.EffectiveLabel ?? "___") : key.DisplayLabel;
            var centerX = ck.Left + ck.Width / 2;
            var centerY = ck.Column == 2
                ? ck.Top + 240 + (ck.Height - 240) / 2  // push C:2 label down below C:5
                : ck.Top + ck.Height / 2;

            if (key.SecondaryLabel is not null)
            {
                using var secFont = new SKFont(SKTypeface.Default, 26);
                using var secPaint = new SKPaint
                {
                    Color = textColor.WithAlpha((byte)(textColor.Alpha * 0.8)),
                    IsAntialias = true,
                };
                canvas.DrawText(key.SecondaryLabel, centerX, centerY - 16,
                    SKTextAlign.Center, secFont, secPaint);
            }

            {
                var fontSize = 36f;
                using var labelFont = new SKFont(SKTypeface.Default, fontSize) { Embolden = true };
                using var labelPaint = new SKPaint { Color = textColor, IsAntialias = true };

                var textWidth = labelFont.MeasureText(displayLabel);
                if (textWidth > ck.Width - 10)
                    labelFont.Size = fontSize * ((ck.Width - 10) / textWidth);

                var textY = key.SecondaryLabel is not null ? centerY + 24 : centerY + 12;
                canvas.DrawText(displayLabel, centerX, textY,
                    SKTextAlign.Center, labelFont, labelPaint);
            }

            if (key.ShiftedLabel is not null)
            {
                using var shiftFont = new SKFont(SKTypeface.Default, 28);
                using var shiftPaint = new SKPaint
                {
                    Color = textColor.WithAlpha((byte)(textColor.Alpha * 0.7)),
                    IsAntialias = true,
                };
                canvas.DrawText(key.ShiftedLabel, ck.Left + ck.Width - 8, ck.Top + 32,
                    SKTextAlign.Right, shiftFont, shiftPaint);
            }

            // Restore C:2 clip
            if (ck.Column == 2)
                canvas.Restore();
        }

        canvas.Restore();
    }

    #endregion

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
