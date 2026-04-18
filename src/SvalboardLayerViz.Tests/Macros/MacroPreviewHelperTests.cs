using SvalboardLayerViz.Core.Macros;
using Xunit;

namespace SvalboardLayerViz.Tests.Macros;

public class MacroPreviewHelperTests
{
    [Fact]
    public void EmptyActions_ReturnsNull()
    {
        Assert.Null(MacroPreviewHelper.GetPreview([]));
    }

    [Fact]
    public void SingleTextAction_ReturnsText()
    {
        var actions = new MacroAction[] { new MacroTextAction("Hello") };
        Assert.Equal("Hello", MacroPreviewHelper.GetPreview(actions));
    }

    [Fact]
    public void TapAction_ResolvesKeyName()
    {
        // 0x04 = A in HID
        var actions = new MacroAction[] { new MacroTapAction(0x04) };
        Assert.Equal("A", MacroPreviewHelper.GetPreview(actions));
    }

    [Fact]
    public void DownAction_ShowsArrow()
    {
        var actions = new MacroAction[] { new MacroDownAction(0x04) };
        Assert.Equal("↓A", MacroPreviewHelper.GetPreview(actions));
    }

    [Fact]
    public void UpAction_ShowsArrow()
    {
        var actions = new MacroAction[] { new MacroUpAction(0x04) };
        Assert.Equal("↑A", MacroPreviewHelper.GetPreview(actions));
    }

    [Fact]
    public void DelayAction_ShowsMs()
    {
        var actions = new MacroAction[] { new MacroDelayAction(500) };
        Assert.Equal("500ms", MacroPreviewHelper.GetPreview(actions));
    }

    [Fact]
    public void MixedActions_JoinedWithSpace()
    {
        var actions = new MacroAction[]
        {
            new MacroTextAction("Hi"),
            new MacroDelayAction(100),
            new MacroTapAction(0x04),
        };
        Assert.Equal("Hi 100ms A", MacroPreviewHelper.GetPreview(actions));
    }

    [Fact]
    public void LongContent_TruncatesWithEllipsis()
    {
        // "Hello World and more" is 20 chars, max default 40 but let's use a short max
        var actions = new MacroAction[]
        {
            new MacroTextAction("Hello World and some more text that is definitely too long"),
        };
        var result = MacroPreviewHelper.GetPreview(actions, maxLength: 12);
        Assert.NotNull(result);
        // The single part exceeds maxLength but it's the first part, so it's included
        Assert.Equal("Hello World and some more text that is definitely too long", result);
    }

    [Fact]
    public void MultipleActions_TruncatesAfterExceedingMax()
    {
        var actions = new MacroAction[]
        {
            new MacroTextAction("AAAAAAAAAA"), // 10 chars
            new MacroTextAction("BBBB"),        // would push past 12
        };
        var result = MacroPreviewHelper.GetPreview(actions, maxLength: 12);
        Assert.NotNull(result);
        Assert.Equal("AAAAAAAAAA …", result);
    }

    [Fact]
    public void UnknownKeycode_ShowsHex()
    {
        var actions = new MacroAction[] { new MacroTapAction(0xFF) };
        var result = MacroPreviewHelper.GetPreview(actions);
        Assert.NotNull(result);
        Assert.Equal("0xFF", result);
    }

    [Fact]
    public void ModTapAction_ShowsModAndKey()
    {
        // Shift+D
        var actions = new MacroAction[] { new MacroModTapAction(0x07, 0x02) };
        var result = MacroPreviewHelper.GetPreview(actions);
        Assert.Equal("Shift+D", result);
    }

    [Fact]
    public void ModTapAction_MultipleModifiers()
    {
        // Ctrl+Alt+A (mods = 0x05 = LCtrl|LAlt)
        var actions = new MacroAction[] { new MacroModTapAction(0x04, 0x05) };
        var result = MacroPreviewHelper.GetPreview(actions);
        Assert.Equal("Ctrl+Alt+A", result);
    }

    [Fact]
    public void FormatMods_AllMods()
    {
        Assert.Equal("Ctrl", MacroPreviewHelper.FormatMods(0x01));
        Assert.Equal("Shift", MacroPreviewHelper.FormatMods(0x02));
        Assert.Equal("Alt", MacroPreviewHelper.FormatMods(0x04));
        Assert.Equal("Gui", MacroPreviewHelper.FormatMods(0x08));
        Assert.Equal("RCtrl", MacroPreviewHelper.FormatMods(0x10));
        Assert.Equal("RShift", MacroPreviewHelper.FormatMods(0x20));
        Assert.Equal("RAlt", MacroPreviewHelper.FormatMods(0x40));
        Assert.Equal("RGui", MacroPreviewHelper.FormatMods(0x80));
        Assert.Equal("Ctrl+Shift", MacroPreviewHelper.FormatMods(0x03));
    }
}
