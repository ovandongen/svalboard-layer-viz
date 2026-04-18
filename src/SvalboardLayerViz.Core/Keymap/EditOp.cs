namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// A single edit operation applied to a keymap. Carries both old and new values
/// to support undo/redo without re-reading the device.
/// </summary>
public abstract record EditOp;

/// <summary>
/// Sets one key in the keymap to a new keycode.
/// </summary>
public record SetKeyOp(int Layer, int Row, int Col, ushort OldCode, ushort NewCode) : EditOp;

/// <summary>
/// Changes a QMK firmware setting value.
/// </summary>
public record SetQmkSettingOp(ushort SettingId, ushort OldValue, ushort NewValue) : EditOp;

/// <summary>
/// Replaces the entire macro buffer. Stores old and new encoded byte arrays
/// for undo/redo. Cost is ~1KB per undo step (one buffer capacity).
/// </summary>
public record SetMacroBufferOp(byte[] OldBuffer, byte[] NewBuffer) : EditOp;

/// <summary>
/// Replaces a single combo entry by index. 10 bytes old/new for undo/redo.
/// </summary>
public record SetComboOp(int Index, byte[] OldBytes, byte[] NewBytes) : EditOp;

/// <summary>
/// Replaces a single tap-dance entry by index. 10 bytes old/new for undo/redo.
/// </summary>
public record SetTapDanceOp(int Index, byte[] OldBytes, byte[] NewBytes) : EditOp;
