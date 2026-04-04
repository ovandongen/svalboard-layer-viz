using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Layout;
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
        int row = 1,
        int col = 2,
        double x = 5.0,
        double y = 3.0,
        double width = 1.0,
        double height = 1.0) => new()
    {
        Row = row,
        Col = col,
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

    private static PositionedKey MakePosKey(
        string label = "A",
        string? secondaryLabel = null,
        bool transparent = false,
        string? effectiveLabel = null,
        ushort rawKeycode = 0x0004,
        int row = 1,
        int col = 2,
        double x = 5.0,
        double y = 3.0,
        double width = 1.0,
        double height = 1.0)
    {
        var key = MakeKey(label, secondaryLabel, transparent, effectiveLabel, rawKeycode, row, col, x, y, width, height);
        return new PositionedKey(key, x * 60, y * 60, width * 60, height * 60);
    }

    private static Layer MakeLayer(int index = 0) => new()
    {
        Index = index,
        Keys = [],
    };

    [Fact]
    public void DisplayLabel_NormalKey_ReturnsLabel()
    {
        var vm = new KeyViewModel(MakePosKey(label: "Space"), MakeLayer(), 0, 0);
        Assert.Equal("Space", vm.DisplayLabel);
    }

    [Fact]
    public void DisplayLabel_TransparentWithEffective_ReturnsEffective()
    {
        var vm = new KeyViewModel(MakePosKey(label: "___", transparent: true, effectiveLabel: "A"), MakeLayer(), 0, 0);
        Assert.Equal("A", vm.DisplayLabel);
    }

    [Fact]
    public void DisplayLabel_TransparentWithoutEffective_ReturnsUnderscores()
    {
        var vm = new KeyViewModel(MakePosKey(label: "___", transparent: true), MakeLayer(), 0, 0);
        Assert.Equal("___", vm.DisplayLabel);
    }

    [Fact]
    public void SecondaryLabel_Passthrough()
    {
        var vm = new KeyViewModel(MakePosKey(secondaryLabel: "Ctrl"), MakeLayer(), 0, 0);
        Assert.Equal("Ctrl", vm.SecondaryLabel);
    }

    [Fact]
    public void SecondaryLabel_Null_WhenNotSet()
    {
        var vm = new KeyViewModel(MakePosKey(), MakeLayer(), 0, 0);
        Assert.Null(vm.SecondaryLabel);
    }

    [Fact]
    public void IsTransparent_ReflectsKey()
    {
        var transparent = new KeyViewModel(MakePosKey(transparent: true), MakeLayer(), 0, 0);
        var normal = new KeyViewModel(MakePosKey(), MakeLayer(), 0, 0);
        Assert.True(transparent.IsTransparent);
        Assert.False(normal.IsTransparent);
    }

    [Fact]
    public void IsEmpty_TrueWhenNoLabelAndNotTransparent()
    {
        var vm = new KeyViewModel(MakePosKey(label: ""), MakeLayer(), 0, 0);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public void IsEmpty_FalseWhenTransparent()
    {
        var vm = new KeyViewModel(MakePosKey(label: "", transparent: true), MakeLayer(), 0, 0);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void Position_ScaledAt60PixelsPerUnit()
    {
        // clusterOrigin = (0,0) so Left/Top = BoardX/BoardY
        var vm = new KeyViewModel(MakePosKey(x: 9.5, y: 2.5, width: 1.0, height: 1.0), MakeLayer(), 0, 0);
        Assert.Equal(570.0, vm.Left);   // 9.5 * 60
        Assert.Equal(150.0, vm.Top);    // 2.5 * 60
        Assert.Equal(60.0, vm.Width);   // 1.0 * 60
        Assert.Equal(60.0, vm.Height);  // 1.0 * 60
    }

    [Fact]
    public void Tooltip_IncludesLayerIndex()
    {
        var vm = new KeyViewModel(MakePosKey(), MakeLayer(index: 3), 0, 0);
        Assert.Contains("Layer 3", vm.Tooltip);
    }

    [Fact]
    public void Tooltip_IncludesRawHex()
    {
        var vm = new KeyViewModel(MakePosKey(rawKeycode: 0x5222), MakeLayer(), 0, 0);
        Assert.Contains("0x5222", vm.Tooltip);
    }

    [Fact]
    public void Tooltip_IncludesTransparentNote_WhenTransparent()
    {
        var vm = new KeyViewModel(MakePosKey(transparent: true), MakeLayer(), 0, 0);
        Assert.Contains("Transparent", vm.Tooltip);
    }

    [Fact]
    public void Tooltip_NoTransparentNote_WhenNormal()
    {
        var vm = new KeyViewModel(MakePosKey(), MakeLayer(), 0, 0);
        Assert.DoesNotContain("Transparent", vm.Tooltip);
    }

    [Fact]
    public void IsPressed_DefaultFalse()
    {
        var vm = new KeyViewModel(MakePosKey(), MakeLayer(), 0, 0);
        Assert.False(vm.IsPressed);
    }

    [Fact]
    public void IsPressed_WhenSet_ChangesColors()
    {
        var vm = new KeyViewModel(MakePosKey(), MakeLayer(), 0, 0);
        var normalBorder = vm.BorderColor;
        var normalBg = vm.BackgroundColor;

        vm.IsPressed = true;
        Assert.Equal("#FFFFFF", vm.BorderColor);
        Assert.Equal(normalBg, vm.BackgroundColor); // Background unchanged — glow is border-only
        Assert.NotEqual(normalBorder, vm.BorderColor);
    }

    [Fact]
    public void IsPressed_WhenSet_IncreasesBorderThickness()
    {
        var vm = new KeyViewModel(MakePosKey(), MakeLayer(), 0, 0);
        Assert.Equal(new Avalonia.Thickness(1.5), vm.ActiveBorderThickness);

        vm.IsPressed = true;
        Assert.Equal(new Avalonia.Thickness(3.0), vm.ActiveBorderThickness);
    }

    [Fact]
    public void IsPressed_NotifiesPropertyChanged()
    {
        var vm = new KeyViewModel(MakePosKey(), MakeLayer(), 0, 0);
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.IsPressed = true;

        Assert.Contains("IsPressed", changed);
        Assert.Contains("BorderColor", changed);
        Assert.Contains("ActiveBorderThickness", changed);
    }

    // ── Cluster-relative positioning tests ──

    [Fact]
    public void Left_WithClusterOrigin_SubtractsFromBoardCoords()
    {
        // Key at board-absolute (9.5*60, 2.5*60), cluster origin at (8.5*60, 1.5*60)
        var pk = MakePosKey(x: 9.5, y: 2.5);
        var vm = new KeyViewModel(pk, MakeLayer(), clusterOriginX: 8.5 * 60, clusterOriginY: 1.5 * 60);
        Assert.Equal((9.5 - 8.5) * 60, vm.Left);
        Assert.Equal((2.5 - 1.5) * 60, vm.Top);
    }

    [Fact]
    public void Left_ZeroOrigin_UsesBoardAbsoluteCoords()
    {
        var vm = new KeyViewModel(MakePosKey(x: 7.0, y: 2.0), MakeLayer(), 0, 0);
        Assert.Equal(7.0 * 60, vm.Left);
        Assert.Equal(2.0 * 60, vm.Top);
    }

    [Fact]
    public void Left_RightHandClusterOrigin_SubtractsBoth()
    {
        // R-Middle South at X=16.3, cluster origin at board-absolute pixel coords
        var pk = MakePosKey(row: 7, col: 0, x: 16.3, y: 2.0);
        var vm = new KeyViewModel(pk, MakeLayer(), clusterOriginX: 15.3 * 60, clusterOriginY: 0.0);
        Assert.Equal((16.3 - 15.3) * 60, vm.Left, 1);
        Assert.Equal(2.0 * 60, vm.Top);
    }

    [Fact]
    public void Position_IsStaticAfterConstruction()
    {
        var pk = MakePosKey(row: 7, col: 0, x: 16.3, y: 2.0);
        var vm = new KeyViewModel(pk, MakeLayer(), clusterOriginX: 15.3 * 60, clusterOriginY: 0.0);
        var left1 = vm.Left;
        var top1 = vm.Top;
        var left2 = vm.Left;
        var top2 = vm.Top;
        Assert.Equal(left1, left2);
        Assert.Equal(top1, top2);
    }
}
