using SvalboardLayerViz.Core.Dynamic;
using SvalboardLayerViz.Core.Keymap;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

public class EditSessionDynamicEntryTests
{
    private static KeymapEditSession MakeSession(int comboCount = 2, int tdCount = 2)
    {
        var baseline = new ushort[1, 1, 1];
        var combos = Enumerable.Range(0, comboCount)
            .Select(_ => new byte[Combo.EntryBytes])
            .ToList();
        var tds = Enumerable.Range(0, tdCount)
            .Select(_ => new byte[TapDance.EntryBytes])
            .ToList();
        return new KeymapEditSession(baseline, null, null, null, combos, tds);
    }

    [Fact]
    public void NewSession_NoPendingChanges()
    {
        var s = MakeSession();
        Assert.False(s.HasPendingChanges);
        Assert.False(s.HasPendingComboChanges);
        Assert.False(s.HasPendingTapDanceChanges);
        Assert.Equal(2, s.ComboCount);
        Assert.Equal(2, s.TapDanceCount);
    }

    [Fact]
    public void ApplySetComboOp_UpdatesCurrent_FlagsPending()
    {
        var s = MakeSession();
        var newBytes = ComboCodec.Encode(new Combo(0x04, 0x05, 0, 0, 0x29));

        s.Apply(new SetComboOp(0, s.GetCurrentCombo(0), newBytes));

        Assert.True(s.HasPendingComboChanges);
        Assert.True(s.HasPendingChanges);
        Assert.Equal(newBytes, s.GetCurrentCombo(0));
        Assert.Equal(new byte[Combo.EntryBytes], s.GetBaselineCombo(0));
    }

    [Fact]
    public void Undo_RevertsComboOp()
    {
        var s = MakeSession();
        var newBytes = new byte[Combo.EntryBytes];
        newBytes[0] = 0x04;
        s.Apply(new SetComboOp(0, s.GetCurrentCombo(0), newBytes));

        s.Undo();

        Assert.False(s.HasPendingComboChanges);
        Assert.Equal(new byte[Combo.EntryBytes], s.GetCurrentCombo(0));
    }

    [Fact]
    public void Redo_ReappliesComboOp()
    {
        var s = MakeSession();
        var newBytes = new byte[Combo.EntryBytes];
        newBytes[0] = 0x04;
        s.Apply(new SetComboOp(0, s.GetCurrentCombo(0), newBytes));
        s.Undo();
        s.Redo();

        Assert.True(s.HasPendingComboChanges);
        Assert.Equal(newBytes, s.GetCurrentCombo(0));
    }

    [Fact]
    public void ApplySetTapDanceOp_UpdatesCurrent()
    {
        var s = MakeSession();
        var newBytes = TapDanceCodec.Encode(new TapDance(0x04, 0xE1, 0, 0, 200));

        s.Apply(new SetTapDanceOp(1, s.GetCurrentTapDance(1), newBytes));

        Assert.True(s.HasPendingTapDanceChanges);
        Assert.Equal(newBytes, s.GetCurrentTapDance(1));
    }

