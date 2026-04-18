using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Tests.Protocol;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

public class SaveFlowExecutor_Macro_Tests
{
    private static byte[] MakeBuffer(int size)
    {
        var buf = new byte[size];
        for (var i = 0; i < size; i++)
            buf[i] = (byte)(i & 0xFF);
        return buf;
    }

    [Fact]
    public void Execute_MacroBufferWrite_64Bytes_ChunksInto3Segments()
    {
        var fake = new FakeVialProtocolService();
        var buffer = MakeBuffer(64);
        var writes = new DeviceWrite[] { new MacroBufferWrite(buffer) };

        var outcome = SaveFlowExecutor.Execute(fake, writes, CancellationToken.None);

        Assert.Single(outcome.Applied);
        Assert.Null(outcome.FailedAt);
        Assert.False(outcome.Cancelled);

        Assert.Equal(3, fake.MacroSetBufferCalls.Count);
        // Chunk 0: offset 0, 28 bytes
        Assert.Equal(0, fake.MacroSetBufferCalls[0].Offset);
        Assert.Equal(28, fake.MacroSetBufferCalls[0].Data.Length);
        // Chunk 1: offset 28, 28 bytes
        Assert.Equal(28, fake.MacroSetBufferCalls[1].Offset);
        Assert.Equal(28, fake.MacroSetBufferCalls[1].Data.Length);
        // Chunk 2: offset 56, 8 bytes
        Assert.Equal(56, fake.MacroSetBufferCalls[2].Offset);
        Assert.Equal(8, fake.MacroSetBufferCalls[2].Data.Length);

        // Verify data integrity
        Assert.Equal(buffer[0..28], fake.MacroSetBufferCalls[0].Data);
        Assert.Equal(buffer[28..56], fake.MacroSetBufferCalls[1].Data);
        Assert.Equal(buffer[56..64], fake.MacroSetBufferCalls[2].Data);
    }

    [Fact]
    public void Execute_MacroBufferWrite_20Bytes_SingleChunk()
    {
        var fake = new FakeVialProtocolService();
        var buffer = MakeBuffer(20);
        var writes = new DeviceWrite[] { new MacroBufferWrite(buffer) };

        var outcome = SaveFlowExecutor.Execute(fake, writes, CancellationToken.None);

        Assert.Single(outcome.Applied);
        Assert.Single(fake.MacroSetBufferCalls);
        Assert.Equal(0, fake.MacroSetBufferCalls[0].Offset);
        Assert.Equal(20, fake.MacroSetBufferCalls[0].Data.Length);
    }

    [Fact]
    public void Execute_MacroBufferWrite_56Bytes_TwoExactChunks()
    {
        var fake = new FakeVialProtocolService();
        var buffer = MakeBuffer(56);
        var writes = new DeviceWrite[] { new MacroBufferWrite(buffer) };

        var outcome = SaveFlowExecutor.Execute(fake, writes, CancellationToken.None);

        Assert.Single(outcome.Applied);
        Assert.Equal(2, fake.MacroSetBufferCalls.Count);
        Assert.Equal(28, fake.MacroSetBufferCalls[0].Data.Length);
        Assert.Equal(28, fake.MacroSetBufferCalls[1].Data.Length);
    }

    [Fact]
    public void Execute_MacroBufferWrite_MidChunkFailure_ReportsFailedWrite()
    {
        var fake = new FakeVialProtocolService
        {
            MacroSetBufferFailAt = (1, new IOException("HID timeout")),
        };
        var buffer = MakeBuffer(64);
        var writes = new DeviceWrite[] { new MacroBufferWrite(buffer) };

        var outcome = SaveFlowExecutor.Execute(fake, writes, CancellationToken.None);

        Assert.Empty(outcome.Applied);
        Assert.NotNull(outcome.FailedAt);
        Assert.IsType<MacroBufferWrite>(outcome.FailedAt);
        Assert.IsType<IOException>(outcome.Failure);
        Assert.False(outcome.Cancelled);
        // First chunk succeeded in the fake, second threw
        Assert.Single(fake.MacroSetBufferCalls);
    }

    [Fact]
    public void Execute_MixedKeycodeAndMacro_AllSucceed()
    {
        var fake = new FakeVialProtocolService();
        var keyWrite = new SetKeycodeWrite(0, 1, 2, 0x0004);
        var macroWrite = new MacroBufferWrite(MakeBuffer(20));
        var writes = new DeviceWrite[] { keyWrite, macroWrite };

        var outcome = SaveFlowExecutor.Execute(fake, writes, CancellationToken.None);

        Assert.Equal(2, outcome.Applied.Count);
        Assert.Null(outcome.FailedAt);
        Assert.Single(fake.SetKeycodeCalls);
        Assert.Single(fake.MacroSetBufferCalls);
    }

    [Fact]
    public void Execute_MacroBufferWrite_CancellationBeforeExecution()
    {
        var fake = new FakeVialProtocolService();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var writes = new DeviceWrite[] { new MacroBufferWrite(MakeBuffer(64)) };

        var outcome = SaveFlowExecutor.Execute(fake, writes, cts.Token);

        Assert.True(outcome.Cancelled);
        Assert.Empty(outcome.Applied);
        Assert.Empty(fake.MacroSetBufferCalls);
    }

    [Fact]
    public void Execute_EmptyMacroBuffer_ZeroChunks()
    {
        var fake = new FakeVialProtocolService();
        var writes = new DeviceWrite[] { new MacroBufferWrite([]) };

        var outcome = SaveFlowExecutor.Execute(fake, writes, CancellationToken.None);

        Assert.Single(outcome.Applied);
        Assert.Empty(fake.MacroSetBufferCalls);
    }
}
