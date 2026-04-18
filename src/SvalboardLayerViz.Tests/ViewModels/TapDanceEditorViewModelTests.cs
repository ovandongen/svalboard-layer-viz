using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Dynamic;
using SvalboardLayerViz.Core.Keymap;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

public class TapDanceEditorViewModelTests
{
    private static byte[] EmptyEntry() => TapDanceCodec.Encode(TapDance.Empty);

    private static IReadOnlyList<byte[]> MakeSlots(int count) =>
        Enumerable.Range(0, count).Select(_ => EmptyEntry()).ToList();

    [Fact]
    public void Constructor_PopulatesRowsAndDefaultTerm()
    {
        var vm = new TapDanceEditorViewModel(
            MakeSlots(3), new KeycodeService(), true, null);

        Assert.Equal(3, vm.Rows.Count);
        Assert.All(vm.Rows, r =>
        {
            Assert.True(r.IsEmpty);
            Assert.Equal(TapDance.DefaultTappingTerm, r.TappingTerm);
        });
    }

    [Fact]
    public async Task EditSlot_StagesEditWithNewKeycode()
    {
        var captured = new List<(int, byte[])>();
        var vm = new TapDanceEditorViewModel(
            MakeSlots(2), new KeycodeService(), true, (i, b) => captured.Add((i, b)));
        vm.RequestKeyPick = (onApply, _) => { onApply(0x04); return Task.CompletedTask; };

        await vm.Rows[0].EditSlotCommand.ExecuteAsync("0");

        Assert.Single(captured);
        var td = TapDanceCodec.Decode(captured[0].Item2);
        Assert.Equal(0x04, td.OnTap);
    }

    [Fact]
    public void TappingTerm_ChangeStagesEdit()
    {
        var captured = new List<(int, byte[])>();
        var vm = new TapDanceEditorViewModel(
            MakeSlots(1), new KeycodeService(), true, (i, b) => captured.Add((i, b)));

        vm.Rows[0].TappingTerm = 300;

        Assert.Single(captured);
        var td = TapDanceCodec.Decode(captured[0].Item2);
        Assert.Equal(300, td.TappingTerm);
    }

    [Fact]
    public void TappingTerm_InitialLoad_DoesNotFireEdit()
    {
        var captured = new List<(int, byte[])>();
        var entry = TapDanceCodec.Encode(new TapDance(0, 0, 0, 0, 175));
        _ = new TapDanceEditorViewModel(
            [entry], new KeycodeService(), true, (i, b) => captured.Add((i, b)));

        Assert.Empty(captured);
    }

    [Fact]
    public async Task EditSlot_ReadOnly_DoesNothing()
    {
        var captured = new List<(int, byte[])>();
        var vm = new TapDanceEditorViewModel(
            MakeSlots(1), new KeycodeService(), false, (i, b) => captured.Add((i, b)));
        vm.RequestKeyPick = (onApply, _) => { onApply(0x04); return Task.CompletedTask; };

        await vm.Rows[0].EditSlotCommand.ExecuteAsync("0");

        Assert.Empty(captured);
    }

    [Fact]
    public void Clear_ResetsSlotsAndDefaultsTerm()
    {
        var entry = TapDanceCodec.Encode(new TapDance(0x04, 0xE0, 0x05, 0x06, 150));
        var captured = new List<(int, byte[])>();
        var vm = new TapDanceEditorViewModel(
            [entry], new KeycodeService(), true, (i, b) => captured.Add((i, b)));

        vm.Rows[0].ClearCommand.Execute(null);

        Assert.NotEmpty(captured);
        var last = TapDanceCodec.Decode(captured[^1].Item2);
        Assert.True(last.IsEmpty);
        Assert.Equal(TapDance.DefaultTappingTerm, last.TappingTerm);
    }

    [Fact]
    public void UsageLabel_ReflectsUsedCount()
    {
        var populated = TapDanceCodec.Encode(new TapDance(0x04, 0, 0, 0, 200));
        var list = new List<byte[]> { populated, EmptyEntry() };
        var vm = new TapDanceEditorViewModel(list, new KeycodeService(), true, null);

        Assert.Equal(1, vm.UsedCount);
        Assert.Contains("1", vm.UsageLabel);
        Assert.Contains("2", vm.UsageLabel);
    }

    [Fact]
    public void DisplayIndex_FormatsAsTDN()
    {
        var vm = new TapDanceEditorViewModel(
            MakeSlots(3), new KeycodeService(), true, null);

        Assert.Equal("TD0", vm.Rows[0].DisplayIndex);
        Assert.Equal("TD2", vm.Rows[2].DisplayIndex);
    }
}
