using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.Models;

/// <summary>
/// Covers the WithResolvedLabels path used by MainWindowViewModel.ApplySettings
/// and RefreshAllKeyLabels. The contract: no changes → return same reference;
/// any change → return a new top-level record with only the affected layers
/// rebuilt. Layer names can be overlaid from a map; null map means "leave alone".
/// </summary>
public class KeyboardConfigWithResolvedLabelsTests
{
    private static KeyboardConfig MakeConfig(params (int Index, string Name, ushort[] Codes)[] layers)
    {
        var layerList = new List<Layer>();
        foreach (var (idx, name, codes) in layers)
        {
            var keys = new List<Key>();
            for (var i = 0; i < codes.Length; i++)
            {
                keys.Add(new Key
                {
                    Row = 0, Col = i,
                    RawKeycode = codes[i],
                    DisplayLabel = $"0x{codes[i]:X4}", // stale; resolver should overwrite
                });
            }
            layerList.Add(new Layer { Index = idx, Name = name, Keys = keys });
        }

        return new KeyboardConfig
        {
            DeviceName = "Test", VendorId = 0, ProductId = 0, KeyboardId = 0,
            MatrixRows = 10, MatrixCols = 6,
            Layers = layerList,
        };
    }

    [Fact]
    public void NoChange_ReturnsSameReference()
    {
        var svc = new KeycodeService();
        // Pre-resolve labels so nothing differs after the call.
        var config = MakeConfig((0, "Base", [0x0004]));
        var resolved = config.WithResolvedLabels(svc, layerNames: null);
        // Second call with same inputs: already in sync.
        var again = resolved.WithResolvedLabels(svc, layerNames: null);

        Assert.Same(resolved, again);
    }

    [Fact]
    public void StaleLabels_OverwrittenByResolver()
    {
        var svc = new KeycodeService();
        var config = MakeConfig((0, null!, [0x0004])); // 0x0004 = KC_A

        var resolved = config.WithResolvedLabels(svc, layerNames: null);

        Assert.NotSame(config, resolved);
        Assert.Equal("A", resolved.Layers[0].Keys[0].DisplayLabel);
    }

    [Fact]
    public void LayerNames_Overlay_FromMap()
    {
        var svc = new KeycodeService();
        var config = MakeConfig((0, null!, [0x0004]), (1, "OldName", [0x0004]));

        var resolved = config.WithResolvedLabels(svc,
            new Dictionary<int, string> { [0] = "Base", [1] = "Symbols" });

        Assert.Equal("Base", resolved.Layers[0].Name);
        Assert.Equal("Symbols", resolved.Layers[1].Name);
    }

    [Fact]
    public void LayerNames_MissingEntry_ClearsName()
    {
        var svc = new KeycodeService();
        var config = MakeConfig((0, "KeepMe", [0x0004]));

        // Non-null map with no entry for index 0 → clear to null.
        var resolved = config.WithResolvedLabels(svc, new Dictionary<int, string>());

        Assert.Null(resolved.Layers[0].Name);
    }

    [Fact]
    public void NullLayerNames_LeavesNamesAlone()
    {
        var svc = new KeycodeService();
        var config = MakeConfig((0, "KeepMe", [0x0004]));

        var resolved = config.WithResolvedLabels(svc, layerNames: null);

        Assert.Equal("KeepMe", resolved.Layers[0].Name);
    }

    [Fact]
    public void CustomLabel_AppliedFromService()
    {
        var svc = new KeycodeService();
        svc.SetCustomKeyLabels(new Dictionary<string, string> { ["0x0004"] = "MyA" });
        var config = MakeConfig((0, null!, [0x0004]));

        var resolved = config.WithResolvedLabels(svc, layerNames: null);

        Assert.Equal("MyA", resolved.Layers[0].Keys[0].DisplayLabel);
    }

    [Fact]
    public void UnknownKeycodes_MarkedIsUnknown()
    {
        var svc = new KeycodeService();
        var config = MakeConfig((0, null!, [0x5300])); // gap range
        var resolved = config.WithResolvedLabels(svc, layerNames: null);

        Assert.True(resolved.Layers[0].Keys[0].IsUnknown);
    }

    [Fact]
    public void OnlyDiffLayers_Rebuilt()
    {
        // Layer 0 fully resolved, layer 1 stale. Result should share layer 0
        // but rebuild layer 1.
        var svc = new KeycodeService();
        var config = MakeConfig((0, null!, [0x0004]), (1, null!, [0x0005]));
        var pass1 = config.WithResolvedLabels(svc, layerNames: null);
        // Simulate external mutation of layer 1's label to stale.
        var mutated = pass1 with
        {
            Layers = new List<Layer>
            {
                pass1.Layers[0],
                pass1.Layers[1] with
                {
                    Keys = pass1.Layers[1].Keys.Select(k => k with { DisplayLabel = "STALE" }).ToList(),
                },
            },
        };

        var pass2 = mutated.WithResolvedLabels(svc, layerNames: null);

        Assert.Same(mutated.Layers[0], pass2.Layers[0]);
        Assert.NotSame(mutated.Layers[1], pass2.Layers[1]);
        Assert.Equal("B", pass2.Layers[1].Keys[0].DisplayLabel);
    }
}
