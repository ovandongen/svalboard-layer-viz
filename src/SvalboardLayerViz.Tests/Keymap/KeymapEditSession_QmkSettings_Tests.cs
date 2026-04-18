using SvalboardLayerViz.Core.Keymap;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

public class KeymapEditSession_QmkSettings_Tests
{
    private static ushort[,,] SmallBaseline() => new ushort[2, 10, 6];

    private static Dictionary<ushort, ushort> SampleSettings() => new()
    {
        [0x0007] = 200,  // tapping term
        [0x0009] = 0,    // permissive hold
    };

    // --- Construction ---

    [Fact]
    public void Constructor_WithSettings_NoPendingSettingsChanges()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        Assert.False(session.HasPendingSettingsChanges);
        Assert.Empty(session.PendingSettingsChanges);
    }

    [Fact]
    public void Constructor_WithoutSettings_SettingsAccessorsReturnNull()
    {
        var session = new KeymapEditSession(SmallBaseline());
        Assert.Null(session.GetCurrentSetting(0x0007));
        Assert.Null(session.GetBaselineSetting(0x0007));
        Assert.False(session.HasPendingSettingsChanges);
    }

    [Fact]
    public void Constructor_DefaultOverload_NoSettings()
    {
        var session = new KeymapEditSession(SmallBaseline());
        Assert.Empty(session.PendingSettingsChanges);
        Assert.False(session.HasPendingChanges);
    }

    [Fact]
    public void Constructor_WithSettings_AccessorsReturnValues()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        Assert.Equal((ushort)200, session.GetCurrentSetting(0x0007));
        Assert.Equal((ushort)200, session.GetBaselineSetting(0x0007));
        Assert.Equal((ushort)0, session.GetCurrentSetting(0x0009));
    }

    // --- Apply / Undo / Redo ---

    [Fact]
    public void Apply_SetQmkSettingOp_UpdatesCurrentSetting()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        session.Apply(new SetQmkSettingOp(0x0007, 200, 300));
        Assert.Equal((ushort)300, session.GetCurrentSetting(0x0007));
        Assert.Equal((ushort)200, session.GetBaselineSetting(0x0007));
    }

    [Fact]
    public void Apply_SetQmkSettingOp_MarksHasPendingChanges()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        Assert.False(session.HasPendingChanges);
        session.Apply(new SetQmkSettingOp(0x0007, 200, 300));
        Assert.True(session.HasPendingChanges);
        Assert.True(session.HasPendingSettingsChanges);
    }

    [Fact]
    public void Apply_SetQmkSettingOp_AppearsInPendingSettingsChanges()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        session.Apply(new SetQmkSettingOp(0x0007, 200, 300));
        var pending = session.PendingSettingsChanges;
        Assert.Single(pending);
        Assert.Equal((ushort)0x0007, pending[0].SettingId);
        Assert.Equal((ushort)200, pending[0].OldValue);
        Assert.Equal((ushort)300, pending[0].NewValue);
    }

    [Fact]
    public void Undo_SetQmkSettingOp_RestoresPreviousValue()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        session.Apply(new SetQmkSettingOp(0x0007, 200, 300));
        session.Undo();
        Assert.Equal((ushort)200, session.GetCurrentSetting(0x0007));
        Assert.False(session.HasPendingSettingsChanges);
    }

    [Fact]
    public void Redo_SetQmkSettingOp_ReappliesValue()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        session.Apply(new SetQmkSettingOp(0x0007, 200, 300));
        session.Undo();
        session.Redo();
        Assert.Equal((ushort)300, session.GetCurrentSetting(0x0007));
        Assert.True(session.HasPendingSettingsChanges);
    }

    [Fact]
    public void Apply_RevertToBaseline_NoPendingSettingsChanges()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        session.Apply(new SetQmkSettingOp(0x0007, 200, 300));
        session.Apply(new SetQmkSettingOp(0x0007, 300, 200));
        Assert.False(session.HasPendingSettingsChanges);
        Assert.Empty(session.PendingSettingsChanges);
    }

    // --- Mixed keymap + settings ---

    [Fact]
    public void MixedEdits_KeymapAndSettings_BothTrackedInHasPendingChanges()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        session.Apply(new SetKeyOp(0, 0, 0, 0, 0x04));
        session.Apply(new SetQmkSettingOp(0x0007, 200, 300));
        Assert.True(session.HasPendingChanges);
        Assert.Single(session.PendingChanges);
        Assert.Single(session.PendingSettingsChanges);
    }

    [Fact]
    public void MixedEdits_UndoSettings_KeymapStillPending()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        session.Apply(new SetKeyOp(0, 0, 0, 0, 0x04));
        session.Apply(new SetQmkSettingOp(0x0007, 200, 300));
        session.Undo(); // undoes settings op
        Assert.True(session.HasPendingChanges);
        Assert.Single(session.PendingChanges);
        Assert.False(session.HasPendingSettingsChanges);
    }

    [Fact]
    public void MixedEdits_UndoKeymap_SettingsStillPending()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        session.Apply(new SetQmkSettingOp(0x0007, 200, 300));
        session.Apply(new SetKeyOp(0, 0, 0, 0, 0x04));
        session.Undo(); // undoes key op
        Assert.True(session.HasPendingChanges);
        Assert.Empty(session.PendingChanges);
        Assert.True(session.HasPendingSettingsChanges);
    }

    // --- BuildDeviceWrites ---

    [Fact]
    public void BuildDeviceWrites_SettingsOnly_ReturnsSetQmkSettingWrites()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        session.Apply(new SetQmkSettingOp(0x0007, 200, 300));
        var writes = session.BuildDeviceWrites();
        var settingWrite = Assert.Single(writes);
        var sw = Assert.IsType<SetQmkSettingWrite>(settingWrite);
        Assert.Equal((ushort)0x0007, sw.SettingId);
        Assert.Equal((ushort)300, sw.Value);
    }

    [Fact]
    public void BuildDeviceWrites_Mixed_ReturnsBothTypes()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        session.Apply(new SetKeyOp(0, 0, 0, 0, 0x04));
        session.Apply(new SetQmkSettingOp(0x0007, 200, 300));
        var writes = session.BuildDeviceWrites();
        Assert.Equal(2, writes.Count);
        Assert.IsType<SetKeycodeWrite>(writes[0]);
        Assert.IsType<SetQmkSettingWrite>(writes[1]);
    }

    [Fact]
    public void BuildDeviceWrites_NoChanges_WithSettings_ReturnsEmpty()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        Assert.Empty(session.BuildDeviceWrites());
    }

    // --- Discard ---

    [Fact]
    public void Discard_ResetsSettingsToBaseline()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        session.Apply(new SetQmkSettingOp(0x0007, 200, 300));
        session.Apply(new SetQmkSettingOp(0x0009, 0, 1));
        session.Discard();
        Assert.Equal((ushort)200, session.GetCurrentSetting(0x0007));
        Assert.Equal((ushort)0, session.GetCurrentSetting(0x0009));
        Assert.False(session.HasPendingSettingsChanges);
    }

    [Fact]
    public void Discard_ClearsUndoRedoForSettingsOps()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        session.Apply(new SetQmkSettingOp(0x0007, 200, 300));
        session.Discard();
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    // --- Undo/Redo stack interactions ---

    [Fact]
    public void UndoCount_IncludesSettingsOps()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        session.Apply(new SetKeyOp(0, 0, 0, 0, 0x04));
        session.Apply(new SetQmkSettingOp(0x0007, 200, 300));
        Assert.Equal(2, session.UndoCount);
    }

    [Fact]
    public void Apply_SettingsOp_ClearsRedoStack()
    {
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        session.Apply(new SetQmkSettingOp(0x0007, 200, 300));
        session.Undo();
        Assert.Equal(1, session.RedoCount);
        session.Apply(new SetQmkSettingOp(0x0009, 0, 1));
        Assert.Equal(0, session.RedoCount);
    }

    [Fact]
    public void Apply_SetQmkSettingOp_UnknownSettingId_Throws()
    {
        // Dictionary indexer-assignment would silently create a new entry for
        // an unknown ID, masking firmware-protocol bugs and typos. Validation
        // turns it into a loud InvalidOperationException naming the bad ID.
        var session = new KeymapEditSession(SmallBaseline(), SampleSettings());
        var ex = Assert.Throws<InvalidOperationException>(
            () => session.Apply(new SetQmkSettingOp(0x00FE, 0, 1)));
        Assert.Contains("0x00FE", ex.Message);
        Assert.Contains("SetQmkSettingOp", ex.Message);
    }
}
