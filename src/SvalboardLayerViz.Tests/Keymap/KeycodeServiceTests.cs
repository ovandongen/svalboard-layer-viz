using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

public class KeycodeServiceTests
{
    private readonly KeycodeService _sut = new();

    [Fact]
    public void Resolve_NoKey_ReturnsEmpty()
    {
        var result = _sut.Resolve(0x0000);
        Assert.True(result.IsEmpty);
        Assert.Equal("", result.Label);
    }

    [Fact]
    public void Resolve_Transparent_ReturnsTransparent()
    {
        var result = _sut.Resolve(0x0001);
        Assert.True(result.IsTransparent);
        Assert.Equal("___", result.Label);
    }

    [Fact]
    public void Resolve_BasicKey_A_ReturnsA()
    {
        var result = _sut.Resolve(0x0004);
        Assert.Equal("A", result.Label);
        Assert.False(result.IsTransparent);
        Assert.False(result.IsEmpty);
    }

    [Fact]
    public void Resolve_BasicKey_Space_ReturnsSpace()
    {
        var result = _sut.Resolve(0x002C);
        Assert.Equal("Space", result.Label);
    }

    [Fact]
    public void Resolve_MomentaryLayer2_ReturnsMO2()
    {
        // MO(2) = 0x5220 + 2 = 0x5222
        var result = _sut.Resolve(0x5222);
        Assert.Equal("MO(2)", result.Label);
        Assert.True(result.IsLayerSwitch);
        Assert.Equal(2, result.TargetLayer);
    }

    [Fact]
    public void Resolve_ToggleLayer1_ReturnsTG1()
    {
        // TG(1) = 0x5260 + 1 = 0x5261
        var result = _sut.Resolve(0x5261);
        Assert.Equal("TG(1)", result.Label);
        Assert.True(result.IsLayerSwitch);
        Assert.Equal(1, result.TargetLayer);
    }

    [Fact]
    public void Resolve_CtrlA_ReturnsModifiedKey()
    {
        // Ctrl (0x01) << 8 | A (0x04) = 0x0104
        var result = _sut.Resolve(0x0104);
        Assert.Equal("A", result.Label);
        Assert.Equal("Ctrl", result.SecondaryLabel);
    }

    [Fact]
    public void Resolve_UnknownKeycode_ReturnsHex()
    {
        var result = _sut.Resolve(0xFFFF);
        Assert.StartsWith("0x", result.Label);
    }

    // --- Batch 1: Layer function range tests (keybard-ng keygen.ts ranges) ---

    [Fact]
    public void Resolve_TO_Layer0_ReturnsTO0()
    {
        // TO(0) = 0x5200
        var result = _sut.Resolve(0x5200);
        Assert.Equal("TO(0)", result.Label);
        Assert.True(result.IsLayerSwitch);
        Assert.Equal(0, result.TargetLayer);
    }

    [Fact]
    public void Resolve_MO_Layer3_ReturnsMO3()
    {
        // MO(3) = 0x5220 + 3 = 0x5223
        var result = _sut.Resolve(0x5223);
        Assert.Equal("MO(3)", result.Label);
        Assert.True(result.IsLayerSwitch);
        Assert.Equal(3, result.TargetLayer);
    }

    [Fact]
    public void Resolve_DF_Layer2_ReturnsDF2()
    {
        // DF(2) = 0x5240 + 2 = 0x5242
        var result = _sut.Resolve(0x5242);
        Assert.Equal("DF(2)", result.Label);
        Assert.True(result.IsLayerSwitch);
        Assert.Equal(2, result.TargetLayer);
    }

    [Fact]
    public void Resolve_OSL_Layer1_ReturnsOSL1()
    {
        // OSL(1) = 0x5280 + 1 = 0x5281
        var result = _sut.Resolve(0x5281);
        Assert.Equal("OSL(1)", result.Label);
        Assert.True(result.IsLayerSwitch);
        Assert.Equal(1, result.TargetLayer);
    }

    [Fact]
    public void Resolve_TT_Layer4_ReturnsTT4()
    {
        // TT(4) = 0x52C0 + 4 = 0x52C4
        var result = _sut.Resolve(0x52C4);
        Assert.Equal("TT(4)", result.Label);
        Assert.True(result.IsLayerSwitch);
        Assert.Equal(4, result.TargetLayer);
    }

    [Fact]
    public void Resolve_OneShotMod_Shift_ReturnsOSM()
    {
        // OSM(Shift) = 0x52A0 + 0x02 = 0x52A2
        var result = _sut.Resolve(0x52A2);
        Assert.Equal("OSM(Shift)", result.Label);
        Assert.False(result.IsLayerSwitch);
    }

    [Fact]
    public void Resolve_AllLayerFunctions_DoNotOverlap()
    {
        // Verify that each range boundary resolves to the correct function type
        Assert.Equal("TO(0)", _sut.Resolve(0x5200).Label);     // TO start
        Assert.Equal("TO(31)", _sut.Resolve(0x521F).Label);    // TO end
        Assert.Equal("MO(0)", _sut.Resolve(0x5220).Label);     // MO start (right after TO)
        Assert.Equal("MO(31)", _sut.Resolve(0x523F).Label);    // MO end
        Assert.Equal("DF(0)", _sut.Resolve(0x5240).Label);     // DF start (right after MO)
        Assert.Equal("TG(0)", _sut.Resolve(0x5260).Label);     // TG start
        Assert.Equal("OSL(0)", _sut.Resolve(0x5280).Label);    // OSL start
        Assert.Equal("TT(0)", _sut.Resolve(0x52C0).Label);     // TT start
    }

