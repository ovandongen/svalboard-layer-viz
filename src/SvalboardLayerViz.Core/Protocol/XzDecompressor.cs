using SharpCompress.Compressors.Xz;

namespace SvalboardLayerViz.Core.Protocol;

/// <summary>
/// Decompresses XZ-compressed data from Vial keyboard definitions.
/// The definition payload is XZ-compressed JSON.
/// </summary>
public static class XzDecompressor
{
    /// <summary>
    /// Decompresses XZ data and returns the resulting string (UTF-8 JSON).
    /// </summary>
    public static string DecompressToString(byte[] xzData)
    {
        using var inputStream = new MemoryStream(xzData);
        using var xzStream = new XZStream(inputStream);
        using var reader = new StreamReader(xzStream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Decompresses XZ data and returns raw bytes.
    /// </summary>
    public static byte[] Decompress(byte[] xzData)
    {
        using var inputStream = new MemoryStream(xzData);
        using var xzStream = new XZStream(inputStream);
        using var outputStream = new MemoryStream();
        xzStream.CopyTo(outputStream);
        return outputStream.ToArray();
    }
}
