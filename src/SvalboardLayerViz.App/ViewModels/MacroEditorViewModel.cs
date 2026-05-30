using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.Core.Macros;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// ViewModel for the macro editor dialog. Shows all macro slots on the left,
/// selected macro's actions on the right, and a buffer usage bar at the bottom.
/// </summary>
public partial class MacroEditorViewModel : ObservableObject, IKeyPickerHost
{
    private readonly Action<byte[]>? _applyMacroEdit;
    private int _bufferCapacity;

    public ObservableCollection<MacroSlotViewModel> MacroSlots { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSlot))]
    private MacroSlotViewModel? _selectedSlot;

    public ObservableCollection<MacroActionViewModel> SelectedActions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemoveAction))]
    [NotifyPropertyChangedFor(nameof(CanMoveActionUp))]
    [NotifyPropertyChangedFor(nameof(CanMoveActionDown))]
    private MacroActionViewModel? _selectedAction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsagePercent))]
    [NotifyPropertyChangedFor(nameof(IsOverCapacity))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private int _usedBytes;


    public int BufferCapacity => _bufferCapacity;
    public bool IsEditMode { get; }
    public bool HasSelectedSlot => SelectedSlot is not null;
    public double UsagePercent => _bufferCapacity > 0 ? (double)UsedBytes / _bufferCapacity * 100 : 0;
    public bool IsOverCapacity => UsedBytes > _bufferCapacity;
    public bool CanSave => IsEditMode && !IsOverCapacity;
    public bool CanRemoveAction => SelectedAction is not null && IsEditMode;
    public bool CanMoveActionUp => SelectedAction is not null && IsEditMode
        && SelectedActions.IndexOf(SelectedAction) > 0;
    public bool CanMoveActionDown => SelectedAction is not null && IsEditMode
        && SelectedActions.IndexOf(SelectedAction) < SelectedActions.Count - 1;

    /// <summary>Invoked when the user clicks Save. Wired by App.axaml.cs to close dialog.</summary>
    public Action? Saved { get; set; }

    /// <summary>Invoked when the user clicks Cancel. Wired by App.axaml.cs to close dialog.</summary>
    public Action? Cancelled { get; set; }

    /// <summary>
    /// Opens the key picker dialog. The <see cref="Action{T}"/> callback receives the selected
    /// ushort keycode. Wired by App.axaml.cs to open KeyPickerDialog modal over this dialog.
    /// </summary>
    public Func<Action<ushort>, IReadOnlySet<PickerCategory>?, Task>? RequestKeyPick { get; set; }

    private static readonly IReadOnlySet<PickerCategory> TapCategories =
        new HashSet<PickerCategory>
        {
            PickerCategory.Basic,
            PickerCategory.Media,
            PickerCategory.Mouse,
            PickerCategory.Custom,
            PickerCategory.Advanced,
        };

    private static readonly IReadOnlySet<PickerCategory> HoldReleaseCategories =
        new HashSet<PickerCategory> { PickerCategory.Basic };

    public MacroEditorViewModel(MacroBuffer? macroBuffer, bool isEditMode, Action<byte[]>? applyMacroEdit)
    {
        IsEditMode = isEditMode;
        _applyMacroEdit = applyMacroEdit;
        _bufferCapacity = macroBuffer?.BufferCapacity ?? 0;

        if (macroBuffer is not null)
            LoadSlots(macroBuffer);

        RecalculateBufferSize();
    }

    /// <summary>
    /// Replaces all slot data from a new buffer. Called when deferred macro
    /// load completes while the dialog is already open.
    /// </summary>
    public void UpdateFromBuffer(MacroBuffer macroBuffer)
    {
        _bufferCapacity = macroBuffer.BufferCapacity;
        SelectedAction = null;
        SelectedSlot = null;
        MacroSlots.Clear();
        SelectedActions.Clear();
        LoadSlots(macroBuffer);
        RecalculateBufferSize();
        OnPropertyChanged(nameof(BufferCapacity));
        OnPropertyChanged(nameof(UsagePercent));
    }

    private void LoadSlots(MacroBuffer macroBuffer)
    {
        foreach (var macro in macroBuffer.Macros)
            MacroSlots.Add(new MacroSlotViewModel(macro));
    }

    partial void OnSelectedSlotChanged(MacroSlotViewModel? value)
    {
        RebuildSelectedActions();
    }

    private void RebuildSelectedActions()
    {
        // Unhook changed callbacks from old action VMs
        foreach (var vm in SelectedActions)
            vm.Changed = null;

        SelectedActions.Clear();

        if (SelectedSlot is null) return;

        foreach (var action in SelectedSlot.Actions)
        {
            var vm = new MacroActionViewModel(action);
            vm.Changed = () => OnActionContentChanged(vm);
            SelectedActions.Add(vm);
        }
    }

    private void OnActionContentChanged(MacroActionViewModel vm)
    {
        if (SelectedSlot is null) return;

        var idx = SelectedActions.IndexOf(vm);
        if (idx >= 0 && idx < SelectedSlot.Actions.Count)
            SelectedSlot.Actions[idx] = vm.Action;

        RecalculateBufferSize();
    }

    private void AddActionToSlot(MacroAction action)
    {
        if (SelectedSlot is null || !IsEditMode) return;

        SelectedSlot.Actions.Add(action);
        var vm = new MacroActionViewModel(action);
        vm.Changed = () => OnActionContentChanged(vm);
        SelectedActions.Add(vm);
        SelectedAction = vm;
        RecalculateBufferSize();
    }

    [RelayCommand]
    private async Task AddTapAction()
    {
        if (SelectedSlot is null || !IsEditMode || RequestKeyPick is null) return;
        await RequestKeyPick(code =>
        {
            var action = KeycodeToMacroTap(code);
            if (action is not null)
                AddActionToSlot(action);
        }, TapCategories);
    }

    [RelayCommand]
    private async Task AddDownAction()
    {
        if (SelectedSlot is null || !IsEditMode || RequestKeyPick is null) return;
        await RequestKeyPick(code =>
        {
            if (code > 0xFF) return;
            AddActionToSlot(new MacroDownAction((byte)code));
        }, HoldReleaseCategories);
    }

    [RelayCommand]
    private async Task AddUpAction()
    {
        if (SelectedSlot is null || !IsEditMode || RequestKeyPick is null) return;
        await RequestKeyPick(code =>
        {
            if (code > 0xFF) return;
            AddActionToSlot(new MacroUpAction((byte)code));
        }, HoldReleaseCategories);
    }

    [RelayCommand]
    private async Task EditKeyAction()
    {
        if (SelectedSlot is null || SelectedAction is null || !IsEditMode
            || !SelectedAction.IsKey || RequestKeyPick is null) return;

        var actionVm = SelectedAction;
        var idx = SelectedActions.IndexOf(actionVm);
        if (idx < 0) return;

        var categories = actionVm.Action switch
        {
            MacroDownAction or MacroUpAction => HoldReleaseCategories,
            MacroTapAction or MacroModTapAction => TapCategories,
            _ => null,
        };

        await RequestKeyPick(code =>
        {
            MacroAction? newAction = actionVm.Action switch
            {
                MacroTapAction or MacroModTapAction => KeycodeToMacroTap(code),
                MacroDownAction => code <= 0xFF ? new MacroDownAction((byte)code) : null,
                MacroUpAction => code <= 0xFF ? new MacroUpAction((byte)code) : null,
                _ => null
            };
            if (newAction is null) return;
            actionVm.ReplaceAction(newAction);
            if (idx < SelectedSlot.Actions.Count)
                SelectedSlot.Actions[idx] = newAction;
            SelectedSlot.RefreshPreview();
            RecalculateBufferSize();
        }, categories);
    }

    [RelayCommand]
    private void AddDelayAction()
    {
        if (SelectedSlot is null || !IsEditMode) return;
        AddActionToSlot(new MacroDelayAction(100));
    }

    [RelayCommand]
    private void AddTextAction()
    {
        if (SelectedSlot is null || !IsEditMode) return;
        AddActionToSlot(new MacroTextAction(""));
    }

    [RelayCommand]
    private void RemoveAction()
    {
        if (SelectedSlot is null || SelectedAction is null || !IsEditMode) return;

        var idx = SelectedActions.IndexOf(SelectedAction);
        SelectedAction.Changed = null;
        SelectedActions.RemoveAt(idx);
        SelectedSlot.Actions.RemoveAt(idx);

        // Select adjacent action
        if (SelectedActions.Count > 0)
            SelectedAction = SelectedActions[Math.Min(idx, SelectedActions.Count - 1)];
        else
            SelectedAction = null;

        RecalculateBufferSize();
    }

    [RelayCommand]
    private void MoveActionUp()
    {
        if (SelectedSlot is null || SelectedAction is null || !IsEditMode) return;
        var idx = SelectedActions.IndexOf(SelectedAction);
        if (idx <= 0) return;

        SelectedActions.Move(idx, idx - 1);
        (SelectedSlot.Actions[idx], SelectedSlot.Actions[idx - 1]) =
            (SelectedSlot.Actions[idx - 1], SelectedSlot.Actions[idx]);

        SelectedSlot.RefreshPreview();
        RecalculateBufferSize();
    }

    [RelayCommand]
    private void MoveActionDown()
    {
        if (SelectedSlot is null || SelectedAction is null || !IsEditMode) return;
        var idx = SelectedActions.IndexOf(SelectedAction);
        if (idx < 0 || idx >= SelectedActions.Count - 1) return;

        SelectedActions.Move(idx, idx + 1);
        (SelectedSlot.Actions[idx], SelectedSlot.Actions[idx + 1]) =
            (SelectedSlot.Actions[idx + 1], SelectedSlot.Actions[idx]);

        SelectedSlot.RefreshPreview();
        RecalculateBufferSize();
    }

    [RelayCommand]
    private void Save()
    {
        if (!CanSave) return;

        var macros = BuildMacroList();
        var encoded = MacroCodec.Encode(macros, _bufferCapacity);
        _applyMacroEdit?.Invoke(encoded);
        Saved?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Cancelled?.Invoke();
    }

    private void RecalculateBufferSize()
    {
        var macros = BuildMacroList();
        UsedBytes = MacroCodec.ComputeEncodedSize(macros);
    }

    private List<Macro> BuildMacroList()
    {
        var macros = new List<Macro>(MacroSlots.Count);
        foreach (var slot in MacroSlots)
            macros.Add(new Macro(slot.Index, slot.Actions.ToList()));
        return macros;
    }

    /// <summary>
    /// Converts a picker keycode to a macro tap action. Basic keycodes (≤0xFF)
    /// become <see cref="MacroTapAction"/>, modified keycodes (0x0100–0x1FFF)
    /// become <see cref="MacroModTapAction"/> with extracted mods + base key.
    /// Returns null for unsupported keycode ranges.
    /// </summary>
    internal static MacroAction? KeycodeToMacroTap(ushort code)
    {
        if (code <= 0xFF)
            return new MacroTapAction((byte)code);

        // Modified keycode range: 0x0100–0x1FFF
        // Keycode mod field (5 bits): bit0=Ctrl, 1=Shift, 2=Alt, 3=Gui, 4=Right.
        // Macro mods byte (8 bits): bits 0–3 = left CSAG, bits 4–7 = right CSAG.
        if (code is >= 0x0100 and <= 0x1FFF)
        {
            var modField = (byte)((code >> 8) & 0x1F);
            var csag = (byte)(modField & 0x0F);
            var isRight = (modField & 0x10) != 0;
            var macroMods = isRight ? (byte)(csag << 4) : csag;
            var baseKey = (byte)(code & 0xFF);
            return new MacroModTapAction(baseKey, macroMods);
        }

        return null;
    }
}
