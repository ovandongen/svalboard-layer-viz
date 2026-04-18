using SvalboardLayerViz.Core.Protocol;

namespace SvalboardLayerViz.Core.QmkSettings;

/// <summary>
/// Single source of truth for known QMK firmware settings, grouped by category.
/// Mirrors <see cref="Keymap.KeycodeCatalog"/> pattern: static, grouped lists,
/// lazy lookup dictionary, fallback for unknown IDs.
/// Reference: vial-gui qmk_settings.json, vial-qmk quantum/qmk_settings.h
/// Bitfield settings (0x01, 0x03, 0x08, 0x15) are excluded — they need per-bit UI.
/// </summary>
public static class QmkSettingsCatalog
{
    // --- Grouped catalog lists ---

    public static readonly IReadOnlyList<QmkSettingDescriptor> TapHold =
    [
        new(VialCommands.QmkSettingTappingTerm, "QmkSetting_TappingTerm", "TapHold",
            QmkSettingType.UInt16, 0, 10000, 200),
        new(VialCommands.QmkSettingTapCodeDelay, "QmkSetting_TapCodeDelay", "TapHold",
            QmkSettingType.UInt16, 0, 1000, 10),
        new(VialCommands.QmkSettingTapHoldCapsDelay, "QmkSetting_TapHoldCapsDelay", "TapHold",
            QmkSettingType.UInt16, 0, 1000, 80),
        new(VialCommands.QmkSettingTappingToggle, "QmkSetting_TappingToggle", "TapHold",
            QmkSettingType.UInt8, 0, 100, 5),
        new(VialCommands.QmkSettingQuickTapTerm, "QmkSetting_QuickTapTerm", "TapHold",
            QmkSettingType.UInt16, 0, 10000, 200),
        new(VialCommands.QmkSettingFlowTapTerm, "QmkSetting_FlowTapTerm", "TapHold",
            QmkSettingType.UInt16, 0, 10000, 0),
        new(VialCommands.QmkSettingPermissiveHold, "QmkSetting_PermissiveHold", "TapHold",
            QmkSettingType.Boolean, 0, 1, 0),
        new(VialCommands.QmkSettingHoldOnOtherKeyPress, "QmkSetting_HoldOnOtherKeyPress", "TapHold",
            QmkSettingType.Boolean, 0, 1, 0),
        new(VialCommands.QmkSettingRetroTapping, "QmkSetting_RetroTapping", "TapHold",
            QmkSettingType.Boolean, 0, 1, 0),
        new(VialCommands.QmkSettingChordalHold, "QmkSetting_ChordalHold", "TapHold",
            QmkSettingType.Boolean, 0, 1, 0),
    ];

    public static readonly IReadOnlyList<QmkSettingDescriptor> OneShot =
    [
        new(VialCommands.QmkSettingOneShotTapToggle, "QmkSetting_OneShotTapToggle", "OneShot",
            QmkSettingType.UInt8, 0, 50, 5),
        new(VialCommands.QmkSettingOneShotTimeout, "QmkSetting_OneShotTimeout", "OneShot",
            QmkSettingType.UInt16, 0, 60000, 5000),
    ];

    public static readonly IReadOnlyList<QmkSettingDescriptor> Combos =
    [
        new(VialCommands.QmkSettingComboTerm, "QmkSetting_ComboTerm", "Combos",
            QmkSettingType.UInt16, 0, 10000, 50),
    ];

    public static readonly IReadOnlyList<QmkSettingDescriptor> AutoShift =
    [
        new(VialCommands.QmkSettingAutoShiftFlags, "QmkSetting_AutoShift_Enable", "AutoShift",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 0),
        new(VialCommands.QmkSettingAutoShiftFlags, "QmkSetting_AutoShift_Modifiers", "AutoShift",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 1),
        new(VialCommands.QmkSettingAutoShiftFlags, "QmkSetting_AutoShift_NoSpecial", "AutoShift",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 2),
        new(VialCommands.QmkSettingAutoShiftFlags, "QmkSetting_AutoShift_NoNumeric", "AutoShift",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 3),
        new(VialCommands.QmkSettingAutoShiftFlags, "QmkSetting_AutoShift_NoAlpha", "AutoShift",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 4),
        new(VialCommands.QmkSettingAutoShiftFlags, "QmkSetting_AutoShift_Repeat", "AutoShift",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 5),
        new(VialCommands.QmkSettingAutoShiftFlags, "QmkSetting_AutoShift_NoRepeat", "AutoShift",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 6),
        new(VialCommands.QmkSettingAutoShiftTimeout, "QmkSetting_AutoShiftTimeout", "AutoShift",
            QmkSettingType.UInt16, 0, 1000, 175),
    ];

