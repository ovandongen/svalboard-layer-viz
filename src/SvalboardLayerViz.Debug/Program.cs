using SvalboardLayerViz.Debug;

var mode = args.Length > 0 ? args[0] : "icon";

switch (mode)
{
    case "led-probe":
        return LedProbe.Run();

    case "icon":
    {
        var outputDir = Path.Combine(
            Path.GetDirectoryName(AppContext.BaseDirectory)!,
            "..", "..", "..", "..", "..", "src", "SvalboardLayerViz.App", "Assets");
        Directory.CreateDirectory(outputDir);

        var outputPath = Path.GetFullPath(Path.Combine(outputDir, "icon.png"));
        IconGenerator.Generate(outputPath);
        return 0;
    }

    default:
        Console.WriteLine($"Unknown mode: {mode}");
        Console.WriteLine("Usage: dotnet run --project src/SvalboardLayerViz.Debug -- [icon|led-probe]");
        return 1;
}
