namespace SvalboardLayerViz.Core.Macros;

/// <summary>
/// The complete decoded macro buffer from the device. Contains all macro slots,
/// total buffer capacity, and current usage in bytes.
/// </summary>
public record MacroBuffer(IReadOnlyList<Macro> Macros, int BufferCapacity, int UsedBytes);
