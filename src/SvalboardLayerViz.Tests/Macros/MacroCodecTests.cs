using System.IO;
using SvalboardLayerViz.Core.Macros;
using Xunit;

namespace SvalboardLayerViz.Tests.Macros;

public class MacroCodecTests
{
    // --- Decode tests ---

    [Fact]
    public void Decode_EmptyBuffer_ProducesEmptyMacros()
    {
        // 3 macros, all empty (just terminators)
        var buffer = new byte[] { 0x00, 0x00, 0x00 };
        var result = MacroCodec.Decode(buffer, 3, 64);

        Assert.Equal(3, result.Macros.Count);
        Assert.All(result.Macros, m => Assert.Empty(m.Actions));
    }

    [Fact]
    public void Decode_SingleTapAction()
    {
        // Macro 0: SS_QMK_PREFIX + SS_TAP_CODE + keycode 0x04 (A)
        var buffer = new byte[] { 0x01, 0x01, 0x04, 0x00 };
        var result = MacroCodec.Decode(buffer, 1, 64);

        Assert.Single(result.Macros);
        var action = Assert.Single(result.Macros[0].Actions);
        var tap = Assert.IsType<MacroTapAction>(action);
        Assert.Equal(0x04, tap.Keycode);
    }

    [Fact]
    public void Decode_DownAndUpActions()
    {
        // Macro 0: prefix+down+0x04, prefix+up+0x04
        var buffer = new byte[] { 0x01, 0x02, 0x04, 0x01, 0x03, 0x04, 0x00 };
        var result = MacroCodec.Decode(buffer, 1, 64);

        Assert.Equal(2, result.Macros[0].Actions.Count);
        var down = Assert.IsType<MacroDownAction>(result.Macros[0].Actions[0]);
        Assert.Equal(0x04, down.Keycode);
        var up = Assert.IsType<MacroUpAction>(result.Macros[0].Actions[1]);
        Assert.Equal(0x04, up.Keycode);
    }

    [Fact]
    public void Decode_DelayAction()
    {
        // Macro 0: prefix + SS_DELAY_CODE + "500" + '|'
        var buffer = new byte[] { 0x01, 0x04, (byte)'5', (byte)'0', (byte)'0', (byte)'|', 0x00 };
        var result = MacroCodec.Decode(buffer, 1, 64);

        var action = Assert.Single(result.Macros[0].Actions);
        var delay = Assert.IsType<MacroDelayAction>(action);
        Assert.Equal(500, delay.DelayMs);
    }

    [Fact]
    public void Decode_DelayZero()
    {
        // prefix + SS_DELAY_CODE + "0" + '|'
        var buffer = new byte[] { 0x01, 0x04, (byte)'0', (byte)'|', 0x00 };
        var result = MacroCodec.Decode(buffer, 1, 64);

        var delay = Assert.IsType<MacroDelayAction>(Assert.Single(result.Macros[0].Actions));
        Assert.Equal(0, delay.DelayMs);
    }

    [Fact]
    public void Decode_DelayNoDigitsThrows()
    {
        // prefix + SS_DELAY_CODE + '|' — firmware emitting a delay with no
        // digits is malformed. Silently decoding to 0 re-encodes as "0|",
        // a semantic round-trip change. Refuse.
        var buffer = new byte[] { 0x01, 0x04, (byte)'|', 0x00 };

        Assert.Throws<InvalidDataException>(() => MacroCodec.Decode(buffer, 1, 64));
    }

    [Fact]
    public void Decode_DelayMissingPipeThrows()
    {
        // prefix + SS_DELAY_CODE + "50" + terminator (no pipe) — firmware must
        // always emit '|'; missing terminator is a real corruption, not a
        // graceful shortcut. Refuse to guess a value.
        var buffer = new byte[] { 0x01, 0x04, (byte)'5', (byte)'0', 0x00 };

        Assert.Throws<InvalidDataException>(() => MacroCodec.Decode(buffer, 1, 64));
    }

