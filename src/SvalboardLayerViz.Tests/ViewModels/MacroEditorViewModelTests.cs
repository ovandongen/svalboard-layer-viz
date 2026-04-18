using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Macros;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

public class MacroEditorViewModelTests
{
    private static MacroBuffer MakeBuffer(int macroCount = 4, int capacity = 256)
    {
        var macros = new List<Macro>();
        for (var i = 0; i < macroCount; i++)
            macros.Add(new Macro(i, []));
        return new MacroBuffer(macros, capacity, 0 + macroCount); // each empty macro = 1 terminator byte
    }

    private static MacroBuffer MakeBufferWithActions()
    {
        var macros = new List<Macro>
        {
            new(0, [new MacroTapAction(0x04), new MacroTextAction("hi")]),
            new(1, [new MacroDelayAction(50)]),
            new(2, []),
        };
        return new MacroBuffer(macros, 256, MacroCodec.ComputeEncodedSize(macros));
    }

    // --- UpdateFromBuffer (deferred load) ---

    [Fact]
    public void UpdateFromBuffer_PopulatesEmptyDialog()
    {
        var vm = new MacroEditorViewModel(null, isEditMode: false, applyMacroEdit: null);
        Assert.Empty(vm.MacroSlots);
        Assert.Equal(0, vm.BufferCapacity);

        var buffer = MakeBufferWithActions();
        vm.UpdateFromBuffer(buffer);

        Assert.Equal(3, vm.MacroSlots.Count);
        Assert.Equal(256, vm.BufferCapacity);
        Assert.True(vm.UsedBytes > 0);
    }

    [Fact]
    public void UpdateFromBuffer_ReplacesExistingSlots()
    {
        var vm = new MacroEditorViewModel(MakeBuffer(2), isEditMode: true, applyMacroEdit: null);
        vm.SelectedSlot = vm.MacroSlots[0];
        Assert.Equal(2, vm.MacroSlots.Count);

        vm.UpdateFromBuffer(MakeBufferWithActions());

        Assert.Equal(3, vm.MacroSlots.Count);
        Assert.Null(vm.SelectedSlot); // selection cleared
    }

    // --- Construction ---

    [Fact]
    public void Constructor_WithBuffer_PopulatesSlotsCorrectly()
    {
        var buffer = MakeBuffer(4);
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);

