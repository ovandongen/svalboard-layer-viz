using SvalboardLayerViz.Core.Export;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.Export;

public class KeyStyleResolverTests
{
    private static readonly LayerColors DefaultColors = new(
        "#334455", "#556677", "#778899",
        "#223344", "#445566", "#FFFFFF", "#AABBCC");

    private static readonly LayerColors TargetColors = new(
        "#AA1122", "#BB2233", "#CC3344",
        "#991122", "#AA2233", "#FFFFFF", "#DDDDDD");

    private static Key MakeKey(
        bool transparent = false, bool layerSwitch = false,
        int? targetLayer = null) => new()
    {
        Row = 1, Col = 2, RawKeycode = 0x0004,
        DisplayLabel = "A",
        IsTransparent = transparent,
        IsLayerSwitch = layerSwitch,
        TargetLayer = targetLayer,
        X = 0, Y = 0,
    };

    [Fact]
    public void NormalKey_UsesLayerBackground()
    {
        var style = KeyStyleResolver.Resolve(MakeKey(), layerIndex: 0, DefaultColors, null);
        Assert.Equal(DefaultColors.Background, style.Background);
        Assert.Equal(DefaultColors.Border, style.Border);
        Assert.Equal(DefaultColors.TextColor, style.Text);
        Assert.Equal(1.0, style.Opacity);
    }

    [Fact]
    public void TransparentKey_UsesDimmedColors()
    {
        var style = KeyStyleResolver.Resolve(
            MakeKey(transparent: true), layerIndex: 0, DefaultColors, null);
        Assert.Equal(DefaultColors.TransparentBackground, style.Background);
        Assert.Equal(DefaultColors.TransparentBorder, style.Border);
        Assert.Equal(DefaultColors.TransparentTextColor, style.Text);
        Assert.Equal(0.55, style.Opacity);
    }

    [Fact]
    public void LayerSwitchKey_UsesTargetLayerColors()
    {
        var style = KeyStyleResolver.Resolve(
            MakeKey(layerSwitch: true, targetLayer: 2), layerIndex: 0, DefaultColors, TargetColors);
        Assert.Equal(TargetColors.Background, style.Background);
        Assert.Equal(TargetColors.Accent, style.Border);
        Assert.Equal(TargetColors.TextColor, style.Text);
        Assert.Equal(1.0, style.Opacity);
    }

    [Fact]
    public void ActivatorForCurrentLayer_WithoutTargetColors_UsesFullOpacity()
    {
        // Transparent layer-switch key whose target == current layer (e.g., MO(3) on layer 3)
        // When no target colors provided, falls through to activator logic
        var style = KeyStyleResolver.Resolve(
            MakeKey(transparent: true, layerSwitch: true, targetLayer: 3),
            layerIndex: 3, DefaultColors, null);
        Assert.Equal(DefaultColors.Background, style.Background);
        Assert.Equal(DefaultColors.Border, style.Border);
        Assert.Equal(1.0, style.Opacity);
    }

    [Fact]
    public void ActivatorForCurrentLayer_WithTargetColors_UsesTargetColors()
    {
        // When target colors ARE provided, layer-switch logic takes precedence
        var style = KeyStyleResolver.Resolve(
            MakeKey(transparent: true, layerSwitch: true, targetLayer: 3),
            layerIndex: 3, DefaultColors, TargetColors);
        Assert.Equal(TargetColors.Background, style.Background);
        Assert.Equal(1.0, style.Opacity);
    }

    [Fact]
    public void LayerSwitchWithoutTargetColors_FallsBackToLayerColors()
    {
        var style = KeyStyleResolver.Resolve(
            MakeKey(layerSwitch: true, targetLayer: 5), layerIndex: 0, DefaultColors, null);
        // No target colors provided, so falls through to normal transparent/non-transparent logic
        Assert.Equal(DefaultColors.Background, style.Background);
    }
}
