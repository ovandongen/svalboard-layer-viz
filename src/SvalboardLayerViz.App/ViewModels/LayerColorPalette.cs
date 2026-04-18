using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// Precomputed per-layer colors. Built once per keymap load in
/// <see cref="MainWindowViewModel.BuildLayerViewModels"/> and looked up O(1) by
/// every <see cref="KeyViewModel"/> — both for the key's own layer and (for
/// layer-switch keys) the target layer.
/// Before this existed, every key reran HSL→RGB twice on construction, so a
/// Svalboard load did roughly 60 keys × layers × 2 color computations.
/// </summary>
public sealed class LayerColorPalette
{
    private readonly Dictionary<int, LayerColors> _colors;
    private readonly int _totalLayers;

    public int TotalLayers => _totalLayers;

    public LayerColorPalette(
        IReadOnlyList<Layer> layers,
        IReadOnlyDictionary<int, string>? userLayerColors = null)
        : this(layers, layers.Count, userLayerColors)
    {
    }

    public LayerColorPalette(
        IReadOnlyList<Layer> layers,
        int totalLayers,
        IReadOnlyDictionary<int, string>? userLayerColors = null)
    {
        _totalLayers = Math.Max(totalLayers, 1);
        _colors = new Dictionary<int, LayerColors>(layers.Count);

        foreach (var layer in layers)
        {
            var userColor = userLayerColors?.GetValueOrDefault(layer.Index);
            _colors[layer.Index] = LayerColorService.GetLayerColors(
                layer.Index, _totalLayers,
                layer.ColorHue, layer.ColorSat, layer.ColorVal, userColor);
        }
    }

    /// <summary>Convenience for callers (tests) that have a single layer in hand.</summary>
    public static LayerColorPalette ForSingleLayer(
        Layer layer, int totalLayers, IReadOnlyDictionary<int, string>? userLayerColors = null)
        => new([layer], totalLayers, userLayerColors);

    /// <summary>
    /// Returns the precomputed colors for the given layer.
    /// Falls back to algorithmic default if index was not in the input (e.g.,
    /// a layer-switch key pointing beyond the configured range).
    /// </summary>
    public LayerColors Get(int layerIndex)
    {
        if (_colors.TryGetValue(layerIndex, out var c)) return c;
        return LayerColorService.GetLayerColors(layerIndex, Math.Max(_totalLayers, 1));
    }
}
