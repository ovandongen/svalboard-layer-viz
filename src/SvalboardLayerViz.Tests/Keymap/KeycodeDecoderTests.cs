using SvalboardLayerViz.Core.Keymap;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

public class KeycodeDecoderTests
{
    // --- Special keycodes ---

    [Fact]
    public void Decode_0x0000_ReturnsNoKeycode()
    {
        Assert.IsType<NoKeycode>(KeycodeDecoder.Decode(0x0000));
    }

    [Fact]
    public void Decode_0x0001_ReturnsTransparent()
    {
        Assert.IsType<TransparentKeycode>(KeycodeDecoder.Decode(0x0001));
    }

    // --- Basic keycodes ---

    [Theory]
    [InlineData(0x0002)] // edge: lowest non-special basic
    [InlineData(0x0004)] // KC_A
    [InlineData(0x001D)] // KC_Z
    [InlineData(0x0028)] // KC_ENTER
    [InlineData(0x00E0)] // KC_LCTRL
    [InlineData(0x00FF)] // max basic
    public void Decode_BasicRange_ReturnsBasicKeycode(ushort code)
    {
        var result = KeycodeDecoder.Decode(code);
        var basic = Assert.IsType<BasicKeycode>(result);
        Assert.Equal(code, basic.BaseCode);
    }

    // --- Modified keycodes ---

    [Theory]
    [InlineData(0x0204, ModFlags.Shift, 0x04)]
    [InlineData(0x0306, ModFlags.Ctrl | ModFlags.Shift, 0x06)]
    [InlineData(0x0F04, ModFlags.Ctrl | ModFlags.Shift | ModFlags.Alt | ModFlags.Gui, 0x04)]
    public void Decode_ModifiedKeycode(ushort code, ModFlags expectedMods, ushort expectedBase)
    {
        var mod = Assert.IsType<ModifiedKeycode>(KeycodeDecoder.Decode(code));
        Assert.Equal(expectedMods, mod.Mods);
        Assert.Equal(expectedBase, mod.BaseCode);
    }

    // --- Mod-tap ---

    [Theory]
    [InlineData(0x2204, ModFlags.Shift, 0x04)]
    [InlineData(0x2128, ModFlags.Ctrl, 0x28)]
    public void Decode_ModTap(ushort code, ModFlags expectedMods, ushort expectedBase)
    {
        var mt = Assert.IsType<ModTapKeycode>(KeycodeDecoder.Decode(code));
        Assert.Equal(expectedMods, mt.Mods);
        Assert.Equal(expectedBase, mt.BaseCode);
    }

    [Fact]
    public void Decode_ModTap_MaxRange()
    {
        // 0x3FFF = mod-tap max
        var result = KeycodeDecoder.Decode(0x3FFF);
        Assert.IsType<ModTapKeycode>(result);
    }

    // --- Layer-tap ---

    [Theory]
    [InlineData(0x4204, 2, 0x04)]
    [InlineData(0x4000, 0, 0x00)]
    [InlineData(0x4F04, 15, 0x04)]
    public void Decode_LayerTap(ushort code, int expectedLayer, ushort expectedBase)
    {
        var lt = Assert.IsType<LayerTapKeycode>(KeycodeDecoder.Decode(code));
        Assert.Equal(expectedLayer, lt.Layer);
        Assert.Equal(expectedBase, lt.BaseCode);
    }

    // --- Layer-mod ---

    [Fact]
    public void Decode_LayerMod_Layer3_Ctrl()
    {
        var result = KeycodeDecoder.Decode(0x5031);
        var lm = Assert.IsType<LayerModKeycode>(result);
        Assert.Equal(3, lm.Layer);
        Assert.Equal(ModFlags.Ctrl, lm.Mods);
    }

    [Fact]
    public void Decode_LayerMod_MaxRange()
    {
        // 0x51FF = layer-mod max
        var result = KeycodeDecoder.Decode(0x51FF);
        Assert.IsType<LayerModKeycode>(result);
    }

    // --- Layer functions ---

    [Theory]
    [InlineData(0x5200, LayerFunctionKind.TO, 0)]
    [InlineData(0x5203, LayerFunctionKind.TO, 3)]
    [InlineData(0x5220, LayerFunctionKind.MO, 0)]
    [InlineData(0x5225, LayerFunctionKind.MO, 5)]
    [InlineData(0x5240, LayerFunctionKind.DF, 0)]
    [InlineData(0x5260, LayerFunctionKind.TG, 0)]
    [InlineData(0x5267, LayerFunctionKind.TG, 7)]
    [InlineData(0x5280, LayerFunctionKind.OSL, 0)]
    [InlineData(0x5283, LayerFunctionKind.OSL, 3)]
    [InlineData(0x52C0, LayerFunctionKind.TT, 0)]
    [InlineData(0x52C7, LayerFunctionKind.TT, 7)]
    public void Decode_LayerFunction(ushort code, LayerFunctionKind expectedKind, int expectedLayer)
    {
        var result = KeycodeDecoder.Decode(code);
        var lf = Assert.IsType<LayerFunctionKeycode>(result);
        Assert.Equal(expectedKind, lf.Kind);
        Assert.Equal(expectedLayer, lf.Layer);
    }

    // --- One-shot mod ---

