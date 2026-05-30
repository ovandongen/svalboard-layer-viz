using System.Buffers.Binary;

namespace SvalboardLayerViz.Core.Dynamic;

/// <summary>
/// A Vial tap-dance entry: one input key produces different keycodes based on
/// tap count / hold pattern. Empty keycode slots are 0x0000 (KC_NO).
/// </summary>
public sealed record TapDance(
    ushort OnTap,
    ushort OnHold,
    ushort OnDoubleTap,
    ushort OnTapHold,
    ushort TappingTerm)
{
    public const int EntryBytes = 10;

    /// <summary>Firmware default tapping term when the user hasn't set one.</summary>
    public const ushort DefaultTappingTerm = 200;

    public static TapDance Empty => new(0, 0, 0, 0, DefaultTappingTerm);

    public bool IsEmpty =>
        OnTap == 0 && OnHold == 0 && OnDoubleTap == 0 && OnTapHold == 0;
}

/// <summary>
/// Encodes/decodes a single tap-dance entry as 10 bytes (5 × uint16 little-endian):
/// [on_tap, on_hold, on_double_tap, on_tap_hold, tapping_term].
/// </summary>
public static class TapDanceCodec
{
    public static byte[] Encode(TapDance td)
    {
        var buf = new byte[TapDance.EntryBytes];
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0), td.OnTap);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2), td.OnHold);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(4), td.OnDoubleTap);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(6), td.OnTapHold);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(8), td.TappingTerm);
        return buf;
    }

    public static TapDance Decode(byte[] bytes)
    {
        if (bytes.Length != TapDance.EntryBytes)
            throw new ArgumentException(
                $"TapDance entry requires exactly {TapDance.EntryBytes} bytes, got {bytes.Length}.",
                nameof(bytes));

        return new TapDance(
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0)),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6)),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8)));
    }
}
