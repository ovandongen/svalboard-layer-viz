using SvalboardLayerViz.Core.Keymap;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

public class KeymapEditSessionTests
{
    // Test-only EditOp subtype with no matching switch case. Ensures the
    // default branch in ExecuteForward/ExecuteReverse fires loudly when a
    // future op subtype is added without updating both methods, instead of
    // silently diverging the undo stack from _current state.
    private sealed record UnknownOp() : EditOp;

    [Fact]
    public void Apply_UnknownEditOpSubtype_Throws()
    {
        var session = new KeymapEditSession(MakeBaseline());
        var ex = Assert.Throws<InvalidOperationException>(() => session.Apply(new UnknownOp()));
        Assert.Contains("UnknownOp", ex.Message);
    }

    // Helper: 2 layers × 2 rows × 2 cols, all zeroed
    private static ushort[,,] MakeBaseline(int layers = 2, int rows = 2, int cols = 2)
    {
        var b = new ushort[layers, rows, cols];
        // Put some known values in
        b[0, 0, 0] = 0x0004; // KC_A
        b[0, 0, 1] = 0x0005; // KC_B
        b[0, 1, 0] = 0x0006; // KC_C
        b[0, 1, 1] = 0x0007; // KC_D
        b[1, 0, 0] = 0x0001; // KC_TRNS
        b[1, 0, 1] = 0x0001;
        b[1, 1, 0] = 0x0028; // KC_ENTER
        b[1, 1, 1] = 0x0029; // KC_ESC
        return b;
    }

    // --- Construction ---

    [Fact]
    public void Constructor_ClonesBaseline_MutationsDoNotAffectOriginal()
    {
        var baseline = MakeBaseline();
        var session = new KeymapEditSession(baseline);
        baseline[0, 0, 0] = 0xFFFF;
        Assert.Equal(0x0004, session.GetBaseline(0, 0, 0));
    }

    [Fact]
    public void Constructor_NoPendingChanges()
    {
        var session = new KeymapEditSession(MakeBaseline());
        Assert.False(session.HasPendingChanges);
        Assert.Empty(session.PendingChanges);
    }

    [Fact]
    public void Constructor_CorrectDimensions()
    {
        var session = new KeymapEditSession(MakeBaseline(3, 4, 5));
        Assert.Equal(3, session.Layers);
        Assert.Equal(4, session.Rows);
        Assert.Equal(5, session.Cols);
    }

    // --- FromSnapshot ---

    [Fact]
    public void FromSnapshot_RebuildsCorrectly()
    {
        var baseline = MakeBaseline();
        var flat = new ushort[2 * 2 * 2];
        var i = 0;
        for (var l = 0; l < 2; l++)
            for (var r = 0; r < 2; r++)
                for (var c = 0; c < 2; c++)
                    flat[i++] = baseline[l, r, c];

        var session = KeymapEditSession.FromSnapshot(flat, 2, 2, 2);
        Assert.Equal(0x0004, session.GetBaseline(0, 0, 0));
        Assert.Equal(0x0029, session.GetBaseline(1, 1, 1));
        Assert.False(session.HasPendingChanges);
    }

