using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Translates raw 16-bit keycodes to human-readable display labels.
///
/// Keycode encoding (from QMK/Vial):
/// - 0x0000         = KC_NO (no key)
/// - 0x0001         = KC_TRNS (transparent)
/// - 0x0004-0x00FF  = Basic keycodes (KC_A=0x04, etc.)
/// - 0x0100-0x1FFF  = Modifier + basic key combinations
/// - 0x2000-0x3FFF  = Mod-tap (MT)
/// - 0x4000-0x4FFF  = Layer-tap (LT)
/// - 0x5000-0x52DF  = Layer functions (LM, TO, MO, DF, TG, OSL, OSM, TT)
/// - 0x7E40-0x7FFF  = User/custom keycodes
///
/// Reference: keybard-ng/src/services/key.service.ts, keybard-ng/src/constants/keygen.ts
/// </summary>
public class KeycodeService
{
    // QMK keycode ranges — source of truth: keybard-ng/src/constants/keygen.ts
    private const ushort QK_MOD_TAP = 0x2000;
    private const ushort QK_MOD_TAP_MAX = 0x3FFF;
    private const ushort QK_LAYER_TAP = 0x4000;
    private const ushort QK_LAYER_TAP_MAX = 0x4FFF;
    private const ushort QK_LAYER_MOD = 0x5000;
    private const ushort QK_LAYER_MOD_MAX = 0x51FF;
    private const ushort QK_TO = 0x5200;      // Turn on layer
    private const ushort QK_TO_MAX = 0x521F;
    private const ushort QK_MO = 0x5220;      // Momentary layer
    private const ushort QK_MO_MAX = 0x523F;
    private const ushort QK_DF = 0x5240;      // Default layer
    private const ushort QK_DF_MAX = 0x525F;
    private const ushort QK_TG = 0x5260;      // Toggle layer
    private const ushort QK_TG_MAX = 0x527F;
    private const ushort QK_OSL = 0x5280;     // One-shot layer
    private const ushort QK_OSL_MAX = 0x529F;
    private const ushort QK_ONE_SHOT_MOD = 0x52A0; // One-shot modifier
    private const ushort QK_ONE_SHOT_MOD_MAX = 0x52BF;
    private const ushort QK_TT = 0x52C0;      // Tap-toggle
    private const ushort QK_TT_MAX = 0x52DF;
    private const ushort QK_KB = 0x7E00;       // Keyboard-specific custom keycodes
    private const ushort QK_KB_MAX = 0x7FFF;   // Covers both QK_KB and QK_USER ranges

    private IReadOnlyList<CustomKeycode>? _customKeycodes;
    private Dictionary<string, string>? _customKeyLabels;

    /// <summary>
    /// Sets custom keycodes from the device definition for resolution.
    /// Call before resolving keys.
    /// </summary>
    public void SetCustomKeycodes(IReadOnlyList<CustomKeycode> keycodes)
    {
        _customKeycodes = keycodes;
    }

    /// <summary>
    /// Sets user-defined labels for unknown keycodes from settings.
    /// Key = hex string (e.g. "0x5300"), Value = display label.
    /// </summary>
    public void SetCustomKeyLabels(Dictionary<string, string>? labels)
    {
        _customKeyLabels = labels;
    }

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

        // User-defined custom label takes priority over all other resolution
        var hexKey = $"0x{keycode:X4}";
        if (_customKeyLabels is not null && _customKeyLabels.TryGetValue(hexKey, out var userLabel))
            return new KeycodeInfo(userLabel);

        // Basic keycodes
        if (keycode <= 0x00FF)
        {
            var label = BasicKeycodes.GetValueOrDefault(keycode, $"0x{keycode:X4}");
            var shifted = ShiftedSymbols.GetValueOrDefault(keycode);
            return new KeycodeInfo(label, ShiftedLabel: shifted);
        }

        // Mod-tap: hold = modifier, tap = keycode (0x2000-0x3FFF)
        if (keycode is >= QK_MOD_TAP and <= QK_MOD_TAP_MAX)
        {
            var mods = (keycode >> 8) & 0x1F;
            var baseKey = (ushort)(keycode & 0x00FF);
            var baseLabel = BasicKeycodes.GetValueOrDefault(baseKey, $"0x{baseKey:X2}");
            var modLabel = FormatModifiers(mods);
            return new KeycodeInfo(baseLabel, SecondaryLabel: $"MT({modLabel})");
        }

