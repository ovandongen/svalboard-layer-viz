using HidSharp;

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
    /// Returns the value as u16, or null if the setting is not available or the device
    /// doesn't support QMK settings.
    /// </summary>
    ushort? GetQmkSetting(ushort settingId);

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
}
