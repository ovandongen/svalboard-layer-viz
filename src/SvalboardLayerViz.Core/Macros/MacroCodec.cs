namespace SvalboardLayerViz.Core.Macros;

/// <summary>
/// Encodes and decodes QMK macro buffers. The firmware stores all macros in a
/// single contiguous byte buffer, each terminated by 0x00.
///
/// Byte format (new QMK SS_QMK_PREFIX scheme):
///   0x00                        = macro terminator
///   0x01 0x01 kc                = SS_TAP_CODE — tap keycode
///   0x01 0x02 kc                = SS_DOWN_CODE — press keycode
///   0x01 0x03 kc                = SS_UP_CODE — release keycode
///   0x01 0x04 digits '|'        = SS_DELAY_CODE — delay in ms, pipe-terminated
///   anything else (not 0x00/0x01) = text character (send_string ASCII)
/// </summary>
public static class MacroCodec
{
    private const byte Terminator = 0x00;
    private const byte SsQmkPrefix = 0x01;
    private const byte SsTapCode = 0x01;
    private const byte SsDownCode = 0x02;
    private const byte SsUpCode = 0x03;
    private const byte SsDelayCode = 0x04;
    private const byte SsModTapCode = 0x05;
    private const byte DelayTerminator = (byte)'|';

    /// <summary>
    /// Decodes a raw macro buffer into structured macros.
    /// </summary>
    /// <param name="buffer">Raw byte buffer from the device.</param>
    /// <param name="macroCount">Number of macro slots reported by the firmware.</param>
    /// <param name="bufferCapacity">Total buffer capacity in bytes.</param>
    public static MacroBuffer Decode(byte[] buffer, int macroCount, int bufferCapacity)
    {
        var macros = new List<Macro>(macroCount);
        var pos = 0;

        for (var macroIdx = 0; macroIdx < macroCount; macroIdx++)
        {
            var actions = new List<MacroAction>();

            while (pos < buffer.Length && buffer[pos] != Terminator)
            {
                var b = buffer[pos];

                if (b == SsQmkPrefix)
                {
                    // Escape prefix — next byte is the action type
                    pos++;
                    if (pos >= buffer.Length)
                        throw new InvalidDataException(
                            $"Macro {macroIdx}: buffer ended after SS_QMK_PREFIX; missing action type byte.");

                    var actionType = buffer[pos];
                    switch (actionType)
                    {
                        case SsTapCode:
                            pos++;
                            if (pos >= buffer.Length)
                                throw new InvalidDataException(
                                    $"Macro {macroIdx}: buffer ended after SS_TAP_CODE; missing keycode byte.");
                            actions.Add(new MacroTapAction(buffer[pos++]));
                            break;

                        case SsDownCode:
                            pos++;
                            if (pos >= buffer.Length)
                                throw new InvalidDataException(
                                    $"Macro {macroIdx}: buffer ended after SS_DOWN_CODE; missing keycode byte.");
                            actions.Add(new MacroDownAction(buffer[pos++]));
                            break;

                        case SsUpCode:
                            pos++;
                            if (pos >= buffer.Length)
                                throw new InvalidDataException(
                                    $"Macro {macroIdx}: buffer ended after SS_UP_CODE; missing keycode byte.");
                            actions.Add(new MacroUpAction(buffer[pos++]));
                            break;

                        case SsModTapCode:
                            pos++;
                            if (pos + 1 >= buffer.Length)
                                throw new InvalidDataException(
                                    $"Macro {macroIdx}: buffer ended inside SS_MOD_TAP_CODE; need keycode+mods.");
                            var kc = buffer[pos++];
                            var mods = buffer[pos++];
                            actions.Add(new MacroModTapAction(kc, mods));
                            break;

                        case SsDelayCode:
                            pos++;
                            var digits = new List<char>();
                            while (pos < buffer.Length
                                   && buffer[pos] != DelayTerminator
                                   && buffer[pos] != Terminator)
                            {
                                var ch = (char)buffer[pos++];
                                if (ch is < '0' or > '9')
                                    throw new InvalidDataException(
                                        $"Macro {macroIdx}: non-digit 0x{(byte)ch:X2} in delay value.");
                                digits.Add(ch);
                            }
                            // Firmware must emit the pipe terminator. Reaching
                            // buffer end or a macro terminator here means the
                            // delay is truncated — refuse to guess a value.
                            if (pos >= buffer.Length || buffer[pos] != DelayTerminator)
                                throw new InvalidDataException(
                                    $"Macro {macroIdx}: delay value missing '|' terminator.");
                            pos++; // consume the pipe
                            if (digits.Count == 0)
                                throw new InvalidDataException(
                                    $"Macro {macroIdx}: SS_DELAY_CODE with no digits before '|'.");
                            var ms = int.Parse(new string(digits.ToArray()));
                            actions.Add(new MacroDelayAction(ms));
                            break;

                        default:
                            // Unknown action after SS_QMK_PREFIX. Silently skipping would
                            // drop the action on re-encode — firmware data loss with no
                            // signal. Refuse to round-trip macros we can't represent.
                            throw new InvalidDataException(
                                $"Macro {macroIdx}: unknown action byte 0x{actionType:X2} at position {pos}.");
                    }
                }
                else
                {
                    // Text character — collect consecutive non-special bytes
                    var textStart = pos;
                    while (pos < buffer.Length
                           && buffer[pos] != Terminator
                           && buffer[pos] != SsQmkPrefix)
                    {
                        pos++;
                    }
                    var text = new char[pos - textStart];
                    for (var i = 0; i < text.Length; i++)
                        text[i] = (char)buffer[textStart + i];
                    actions.Add(new MacroTextAction(new string(text)));
                }
            }

            // Skip the terminator
            if (pos < buffer.Length && buffer[pos] == Terminator)
                pos++;

            macros.Add(new Macro(macroIdx, actions));
        }

        var usedBytes = ComputeEncodedSize(macros);
        return new MacroBuffer(macros, bufferCapacity, usedBytes);
    }