    public static readonly IReadOnlyList<QmkSettingDescriptor> MouseKeys =
    [
        new(VialCommands.QmkSettingMouseKeyDelay, "QmkSetting_MouseKeyDelay", "MouseKeys",
            QmkSettingType.UInt16, 0, 10000, 300),
        new(VialCommands.QmkSettingMouseKeyInterval, "QmkSetting_MouseKeyInterval", "MouseKeys",
            QmkSettingType.UInt16, 0, 10000, 50),
        new(VialCommands.QmkSettingMouseKeyMoveDelta, "QmkSetting_MouseKeyMoveDelta", "MouseKeys",
            QmkSettingType.UInt16, 0, 1000, 8),
        new(VialCommands.QmkSettingMouseKeyMaxSpeed, "QmkSetting_MouseKeyMaxSpeed", "MouseKeys",
            QmkSettingType.UInt16, 0, 1000, 10),
        new(VialCommands.QmkSettingMouseKeyTimeToMax, "QmkSetting_MouseKeyTimeToMax", "MouseKeys",
            QmkSettingType.UInt16, 0, 1000, 20),
        new(VialCommands.QmkSettingMouseKeyWheelDelay, "QmkSetting_MouseKeyWheelDelay", "MouseKeys",
            QmkSettingType.UInt16, 0, 10000, 300),
        new(VialCommands.QmkSettingMouseKeyWheelInterval, "QmkSetting_MouseKeyWheelInterval", "MouseKeys",
            QmkSettingType.UInt16, 0, 10000, 100),
        new(VialCommands.QmkSettingMouseKeyWheelMaxSpeed, "QmkSetting_MouseKeyWheelMaxSpeed", "MouseKeys",
            QmkSettingType.UInt16, 0, 1000, 8),
        new(VialCommands.QmkSettingMouseKeyWheelTimeToMax, "QmkSetting_MouseKeyWheelTimeToMax", "MouseKeys",
            QmkSettingType.UInt16, 0, 1000, 40),
    ];

    // --- Bitfield settings: each bit becomes a separate Boolean entry ---

    public static readonly IReadOnlyList<QmkSettingDescriptor> GraveEscape =
    [
        new(VialCommands.QmkSettingGraveEscOverride, "QmkSetting_GraveEsc_Alt", "GraveEscape",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 0),
        new(VialCommands.QmkSettingGraveEscOverride, "QmkSetting_GraveEsc_Ctrl", "GraveEscape",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 1),
        new(VialCommands.QmkSettingGraveEscOverride, "QmkSetting_GraveEsc_Gui", "GraveEscape",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 2),
        new(VialCommands.QmkSettingGraveEscOverride, "QmkSetting_GraveEsc_Shift", "GraveEscape",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 3),
    ];

    public static readonly IReadOnlyList<QmkSettingDescriptor> Magic =
    [
        new(VialCommands.QmkSettingMagic, "QmkSetting_Magic_SwapCapsCtrl", "Magic",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 0),
        new(VialCommands.QmkSettingMagic, "QmkSetting_Magic_CapsToCtrl", "Magic",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 1),
        new(VialCommands.QmkSettingMagic, "QmkSetting_Magic_SwapLAltLGui", "Magic",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 2),
        new(VialCommands.QmkSettingMagic, "QmkSetting_Magic_SwapRAltRGui", "Magic",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 3),
        new(VialCommands.QmkSettingMagic, "QmkSetting_Magic_NoGui", "Magic",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 4),
        new(VialCommands.QmkSettingMagic, "QmkSetting_Magic_SwapGraveEsc", "Magic",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 5),
        new(VialCommands.QmkSettingMagic, "QmkSetting_Magic_SwapBsBackslash", "Magic",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 6),
        new(VialCommands.QmkSettingMagic, "QmkSetting_Magic_Nkro", "Magic",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 7),
        new(VialCommands.QmkSettingMagic, "QmkSetting_Magic_SwapLCtlLGui", "Magic",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 8),
        new(VialCommands.QmkSettingMagic, "QmkSetting_Magic_SwapRCtlRGui", "Magic",
            QmkSettingType.Boolean, 0, 1, 0, BitIndex: 9),
    ];

