using SvalboardLayerViz.Core.Keymap;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

public class SaveResultTests
{
    [Fact]
    public void SaveSuccess_ExposesAppliedCount()
    {
        SaveResult r = new SaveSuccess(Applied: 7);
        Assert.Equal(7, r.Applied);
        Assert.IsType<SaveSuccess>(r);
    }

    [Fact]
    public void SaveCancelled_RetainsPartialAppliedCount()
    {
        SaveResult r = new SaveCancelled(Applied: 3);
        Assert.Equal(3, r.Applied);
        Assert.IsType<SaveCancelled>(r);
    }

    [Fact]
    public void SaveAborted_CarriesReasonAndZeroApplied()
    {
        SaveResult r = new SaveAborted("user declined safety warnings");
        Assert.Equal(0, r.Applied);
        var a = Assert.IsType<SaveAborted>(r);
        Assert.Equal("user declined safety warnings", a.Reason);
    }

    [Fact]
    public void SavePartial_CarriesCountsAndRemainingWrites()
    {
        var remaining = new List<SetKeycodeWrite>
        {
            new(Layer: 1, Row: 2, Col: 3, Keycode: 0x0004),
        };
        SaveResult r = new SavePartial(
            Applied: 2,
            StillPending: 1,
            Diverged: 0,
            FailureMessage: "HID timeout",
            Remaining: remaining);

        var p = Assert.IsType<SavePartial>(r);
        Assert.Equal(2, p.Applied);
        Assert.Equal(1, p.StillPending);
        Assert.Equal(0, p.Diverged);
        Assert.Equal("HID timeout", p.FailureMessage);
        Assert.Single(p.Remaining);
    }
}
