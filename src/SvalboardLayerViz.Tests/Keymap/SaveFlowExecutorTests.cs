using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Tests.Protocol;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

public class SaveFlowExecutorTests
{
    private static readonly SetKeycodeWrite W1 = new(Layer: 0, Row: 1, Col: 2, Keycode: 0x0004);
    private static readonly SetKeycodeWrite W2 = new(Layer: 0, Row: 3, Col: 4, Keycode: 0x0005);
    private static readonly SetKeycodeWrite W3 = new(Layer: 1, Row: 5, Col: 0, Keycode: 0x0006);

    [Fact]
    public void Execute_EmptyList_ReturnsEmptyOutcome()
    {
        var fake = new FakeVialProtocolService();

        var outcome = SaveFlowExecutor.Execute(fake, [], CancellationToken.None);

        Assert.Empty(outcome.Applied);
        Assert.Null(outcome.FailedAt);
        Assert.Null(outcome.Failure);
        Assert.False(outcome.Cancelled);
        Assert.Empty(fake.SetKeycodeCalls);
    }

    [Fact]
    public void Execute_HappyPath_WritesAllInOrder()
    {
        var fake = new FakeVialProtocolService();
        var writes = new[] { W1, W2, W3 };

        var outcome = SaveFlowExecutor.Execute(fake, writes, CancellationToken.None);

        Assert.Equal(3, outcome.Applied.Count);
        Assert.Null(outcome.FailedAt);
        Assert.Null(outcome.Failure);
        Assert.False(outcome.Cancelled);

        Assert.Equal(3, fake.SetKeycodeCalls.Count);
        Assert.Equal((W1.Layer, W1.Row, W1.Col, W1.Keycode),
            (fake.SetKeycodeCalls[0].Layer, fake.SetKeycodeCalls[0].Row, fake.SetKeycodeCalls[0].Col, fake.SetKeycodeCalls[0].Keycode));
        Assert.Equal(W3.Keycode, fake.SetKeycodeCalls[2].Keycode);
    }

    [Fact]
    public void Execute_FailureOnFirstCall_ReportsFailedAtAndNoApplied()
    {
        var fake = new FakeVialProtocolService
        {
            SetKeycodeFailAt = (0, new IOException("HID timeout")),
        };
        var writes = new[] { W1, W2, W3 };

        var outcome = SaveFlowExecutor.Execute(fake, writes, CancellationToken.None);

        Assert.Empty(outcome.Applied);
        Assert.Equal(W1, outcome.FailedAt);
        Assert.IsType<IOException>(outcome.Failure);
        Assert.False(outcome.Cancelled);
        Assert.Empty(fake.SetKeycodeCalls);
    }

    [Fact]
    public void Execute_FailureOnMiddleCall_ReportsAppliedBeforeFailure()
    {
        var fake = new FakeVialProtocolService
        {
            SetKeycodeFailAt = (1, new InvalidOperationException("boom")),
        };
        var writes = new[] { W1, W2, W3 };

        var outcome = SaveFlowExecutor.Execute(fake, writes, CancellationToken.None);

        Assert.Single(outcome.Applied);
        Assert.Equal(W1, outcome.Applied[0]);
        Assert.Equal(W2, outcome.FailedAt);
        Assert.IsType<InvalidOperationException>(outcome.Failure);
        Assert.False(outcome.Cancelled);
        Assert.Single(fake.SetKeycodeCalls); // only W1 landed
    }

    [Fact]
    public void Execute_FailureOnLastCall_ReportsNMinusOneApplied()
    {
        var fake = new FakeVialProtocolService
        {
            SetKeycodeFailAt = (2, new IOException("late failure")),
        };
        var writes = new[] { W1, W2, W3 };

        var outcome = SaveFlowExecutor.Execute(fake, writes, CancellationToken.None);

        Assert.Equal(2, outcome.Applied.Count);
        Assert.Equal(W3, outcome.FailedAt);
        Assert.NotNull(outcome.Failure);
    }

    [Fact]
    public void Execute_PrecancelledToken_PerformsNoWrites()
    {
        var fake = new FakeVialProtocolService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var outcome = SaveFlowExecutor.Execute(fake, [W1, W2], cts.Token);

        Assert.True(outcome.Cancelled);
        Assert.Empty(outcome.Applied);
        Assert.Null(outcome.FailedAt);
        Assert.Empty(fake.SetKeycodeCalls);
    }

