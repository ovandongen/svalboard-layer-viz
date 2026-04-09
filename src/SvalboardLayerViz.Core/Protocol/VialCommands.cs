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
    public const byte MacroGetCount = 0x0C;
    public const byte MacroGetBufferSize = 0x0D;
    public const byte MacroGetBuffer = 0x0E;
    public const byte MacroSetBuffer = 0x0F;
    public const byte GetLayerCount = 0x11;
    public const byte KeymapGetBuffer = 0x12;

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

    /// <summary>
    /// QMK setting ID for TAPPING_TERM (u16, milliseconds).
    /// Confirmed on Svalboard firmware via probe: setting 0x0007 returns tapping term.
    /// </summary>
    public const ushort QmkSettingTappingTerm = 0x0007;

    // --- XZ magic bytes for definition payload ---

    public static readonly byte[] XzMagicBytes = [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00];

    // --- HID Usage Page / Usage for Vial keyboards ---

    public const ushort VialUsagePage = 0xFF60;
    public const ushort VialUsage1 = 0x61;
    public const ushort VialUsage2 = 0x62;
}
