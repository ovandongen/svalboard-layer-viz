using System.ComponentModel;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Keymap.Builders;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

public class BuilderTests
{
    // --- ModTapBuilder ---

    [Fact]
    public void ModTapBuilder_InitialState_CannotBuild()
    {
        var b = new ModTapBuilder();
        Assert.False(b.CanBuild);
    }

    [Fact]
    public void ModTapBuilder_SetBothProperties_CanBuild()
    {
        var b = new ModTapBuilder { Mods = ModFlags.Ctrl, TapKey = 0x04 };
        Assert.True(b.CanBuild);
    }

    [Fact]
    public void ModTapBuilder_Build_ReturnsModTapKeycode()
    {
        var b = new ModTapBuilder { Mods = ModFlags.Shift, TapKey = 0x04 };
        var result = (ModTapKeycode)b.Build();
        Assert.Equal(ModFlags.Shift, result.Mods);
        Assert.Equal(0x04, result.BaseCode);
    }

    [Fact]
    public void ModTapBuilder_ClearTapKey_CannotBuild()
    {
        var b = new ModTapBuilder { Mods = ModFlags.Ctrl, TapKey = 0x04 };
        Assert.True(b.CanBuild);

        b.TapKey = null;
        Assert.False(b.CanBuild);
    }

    [Fact]
    public void ModTapBuilder_Build_WithoutState_Throws()
    {
        var b = new ModTapBuilder();
        Assert.Throws<InvalidOperationException>(() => b.Build());
    }

    [Fact]
    public void ModTapBuilder_PreviewLabel_ShowsContent()
    {
        var b = new ModTapBuilder { Mods = ModFlags.Ctrl, TapKey = 0x04 };
        Assert.Contains("MT(", b.PreviewLabel);
    }

    [Fact]
    public void ModTapBuilder_ModFlagHelpers_RoundTrip()
    {
        var b = new ModTapBuilder();
        b.IsCtrl = true;
        b.IsShift = true;
        Assert.Equal(ModFlags.Ctrl | ModFlags.Shift, b.Mods);
        b.IsCtrl = false;
        Assert.Equal(ModFlags.Shift, b.Mods);
    }

    // --- LayerTapBuilder ---

    [Fact]
    public void LayerTapBuilder_Build_ReturnsLayerTapKeycode()
    {
        var b = new LayerTapBuilder { Layer = 2, TapKey = 0x04 };
        var lt = (LayerTapKeycode)b.Build();
        Assert.Equal(2, lt.Layer);
        Assert.Equal(0x04, lt.BaseCode);
    }

    [Fact]
    public void LayerTapBuilder_PartialState_CannotBuild()
    {
        var b = new LayerTapBuilder { Layer = 1 };
        Assert.False(b.CanBuild);
    }

    // --- LayerModBuilder ---

    [Fact]
    public void LayerModBuilder_Build_ReturnsLayerModKeycode()
    {
        var b = new LayerModBuilder { Layer = 3, Mods = ModFlags.Alt };
        var lm = (LayerModKeycode)b.Build();
        Assert.Equal(3, lm.Layer);
        Assert.Equal(ModFlags.Alt, lm.Mods);
    }

    // --- LayerFunctionBuilder ---

    [Fact]
    public void LayerFunctionBuilder_Build_ReturnsLayerFunctionKeycode()
    {
        var b = new LayerFunctionBuilder { Kind = LayerFunctionKind.MO, Layer = 1 };
        var lf = (LayerFunctionKeycode)b.Build();
        Assert.Equal(LayerFunctionKind.MO, lf.Kind);
        Assert.Equal(1, lf.Layer);
    }

    [Theory]
    [InlineData(LayerFunctionKind.TO)]
    [InlineData(LayerFunctionKind.MO)]
    [InlineData(LayerFunctionKind.DF)]
    [InlineData(LayerFunctionKind.TG)]
    [InlineData(LayerFunctionKind.OSL)]
    [InlineData(LayerFunctionKind.TT)]
    public void LayerFunctionBuilder_AllKinds_BuildSuccessfully(LayerFunctionKind kind)
    {
        var b = new LayerFunctionBuilder { Kind = kind, Layer = 0 };
        Assert.True(b.CanBuild);

        var result = (LayerFunctionKeycode)b.Build();
        Assert.Equal(kind, result.Kind);
    }

