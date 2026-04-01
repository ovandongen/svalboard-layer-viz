using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Layout;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

public class ClusterViewModelTests
{
    [Fact]
    public void BuildFromLayout_ReturnsExpectedClusterCount()
    {
        var clusters = ClusterViewModel.BuildFromLayout();
        // Svalboard has 11 clusters: L-Mod, L-Index...L-Pinky, L-Thumb, R-Index...R-Pinky, R-Thumb
        Assert.Equal(11, clusters.Count);
    }

    [Fact]
    public void BuildFromLayout_AllClustersHavePositiveDimensions()
    {
        var clusters = ClusterViewModel.BuildFromLayout();
        foreach (var cluster in clusters)
        {
            Assert.True(cluster.Width > 0, $"{cluster.Name} has non-positive width");
            Assert.True(cluster.Height > 0, $"{cluster.Name} has non-positive height");
        }
    }

    [Fact]
    public void Constructor_ScalesAt60PixelsPerUnit()
    {
        // KeyPosition(Row, Col, X, Y, Cluster, Direction, Width, Height)
        var positions = new[]
        {
            new KeyPosition(0, 0, 0, 0, "Test", "N"),
            new KeyPosition(0, 1, 2, 0, "Test", "S"),
        };
        var vm = new ClusterViewModel("Test", positions);

        // X range: 0 to 3 (2 + width 1), scaled at 60px + 4px padding each side
        Assert.Equal(-4.0, vm.Left, 1);
        Assert.Equal(3 * 60.0 + 4.0 - (-4.0), vm.Width, 1);
    }

    [Fact]
    public void Constructor_IncludesPadding()
    {
        var positions = new[]
        {
            new KeyPosition(0, 0, 1.0, 1.0, "Pad", "Down"),
        };
        var vm = new ClusterViewModel("Pad", positions);

        // Without padding: Left=60, Top=60, Width=60, Height=60
        // With 4px padding: Left=56, Top=56, Width=68, Height=68
        Assert.Equal(1.0 * 60 - 4.0, vm.Left, 1);
        Assert.Equal(1.0 * 60 - 4.0, vm.Top, 1);
        Assert.Equal(60.0 + 8.0, vm.Width, 1);
        Assert.Equal(60.0 + 8.0, vm.Height, 1);
    }

    [Fact]
    public void ClusterNames_MatchExpectedSet()
    {
        var clusters = ClusterViewModel.BuildFromLayout();
        var names = clusters.Select(c => c.Name).OrderBy(n => n).ToList();

        Assert.Contains("L-Index", names);
        Assert.Contains("L-Middle", names);
        Assert.Contains("L-Ring", names);
        Assert.Contains("L-Pinky", names);
        Assert.Contains("R-Index", names);
        Assert.Contains("R-Middle", names);
        Assert.Contains("R-Ring", names);
        Assert.Contains("R-Pinky", names);
    }
}
