namespace SvalboardLayerViz.Core.Macros;

/// <summary>
/// A single action within a QMK macro. Discriminated union — one subtype per
/// action kind the firmware supports.
/// </summary>
public abstract record MacroAction;

/// <summary>Tap (press + release) a keycode. Encoded as SS_TAP_CODE (0x01) + keycode.</summary>
public record MacroTapAction(byte Keycode) : MacroAction;

/// <summary>Press (hold down) a keycode. Encoded as SS_DOWN_CODE (0x02) + keycode.</summary>
public record MacroDownAction(byte Keycode) : MacroAction;

/// <summary>Release a keycode. Encoded as SS_UP_CODE (0x03) + keycode.</summary>
public record MacroUpAction(byte Keycode) : MacroAction;

/// <summary>Delay in milliseconds. Encoded as SS_DELAY_CODE (0x04) + ASCII digits.</summary>
public record MacroDelayAction(int DelayMs) : MacroAction;

/// <summary>
/// Tap a keycode while holding modifier(s). Encoded as SS_MOD_TAP (0x05) + keycode + mods.
/// Mods byte: bit 0=LCtrl, 1=LShift, 2=LAlt, 3=LGui, 4=RCtrl, 5=RShift, 6=RAlt, 7=RGui.
/// </summary>
public record MacroModTapAction(byte Keycode, byte Mods) : MacroAction;

/// <summary>Type a string of printable ASCII characters.</summary>
/// <remarks>Each char must be in the range 0x20–0xFF. Chars below 0x20 collide with
/// the macro terminator (0x00) and SS_QMK_PREFIX (0x01); chars above 0xFF truncate
/// silently to a low byte on encode. Validated at <see cref="MacroCodec.Encode(IReadOnlyList{Macro}, int)"/>.</remarks>
public record MacroTextAction(string Text) : MacroAction;
