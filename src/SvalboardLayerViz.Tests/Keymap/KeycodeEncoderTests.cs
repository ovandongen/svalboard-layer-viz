using SvalboardLayerViz.Core.Keymap;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

public class KeycodeEncoderTests
{
    // --- Special keycodes ---

    [Fact]
    public void Encode_NoKeycode_Returns0x0000()
    {
        Assert.Equal(0x0000, KeycodeEncoder.Encode(new NoKeycode()));
    }

    [Fact]
    public void Encode_Transparent_Returns0x0001()
    {
        Assert.Equal(0x0001, KeycodeEncoder.Encode(new TransparentKeycode()));
    }

    // --- Basic keycodes ---

    [Theory]
    [InlineData(0x04)] // KC_A
    [InlineData(0x1E)] // KC_1
    [InlineData(0x28)] // KC_ENTER
    [InlineData(0xE0)] // KC_LCTRL
    [InlineData(0xCD)] // MS_UP
    public void Encode_BasicKeycode_ReturnsBaseCode(ushort code)
    {
        Assert.Equal(code, KeycodeEncoder.Encode(new BasicKeycode(code)));
    }

    // --- Modified keycodes ---

    [Theory]
    [InlineData(ModFlags.Shift, 0x04, 0x0204)]
    [InlineData(ModFlags.Ctrl | ModFlags.Shift, 0x06, 0x0306)]
    [InlineData(ModFlags.Alt | ModFlags.Gui, 0x04, 0x0C04)]
    public void Encode_ModifiedKeycode(ModFlags mods, ushort baseCode, ushort expected)
    {
        Assert.Equal(expected, KeycodeEncoder.Encode(new ModifiedKeycode(mods, baseCode)));
    }

    // --- Mod-tap ---

    [Theory]
    [InlineData(ModFlags.Shift, 0x04, 0x2204)]
    [InlineData(ModFlags.Ctrl, 0x28, 0x2128)]
    public void Encode_ModTap(ModFlags mods, ushort baseCode, ushort expected)
    {
        Assert.Equal(expected, KeycodeEncoder.Encode(new ModTapKeycode(mods, baseCode)));
    }

    // --- Layer-tap ---

    [Theory]
    [InlineData(2, 0x04, 0x4204)]
    [InlineData(0, 0x00, 0x4000)]
    public void Encode_LayerTap(int layer, ushort baseCode, ushort expected)
    {
        Assert.Equal(expected, KeycodeEncoder.Encode(new LayerTapKeycode(layer, baseCode)));
    }

    // --- Layer-mod ---

    [Fact]
    public void Encode_LayerMod_Layer3_Ctrl()
    {
        // 0x5000 | (3 << 4) | Ctrl(0x01) = 0x5031
        Assert.Equal(0x5031, KeycodeEncoder.Encode(new LayerModKeycode(3, ModFlags.Ctrl)));
    }

    // --- Layer functions ---

    [Theory]
    [InlineData(LayerFunctionKind.TO,  0, 0x5200)]
    [InlineData(LayerFunctionKind.TO,  3, 0x5203)]
    [InlineData(LayerFunctionKind.MO,  0, 0x5220)]
    [InlineData(LayerFunctionKind.MO,  1, 0x5221)]
    [InlineData(LayerFunctionKind.DF,  2, 0x5242)]
    [InlineData(LayerFunctionKind.TG,  0, 0x5260)]
    [InlineData(LayerFunctionKind.TG,  5, 0x5265)]
    [InlineData(LayerFunctionKind.OSL, 1, 0x5281)]
    [InlineData(LayerFunctionKind.TT,  0, 0x52C0)]
    [InlineData(LayerFunctionKind.TT,  7, 0x52C7)]
    public void Encode_LayerFunction(LayerFunctionKind kind, int layer, ushort expected)
    {
        Assert.Equal(expected, KeycodeEncoder.Encode(new LayerFunctionKeycode(kind, layer)));
    }

    // --- One-shot mod ---

    [Theory]
    [InlineData(ModFlags.Shift, 0x52A2)]
    [InlineData(ModFlags.Ctrl | ModFlags.Alt, 0x52A5)]
    public void Encode_OneShotMod(ModFlags mods, ushort expected)
    {
        Assert.Equal(expected, KeycodeEncoder.Encode(new OneShotModKeycode(mods)));
    }

    // --- Custom keycodes ---

    [Theory]
    [InlineData(0, 0x7E00)]
    [InlineData(1, 0x7E01)]
    [InlineData(63, 0x7E3F)]
    public void Encode_CustomKeycode(int index, ushort expected)
    {
        Assert.Equal(expected, KeycodeEncoder.Encode(new CustomKeycodeDescriptor(index)));
    }

    // --- Named QMK specials ---

    [Theory]
    [InlineData(0x7C79)] // QK_REPEAT_KEY
    [InlineData(0x7C7B)] // QK_LAYER_LOCK
    public void Encode_SpecialKeycode_PassthroughCode(ushort code)
    {
        Assert.Equal(code, KeycodeEncoder.Encode(new SpecialKeycode(code)));
    }

    // --- Raw keycodes ---

    [Theory]
    [InlineData(0x5300)]
    [InlineData(0xFFFF)]
    public void Encode_RawKeycode_PassthroughValue(ushort value)
    {
        Assert.Equal(value, KeycodeEncoder.Encode(new RawKeycode(value)));
    }

    // --- Round-trip tests: encode(decode(x)) == x ---

    [Theory]
    [InlineData(0x0000)] // KC_NO
    [InlineData(0x0001)] // KC_TRNS
    [InlineData(0x0004)] // KC_A
    [InlineData(0x00E0)] // KC_LCTRL
    [InlineData(0x0204)] // Shift+A
    [InlineData(0x2204)] // MT(Shift, A)
    [InlineData(0x4204)] // LT(2, A)
    [InlineData(0x5031)] // LM(3, Ctrl)
    [InlineData(0x5220)] // MO(0)
    [InlineData(0x5221)] // MO(1)
    [InlineData(0x5260)] // TG(0)
    [InlineData(0x5281)] // OSL(1)
    [InlineData(0x52A2)] // OSM(Shift)
    [InlineData(0x52C3)] // TT(3)
    [InlineData(0x7E00)] // Custom 0
    [InlineData(0x7E0A)] // Custom 10
    [InlineData(0x7C79)] // QK_REPEAT_KEY
    [InlineData(0x7C7B)] // QK_LAYER_LOCK
    [InlineData(0x7700)] // Macro 0
    [InlineData(0x7701)] // Macro 1
    [InlineData(0x770F)] // Macro 15
    public void RoundTrip_EncodeDecodeReturnsOriginal(ushort original)
    {
        var descriptor = KeycodeDecoder.Decode(original);
        var reEncoded = KeycodeEncoder.Encode(descriptor);
        Assert.Equal(original, reEncoded);
    }

    // --- Macro keycodes ---

    [Theory]
    [InlineData(0, 0x7700)]
    [InlineData(1, 0x7701)]
    [InlineData(15, 0x770F)]
    [InlineData(255, 0x77FF)]
    public void Encode_MacroKeycode(int index, ushort expected)
    {
        Assert.Equal(expected, KeycodeEncoder.Encode(new MacroKeycode(index)));
    }
}
