using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Keymap;

namespace SvalboardLayerViz.App.Services;

/// <summary>
/// Host-provided dialog and prompt operations that <see cref="MainWindowViewModel"/>
/// drives. Injected so the VM never holds windowing concerns: the headless
/// <see cref="NoopDialogService"/> default lets Core/VM tests run without a UI,
/// while <see cref="DesktopDialogService"/> wires real Avalonia dialogs in
/// production. Replaces the old grab-bag of settable <c>Open*Requested</c>
/// callback properties on the VM.
/// </summary>
public interface IDialogService
{
    void OpenSettings(int? initialTabIndex);
    void OpenHistory();
    void OpenMacroEditor();
    void OpenComboEditor();
    void OpenTapDanceEditor();
    void OpenExport();
    void OpenHelp();
    void OpenDiagnostics();
    void OpenKeyPicker(KeyEditRequest request);
    void ShowKeyLabelEditor(KeyViewModel keyVm);

    /// <summary>
    /// Shows the Vial unlock dialog; invokes <paramref name="onUnlocked"/> when the
    /// user completes the unlock sequence. The headless default invokes it
    /// immediately (tests / no-UI begin editing straight away).
    /// </summary>
    void OpenUnlock(Action onUnlocked);

    /// <summary>
    /// Pre-save confirmation when the keymap has safety warnings. Returns
    /// <c>true</c> to proceed, <c>false</c> to abort. Headless default returns
    /// <c>true</c> (auto-confirm).
    /// </summary>
    Task<bool> ConfirmSafetyWarningsAsync(IReadOnlyList<SafetyWarning> warnings);

    /// <summary>Prompts for a snapshot label. <c>null</c> = the user cancelled. Headless default returns <c>null</c>.</summary>
    Task<string?> PromptSnapshotLabelAsync();
}

/// <summary>Headless default: every dialog is a no-op. Used by tests and any VM constructed without a host.</summary>
internal sealed class NoopDialogService : IDialogService
{
    public void OpenSettings(int? initialTabIndex) { }
    public void OpenHistory() { }
    public void OpenMacroEditor() { }
    public void OpenComboEditor() { }
    public void OpenTapDanceEditor() { }
    public void OpenExport() { }
    public void OpenHelp() { }
    public void OpenDiagnostics() { }
    public void OpenKeyPicker(KeyEditRequest request) { }
    public void ShowKeyLabelEditor(KeyViewModel keyVm) { }
    public void OpenUnlock(Action onUnlocked) => onUnlocked();
    public Task<bool> ConfirmSafetyWarningsAsync(IReadOnlyList<SafetyWarning> warnings) => Task.FromResult(true);
    public Task<string?> PromptSnapshotLabelAsync() => Task.FromResult<string?>(null);
}
