using SvalboardLayerViz.Core.Dynamic;
using Xunit;

namespace SvalboardLayerViz.Tests.Protocol;

public class VialProtocolDynamicEntryTests
{
    [Fact]
    public void GetDynamicEntryCounts_ReturnsConfiguredValue()
    {
        var fake = new FakeVialProtocolService { DynamicEntryCounts = new(8, 16, 4, 2) };

        var counts = fake.GetDynamicEntryCounts();

        Assert.Equal(8, counts.TapDanceCount);
        Assert.Equal(16, counts.ComboCount);
        Assert.Equal(4, counts.KeyOverrideCount);
        Assert.Equal(2, counts.AltRepeatCount);
    }

    [Fact]
    public void GetDynamicEntryCounts_DefaultsToAllZero()
    {
        var fake = new FakeVialProtocolService();
        var counts = fake.GetDynamicEntryCounts();

        Assert.Equal(0, counts.TapDanceCount);
        Assert.Equal(0, counts.ComboCount);
    }

    [Fact]
    public void GetComboEntry_ReturnsStoredBytes()
    {
        var fake = new FakeVialProtocolService();
        var bytes = ComboCodec.Encode(new Combo(1, 2, 3, 4, 5));
        fake.ComboEntries[0] = bytes;

        var result = fake.GetComboEntry(0);

        Assert.Equal(bytes, result);
    }

    [Fact]
    public void GetComboEntry_UnknownIndex_ReturnsZeroBuffer()
    {
        var fake = new FakeVialProtocolService();

        var result = fake.GetComboEntry(3);

        Assert.Equal(Combo.EntryBytes, result.Length);
        Assert.All(result, b => Assert.Equal(0, b));
    }

    [Fact]
    public void SetComboEntry_RecordsCallAndStores()
    {
        var fake = new FakeVialProtocolService();
        var bytes = ComboCodec.Encode(new Combo(0x04, 0x05, 0, 0, 0x29));

        fake.SetComboEntry(2, bytes);

        var call = Assert.Single(fake.SetComboCalls);
        Assert.Equal(2, call.Index);
        Assert.Equal(bytes, call.Entry);
        Assert.Equal(bytes, fake.ComboEntries[2]);
    }

    [Fact]
    public void SetComboEntry_FailAt_Throws()
    {
        var fake = new FakeVialProtocolService
        {
            SetComboFailAt = (1, new InvalidOperationException("HID fail"))
        };
        var bytes = new byte[Combo.EntryBytes];

        fake.SetComboEntry(0, bytes);
        Assert.Throws<InvalidOperationException>(() => fake.SetComboEntry(1, bytes));
    }

    [Fact]
    public void GetTapDanceEntry_ReturnsStoredBytes()
    {
        var fake = new FakeVialProtocolService();
        var bytes = TapDanceCodec.Encode(new TapDance(0x04, 0xE1, 0x05, 0, 250));
        fake.TapDanceEntries[1] = bytes;

        Assert.Equal(bytes, fake.GetTapDanceEntry(1));
    }

    [Fact]
    public void SetTapDanceEntry_RecordsCallAndStores()
    {
        var fake = new FakeVialProtocolService();
        var bytes = TapDanceCodec.Encode(new TapDance(0x04, 0xE1, 0, 0, 200));

        fake.SetTapDanceEntry(0, bytes);

        var call = Assert.Single(fake.SetTapDanceCalls);
        Assert.Equal(0, call.Index);
        Assert.Equal(bytes, call.Entry);
    }

    [Fact]
    public void SetTapDanceEntry_FailAt_Throws()
    {
        var fake = new FakeVialProtocolService
        {
            SetTapDanceFailAt = (0, new InvalidOperationException("HID fail"))
        };
        Assert.Throws<InvalidOperationException>(() =>
            fake.SetTapDanceEntry(0, new byte[TapDance.EntryBytes]));
    }
}
