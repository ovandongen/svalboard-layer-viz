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
}