    /// <summary>
    /// Encodes structured macros back into a raw byte buffer.
    /// The returned buffer is exactly <paramref name="bufferCapacity"/> bytes,
    /// padded with 0x00 if macros don't fill it.
    /// </summary>
    public static byte[] Encode(MacroBuffer macroBuffer)
    {
        return Encode(macroBuffer.Macros, macroBuffer.BufferCapacity);
    }

    /// <summary>
    /// Encodes a list of macros into a byte buffer of the given capacity.
    /// </summary>
    public static byte[] Encode(IReadOnlyList<Macro> macros, int bufferCapacity)
    {
        var result = new byte[bufferCapacity]; // zero-initialized = all terminators
        var pos = 0;

        foreach (var macro in macros)
        {
            foreach (var action in macro.Actions)
            {
                switch (action)
                {
                    case MacroTapAction tap:
                        EnsureSpace(pos, 3, bufferCapacity);
                        result[pos++] = SsQmkPrefix;
                        result[pos++] = SsTapCode;
                        result[pos++] = tap.Keycode;
                        break;

                    case MacroDownAction down:
                        EnsureSpace(pos, 3, bufferCapacity);
                        result[pos++] = SsQmkPrefix;
                        result[pos++] = SsDownCode;
                        result[pos++] = down.Keycode;
                        break;

                    case MacroUpAction up:
                        EnsureSpace(pos, 3, bufferCapacity);
                        result[pos++] = SsQmkPrefix;
                        result[pos++] = SsUpCode;
                        result[pos++] = up.Keycode;
                        break;

                    case MacroModTapAction mt:
                        EnsureSpace(pos, 4, bufferCapacity);
                        result[pos++] = SsQmkPrefix;
                        result[pos++] = SsModTapCode;
                        result[pos++] = mt.Keycode;
                        result[pos++] = mt.Mods;
                        break;

                    case MacroDelayAction delay:
                        var delayStr = delay.DelayMs.ToString();
                        EnsureSpace(pos, 2 + delayStr.Length + 1, bufferCapacity);
                        result[pos++] = SsQmkPrefix;
                        result[pos++] = SsDelayCode;
                        foreach (var c in delayStr)
                            result[pos++] = (byte)c;
                        result[pos++] = DelayTerminator;
                        break;

                    case MacroTextAction text:
                        var t = text.Text;
                        if (t.Length > 0)
                        {
                            // Text bytes go in raw — no escape. A byte <0x20 collides
                            // with Terminator (0x00) / SsQmkPrefix (0x01) and scrambles
                            // decode. A char >0xFF truncates silently to a low byte.
                            // Reject both loudly at the encode boundary.
                            for (var i = 0; i < t.Length; i++)
                            {
                                var c = t[i];
                                if (c < 0x20 || c > 0xFF)
                                    throw new ArgumentException(
                                        $"MacroTextAction.Text contains non-printable char 0x{(int)c:X4} at index {i}; only bytes 0x20–0xFF allowed.",
                                        nameof(macros));
                            }
                            EnsureSpace(pos, t.Length, bufferCapacity);
                            for (var i = 0; i < t.Length; i++)
                                result[pos++] = (byte)t[i];
                        }
                        break;
                }
            }

            // Write terminator for this macro
            EnsureSpace(pos, 1, bufferCapacity);
            result[pos++] = Terminator;
        }

        return result;
    }

    /// <summary>
    /// Computes the number of bytes required to encode the given macros
    /// (including all terminators).
    /// </summary>
    public static int ComputeEncodedSize(IReadOnlyList<Macro> macros)
    {
        var size = 0;
        foreach (var macro in macros)
        {
            foreach (var action in macro.Actions)
            {
                size += action switch
                {
                    MacroTapAction => 3,   // prefix + tap + keycode
                    MacroDownAction => 3,  // prefix + down + keycode
                    MacroUpAction => 3,    // prefix + up + keycode
                    MacroModTapAction => 4, // prefix + mod_tap + keycode + mods
                    MacroDelayAction d => 2 + d.DelayMs.ToString().Length + 1, // prefix + delay + digits + '|'
                    MacroTextAction t => t.Text.Length,
                    _ => 0,
                };
            }
            size++; // terminator
        }
        return size;
    }

    /// <summary>
    /// Maps an ASCII digit character to its QMK HID keycode.
    /// '1'→0x1E, '2'→0x1F, … '9'→0x26, '0'→0x27.
    /// </summary>
    internal static byte AsciiDigitToHidKeycode(char digit) =>
        digit == '0' ? (byte)0x27 : (byte)(0x1E + (digit - '1'));

    private static void EnsureSpace(int pos, int needed, int capacity)
    {
        if (pos + needed > capacity)
            throw new InvalidOperationException(
                $"Macro buffer overflow: need {pos + needed} bytes but capacity is {capacity}.");
    }
}
