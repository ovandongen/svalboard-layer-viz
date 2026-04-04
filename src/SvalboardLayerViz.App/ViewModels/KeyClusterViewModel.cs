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

    /// <summary>Cluster position within the hand (hand-relative, pixels).</summary>
    public double Left { get; }
    public double Top { get; }
    public double Width { get; }
    public double Height { get; }

    public KeyClusterViewModel(
        PositionedCluster cluster,
        Layer layer,
        double handOriginPx,
        bool isRightHand = false,
        int totalLayers = 8,
        Dictionary<int, string>? userLayerColors = null,
        Action<KeyViewModel>? setLabelRequested = null)
    {
        Name = cluster.Name;
        IsRightHand = isRightHand;

        // Hand-relative position (subtract hand origin from board-absolute)
        Left = cluster.Left - handOriginPx;
        Top = cluster.Top;
        Width = cluster.Width;
        Height = cluster.Height;

        // Keys positioned relative to cluster origin
        Keys = cluster.Keys.Select(pk => new KeyViewModel(pk, layer,
            cluster.Left, cluster.Top,
            totalLayers, userLayerColors, setLabelRequested)).ToList();
    }
}
