namespace SvalboardLayerViz.Core.Macros;

/// <summary>
/// A single macro: an ordered sequence of actions assigned to a macro slot.
/// </summary>
public record Macro(int Index, IReadOnlyList<MacroAction> Actions);
