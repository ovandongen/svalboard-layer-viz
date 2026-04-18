using SvalboardLayerViz.Core.Protocol;
using Xunit;

namespace SvalboardLayerViz.Tests.Protocol;

public class VialProtocolMacroTests
{
    // --- GetMacroCount ---

    [Fact]
    public void GetMacroCount_ReturnsConfiguredValue()
    {
        var fake = new FakeVialProtocolService { MacroCount = 16 };

        Assert.Equal(16, fake.GetMacroCount());
    }

    [Fact]
    public void GetMacroCount_DefaultsToZero()
    {
        var fake = new FakeVialProtocolService();

        Assert.Equal(0, fake.GetMacroCount());
    }

    // --- GetMacroBufferSize ---

    [Fact]
    public void GetMacroBufferSize_ReturnsConfiguredValue()
    {
        var fake = new FakeVialProtocolService { MacroBufferSize = 512 };

        Assert.Equal(512, fake.GetMacroBufferSize());
    }

    // --- GetMacroBuffer ---

    [Fact]
    public void GetMacroBuffer_ReturnsConfiguredData()
    {
        var data = new byte[] { 0x04, 0x05, 0x00, 0x06, 0x00 };
        var fake = new FakeVialProtocolService { MacroBufferData = data };

        var result = fake.GetMacroBuffer(data.Length);

        Assert.Equal(data, result);
    }

    [Fact]
    public void GetMacroBuffer_NullData_ReturnsZeroFilledArray()
    {
        var fake = new FakeVialProtocolService();

        var result = fake.GetMacroBuffer(64);

        Assert.Equal(64, result.Length);
        Assert.All(result, b => Assert.Equal(0, b));
    }

    // --- MacroSetBuffer ---

    [Fact]
    public void MacroSetBuffer_RecordsCall()
    {
        var fake = new FakeVialProtocolService();
        var data = new byte[] { 0x04, 0x05, 0x00 };

        fake.MacroSetBuffer(100, data);

        var call = Assert.Single(fake.MacroSetBufferCalls);
        Assert.Equal(100, call.Offset);
        Assert.Equal(data, call.Data);
    }

    [Fact]
    public void MacroSetBuffer_MultipleCalls_RecordsAll()
    {
        var fake = new FakeVialProtocolService();

        fake.MacroSetBuffer(0, new byte[] { 0x01 });
        fake.MacroSetBuffer(28, new byte[] { 0x02 });
        fake.MacroSetBuffer(56, new byte[] { 0x03 });

        Assert.Equal(3, fake.MacroSetBufferCalls.Count);
        Assert.Equal(0, fake.MacroSetBufferCalls[0].Offset);
        Assert.Equal(28, fake.MacroSetBufferCalls[1].Offset);
        Assert.Equal(56, fake.MacroSetBufferCalls[2].Offset);
    }

    [Fact]
    public void MacroSetBuffer_FailAt_ThrowsOnSpecifiedCall()
    {
        var fake = new FakeVialProtocolService
        {
            MacroSetBufferFailAt = (1, new IOException("HID write failed"))
        };

        fake.MacroSetBuffer(0, new byte[] { 0x01 }); // Call 0 — succeeds
        Assert.Throws<IOException>(() => fake.MacroSetBuffer(28, new byte[] { 0x02 })); // Call 1 — fails

        Assert.Single(fake.MacroSetBufferCalls); // Only the successful call recorded
    }

    // --- DynamicKeymapMacroReset ---

    [Fact]
    public void DynamicKeymapMacroReset_IncrementsCount()
    {
        var fake = new FakeVialProtocolService();

        fake.DynamicKeymapMacroReset();
        fake.DynamicKeymapMacroReset();

        Assert.Equal(2, fake.DynamicKeymapMacroResetCount);
    }
}

public class VialProtocolMacroCommandConstantTests
{
    [Fact]
    public void MacroGetCount_Is0x0C()
    {
        Assert.Equal(0x0C, VialCommands.MacroGetCount);
    }

    [Fact]
    public void MacroGetBufferSize_Is0x0D()
    {
        Assert.Equal(0x0D, VialCommands.MacroGetBufferSize);
    }

    [Fact]
    public void MacroGetBuffer_Is0x0E()
    {
        Assert.Equal(0x0E, VialCommands.MacroGetBuffer);
    }

    [Fact]
    public void MacroSetBuffer_Is0x0F()
    {
        Assert.Equal(0x0F, VialCommands.MacroSetBuffer);
    }

    [Fact]
    public void DynamicKeymapMacroReset_Is0x0B()
    {
        Assert.Equal(0x0B, VialCommands.DynamicKeymapMacroReset);
    }
}
