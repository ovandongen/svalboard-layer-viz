using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.Dynamic;
using SvalboardLayerViz.Core.Keymap;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// ViewModel for the combo editor dialog. Lists all firmware combo slots; each
/// has 4 input keys + 1 output. Edits live-stream through <c>applyComboEdit</c>
/// so they land on the edit session as <c>SetComboOp</c> immediately.
/// </summary>
public partial class ComboEditorViewModel : SlotListEditorViewModel<ComboRowViewModel>
{
    private readonly Action<int, byte[]>? _applyComboEdit;

    protected override string UsageLabelKey => "ComboEditor_Usage";

    private static readonly IReadOnlySet<PickerCategory> InputCategories =
        new HashSet<PickerCategory> { PickerCategory.Basic };

    private static readonly IReadOnlySet<PickerCategory> OutputCategories =
        new HashSet<PickerCategory>
        {
            PickerCategory.Basic, PickerCategory.Modifier, PickerCategory.Layer,
            PickerCategory.Media, PickerCategory.Mouse, PickerCategory.Macro,
            PickerCategory.Custom, PickerCategory.Advanced,
        };

    public ComboEditorViewModel(
        IReadOnlyList<byte[]> combos,
        KeycodeService keycodeService,
        bool isEditMode,
        Action<int, byte[]>? applyComboEdit)
        : base(isEditMode)
    {
        _applyComboEdit = applyComboEdit;

        for (var i = 0; i < combos.Count; i++)
            Rows.Add(new ComboRowViewModel(i, combos[i], keycodeService, isEditMode, OnRowEdited, PickSlotAsync));
    }

    private void OnRowEdited(int index, byte[] newBytes)
    {
        _applyComboEdit?.Invoke(index, newBytes);
        RaiseUsageChanged();
    }

    private Task PickSlotAsync(int slotIndex, Action<ushort> onApply)
    {
        if (RequestKeyPick is null) return Task.CompletedTask;
        var categories = slotIndex == 4 ? OutputCategories : InputCategories;
        return RequestKeyPick(onApply, categories);
    }
}

/// <summary>
/// One combo entry: 4 inputs + 1 output. Keeps raw bytes in sync with the
/// 5 ushort slots so edits round-trip back to the edit session.
/// </summary>
public partial class ComboRowViewModel : ObservableObject, ISlotEditorRow
{
    private readonly KeycodeService _keycodeService;
    private readonly Action<int, byte[]> _onEdited;
    private readonly Func<int, Action<ushort>, Task> _pickSlot;
    private readonly ushort[] _slots = new ushort[5];

    public int Index { get; }
    public bool IsEditMode { get; }
    public string DisplayIndex => $"C{Index}";

    [ObservableProperty] private string _input0Label = "";
    [ObservableProperty] private string _input1Label = "";
    [ObservableProperty] private string _input2Label = "";
    [ObservableProperty] private string _input3Label = "";
    [ObservableProperty] private string _outputLabel = "";
    [ObservableProperty] private bool _isEmpty;

    public ComboRowViewModel(
        int index, byte[] bytes, KeycodeService keycodeService, bool isEditMode,
        Action<int, byte[]> onEdited, Func<int, Action<ushort>, Task> pickSlot)
    {
        Index = index;
        IsEditMode = isEditMode;
        _keycodeService = keycodeService;
        _onEdited = onEdited;
        _pickSlot = pickSlot;

        Combo combo;
        try
        {
            combo = ComboCodec.Decode(bytes);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("Combo", $"Slot {index} decode failed ({ex.Message}); showing empty.");
            combo = Combo.Empty;
        }
        _slots[0] = combo.Input0;
        _slots[1] = combo.Input1;
        _slots[2] = combo.Input2;
        _slots[3] = combo.Input3;
        _slots[4] = combo.Output;
        RefreshLabels();
    }

    public ushort GetSlot(int slot) => _slots[slot];

    [RelayCommand]
    private async Task EditSlot(string slotStr)
    {
        if (!IsEditMode) return;
        if (!int.TryParse(slotStr, out var slot) || slot < 0 || slot > 4) return;
        await _pickSlot(slot, code =>
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
        for (var i = 0; i < 5; i++) _slots[i] = 0;
        RefreshLabels();
        _onEdited(Index, Encode());
    }

    private void RefreshLabels()
    {
        Input0Label = LabelFor(_slots[0]);
        Input1Label = LabelFor(_slots[1]);
        Input2Label = LabelFor(_slots[2]);
        Input3Label = LabelFor(_slots[3]);
        OutputLabel = LabelFor(_slots[4]);
        IsEmpty = _slots[0] == 0 && _slots[1] == 0 && _slots[2] == 0
            && _slots[3] == 0 && _slots[4] == 0;
    }

    private string LabelFor(ushort code) =>
        code == 0 ? "—" : _keycodeService.Resolve(code).Label;

    private byte[] Encode() => ComboCodec.Encode(
        new Combo(_slots[0], _slots[1], _slots[2], _slots[3], _slots[4]));
}
