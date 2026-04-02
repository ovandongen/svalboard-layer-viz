using SvalboardLayerViz.Core.Export;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.Export;

public class ExportServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ExportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"export-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static IReadOnlyList<Layer> MakeLayers() =>
    [
        new Layer
        {
            Index = 0,
            Name = "Base",
            Keys =
            [
                new Key { Row = 1, Col = 0, RawKeycode = 0x04, DisplayLabel = "A", X = 9.5, Y = 3.5 },
                new Key { Row = 2, Col = 2, RawKeycode = 0x05, DisplayLabel = "B", X = 7.0, Y = 1.0 },
            ],
        },
        new Layer
        {
            Index = 1,
            Name = "Nav",
            Keys =
            [
                new Key { Row = 1, Col = 0, RawKeycode = 0x50, DisplayLabel = "Left", X = 9.5, Y = 3.5 },
                new Key { Row = 2, Col = 2, RawKeycode = 0x52, DisplayLabel = "Up", X = 7.0, Y = 1.0 },
            ],
        },
    ];

    [Fact]
    public void ExportPng_ProducesValidPngFile()
    {
        var path = Path.Combine(_tempDir, "test.png");
        var options = new ExportOptions
        {
            Format = ExportFormat.Png,
            SelectedLayerIndices = [0, 1],
            OutputPath = path,
            Scale = 1.0f,
        };

        ExportService.Export(options, MakeLayers(), totalLayers: 2);

        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 100);
        // PNG magic bytes
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
    }

    [Fact]
    public void ExportPdf_ProducesValidPdfFile()
    {
        var path = Path.Combine(_tempDir, "test.pdf");
        var options = new ExportOptions
        {
            Format = ExportFormat.Pdf,
            SelectedLayerIndices = [0],
            OutputPath = path,
        };

        ExportService.Export(options, MakeLayers(), totalLayers: 2);

        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 100);
        // PDF magic bytes
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    [Fact]
    public void ExportSvg_ProducesValidSvgFile()
    {
        var path = Path.Combine(_tempDir, "test.svg");
        var options = new ExportOptions
        {
            Format = ExportFormat.Svg,
            SelectedLayerIndices = [0],
            OutputPath = path,
        };

        ExportService.Export(options, MakeLayers(), totalLayers: 2);

        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("<svg", content);
    }

    [Fact]
    public void ExportPng_WithHideThumbClusters_ProducesValidFile()
    {
        var path = Path.Combine(_tempDir, "hidden.png");

        ExportService.Export(new ExportOptions
        {
            Format = ExportFormat.Png,
            SelectedLayerIndices = [0],
            OutputPath = path,
            Scale = 1.0f,
            HideThumbClusters = new Dictionary<int, bool> { { 0, true } },
        }, MakeLayers(), totalLayers: 2);

        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        // Still a valid PNG
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
    }

    [Fact]
    public void ExportPdf_MultipleLayers_CreatesFile()
    {
        var path = Path.Combine(_tempDir, "multi.pdf");
        var options = new ExportOptions
        {
            Format = ExportFormat.Pdf,
            SelectedLayerIndices = [0, 1],
            OutputPath = path,
        };

        ExportService.Export(options, MakeLayers(), totalLayers: 2);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 100);
    }
}
