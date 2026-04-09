using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Layout;
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

    /// <summary>Left hand (rows 0-4): thumb + 4 finger clusters.</summary>
    public HandViewModel LeftHand { get; }

    /// <summary>Right hand (rows 5-9): thumb + 4 finger clusters.</summary>
    public HandViewModel RightHand { get; }

    /// <summary>Flat list of all KeyViewModels for matrix polling.</summary>
    [ObservableProperty]
    private ObservableCollection<KeyViewModel> _keys = [];

    public LayerViewModel(Layer layer, Action<int>? selectLayer = null, int totalLayers = 8,
        Dictionary<int, string>? userLayerColors = null,
        Action<KeyViewModel>? setLabelRequested = null,
        IReadOnlyDictionary<int, (byte? H, byte? S, byte? V)>? deviceLayerColors = null)
    {
        Layer = layer;
        _selectLayer = selectLayer;

        var userColor = userLayerColors?.GetValueOrDefault(layer.Index);
        var colors = LayerColorService.GetLayerColors(layer.Index, totalLayers,
            layer.ColorHue, layer.ColorSat, layer.ColorVal, userColor);
        TabColor = colors.Accent;

        var layout = BoardLayoutComputer.Compute(layer);

        LeftHand = new HandViewModel(layout.LeftHand, false, layer, totalLayers, userLayerColors, setLabelRequested, deviceLayerColors);
        RightHand = new HandViewModel(layout.RightHand, true, layer, totalLayers, userLayerColors, setLabelRequested, deviceLayerColors);

        foreach (var keyVm in LeftHand.AllKeys.Concat(RightHand.AllKeys))
            Keys.Add(keyVm);
    }

    [RelayCommand]
    private void Select() => _selectLayer?.Invoke(Index);
}
