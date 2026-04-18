using SvalboardLayerViz.Core.History;
using SvalboardLayerViz.Core.Keymap;
using Xunit;

namespace SvalboardLayerViz.Tests.History;

public class SnapshotDiffTests
{
    private static ushort[,,] MakeKeymap(ushort fill = 0)
    {
        var km = new ushort[2, 3, 4];
        for (var l = 0; l < 2; l++)
            for (var r = 0; r < 3; r++)
                for (var c = 0; c < 4; c++)
                    km[l, r, c] = fill;
        return km;
    }

    [Fact]
    public void Diff_IdenticalKeymaps_ReturnsEmpty()
    {
        var km = MakeKeymap(0x0004);
        var diffs = SnapshotDiffComputer.Diff(km, km);
        Assert.Empty(diffs);
    }

    [Fact]
    public void Diff_OneChange_ReturnsOneDiff()
    {
        var from = MakeKeymap(0x0004);
        var to = MakeKeymap(0x0004);
        to[1, 2, 3] = 0x0005;

        var diffs = SnapshotDiffComputer.Diff(from, to);
        Assert.Single(diffs);

        var d = diffs[0];
        Assert.Equal(1, d.Layer);
        Assert.Equal(2, d.Row);
        Assert.Equal(3, d.Col);
        Assert.Equal(0x0004, d.OldCode);
        Assert.Equal(0x0005, d.NewCode);
    }

    [Fact]
    public void Diff_MultipleChanges_ReturnsAll()
    {
        var from = MakeKeymap();
        var to = MakeKeymap();
        to[0, 0, 0] = 0x0010;
        to[0, 1, 2] = 0x0020;
        to[1, 0, 0] = 0x0030;

        var diffs = SnapshotDiffComputer.Diff(from, to);
        Assert.Equal(3, diffs.Count);
    }

    [Fact]
    public void Diff_MismatchedDimensions_Throws()
    {
        var from = new ushort[2, 3, 4];
        var to = new ushort[2, 3, 5]; // different cols

        Assert.Throws<ArgumentException>(() => SnapshotDiffComputer.Diff(from, to));
    }

    [Fact]
    public void Diff_DifferentLayerCount_Throws()
    {
        var from = new ushort[2, 3, 4];
        var to = new ushort[3, 3, 4];

        Assert.Throws<ArgumentException>(() => SnapshotDiffComputer.Diff(from, to));
    }

    // --- ToEditOps ---

    [Fact]
    public void ToEditOps_ConvertsDiffsToSetKeyOps()
    {
        var diffs = new List<SnapshotDiffComputer.KeyDiff>
        {
            new(0, 1, 2, 0x0004, 0x0005),
            new(1, 0, 0, 0x0010, 0x0020),
        };

        var ops = SnapshotDiffComputer.ToEditOps(diffs);
        Assert.Equal(2, ops.Count);

        Assert.Equal(0, ops[0].Layer);
        Assert.Equal(1, ops[0].Row);
        Assert.Equal(2, ops[0].Col);
        Assert.Equal(0x0004, ops[0].OldCode);
        Assert.Equal(0x0005, ops[0].NewCode);

        Assert.Equal(1, ops[1].Layer);
    }

    [Fact]
    public void ToEditOps_EmptyDiffs_ReturnsEmpty()
    {
        var ops = SnapshotDiffComputer.ToEditOps([]);
        Assert.Empty(ops);
    }

    // --- Integration: Diff → EditOps → EditSession ---

    [Fact]
    public void DiffToEditSession_RoundTrip()
    {
        var from = MakeKeymap(0x0004);
        var to = MakeKeymap(0x0004);
        to[0, 0, 0] = 0x0010;
        to[1, 2, 3] = 0x0020;

        var diffs = SnapshotDiffComputer.Diff(from, to);
        var ops = SnapshotDiffComputer.ToEditOps(diffs);

        var session = new KeymapEditSession(from);
        foreach (var op in ops)
            session.Apply(op);

        Assert.Equal(0x0010, session.GetCurrent(0, 0, 0));
        Assert.Equal(0x0020, session.GetCurrent(1, 2, 3));
        Assert.Equal(2, session.PendingChanges.Count);
    }
}
