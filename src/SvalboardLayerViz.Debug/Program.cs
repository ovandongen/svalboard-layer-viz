using SvalboardLayerViz.Debug;

// Generate app icon PNG
var outputDir = Path.Combine(
    Path.GetDirectoryName(AppContext.BaseDirectory)!,
    "..", "..", "..", "..", "..", "src", "SvalboardLayerViz.App", "Assets");
Directory.CreateDirectory(outputDir);

var outputPath = Path.GetFullPath(Path.Combine(outputDir, "icon.png"));
IconGenerator.Generate(outputPath);
