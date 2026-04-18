namespace SvalboardLayerViz.Core.Dynamic;

/// <summary>
/// Counts of each dynamic-entry table the firmware supports. Queried once at
/// load time via Vial sub-command 0xFE 0x0D 0x00.
/// </summary>
public sealed record DynamicEntryCounts(
    int TapDanceCount,
    int ComboCount,
    int KeyOverrideCount,
    int AltRepeatCount);
