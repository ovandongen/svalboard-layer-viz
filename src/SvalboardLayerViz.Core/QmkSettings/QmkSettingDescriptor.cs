namespace SvalboardLayerViz.Core.QmkSettings;

/// <summary>
/// Describes a known QMK setting: identity, display metadata, value type, and constraints.
/// This is a catalog entry (static metadata), not a live value. Live values come from the
/// device via <see cref="Models.KeyboardConfig.QmkSettingValue"/>.
/// </summary>
/// <param name="BitIndex">For bitfield settings: which bit (0-15) within the QSID value
/// this entry represents. -1 means the entry covers the whole value.</param>
public record QmkSettingDescriptor(
    ushort Id,
    string NameKey,
    string GroupKey,
    QmkSettingType Type,
    ushort Min,
    ushort Max,
    ushort DefaultValue,
    int BitIndex = -1)
{
    /// <summary>True when this descriptor represents a single bit within a packed QSID value.</summary>
    public bool IsBitfield => BitIndex >= 0;
}