    [Fact]
    public void Decode_TextAction()
    {
        // "Hello" as printable ASCII
        var buffer = "Hello\0"u8.ToArray();
        var result = MacroCodec.Decode(buffer, 1, 64);

        var action = Assert.Single(result.Macros[0].Actions);
        var text = Assert.IsType<MacroTextAction>(action);
        Assert.Equal("Hello", text.Text);
    }

    [Fact]
    public void Decode_MixedActions()
    {
        // prefix+down(Shift) + text "a" + prefix+up(Shift) + prefix+delay "50|"
        var buffer = new byte[]
        {
            0x01, 0x02, 0xE1,       // down Shift
            (byte)'a',              // text "a"
            0x01, 0x03, 0xE1,       // up Shift
            0x01, 0x04, (byte)'5', (byte)'0', (byte)'|', // delay 50ms
            0x00                    // terminator
        };
        var result = MacroCodec.Decode(buffer, 1, 64);

        var actions = result.Macros[0].Actions;
        Assert.Equal(4, actions.Count);
        Assert.IsType<MacroDownAction>(actions[0]);
        Assert.IsType<MacroTextAction>(actions[1]);
        Assert.IsType<MacroUpAction>(actions[2]);
        Assert.IsType<MacroDelayAction>(actions[3]);
    }

    [Fact]
    public void Decode_MultipleMacros()
    {
        // Macro 0: text "A", Macro 1: text "B", Macro 2: empty
        var buffer = new byte[] { (byte)'A', 0x00, (byte)'B', 0x00, 0x00 };
        var result = MacroCodec.Decode(buffer, 3, 64);

        Assert.Equal(3, result.Macros.Count);
        Assert.Equal("A", Assert.IsType<MacroTextAction>(Assert.Single(result.Macros[0].Actions)).Text);
        Assert.Equal("B", Assert.IsType<MacroTextAction>(Assert.Single(result.Macros[1].Actions)).Text);
        Assert.Empty(result.Macros[2].Actions);
    }

    [Fact]
    public void Decode_MacroIndicesAreCorrect()
    {
        var buffer = new byte[] { 0x00, 0x00, 0x00 };
        var result = MacroCodec.Decode(buffer, 3, 64);

        Assert.Equal(0, result.Macros[0].Index);
        Assert.Equal(1, result.Macros[1].Index);
        Assert.Equal(2, result.Macros[2].Index);
    }

    [Fact]
    public void Decode_BufferCapacityPreserved()
    {
        var buffer = new byte[] { 0x00 };
        var result = MacroCodec.Decode(buffer, 1, 512);

        Assert.Equal(512, result.BufferCapacity);
    }

    [Fact]
    public void Decode_UnknownPrefixedAction_Throws()
    {
        // Silently skipping an unknown action loses firmware data on re-encode.
        // prefix + 0x09 (unknown) + text "A"
        var buffer = new byte[] { 0x01, 0x09, (byte)'A', 0x00 };

        var ex = Assert.Throws<InvalidDataException>(() => MacroCodec.Decode(buffer, 1, 64));
        Assert.Contains("0x09", ex.Message);
    }

    // --- Encode tests ---

    [Fact]
    public void Encode_EmptyMacros()
    {
        var macros = new List<Macro>
        {
            new(0, []),
            new(1, []),
        };
        var encoded = MacroCodec.Encode(macros, 16);

        Assert.Equal(0x00, encoded[0]);
        Assert.Equal(0x00, encoded[1]);
        Assert.Equal(16, encoded.Length);
    }

    [Fact]
    public void Encode_TapAction()
    {
        var macros = new List<Macro>
        {
            new(0, [new MacroTapAction(0x04)])
        };
        var encoded = MacroCodec.Encode(macros, 16);

        Assert.Equal(0x01, encoded[0]); // SS_QMK_PREFIX
        Assert.Equal(0x01, encoded[1]); // SS_TAP_CODE
        Assert.Equal(0x04, encoded[2]); // keycode
        Assert.Equal(0x00, encoded[3]); // terminator
    }