        // Layer-tap: hold = layer, tap = keycode (0x4000-0x4FFF)
        if (keycode is >= QK_LAYER_TAP and <= QK_LAYER_TAP_MAX)
        {
            var layer = (keycode >> 8) & 0x0F;
            var baseKey = (ushort)(keycode & 0x00FF);
            if (baseKey == 0x00) // LT(layer, KC_NO) — hold-only layer switch
                return new KeycodeInfo($"LT({layer})", IsLayerSwitch: true, TargetLayer: layer);
            var baseLabel = BasicKeycodes.GetValueOrDefault(baseKey, $"0x{baseKey:X2}");
            return new KeycodeInfo(baseLabel, SecondaryLabel: $"LT({layer})", IsLayerSwitch: true, TargetLayer: layer);
        }

        // Layer-mod: activate layer with modifier (0x5000-0x51FF)
        if (keycode is >= QK_LAYER_MOD and <= QK_LAYER_MOD_MAX)
        {
            var layer = (keycode >> 4) & 0x0F;
            var mods = keycode & 0x0F;
            var modLabel = FormatModifiers(mods);
            return new KeycodeInfo($"LM({layer})", SecondaryLabel: modLabel, IsLayerSwitch: true, TargetLayer: layer);
        }

        // Layer functions (ordered by range: 0x5200 → 0x52DF)
        if (keycode is >= QK_TO and <= QK_TO_MAX)
            return new KeycodeInfo($"TO({keycode - QK_TO})", IsLayerSwitch: true, TargetLayer: keycode - QK_TO);

        if (keycode is >= QK_MO and <= QK_MO_MAX)
            return new KeycodeInfo($"MO({keycode - QK_MO})", IsLayerSwitch: true, TargetLayer: keycode - QK_MO);

        if (keycode is >= QK_DF and <= QK_DF_MAX)
            return new KeycodeInfo($"DF({keycode - QK_DF})", IsLayerSwitch: true, TargetLayer: keycode - QK_DF);

        if (keycode is >= QK_TG and <= QK_TG_MAX)
            return new KeycodeInfo($"TG({keycode - QK_TG})", IsLayerSwitch: true, TargetLayer: keycode - QK_TG);

        if (keycode is >= QK_OSL and <= QK_OSL_MAX)
            return new KeycodeInfo($"OSL({keycode - QK_OSL})", IsLayerSwitch: true, TargetLayer: keycode - QK_OSL);

        if (keycode is >= QK_ONE_SHOT_MOD and <= QK_ONE_SHOT_MOD_MAX)
        {
            var mods = keycode - QK_ONE_SHOT_MOD;
            return new KeycodeInfo($"OSM({FormatModifiers(mods)})");
        }

        if (keycode is >= QK_TT and <= QK_TT_MAX)
            return new KeycodeInfo($"TT({keycode - QK_TT})", IsLayerSwitch: true, TargetLayer: keycode - QK_TT);

        // Custom/keyboard keycodes (0x7E00-0x7FFF)
        if (keycode is >= QK_KB and <= QK_KB_MAX)
        {
            var index = keycode - QK_KB;
            if (_customKeycodes is not null && index < _customKeycodes.Count)
            {
                var custom = _customKeycodes[index];
                var label = !string.IsNullOrEmpty(custom.ShortName) ? custom.ShortName : custom.Name;
                return new KeycodeInfo(label);
            }
            return new KeycodeInfo($"KB{index}");
        }

        // Modifier + key combinations (0x0100-0x1FFF)
        if ((keycode & 0xFF00) != 0 && (keycode & 0x00FF) != 0)
        {
            var mods = (keycode >> 8) & 0x1F;
            var baseKey = (ushort)(keycode & 0x00FF);

            // Shift-only + key with a known symbol → show the symbol directly
            if (mods == 0x02 && ShiftedSymbols.TryGetValue(baseKey, out var symbol))
                return new KeycodeInfo(symbol);

            var baseLabel = BasicKeycodes.GetValueOrDefault(baseKey, $"0x{baseKey:X2}");
            var modLabel = FormatModifiers(mods);
            return new KeycodeInfo(baseLabel, SecondaryLabel: modLabel);
        }

        // Fallback: show hex
        return new KeycodeInfo($"0x{keycode:X4}", IsUnknown: true);
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
    /// US ANSI layout: Shift + base key → resulting symbol.
    /// Used to display "{" instead of "Shift [", etc.
    /// </summary>
    private static readonly Dictionary<ushort, string> ShiftedSymbols = new()
    {
        [0x1E] = "!", [0x1F] = "@", [0x20] = "#", [0x21] = "$",
        [0x22] = "%", [0x23] = "^", [0x24] = "&", [0x25] = "*",
        [0x26] = "(", [0x27] = ")",
        [0x2D] = "_", [0x2E] = "+",
        [0x2F] = "{", [0x30] = "}",
        [0x31] = "|", [0x33] = ":", [0x34] = "\"", [0x35] = "~",
        [0x36] = "<", [0x37] = ">", [0x38] = "?",
    };

