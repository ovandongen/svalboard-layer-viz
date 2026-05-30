using System.Text.Json;

namespace SvalboardLayerViz.Core.Persistence;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> so the app's JSON boundaries stay
/// consistent instead of each service spinning up its own ad-hoc options.
/// </summary>
public static class CoreJson
{
    /// <summary>
    /// Our own persistence round-trip (user settings, history snapshots):
    /// pretty-printed, camelCase keys, case-insensitive on read so older
    /// PascalCase files still load.
    /// </summary>
    public static JsonSerializerOptions Default { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Lenient read of <em>external</em> JSON we don't own (e.g. the firmware
    /// layout definition). Read-only; property names come from
    /// <c>[JsonPropertyName]</c> attributes, case-insensitive for safety. Kept
    /// separate from <see cref="Default"/> on purpose — this is a parser for a
    /// foreign schema, not our persistence format.
    /// </summary>
    public static JsonSerializerOptions ExternalRead { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