    [Fact]
    public void LayerFunctionBuilder_PreviewLabel_ShowsFunction()
    {
        var b = new LayerFunctionBuilder { Kind = LayerFunctionKind.TG, Layer = 2 };
        Assert.Equal("TG(2)", b.PreviewLabel);
    }

    // --- OneShotModBuilder ---

    [Fact]
    public void OneShotModBuilder_Build_ReturnsOneShotModKeycode()
    {
        var b = new OneShotModBuilder { Mods = ModFlags.Gui };
        var result = (OneShotModKeycode)b.Build();
        Assert.Equal(ModFlags.Gui, result.Mods);
    }

    // --- CustomKeycodeBuilder ---

    [Fact]
    public void CustomKeycodeBuilder_Build_ReturnsCustomDescriptor()
    {
        var b = new CustomKeycodeBuilder { Index = 5 };
        var result = (CustomKeycodeDescriptor)b.Build();
        Assert.Equal(5, result.Index);
    }

    // --- BuilderRegistry ---

    [Fact]
    public void BuilderRegistry_CreateAll_ReturnsAllBuilders()
    {
        var builders = BuilderRegistry.CreateAll();
        Assert.Equal(6, builders.Count);
    }

    [Fact]
    public void BuilderRegistry_CreateAll_ReturnsFreshInstances()
    {
        var a = BuilderRegistry.CreateAll();
        var b = BuilderRegistry.CreateAll();
        Assert.NotSame(a[0], b[0]);
    }

    [Fact]
    public void BuilderRegistry_CreateByCategory_FiltersCorrectly()
    {
        var layer = BuilderRegistry.CreateByCategory(BuilderCategory.Layer);
        Assert.Equal(3, layer.Count); // LayerTap, LayerMod, LayerFunction

        var modifier = BuilderRegistry.CreateByCategory(BuilderCategory.Modifier);
        Assert.Equal(2, modifier.Count); // ModTap, OneShotMod

        var custom = BuilderRegistry.CreateByCategory(BuilderCategory.Custom);
        Assert.Single(custom);
    }

    // --- Builder encode round-trip ---

    [Fact]
    public void ModTapBuilder_RoundTrips_ThroughEncoder()
    {
        var b = new ModTapBuilder { Mods = ModFlags.Ctrl | ModFlags.Shift, TapKey = 0x04 };
        var desc = b.Build();
        var raw = KeycodeEncoder.Encode(desc);
        var decoded = KeycodeDecoder.Decode(raw);
        Assert.Equal(desc, decoded);
    }

    [Fact]
    public void LayerFunctionBuilder_RoundTrips_ThroughEncoder()
    {
        var b = new LayerFunctionBuilder { Kind = LayerFunctionKind.MO, Layer = 3 };
        var desc = b.Build();
        var raw = KeycodeEncoder.Encode(desc);
        var decoded = KeycodeDecoder.Decode(raw);
        Assert.Equal(desc, decoded);
    }

    // --- Category and DisplayName ---

    [Fact]
    public void AllBuilders_HaveNonEmptyDisplayName()
    {
        foreach (var b in BuilderRegistry.CreateAll())
            Assert.False(string.IsNullOrWhiteSpace(b.DisplayName));
    }

    // --- Observability: every mutable property raises CanBuild + PreviewLabel ---
    // Covers the "builders are observable" requirement of the MVVM refactor. If a
    // new slot is added without wiring change notification, these catch it.

    private static HashSet<string> CaptureNotifications(INotifyPropertyChanged source, Action mutate)
    {
        var seen = new HashSet<string>();
        source.PropertyChanged += (_, e) => { if (e.PropertyName is not null) seen.Add(e.PropertyName); };
        mutate();
        return seen;
    }

    [Fact]
    public void ModTapBuilder_MutatingMods_RaisesCanBuildAndPreview()
    {
        var b = new ModTapBuilder();
        var seen = CaptureNotifications(b, () => b.Mods = ModFlags.Ctrl);
        Assert.Contains(nameof(b.Mods), seen);
        Assert.Contains(nameof(b.CanBuild), seen);
        Assert.Contains(nameof(b.PreviewLabel), seen);
    }