    /// <summary>
    /// QMK HID keycodes. Reference: https://docs.qmk.fm/keycodes
    /// </summary>
    private static readonly Dictionary<ushort, string> BasicKeycodes = new()
    {
        // Letters
        [0x04] = "A", [0x05] = "B", [0x06] = "C", [0x07] = "D",
        [0x08] = "E", [0x09] = "F", [0x0A] = "G", [0x0B] = "H",
        [0x0C] = "I", [0x0D] = "J", [0x0E] = "K", [0x0F] = "L",
        [0x10] = "M", [0x11] = "N", [0x12] = "O", [0x13] = "P",
        [0x14] = "Q", [0x15] = "R", [0x16] = "S", [0x17] = "T",
        [0x18] = "U", [0x19] = "V", [0x1A] = "W", [0x1B] = "X",
        [0x1C] = "Y", [0x1D] = "Z",

        // Numbers
        [0x1E] = "1", [0x1F] = "2", [0x20] = "3", [0x21] = "4",
        [0x22] = "5", [0x23] = "6", [0x24] = "7", [0x25] = "8",
        [0x26] = "9", [0x27] = "0",

        // Editing & whitespace
        [0x28] = "Enter", [0x29] = "Esc", [0x2A] = "Bksp", [0x2B] = "Tab",
        [0x2C] = "Space", [0x2D] = "-", [0x2E] = "=", [0x2F] = "[",
        [0x30] = "]", [0x31] = "\\", [0x32] = "#", [0x33] = ";",
        [0x34] = "'", [0x35] = "`", [0x36] = ",", [0x37] = ".",
        [0x38] = "/",

        // Lock keys & function keys
        [0x39] = "Caps", [0x3A] = "F1", [0x3B] = "F2", [0x3C] = "F3",
        [0x3D] = "F4", [0x3E] = "F5", [0x3F] = "F6", [0x40] = "F7",
        [0x41] = "F8", [0x42] = "F9", [0x43] = "F10", [0x44] = "F11",
        [0x45] = "F12",

        // Navigation & editing
        [0x46] = "PrtSc", [0x47] = "ScrLk", [0x48] = "Pause",
        [0x49] = "Ins", [0x4A] = "Home", [0x4B] = "PgUp",
        [0x4C] = "Del", [0x4D] = "End", [0x4E] = "PgDn",
        [0x4F] = "Right", [0x50] = "Left", [0x51] = "Down", [0x52] = "Up",

        // Numpad
        [0x53] = "NumLk", [0x54] = "KP /", [0x55] = "KP *", [0x56] = "KP -",
        [0x57] = "KP +", [0x58] = "KP Ent", [0x59] = "KP 1", [0x5A] = "KP 2",
        [0x5B] = "KP 3", [0x5C] = "KP 4", [0x5D] = "KP 5", [0x5E] = "KP 6",
        [0x5F] = "KP 7", [0x60] = "KP 8", [0x61] = "KP 9", [0x62] = "KP 0",
        [0x63] = "KP .",

        // Non-US & special
        [0x64] = "NUBS", [0x65] = "App",

        // Mouse keys
        [0xCD] = "Ms\u2191", [0xCE] = "Ms\u2193", [0xCF] = "Ms\u2190", [0xD0] = "Ms\u2192",
        [0xD1] = "Btn1", [0xD2] = "Btn2", [0xD3] = "Btn3",
        [0xD4] = "Btn4", [0xD5] = "Btn5",
        [0xD6] = "Wh\u2191", [0xD7] = "Wh\u2193", [0xD8] = "Wh\u2190", [0xD9] = "Wh\u2192",
        [0xDA] = "Acl0", [0xDB] = "Acl1", [0xDC] = "Acl2",

        // F13-F24
        [0x68] = "F13", [0x69] = "F14", [0x6A] = "F15", [0x6B] = "F16",
        [0x6C] = "F17", [0x6D] = "F18", [0x6E] = "F19", [0x6F] = "F20",
        [0x70] = "F21", [0x71] = "F22", [0x72] = "F23", [0x73] = "F24",

        // Media (HID keyboard page)
        [0x7F] = "Mute", [0x80] = "Vol+", [0x81] = "Vol-",

        // Modifiers
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
    int? TargetLayer = null,
    bool IsUnknown = false,
    string? ShiftedLabel = null
);
