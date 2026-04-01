using SvalboardLayerViz.Core.Keymap;
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
        // MO(2) = 0x5100 + 2 = 0x5102
        var result = _sut.Resolve(0x5102);
        Assert.Equal("MO(2)", result.Label);
        Assert.True(result.IsLayerSwitch);
        Assert.Equal(2, result.TargetLayer);
    }

    [Fact]
    public void Resolve_ToggleLayer1_ReturnsTG1()
    {
        // TG(1) = 0x5300 + 1 = 0x5301
        var result = _sut.Resolve(0x5301);
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
}
