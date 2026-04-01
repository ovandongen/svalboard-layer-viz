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
}