    [Fact]
    public void ModTapBuilder_MutatingTapKey_RaisesCanBuildAndPreview()
    {
        var b = new ModTapBuilder { Mods = ModFlags.Ctrl };
        var seen = CaptureNotifications(b, () => b.TapKey = 0x04);
        Assert.Contains(nameof(b.TapKey), seen);
        Assert.Contains(nameof(b.CanBuild), seen);
        Assert.Contains(nameof(b.PreviewLabel), seen);
    }

    [Fact]
    public void ModTapBuilder_SettingIsCtrl_RaisesModsAndHelpers()
    {
        var b = new ModTapBuilder();
        var seen = CaptureNotifications(b, () => b.IsCtrl = true);
        Assert.Contains(nameof(b.Mods), seen);
        Assert.Contains(nameof(b.IsCtrl), seen);
        Assert.Contains(nameof(b.CanBuild), seen);
    }

    [Fact]
    public void OneShotModBuilder_MutatingMods_RaisesCanBuildAndPreview()
    {
        var b = new OneShotModBuilder();
        var seen = CaptureNotifications(b, () => b.Mods = ModFlags.Shift);
        Assert.Contains(nameof(b.Mods), seen);
        Assert.Contains(nameof(b.CanBuild), seen);
        Assert.Contains(nameof(b.PreviewLabel), seen);
    }

    [Fact]
    public void LayerTapBuilder_MutatingLayer_RaisesCanBuildAndPreview()
    {
        var b = new LayerTapBuilder { TapKey = 0x04 };
        var seen = CaptureNotifications(b, () => b.Layer = 2);
        Assert.Contains(nameof(b.Layer), seen);
        Assert.Contains(nameof(b.CanBuild), seen);
        Assert.Contains(nameof(b.PreviewLabel), seen);
    }

    [Fact]
    public void LayerModBuilder_MutatingMods_RaisesCanBuildAndPreview()
    {
        var b = new LayerModBuilder { Layer = 1 };
        var seen = CaptureNotifications(b, () => b.Mods = ModFlags.Alt);
        Assert.Contains(nameof(b.Mods), seen);
        Assert.Contains(nameof(b.CanBuild), seen);
        Assert.Contains(nameof(b.PreviewLabel), seen);
    }

    [Fact]
    public void LayerFunctionBuilder_MutatingKind_RaisesCanBuildAndPreview()
    {
        var b = new LayerFunctionBuilder { Layer = 1 };
        var seen = CaptureNotifications(b, () => b.Kind = LayerFunctionKind.MO);
        Assert.Contains(nameof(b.Kind), seen);
        Assert.Contains(nameof(b.CanBuild), seen);
        Assert.Contains(nameof(b.PreviewLabel), seen);
    }

    [Fact]
    public void LayerFunctionBuilder_MutatingLayer_RaisesCanBuildAndPreview()
    {
        var b = new LayerFunctionBuilder { Kind = LayerFunctionKind.MO };
        var seen = CaptureNotifications(b, () => b.Layer = 2);
        Assert.Contains(nameof(b.Layer), seen);
        Assert.Contains(nameof(b.CanBuild), seen);
        Assert.Contains(nameof(b.PreviewLabel), seen);
    }

    [Fact]
    public void CustomKeycodeBuilder_MutatingIndex_RaisesCanBuildAndPreview()
    {
        var b = new CustomKeycodeBuilder();
        var seen = CaptureNotifications(b, () => b.Index = 3);
        Assert.Contains(nameof(b.Index), seen);
        Assert.Contains(nameof(b.CanBuild), seen);
        Assert.Contains(nameof(b.PreviewLabel), seen);
    }

    [Fact]
    public void SettingSameValue_DoesNotRaise()
    {
        var b = new LayerFunctionBuilder { Kind = LayerFunctionKind.MO };
        var seen = CaptureNotifications(b, () => b.Kind = LayerFunctionKind.MO);
        Assert.Empty(seen);
    }

    // --- IHasTapKey ---

    [Fact]
    public void ModTapBuilder_ImplementsIHasTapKey()
    {
        Assert.IsAssignableFrom<IHasTapKey>(new ModTapBuilder());
    }

    [Fact]
    public void LayerTapBuilder_ImplementsIHasTapKey()
    {
        Assert.IsAssignableFrom<IHasTapKey>(new LayerTapBuilder());
    }
}
