using SvalboardLayerViz.Core.Protocol;
using Xunit;

namespace SvalboardLayerViz.Tests.Protocol;

public class MatrixPollingServiceTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Start_BeginsPolling()
    {
        var fake = new FakeVialProtocolService();
        using var sut = new MatrixPollingService(fake, 10, 6);

        sut.Start();
        var reached = fake.WaitForPolls(1, TestTimeout);
        sut.Stop();

        Assert.True(reached);
    }

    [Fact]
    public void Stop_HaltsPolling()
    {
        var fake = new FakeVialProtocolService();
        using var sut = new MatrixPollingService(fake, 10, 6);

        sut.Start();
        Assert.True(fake.WaitForPolls(1, TestTimeout));
        sut.Stop();
        var countAfterStop = fake.MatrixPollCount;
        Thread.Sleep(200); // give any in-flight tick a chance to land
        Assert.Equal(countAfterStop, fake.MatrixPollCount);
    }

    [Fact]
    public void MatrixStateChanged_FiresOnChange()
    {
        var fake = new FakeVialProtocolService();
        var state = new bool[10, 6];
        state[0, 0] = true;
        fake.NextMatrixState = state;

        using var sut = new MatrixPollingService(fake, 10, 6);
        var received = new TaskCompletionSource<bool[,]>();
        sut.MatrixStateChanged += s => received.TrySetResult(s);

        sut.Start();
        Assert.True(received.Task.Wait(TestTimeout));
        sut.Stop();

        var payload = received.Task.Result;
        Assert.True(payload[0, 0]);
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
        Assert.True(fake.WaitForPolls(1, TestTimeout));
        sut.Dispose();

        Assert.False(sut.IsRunning);
    }
}
