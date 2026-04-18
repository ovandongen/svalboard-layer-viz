using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

/// <summary>
/// Boundary coverage for every structured keycode range's edges (first/last value),
/// gap ranges, and null/empty custom-keycode fallbacks. Ensures the decoder's
/// range comparisons don't drift off-by-one.
/// </summary>
public class KeycodeServiceBoundaryTests
{
    private readonly KeycodeService _sut = new();

    // --- TO (0x5200–0x521F) ---

    [Theory]
    [InlineData(0x5200, 0)]  // TO(0)
    [InlineData(0x521F, 31)] // TO(31) max
    public void Resolve_ToBoundaries_ParseCorrectly(ushort code, int expectedLayer)
    {
        var info = _sut.Resolve(code);
        Assert.Equal($"TO({expectedLayer})", info.Label);
        Assert.Equal(expectedLayer, info.TargetLayer);
        Assert.Equal(LayerSwitchType.Activate, info.SwitchType);
    }

    // --- MO (0x5220–0x523F) ---

    [Theory]
    [InlineData(0x5220, 0)]
    [InlineData(0x523F, 31)]
    public void Resolve_MoBoundaries_ParseCorrectly(ushort code, int expectedLayer)
    {
        var info = _sut.Resolve(code);
        Assert.Equal($"MO({expectedLayer})", info.Label);
        Assert.Equal(LayerSwitchType.Momentary, info.SwitchType);
    }

    // --- DF (0x5240–0x525F) ---

    [Theory]
    [InlineData(0x5240, 0)]
    [InlineData(0x525F, 31)]
    public void Resolve_DfBoundaries_ParseCorrectly(ushort code, int expectedLayer)
    {
        var info = _sut.Resolve(code);
        Assert.Equal($"DF({expectedLayer})", info.Label);
        Assert.Equal(LayerSwitchType.Activate, info.SwitchType);
    }

    // --- TG (0x5260–0x527F) ---

    [Theory]
    [InlineData(0x5260, 0)]
    [InlineData(0x527F, 31)]
    public void Resolve_TgBoundaries_ParseCorrectly(ushort code, int expectedLayer)
    {
        var info = _sut.Resolve(code);
        Assert.Equal($"TG({expectedLayer})", info.Label);
        Assert.Equal(LayerSwitchType.Toggle, info.SwitchType);
    }

    // --- OSL (0x5280–0x529F) ---

    [Theory]
    [InlineData(0x5280, 0)]
    [InlineData(0x529F, 31)]
    public void Resolve_OslBoundaries_ParseCorrectly(ushort code, int expectedLayer)
    {
        var info = _sut.Resolve(code);
        Assert.Equal($"OSL({expectedLayer})", info.Label);
        Assert.Equal(LayerSwitchType.OneShot, info.SwitchType);
    }

    // --- TT (0x52C0–0x52DF) ---

    [Theory]
    [InlineData(0x52C0, 0)]
    [InlineData(0x52DF, 31)]
    public void Resolve_TtBoundaries_ParseCorrectly(ushort code, int expectedLayer)
    {
        var info = _sut.Resolve(code);
        Assert.Equal($"TT({expectedLayer})", info.Label);
        Assert.Equal(LayerSwitchType.Momentary, info.SwitchType);
    }

    // --- Mod-Tap (0x2000–0x3FFF) ---

    [Theory]
    [InlineData(0x2000)] // lowest — no mods, no base
    [InlineData(0x3FFF)] // highest — all 5 mod bits + full base
    public void Resolve_ModTapBoundaries_AreDecodedAsMt(ushort code)
    {
        var info = _sut.Resolve(code);
        // MT should produce a SecondaryLabel starting with "MT(" (may be empty-mod "MT()" at low edge)
        Assert.NotNull(info.SecondaryLabel);
        Assert.StartsWith("MT(", info.SecondaryLabel);
    }

    // --- Layer-Tap (0x4000–0x4FFF) ---

    [Theory]
    [InlineData(0x4000, 0)]  // LT(0) no base → label "LT(0)"
    [InlineData(0x4F00, 15)] // LT(15) no base
    public void Resolve_LayerTapLayerBoundaries_ParseCorrectly(ushort code, int expectedLayer)
    {
        var info = _sut.Resolve(code);
        Assert.True(info.IsLayerSwitch);
        Assert.Equal(expectedLayer, info.TargetLayer);
        Assert.Equal(LayerSwitchType.Momentary, info.SwitchType);
    }

    // --- Layer-Mod (0x5000–0x51FF) ---

    [Theory]
    [InlineData(0x5000, 0)]
    [InlineData(0x51F0, 15)] // layer=15, mods=0
    public void Resolve_LayerModBoundaries_ParseCorrectly(ushort code, int expectedLayer)
    {
        var info = _sut.Resolve(code);
        Assert.True(info.IsLayerSwitch);
        Assert.Equal(expectedLayer, info.TargetLayer);
        Assert.StartsWith("LM(", info.Label);
    }

    // --- One-shot Mod (0x52A0–0x52BF) ---

