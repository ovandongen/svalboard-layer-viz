using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

public class TransparentKeyResolverTests
{
    private static Key MakeKey(int row, int col, string label, bool transparent = false) => new()
    {
        Row = row,
        Col = col,
        RawKeycode = 0x0004,
        DisplayLabel = label,
        IsTransparent = transparent,
    };

    private static Layer MakeLayer(int index, params Key[] keys) => new()
    {
        Index = index,
        Keys = keys.ToList(),
    };

    [Fact]
    public void Layer0_KeysNeverModified()
    {
        var layers = new List<Layer>
        {
            MakeLayer(0, MakeKey(0, 0, "A")),
        };

        TransparentKeyResolver.Resolve(layers);

        Assert.Null(layers[0].Keys[0].EffectiveLabel);
    }

    [Fact]
    public void Layer1Transparent_ResolvesToLayer0Label()
    {
        var layers = new List<Layer>
        {
            MakeLayer(0, MakeKey(0, 0, "A")),
            MakeLayer(1, MakeKey(0, 0, "___", transparent: true)),
        };

        TransparentKeyResolver.Resolve(layers);

        Assert.Equal("A", layers[1].Keys[0].EffectiveLabel);
    }

    [Fact]
    public void DeepResolution_SkipsTransparentLayers()
    {
        var layers = new List<Layer>
        {
            MakeLayer(0, MakeKey(0, 0, "Space")),
            MakeLayer(1, MakeKey(0, 0, "___", transparent: true)),
            MakeLayer(2, MakeKey(0, 0, "___", transparent: true)),
        };

        TransparentKeyResolver.Resolve(layers);

        Assert.Equal("Space", layers[1].Keys[0].EffectiveLabel);
        Assert.Equal("Space", layers[2].Keys[0].EffectiveLabel);
    }

    [Fact]
    public void NonTransparentKeys_Untouched()
    {
        var layers = new List<Layer>
        {
            MakeLayer(0, MakeKey(0, 0, "A")),
            MakeLayer(1, MakeKey(0, 0, "B")),
        };

        TransparentKeyResolver.Resolve(layers);

        Assert.Null(layers[1].Keys[0].EffectiveLabel);
        Assert.Equal("B", layers[1].Keys[0].DisplayLabel);
    }

    [Fact]
    public void AllTransparentStack_LeavesEffectiveLabelNull()
    {
        var layers = new List<Layer>
        {
            MakeLayer(0, MakeKey(0, 0, "___", transparent: true)),
            MakeLayer(1, MakeKey(0, 0, "___", transparent: true)),
        };

        TransparentKeyResolver.Resolve(layers);

        // Layer 0 transparent has no lower layer — stays null
        Assert.Null(layers[0].Keys[0].EffectiveLabel);
        // Layer 1 transparent — layer 0 is also transparent, so no resolution
        Assert.Null(layers[1].Keys[0].EffectiveLabel);
    }

    [Fact]
    public void Resolution_MatchesByRowCol()
    {
        var layers = new List<Layer>
        {
            MakeLayer(0, MakeKey(0, 0, "A"), MakeKey(1, 0, "B")),
            MakeLayer(1, MakeKey(0, 0, "___", transparent: true), MakeKey(1, 0, "___", transparent: true)),
        };

        TransparentKeyResolver.Resolve(layers);

        Assert.Equal("A", layers[1].Keys[0].EffectiveLabel);
        Assert.Equal("B", layers[1].Keys[1].EffectiveLabel);
    }

    [Fact]
    public void MixedTransparentAndNonTransparent_OnSameLayer()
    {
        var layers = new List<Layer>
        {
            MakeLayer(0, MakeKey(0, 0, "A"), MakeKey(0, 1, "B")),
            MakeLayer(1, MakeKey(0, 0, "___", transparent: true), MakeKey(0, 1, "X")),
        };

        TransparentKeyResolver.Resolve(layers);

        Assert.Equal("A", layers[1].Keys[0].EffectiveLabel);
        Assert.Null(layers[1].Keys[1].EffectiveLabel);
    }
}
