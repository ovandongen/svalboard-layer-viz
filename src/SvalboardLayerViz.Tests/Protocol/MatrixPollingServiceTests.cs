using SvalboardLayerViz.Core.Protocol;
using HidSharp;
using Xunit;

namespace SvalboardLayerViz.Tests.Protocol;

/// <summary>
/// Fake protocol service for testing matrix polling without a real HID device.
/// </summary>
public class FakeVialProtocolService : IVialProtocolService
{
    public bool[,]? NextMatrixState { get; set; }
    public int CallCount { get; private set; }

    public void Connect(HidDevice device) { }
    public int GetLayerCount() => 1;
    public ulong GetKeyboardId() => 0;
    public int GetDefinitionSize() => 0;
    public byte[] GetDefinition() => [];
    public ushort[,,] GetKeymapBuffer(int layers, int rows, int cols) => new ushort[layers, rows, cols];

    public bool[,] GetSwitchMatrixState(int rows, int cols)
    {
        CallCount++;
        return NextMatrixState ?? new bool[rows, cols];
    }

    public ushort? GetQmkSetting(ushort settingId) => null;

    public void Dispose() { }
}

public class MatrixPollingServiceTests
{
    [Fact]
    public void Start_BeginsPolling()
    {
        var fake = new FakeVialProtocolService();
        using var sut = new MatrixPollingService(fake, 10, 6);

        sut.Start();
        Thread.Sleep(1000); // Allow a few poll cycles (longer for CI runners)
        sut.Stop();

        Assert.True(fake.CallCount > 0);
    }

    [Fact]
    public void Stop_HaltsPolling()
    {
        var fake = new FakeVialProtocolService();
        using var sut = new MatrixPollingService(fake, 10, 6);

        sut.Start();
        Thread.Sleep(500);
        sut.Stop();
        var countAfterStop = fake.CallCount;
        Thread.Sleep(200);

        Assert.Equal(countAfterStop, fake.CallCount);
    }

    [Fact]
    public void MatrixStateChanged_FiresOnChange()
    {
        var fake = new FakeVialProtocolService();
        var state = new bool[10, 6];
        state[0, 0] = true;
        fake.NextMatrixState = state;

        using var sut = new MatrixPollingService(fake, 10, 6);
        bool[,]? received = null;
        sut.MatrixStateChanged += s => received = s;

        sut.Start();
        Thread.Sleep(1000);
        sut.Stop();

        Assert.NotNull(received);
        Assert.True(received![0, 0]);
    }

    [Fact]
    public void IsRunning_ReflectsState()
    {
        var fake = new FakeVialProtocolService();
        using var sut = new MatrixPollingService(fake, 10, 6);

        Assert.False(sut.IsRunning);
        sut.Start();
        Assert.True(sut.IsRunning);
        sut.Stop();
        Assert.False(sut.IsRunning);
    }

    [Fact]
    public void Dispose_StopsPolling()
    {
        var fake = new FakeVialProtocolService();
        var sut = new MatrixPollingService(fake, 10, 6);
        sut.Start();
        sut.Dispose();

        Assert.False(sut.IsRunning);
    }
}
