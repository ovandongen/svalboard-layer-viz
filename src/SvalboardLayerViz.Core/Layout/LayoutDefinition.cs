using System.Text.Json.Serialization;

namespace SvalboardLayerViz.Core.Layout;

/// <summary>
/// Represents the parsed JSON keyboard definition from the Vial device.
/// This is deserialized from the XZ-compressed payload.
/// </summary>
public record LayoutDefinition
{
    [JsonPropertyName("matrix")]
    public MatrixDimensions Matrix { get; init; } = new();

    [JsonPropertyName("customKeycodes")]
    public List<CustomKeycodeDefinition>? CustomKeycodes { get; init; }

    [JsonPropertyName("layouts")]
    public Dictionary<string, object>? Layouts { get; init; }

    [JsonPropertyName("lighting")]
    public string? Lighting { get; init; }
}

public record MatrixDimensions
{
    [JsonPropertyName("rows")]
    public int Rows { get; init; }

    [JsonPropertyName("cols")]
    public int Cols { get; init; }
}

public record CustomKeycodeDefinition
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("shortName")]
    public string? ShortName { get; init; }
}
