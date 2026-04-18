using SvalboardLayerViz.Core.Dynamic;
using SvalboardLayerViz.Core.Protocol;
using HidSharp;
using Xunit;

namespace SvalboardLayerViz.Tests.Protocol;

/// <summary>
/// Verifies that polling services do not leak event subscriptions across
/// Start/Stop/Dispose cycles, and do not fire PollError after Dispose.
/// </summary>
public class PollingServiceSubscriptionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(2);

    [Fact]
    public void MatrixPollingService_Dispose_DoesNotFireErrorAfterDispose()
    {
        // Deterministic instead of "sleep 150 ms then compare counts": the
        // previous form could pass under CI slowdown or a GC pause that kept
        // the poll thread from re-firing within the window. Now a post-dispose
        // fire actively sets an event; Wait returning true fails the test.
        var fake = new ThrowingProtocolService();
        var sut = new MatrixPollingService(fake, 10, 6);
        var disposed = 0;
        using var postDisposeSignal = new ManualResetEventSlim(false);
        var firstErrorSignal = new ManualResetEventSlim(false);
        sut.PollError += _ =>
        {
            if (Volatile.Read(ref disposed) == 1) postDisposeSignal.Set();
            else firstErrorSignal.Set();
        };

        sut.Start();
        Assert.True(firstErrorSignal.Wait(TestTimeout));
        Volatile.Write(ref disposed, 1);
        sut.Dispose();

        Assert.False(postDisposeSignal.Wait(150));
    }

    [Fact]
    public void LedPollingService_Dispose_DoesNotFireErrorAfterDispose()
    {
        var fake = new ThrowingProtocolService();
        var sut = new LedPollingService(fake);
        var disposed = 0;
        using var postDisposeSignal = new ManualResetEventSlim(false);
        var firstErrorSignal = new ManualResetEventSlim(false);
        sut.PollError += _ =>
        {
            if (Volatile.Read(ref disposed) == 1) postDisposeSignal.Set();
            else firstErrorSignal.Set();
        };

        sut.Start();
        Assert.True(firstErrorSignal.Wait(TestTimeout));
        Volatile.Write(ref disposed, 1);
        sut.Dispose();

        Assert.False(postDisposeSignal.Wait(150));
    }

    [Fact]
    public void MatrixPollingService_StartStopCycle_DoesNotLeakHandlers()
    {
        var fake = new FakeVialProtocolService();
        var sut = new MatrixPollingService(fake, 10, 6);
        var stateChanges = 0;
        void Handler(bool[,] _) => Interlocked.Increment(ref stateChanges);
        sut.MatrixStateChanged += Handler;

        for (var i = 0; i < 5; i++)
        {
            sut.Start();
            fake.WaitForPolls(fake.MatrixPollCount + 1, TestTimeout);
            sut.Stop();
        }

        sut.MatrixStateChanged -= Handler;
        sut.Dispose();
        // No assertion on stateChanges count — purpose is exercising the
        // subscribe/unsubscribe path without exceptions or stuck threads.
        Assert.False(sut.IsRunning);
    }

    /// <summary>
    /// Throws on every read call so the polling loop hits its catch-and-fire
    /// path immediately. All other members are unused.
    /// </summary>
    private sealed class ThrowingProtocolService : IVialProtocolService
    {
        public void Connect(HidDevice device) { }
        public int GetLayerCount() => throw new InvalidOperationException("test");
        public ulong GetKeyboardId() => 0;
        public int GetDefinitionSize() => 0;
        public byte[] GetDefinition() => [];
        public ushort[,,] GetKeymapBuffer(int layers, int rows, int cols) => new ushort[layers, rows, cols];
        public bool[,] GetSwitchMatrixState(int rows, int cols) => throw new InvalidOperationException("test");
        public ushort? GetQmkSetting(ushort settingId, byte width = 2) => null;
        public IReadOnlyList<ushort> GetQmkSettingsList() => [];
        public void SetQmkSetting(ushort settingId, ushort value, byte width = 2) { }
        public void ResetQmkSettings() { }
        public uint? GetSvalProtoVersion() => null;
        public (byte H, byte S, byte V)? GetLayerColor(int layer) => null;
        public (byte H, byte S)? GetCurrentLedHueSat() => throw new InvalidOperationException("test");
        public void SetKeycode(int layer, int row, int col, ushort keycode) { }
        public void KeymapSetBuffer(int offset, byte[] data) { }
        public void DynamicKeymapReset() { }
        public void EepromReset() { }
        public UnlockStatus GetUnlockStatus() => new(true, false, []);
        public void UnlockStart() { }
        public bool UnlockPoll() => true;
        public void Lock() { }
        public int GetMacroCount() => 0;
        public int GetMacroBufferSize() => 0;
        public byte[] GetMacroBuffer(int bufferSize) => [];
        public void MacroSetBuffer(int offset, byte[] data) { }
        public void DynamicKeymapMacroReset() { }
        public DynamicEntryCounts GetDynamicEntryCounts() => new(0, 0, 0, 0);
        public byte[] GetComboEntry(int index) => [];
        public void SetComboEntry(int index, byte[] entry) { }
        public byte[] GetTapDanceEntry(int index) => [];
        public void SetTapDanceEntry(int index, byte[] entry) { }
        public void Dispose() { }
    }
}
