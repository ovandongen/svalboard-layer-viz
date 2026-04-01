using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

public class KeyViewModelTests
{
    private static Key MakeKey(
        string label = "A",
        string? secondaryLabel = null,
        bool transparent = false,
        string? effectiveLabel = null,
        ushort rawKeycode = 0x0004,
        double x = 5.0,
        double y = 3.0,
        double width = 1.0,
        double height = 1.0) => new()
    {
        Row = 1,
        Col = 2,
        RawKeycode = rawKeycode,
        DisplayLabel = label,
        SecondaryLabel = secondaryLabel,
        IsTransparent = transparent,
        EffectiveLabel = effectiveLabel,
        X = x,
        Y = y,
        Width = width,
        Height = height,
    };

    private static Layer MakeLayer(int index = 0) => new()
    {
        Index = index,
        Keys = [],
    };

    [Fact]
    public void DisplayLabel_NormalKey_ReturnsLabel()
    {
        var vm = new KeyViewModel(MakeKey(label: "Space"), MakeLayer());
        Assert.Equal("Space", vm.DisplayLabel);
    }

    [Fact]
    public void DisplayLabel_TransparentWithEffective_ReturnsEffective()
    {
        var vm = new KeyViewModel(MakeKey(label: "___", transparent: true, effectiveLabel: "A"), MakeLayer());
        Assert.Equal("A", vm.DisplayLabel);
    }

    [Fact]
    public void DisplayLabel_TransparentWithoutEffective_ReturnsUnderscores()
    {
        var vm = new KeyViewModel(MakeKey(label: "___", transparent: true), MakeLayer());
        Assert.Equal("___", vm.DisplayLabel);
    }

    [Fact]
    public void SecondaryLabel_Passthrough()
    {
        var vm = new KeyViewModel(MakeKey(secondaryLabel: "Ctrl"), MakeLayer());
        Assert.Equal("Ctrl", vm.SecondaryLabel);
    }

    [Fact]
    public void SecondaryLabel_Null_WhenNotSet()
    {
        var vm = new KeyViewModel(MakeKey(), MakeLayer());
        Assert.Null(vm.SecondaryLabel);
    }

    [Fact]
    public void IsTransparent_ReflectsKey()
    {
        var transparent = new KeyViewModel(MakeKey(transparent: true), MakeLayer());
        var normal = new KeyViewModel(MakeKey(), MakeLayer());
        Assert.True(transparent.IsTransparent);
        Assert.False(normal.IsTransparent);
    }

    [Fact]
    public void IsEmpty_TrueWhenNoLabelAndNotTransparent()
    {
        var vm = new KeyViewModel(MakeKey(label: ""), MakeLayer());
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public void IsEmpty_FalseWhenTransparent()
    {
        var vm = new KeyViewModel(MakeKey(label: "", transparent: true), MakeLayer());
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void Position_ScaledAt60PixelsPerUnit()
    {
        var vm = new KeyViewModel(MakeKey(x: 9.5, y: 2.5, width: 1.0, height: 1.0), MakeLayer());
        Assert.Equal(570.0, vm.Left);   // 9.5 * 60
        Assert.Equal(150.0, vm.Top);    // 2.5 * 60
        Assert.Equal(60.0, vm.Width);   // 1.0 * 60
        Assert.Equal(60.0, vm.Height);  // 1.0 * 60
    }

    [Fact]
    public void Tooltip_IncludesLayerIndex()
    {
        var vm = new KeyViewModel(MakeKey(), MakeLayer(index: 3));
        Assert.Contains("Layer 3", vm.Tooltip);
    }

    [Fact]
    public void Tooltip_IncludesRawHex()
    {
        var vm = new KeyViewModel(MakeKey(rawKeycode: 0x5102), MakeLayer());
        Assert.Contains("0x5102", vm.Tooltip);
    }

    [Fact]
    public void Tooltip_IncludesTransparentNote_WhenTransparent()
    {
        var vm = new KeyViewModel(MakeKey(transparent: true), MakeLayer());
        Assert.Contains("Transparent", vm.Tooltip);
    }

    [Fact]
    public void Tooltip_NoTransparentNote_WhenNormal()
    {
        var vm = new KeyViewModel(MakeKey(), MakeLayer());
        Assert.DoesNotContain("Transparent", vm.Tooltip);
    }
}
