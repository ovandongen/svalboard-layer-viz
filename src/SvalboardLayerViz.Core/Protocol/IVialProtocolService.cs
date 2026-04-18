using HidSharp;
using SvalboardLayerViz.Core.Dynamic;

namespace SvalboardLayerViz.Core.Protocol;

/// <summary>
/// Interface for Vial protocol communication, enabling testability.
/// </summary>
public interface IVialProtocolService : IDisposable
{
    void Connect(HidDevice device);
    int GetLayerCount();
    ulong GetKeyboardId();
    int GetDefinitionSize();
    byte[] GetDefinition();
    ushort[,,] GetKeymapBuffer(int layers, int rows, int cols);

    /// <summary>
    /// Reads the current switch matrix state (which physical keys are pressed).
    /// Standard VIA command id_switch_matrix_state.
    /// Returns bool[rows, cols] where true = key pressed.
    /// </summary>
    bool[,] GetSwitchMatrixState(int rows, int cols);

    /// <summary>
    /// Reads a QMK setting value from the device via Vial's QMK Settings protocol.
    /// <paramref name="width"/> is the byte width reported by the firmware query (1 or 2).
    /// Returns the value as u16, or null if the setting is not available.
    /// </summary>
    ushort? GetQmkSetting(ushort settingId, byte width = 2);

    /// <summary>
    /// Probes the Svalboard custom sub-protocol (0xEE). Returns the protocol version
    /// if the device responds with the "sval" handshake, otherwise null.
    /// </summary>
    uint? GetSvalProtoVersion();

    /// <summary>
    /// Reads the stored HSV color for a single layer via the Svalboard custom
    /// sub-protocol (0xEE, 0x10). Returns null if the device doesn't support it.
    /// </summary>
    (byte H, byte S, byte V)? GetLayerColor(int layer);

    /// <summary>
    /// Reads the currently displayed rgblight hue+sat via standard VIA lighting
    /// command (0x08, 0x83). Used as a live indicator of the active layer on
    /// Svalboards where the layer indicator LED is driven through rgblight.
    /// Returns null if the firmware doesn't implement this VIA command.
    /// </summary>
    (byte H, byte S)? GetCurrentLedHueSat();

    // --- QMK Settings commands (Phase 4b) ---

    /// <summary>
    /// Discovers which QMK setting IDs the firmware supports via paginated query.
    /// Response contains u16 QSIDs (2 bytes each), terminated by 0xFFFF fill bytes.
    /// Returns empty list if the firmware doesn't support QMK settings.
    /// </summary>
    IReadOnlyList<ushort> GetQmkSettingsList();

    /// <summary>
    /// Sets a QMK setting value on the device.
    /// <paramref name="width"/> controls how many value bytes are sent (1 or 2).
    /// Throws on failure (non-zero status byte in response).
    /// </summary>
    void SetQmkSetting(ushort settingId, ushort value, byte width = 2);

    /// <summary>
    /// Resets all QMK settings to firmware defaults.
    /// Sends [0xFE, 0x0C].
    /// </summary>
    void ResetQmkSettings();

    // --- Write commands (Phase 4) ---

    /// <summary>
    /// Sets a single keycode on the device.
    /// Sends VIA command 0x05: [layer, row, col, kc_hi, kc_lo].
    /// </summary>
    void SetKeycode(int layer, int row, int col, ushort keycode);

    /// <summary>
    /// Writes a chunk of keymap data to the device buffer.
    /// Sends VIA command 0x13: [offset_be16, size, ...payload].
    /// Chunks must be at most <see cref="VialCommands.PayloadSize"/> bytes.
    /// </summary>
    void KeymapSetBuffer(int offset, byte[] data);

    /// <summary>
    /// Resets the dynamic keymap to firmware defaults (factory reset of layers).
    /// Sends VIA command 0x06.
    /// </summary>
    void DynamicKeymapReset();

