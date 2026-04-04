using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.Core.Layout;

/// <summary>
/// Computes the board layout from a Layer model.
/// Single source of truth for hand/cluster/key grouping and positioning,
/// used by both the UI (ViewModels) and export (BoardRenderer).
/// </summary>
public static class BoardLayoutComputer
{
    private static readonly Dictionary<(int Row, int Col), KeyPosition> ClusterLookup;

    /// <summary>Cluster names that sit at the bottom of the board (thumbs + left modifiers).</summary>
    public static readonly HashSet<string> BottomClusters = ["L-Thumb", "R-Thumb", "L-Mod"];

    static BoardLayoutComputer()
    {
        ClusterLookup = SvalboardLayout.GetKeyPositions()
            .ToDictionary(p => (p.Row, p.Col));
    }

    /// <summary>
    /// Computes the full board layout for a layer, grouping keys into hands and clusters
    /// with board-absolute pixel coordinates.
    /// </summary>
    public static ComputedBoardLayout Compute(Layer layer)
    {
        var leftKeys = layer.Keys.Where(k => !SvalboardLayout.IsRightHand(k.Row)).ToList();
        var rightKeys = layer.Keys.Where(k => SvalboardLayout.IsRightHand(k.Row)).ToList();

        var leftHand = ComputeHand(leftKeys, isRightHand: false);
        var rightHand = ComputeHand(rightKeys, isRightHand: true);

        var allKeys = leftHand.AllKeys.Concat(rightHand.AllKeys).ToList();

        return new ComputedBoardLayout(leftHand, rightHand, allKeys);
    }

    /// <summary>
    /// Computes the board height excluding bottom clusters (for hide-thumb export mode).
    /// </summary>
    public static double GetBoardHeightExcludingBottomClusters()
    {
        double maxY = 0;
        foreach (var pos in SvalboardLayout.GetKeyPositions())
        {
            if (BottomClusters.Contains(pos.Cluster))
                continue;
            var bottom = (pos.Y + pos.Height) * SvalboardLayout.Scale;
            if (bottom > maxY) maxY = bottom;
        }
        return maxY;
    }

    private static PositionedHand ComputeHand(IReadOnlyList<Key> keys, bool isRightHand)
    {
        if (keys.Count == 0)
            return new PositionedHand(null, [], [], 0, 0);

        var thumbRow = isRightHand ? 5 : 0;
        var byRow = keys.GroupBy(k => k.Row).OrderBy(g => g.Key);

        PositionedCluster? thumb = null;
        var fingers = new List<PositionedCluster>();
        var allKeys = new List<PositionedKey>();

        foreach (var group in byRow)
        {
            var rowKeys = group.ToList();
            var clusterName = ClusterLookup.GetValueOrDefault((group.Key, 0))?.Cluster
                              ?? $"Row{group.Key}";
            var isThumb = group.Key == thumbRow;

            var positionedKeys = rowKeys.Select(k => new PositionedKey(
                k,
                k.X * SvalboardLayout.Scale,
                k.Y * SvalboardLayout.Scale,
                k.Width * SvalboardLayout.Scale,
                k.Height * SvalboardLayout.Scale)).ToList();

            var clusterLeft = positionedKeys.Min(pk => pk.BoardX);
            var clusterTop = positionedKeys.Min(pk => pk.BoardY);
            var clusterRight = positionedKeys.Max(pk => pk.BoardX + pk.Width);
            var clusterBottom = positionedKeys.Max(pk => pk.BoardY + pk.Height);

            var cluster = new PositionedCluster(
                clusterName,
                isThumb,
                positionedKeys,
                clusterLeft, clusterTop,
                clusterRight - clusterLeft, clusterBottom - clusterTop);

            if (isThumb)
                thumb = cluster;
            else
                fingers.Add(cluster);

            allKeys.AddRange(positionedKeys);
        }

        // Compute hand bounding box from all clusters
        var allClusters = fingers.AsEnumerable();
        if (thumb != null) allClusters = allClusters.Prepend(thumb);
        var clusterList = allClusters.ToList();

        var handWidth = clusterList.Max(c => c.Left + c.Width);
        var handHeight = clusterList.Max(c => c.Top + c.Height);

        return new PositionedHand(thumb, fingers, allKeys, handWidth, handHeight);
    }
}
