using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.App.Localization;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// Marker for rows in a <see cref="SlotListEditorViewModel{TRow}"/>. Exposes
/// the "empty" state so the base class can compute used-vs-capacity totals.
/// </summary>
public interface ISlotEditorRow : INotifyPropertyChanged
{
    bool IsEmpty { get; }
}

/// <summary>
/// Shared scaffolding for dialog VMs that edit a fixed-size list of firmware
/// slots (combos, tap-dances, and similar dynamic entries). Owns the
/// <c>Rows</c> collection, selection, close, key-picker delegate, and the
/// capacity/used/usage-label triple. Subclasses supply the resource key for
/// the usage label and populate <c>Rows</c> in their constructor.
/// </summary>
public abstract partial class SlotListEditorViewModel<TRow> : ObservableObject, IKeyPickerHost
    where TRow : class, ISlotEditorRow
{
    public ObservableCollection<TRow> Rows { get; } = [];
    public bool IsEditMode { get; }
    public int Capacity => Rows.Count;
    public int UsedCount => Rows.Count(r => !r.IsEmpty);
    public string UsageLabel => Loc.Instance.Format(UsageLabelKey, UsedCount, Capacity);

    [ObservableProperty] private TRow? _selectedRow;

    public Action? Closed { get; set; }
    public Func<Action<ushort>, IReadOnlySet<PickerCategory>?, Task>? RequestKeyPick { get; set; }

    protected abstract string UsageLabelKey { get; }

    protected SlotListEditorViewModel(bool isEditMode)
    {
        IsEditMode = isEditMode;
    }

    /// <summary>Subclass calls this from its own per-row edited callback.</summary>
    protected void RaiseUsageChanged()
    {
        OnPropertyChanged(nameof(UsedCount));
        OnPropertyChanged(nameof(UsageLabel));
    }

    [RelayCommand]
    private void Close() => Closed?.Invoke();
}
