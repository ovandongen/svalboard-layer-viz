using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.Core.Layout;

/// <summary>
/// A key with board-absolute pixel coordinates, computed from raw layout units.
/// </summary>
public record PositionedKey(Key Key, double BoardX, double BoardY, double Width, double Height);

/// <summary>
/// A cluster of keys with board-absolute bounding box (pixels).
/// </summary>
public record PositionedCluster(
    string Name,
    bool IsThumb,
    IReadOnlyList<PositionedKey> Keys,
    double Left, double Top, double Width, double Height);

/// <summary>
/// One hand's computed layout: thumb + finger clusters with bounding box (pixels).
/// </summary>
public record PositionedHand(
    PositionedCluster? ThumbCluster,
    IReadOnlyList<PositionedCluster> FingerClusters,
    IReadOnlyList<PositionedKey> AllKeys,
    double Width, double Height);

/// <summary>
/// Complete board layout for one layer — single source of truth for both UI and export.
/// All coordinates are board-absolute pixels (layout units * Scale).
/// </summary>
public record ComputedBoardLayout(
    PositionedHand LeftHand,
    PositionedHand RightHand,
    IReadOnlyList<PositionedKey> AllKeys);
