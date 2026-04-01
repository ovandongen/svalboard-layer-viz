namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Translates raw 16-bit keycodes to human-readable display labels.
///
/// Keycode encoding (from QMK/Vial):
/// - 0x0000         = KC_NO (no key)
/// - 0x0001         = KC_TRNS (transparent)
/// - 0x0004-0x00FF  = Basic keycodes (KC_A=0x04, etc.)
/// - Upper byte as modifier mask: (mods &lt;&lt; 8) | keycode
/// - Layer keys: Various ranges for MO(), TG(), TO(), etc.
///
/// Reference: keybard-ng/src/services/key.service.ts, keybard-ng/src/constants/keygen.ts
/// </summary>
public class KeycodeService
{
    // QMK keycode ranges for layer functions
    private const ushort QK_MO = 0x5100;     // Momentary layer
    private const ushort QK_MO_MAX = 0x51FF;
    private const ushort QK_DF = 0x5200;     // Default layer
    private const ushort QK_DF_MAX = 0x52FF;
    private const ushort QK_TG = 0x5300;     // Toggle layer
    private const ushort QK_TG_MAX = 0x53FF;
    private const ushort QK_TO = 0x5400;     // Turn on layer
    private const ushort QK_TO_MAX = 0x54FF;
    private const ushort QK_TT = 0x5800;     // Tap-toggle
    private const ushort QK_TT_MAX = 0x58FF;
    private const ushort QK_OSL = 0x5400;    // One-shot layer
    private const ushort QK_OSL_MAX = 0x54FF;

    /// <summary>
    /// Converts a raw keycode to a display label.
    /// </summary>
    public KeycodeInfo Resolve(ushort keycode)
    {
        // No key
        if (keycode == 0x0000)
            return new KeycodeInfo("", IsEmpty: true);

        // Transparent
        if (keycode == 0x0001)
            return new KeycodeInfo("___", IsTransparent: true);

        // Basic keycodes
        if (keycode <= 0x00FF)
        {
            var label = BasicKeycodes.GetValueOrDefault(keycode, $"0x{keycode:X4}");
            return new KeycodeInfo(label);
        }

        // Layer functions
        if (keycode is >= QK_MO and <= QK_MO_MAX)
            return new KeycodeInfo($"MO({keycode - QK_MO})", IsLayerSwitch: true, TargetLayer: keycode - QK_MO);

        if (keycode is >= QK_TG and <= QK_TG_MAX)
            return new KeycodeInfo($"TG({keycode - QK_TG})", IsLayerSwitch: true, TargetLayer: keycode - QK_TG);

        if (keycode is >= QK_DF and <= QK_DF_MAX)
            return new KeycodeInfo($"DF({keycode - QK_DF})", IsLayerSwitch: true, TargetLayer: keycode - QK_DF);

        if (keycode is >= QK_TO and <= QK_TO_MAX)
            return new KeycodeInfo($"TO({keycode - QK_TO})", IsLayerSwitch: true, TargetLayer: keycode - QK_TO);

        if (keycode is >= QK_TT and <= QK_TT_MAX)
            return new KeycodeInfo($"TT({keycode - QK_TT})", IsLayerSwitch: true, TargetLayer: keycode - QK_TT);

        // Modifier + key combinations
        if ((keycode & 0xFF00) != 0 && (keycode & 0x00FF) != 0)
        {
            var mods = (keycode >> 8) & 0x1F;
            var baseKey = (ushort)(keycode & 0x00FF);
            var baseLabel = BasicKeycodes.GetValueOrDefault(baseKey, $"0x{baseKey:X2}");
            var modLabel = FormatModifiers(mods);
            return new KeycodeInfo(baseLabel, SecondaryLabel: modLabel);
        }

        // Fallback: show hex
        return new KeycodeInfo($"0x{keycode:X4}");
    }

    private static string FormatModifiers(int mods)
    {
        var parts = new List<string>();
        if ((mods & 0x01) != 0) parts.Add("Ctrl");
        if ((mods & 0x02) != 0) parts.Add("Shift");
        if ((mods & 0x04) != 0) parts.Add("Alt");
        if ((mods & 0x08) != 0) parts.Add("GUI");
        return string.Join("+", parts);
    }

    /// <summary>
    /// Basic QMK keycodes (subset — expand as needed).
    /// Full list: https://docs.qmk.fm/keycodes
    /// </summary>
    private static readonly Dictionary<ushort, string> BasicKeycodes = new()
    {
        [0x04] = "A", [0x05] = "B", [0x06] = "C", [0x07] = "D",
        [0x08] = "E", [0x09] = "F", [0x0A] = "G", [0x0B] = "H",
        [0x0C] = "I", [0x0D] = "J", [0x0E] = "K", [0x0F] = "L",
        [0x10] = "M", [0x11] = "N", [0x12] = "O", [0x13] = "P",
        [0x14] = "Q", [0x15] = "R", [0x16] = "S", [0x17] = "T",
        [0x18] = "U", [0x19] = "V", [0x1A] = "W", [0x1B] = "X",
        [0x1C] = "Y", [0x1D] = "Z",

        [0x1E] = "1", [0x1F] = "2", [0x20] = "3", [0x21] = "4",
        [0x22] = "5", [0x23] = "6", [0x24] = "7", [0x25] = "8",
        [0x26] = "9", [0x27] = "0",

        [0x28] = "Enter", [0x29] = "Esc", [0x2A] = "Bksp", [0x2B] = "Tab",
        [0x2C] = "Space", [0x2D] = "-", [0x2E] = "=", [0x2F] = "[",
        [0x30] = "]", [0x31] = "\\", [0x33] = ";", [0x34] = "'",
        [0x35] = "`", [0x36] = ",", [0x37] = ".", [0x38] = "/",

        [0x39] = "Caps", [0x3A] = "F1", [0x3B] = "F2", [0x3C] = "F3",
        [0x3D] = "F4", [0x3E] = "F5", [0x3F] = "F6", [0x40] = "F7",
        [0x41] = "F8", [0x42] = "F9", [0x43] = "F10", [0x44] = "F11",
        [0x45] = "F12",

        [0x46] = "PrtSc", [0x47] = "ScrLk", [0x48] = "Pause",
        [0x49] = "Ins", [0x4A] = "Home", [0x4B] = "PgUp",
        [0x4C] = "Del", [0x4D] = "End", [0x4E] = "PgDn",
        [0x4F] = "Right", [0x50] = "Left", [0x51] = "Down", [0x52] = "Up",

        [0xE0] = "LCtrl", [0xE1] = "LShift", [0xE2] = "LAlt", [0xE3] = "LGUI",
        [0xE4] = "RCtrl", [0xE5] = "RShift", [0xE6] = "RAlt", [0xE7] = "RGUI",
    };
}

/// <summary>
/// Resolved information about a keycode, ready for display.
/// </summary>
public record KeycodeInfo(
    string Label,
    string? SecondaryLabel = null,
    bool IsTransparent = false,
    bool IsEmpty = false,
    bool IsLayerSwitch = false,
    int? TargetLayer = null
);
