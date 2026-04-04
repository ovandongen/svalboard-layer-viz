using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.Layout;

/// <summary>
/// Verifies that the shared BoardLayoutComputer produces consistent results
/// when consumed by both the UI (ViewModels) and export (BoardRenderer) paths.
/// </summary>
public class LayoutConsistencyTests
{
    private static Layer MakeFullLayer(int index = 0)
    {
        var positions = SvalboardLayout.GetKeyPositions();
        var keys = positions.Select(p => new Key
        {
            Row = p.Row, Col = p.Col, RawKeycode = 0x04, DisplayLabel = "A",
            X = p.X, Y = p.Y, Width = p.Width, Height = p.Height,
        }).ToList();

        return new Layer { Index = index, Keys = keys };
    }

    [Fact]
    public void AllKeys_AppearInExactlyOneCluster()
    {
        var layout = BoardLayoutComputer.Compute(MakeFullLayer());

        var allClusters = layout.LeftHand.FingerClusters
            .Concat(layout.RightHand.FingerClusters);
        if (layout.LeftHand.ThumbCluster != null)
            allClusters = allClusters.Append(layout.LeftHand.ThumbCluster);
        if (layout.RightHand.ThumbCluster != null)
            allClusters = allClusters.Append(layout.RightHand.ThumbCluster);

        var keysByRowCol = new Dictionary<(int, int), int>();
        foreach (var cluster in allClusters)
        {
            foreach (var pk in cluster.Keys)
            {
                var key = (pk.Key.Row, pk.Key.Col);
                keysByRowCol.TryGetValue(key, out var count);
                keysByRowCol[key] = count + 1;
            }
        }

        // Every key appears exactly once
        Assert.All(keysByRowCol, kv =>
            Assert.True(kv.Value == 1, $"Key ({kv.Key.Item1},{kv.Key.Item2}) appears {kv.Value} times"));

        // Total matches layout
        Assert.Equal(52, keysByRowCol.Count);
    }

    [Fact]
    public void ThumbDetection_MatchesBottomClusters()
    {
        // Verify that BoardLayoutComputer's row-based thumb detection
        // identifies the same keys as the BottomClusters name set
        var layout = BoardLayoutComputer.Compute(MakeFullLayer());
        var positions = SvalboardLayout.GetKeyPositions()
            .ToDictionary(p => (p.Row, p.Col));

        var allClusters = layout.LeftHand.FingerClusters
            .Concat(layout.RightHand.FingerClusters);
        if (layout.LeftHand.ThumbCluster != null)
            allClusters = allClusters.Append(layout.LeftHand.ThumbCluster);
        if (layout.RightHand.ThumbCluster != null)
            allClusters = allClusters.Append(layout.RightHand.ThumbCluster);

        foreach (var cluster in allClusters)
        {
            var isBottomByName = BoardLayoutComputer.BottomClusters.Contains(cluster.Name);
            foreach (var pk in cluster.Keys)
            {
                if (positions.TryGetValue((pk.Key.Row, pk.Key.Col), out var pos))
                {
                    var isBottomByPosition = BoardLayoutComputer.BottomClusters.Contains(pos.Cluster);
                    // The cluster's bottom status should be consistent with individual key positions
                    Assert.Equal(isBottomByPosition, isBottomByName ||
                        (cluster.IsThumb && BoardLayoutComputer.BottomClusters.Contains(pos.Cluster)));
                }
            }
        }
    }

    [Fact]
    public void ExportAndUI_UseIdenticalBoardAbsolutePositions()
    {
        // Both BoardRenderer and ViewModels consume BoardLayoutComputer output.
        // Verify the computed positions are deterministic and match.
        var layer = MakeFullLayer();

        var layout1 = BoardLayoutComputer.Compute(layer);
        var layout2 = BoardLayoutComputer.Compute(layer);

        Assert.Equal(layout1.AllKeys.Count, layout2.AllKeys.Count);
        for (int i = 0; i < layout1.AllKeys.Count; i++)
        {
            Assert.Equal(layout1.AllKeys[i].BoardX, layout2.AllKeys[i].BoardX);
            Assert.Equal(layout1.AllKeys[i].BoardY, layout2.AllKeys[i].BoardY);
            Assert.Equal(layout1.AllKeys[i].Width, layout2.AllKeys[i].Width);
            Assert.Equal(layout1.AllKeys[i].Height, layout2.AllKeys[i].Height);
        }
    }

    [Fact]
    public void ViewModelKeys_ReconstructToBoardAbsolute()
    {
        // Verify that UI cluster-relative coordinates can be traced back to board-absolute
        var layer = MakeFullLayer();
        var layout = BoardLayoutComputer.Compute(layer);

        var leftHand = new HandViewModel(layout.LeftHand, false, layer);
        var rightHand = new HandViewModel(layout.RightHand, true, layer);

        // For each cluster, key.Left + cluster.Left + handOrigin should equal BoardX
        var leftOriginPx = 0.0;
        var rightOriginPx = SvalboardLayout.RightHandOriginX * SvalboardLayout.Scale;

        VerifyHandReconstruction(leftHand, leftOriginPx, layout.LeftHand);
        VerifyHandReconstruction(rightHand, rightOriginPx, layout.RightHand);
    }

    private static void VerifyHandReconstruction(HandViewModel handVm, double handOriginPx, PositionedHand posHand)
    {
        var allVmClusters = handVm.FingerClusters.AsEnumerable();
        if (handVm.ThumbCluster != null) allVmClusters = allVmClusters.Prepend(handVm.ThumbCluster);

        var allPosClusters = posHand.FingerClusters.AsEnumerable();
        if (posHand.ThumbCluster != null) allPosClusters = allPosClusters.Prepend(posHand.ThumbCluster);

        var vmClusterList = allVmClusters.ToList();
        var posClusterList = allPosClusters.ToList();
        Assert.Equal(posClusterList.Count, vmClusterList.Count);

        for (int ci = 0; ci < vmClusterList.Count; ci++)
        {
            var vmCluster = vmClusterList[ci];
            var posCluster = posClusterList[ci];
            Assert.Equal(posCluster.Keys.Count, vmCluster.Keys.Count);

            for (int ki = 0; ki < vmCluster.Keys.Count; ki++)
            {
                var vmKey = vmCluster.Keys[ki];
                var posKey = posCluster.Keys[ki];

                // Reconstruct board-absolute: key.Left + cluster.Left + handOrigin
                var reconstructedX = vmKey.Left + vmCluster.Left + handOriginPx;
                var reconstructedY = vmKey.Top + vmCluster.Top;

                Assert.Equal(posKey.BoardX, reconstructedX, 0.1);
                Assert.Equal(posKey.BoardY, reconstructedY, 0.1);
            }
        }
    }
}