    [Fact]
    public void FromSnapshot_WrongLength_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            KeymapEditSession.FromSnapshot(new ushort[5], 2, 2, 2));
    }

    // --- Apply ---

    [Fact]
    public void Apply_ChangesCurrentState()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x001E)); // A → 1

        Assert.Equal(0x001E, session.GetCurrent(0, 0, 0));
        Assert.Equal(0x0004, session.GetBaseline(0, 0, 0));
    }

    [Fact]
    public void Apply_MarksPendingChanges()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x001E));

        Assert.True(session.HasPendingChanges);
        Assert.Single(session.PendingChanges);
        var (layer, row, col, oldCode, newCode) = session.PendingChanges[0];
        Assert.Equal(0, layer);
        Assert.Equal(0, row);
        Assert.Equal(0, col);
        Assert.Equal(0x0004, oldCode);
        Assert.Equal(0x001E, newCode);
    }

    [Fact]
    public void Apply_MultipleEdits_AllTracked()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x001E));
        session.Apply(new SetKeyOp(1, 1, 0, 0x0028, 0x0029));

        Assert.Equal(2, session.PendingChanges.Count);
    }

    [Fact]
    public void Apply_SameKeySameValue_NoPendingChange()
    {
        var session = new KeymapEditSession(MakeBaseline());
        // Change and change back via apply (not undo)
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x001E));
        session.Apply(new SetKeyOp(0, 0, 0, 0x001E, 0x0004));

        Assert.False(session.HasPendingChanges);
    }

    // --- Undo ---

    [Fact]
    public void Undo_RestoresPreviousValue()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x001E));
        session.Undo();

        Assert.Equal(0x0004, session.GetCurrent(0, 0, 0));
        Assert.False(session.HasPendingChanges);
    }

    [Fact]
    public void Undo_MultipleTimes()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x001E));
        session.Apply(new SetKeyOp(0, 0, 1, 0x0005, 0x001F));

        session.Undo();
        Assert.Equal(0x0005, session.GetCurrent(0, 0, 1)); // restored
        Assert.Equal(0x001E, session.GetCurrent(0, 0, 0)); // still edited

        session.Undo();
        Assert.Equal(0x0004, session.GetCurrent(0, 0, 0)); // restored
        Assert.False(session.HasPendingChanges);
    }

    [Fact]
    public void Undo_WhenEmpty_DoesNothing()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Undo(); // should not throw
        Assert.Equal(0x0004, session.GetCurrent(0, 0, 0));
    }

    [Fact]
    public void Undo_RestoresPriorState_AndCanUndoTracks()
    {
        // Original assertion only checked CanUndo before/after Undo(), which
        // could pass even if Undo silently no-op'd. Verify GetCurrent rewinds.
        var session = new KeymapEditSession(MakeBaseline());
        var baseline = session.GetCurrent(0, 0, 0);
        Assert.False(session.CanUndo);

        session.Apply(new SetKeyOp(0, 0, 0, baseline, 0x001E));
        Assert.True(session.CanUndo);
        Assert.Equal((ushort)0x001E, session.GetCurrent(0, 0, 0));

        session.Undo();
        Assert.False(session.CanUndo);
        Assert.Equal(baseline, session.GetCurrent(0, 0, 0));
    }

    // --- Redo ---

    [Fact]
    public void Redo_ReappliesUndoneOp()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x001E));
        session.Undo();
        session.Redo();

        Assert.Equal(0x001E, session.GetCurrent(0, 0, 0));
        Assert.True(session.HasPendingChanges);
    }

    [Fact]
    public void Redo_WhenEmpty_DoesNothing()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Redo(); // should not throw
        Assert.Equal(0x0004, session.GetCurrent(0, 0, 0));
    }

    [Fact]
    public void CanRedo_CorrectlyReflectsState()
    {
        var session = new KeymapEditSession(MakeBaseline());
        Assert.False(session.CanRedo);

        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x001E));
        Assert.False(session.CanRedo);

        session.Undo();
        Assert.True(session.CanRedo);

        session.Redo();
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Apply_ClearsRedoStack()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x001E));
        session.Undo();
        Assert.True(session.CanRedo);

        session.Apply(new SetKeyOp(0, 0, 1, 0x0005, 0x001F)); // new edit
        Assert.False(session.CanRedo); // redo cleared
    }

    // --- Undo/Redo chains ---

    [Fact]
    public void UndoRedoChain_MultipleOps()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x0010));
        session.Apply(new SetKeyOp(0, 0, 0, 0x0010, 0x0020));
        session.Apply(new SetKeyOp(0, 0, 0, 0x0020, 0x0030));

        Assert.Equal(0x0030, session.GetCurrent(0, 0, 0));

        session.Undo();
        Assert.Equal(0x0020, session.GetCurrent(0, 0, 0));

        session.Undo();
        Assert.Equal(0x0010, session.GetCurrent(0, 0, 0));

        session.Redo();
        Assert.Equal(0x0020, session.GetCurrent(0, 0, 0));

        session.Redo();
        Assert.Equal(0x0030, session.GetCurrent(0, 0, 0));
    }

    [Fact]
    public void UndoCount_And_RedoCount()
    {
        var session = new KeymapEditSession(MakeBaseline());
        Assert.Equal(0, session.UndoCount);
        Assert.Equal(0, session.RedoCount);

        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x0010));
        session.Apply(new SetKeyOp(0, 0, 1, 0x0005, 0x0011));
        Assert.Equal(2, session.UndoCount);
        Assert.Equal(0, session.RedoCount);

        session.Undo();
        Assert.Equal(1, session.UndoCount);
        Assert.Equal(1, session.RedoCount);
    }

    // --- Discard ---

    [Fact]
    public void Discard_ResetsToBaseline()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x001E));
        session.Apply(new SetKeyOp(1, 1, 1, 0x0029, 0x0000));

        session.Discard();

        Assert.False(session.HasPendingChanges);
        Assert.Equal(0x0004, session.GetCurrent(0, 0, 0));
        Assert.Equal(0x0029, session.GetCurrent(1, 1, 1));
    }

    [Fact]
    public void Discard_ClearsUndoAndRedo()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x001E));
        session.Undo();

        session.Discard();

        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    // --- BuildDeviceWrites ---

    [Fact]
    public void BuildDeviceWrites_NoPending_ReturnsEmpty()
    {
        var session = new KeymapEditSession(MakeBaseline());
        Assert.Empty(session.BuildDeviceWrites());
    }

    [Fact]
    public void BuildDeviceWrites_SingleChange_ReturnsOneWrite()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x001E));

        var writes = session.BuildDeviceWrites();
        Assert.Single(writes);
        var write = Assert.IsType<SetKeycodeWrite>(writes[0]);
        Assert.Equal(0, write.Layer);
        Assert.Equal(0, write.Row);
        Assert.Equal(0, write.Col);
        Assert.Equal(0x001E, write.Keycode);
    }

    [Fact]
    public void BuildDeviceWrites_MultipleChanges_ReturnsAllWrites()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x001E));
        session.Apply(new SetKeyOp(1, 1, 0, 0x0028, 0x0029));
        session.Apply(new SetKeyOp(0, 1, 1, 0x0007, 0x0008));

        var writes = session.BuildDeviceWrites();
        Assert.Equal(3, writes.Count);
    }

    [Fact]
    public void BuildDeviceWrites_UndoneChange_NotIncluded()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x001E));
        session.Apply(new SetKeyOp(0, 0, 1, 0x0005, 0x001F));
        session.Undo(); // undo second edit

        var writes = session.BuildDeviceWrites();
        Assert.Single(writes);
        var write = Assert.IsType<SetKeycodeWrite>(writes[0]);
        Assert.Equal(0x001E, write.Keycode);
    }

    [Fact]
    public void BuildDeviceWrites_SameKeyEditedTwice_OnlyFinalValue()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x001E));
        session.Apply(new SetKeyOp(0, 0, 0, 0x001E, 0x001F));

        var writes = session.BuildDeviceWrites();
        Assert.Single(writes); // diffed against baseline, only one key changed
        var write = Assert.IsType<SetKeycodeWrite>(writes[0]);
        Assert.Equal(0x001F, write.Keycode);
    }

    [Fact]
    public void BuildDeviceWrites_RevertedChange_NotIncluded()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x001E));
        session.Apply(new SetKeyOp(0, 0, 0, 0x001E, 0x0004)); // revert to original

        Assert.Empty(session.BuildDeviceWrites());
    }

    // --- Cross-layer edits ---

    [Fact]
    public void Apply_DifferentLayers_TrackedIndependently()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0x001E));
        session.Apply(new SetKeyOp(1, 0, 0, 0x0001, 0x0004));

        Assert.Equal(2, session.PendingChanges.Count);
        Assert.Equal(0x001E, session.GetCurrent(0, 0, 0));
        Assert.Equal(0x0004, session.GetCurrent(1, 0, 0));
    }

    // --- GetCurrent / GetBaseline ---

    [Fact]
    public void GetCurrent_ReturnsBaselineWhenNoEdits()
    {
        var session = new KeymapEditSession(MakeBaseline());
        Assert.Equal(0x0004, session.GetCurrent(0, 0, 0));
        Assert.Equal(0x0028, session.GetCurrent(1, 1, 0));
    }

    [Fact]
    public void GetBaseline_UnchangedByEdits()
    {
        var session = new KeymapEditSession(MakeBaseline());
        session.Apply(new SetKeyOp(0, 0, 0, 0x0004, 0xFFFF));
        Assert.Equal(0x0004, session.GetBaseline(0, 0, 0));
    }
}
