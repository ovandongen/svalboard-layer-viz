namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// A write command to send to the device. Built from pending edits by
/// <see cref="KeymapEditSession.BuildDeviceWrites"/>.
/// </summary>
public abstract record DeviceWrite;

/// <summary>
/// Sets a single keycode on the device via VIA command 0x05.
/// </summary>
public record SetKeycodeWrite(int Layer, int Row, int Col, ushort Keycode) : DeviceWrite;

/// <summary>
/// Writes a chunk of raw keymap buffer data via VIA command 0x13.
/// Used for bulk transfers when many keys change.
/// </summary>
public record KeymapBufferWrite(int Offset, byte[] Data) : DeviceWrite;

/// <summary>
/// Sets a QMK setting on the device via Vial command 0xFE 0x0B.
/// </summary>
/// <param name="Width">Byte width for the setting (1 = u8, 2 = u16).</param>
public record SetQmkSettingWrite(ushort SettingId, ushort Value, byte Width = 2) : DeviceWrite;

/// <summary>
/// Writes the full macro buffer to the device. The <see cref="SaveFlowExecutor"/>
/// chunks this into protocol-sized segments via <c>MacroSetBuffer(offset, chunk)</c>.
/// </summary>
public record MacroBufferWrite(byte[] EncodedBuffer) : DeviceWrite;

/// <summary>
/// Writes a single combo entry via Vial sub-command 0xFE 0x0D 0x04.
/// </summary>
public record ComboEntryWrite(int Index, byte[] Entry) : DeviceWrite;

/// <summary>
/// Writes a single tap-dance entry via Vial sub-command 0xFE 0x0D 0x02.
/// </summary>
public record TapDanceEntryWrite(int Index, byte[] Entry) : DeviceWrite;
