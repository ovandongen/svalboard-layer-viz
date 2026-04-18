using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Dynamic;
using SvalboardLayerViz.Core.Keymap;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

public class ComboEditorViewModelTests
{
    private static byte[] EmptyEntry() => ComboCodec.Encode(Combo.Empty);

    private static IReadOnlyList<byte[]> MakeSlots(int count) =>
        Enumerable.Range(0, count).Select(_ => EmptyEntry()).ToList();

    [Fact]
    public void Constructor_PopulatesRowsFromCombos()
    {
        var vm = new ComboEditorViewModel(
            MakeSlots(4), new KeycodeService(), isEditMode: true, applyComboEdit: null);

        Assert.Equal(4, vm.Rows.Count);
        Assert.All(vm.Rows, r => Assert.True(r.IsEmpty));
        Assert.Equal(0, vm.UsedCount);
    }

    [Fact]
    public void Constructor_WithPopulatedCombo_ShowsLabels()
    {
        var combo = new Combo(0xE0, 0xE1, 0, 0, 0x04); // LCtrl+LShift → KC_A
        var vm = new ComboEditorViewModel(
            [ComboCodec.Encode(combo)], new KeycodeService(), true, null);

        Assert.Single(vm.Rows);
        Assert.False(vm.Rows[0].IsEmpty);
        Assert.NotEqual("—", vm.Rows[0].Input0Label);
        Assert.NotEqual("—", vm.Rows[0].OutputLabel);
    }

    [Fact]
    public async Task EditSlot_InEditMode_UpdatesSlotAndStagesEdit()
    {
        var captured = new List<(int, byte[])>();
        var vm = new ComboEditorViewModel(
            MakeSlots(2), new KeycodeService(), true,
            (idx, bytes) => captured.Add((idx, bytes)));
        vm.RequestKeyPick = (onApply, _) => { onApply(0x04); return Task.CompletedTask; };

        await vm.Rows[0].EditSlotCommand.ExecuteAsync("0");

        Assert.Single(captured);
        Assert.Equal(0, captured[0].Item1);
        var combo = ComboCodec.Decode(captured[0].Item2);
        Assert.Equal(0x04, combo.Input0);
        Assert.Equal(1, vm.UsedCount);
    }

    [Fact]
    public async Task EditSlot_Output_UsesOutputCategories()
    {
        IReadOnlySet<PickerCategory>? seenCategories = null;
        var vm = new ComboEditorViewModel(
            MakeSlots(1), new KeycodeService(), true, (_, _) => { });
        vm.RequestKeyPick = (onApply, cats) =>
        {
            seenCategories = cats;
            onApply(0x5100);
            return Task.CompletedTask;
        };

        await vm.Rows[0].EditSlotCommand.ExecuteAsync("4");

        Assert.NotNull(seenCategories);
        Assert.Contains(PickerCategory.Layer, seenCategories!);
    }

    [Fact]
    public async Task EditSlot_Input_UsesInputCategoriesOnly()
    {
        IReadOnlySet<PickerCategory>? seenCategories = null;
        var vm = new ComboEditorViewModel(
            MakeSlots(1), new KeycodeService(), true, (_, _) => { });
        vm.RequestKeyPick = (onApply, cats) =>
        {
            seenCategories = cats;
            onApply(0x04);
            return Task.CompletedTask;
        };

        await vm.Rows[0].EditSlotCommand.ExecuteAsync("0");

        Assert.NotNull(seenCategories);
        Assert.DoesNotContain(PickerCategory.Layer, seenCategories!);
        Assert.Contains(PickerCategory.Basic, seenCategories!);
    }

    [Fact]
    public async Task EditSlot_ReadOnly_DoesNothing()
    {
        var captured = new List<(int, byte[])>();
        var vm = new ComboEditorViewModel(
            MakeSlots(1), new KeycodeService(), isEditMode: false, (i, b) => captured.Add((i, b)));
        vm.RequestKeyPick = (onApply, _) => { onApply(0x04); return Task.CompletedTask; };

        await vm.Rows[0].EditSlotCommand.ExecuteAsync("0");

        Assert.Empty(captured);
    }

    [Fact]
    public void Clear_ResetsSlotsAndStagesEdit()
    {
        var combo = new Combo(0xE0, 0xE1, 0, 0, 0x04);
        var captured = new List<(int, byte[])>();
        var vm = new ComboEditorViewModel(
            [ComboCodec.Encode(combo)], new KeycodeService(), true,
            (i, b) => captured.Add((i, b)));

        vm.Rows[0].ClearCommand.Execute(null);

        Assert.Single(captured);
        var decoded = ComboCodec.Decode(captured[0].Item2);
        Assert.True(decoded.IsEmpty);
        Assert.True(vm.Rows[0].IsEmpty);
    }

    [Fact]
    public void UsageLabel_ReflectsUsedCount()
    {
        var populated = new Combo(0xE0, 0, 0, 0, 0x04);
        var list = new List<byte[]>
        {
            ComboCodec.Encode(populated),
            EmptyEntry(),
            EmptyEntry(),
        };
        var vm = new ComboEditorViewModel(list, new KeycodeService(), true, null);

        Assert.Equal(1, vm.UsedCount);
        Assert.Contains("1", vm.UsageLabel);
        Assert.Contains("3", vm.UsageLabel);
    }
}