    [Fact]
    public void Encode_DownUpActions()
    {
        var macros = new List<Macro>
        {
            new(0, [new MacroDownAction(0xE1), new MacroUpAction(0xE1)])
        };
        var encoded = MacroCodec.Encode(macros, 16);

        Assert.Equal(0x01, encoded[0]); // prefix
        Assert.Equal(0x02, encoded[1]); // SS_DOWN_CODE
        Assert.Equal(0xE1, encoded[2]); // keycode
        Assert.Equal(0x01, encoded[3]); // prefix
        Assert.Equal(0x03, encoded[4]); // SS_UP_CODE
        Assert.Equal(0xE1, encoded[5]); // keycode
        Assert.Equal(0x00, encoded[6]); // terminator
    }

    [Fact]
    public void Encode_DelayAction()
    {
        var macros = new List<Macro>
        {
            new(0, [new MacroDelayAction(500)])
        };
        var encoded = MacroCodec.Encode(macros, 16);

        Assert.Equal(0x01, encoded[0]);       // prefix
        Assert.Equal(0x04, encoded[1]);       // SS_DELAY_CODE
        Assert.Equal((byte)'5', encoded[2]);
        Assert.Equal((byte)'0', encoded[3]);
        Assert.Equal((byte)'0', encoded[4]);
        Assert.Equal((byte)'|', encoded[5]);  // pipe terminator
        Assert.Equal(0x00, encoded[6]);       // macro terminator
    }

    [Fact]
    public void Encode_TextAction()
    {
        var macros = new List<Macro>
        {
            new(0, [new MacroTextAction("Hi")])
        };
        var encoded = MacroCodec.Encode(macros, 16);

        Assert.Equal((byte)'H', encoded[0]);
        Assert.Equal((byte)'i', encoded[1]);
        Assert.Equal(0x00, encoded[2]); // terminator
    }

    [Fact]
    public void Encode_TextContaining0x01_Throws()
    {
        // 0x01 is SS_QMK_PREFIX — embedding it raw in text scrambles decode.
        var macros = new List<Macro>
        {
            new(0, [new MacroTextAction("bad\x01text")])
        };

        var ex = Assert.Throws<ArgumentException>(() => MacroCodec.Encode(macros, 64));
        Assert.Contains("0x0001", ex.Message);
    }

    [Fact]
    public void Encode_TextContainingNull_Throws()
    {
        // 0x00 is the macro terminator — embedding it cuts the macro short on decode.
        var macros = new List<Macro>
        {
            new(0, [new MacroTextAction("a\0b")])
        };

        Assert.Throws<ArgumentException>(() => MacroCodec.Encode(macros, 64));
    }

    [Fact]
    public void Encode_TextContainingHighUnicode_Throws()
    {
        // Chars > 0xFF truncate silently on (byte) cast — emoji would produce garbage bytes.
        var macros = new List<Macro>
        {
            new(0, [new MacroTextAction("hi\u2603")])  // snowman U+2603
        };

        Assert.Throws<ArgumentException>(() => MacroCodec.Encode(macros, 64));
    }

    [Fact]
    public void Encode_OverflowThrows()
    {
        var macros = new List<Macro>
        {
            new(0, [new MacroTextAction("This text is way too long for a tiny buffer")])
        };

        Assert.Throws<InvalidOperationException>(() => MacroCodec.Encode(macros, 4));
    }

    // --- Round-trip tests ---

    [Fact]
    public void RoundTrip_EmptyMacros()
    {
        var original = new byte[] { 0x00, 0x00, 0x00 };
        var decoded = MacroCodec.Decode(original, 3, 64);
        var encoded = MacroCodec.Encode(decoded);

        for (var i = 0; i < 3; i++)
            Assert.Equal(original[i], encoded[i]);
    }

    [Fact]
    public void RoundTrip_ComplexMacro()
    {
        var original = new byte[]
        {
            0x01, 0x01, 0x04,        // tap A
            0x01, 0x02, 0xE1,        // down Shift
            (byte)'h', (byte)'i',    // text "hi"
            0x01, 0x03, 0xE1,        // up Shift
            0x01, 0x04, (byte)'1', (byte)'0', (byte)'0', (byte)'|', // delay 100
            0x00,                    // terminator macro 0
            (byte)'B',              // text "B"
            0x00,                    // terminator macro 1
        };
        var decoded = MacroCodec.Decode(original, 2, 64);
        var encoded = MacroCodec.Encode(decoded);

        for (var i = 0; i < original.Length; i++)
            Assert.Equal(original[i], encoded[i]);
    }

