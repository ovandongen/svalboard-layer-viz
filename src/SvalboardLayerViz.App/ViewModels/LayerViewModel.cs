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

    public LayerViewModel(
        Layer layer,
        LayerColorPalette palette,
        Action<int>? selectLayer = null,
        Action<KeyViewModel>? setLabelRequested = null)
    {
        Layer = layer;
        _selectLayer = selectLayer;

        TabColor = palette.Get(layer.Index).Accent;

        var layout = BoardLayoutComputer.Compute(layer);
        LeftHand = new HandViewModel(layout.LeftHand, false, layer, palette, setLabelRequested);
        RightHand = new HandViewModel(layout.RightHand, true, layer, palette, setLabelRequested);

        foreach (var keyVm in LeftHand.AllKeys.Concat(RightHand.AllKeys))
            Keys.Add(keyVm);
    }

    [RelayCommand]
    private void Select() => _selectLayer?.Invoke(Index);

    /// <summary>
    /// Marks a key as having a pending edit, updating its displayed label and visual state.
    /// No-op if (row, col) is not found in this layer.
    /// </summary>
    public void ApplyPendingOverride(int row, int col, ushort newKeycode, string newLabel, string? newSecondaryLabel = null)
    {
        foreach (var keyVm in Keys)
        {
            if (keyVm.Key.Row != row || keyVm.Key.Col != col) continue;
            keyVm.PendingKeycode = newKeycode;
            keyVm.PendingLabel = newLabel;
            keyVm.PendingSecondaryLabel = newSecondaryLabel;
            keyVm.IsPending = true;
            return;
        }
    }

    /// <summary>
    /// Clears a single pending override (e.g., after undo/discard of one edit).
    /// </summary>
    public void ClearPendingOverride(int row, int col)
    {
        foreach (var keyVm in Keys)
        {
            if (keyVm.Key.Row != row || keyVm.Key.Col != col) continue;
            keyVm.IsPending = false;
            keyVm.PendingLabel = null;
            keyVm.PendingSecondaryLabel = null;
            keyVm.PendingKeycode = 0;
            return;
        }
    }

    /// <summary>Clears all pending overrides on all keys in this layer.</summary>
    public void ClearAllPendingOverrides()
    {
        foreach (var keyVm in Keys)
        {
            if (!keyVm.IsPending) continue;
            keyVm.IsPending = false;
            keyVm.PendingLabel = null;
            keyVm.PendingSecondaryLabel = null;
            keyVm.PendingKeycode = 0;
        }
    }

    /// <summary>Pushes edit-mode state down to all keys so KeyView can enable click-to-edit.</summary>
    public void SetEditMode(bool enabled)
    {
        foreach (var keyVm in Keys)
            keyVm.IsEditMode = enabled;
    }

    /// <summary>
    /// In-place update of every KeyViewModel's baseline Key from a fresh
    /// Layer record (after a reload). Keeps all VM instances stable so view
    /// bindings don't need to re-resolve.
    /// </summary>
    public void UpdateBaselineFromLayer(Layer newLayer)
    {
        foreach (var keyVm in Keys)
        {
            var newKey = newLayer.Keys.FirstOrDefault(k => k.Row == keyVm.Key.Row && k.Col == keyVm.Key.Col);
            if (newKey is not null)
                keyVm.UpdateBaseline(newKey);
        }
    }
}
