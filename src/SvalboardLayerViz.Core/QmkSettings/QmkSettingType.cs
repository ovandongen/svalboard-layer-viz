namespace SvalboardLayerViz.Core.QmkSettings;

/// <summary>
/// Value type for a QMK firmware setting, used by <see cref="QmkSettingDescriptor"/>
/// to drive UI control selection (slider, checkbox, dropdown, etc.).
/// </summary>
public enum QmkSettingType
{
    Boolean,
    UInt8,
    UInt16,
    Enum,
}