    [Theory]
    [InlineData(0x52A0)]
    [InlineData(0x52BF)]
    public void Resolve_OneShotModBoundaries_LabelAsOsm(ushort code)
    {
        var info = _sut.Resolve(code);
        Assert.StartsWith("OSM(", info.Label);
    }

    // --- Tap-dance (0x5700–0x57FF) ---

    [Theory]
    [InlineData(0x5700, 0)]
    [InlineData(0x57FF, 255)]
    public void Resolve_TapDanceBoundaries_LabelAsTd(ushort code, int expectedIndex)
    {
        var info = _sut.Resolve(code);
        Assert.Equal($"TD{expectedIndex}", info.Label);
    }

    // --- Macro (0x7700–0x77FF) ---

    [Theory]
    [InlineData(0x7700, 0)]
    [InlineData(0x77FF, 255)]
    public void Resolve_MacroBoundaries_LabelAsM(ushort code, int expectedIndex)
    {
        var info = _sut.Resolve(code);
        Assert.Equal($"M{expectedIndex}", info.Label);
    }

    // --- Custom/KB (0x7E00–0x7FFF) ---

    [Theory]
    [InlineData(0x7E00, 0)]
    [InlineData(0x7FFF, 511)]
    public void Resolve_CustomKeycode_NoCatalog_FallsBackToKbLabel(ushort code, int expectedIndex)
    {
        var info = _sut.Resolve(code);
        Assert.Equal($"KB{expectedIndex}", info.Label);
    }

    // --- Gap ranges — not in any structured range, should be RawKeycode ---

    [Theory]
    [InlineData((ushort)0x52E0)] // gap between TT_MAX (0x52DF) and tap-dance
    [InlineData((ushort)0x56FF)] // gap before tap-dance
    [InlineData((ushort)0x5800)] // gap after tap-dance_max
    [InlineData((ushort)0x76FF)] // gap before macro
    [InlineData((ushort)0x7800)] // gap after macro_max
    [InlineData((ushort)0x7D00)] // gap before QK_KB
    [InlineData((ushort)0xFFFF)] // top of range
    public void Resolve_GapRange_MarkedUnknown(ushort code)
    {
        var info = _sut.Resolve(code);
        Assert.True(info.IsUnknown);
        Assert.Equal($"0x{code:X4}", info.Label);
    }

    // --- Custom keycode fallbacks ---

    [Fact]
    public void Resolve_CustomKeycode_EmptyCatalog_FallsBackToKbLabel()
    {
        _sut.SetCustomKeycodes([]);
        var info = _sut.Resolve(0x7E00);
        Assert.Equal("KB0", info.Label);
    }

    [Fact]
    public void Resolve_CustomKeycode_IndexBeyondCatalog_FallsBackToKbLabel()
    {
        _sut.SetCustomKeycodes([Ck("ONLY_ONE", "O1")]);
        var info = _sut.Resolve(0x7E05); // index 5, catalog only has index 0
        Assert.Equal("KB5", info.Label);
    }

    [Fact]
    public void Resolve_CustomKeycode_UsesShortNameWhenPresent()
    {
        _sut.SetCustomKeycodes([Ck("CUSTOM_LONG_NAME", "SN")]);
        var info = _sut.Resolve(0x7E00);
        Assert.Equal("SN", info.Label);
    }

    [Fact]
    public void Resolve_CustomKeycode_UsesNameWhenShortNameEmpty()
    {
        _sut.SetCustomKeycodes([Ck("FULL_NAME", "")]);
        var info = _sut.Resolve(0x7E00);
        Assert.Equal("FULL_NAME", info.Label);
    }

    private static CustomKeycode Ck(string name, string shortName) =>
        new() { Name = name, Title = name, ShortName = shortName };

    // --- Custom key labels (user settings) ---

    [Fact]
    public void Resolve_CustomKeyLabel_OverridesDecodedLabel()
    {
        _sut.SetCustomKeyLabels(new Dictionary<string, string> { ["0x0004"] = "MyA" });
        var info = _sut.Resolve(0x0004);
        Assert.Equal("MyA", info.Label);
    }

    [Fact]
    public void Resolve_CustomKeyLabels_Null_DoesNotThrow()
    {
        _sut.SetCustomKeyLabels(null);
        var info = _sut.Resolve(0x0004);
        Assert.Equal("A", info.Label);
    }

    // --- Modifier-only keycodes (modified base = 0 is not a ModifiedKeycode) ---

    [Theory]
    [InlineData(0x0100)] // ctrl-only — no base key
    [InlineData(0x0200)] // shift-only
    [InlineData(0x0400)] // alt-only
    [InlineData(0x0800)] // gui-only
    public void Resolve_ModOnlyWithNoBase_IsUnknown(ushort code)
    {
        // High nibble set, low byte 0 — decoder's ModifiedKeycode guard requires
        // both halves non-zero, so these fall through to RawKeycode.
        var info = _sut.Resolve(code);
        Assert.True(info.IsUnknown);
    }
}