    /// <summary>
    /// Resets the full EEPROM to defaults (all settings, not just keymap).
    /// Sends VIA command 0x0A. Use sparingly — surface behind a confirmation dialog.
    /// </summary>
    void EepromReset();

    // --- Macro commands (Phase 4c) ---

    /// <summary>
    /// Returns the number of macro slots configured on the device.
    /// Sends VIA command 0x0C.
    /// </summary>
    int GetMacroCount();

    /// <summary>
    /// Returns the total macro buffer size in bytes.
    /// Sends VIA command 0x0D.
    /// </summary>
    int GetMacroBufferSize();

    /// <summary>
    /// Reads the full macro buffer as raw bytes via chunked reads.
    /// Sends VIA command 0x0E with offset/size parameters.
    /// The caller is responsible for decoding via <see cref="Core.Macros.MacroCodec"/>.
    /// </summary>
    byte[] GetMacroBuffer(int bufferSize);

    /// <summary>
    /// Writes a chunk of macro data to the device buffer.
    /// Sends VIA command 0x0F: [offset_be16, size, ...payload].
    /// Chunks must be at most <see cref="VialCommands.PayloadSize"/> bytes.
    /// </summary>
    void MacroSetBuffer(int offset, byte[] data);

    /// <summary>
    /// Resets all macros to firmware defaults.
    /// Sends VIA command 0x0B.
    /// </summary>
    void DynamicKeymapMacroReset();

    // --- Dynamic entry commands (Phase 4d — combos + tap-dance) ---

    /// <summary>
    /// Queries the number of dynamic entries the firmware supports for each table.
    /// Sends [0xFE, 0x0D, 0x00]. Response bytes 0..3 = [td, combo, keyOverride, altRepeat] counts.
    /// Returns all-zeros if the firmware doesn't implement dynamic entries.
    /// </summary>
    DynamicEntryCounts GetDynamicEntryCounts();

    /// <summary>
    /// Reads a single combo entry (10 bytes) by index.
    /// Sends [0xFE, 0x0D, 0x03, index]. Response: [status, in0_lo, in0_hi, ...].
    /// </summary>
    byte[] GetComboEntry(int index);

    /// <summary>
    /// Writes a single combo entry.
    /// Sends [0xFE, 0x0D, 0x04, index, ...10 bytes]. Throws on non-zero status.
    /// </summary>
    void SetComboEntry(int index, byte[] entry);

    /// <summary>
    /// Reads a single tap-dance entry (10 bytes) by index.
    /// Sends [0xFE, 0x0D, 0x01, index]. Response: [status, on_tap_lo, on_tap_hi, ...].
    /// </summary>
    byte[] GetTapDanceEntry(int index);

    /// <summary>
    /// Writes a single tap-dance entry.
    /// Sends [0xFE, 0x0D, 0x02, index, ...10 bytes]. Throws on non-zero status.
    /// </summary>
    void SetTapDanceEntry(int index, byte[] entry);

    // --- Unlock commands (Phase 4) ---

    /// <summary>
    /// Queries the current Vial unlock status.
    /// Sends [0xFE, 0x05]. Returns unlock state and keys the user must hold.
    /// </summary>
    UnlockStatus GetUnlockStatus();

    /// <summary>
    /// Begins the Vial unlock sequence. The user must then hold the indicated
    /// keys while the host polls with <see cref="UnlockPoll"/>.
    /// Sends [0xFE, 0x06].
    /// </summary>
    void UnlockStart();

    /// <summary>
    /// Polls the unlock sequence progress. Call at ~4-5 Hz while the unlock
    /// dialog is open. Returns true when the keyboard is fully unlocked.
    /// Sends [0xFE, 0x07].
    /// </summary>
    bool UnlockPoll();

    /// <summary>
    /// Re-locks the keyboard after editing is complete.
    /// Sends [0xFE, 0x08].
    /// </summary>
    void Lock();
}
