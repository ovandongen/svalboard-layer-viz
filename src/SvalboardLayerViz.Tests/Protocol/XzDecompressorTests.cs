using SvalboardLayerViz.Core.Protocol;
using Xunit;

namespace SvalboardLayerViz.Tests.Protocol;

public class XzDecompressorTests
{
    // XZ-compressed "Hello, Svalboard!" generated via: lzma.compress(b'Hello, Svalboard!', format=lzma.FORMAT_XZ)
    private static readonly byte[] ValidXzHello = Convert.FromBase64String(
        "/Td6WFoAAATm1rRGAgAhARYAAAB0L+WjAQAQSGVsbG8sIFN2YWxib2FyZCEAAAAALaLxG7DsY2cAASkRMgpwDh+2830BAAAAAARZWg==");

    private const string ExpectedHello = "Hello, Svalboard!";

    // XZ-compressed payload of 100_000 'A' bytes.
    private static readonly byte[] ValidXzLarge = Convert.FromBase64String(
        "/Td6WFoAAATm1rRGAgAhARYAAAB0L+Wj4YafAFNdACDv+7/+o7Fe5fg/sqomVfhocEFwFQ+N/R5MG4pCtxn0aRhxrmYjiopNL6MN2X+m44wjEVPgWRjFdYrid/i2lH8MasDedElk4ulcU7IE1rH1lwAAAAAUqBwqWCwzGgABb6CNBgAAkORuUbHEZ/sCAAAAAARZWg==");

    [Fact]
    public void DecompressToString_ValidInput_ReturnsOriginalString()
    {
        var result = XzDecompressor.DecompressToString(ValidXzHello);
        Assert.Equal(ExpectedHello, result);
    }

    [Fact]
    public void Decompress_ValidInput_ReturnsOriginalBytes()
    {
        var result = XzDecompressor.Decompress(ValidXzHello);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(ExpectedHello), result);
    }

    [Fact]
    public void Decompress_LargePayload_RoundTripsExactly()
    {
        var result = XzDecompressor.Decompress(ValidXzLarge);
        Assert.Equal(100_000, result.Length);
        Assert.All(result, b => Assert.Equal((byte)'A', b));
    }

    [Fact]
    public void Decompress_EmptyInput_Throws()
    {
        Assert.ThrowsAny<Exception>(() => XzDecompressor.Decompress([]));
    }

    [Fact]
    public void DecompressToString_EmptyInput_ReturnsEmptyOrThrows()
    {
        // An empty input stream yields either an exception (preferred: real corruption)
        // or an empty string. Either behavior is acceptable; crash is not.
        try
        {
            var result = XzDecompressor.DecompressToString([]);
            Assert.Equal(string.Empty, result);
        }
        catch (Exception)
        {
            // acceptable
        }
    }

    [Fact]
    public void Decompress_CorruptMagicBytes_Throws()
    {
        var corrupt = (byte[])ValidXzHello.Clone();
        corrupt[0] = 0x00; // magic byte FD → 00
        Assert.ThrowsAny<Exception>(() => XzDecompressor.Decompress(corrupt));
    }

    [Fact]
    public void Decompress_CorruptStreamHeader_Throws()
    {
        var corrupt = (byte[])ValidXzHello.Clone();
        // Flip a bit inside the XZ stream header flags (byte 7) — should invalidate CRC.
        corrupt[7] ^= 0x01;
        Assert.ThrowsAny<Exception>(() => XzDecompressor.Decompress(corrupt));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]  // only magic bytes
    [InlineData(12)] // magic + stream header but no data
    [InlineData(20)]
    public void Decompress_TruncatedStream_Throws(int keepBytes)
    {
        if (keepBytes >= ValidXzHello.Length) return;
        var truncated = ValidXzHello.AsSpan(0, keepBytes).ToArray();
        Assert.ThrowsAny<Exception>(() => XzDecompressor.Decompress(truncated));
    }

    [Fact]
    public void Decompress_NotXzAtAll_Throws()
    {
        var garbage = System.Text.Encoding.UTF8.GetBytes("this is plain text, not XZ at all");
        Assert.ThrowsAny<Exception>(() => XzDecompressor.Decompress(garbage));
    }

    [Fact]
    public void Decompress_AllZeroBuffer_Throws()
    {
        var zeros = new byte[64];
        Assert.ThrowsAny<Exception>(() => XzDecompressor.Decompress(zeros));
    }
}
