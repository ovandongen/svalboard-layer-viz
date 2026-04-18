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
        WriteLe(buf, 0, combo.Input0);
        WriteLe(buf, 2, combo.Input1);
        WriteLe(buf, 4, combo.Input2);
        WriteLe(buf, 6, combo.Input3);
        WriteLe(buf, 8, combo.Output);
        return buf;
    }

    public static Combo Decode(byte[] bytes)
    {
        if (bytes.Length != Combo.EntryBytes)
            throw new ArgumentException(
                $"Combo entry requires exactly {Combo.EntryBytes} bytes, got {bytes.Length}.",
                nameof(bytes));

        return new Combo(
            ReadLe(bytes, 0),
            ReadLe(bytes, 2),
            ReadLe(bytes, 4),
            ReadLe(bytes, 6),
            ReadLe(bytes, 8));
    }

    private static void WriteLe(byte[] buf, int offset, ushort value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static ushort ReadLe(byte[] buf, int offset) =>
        (ushort)(buf[offset] | (buf[offset + 1] << 8));
}