    [Fact]
    public void Execute_PropagatesKeycodeCorrectlyIntoFakeKeymap()
    {
        // Verifies the fake's round-trip: SetKeycode should mutate its Keymap
        // so a subsequent re-read reflects the writes.
        var fake = new FakeVialProtocolService
        {
            Keymap = new ushort[2, 10, 6],
        };

        var outcome = SaveFlowExecutor.Execute(fake, [W1, W3], CancellationToken.None);

        Assert.Equal(2, outcome.Applied.Count);
        Assert.Equal(W1.Keycode, fake.Keymap[W1.Layer, W1.Row, W1.Col]);
        Assert.Equal(W3.Keycode, fake.Keymap[W3.Layer, W3.Row, W3.Col]);
    }

    [Fact]
    public void Execute_StopsAfterFailureAndDoesNotAttemptRemaining()
    {
        var fake = new FakeVialProtocolService
        {
            SetKeycodeFailAt = (0, new IOException("nope")),
        };

        var outcome = SaveFlowExecutor.Execute(fake, [W1, W2, W3], CancellationToken.None);

        Assert.Empty(outcome.Applied);
        Assert.Equal(W1, outcome.FailedAt);
        Assert.Empty(fake.SetKeycodeCalls); // nothing after failure
    }

    // --- QMK settings writes ---

    private static readonly SetQmkSettingWrite S1 = new(SettingId: 0x0007, Value: 300);
    private static readonly SetQmkSettingWrite S2 = new(SettingId: 0x0009, Value: 1);

    [Fact]
    public void Execute_SettingsOnly_WritesAllSettings()
    {
        var fake = new FakeVialProtocolService();

        var outcome = SaveFlowExecutor.Execute(fake, [S1, S2], CancellationToken.None);

        Assert.Equal(2, outcome.Applied.Count);
        Assert.Null(outcome.FailedAt);
        Assert.Equal(2, fake.SetQmkSettingCalls.Count);
        Assert.Equal(S1.SettingId, fake.SetQmkSettingCalls[0].SettingId);
        Assert.Equal(S1.Value, fake.SetQmkSettingCalls[0].Value);
        Assert.Equal(S2.SettingId, fake.SetQmkSettingCalls[1].SettingId);
    }

    [Fact]
    public void Execute_MixedWrites_KeycodesAndSettings_InOrder()
    {
        var fake = new FakeVialProtocolService();
        DeviceWrite[] writes = [W1, S1, W2, S2];

        var outcome = SaveFlowExecutor.Execute(fake, writes, CancellationToken.None);

        Assert.Equal(4, outcome.Applied.Count);
        Assert.Equal(W1, outcome.Applied[0]);
        Assert.Equal(S1, outcome.Applied[1]);
        Assert.Equal(W2, outcome.Applied[2]);
        Assert.Equal(S2, outcome.Applied[3]);
        Assert.Equal(2, fake.SetKeycodeCalls.Count);
        Assert.Equal(2, fake.SetQmkSettingCalls.Count);
    }

    [Fact]
    public void Execute_SettingsFailure_ReportsFailedAt()
    {
        var fake = new FakeVialProtocolService
        {
            SetQmkSettingFailAt = (0, new IOException("settings HID timeout")),
        };

        var outcome = SaveFlowExecutor.Execute(fake, [S1, S2], CancellationToken.None);

        Assert.Empty(outcome.Applied);
        Assert.Equal(S1, outcome.FailedAt);
        Assert.IsType<IOException>(outcome.Failure);
        Assert.Empty(fake.SetQmkSettingCalls);
    }

    [Fact]
    public void Execute_MixedWrites_FailureMidSettings_ReportsPartialApplied()
    {
        var fake = new FakeVialProtocolService
        {
            SetQmkSettingFailAt = (0, new InvalidOperationException("boom")),
        };
        DeviceWrite[] writes = [W1, W2, S1, S2];

        var outcome = SaveFlowExecutor.Execute(fake, writes, CancellationToken.None);

        Assert.Equal(2, outcome.Applied.Count);
        Assert.Equal(W1, outcome.Applied[0]);
        Assert.Equal(W2, outcome.Applied[1]);
        Assert.Equal(S1, outcome.FailedAt);
        Assert.Equal(2, fake.SetKeycodeCalls.Count);
        Assert.Empty(fake.SetQmkSettingCalls);
    }
}
