using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// Groups keys belonging to one finger or thumb cluster.
/// Wraps a PositionedCluster with UI concerns (KeyViewModels with cluster-relative coordinates).
/// </summary>
public class KeyClusterViewModel
{
    public string Name { get; }
    public IReadOnlyList<KeyViewModel> Keys { get; }
    public bool IsRightHand { get; }

    // Named accessors for creative thumb-cluster layout bindings.
    // Indexer bindings ({Binding Keys[N]}) can fail to re-resolve cleanly when
    // the cluster VM is replaced after a save — named paths are more reliable
    // with Avalonia's compiled bindings.
    public KeyViewModel? Thumb0 => Keys.Count > 0 ? Keys[0] : null;
    public KeyViewModel? Thumb1 => Keys.Count > 1 ? Keys[1] : null;
    public KeyViewModel? Thumb2 => Keys.Count > 2 ? Keys[2] : null;
    public KeyViewModel? Thumb3 => Keys.Count > 3 ? Keys[3] : null;
    public KeyViewModel? Thumb4 => Keys.Count > 4 ? Keys[4] : null;
    public KeyViewModel? Thumb5 => Keys.Count > 5 ? Keys[5] : null;

    /// <summary>Cluster position within the hand (hand-relative, pixels).</summary>
    public double Left { get; }
    public double Top { get; }
    public double Width { get; }
    public double Height { get; }

    public KeyClusterViewModel(
        PositionedCluster cluster,
        Layer layer,
        double handOriginPx,
        LayerColorPalette palette,
        bool isRightHand = false,
        Action<KeyViewModel>? setLabelRequested = null)
    {
        Name = cluster.Name;
        IsRightHand = isRightHand;

        Left = cluster.Left - handOriginPx;
        Top = cluster.Top;
        Width = cluster.Width;
        Height = cluster.Height;

        Keys = cluster.Keys.Select(pk => new KeyViewModel(pk, layer,
            cluster.Left, cluster.Top, palette, setLabelRequested)).ToList();
    }
}