    [Fact]
    public void RoundTrip_TextAndTap()
    {
        // The exact case the user reported: text followed by tap
        var macros = new List<Macro>
        {
            new(0, [new MacroTextAction("hello"), new MacroTapAction(0x05)])
        };
        var encoded = MacroCodec.Encode(macros, 64);

        // Verify bytes: "hello" + prefix + tap + 0x05 + terminator
        Assert.Equal((byte)'h', encoded[0]);
        Assert.Equal((byte)'e', encoded[1]);
        Assert.Equal((byte)'l', encoded[2]);
        Assert.Equal((byte)'l', encoded[3]);
        Assert.Equal((byte)'o', encoded[4]);
        Assert.Equal(0x01, encoded[5]);  // prefix
        Assert.Equal(0x01, encoded[6]);  // tap
        Assert.Equal(0x05, encoded[7]);  // B keycode
        Assert.Equal(0x00, encoded[8]);  // terminator

        // Round-trip
        var decoded = MacroCodec.Decode(encoded, 1, 64);
        Assert.Equal(2, decoded.Macros[0].Actions.Count);
        Assert.Equal("hello", ((MacroTextAction)decoded.Macros[0].Actions[0]).Text);
        Assert.Equal(0x05, ((MacroTapAction)decoded.Macros[0].Actions[1]).Keycode);
    }

    // --- ComputeEncodedSize tests ---

    [Fact]
    public void ComputeEncodedSize_EmptyMacros()
    {
        var macros = new List<Macro> { new(0, []), new(1, []) };
        Assert.Equal(2, MacroCodec.ComputeEncodedSize(macros)); // 2 terminators
    }

    [Fact]
    public void ComputeEncodedSize_WithActions()
    {
        var macros = new List<Macro>
        {
            new(0, [new MacroTapAction(0x04), new MacroTextAction("AB")]),
        };
        // tap=3 + text=2 + terminator=1 = 6
        Assert.Equal(6, MacroCodec.ComputeEncodedSize(macros));
    }

    [Fact]
    public void ComputeEncodedSize_DelayDigitCounting()
    {
        var macros = new List<Macro>
        {
            new(0, [new MacroDelayAction(5)]),    // prefix+delay+"5"+"|" = 4
            new(1, [new MacroDelayAction(50)]),   // prefix+delay+"50"+"|" = 5
            new(2, [new MacroDelayAction(999)]),  // prefix+delay+"999"+"|" = 6
        };
        // macro 0: 4 + 1(term) = 5
        // macro 1: 5 + 1(term) = 6
        // macro 2: 6 + 1(term) = 7
        Assert.Equal(18, MacroCodec.ComputeEncodedSize(macros));
    }

    // --- Delay + text: pipe terminator eliminates ambiguity ---

    [Fact]
    public void Encode_DelayFollowedByDigitText_NoAmbiguity()
    {
        // With pipe terminator, digits after delay are unambiguously text
        var macros = new List<Macro>
        {
            new(0, [new MacroDelayAction(100), new MacroTextAction("42abc")])
        };
        var encoded = MacroCodec.Encode(macros, 64);
        var decoded = MacroCodec.Decode(encoded, 1, 64);

        Assert.Equal(2, decoded.Macros[0].Actions.Count);
        Assert.Equal(100, ((MacroDelayAction)decoded.Macros[0].Actions[0]).DelayMs);
        Assert.Equal("42abc", ((MacroTextAction)decoded.Macros[0].Actions[1]).Text);
    }

    [Fact]
    public void Encode_DelayFollowedByNonDigitText_RoundTrips()
    {
        var macros = new List<Macro>
        {
            new(0, [new MacroDelayAction(500), new MacroTextAction("hello")])
        };
        var encoded = MacroCodec.Encode(macros, 64);
        var decoded = MacroCodec.Decode(encoded, 1, 64);

        var actions = decoded.Macros[0].Actions;
        Assert.Equal(2, actions.Count);
        Assert.IsType<MacroDelayAction>(actions[0]);
        Assert.IsType<MacroTextAction>(actions[1]);
        Assert.Equal("hello", ((MacroTextAction)actions[1]).Text);
    }