    public static readonly IReadOnlyList<QmkSettingDescriptor> Misc = [];

    public static IReadOnlyList<(string GroupName, IReadOnlyList<QmkSettingDescriptor> Entries)> AllGroups =>
    [
        ("Magic", Magic),
        ("Grave Escape", GraveEscape),
        ("Tap/Hold", TapHold),
        ("Auto-shift", AutoShift),
        ("Combos", Combos),
        ("One-shot", OneShot),
        ("Mouse Keys", MouseKeys),
        ("Misc", Misc),
    ];

    // --- Lazy lookup dicts ---

    private static Dictionary<ushort, QmkSettingDescriptor>? _byId;
    private static Dictionary<ushort, IReadOnlyList<QmkSettingDescriptor>>? _allById;

    public static IReadOnlyDictionary<ushort, QmkSettingDescriptor> ById =>
        _byId ??= BuildById();

    /// <summary>
    /// Returns ALL descriptors for a given QSID. For bitfield QSIDs this returns
    /// one entry per bit; for scalar QSIDs it returns a single entry.
    /// Returns null if the QSID is unknown.
    /// </summary>
    public static IReadOnlyList<QmkSettingDescriptor>? GetAllForId(ushort id) =>
        AllById.TryGetValue(id, out var list) ? list : null;

    private static IReadOnlyDictionary<ushort, IReadOnlyList<QmkSettingDescriptor>> AllById =>
        _allById ??= BuildAllById();

    /// <summary>
    /// Returns the first descriptor for a known setting ID, or a fallback descriptor
    /// for unknown IDs discovered by firmware but absent from the catalog.
    /// </summary>
    public static QmkSettingDescriptor GetOrFallback(ushort id) =>
        ById.TryGetValue(id, out var d)
            ? d
            : new QmkSettingDescriptor(id, $"Unknown (QSID 0x{id:X4})", "Misc",
                QmkSettingType.UInt16, 0, 65535, 0);

    /// <summary>
    /// Returns the byte width for a known setting. For scalar settings: 1 for UInt8, 2 for UInt16.
    /// For bitfield QSIDs: width of the parent field (1 if max bit &lt; 8, 2 otherwise).
    /// Unknown IDs default to 2 (u16). The firmware query does NOT include width —
    /// it must come from the catalog.
    /// </summary>
    public static byte GetWidth(ushort id)
    {
        var all = GetAllForId(id);
        if (all is null) return 2;

        var first = all[0];
        if (!first.IsBitfield)
            return first.Type is QmkSettingType.UInt8 or QmkSettingType.Boolean ? (byte)1 : (byte)2;

        // Bitfield: parent field width depends on how many bits are used
        var maxBit = all.Max(d => d.BitIndex);
        return maxBit >= 8 ? (byte)2 : (byte)1;
    }

    private static Dictionary<ushort, QmkSettingDescriptor> BuildById()
    {
        var dict = new Dictionary<ushort, QmkSettingDescriptor>();
        foreach (var (_, entries) in AllGroups)
            foreach (var e in entries)
                dict.TryAdd(e.Id, e);
        return dict;
    }

    private static Dictionary<ushort, IReadOnlyList<QmkSettingDescriptor>> BuildAllById()
    {
        var dict = new Dictionary<ushort, List<QmkSettingDescriptor>>();
        foreach (var (_, entries) in AllGroups)
        {
            foreach (var e in entries)
            {
                if (!dict.TryGetValue(e.Id, out var list))
                {
                    list = [];
                    dict[e.Id] = list;
                }
                list.Add(e);
            }
        }
        return dict.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<QmkSettingDescriptor>)kv.Value);
    }
}
