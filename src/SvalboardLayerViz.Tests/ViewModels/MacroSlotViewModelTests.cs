using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Macros;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

public class MacroSlotViewModelTests
{
    [Fact]
    public void EmptyMacro_PreviewIsEmpty()
    {
        var vm = new MacroSlotViewModel(new Macro(0, []));

        Assert.Equal("(empty)", vm.Preview);
    }

    [Fact]
    public void DisplayIndex_FormattedCorrectly()
    {
        var vm = new MacroSlotViewModel(new Macro(3, []));

        Assert.Equal("M3", vm.DisplayIndex);
        Assert.Equal(3, vm.Index);
    }

    [Fact]
    public void TextAction_PreviewShowsText()
    {
        var vm = new MacroSlotViewModel(new Macro(0, [new MacroTextAction("hello")]));

        Assert.Equal("hello", vm.Preview);
    }

    [Fact]
    public void TapAction_PreviewShowsKeyName()
    {
        var vm = new MacroSlotViewModel(new Macro(0, [new MacroTapAction(0x04)])); // KC_A

        Assert.Contains("A", vm.Preview);
    }

    [Fact]
    public void DownAction_PreviewShowsArrowAndKeyName()
    {
        var vm = new MacroSlotViewModel(new Macro(0, [new MacroDownAction(0xE0)])); // LCtrl

        Assert.Contains("↓", vm.Preview);
    }

    [Fact]
    public void DelayAction_PreviewShowsMs()
    {
        var vm = new MacroSlotViewModel(new Macro(0, [new MacroDelayAction(200)]));

        Assert.Contains("200ms", vm.Preview);
    }

    [Fact]
    public void MixedActions_PreviewJoinedWithSpaces()
    {
        var vm = new MacroSlotViewModel(new Macro(0, [
            new MacroDownAction(0xE0), // LCtrl
            new MacroTapAction(0x06),  // KC_C
            new MacroUpAction(0xE0),
        ]));

        // Should contain parts separated by spaces
        Assert.Contains(" ", vm.Preview);
    }

    [Fact]
    public void LongPreview_TruncatedWithEllipsis()
    {
        // Create many text actions to exceed 40 chars
        var vm = new MacroSlotViewModel(new Macro(0, [
            new MacroTextAction("ABCDEFGHIJKLMNOPQRSTUVWXYZ"),
            new MacroTextAction("ABCDEFGHIJKLMNOPQRSTUVWXYZ"),
        ]));

        Assert.Contains("…", vm.Preview);
    }

    [Fact]
    public void AddingAction_RefreshesPreview()
    {
        var vm = new MacroSlotViewModel(new Macro(0, []));
        Assert.Equal("(empty)", vm.Preview);

        vm.Actions.Add(new MacroTapAction(0x04));

        Assert.NotEqual("(empty)", vm.Preview);
    }

    [Fact]
    public void RemovingAction_RefreshesPreview()
    {
        var vm = new MacroSlotViewModel(new Macro(0, [new MacroTapAction(0x04)]));
        Assert.NotEqual("(empty)", vm.Preview);

        vm.Actions.RemoveAt(0);

        Assert.Equal("(empty)", vm.Preview);
    }

    [Fact]
    public void ConstructorFromIndex_CreatesEmptySlot()
    {
        var vm = new MacroSlotViewModel(5);

        Assert.Equal(5, vm.Index);
        Assert.Equal("M5", vm.DisplayIndex);
        Assert.Empty(vm.Actions);
        Assert.Equal("(empty)", vm.Preview);
    }
}