    [Fact]
    public void ComputeEncodedSize_DelayFollowedByDigitText()
    {
        var macros = new List<Macro>
        {
            new(0, [new MacroDelayAction(100), new MacroTextAction("42abc")])
        };
        // delay: 2 + 3 ("100") + 1 ("|") = 6
        // text: 5 ("42abc")
        // terminator: 1
        // total: 12
        Assert.Equal(12, MacroCodec.ComputeEncodedSize(macros));
    }

    // --- Mod-tap (SS_MOD_TAP = 0x05) tests ---

    [Fact]
    public void Decode_ModTapAction()
    {
        // prefix + mod_tap + keycode D (0x07) + LShift (0x02)
        var buffer = new byte[] { 0x01, 0x05, 0x07, 0x02, 0x00 };
        var result = MacroCodec.Decode(buffer, 1, 64);

        var action = Assert.Single(result.Macros[0].Actions);
        var mt = Assert.IsType<MacroModTapAction>(action);
        Assert.Equal(0x07, mt.Keycode);
        Assert.Equal(0x02, mt.Mods);
    }

    [Fact]
    public void Encode_ModTapAction()
    {
        var macros = new List<Macro>
        {
            new(0, [new MacroModTapAction(0x07, 0x02)])
        };
        var encoded = MacroCodec.Encode(macros, 16);

        Assert.Equal(0x01, encoded[0]); // prefix
        Assert.Equal(0x05, encoded[1]); // mod_tap
        Assert.Equal(0x07, encoded[2]); // keycode D
        Assert.Equal(0x02, encoded[3]); // LShift
        Assert.Equal(0x00, encoded[4]); // terminator
    }

    [Fact]
    public void RoundTrip_ModTapAction()
    {
        var original = new byte[]
        {
            0x01, 0x05, 0x07, 0x02,  // mod-tap Shift+D
            0x00
        };
        var decoded = MacroCodec.Decode(original, 1, 64);
        var encoded = MacroCodec.Encode(decoded);

        for (var i = 0; i < original.Length; i++)
            Assert.Equal(original[i], encoded[i]);
    }

    [Fact]
    public void Decode_RealKeybardBuffer_M0()
    {
        // Exact bytes from keybard: text("qaz123") + tap(S) + mod-tap(Shift+D)
        var buffer = new byte[]
        {
            0x71, 0x61, 0x7A, 0x31, 0x32, 0x33, // "qaz123"
            0x01, 0x01, 0x16,                     // tap(S)
            0x01, 0x05, 0x07, 0x02,               // mod-tap(Shift+D)
            0x00
        };
        var result = MacroCodec.Decode(buffer, 1, 64);

        var actions = result.Macros[0].Actions;
        Assert.Equal(3, actions.Count);
        Assert.Equal("qaz123", ((MacroTextAction)actions[0]).Text);
        Assert.Equal(0x16, ((MacroTapAction)actions[1]).Keycode);
        var mt = Assert.IsType<MacroModTapAction>(actions[2]);
        Assert.Equal(0x07, mt.Keycode);
        Assert.Equal(0x02, mt.Mods);
    }

    [Fact]
    public void ComputeEncodedSize_ModTap()
    {
        var macros = new List<Macro>
        {
            new(0, [new MacroModTapAction(0x07, 0x02)])
        };
        // mod_tap=4 + terminator=1 = 5
        Assert.Equal(5, MacroCodec.ComputeEncodedSize(macros));
    }

    [Fact]
    public void AsciiDigitToHidKeycode_CorrectMapping()
    {
        Assert.Equal(0x1E, MacroCodec.AsciiDigitToHidKeycode('1'));
        Assert.Equal(0x1F, MacroCodec.AsciiDigitToHidKeycode('2'));
        Assert.Equal(0x26, MacroCodec.AsciiDigitToHidKeycode('9'));
        Assert.Equal(0x27, MacroCodec.AsciiDigitToHidKeycode('0'));
    }
}
