using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using SvalboardLayerViz.Core.Macros;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// Represents one macro slot (e.g. M0, M1) in the macro editor's left-hand list.
/// Holds a mutable action list and computes a preview string.
/// </summary>
public partial class MacroSlotViewModel : ObservableObject
{
    public int Index { get; }
    public string DisplayIndex => $"M{Index}";

    public ObservableCollection<MacroAction> Actions { get; }

    [ObservableProperty]
    private string _preview = "";

    public MacroSlotViewModel(Macro macro)
    {
        Index = macro.Index;
        Actions = new ObservableCollection<MacroAction>(macro.Actions);
        Actions.CollectionChanged += OnActionsChanged;
        RefreshPreview();
    }

    /// <summary>Creates an empty slot for the given index.</summary>
    public MacroSlotViewModel(int index)
    {
        Index = index;
        Actions = [];
        Actions.CollectionChanged += OnActionsChanged;
        RefreshPreview();
    }

    private void OnActionsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshPreview();

    public void RefreshPreview() =>
        Preview = MacroPreviewHelper.GetPreview(Actions) ?? "(empty)";
}
