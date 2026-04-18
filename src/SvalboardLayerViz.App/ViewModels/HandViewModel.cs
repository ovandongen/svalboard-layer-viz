using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// Groups all clusters for one hand (4 finger clusters + 1 thumb cluster).
/// Wraps a PositionedHand from BoardLayoutComputer with UI concerns.
/// </summary>
public class HandViewModel
{
    public KeyClusterViewModel ThumbCluster { get; }
    public IReadOnlyList<KeyClusterViewModel> FingerClusters { get; }

    /// <summary>Flat list of all KeyViewModels in this hand, for matrix polling.</summary>
    public IReadOnlyList<KeyViewModel> AllKeys { get; }

    /// <summary>Total hand bounding box (pixels) encompassing all clusters.</summary>
    public double Width { get; }
    public double Height { get; }

    public HandViewModel(
        PositionedHand hand,
        bool isRightHand,
        Layer layer,
        LayerColorPalette palette,
        Action<KeyViewModel>? setLabelRequested = null)
    {
        var handOriginPx = isRightHand ? SvalboardLayout.RightHandOriginX * SvalboardLayout.Scale : 0.0;

        if (hand.AllKeys.Count == 0)
        {
            ThumbCluster = null!;
            FingerClusters = [];
            AllKeys = [];
            Width = 0;
            Height = 0;
            return;
        }

        var fingers = hand.FingerClusters.Select(c =>
            new KeyClusterViewModel(c, layer, handOriginPx, palette, isRightHand, setLabelRequested)).ToList();

        KeyClusterViewModel? thumb = null;
        if (hand.ThumbCluster is not null)
        {
            thumb = new KeyClusterViewModel(hand.ThumbCluster, layer, handOriginPx,
                palette, isRightHand, setLabelRequested);
        }

        ThumbCluster = thumb!;
        FingerClusters = fingers;
        AllKeys = (thumb?.Keys ?? []).Concat(fingers.SelectMany(f => f.Keys)).ToList();

        // Compute hand bounding box from all clusters (hand-relative)
        var allClusters = fingers.AsEnumerable();
        if (thumb != null) allClusters = allClusters.Prepend(thumb);
        var clusterList = allClusters.ToList();
        Width = clusterList.Max(c => c.Left + c.Width);
        Height = clusterList.Max(c => c.Top + c.Height);
    }
}
