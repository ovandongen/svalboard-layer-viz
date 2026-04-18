using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Protocol;
using SvalboardLayerViz.Tests.Protocol;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

public class UnlockFlowTests
{
    private readonly FakeVialProtocolService _fake = new();
    private ManualTimer? _lastTimer;

    private UnlockDialogViewModel CreateVm()
    {
        return new UnlockDialogViewModel(_fake, TimerFactory);
    }

    private IDisposable TimerFactory(TimeSpan interval, Action tick)
    {
        _lastTimer = new ManualTimer(tick);
        return _lastTimer;
    }

    // --- Already unlocked ---

    [Fact]
    public async Task AlreadyUnlocked_SetsIsUnlockedAndDoesNotPoll()
    {
        _fake.NextUnlockStatus = new UnlockStatus(true, false, []);
        var vm = CreateVm();

        await vm.BeginAsync();

        Assert.True(vm.IsUnlocked);
        Assert.False(vm.IsPolling);
        Assert.Equal(0, _fake.UnlockStartCount);
        Assert.Equal(0, _fake.UnlockPollCount);
    }

    [Fact]
    public async Task AlreadyUnlocked_FiresUnlockCompletedCallback()
    {
        _fake.NextUnlockStatus = new UnlockStatus(true, false, []);
        var vm = CreateVm();
        var callbackFired = false;
        vm.UnlockCompleted = () => callbackFired = true;

        await vm.BeginAsync();

        Assert.True(callbackFired);
    }

    // --- Not unlocked — starts polling ---

    [Fact]
    public async Task NotUnlocked_CallsUnlockStartAndBeginsPolling()
    {
        _fake.NextUnlockStatus = new UnlockStatus(false, false, [(1, 0), (6, 0)]);
        _fake.UnlockPollResult = false;
        var vm = CreateVm();

        await vm.BeginAsync();

        Assert.False(vm.IsUnlocked);
        Assert.True(vm.IsPolling);
        Assert.Equal(1, _fake.UnlockStartCount);
        Assert.NotNull(_lastTimer);
    }

    [Fact]
    public async Task NotUnlocked_BuildsMiniBoard()
    {
        _fake.NextUnlockStatus = new UnlockStatus(false, false, [(2, 1)]);
        _fake.UnlockPollResult = false;
        var vm = CreateVm();

        await vm.BeginAsync();

        // Should have all key positions from SvalboardLayout
        var allPositions = SvalboardLayout.GetKeyPositions();
        Assert.Equal(allPositions.Count, vm.AllKeyPositions.Count);

        // Exactly one key should be highlighted (row 2, col 1 = L-Middle East)
        var highlighted = vm.AllKeyPositions.Where(k => k.IsHighlighted).ToList();
        Assert.Single(highlighted);
        Assert.Contains("L-Middle", highlighted[0].Label);
    }

    // --- Polling transitions to unlocked ---

    [Fact]
    public async Task PollTransitionsToUnlocked()
    {
        _fake.NextUnlockStatus = new UnlockStatus(false, false, [(1, 0)]);
        _fake.UnlockPollResult = false;
        var vm = CreateVm();
        var callbackFired = false;
        vm.UnlockCompleted = () => callbackFired = true;

        await vm.BeginAsync();
        Assert.False(vm.IsUnlocked);

        // Tick a few times while still locked
        _lastTimer!.Tick();
        await Task.Delay(10); // let async poll complete
        Assert.Equal(1, _fake.UnlockPollCount);
        Assert.False(vm.IsUnlocked);

        // Now unlock succeeds
        _fake.UnlockPollResult = true;
        _lastTimer.Tick();
        await Task.Delay(10);

        Assert.True(vm.IsUnlocked);
        Assert.False(vm.IsPolling);
        Assert.True(callbackFired);
    }

    // --- Cancel ---

    [Fact]
    public async Task Cancel_StopsPollingAndFiresCallback()
    {
        _fake.NextUnlockStatus = new UnlockStatus(false, false, [(1, 0)]);
        _fake.UnlockPollResult = false;
        var vm = CreateVm();
        var cancelFired = false;
        vm.Cancelled = () => cancelFired = true;

        await vm.BeginAsync();
        Assert.True(vm.IsPolling);

        vm.CancelCommand.Execute(null);

        Assert.False(vm.IsPolling);
        Assert.False(vm.IsUnlocked);
        Assert.True(cancelFired);
        Assert.True(_lastTimer!.IsDisposed);
    }

    // --- Keys-to-hold mapping ---

    [Fact]
    public async Task KeysToHold_MultipleKeys_AllHighlighted()
    {
        // L-Middle East (2,1) and R-Index North (6,3)
        _fake.NextUnlockStatus = new UnlockStatus(false, false, [(2, 1), (6, 3)]);
        _fake.UnlockPollResult = false;
        var vm = CreateVm();

        await vm.BeginAsync();

        var highlighted = vm.AllKeyPositions.Where(k => k.IsHighlighted).ToList();
        Assert.Equal(2, highlighted.Count);
    }

    // --- Status text ---

    [Fact]
    public async Task StatusText_UpdatesOnUnlock()
    {
        _fake.NextUnlockStatus = new UnlockStatus(true, false, []);
        var vm = CreateVm();

        await vm.BeginAsync();

        Assert.Contains("unlocked", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StatusText_ShowsHoldKeysWhilePolling()
    {
        _fake.NextUnlockStatus = new UnlockStatus(false, false, [(1, 0)]);
        _fake.UnlockPollResult = false;
        var vm = CreateVm();

        await vm.BeginAsync();

        Assert.Contains("Hold", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test-only timer that can be ticked manually.
    /// </summary>
    private sealed class ManualTimer : IDisposable
    {
        private readonly Action _tick;

        public bool IsDisposed { get; private set; }

        public ManualTimer(Action tick)
        {
            _tick = tick;
        }

        public void Tick()
        {
            if (!IsDisposed) _tick();
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
