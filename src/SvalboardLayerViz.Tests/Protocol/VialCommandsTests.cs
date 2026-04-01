using SvalboardLayerViz.Core.Protocol;
using Xunit;

namespace SvalboardLayerViz.Tests.Protocol;

public class VialCommandsTests
{
    [Fact]
    public void ReportSize_Is32()
    {
        Assert.Equal(32, VialCommands.ReportSize);
    }

    [Fact]
    public void XzMagicBytes_HasCorrectLength()
    {
        Assert.Equal(6, VialCommands.XzMagicBytes.Length);
    }

    [Fact]
    public void XzMagicBytes_StartsWithFD37()
    {
        Assert.Equal(0xFD, VialCommands.XzMagicBytes[0]);
        Assert.Equal(0x37, VialCommands.XzMagicBytes[1]);
    }

    [Fact]
    public void VialPrefix_IsFE()
    {
        Assert.Equal(0xFE, VialCommands.VialPrefix);
    }
}