        Assert.Equal(4, vm.MacroSlots.Count);
        Assert.Equal("M0", vm.MacroSlots[0].DisplayIndex);
        Assert.Equal("M3", vm.MacroSlots[3].DisplayIndex);
        Assert.Equal(256, vm.BufferCapacity);
    }

    [Fact]
    public void Constructor_NullBuffer_CreatesEmptyState()
    {
        var vm = new MacroEditorViewModel(null, isEditMode: true, applyMacroEdit: null);

        Assert.Empty(vm.MacroSlots);
        Assert.Equal(0, vm.BufferCapacity);
        Assert.Equal(0, vm.UsedBytes);
    }

    [Fact]
    public void Constructor_WithActions_CalculatesUsedBytes()
    {
        var buffer = MakeBufferWithActions();
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);

        Assert.True(vm.UsedBytes > 0);
        Assert.Equal(buffer.UsedBytes, vm.UsedBytes);
    }

    // --- Slot selection ---

    [Fact]
    public void SelectSlot_PopulatesSelectedActions()
    {
        var buffer = MakeBufferWithActions();
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);

        vm.SelectedSlot = vm.MacroSlots[0]; // has tap + text

        Assert.Equal(2, vm.SelectedActions.Count);
        Assert.Equal("Tap", vm.SelectedActions[0].TypeLabel);
        Assert.Equal("Text", vm.SelectedActions[1].TypeLabel);
    }

    [Fact]
    public void SelectSlot_ChangingSlot_RebuildsActions()
    {
        var buffer = MakeBufferWithActions();
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);

        vm.SelectedSlot = vm.MacroSlots[0];
        Assert.Equal(2, vm.SelectedActions.Count);

        vm.SelectedSlot = vm.MacroSlots[1]; // has delay only
        Assert.Single(vm.SelectedActions);
        Assert.Equal("Delay", vm.SelectedActions[0].TypeLabel);
    }

    [Fact]
    public void SelectSlot_EmptySlot_ShowsNoActions()
    {
        var buffer = MakeBufferWithActions();
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);

        vm.SelectedSlot = vm.MacroSlots[2]; // empty

        Assert.Empty(vm.SelectedActions);
    }

    // --- Add actions ---

    [Fact]
    public async Task AddTapAction_InEditMode_AddsToSlot()
    {
        var buffer = MakeBuffer(2);
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        vm.RequestKeyPick = (onApply, _) => { onApply(0x04); return Task.CompletedTask; }; // KC_A
        vm.SelectedSlot = vm.MacroSlots[0];

        await vm.AddTapActionCommand.ExecuteAsync(null);

        Assert.Single(vm.SelectedActions);
        Assert.Equal("Tap", vm.SelectedActions[0].TypeLabel);
        Assert.Single(vm.MacroSlots[0].Actions);
    }

    [Fact]
    public async Task AddTapAction_CodeOver0xFF_Rejected()
    {
        var buffer = MakeBuffer(2);
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        vm.RequestKeyPick = (onApply, _) => { onApply(0x5100); return Task.CompletedTask; }; // layer-tap, too big
        vm.SelectedSlot = vm.MacroSlots[0];

        await vm.AddTapActionCommand.ExecuteAsync(null);

        Assert.Empty(vm.SelectedActions);
    }

    [Fact]
    public async Task AddDownAction_InEditMode_AddsToSlot()
    {
        var buffer = MakeBuffer(2);
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        vm.RequestKeyPick = (onApply, _) => { onApply(0xE0); return Task.CompletedTask; }; // LCtrl
        vm.SelectedSlot = vm.MacroSlots[0];

        await vm.AddDownActionCommand.ExecuteAsync(null);

        Assert.Single(vm.SelectedActions);
        Assert.Equal("Hold", vm.SelectedActions[0].TypeLabel);
    }

    [Fact]
    public async Task AddUpAction_InEditMode_AddsToSlot()
    {
        var buffer = MakeBuffer(2);
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        vm.RequestKeyPick = (onApply, _) => { onApply(0xE0); return Task.CompletedTask; };
        vm.SelectedSlot = vm.MacroSlots[0];

        await vm.AddUpActionCommand.ExecuteAsync(null);

        Assert.Single(vm.SelectedActions);
        Assert.Equal("Release", vm.SelectedActions[0].TypeLabel);
    }

    [Fact]
    public void AddDelayAction_InEditMode_AddsDefault100ms()
    {
        var buffer = MakeBuffer(2);
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        vm.SelectedSlot = vm.MacroSlots[0];

        vm.AddDelayActionCommand.Execute(null);

        Assert.Single(vm.SelectedActions);
        Assert.Equal("Delay", vm.SelectedActions[0].TypeLabel);
        Assert.Equal(100, vm.SelectedActions[0].DelayMs);
    }

    [Fact]
    public void AddTextAction_InEditMode_AddsEmptyText()
    {
        var buffer = MakeBuffer(2);
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        vm.SelectedSlot = vm.MacroSlots[0];

        vm.AddTextActionCommand.Execute(null);

        Assert.Single(vm.SelectedActions);
        Assert.Equal("Text", vm.SelectedActions[0].TypeLabel);
        Assert.Equal("", vm.SelectedActions[0].TextContent);
    }

    // --- Remove action ---

    [Fact]
    public void RemoveAction_RemovesFromSlotAndRecalculates()
    {
        var buffer = MakeBufferWithActions();
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        vm.SelectedSlot = vm.MacroSlots[0]; // tap + text
        var bytesBefore = vm.UsedBytes;

        vm.SelectedAction = vm.SelectedActions[0]; // tap
        vm.RemoveActionCommand.Execute(null);

        Assert.Single(vm.SelectedActions);
        Assert.Equal("Text", vm.SelectedActions[0].TypeLabel);
        Assert.True(vm.UsedBytes < bytesBefore);
    }

    // --- Move actions ---

    [Fact]
    public void MoveActionUp_SwapsWithPrevious()
    {
        var buffer = MakeBufferWithActions();
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        vm.SelectedSlot = vm.MacroSlots[0]; // tap + text

        vm.SelectedAction = vm.SelectedActions[1]; // text
        vm.MoveActionUpCommand.Execute(null);

        Assert.Equal("Text", vm.SelectedActions[0].TypeLabel);
        Assert.Equal("Tap", vm.SelectedActions[1].TypeLabel);
    }

    [Fact]
    public void MoveActionDown_SwapsWithNext()
    {
        var buffer = MakeBufferWithActions();
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        vm.SelectedSlot = vm.MacroSlots[0]; // tap + text

        vm.SelectedAction = vm.SelectedActions[0]; // tap
        vm.MoveActionDownCommand.Execute(null);

        Assert.Equal("Text", vm.SelectedActions[0].TypeLabel);
        Assert.Equal("Tap", vm.SelectedActions[1].TypeLabel);
    }

    // --- Buffer size recalculation ---

    [Fact]
    public void BufferSize_RecalculatesOnAddAndRemove()
    {
        var buffer = MakeBuffer(2, capacity: 256);
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        vm.SelectedSlot = vm.MacroSlots[0];

        var initialBytes = vm.UsedBytes;
        vm.AddDelayActionCommand.Execute(null);
        Assert.True(vm.UsedBytes > initialBytes);

        vm.SelectedAction = vm.SelectedActions[0];
        vm.RemoveActionCommand.Execute(null);
        Assert.Equal(initialBytes, vm.UsedBytes);
    }

    // --- Full edit→save→reopen cycle ---

    [Fact]
    public async Task FullCycle_EditSaveReopen_DataSurvives()
    {
        // Simulates: open macros → add tap → save dialog → "save to device" → reopen macros
        var originalBuffer = MakeBuffer(4, 256);

        // 1. Open macro editor, add a tap to M0
        byte[]? savedEncoded = null;
        var vm1 = new MacroEditorViewModel(originalBuffer, isEditMode: true,
            applyMacroEdit: b => savedEncoded = b);
        vm1.RequestKeyPick = (onApply, _) => { onApply(0x04); return Task.CompletedTask; };
        vm1.SelectedSlot = vm1.MacroSlots[0];
        await vm1.AddTapActionCommand.ExecuteAsync(null);
        vm1.SaveCommand.Execute(null);
        Assert.NotNull(savedEncoded);

        // 2. Simulate what MainWindowViewModel does after device save:
        //    Decode the written buffer → set as KeyboardConfig.Macros → rebuild session
        var freshMacros = MacroCodec.Decode(savedEncoded!, 4, 256);
        var reEncodedForSession = MacroCodec.Encode(freshMacros);

        // 3. Simulate GetCurrentMacros() — decode session buffer using freshMacros metadata
        var reopenedMacros = MacroCodec.Decode(reEncodedForSession, freshMacros.Macros.Count, freshMacros.BufferCapacity);

        // 4. Open macro editor again with the reopened data
        var vm2 = new MacroEditorViewModel(reopenedMacros, isEditMode: true, applyMacroEdit: null);
        vm2.SelectedSlot = vm2.MacroSlots[0];

        // The tap action should still be there
        Assert.Single(vm2.SelectedActions);
        var tap = Assert.IsType<MacroTapAction>(vm2.SelectedActions[0].Action);
        Assert.Equal(0x04, tap.Keycode);
    }

    // --- Save ---

    [Fact]
    public void Save_EncodesAndCallsApplyCallback()
    {
        byte[]? captured = null;
        var buffer = MakeBufferWithActions();
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: b => captured = b);
        var savedInvoked = false;
        vm.Saved = () => savedInvoked = true;

        vm.SaveCommand.Execute(null);

        Assert.NotNull(captured);
        Assert.Equal(256, captured!.Length); // padded to buffer capacity
        Assert.True(savedInvoked);
    }

    [Fact]
    public void Save_BlockedWhenOverCapacity()
    {
        // Tiny buffer (10 bytes) with content that exceeds it
        var macros = new List<Macro>
        {
            new(0, [new MacroTextAction("this text is longer than ten bytes")])
        };
        var used = MacroCodec.ComputeEncodedSize(macros);
        var buffer = new MacroBuffer(macros, 10, used);

        byte[]? captured = null;
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: b => captured = b);

        Assert.True(vm.IsOverCapacity);
        Assert.False(vm.CanSave);

        vm.SaveCommand.Execute(null);
        Assert.Null(captured);
    }

    // --- Cancel ---

    [Fact]
    public void Cancel_InvokesCancelledWithoutApply()
    {
        byte[]? captured = null;
        var buffer = MakeBufferWithActions();
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: b => captured = b);
        var cancelledInvoked = false;
        vm.Cancelled = () => cancelledInvoked = true;

        vm.CancelCommand.Execute(null);

        Assert.True(cancelledInvoked);
        Assert.Null(captured);
    }

    // --- Read-only mode ---

    [Fact]
    public void ReadOnlyMode_AddDelayDoesNothing()
    {
        var buffer = MakeBuffer(2);
        var vm = new MacroEditorViewModel(buffer, isEditMode: false, applyMacroEdit: null);
        vm.SelectedSlot = vm.MacroSlots[0];

        vm.AddDelayActionCommand.Execute(null);

        Assert.Empty(vm.SelectedActions);
    }

    [Fact]
    public void ReadOnlyMode_CanSaveIsFalse()
    {
        var buffer = MakeBufferWithActions();
        var vm = new MacroEditorViewModel(buffer, isEditMode: false, applyMacroEdit: null);

        Assert.False(vm.CanSave);
    }

    // --- Inline editing triggers recalc ---

    [Fact]
    public void InlineEditDelay_RecalculatesBufferSize()
    {
        var buffer = MakeBufferWithActions();
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        vm.SelectedSlot = vm.MacroSlots[1]; // delay(50)

        var bytesBefore = vm.UsedBytes;
        vm.SelectedActions[0].DelayMs = 9999; // "9999" = 4 digits vs "50" = 2 digits

        Assert.True(vm.UsedBytes > bytesBefore);
    }

    [Fact]
    public void InlineEditText_RecalculatesBufferSize()
    {
        var buffer = MakeBufferWithActions();
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        vm.SelectedSlot = vm.MacroSlots[0]; // tap + text("hi")

        var bytesBefore = vm.UsedBytes;
        vm.SelectedActions[1].TextContent = "hello world"; // longer text

        Assert.True(vm.UsedBytes > bytesBefore);
    }

    // --- UsagePercent ---

    [Fact]
    public void UsagePercent_CalculatedCorrectly()
    {
        var buffer = MakeBuffer(2, capacity: 100);
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);

        // 2 empty macros = 2 terminators = 2 bytes → 2%
        Assert.Equal(2.0, vm.UsagePercent, precision: 1);
    }

    [Fact]
    public void UsagePercent_ZeroCapacity_ReturnsZero()
    {
        var vm = new MacroEditorViewModel(null, isEditMode: true, applyMacroEdit: null);

        Assert.Equal(0.0, vm.UsagePercent);
    }

    // --- Edit key action ---

    [Fact]
    public async Task EditKeyAction_ChangesTapKeycode()
    {
        var buffer = MakeBufferWithActions(); // slot 0: tap(0x04) + text("hi")
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        vm.RequestKeyPick = (onApply, _) => { onApply(0x05); return Task.CompletedTask; }; // KC_B
        vm.SelectedSlot = vm.MacroSlots[0];
        vm.SelectedAction = vm.SelectedActions[0]; // the tap action

        await vm.EditKeyActionCommand.ExecuteAsync(null);

        Assert.Equal(0x05, ((MacroTapAction)vm.SelectedActions[0].Action).Keycode);
        Assert.IsType<MacroTapAction>(vm.MacroSlots[0].Actions[0]); // slot synced
    }

    [Fact]
    public async Task EditKeyAction_NonKeyAction_DoesNothing()
    {
        var buffer = MakeBufferWithActions();
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        vm.RequestKeyPick = (onApply, _) => { onApply(0x05); return Task.CompletedTask; };
        vm.SelectedSlot = vm.MacroSlots[1]; // delay(50)
        vm.SelectedAction = vm.SelectedActions[0]; // the delay action

        await vm.EditKeyActionCommand.ExecuteAsync(null);

        Assert.IsType<MacroDelayAction>(vm.SelectedActions[0].Action); // unchanged
    }

    [Fact]
    public async Task EditKeyAction_ReadOnlyMode_DoesNothing()
    {
        var buffer = MakeBufferWithActions();
        var vm = new MacroEditorViewModel(buffer, isEditMode: false, applyMacroEdit: null);
        vm.RequestKeyPick = (onApply, _) => { onApply(0x05); return Task.CompletedTask; };
        vm.SelectedSlot = vm.MacroSlots[0];
        vm.SelectedAction = vm.SelectedActions[0];

        await vm.EditKeyActionCommand.ExecuteAsync(null);

        Assert.Equal(0x04, ((MacroTapAction)vm.SelectedActions[0].Action).Keycode); // unchanged
    }

    // --- HasSelectedSlot ---

    [Fact]
    public void HasSelectedSlot_FalseInitially_TrueAfterSelection()
    {
        var buffer = MakeBuffer(2);
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);

        Assert.False(vm.HasSelectedSlot);

        vm.SelectedSlot = vm.MacroSlots[0];
        Assert.True(vm.HasSelectedSlot);
    }

    // --- Mod-tap via picker modified keycodes ---

    [Fact]
    public async Task AddTapAction_ModifiedKeycode_CreatesModTap()
    {
        var buffer = MakeBuffer(2);
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        // Picker returns LSFT(KC_D) = 0x0207
        vm.RequestKeyPick = (onApply, _) => { onApply(0x0207); return Task.CompletedTask; };
        vm.SelectedSlot = vm.MacroSlots[0];

        await vm.AddTapActionCommand.ExecuteAsync(null);

        Assert.Single(vm.SelectedActions);
        var mt = Assert.IsType<MacroModTapAction>(vm.SelectedActions[0].Action);
        Assert.Equal(0x07, mt.Keycode); // D
        Assert.Equal(0x02, mt.Mods);    // LShift
    }

    [Fact]
    public async Task AddTapAction_MultiModKeycode_CombinesBitmask()
    {
        var buffer = MakeBuffer(2);
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        // LCTL|LALT + KC_A = (0x01|0x04)<<8 | 0x04 = 0x0504
        vm.RequestKeyPick = (onApply, _) => { onApply(0x0504); return Task.CompletedTask; };
        vm.SelectedSlot = vm.MacroSlots[0];

        await vm.AddTapActionCommand.ExecuteAsync(null);

        var mt = Assert.IsType<MacroModTapAction>(vm.SelectedActions[0].Action);
        Assert.Equal(0x04, mt.Keycode); // A
        Assert.Equal(0x05, mt.Mods);    // LCtrl | LAlt
    }

    [Fact]
    public async Task AddTapAction_PlainKeycode_CreatesPlainTap()
    {
        var buffer = MakeBuffer(2);
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        vm.RequestKeyPick = (onApply, _) => { onApply(0x04); return Task.CompletedTask; };
        vm.SelectedSlot = vm.MacroSlots[0];

        await vm.AddTapActionCommand.ExecuteAsync(null);

        Assert.IsType<MacroTapAction>(vm.SelectedActions[0].Action);
    }

    [Fact]
    public async Task EditKeyAction_ModTapToPlainTap_WhenPlainKeycode()
    {
        var macros = new List<Macro>
        {
            new(0, [new MacroModTapAction(0x07, 0x02)]), // Shift+D
        };
        var buffer = new MacroBuffer(macros, 256, MacroCodec.ComputeEncodedSize(macros));
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        vm.RequestKeyPick = (onApply, _) => { onApply(0x04); return Task.CompletedTask; }; // plain KC_A
        vm.SelectedSlot = vm.MacroSlots[0];
        vm.SelectedAction = vm.SelectedActions[0];

        await vm.EditKeyActionCommand.ExecuteAsync(null);

        Assert.IsType<MacroTapAction>(vm.SelectedActions[0].Action);
    }

    [Fact]
    public async Task AddTapAction_UnsupportedKeycode_Rejected()
    {
        var buffer = MakeBuffer(2);
        var vm = new MacroEditorViewModel(buffer, isEditMode: true, applyMacroEdit: null);
        // Layer-tap keycode — not valid for macro tap
        vm.RequestKeyPick = (onApply, _) => { onApply(0x5100); return Task.CompletedTask; };
        vm.SelectedSlot = vm.MacroSlots[0];

        await vm.AddTapActionCommand.ExecuteAsync(null);

        Assert.Empty(vm.SelectedActions);
    }

    // --- KeycodeToMacroTap unit tests ---

    [Fact]
    public void KeycodeToMacroTap_BasicKey_ReturnsTap()
    {
        var action = MacroEditorViewModel.KeycodeToMacroTap(0x04);
        var tap = Assert.IsType<MacroTapAction>(action);
        Assert.Equal(0x04, tap.Keycode);
    }

    [Fact]
    public void KeycodeToMacroTap_ModifiedKey_ReturnsModTap()
    {
        var action = MacroEditorViewModel.KeycodeToMacroTap(0x0207); // LSFT(KC_D)
        var mt = Assert.IsType<MacroModTapAction>(action);
        Assert.Equal(0x07, mt.Keycode);
        Assert.Equal(0x02, mt.Mods);
    }

    [Fact]
    public void KeycodeToMacroTap_OutOfRange_ReturnsNull()
    {
        Assert.Null(MacroEditorViewModel.KeycodeToMacroTap(0x5100));
    }
}
