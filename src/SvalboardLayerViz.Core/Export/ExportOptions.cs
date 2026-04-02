namespace SvalboardLayerViz.Core.Export;

public record ExportOptions
{
    public required ExportFormat Format { get; init; }
    public required IReadOnlyList<int> SelectedLayerIndices { get; init; }
    public required string OutputPath { get; init; }

    /// <summary>Scale factor for PNG output (2.0 = high-DPI). Ignored for vector formats.</summary>
    public float Scale { get; init; } = 2.0f;

    /// <summary>Background color hex. Defaults to white (print-friendly).</summary>
    public string BoardBackground { get; init; } = "#FFFFFF";

    /// <summary>Per-layer thumb cluster visibility. Key = layer index, value = true to hide thumbs.</summary>
    public IReadOnlyDictionary<int, bool> HideThumbClusters { get; init; } = new Dictionary<int, bool>();

    /// <summary>Page size for PDF export. Defaults to A4 landscape.</summary>
    public PdfPageSize PdfPageSize { get; init; } = PdfPageSize.A4Landscape;
}
