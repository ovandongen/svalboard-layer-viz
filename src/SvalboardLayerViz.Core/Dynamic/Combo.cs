using System.Buffers.Binary;

namespace SvalboardLayerViz.Core.Dynamic;

/// <summary>
/// A Vial combo: up to 4 input keys pressed together produce one output keycode.
/// Unused input slots are 0x0000 (KC_NO).
/// </summary>
public sealed record Combo(ushort Input0, ushort Input1, ushort Input2, ushort Input3, ushort Output)
{
    public const int EntryBytes = 10;

    public static Combo Empty => new(0, 0, 0, 0, 0);

    public bool IsEmpty => Input0 == 0 && Input1 == 0 && Input2 == 0 && Input3 == 0 && Output == 0;
}

/// <summary>
/// Encodes/decodes a single combo entry as 10 bytes (5 × uint16 little-endian):
/// [in0, in1, in2, in3, output].
/// </summary>
public static class ComboCodec
{
    public static byte[] Encode(Combo combo)
    {
        var buf = new byte[Combo.EntryBytes];
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0), combo.Input0);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2), combo.Input1);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(4), combo.Input2);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(6), combo.Input3);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(8), combo.Output);
        return buf;
    }

    public static Combo Decode(byte[] bytes)
    {
        if (bytes.Length != Combo.EntryBytes)
            throw new ArgumentException(
                $"Combo entry requires exactly {Combo.EntryBytes} bytes, got {bytes.Length}.",
                nameof(bytes));

        return new Combo(
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0)),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6)),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8)));
    }
}
