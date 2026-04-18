using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Keymap;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

public class SafetyConfirmViewModelTests
{
    [Fact]
    public void Warnings_AreFormattedFromInput()
    {
        var input = new[]
        {
            new SafetyWarning(SafetyWarningKind.UnreachableLayer, 2, "Layer 2 is not referenced"),
            new SafetyWarning(SafetyWarningKind.BaseLayerUnusable, 0, "Base layer has no usable keys"),
        };

        var vm = new SafetyConfirmViewModel(input);

        Assert.Equal(2, vm.Warnings.Count);
        Assert.Contains("Layer 2", vm.Warnings[0].Display);
        Assert.Contains("Layer 0", vm.Warnings[1].Display);
    }

    [Fact]
    public void ConfirmCommand_InvokesClosedWithTrue()
    {
        var vm = new SafetyConfirmViewModel(new List<SafetyWarning>());
        bool? result = null;
        vm.Closed = r => result = r;

        vm.ConfirmCommand.Execute(null);

        Assert.True(result);
    }

    [Fact]
    public void CancelCommand_InvokesClosedWithFalse()
    {
        var vm = new SafetyConfirmViewModel(new List<SafetyWarning>());
        bool? result = null;
        vm.Closed = r => result = r;

        vm.CancelCommand.Execute(null);

        Assert.False(result);
    }
}
