using SvalboardLayerViz.App.Services;
using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Keymap;

namespace SvalboardLayerViz.Tests.ViewModels;

/// <summary>
/// Test double for the App host services. Implements both <see cref="IDialogService"/>
/// and <see cref="IShellService"/> with settable hooks for the few interactions VM
/// tests observe (unlock, safety confirmation, save-completed); every other method
/// mirrors the headless no-op defaults.
/// </summary>
internal sealed class FakeAppHost : IDialogService, IShellService
{
    public Action<Action>? OnOpenUnlock;
    public Func<IReadOnlyList<SafetyWarning>, Task<bool>>? OnConfirmSafety;
    public Action<SaveResult>? OnSaveCompleted;

    // IDialogService
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
    public void OpenUnlock(Action onUnlocked) => (OnOpenUnlock ?? (cb => cb()))(onUnlocked);
    public Task<bool> ConfirmSafetyWarningsAsync(IReadOnlyList<SafetyWarning> warnings)
        => OnConfirmSafety?.Invoke(warnings) ?? Task.FromResult(true);
    public Task<string?> PromptSnapshotLabelAsync() => Task.FromResult<string?>(null);

    // IShellService
    public void ShowWindow() { }
    public void ToggleWindow() { }
    public void Quit() { }
    public void CopyDiagnostics() { }
    public void HotkeyChanged(string key, string modifiers) { }
    public void SaveCompleted(SaveResult result) => OnSaveCompleted?.Invoke(result);
}
