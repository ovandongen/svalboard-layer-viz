using SvalboardLayerViz.Core.Layout;
using Xunit;

namespace SvalboardLayerViz.Tests.Layout;

public class SvalboardLayoutTests
{
    private readonly IReadOnlyList<KeyPosition> _positions = SvalboardLayout.GetKeyPositions();

    [Fact]
    public void GetKeyPositions_Returns56Keys()
    {
        // 10 rows: rows 0,5 have 6 keys each; finger rows (1-4, 6-9) have 5 each = 52
        Assert.Equal(52, _positions.Count);
    }

    [Fact]
    public void AllRows0Through9_ArePresent()
    {
        var rows = _positions.Select(p => p.Row).Distinct().OrderBy(r => r).ToList();
        Assert.Equal(Enumerable.Range(0, 10).ToList(), rows);
    }

    [Theory]
    [InlineData(0, 6)]  // L-Mod
    [InlineData(5, 6)]  // Thumb cluster
    [InlineData(1, 5)]  // L-Index
    [InlineData(2, 5)]  // L-Middle
    [InlineData(3, 5)]  // L-Ring
    [InlineData(4, 5)]  // L-Pinky
    [InlineData(6, 5)]  // R-Index
    [InlineData(7, 5)]  // R-Middle
    [InlineData(8, 5)]  // R-Ring
    [InlineData(9, 5)]  // R-Pinky
    public void Row_HasExpectedKeyCount(int row, int expectedCount)
    {
        var count = _positions.Count(p => p.Row == row);
        Assert.Equal(expectedCount, count);
    }

    [Fact]
    public void NoDuplicateRowColPairs()
    {
        var pairs = _positions.Select(p => (p.Row, p.Col)).ToList();
        Assert.Equal(pairs.Count, pairs.Distinct().Count());
    }

    [Fact]
    public void Coordinates_AreInReasonableRange()
    {
        Assert.All(_positions, p =>
        {
            Assert.InRange(p.X, 0.0, 24.0);
            Assert.InRange(p.Y, 0.0, 7.0);
        });
    }

    [Fact]
    public void All11Clusters_ArePresent()
    {
        var expected = new HashSet<string>
        {
            "L-Mod", "L-Index", "L-Middle", "L-Ring", "L-Pinky", "L-Thumb",
            "R-Thumb", "R-Index", "R-Middle", "R-Ring", "R-Pinky"
        };
        var actual = _positions.Select(p => p.Cluster).Distinct().ToHashSet();
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(1)]  // L-Index
    [InlineData(2)]  // L-Middle
    [InlineData(3)]  // L-Ring
    [InlineData(4)]  // L-Pinky
    [InlineData(6)]  // R-Index
    [InlineData(7)]  // R-Middle
    [InlineData(8)]  // R-Ring
    [InlineData(9)]  // R-Pinky
    public void FingerClusters_HaveAllFiveDirections(int row)
    {
        var directions = _positions
            .Where(p => p.Row == row)
            .Select(p => p.Direction)
            .OrderBy(d => d)
            .ToList();
        Assert.Equal(new[] { "Down", "East", "North", "South", "West" }, directions);
    }

    [Fact]
    public void LeftFingerClusters_HaveLowerX_ThanRightFingerClusters()
    {
        // Exclude thumb clusters since L-Thumb and R-Thumb share the center area
        var leftMaxX = _positions
            .Where(p => p.Cluster.StartsWith("L-") && !p.Cluster.Contains("Thumb"))
            .Max(p => p.X);
        var rightMinX = _positions
            .Where(p => p.Cluster.StartsWith("R-") && !p.Cluster.Contains("Thumb"))
            .Min(p => p.X);
        Assert.True(leftMaxX < rightMinX, $"Left max X ({leftMaxX}) should be < right min X ({rightMinX})");
    }

    [Fact]
    public void AllKeys_HaveDefaultWidthAndHeight()
    {
        Assert.All(_positions, p =>
        {
            Assert.Equal(1.0, p.Width);
            Assert.Equal(1.0, p.Height);
        });
    }

    [Theory]
    [InlineData(0, false)]  // L-Thumb
    [InlineData(1, false)]  // L-Index
    [InlineData(2, false)]  // L-Middle
    [InlineData(3, false)]  // L-Ring
    [InlineData(4, false)]  // L-Pinky
    [InlineData(5, true)]   // R-Thumb
    [InlineData(6, true)]   // R-Index
    [InlineData(7, true)]   // R-Middle
    [InlineData(8, true)]   // R-Ring
    [InlineData(9, true)]   // R-Pinky
    public void IsRightHand_CorrectForRow(int row, bool expected)
    {
        Assert.Equal(expected, SvalboardLayout.IsRightHand(row));
    }
}
