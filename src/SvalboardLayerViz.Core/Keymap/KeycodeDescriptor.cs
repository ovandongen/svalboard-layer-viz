namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Structured representation of a QMK keycode. Used by the encoder/decoder
/// to convert between typed descriptors and raw 16-bit values, and by the
/// key picker to emit keycodes without fragile string parsing.
/// </summary>
public abstract record KeycodeDescriptor;

/// <summary>No key assigned (0x0000).</summary>
public record NoKeycode : KeycodeDescriptor;

/// <summary>Transparent — inherits from layer below (0x0001).</summary>
public record TransparentKeycode : KeycodeDescriptor;

/// <summary>Basic keycode (0x0004–0x00FF): letters, numbers, nav, mouse, media, modifiers.</summary>
public record BasicKeycode(ushort BaseCode) : KeycodeDescriptor;

/// <summary>Modifier + basic key combination (0x0100–0x1FFF).</summary>
public record ModifiedKeycode(ModFlags Mods, ushort BaseCode) : KeycodeDescriptor;

/// <summary>Mod-tap: hold = modifier, tap = basic key (0x2000–0x3FFF).</summary>
public record ModTapKeycode(ModFlags Mods, ushort BaseCode) : KeycodeDescriptor;

/// <summary>Layer-tap: hold = activate layer, tap = basic key (0x4000–0x4FFF).</summary>
public record LayerTapKeycode(int Layer, ushort BaseCode) : KeycodeDescriptor;

/// <summary>Layer-mod: activate layer with modifier held (0x5000–0x51FF).</summary>
public record LayerModKeycode(int Layer, ModFlags Mods) : KeycodeDescriptor;

/// <summary>Layer function: MO/TG/TO/DF/TT/OSL (0x5200–0x52BF).</summary>
public record LayerFunctionKeycode(LayerFunctionKind Kind, int Layer) : KeycodeDescriptor;

/// <summary>One-shot modifier (0x52A0–0x52BF).</summary>
public record OneShotModKeycode(ModFlags Mods) : KeycodeDescriptor;

/// <summary>Macro keycode (0x7700–0x77FF): triggers macro slot N.</summary>
public record MacroKeycode(int MacroIndex) : KeycodeDescriptor;

/// <summary>Tap-dance keycode (0x5700–0x57FF): triggers tap-dance slot N.</summary>
public record TapDanceKeycode(int Index) : KeycodeDescriptor;

/// <summary>Custom/keyboard-specific keycode (0x7E00–0x7FFF).</summary>
public record CustomKeycodeDescriptor(int Index) : KeycodeDescriptor;

/// <summary>
/// QMK named keycode that doesn't fit any structured range — e.g.
/// QK_REPEAT_KEY (0x7C79), QK_LAYER_LOCK (0x7C7B). Looked up by full
/// 16-bit code in <see cref="KeycodeCatalog.NamedSpecialKeycodes"/>.
/// </summary>
public record SpecialKeycode(ushort Code) : KeycodeDescriptor;

/// <summary>Escape hatch for unknown or unrecognized keycodes.</summary>
public record RawKeycode(ushort Value) : KeycodeDescriptor;

/// <summary>
/// QMK modifier flags used in mod-tap, layer-mod, one-shot-mod, and modifier combos.
/// Matches the 5-bit encoding: bit 0 = Ctrl, bit 1 = Shift, bit 2 = Alt, bit 3 = GUI.
/// Bit 4 indicates right-side modifiers when set.
/// </summary>
[Flags]
public enum ModFlags
{
    None  = 0,
    Ctrl  = 0x01,
    Shift = 0x02,
    Alt   = 0x04,
    Gui   = 0x08,
    Right = 0x10,
}

/// <summary>
/// Which layer function variant a <see cref="LayerFunctionKeycode"/> represents.
/// </summary>
public enum LayerFunctionKind
{
    /// <summary>TO — turn on layer (0x5200).</summary>
    TO,
    /// <summary>MO — momentary layer (0x5220).</summary>
    MO,
    /// <summary>DF — default layer (0x5240).</summary>
    DF,
    /// <summary>TG — toggle layer (0x5260).</summary>
    TG,
    /// <summary>OSL — one-shot layer (0x5280).</summary>
    OSL,
    /// <summary>TT — tap-toggle layer (0x52C0).</summary>
    TT,
}
