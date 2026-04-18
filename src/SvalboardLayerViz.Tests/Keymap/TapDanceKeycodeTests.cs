using SvalboardLayerViz.Core.Keymap;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

public class TapDanceKeycodeTests
{
    [Theory]
    [InlineData(0x5700, 0)]
    [InlineData(0x5701, 1)]
    [InlineData(0x5710, 16)]
    [InlineData(0x57FF, 0xFF)]
    public void Decode_TapDanceRange_ReturnsTapDanceKeycode(ushort raw, int expectedIndex)
    {
        var d = KeycodeDecoder.Decode(raw);
        var td = Assert.IsType<TapDanceKeycode>(d);
        Assert.Equal(expectedIndex, td.Index);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(31)]
    public void Encode_TapDanceKeycode_RoundTrips(int index)
    {
        var encoded = KeycodeEncoder.Encode(new TapDanceKeycode(index));
        Assert.Equal((ushort)(0x5700 + index), encoded);

        var decoded = KeycodeDecoder.Decode(encoded);
        Assert.Equal(new TapDanceKeycode(index), decoded);
    }

    [Fact]
    public void Resolve_TapDance_ReturnsTDLabel()
    {
        var service = new KeycodeService();
        var info = service.Resolve(0x5703);
        Assert.Equal("TD3", info.Label);
        Assert.Equal("TapDance", info.SecondaryLabel);
    }

    [Fact]
    public void OutsideRange_NotDecodedAsTapDance()
    {
        Assert.IsNotType<TapDanceKeycode>(KeycodeDecoder.Decode(0x56FF));
        Assert.IsNotType<TapDanceKeycode>(KeycodeDecoder.Decode(0x5800));
    }
}
