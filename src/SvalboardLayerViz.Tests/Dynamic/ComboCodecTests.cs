using SvalboardLayerViz.Core.Dynamic;
using Xunit;

namespace SvalboardLayerViz.Tests.Dynamic;

public class ComboCodecTests
{
    [Fact]
    public void Encode_LittleEndianLayout()
    {
        var combo = new Combo(0x0004, 0xE100, 0x1234, 0x0000, 0xABCD);
        var bytes = ComboCodec.Encode(combo);

        Assert.Equal(10, bytes.Length);
        Assert.Equal(new byte[] { 0x04, 0x00, 0x00, 0xE1, 0x34, 0x12, 0x00, 0x00, 0xCD, 0xAB }, bytes);
    }

    [Fact]
    public void Decode_LittleEndianLayout()
    {
        var bytes = new byte[] { 0x04, 0x00, 0x00, 0xE1, 0x34, 0x12, 0x00, 0x00, 0xCD, 0xAB };
        var combo = ComboCodec.Decode(bytes);

        Assert.Equal(0x0004, combo.Input0);
        Assert.Equal(0xE100, combo.Input1);
        Assert.Equal(0x1234, combo.Input2);
        Assert.Equal(0x0000, combo.Input3);
        Assert.Equal(0xABCD, combo.Output);
    }

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var original = new Combo(0x0001, 0x0002, 0x0003, 0x0004, 0x0005);
        var decoded = ComboCodec.Decode(ComboCodec.Encode(original));
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Decode_RejectsShortBuffer()
    {
        Assert.Throws<ArgumentException>(() => ComboCodec.Decode(new byte[9]));
    }

    [Fact]
    public void Empty_IsAllZero()
    {
        Assert.True(Combo.Empty.IsEmpty);
        Assert.Equal(new byte[10], ComboCodec.Encode(Combo.Empty));
    }

    [Fact]
    public void IsEmpty_FalseWhenAnyFieldSet()
    {
        Assert.False(new Combo(1, 0, 0, 0, 0).IsEmpty);
        Assert.False(new Combo(0, 0, 0, 0, 1).IsEmpty);
    }
}