    // --- Batch 2: Expanded keycodes ---

    [Theory]
    [InlineData(0x53, "NumLk")]
    [InlineData(0x59, "KP 1")]
    [InlineData(0x62, "KP 0")]
    [InlineData(0x63, "KP .")]
    [InlineData(0x57, "KP +")]
    public void Resolve_NumpadKeys_ReturnLabels(ushort keycode, string expected)
    {
        Assert.Equal(expected, _sut.Resolve(keycode).Label);
    }

    [Theory]
    [InlineData(0x68, "F13")]
    [InlineData(0x6E, "F19")]
    [InlineData(0x73, "F24")]
    public void Resolve_ExtendedFKeys_ReturnLabels(ushort keycode, string expected)
    {
        Assert.Equal(expected, _sut.Resolve(keycode).Label);
    }

    [Fact]
    public void Resolve_MediaMute_ReturnsMute()
    {
        Assert.Equal("Mute", _sut.Resolve(0x7F).Label);
    }

    [Fact]
    public void Resolve_AppKey_ReturnsApp()
    {
        Assert.Equal("App", _sut.Resolve(0x65).Label);
    }

    [Fact]
    public void Resolve_ModTap_CtrlA()
    {
        // MT(Ctrl, A) = 0x2000 | (0x01 << 8) | 0x04 = 0x2104
        var result = _sut.Resolve(0x2104);
        Assert.Equal("A", result.Label);
        Assert.Equal("MT(Ctrl)", result.SecondaryLabel);
    }

    [Fact]
    public void Resolve_LayerTap_Layer2_A()
    {
        // LT(2, A) = 0x4000 | (2 << 8) | 0x04 = 0x4204
        var result = _sut.Resolve(0x4204);
        Assert.Equal("A", result.Label);
        Assert.Equal("LT(2)", result.SecondaryLabel);
        Assert.True(result.IsLayerSwitch);
        Assert.Equal(2, result.TargetLayer);
    }

    [Fact]
    public void Resolve_CustomKeycode_ReturnsShortName()
    {
        _sut.SetCustomKeycodes([
            new CustomKeycode { Name = "My Custom", Title = "Custom Key", ShortName = "CUST" }
        ]);
        // USER0 = 0x7E40
        var result = _sut.Resolve(0x7E40);
        Assert.Equal("CUST", result.Label);
    }

    [Fact]
    public void Resolve_CustomKeycode_FallsBackToName_WhenNoShortName()
    {
        _sut.SetCustomKeycodes([
            new CustomKeycode { Name = "MyKey", Title = "", ShortName = "" }
        ]);
        var result = _sut.Resolve(0x7E40);
        Assert.Equal("MyKey", result.Label);
    }

    [Fact]
    public void Resolve_CustomKeycode_OutOfRange_ReturnsUserN()
    {
        _sut.SetCustomKeycodes([]);
        var result = _sut.Resolve(0x7E40);
        Assert.Equal("USER0", result.Label);
    }

    // --- Batch 5c: Edge-case tests ---

    [Theory]
    [InlineData(0x04, "A"), InlineData(0x05, "B"), InlineData(0x06, "C"), InlineData(0x07, "D")]
    [InlineData(0x08, "E"), InlineData(0x09, "F"), InlineData(0x0A, "G"), InlineData(0x0B, "H")]
    [InlineData(0x0C, "I"), InlineData(0x0D, "J"), InlineData(0x0E, "K"), InlineData(0x0F, "L")]
    [InlineData(0x10, "M"), InlineData(0x11, "N"), InlineData(0x12, "O"), InlineData(0x13, "P")]
    [InlineData(0x14, "Q"), InlineData(0x15, "R"), InlineData(0x16, "S"), InlineData(0x17, "T")]
    [InlineData(0x18, "U"), InlineData(0x19, "V"), InlineData(0x1A, "W"), InlineData(0x1B, "X")]
    [InlineData(0x1C, "Y"), InlineData(0x1D, "Z")]
    public void Resolve_AllBasicLetters_AtoZ(ushort keycode, string expected)
    {
        Assert.Equal(expected, _sut.Resolve(keycode).Label);
    }

    [Theory]
    [InlineData(0xE0, "LCtrl"), InlineData(0xE1, "LShift"), InlineData(0xE2, "LAlt"), InlineData(0xE3, "LGUI")]
    [InlineData(0xE4, "RCtrl"), InlineData(0xE5, "RShift"), InlineData(0xE6, "RAlt"), InlineData(0xE7, "RGUI")]
    public void Resolve_ModifierKeys_AllEight(ushort keycode, string expected)
    {
        Assert.Equal(expected, _sut.Resolve(keycode).Label);
    }

    [Fact]
    public void Resolve_ShiftAlt_CompoundModifiers()
    {
        // Shift(0x02) + Alt(0x04) = 0x06, key A = 0x04 → 0x0604
        var result = _sut.Resolve(0x0604);
        Assert.Equal("A", result.Label);
        Assert.Contains("Shift", result.SecondaryLabel!);
        Assert.Contains("Alt", result.SecondaryLabel!);
    }

    [Fact]
    public void Resolve_LayerMod_Layer1Ctrl()
    {
        // LM(1, Ctrl) = 0x5000 | (1 << 4) | 0x01 = 0x5011
        var result = _sut.Resolve(0x5011);
        Assert.Equal("LM(1)", result.Label);
        Assert.Equal("Ctrl", result.SecondaryLabel);
        Assert.True(result.IsLayerSwitch);
        Assert.Equal(1, result.TargetLayer);
    }
}
