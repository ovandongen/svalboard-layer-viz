using SvalboardLayerViz.Core.Dynamic;
using Xunit;

namespace SvalboardLayerViz.Tests.Dynamic;

public class TapDanceCodecTests
{
    [Fact]
    public void Encode_LittleEndianLayout()
    {
        var td = new TapDance(0x0004, 0xE100, 0x0005, 0x0006, 200);
        var bytes = TapDanceCodec.Encode(td);

        Assert.Equal(10, bytes.Length);
        Assert.Equal(new byte[] { 0x04, 0x00, 0x00, 0xE1, 0x05, 0x00, 0x06, 0x00, 0xC8, 0x00 }, bytes);
    }

    [Fact]
    public void Decode_LittleEndianLayout()
    {
        var bytes = new byte[] { 0x04, 0x00, 0x00, 0xE1, 0x05, 0x00, 0x06, 0x00, 0xC8, 0x00 };
        var td = TapDanceCodec.Decode(bytes);

        Assert.Equal(0x0004, td.OnTap);
        Assert.Equal(0xE100, td.OnHold);
        Assert.Equal(0x0005, td.OnDoubleTap);
        Assert.Equal(0x0006, td.OnTapHold);
        Assert.Equal(200, td.TappingTerm);
    }

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var original = new TapDance(0x0001, 0x0002, 0x0003, 0x0004, 250);
        var decoded = TapDanceCodec.Decode(TapDanceCodec.Encode(original));
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Decode_RejectsShortBuffer()
    {
        Assert.Throws<ArgumentException>(() => TapDanceCodec.Decode(new byte[9]));
    }

    [Fact]
    public void Empty_HasDefaultTappingTerm()
    {
        Assert.True(TapDance.Empty.IsEmpty);
        Assert.Equal(TapDance.DefaultTappingTerm, TapDance.Empty.TappingTerm);
    }

    [Fact]
    public void IsEmpty_IgnoresTappingTerm()
    {
        // Tapping term alone doesn't make an entry non-empty
        Assert.True(new TapDance(0, 0, 0, 0, 500).IsEmpty);
        Assert.False(new TapDance(0x04, 0, 0, 0, 200).IsEmpty);
    }
}
