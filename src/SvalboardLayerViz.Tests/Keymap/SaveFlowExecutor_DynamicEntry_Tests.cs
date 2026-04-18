using SvalboardLayerViz.Core.Dynamic;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Tests.Protocol;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

public class SaveFlowExecutor_DynamicEntry_Tests
{
    [Fact]
    public void Execute_ComboEntryWrite_CallsProtocol()
    {
        var fake = new FakeVialProtocolService();
        var entry = ComboCodec.Encode(new Combo(0x04, 0x05, 0, 0, 0x29));
        var writes = new[] { new ComboEntryWrite(2, entry) };

        var outcome = SaveFlowExecutor.Execute(fake, writes, CancellationToken.None);

        Assert.Null(outcome.Failure);
        Assert.False(outcome.Cancelled);
        Assert.Single(outcome.Applied);
        var call = Assert.Single(fake.SetComboCalls);
        Assert.Equal(2, call.Index);
        Assert.Equal(entry, call.Entry);
    }

    [Fact]
    public void Execute_TapDanceEntryWrite_CallsProtocol()
    {
        var fake = new FakeVialProtocolService();
        var entry = TapDanceCodec.Encode(new TapDance(0x04, 0xE1, 0, 0, 200));
        var writes = new[] { new TapDanceEntryWrite(0, entry) };

        var outcome = SaveFlowExecutor.Execute(fake, writes, CancellationToken.None);

        Assert.Null(outcome.Failure);
        Assert.Single(outcome.Applied);
        var call = Assert.Single(fake.SetTapDanceCalls);
        Assert.Equal(0, call.Index);
        Assert.Equal(entry, call.Entry);
    }

    [Fact]
    public void Execute_ComboWriteFailure_RecordedInOutcome()
    {
        var fake = new FakeVialProtocolService
        {
            SetComboFailAt = (0, new InvalidOperationException("boom"))
        };
        var writes = new[] { new ComboEntryWrite(0, new byte[Combo.EntryBytes]) };

        var outcome = SaveFlowExecutor.Execute(fake, writes, CancellationToken.None);

        Assert.NotNull(outcome.Failure);
        Assert.NotNull(outcome.FailedAt);
        Assert.Empty(outcome.Applied);
    }
}
