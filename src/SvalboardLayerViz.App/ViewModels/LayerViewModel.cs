using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.App.ViewModels;

public partial class LayerViewModel : ObservableObject
{
    public Layer Layer { get; }
    private readonly Action<int>? _selectLayer;

    public string DisplayName => Layer.DisplayName;
    public int Index => Layer.Index;

    [ObservableProperty]
    private ObservableCollection<KeyViewModel> _keys = [];

    public LayerViewModel(Layer layer, Action<int>? selectLayer = null)
    {
        Layer = layer;
        _selectLayer = selectLayer;

        foreach (var key in layer.Keys)
        {
            Keys.Add(new KeyViewModel(key, layer));
        }
    }

    [RelayCommand]
    private void Select() => _selectLayer?.Invoke(Index);
}
