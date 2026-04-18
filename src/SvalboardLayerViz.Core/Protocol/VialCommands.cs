namespace SvalboardLayerViz.Core.Protocol;

/// <summary>
/// Vial/VIA protocol command IDs.
/// Reference: keybard-ng/src/services/usb.service.ts
/// </summary>
public static class VialCommands
{
    /// <summary>Standard HID report size for Vial communication.</summary>
    public const int ReportSize = 32;

    /// <summary>Usable data payload per message (ReportSize minus overhead).</summary>
    public const int PayloadSize = 28;

    // --- VIA commands ---

    public const byte GetProtocolVersion = 0x01;
    public const byte GetKeyboardValue = 0x02;
    public const byte SwitchMatrixState = 0x03; // Sub-command for GetKeyboardValue
    public const byte SetKeyboardValue = 0x03;
    public const byte GetKeycode = 0x04;
    public const byte SetKeycode = 0x05;
    public const byte LightingSetValue = 0x07;
    public const byte LightingGetValue = 0x08;
    public const byte LightingSave = 0x09;

    /// <summary>
    /// Sub-command for LightingGetValue that returns current QMK rgblight hue+sat.
    /// Response: [0x08, 0x83, H, S, ...]. Confirmed working on Svalboard firmware.
    /// </summary>
    public const byte QmkRgblightColor = 0x83;
    public const byte DynamicKeymapReset = 0x06;
    public const byte EepromReset = 0x0A;
    public const byte DynamicKeymapMacroReset = 0x0B;
    public const byte MacroGetCount = 0x0C;
    public const byte MacroGetBufferSize = 0x0D;
    public const byte MacroGetBuffer = 0x0E;
    public const byte MacroSetBuffer = 0x0F;
    public const byte GetLayerCount = 0x11;
    public const byte KeymapGetBuffer = 0x12;
    public const byte KeymapSetBuffer = 0x13;

    // --- Vial prefix and sub-commands ---

    /// <summary>All Vial-specific commands are prefixed with this byte.</summary>
    public const byte VialPrefix = 0xFE;

    public const byte VialGetKeyboardId = 0x00;
    public const byte VialGetSize = 0x01;
    public const byte VialGetDefinition = 0x02;
    public const byte VialGetEncoder = 0x03;
    public const byte VialSetEncoder = 0x04;
    public const byte VialGetUnlockStatus = 0x05;
    public const byte VialUnlockStart = 0x06;
    public const byte VialUnlockPoll = 0x07;
    public const byte VialLock = 0x08;
    public const byte VialQmkSettingsQuery = 0x09;
    public const byte VialQmkSettingsGet = 0x0A;
    public const byte VialQmkSettingsSet = 0x0B;
    public const byte VialQmkSettingsReset = 0x0C;
    public const byte VialDynamicEntryOp = 0x0D;

    // Sub-ops for VialDynamicEntryOp (third byte of command).
    public const byte DynamicEntryGetNumberOfEntries = 0x00;
    public const byte DynamicEntryTapDanceGet = 0x01;
    public const byte DynamicEntryTapDanceSet = 0x02;
    public const byte DynamicEntryComboGet = 0x03;
    public const byte DynamicEntryComboSet = 0x04;

    // --- Svalboard custom sub-protocol (identifier 0xEE) ---
    // Reference: keybard-ng/pages/js/vial/sval.js
    // Confirmed against Svalboard Lightly firmware (sval proto version 3).

    /// <summary>Svalboard custom-protocol identifier. All sval commands are prefixed with this byte.</summary>
    public const byte SvalPrefix = 0xEE;

    /// <summary>Returns ASCII "sval" (4 bytes) followed by a u32 LE proto version.</summary>
    public const byte SvalGetProtoVersion = 0x01;

    /// <summary>Read per-layer stored color. Args: [layer]. Returns: [H, S, V].</summary>
    public const byte SvalLayerColorGet = 0x10;

    // --- QMK Settings IDs (from Vial QMK Settings protocol) ---
    // Reference: vial-gui qmk_settings.json, vial-qmk quantum/qmk_settings.h
    // Only non-bitfield settings are listed here. Bitfield settings (0x01 grave_esc,
    // 0x03 auto_shift flags, 0x08 legacy tap-hold, 0x15 magic) need per-bit UI.

    // Bitfield settings (packed booleans — each bit is a separate flag)
    public const ushort QmkSettingGraveEscOverride = 0x0001;
    public const ushort QmkSettingAutoShiftFlags = 0x0003;
    public const ushort QmkSettingMagic = 0x0015;

    // Combo
    public const ushort QmkSettingComboTerm = 0x0002;

    // Auto Shift
    public const ushort QmkSettingAutoShiftTimeout = 0x0004;

    // One Shot
    public const ushort QmkSettingOneShotTapToggle = 0x0005;
    public const ushort QmkSettingOneShotTimeout = 0x0006;

    // Tap-Hold
    public const ushort QmkSettingTappingTerm = 0x0007;

    // Mouse Keys
    public const ushort QmkSettingMouseKeyDelay = 0x0009;
    public const ushort QmkSettingMouseKeyInterval = 0x000A;
    public const ushort QmkSettingMouseKeyMoveDelta = 0x000B;
    public const ushort QmkSettingMouseKeyMaxSpeed = 0x000C;
    public const ushort QmkSettingMouseKeyTimeToMax = 0x000D;
    public const ushort QmkSettingMouseKeyWheelDelay = 0x000E;
    public const ushort QmkSettingMouseKeyWheelInterval = 0x000F;
    public const ushort QmkSettingMouseKeyWheelMaxSpeed = 0x0010;
    public const ushort QmkSettingMouseKeyWheelTimeToMax = 0x0011;

    // Tap-Hold (continued)
    public const ushort QmkSettingTapCodeDelay = 0x0012;
    public const ushort QmkSettingTapHoldCapsDelay = 0x0013;
    public const ushort QmkSettingTappingToggle = 0x0014;
    public const ushort QmkSettingPermissiveHold = 0x0016;
    public const ushort QmkSettingHoldOnOtherKeyPress = 0x0017;
    public const ushort QmkSettingRetroTapping = 0x0018;
    public const ushort QmkSettingQuickTapTerm = 0x0019;
    public const ushort QmkSettingChordalHold = 0x001A;
    public const ushort QmkSettingFlowTapTerm = 0x001B;

    // --- XZ magic bytes for definition payload ---

    public static readonly byte[] XzMagicBytes = [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00];

    // --- HID Usage Page / Usage for Vial keyboards ---

    public const ushort VialUsagePage = 0xFF60;
    public const ushort VialUsage1 = 0x61;
    public const ushort VialUsage2 = 0x62;
}
