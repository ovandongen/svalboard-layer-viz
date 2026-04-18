using SvalboardLayerViz.Core.Dynamic;
using Xunit;

namespace SvalboardLayerViz.Tests.Dynamic;

/// <summary>
/// Edge-case coverage for Combo and TapDance codecs: all-non-zero roundtrip,
/// boundary values (0 / 1 / 65535), short buffers, oversized buffers, IsEmpty
/// semantics, and byte-level LE verification.
/// </summary>
public class ComboTapDanceEdgeTests
{
    // --- Combo ---

    [Fact]
    public void Combo_AllFieldsNonZero_Roundtrips()
    {
        var src = new Combo(0x0004, 0x0005, 0x0006, 0x0007, 0x5222);
        var bytes = ComboCodec.Encode(src);
        var decoded = ComboCodec.Decode(bytes);
        Assert.Equal(src, decoded);
    }

    [Theory]
    [InlineData((ushort)0x0000)]
    [InlineData((ushort)0x0001)]
    [InlineData((ushort)0xFFFF)]
    [InlineData((ushort)0x1234)]
    public void Combo_OutputBoundaryValues_Roundtrip(ushort output)
    {
        var src = new Combo(0x0004, 0x0005, 0, 0, output);
        var decoded = ComboCodec.Decode(ComboCodec.Encode(src));
        Assert.Equal(output, decoded.Output);
    }

    [Fact]
    public void Combo_IsEmpty_OnlyWhenAllZero()
    {
        Assert.True(Combo.Empty.IsEmpty);
        Assert.True(new Combo(0, 0, 0, 0, 0).IsEmpty);
        Assert.False(new Combo(0x0004, 0, 0, 0, 0).IsEmpty);
        Assert.False(new Combo(0, 0, 0, 0, 0x0004).IsEmpty);
    }

    [Fact]
    public void Combo_Encode_ProducesLittleEndianBytes()
    {
        var c = new Combo(0x1234, 0x5678, 0x9ABC, 0xDEF0, 0x0042);
        var bytes = ComboCodec.Encode(c);

        Assert.Equal(10, bytes.Length);
        // Input0 LE
        Assert.Equal(0x34, bytes[0]);
        Assert.Equal(0x12, bytes[1]);
        // Input1 LE
        Assert.Equal(0x78, bytes[2]);
        Assert.Equal(0x56, bytes[3]);
        // Output LE
        Assert.Equal(0x42, bytes[8]);
        Assert.Equal(0x00, bytes[9]);
    }

    [Fact]
    public void Combo_Decode_ShortBuffer_Throws()
    {
        var tooShort = new byte[Combo.EntryBytes - 1];
        Assert.Throws<ArgumentException>(() => ComboCodec.Decode(tooShort));
    }

    [Fact]
    public void Combo_Decode_OversizedBuffer_Throws()
    {
        // Silent truncation of oversized buffers masks caller bugs — all callers
        // source exactly Combo.EntryBytes from the protocol.
        var tooLong = new byte[Combo.EntryBytes + 10];
        Assert.Throws<ArgumentException>(() => ComboCodec.Decode(tooLong));
    }

    [Fact]
    public void TapDance_Decode_OversizedBuffer_Throws()
    {
        var tooLong = new byte[TapDance.EntryBytes + 10];
        Assert.Throws<ArgumentException>(() => TapDanceCodec.Decode(tooLong));
    }

    [Fact]
    public void Combo_AllZeroBytes_DecodesToEmpty()
    {
        var decoded = ComboCodec.Decode(new byte[Combo.EntryBytes]);
        Assert.True(decoded.IsEmpty);
    }

    // --- TapDance ---

    [Fact]
    public void TapDance_AllFieldsNonZero_Roundtrips()
    {
        var src = new TapDance(0x0004, 0x0005, 0x0006, 0x0007, 180);
        var decoded = TapDanceCodec.Decode(TapDanceCodec.Encode(src));
        Assert.Equal(src, decoded);
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)1)]
    [InlineData((ushort)200)]
    [InlineData((ushort)65535)]
    public void TapDance_TappingTermBoundaries_Roundtrip(ushort term)
    {
        var src = new TapDance(0x0004, 0, 0, 0, term);
        var decoded = TapDanceCodec.Decode(TapDanceCodec.Encode(src));
        Assert.Equal(term, decoded.TappingTerm);
    }

    [Fact]
    public void TapDance_IsEmpty_IgnoresTappingTerm()
    {
        // An "empty" tap-dance has no action keys but may still carry the
        // default tapping term. IsEmpty should return true regardless.
        Assert.True(new TapDance(0, 0, 0, 0, TapDance.DefaultTappingTerm).IsEmpty);
        Assert.True(new TapDance(0, 0, 0, 0, 0).IsEmpty);
        Assert.True(new TapDance(0, 0, 0, 0, 65535).IsEmpty);
        Assert.False(new TapDance(0x0004, 0, 0, 0, 0).IsEmpty);
    }

    [Fact]
    public void TapDance_Empty_UsesDefaultTappingTerm()
    {
        Assert.Equal(TapDance.DefaultTappingTerm, TapDance.Empty.TappingTerm);
        Assert.True(TapDance.Empty.IsEmpty);
    }

    [Fact]
    public void TapDance_Encode_ProducesLittleEndianBytes()
    {
        var td = new TapDance(0x1234, 0x5678, 0x9ABC, 0xDEF0, 200);
        var bytes = TapDanceCodec.Encode(td);

        Assert.Equal(10, bytes.Length);
        Assert.Equal(0x34, bytes[0]);
        Assert.Equal(0x12, bytes[1]);
        // TappingTerm 200 = 0x00C8 LE
        Assert.Equal(0xC8, bytes[8]);
        Assert.Equal(0x00, bytes[9]);
    }

    [Fact]
    public void TapDance_Decode_ShortBuffer_Throws()
    {
        var tooShort = new byte[TapDance.EntryBytes - 1];
        Assert.Throws<ArgumentException>(() => TapDanceCodec.Decode(tooShort));
    }

    [Fact]
    public void TapDance_Decode_ExactlyFitsBuffer()
    {
        // Ensure exactly 10 bytes works.
        var bytes = new byte[TapDance.EntryBytes];
        bytes[0] = 0x04; // OnTap = 0x0004
        bytes[8] = 0xC8; bytes[9] = 0x00; // Term = 200

        var decoded = TapDanceCodec.Decode(bytes);

        Assert.Equal((ushort)0x0004, decoded.OnTap);
        Assert.Equal((ushort)200, decoded.TappingTerm);
    }

    [Fact]
    public void TapDance_AllZeroBytes_DecodesToEmptyWithZeroTerm()
    {
        var decoded = TapDanceCodec.Decode(new byte[TapDance.EntryBytes]);
        Assert.True(decoded.IsEmpty);
        Assert.Equal((ushort)0, decoded.TappingTerm);
    }
}
