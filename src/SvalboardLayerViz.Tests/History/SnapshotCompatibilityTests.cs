using SvalboardLayerViz.Core.History;
using Xunit;

namespace SvalboardLayerViz.Tests.History;

public class SnapshotCompatibilityTests
{
    private static readonly KeyboardId Device = new("1234", "5678", "ABCDEF0123456789");

    [Fact]
    public void Check_ExactMatch_ReturnsExact()
    {
        var snapshot = new KeyboardId("1234", "5678", "ABCDEF0123456789");
        Assert.Equal(SnapshotCompatibility.Exact,
            SnapshotCompatibilityChecker.Check(snapshot, Device));
    }

    [Fact]
    public void Check_SameVidPidDifferentUid_ReturnsFirmware()
    {
        var snapshot = new KeyboardId("1234", "5678", "0000000000000000");
        Assert.Equal(SnapshotCompatibility.Firmware,
            SnapshotCompatibilityChecker.Check(snapshot, Device));
    }

    [Fact]
    public void Check_DifferentVid_ReturnsNone()
    {
        var snapshot = new KeyboardId("FFFF", "5678", "ABCDEF0123456789");
        Assert.Equal(SnapshotCompatibility.None,
            SnapshotCompatibilityChecker.Check(snapshot, Device));
    }

    [Fact]
    public void Check_DifferentPid_ReturnsNone()
    {
        var snapshot = new KeyboardId("1234", "FFFF", "ABCDEF0123456789");
        Assert.Equal(SnapshotCompatibility.None,
            SnapshotCompatibilityChecker.Check(snapshot, Device));
    }

    [Fact]
    public void Check_BothDifferent_ReturnsNone()
    {
        var snapshot = new KeyboardId("AAAA", "BBBB", "CCCCDDDDEEEE0000");
        Assert.Equal(SnapshotCompatibility.None,
            SnapshotCompatibilityChecker.Check(snapshot, Device));
    }

    // --- KeyboardId ---

    [Fact]
    public void KeyboardId_From_FormatsCorrectly()
    {
        var id = KeyboardId.From(0x1234, 0x5678, 0xABCDEF0123456789);
        Assert.Equal("1234", id.VendorId);
        Assert.Equal("5678", id.ProductId);
        Assert.Equal("ABCDEF0123456789", id.Uid);
    }

    [Fact]
    public void KeyboardId_From_PadsWithZeros()
    {
        var id = KeyboardId.From(0x01, 0x02, 0x03);
        Assert.Equal("0001", id.VendorId);
        Assert.Equal("0002", id.ProductId);
        Assert.Equal("0000000000000003", id.Uid);
    }
}
