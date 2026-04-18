using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.Dynamic;
using SvalboardLayerViz.Core.Keymap;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// ViewModel for the tap-dance editor dialog. Lists all firmware tap-dance
/// slots; each has 4 keycode variants (tap / hold / double-tap / tap-hold) +
/// a tapping-term value. Edits live-stream through <c>applyTapDanceEdit</c>.
/// </summary>
public partial class TapDanceEditorViewModel : SlotListEditorViewModel<TapDanceRowViewModel>
{
    private readonly Action<int, byte[]>? _applyTapDanceEdit;

    protected override string UsageLabelKey => "TapDanceEditor_Usage";

    private static readonly IReadOnlySet<PickerCategory> TapCategories =
        new HashSet<PickerCategory>
        {
            PickerCategory.Basic, PickerCategory.Modifier, PickerCategory.Layer,
            PickerCategory.Media, PickerCategory.Mouse, PickerCategory.Macro,
            PickerCategory.Custom, PickerCategory.Advanced,
        };

    public TapDanceEditorViewModel(
        IReadOnlyList<byte[]> tapDances,
        KeycodeService keycodeService,
        bool isEditMode,
        Action<int, byte[]>? applyTapDanceEdit)
        : base(isEditMode)
    {
        _applyTapDanceEdit = applyTapDanceEdit;

        for (var i = 0; i < tapDances.Count; i++)
            Rows.Add(new TapDanceRowViewModel(i, tapDances[i], keycodeService, isEditMode, OnRowEdited, PickAsync));
    }

    private void OnRowEdited(int index, byte[] newBytes)
    {
        _applyTapDanceEdit?.Invoke(index, newBytes);
        RaiseUsageChanged();
    }

    private Task PickAsync(Action<ushort> onApply)
    {
        if (RequestKeyPick is null) return Task.CompletedTask;
        return RequestKeyPick(onApply, TapCategories);
    }
}

/// <summary>
/// One tap-dance entry: 4 keycode slots + tapping term (ms).
/// </summary>
public partial class TapDanceRowViewModel : ObservableObject, ISlotEditorRow
{
    private readonly KeycodeService _keycodeService;
    private readonly Action<int, byte[]> _onEdited;
    private readonly Func<Action<ushort>, Task> _pick;
    private readonly ushort[] _slots = new ushort[4]; // tap, hold, doubletap, taphold
    private ushort _tappingTerm;
    private bool _suppressTermCallback;

    public int Index { get; }
    public bool IsEditMode { get; }
    public string DisplayIndex => $"TD{Index}";

    [ObservableProperty] private string _onTapLabel = "";
    [ObservableProperty] private string _onHoldLabel = "";
    [ObservableProperty] private string _onDoubleTapLabel = "";
    [ObservableProperty] private string _onTapHoldLabel = "";
    [ObservableProperty] private bool _isEmpty;

    public ushort TappingTerm
    {
        get => _tappingTerm;
        set
        {
            if (_tappingTerm == value) return;
            _tappingTerm = value;
            OnPropertyChanged();
            if (!_suppressTermCallback && IsEditMode)
                _onEdited(Index, Encode());
        }
    }

    public TapDanceRowViewModel(
        int index, byte[] bytes, KeycodeService keycodeService, bool isEditMode,
        Action<int, byte[]> onEdited, Func<Action<ushort>, Task> pick)
    {
        Index = index;
        IsEditMode = isEditMode;
        _keycodeService = keycodeService;
        _onEdited = onEdited;
        _pick = pick;

        TapDance td;
        try
        {
            td = TapDanceCodec.Decode(bytes);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("TapDance", $"Slot {index} decode failed ({ex.Message}); showing empty.");
            td = TapDance.Empty;
        }
        _slots[0] = td.OnTap;
        _slots[1] = td.OnHold;
        _slots[2] = td.OnDoubleTap;
        _slots[3] = td.OnTapHold;
        _suppressTermCallback = true;
        try
        {
            TappingTerm = td.TappingTerm;
        }
        finally
        {
            _suppressTermCallback = false;
        }
        RefreshLabels();
    }

    public ushort GetSlot(int slot) => _slots[slot];

    [RelayCommand]
    private async Task EditSlot(string slotStr)
    {
        if (!IsEditMode) return;
        if (!int.TryParse(slotStr, out var slot) || slot < 0 || slot > 3) return;
        await _pick(code =>
        {
            _slots[slot] = code;
            RefreshLabels();
            _onEdited(Index, Encode());
        });
    }

    [RelayCommand]
    private void Clear()
    {
        if (!IsEditMode) return;
        for (var i = 0; i < 4; i++) _slots[i] = 0;
        _suppressTermCallback = true;
        try
        {
            TappingTerm = TapDance.DefaultTappingTerm;
        }
        finally
        {
            _suppressTermCallback = false;
        }
        RefreshLabels();
        _onEdited(Index, Encode());
    }

    private void RefreshLabels()
    {
        OnTapLabel = LabelFor(_slots[0]);
        OnHoldLabel = LabelFor(_slots[1]);
        OnDoubleTapLabel = LabelFor(_slots[2]);
        OnTapHoldLabel = LabelFor(_slots[3]);
        IsEmpty = _slots[0] == 0 && _slots[1] == 0 && _slots[2] == 0 && _slots[3] == 0;
    }

    private string LabelFor(ushort code) =>
        code == 0 ? "—" : _keycodeService.Resolve(code).Label;

    private byte[] Encode() => TapDanceCodec.Encode(
        new TapDance(_slots[0], _slots[1], _slots[2], _slots[3], _tappingTerm));
}
