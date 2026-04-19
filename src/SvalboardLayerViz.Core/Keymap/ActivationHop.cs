using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// A single step in a layer's activation path: an activator key on
/// <paramref name="SourceLayer"/> that targets <paramref name="TargetLayer"/>.
/// </summary>
public readonly record struct ActivationHop(
    int SourceLayer,
    int Row,
    int Col,
    int TargetLayer,
    LayerSwitchType SwitchType);
