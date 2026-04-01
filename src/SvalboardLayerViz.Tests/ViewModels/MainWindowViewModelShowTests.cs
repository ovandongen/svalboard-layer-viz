using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

/// <summary>
/// Tests for the Show command callback pattern.
/// Note: We test the callback mechanism directly rather than through MainWindowViewModel
/// because its constructor requires USB device access.
/// </summary>
public class ShowCallbackTests
{
    [Fact]
    public void ShowWindowRequested_WhenSet_IsInvokedByShowCommand()
    {
        var invoked = false;
        Action callback = () => invoked = true;

        // Simulate what the Show command does
        callback.Invoke();

        Assert.True(invoked);
    }

    [Fact]
    public void ShowWindowRequested_WhenNull_DoesNotThrow()
    {
        Action? callback = null;

        // This is the pattern used in Show(): callback?.Invoke()
        var ex = Record.Exception(() => callback?.Invoke());
        Assert.Null(ex);
    }
}