    [Fact]
    public void Decode_OneShotMod_Shift()
    {
        var result = KeycodeDecoder.Decode(0x52A2);
        var osm = Assert.IsType<OneShotModKeycode>(result);
        Assert.Equal(ModFlags.Shift, osm.Mods);
    }

    [Fact]
    public void Decode_OneShotMod_CtrlAlt()
    {
        var result = KeycodeDecoder.Decode(0x52A5);
        var osm = Assert.IsType<OneShotModKeycode>(result);
        Assert.Equal(ModFlags.Ctrl | ModFlags.Alt, osm.Mods);
    }

    // --- Custom keycodes ---

    [Theory]
    [InlineData(0x7E00, 0)]
    [InlineData(0x7E01, 1)]
    [InlineData(0x7E3F, 63)]
    [InlineData(0x7FFF, 511)] // max custom
    public void Decode_CustomKeycode(ushort code, int expectedIndex)
    {
        var result = KeycodeDecoder.Decode(code);
        var custom = Assert.IsType<CustomKeycodeDescriptor>(result);
        Assert.Equal(expectedIndex, custom.Index);
    }

    // --- Named QMK specials ---

    [Theory]
    [InlineData(0x7C79)] // QK_REPEAT_KEY
    [InlineData(0x7C7B)] // QK_LAYER_LOCK
    public void Decode_NamedSpecial_ReturnsSpecialKeycode(ushort code)
    {
        var result = KeycodeDecoder.Decode(code);
        var s = Assert.IsType<SpecialKeycode>(result);
        Assert.Equal(code, s.Code);
    }

    // --- Unknown / raw keycodes ---

    [Theory]
    [InlineData(0x5300)] // gap between TT max and custom range
    [InlineData(0x6000)] // unmapped range
    [InlineData(0x7DFF)] // just below custom range
    public void Decode_UnknownRange_ReturnsRawKeycode(ushort code)
    {
        var result = KeycodeDecoder.Decode(code);
        var raw = Assert.IsType<RawKeycode>(result);
        Assert.Equal(code, raw.Value);
    }

    // --- Edge cases ---

    [Fact]
    public void Decode_ModifierOnlyNoBaseKey_ReturnsRaw()
    {
        // 0x0200 = Shift + KC_NO(0x00) — no base key, so modifier combo doesn't apply
        var result = KeycodeDecoder.Decode(0x0200);
        Assert.IsType<RawKeycode>(result);
    }

    [Fact]
    public void Decode_0x52E0_InGapAfterTT_ReturnsRaw()
    {
        // 0x52E0 is past TT range (0x52DF max) and before custom range
        var result = KeycodeDecoder.Decode(0x52E0);
        Assert.IsType<RawKeycode>(result);
    }
}

public class KeycodeCatalogTests
{
    [Fact]
    public void BasicKeycodes_ContainsAllLetters()
    {
        for (ushort code = 0x04; code <= 0x1D; code++)
            Assert.True(KeycodeCatalog.BasicKeycodes.ContainsKey(code), $"Missing letter 0x{code:X2}");
    }

    [Fact]
    public void BasicKeycodes_ContainsAllNumbers()
    {
        for (ushort code = 0x1E; code <= 0x27; code++)
            Assert.True(KeycodeCatalog.BasicKeycodes.ContainsKey(code), $"Missing number 0x{code:X2}");
    }

    [Fact]
    public void ShiftedSymbols_ContainsExpectedEntries()
    {
        Assert.Equal("!", KeycodeCatalog.ShiftedSymbols[0x1E]);
        Assert.Equal("@", KeycodeCatalog.ShiftedSymbols[0x1F]);
        Assert.Equal("{", KeycodeCatalog.ShiftedSymbols[0x2F]);
        Assert.Equal("?", KeycodeCatalog.ShiftedSymbols[0x38]);
    }

    [Fact]
    public void AllGroups_HasExpectedGroupCount()
    {
        Assert.Equal(12, KeycodeCatalog.AllGroups.Count);
    }

    [Fact]
    public void AllGroups_NoDuplicateCodes()
    {
        var allCodes = new HashSet<ushort>();
        foreach (var (_, entries) in KeycodeCatalog.AllGroups)
        {
            foreach (var entry in entries)
                Assert.True(allCodes.Add(entry.Code), $"Duplicate code 0x{entry.Code:X4} in catalog");
        }
    }

    [Fact]
    public void BasicKeycodes_MatchesAllGroupEntries()
    {
        var totalEntries = KeycodeCatalog.AllGroups.Sum(g => g.Entries.Count);
        Assert.Equal(totalEntries, KeycodeCatalog.BasicKeycodes.Count);
    }

    // --- Macro keycodes ---

    [Theory]
    [InlineData(0x7700, 0)]   // Macro 0
    [InlineData(0x7701, 1)]   // Macro 1
    [InlineData(0x770F, 15)]  // Macro 15
    [InlineData(0x77FF, 255)] // Max macro
    public void Decode_MacroKeycode_ReturnsMacroKeycode(ushort code, int expectedIndex)
    {
        var descriptor = KeycodeDecoder.Decode(code);
        var macro = Assert.IsType<MacroKeycode>(descriptor);
        Assert.Equal(expectedIndex, macro.MacroIndex);
    }
}
