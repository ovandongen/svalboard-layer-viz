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
}
