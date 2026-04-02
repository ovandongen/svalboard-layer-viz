using SkiaSharp;
using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.Core.Export;

/// <summary>
/// Orchestrates multi-layer export to PNG, PDF, or SVG.
/// </summary>
public static class ExportService
{
    public static void Export(ExportOptions options, IReadOnlyList<Layer> layers, int totalLayers,
        Dictionary<int, string>? userLayerColors = null)
    {
        switch (options.Format)
        {
            case ExportFormat.Png:
                ExportPng(options, layers, totalLayers, userLayerColors);
                break;
            case ExportFormat.Pdf:
                ExportPdf(options, layers, totalLayers, userLayerColors);
                break;
            case ExportFormat.Svg:
                ExportSvg(options, layers, totalLayers, userLayerColors);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options.Format));
        }
    }

    private static void ExportPng(ExportOptions options, IReadOnlyList<Layer> layers, int totalLayers,
        Dictionary<int, string>? userLayerColors)
    {
        var (width, height) = ComputeCanvasSize(options, layers);
        var scaledWidth = (int)(width * options.Scale);
        var scaledHeight = (int)(height * options.Scale);

        using var bitmap = new SKBitmap(scaledWidth, scaledHeight);
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(options.Scale);
        canvas.Clear(BoardRenderer.ParseColor(options.BoardBackground));

        RenderLayers(canvas, options, layers, totalLayers, userLayerColors);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(options.OutputPath);
        data.SaveTo(stream);
    }

    private static void ExportPdf(ExportOptions options, IReadOnlyList<Layer> layers, int totalLayers,
        Dictionary<int, string>? userLayerColors)
    {
        var (pageWidth, pageHeight) = GetPageDimensions(options.PdfPageSize);
        const float margin = 30f;
        var usableWidth = pageWidth - 2 * margin;
        var usableHeight = pageHeight - 2 * margin;
        var scale = usableWidth / BoardRenderer.BoardWidth;

        using var stream = File.OpenWrite(options.OutputPath);
        using var document = SKDocument.CreatePdf(stream);

        var bgColor = BoardRenderer.ParseColor(options.BoardBackground);
        float yOnPage = 0;
        SKCanvas? pageCanvas = null;

        foreach (var idx in options.SelectedLayerIndices)
        {
            var layer = layers.FirstOrDefault(l => l.Index == idx);
            if (layer is null) continue;

            var hideThumb = options.HideThumbClusters.GetValueOrDefault(idx);
            var layerHeight = BoardRenderer.GetLayerBlockHeight(hideThumb) * scale;

            // Start a new page if this layer won't fit (or it's the first layer)
            if (pageCanvas is null || yOnPage + layerHeight > usableHeight)
            {
                if (pageCanvas is not null)
                    document.EndPage();

                pageCanvas = document.BeginPage(pageWidth, pageHeight);
                pageCanvas.Clear(bgColor);
                pageCanvas.Translate(margin, margin);
                yOnPage = 0;
            }

            pageCanvas.Save();
            pageCanvas.Translate(0, yOnPage);
            pageCanvas.Scale(scale);
            BoardRenderer.RenderLayer(pageCanvas, layer, totalLayers, userLayerColors,
                yOffset: 0, hideThumbClusters: hideThumb);
            pageCanvas.Restore();

            yOnPage += layerHeight;
        }

        if (pageCanvas is not null)
            document.EndPage();

        document.Close();
    }

    internal static (float Width, float Height) GetPageDimensions(PdfPageSize size) => size switch
    {
        PdfPageSize.A4Landscape => (841.89f, 595.28f),
        PdfPageSize.LetterLandscape => (792f, 612f),
        _ => (841.89f, 595.28f),
    };

    private static void ExportSvg(ExportOptions options, IReadOnlyList<Layer> layers, int totalLayers,
        Dictionary<int, string>? userLayerColors)
    {
        var (width, height) = ComputeCanvasSize(options, layers);

        using var stream = File.OpenWrite(options.OutputPath);
        using var canvas = SKSvgCanvas.Create(new SKRect(0, 0, width, height), stream);

        // SVG canvas doesn't support Clear, draw a background rect
        using (var bgPaint = new SKPaint
        {
            Color = BoardRenderer.ParseColor(options.BoardBackground),
            Style = SKPaintStyle.Fill,
        })
        {
            canvas.DrawRect(0, 0, width, height, bgPaint);
        }

        RenderLayers(canvas, options, layers, totalLayers, userLayerColors);
    }

    private static void RenderLayers(SKCanvas canvas, ExportOptions options,
        IReadOnlyList<Layer> layers, int totalLayers, Dictionary<int, string>? userLayerColors)
    {
        float yOffset = 0;
        foreach (var idx in options.SelectedLayerIndices)
        {
            var layer = layers.FirstOrDefault(l => l.Index == idx);
            if (layer is null) continue;

            var hideThumb = options.HideThumbClusters.GetValueOrDefault(idx);
            BoardRenderer.RenderLayer(canvas, layer, totalLayers, userLayerColors,
                yOffset, hideThumbClusters: hideThumb);

            yOffset += BoardRenderer.GetLayerBlockHeight(hideThumb);
        }
    }

    private static (float Width, float Height) ComputeCanvasSize(ExportOptions options, IReadOnlyList<Layer> layers)
    {
        float totalHeight = 0;
        foreach (var idx in options.SelectedLayerIndices)
        {
            var hideThumb = options.HideThumbClusters.GetValueOrDefault(idx);
            totalHeight += BoardRenderer.GetLayerBlockHeight(hideThumb);
        }
        return (BoardRenderer.BoardWidth, totalHeight);
    }
}