    [Fact]
    public void Discard_ResetsDynamicEntries()
    {
        var s = MakeSession();
        var newCombo = new byte[Combo.EntryBytes] { 0x04, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var newTd = new byte[TapDance.EntryBytes] { 0x05, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        s.Apply(new SetComboOp(0, s.GetCurrentCombo(0), newCombo));
        s.Apply(new SetTapDanceOp(0, s.GetCurrentTapDance(0), newTd));

        s.Discard();

        Assert.False(s.HasPendingComboChanges);
        Assert.False(s.HasPendingTapDanceChanges);
        Assert.False(s.CanUndo);
    }

    [Fact]
    public void BuildDeviceWrites_EmitsComboAndTapDanceWrites()
    {
        var s = MakeSession(comboCount: 3, tdCount: 3);
        var comboBytes = new byte[Combo.EntryBytes] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var tdBytes = new byte[TapDance.EntryBytes] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        s.Apply(new SetComboOp(1, s.GetCurrentCombo(1), comboBytes));
        s.Apply(new SetTapDanceOp(2, s.GetCurrentTapDance(2), tdBytes));

        var writes = s.BuildDeviceWrites();

        var combo = Assert.Single(writes.OfType<ComboEntryWrite>());
        Assert.Equal(1, combo.Index);
        Assert.Equal(comboBytes, combo.Entry);

        var td = Assert.Single(writes.OfType<TapDanceEntryWrite>());
        Assert.Equal(2, td.Index);
        Assert.Equal(tdBytes, td.Entry);
    }

    [Fact]
    public void BuildDeviceWrites_EmptyWhenNoPending()
    {
        var s = MakeSession();
        Assert.Empty(s.BuildDeviceWrites());
    }

    [Fact]
    public void MultipleEditsToSameSlot_UseLatestValue()
    {
        var s = MakeSession();
        var first = new byte[Combo.EntryBytes] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var second = new byte[Combo.EntryBytes] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        s.Apply(new SetComboOp(0, s.GetCurrentCombo(0), first));
        s.Apply(new SetComboOp(0, s.GetCurrentCombo(0), second));

        var write = Assert.Single(s.BuildDeviceWrites().OfType<ComboEntryWrite>());
        Assert.Equal(second, write.Entry);
    }

    [Fact]
    public void EditingToBaselineValue_NoPending()
    {
        var s = MakeSession();
        var baseline = s.GetCurrentCombo(0);
        var changed = new byte[Combo.EntryBytes] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        s.Apply(new SetComboOp(0, baseline, changed));
        s.Apply(new SetComboOp(0, changed, baseline));

        Assert.False(s.HasPendingComboChanges);
    }

    [Fact]
    public void ApplySetComboOp_OutOfRangeIndex_Throws()
    {
        var s = MakeSession(comboCount: 2);
        var bytes = new byte[Combo.EntryBytes];

        var ex = Assert.Throws<InvalidOperationException>(
            () => s.Apply(new SetComboOp(999, bytes, bytes)));
        Assert.Contains("999", ex.Message);
        Assert.Contains("SetComboOp", ex.Message);
    }

    [Fact]
    public void ApplySetTapDanceOp_OutOfRangeIndex_Throws()
    {
        var s = MakeSession(tdCount: 2);
        var bytes = new byte[TapDance.EntryBytes];

        var ex = Assert.Throws<InvalidOperationException>(
            () => s.Apply(new SetTapDanceOp(-1, bytes, bytes)));
        Assert.Contains("-1", ex.Message);
        Assert.Contains("SetTapDanceOp", ex.Message);
    }

    // Boundary cases: `index = count` is the canonical off-by-one that a
    // `>=` vs `>` regression would let slip through. `index = count - 1` is
    // the positive control — must NOT throw. The existing `999`/`-1` tests
    // only prove that *clearly* bad values throw, not the exact boundary.

    [Fact]
    public void ApplySetComboOp_IndexEqualCount_Throws()
    {
        var s = MakeSession(comboCount: 2);
        var bytes = new byte[Combo.EntryBytes];

        var ex = Assert.Throws<InvalidOperationException>(
            () => s.Apply(new SetComboOp(2, bytes, bytes)));
        Assert.Contains("2", ex.Message);
        Assert.Contains("SetComboOp", ex.Message);
    }

    [Fact]
    public void ApplySetComboOp_IndexCountMinusOne_Succeeds()
    {
        var s = MakeSession(comboCount: 2);
        var newBytes = new byte[Combo.EntryBytes] { 0x04, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        s.Apply(new SetComboOp(1, s.GetCurrentCombo(1), newBytes));

        Assert.Equal(newBytes, s.GetCurrentCombo(1));
    }

    [Fact]
    public void ApplySetTapDanceOp_IndexEqualCount_Throws()
    {
        var s = MakeSession(tdCount: 2);
        var bytes = new byte[TapDance.EntryBytes];

        var ex = Assert.Throws<InvalidOperationException>(
            () => s.Apply(new SetTapDanceOp(2, bytes, bytes)));
        Assert.Contains("2", ex.Message);
        Assert.Contains("SetTapDanceOp", ex.Message);
    }

    [Fact]
    public void ApplySetTapDanceOp_IndexCountMinusOne_Succeeds()
    {
        var s = MakeSession(tdCount: 2);
        var newBytes = new byte[TapDance.EntryBytes] { 0x04, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        s.Apply(new SetTapDanceOp(1, s.GetCurrentTapDance(1), newBytes));

        Assert.Equal(newBytes, s.GetCurrentTapDance(1));
    }
}
