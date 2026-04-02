using SkiaSharp;

namespace SvalboardLayerViz.Debug;

public static class IconGenerator
{
    public static void Generate(string outputPath, int size = 256)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var bg = SKColor.Parse("#1E1E2E");
        var dot = SKColor.Parse("#CDD6F4");
        var accent = SKColor.Parse("#89B4FA");

        // Rounded background
        using var bgPaint = new SKPaint { Color = bg, IsAntialias = true };
        var cornerRadius = size * 0.18f;
        canvas.DrawRoundRect(new SKRect(0, 0, size, size), cornerRadius, cornerRadius, bgPaint);

        // Cluster pattern: 5 dots in a cross
        var center = size / 2f;
        var smallR = size * 0.08f;
        var bigR = size * 0.14f;
        var offset = size * 0.26f;

        using var accentPaint = new SKPaint { Color = accent, IsAntialias = true };
        using var dotPaint = new SKPaint { Color = dot, IsAntialias = true };

        // Center (accent blue)
        canvas.DrawCircle(center, center, bigR, accentPaint);
        // N, S, W, E (light grey)
        canvas.DrawCircle(center, center - offset, smallR, dotPaint);
        canvas.DrawCircle(center, center + offset, smallR, dotPaint);
        canvas.DrawCircle(center - offset, center, smallR, dotPaint);
        canvas.DrawCircle(center + offset, center, smallR, dotPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(outputPath);
        data.SaveTo(stream);

        Console.WriteLine($"Icon saved to {outputPath} ({size}x{size})");
    }
}
