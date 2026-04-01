using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.App.ViewModels;

public partial class LayerViewModel : ObservableObject
{
    public Layer Layer { get; }
    private readonly Action<int>? _selectLayer;

    public string DisplayName => Layer.DisplayName;
    public int Index => Layer.Index;

    /// <summary>Accent color for the layer tab indicator.</summary>
    public string TabColor { get; }

    [ObservableProperty]
    private ObservableCollection<KeyViewModel> _keys = [];

    public LayerViewModel(Layer layer, Action<int>? selectLayer = null, int totalLayers = 8,
        Dictionary<int, string>? userLayerColors = null,
        Action<KeyViewModel>? setLabelRequested = null)
    {
        Layer = layer;
        _selectLayer = selectLayer;

        var userColor = userLayerColors?.GetValueOrDefault(layer.Index);
        var colors = LayerColorService.GetLayerColors(layer.Index, totalLayers,
            layer.ColorHue, layer.ColorSat, layer.ColorVal, userColor);
        TabColor = colors.Accent;

        foreach (var key in layer.Keys)
        {
            Keys.Add(new KeyViewModel(key, layer, totalLayers, userLayerColors, setLabelRequested));
        }
    }

    [RelayCommand]
    private void Select() => _selectLayer?.Invoke(Index);
}
