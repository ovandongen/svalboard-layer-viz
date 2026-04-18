using SvalboardLayerViz.Core.Keymap;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

public class KeymapEditSession_Macro_Tests
{
    private static ushort[,,] SmallBaseline() => new ushort[2, 10, 6];

    /// <summary>
    /// A trivial 32-byte macro buffer: two macros (one 'a' text + terminator,
    /// one empty + terminator), rest zero-padded.
    /// </summary>
    private static byte[] SampleMacroBuffer()
    {
        var buf = new byte[32];
        buf[0] = (byte)'a'; // macro 0: text 'a'
        buf[1] = 0x00;      // terminator
        buf[2] = 0x00;      // macro 1: empty (terminator only)
        return buf;
    }

    private static byte[] AlteredMacroBuffer()
    {
        var buf = new byte[32];
        buf[0] = (byte)'b'; // changed: text 'b'
        buf[1] = 0x00;
        buf[2] = 0x00;
        return buf;
    }

    // --- Construction ---

    [Fact]
    public void Constructor_WithNoMacros_HasPendingMacroChanges_IsFalse()
    {
        var session = new KeymapEditSession(SmallBaseline());
        Assert.False(session.HasPendingMacroChanges);
    }

    [Fact]
    public void Constructor_WithMacros_StartsClean()
    {
        var session = new KeymapEditSession(SmallBaseline(), null, null, SampleMacroBuffer());
        Assert.False(session.HasPendingMacroChanges);
        Assert.False(session.HasPendingChanges);
    }

    [Fact]
    public void Constructor_ClonesInputBuffer()
    {
        var buf = SampleMacroBuffer();
        var session = new KeymapEditSession(SmallBaseline(), null, null, buf);

        // Mutate the original — session should be unaffected
        buf[0] = 0xFF;
        var baseline = session.GetBaselineMacroBuffer();
        Assert.NotNull(baseline);
        Assert.Equal((byte)'a', baseline![0]);
    }

    // --- Apply / Undo / Redo ---

    [Fact]
    public void Apply_SetMacroBufferOp_MarksDirty()
    {
        var session = new KeymapEditSession(SmallBaseline(), null, null, SampleMacroBuffer());
        session.Apply(new SetMacroBufferOp(SampleMacroBuffer(), AlteredMacroBuffer()));

        Assert.True(session.HasPendingMacroChanges);
        Assert.True(session.HasPendingChanges);
    }

    [Fact]
    public void Apply_ThenUndo_CleansUp()
    {
        var original = SampleMacroBuffer();
        var session = new KeymapEditSession(SmallBaseline(), null, null, original);
        session.Apply(new SetMacroBufferOp(original, AlteredMacroBuffer()));

        session.Undo();

        Assert.False(session.HasPendingMacroChanges);
        var current = session.GetCurrentMacroBuffer();
        Assert.Equal(original, current);
    }

    [Fact]
    public void Apply_Undo_Redo_RestoresDirty()
    {
        var original = SampleMacroBuffer();
        var altered = AlteredMacroBuffer();
        var session = new KeymapEditSession(SmallBaseline(), null, null, original);
        session.Apply(new SetMacroBufferOp(original, altered));

        session.Undo();
        session.Redo();

        Assert.True(session.HasPendingMacroChanges);
        var current = session.GetCurrentMacroBuffer();
        Assert.Equal(altered, current);
    }

    // --- Discard ---

    [Fact]
    public void Discard_ResetsCurrentMacroBufferToBaseline()
    {
        var session = new KeymapEditSession(SmallBaseline(), null, null, SampleMacroBuffer());
        session.Apply(new SetMacroBufferOp(SampleMacroBuffer(), AlteredMacroBuffer()));

        session.Discard();

        Assert.False(session.HasPendingMacroChanges);
        Assert.Equal(SampleMacroBuffer(), session.GetCurrentMacroBuffer());
    }

    // --- BuildDeviceWrites ---

    [Fact]
    public void BuildDeviceWrites_IncludesMacroBufferWrite_WhenMacrosChanged()
    {
        var session = new KeymapEditSession(SmallBaseline(), null, null, SampleMacroBuffer());
        var altered = AlteredMacroBuffer();
        session.Apply(new SetMacroBufferOp(SampleMacroBuffer(), altered));

        var writes = session.BuildDeviceWrites();

        var macroWrite = Assert.Single(writes);
        var mw = Assert.IsType<MacroBufferWrite>(macroWrite);
        Assert.Equal(altered, mw.EncodedBuffer);
    }

    [Fact]
    public void BuildDeviceWrites_OmitsMacroBufferWrite_WhenMacrosUnchanged()
    {
        var session = new KeymapEditSession(SmallBaseline(), null, null, SampleMacroBuffer());

        // Only make a keymap edit
        session.Apply(new SetKeyOp(0, 0, 0, 0, 0x0004));

        var writes = session.BuildDeviceWrites();
        Assert.Single(writes);
        Assert.IsType<SetKeycodeWrite>(writes[0]);
    }

    [Fact]
    public void BuildDeviceWrites_MixedKeycodeAndMacro_EmitsBoth()
    {
        var session = new KeymapEditSession(SmallBaseline(), null, null, SampleMacroBuffer());
        session.Apply(new SetKeyOp(0, 0, 0, 0, 0x0004));
        session.Apply(new SetMacroBufferOp(SampleMacroBuffer(), AlteredMacroBuffer()));

        var writes = session.BuildDeviceWrites();

        Assert.Equal(2, writes.Count);
        Assert.Contains(writes, w => w is SetKeycodeWrite);
        Assert.Contains(writes, w => w is MacroBufferWrite);
    }

    // --- HasPendingChanges integration ---

    [Fact]
    public void HasPendingChanges_TrueWhenOnlyMacrosChanged()
    {
        var session = new KeymapEditSession(SmallBaseline(), null, null, SampleMacroBuffer());
        session.Apply(new SetMacroBufferOp(SampleMacroBuffer(), AlteredMacroBuffer()));

        Assert.True(session.HasPendingChanges);
        Assert.Empty(session.PendingChanges);           // no keymap changes
        Assert.Empty(session.PendingSettingsChanges);    // no settings changes
    }

    // --- Defensive copies ---

    [Fact]
    public void GetCurrentMacroBuffer_ReturnsDefensiveCopy()
    {
        var session = new KeymapEditSession(SmallBaseline(), null, null, SampleMacroBuffer());
        var copy1 = session.GetCurrentMacroBuffer()!;
        copy1[0] = 0xFF; // mutate the returned copy

        var copy2 = session.GetCurrentMacroBuffer()!;
        Assert.Equal((byte)'a', copy2[0]); // session state unchanged
    }
}
