using SvalboardLayerViz.App.Localization;
using SvalboardLayerViz.Core.Device;
using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Macros;
using SvalboardLayerViz.Core.Models;
using SvalboardLayerViz.Core.Protocol;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// Owns the keymap edit/save subsystem extracted from <see cref="MainWindowViewModel"/>:
/// the in-memory <see cref="KeymapEditSession"/> lifecycle (enter / activate / restore),
/// all <c>Apply*Edit</c> staging, dirty-count bookkeeping, the full save pipeline
/// (preflight → write → reload → reconcile), and partial/cancel recovery.
///
/// An implementation detail of the VM — holds a concrete back-reference and drives the
/// VM's observable surface (IsEditMode / DirtyCount / IsSaving / StatusMessage /
/// KeyboardConfig / Layers) directly. The VM keeps the public edit API and the
/// <c>[RelayCommand]</c>s (EnterEdit / DiscardEdit / SaveEdit) as thin delegators, and
/// routes polling pause/resume back through <see cref="MainWindowViewModel.StopAllPolling"/>
/// / <see cref="MainWindowViewModel.ResumePolling"/> so this class never references the
/// polling collaborator.
/// </summary>
internal sealed class EditSessionController
{
    private readonly MainWindowViewModel _vm;

    /// <summary>In-memory edit state while <see cref="MainWindowViewModel.IsEditMode"/> is true; null otherwise.</summary>
    private KeymapEditSession? _editSession;

    /// <summary>
    /// Remembers whether the device was locked when the user entered edit mode.
    /// Used to decide whether to re-lock after a successful save: if the user
    /// had to go through the unlock dialog to edit, we put the device back the
    /// way we found it. If they started unlocked (e.g. firmware shipped
    /// unlocked, or they unlocked in Vial earlier), we leave it alone.
    /// </summary>
    private bool _wasLockedOnEnterEdit;

    /// <summary>Cancellation source for an in-flight save. Null when not saving.</summary>
    private CancellationTokenSource? _saveCts;

    public EditSessionController(MainWindowViewModel vm) => _vm = vm;

    /// <summary>The active edit session, or null when not editing. Exposed via vm.EditSession (tests) and the VM's macro/key routing.</summary>
    internal KeymapEditSession? Session => _editSession;

    // ============================================================================
    // Enter / begin / activate / restore
    // ============================================================================

    /// <summary>
    /// Enters edit mode. Checks unlock status first; if locked, opens the unlock dialog
    /// and defers session creation until the user completes the unlock sequence.
    /// </summary>
    public async Task EnterEditAsync()
    {
        if (_vm.IsEditMode || !_vm.IsConnected || _vm.KeyboardConfig is null) return;

        UnlockStatus status;
        try
        {
            status = await Task.Run(() => _vm.ProtocolService.GetUnlockStatus());
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("Edit", $"GetUnlockStatus failed: {ex.Message}");
            _vm.StatusMessage = Loc.Instance.Format("Status_EditUnlockErrorFormat", ex.Message);
            return;
        }

        _wasLockedOnEnterEdit = !status.Unlocked;

        if (status.Unlocked)
            BeginEditSession();
        else
            _vm.Dialogs.OpenUnlock(BeginEditSession);
    }

    /// <summary>
    /// Starts the in-memory edit session. Stops polling (which interferes with edit clicks)
    /// and flips every layer/key into edit mode for click handling.
    /// </summary>
    private void BeginEditSession()
    {
        if (_vm.KeyboardConfig is null) return;

        var layers = _vm.KeyboardConfig.Layers.Count;
        var rows = _vm.KeyboardConfig.MatrixRows;
        var cols = _vm.KeyboardConfig.MatrixCols;
        var baseline = new ushort[layers, rows, cols];
        foreach (var layer in _vm.KeyboardConfig.Layers)
        {
            foreach (var key in layer.Keys)
                baseline[layer.Index, key.Row, key.Col] = key.RawKeycode;
        }

        byte[]? macroBuffer = _vm.KeyboardConfig.Macros is not null
            ? MacroCodec.Encode(_vm.KeyboardConfig.Macros) : null;

        var session = new KeymapEditSession(baseline, BuildQmkSettingsDict(), BuildQmkSettingsWidths(), macroBuffer,
            _vm.KeyboardConfig.Combos, _vm.KeyboardConfig.TapDances);
        ActivateEditSession(session);
        _vm.DirtyCount = 0;
        _vm.StatusMessage = Loc.Instance["Status_EditModeActive"];
        DiagnosticLog.Info("Edit", $"Edit session started: {layers}x{rows}x{cols}");
    }

    /// <summary>
    /// Shared activation: sets the edit session, stops polling, and flips layer VMs
    /// into edit mode. Called by both <see cref="BeginEditSession"/> and
    /// <see cref="RestoreFromSnapshot"/>.
    /// </summary>
    private void ActivateEditSession(KeymapEditSession session)
    {
        _vm.StopAllPolling();
        _editSession = session;
        _vm.IsEditMode = true;

        // Rebuild layer VMs so that previously hidden empty layers become
        // visible — BuildLayerViewModels skips the empty-layer filter when
        // IsEditMode is true, and wires SetEditMode + click routing.
        var currentLayer = _vm.SelectedLayerIndex;
        _vm.BuildLayerViewModels(_vm.Settings.Load());
        _vm.SelectedLayerIndex = -1;
        _vm.SelectedLayerIndex = Math.Min(currentLayer, _vm.Layers.Count - 1);
    }

    /// <summary>
    /// Enters edit mode with a pre-populated session from a snapshot restore.
    /// Each pending change is reflected as a visual override on the corresponding key.
    /// If already in edit mode (e.g. after a save that kept edit mode active),
    /// the existing session is cleanly replaced.
    /// </summary>
    public void RestoreFromSnapshot(KeymapEditSession session)
    {
        if (_vm.KeyboardConfig is null) return;

        // If already in edit mode, tear down the existing session first
        if (_vm.IsEditMode)
        {
            _editSession?.Discard();
            _editSession = null;
            foreach (var layerVm in _vm.Layers)
                layerVm.ClearAllPendingOverrides();
            _vm.IsEditMode = false;
        }

        ActivateEditSession(session);

        // Apply visual overrides for each pending change
        foreach (var (layer, row, col, _, newCode) in session.PendingChanges)
        {
            var layerVm = _vm.Layers.FirstOrDefault(l => l.Index == layer);
            if (layerVm is not null)
            {
                var info = _vm.KeycodeService.Resolve(newCode);
                layerVm.ApplyPendingOverride(row, col, newCode, info.Label, info.SecondaryLabel);
            }
        }

        _vm.DirtyCount = ComputeDirtyCount();
        _vm.StatusMessage = Loc.Instance.Format("Status_RestoreLoaded", _vm.DirtyCount);
        DiagnosticLog.Info("Snapshot", $"Restore session loaded: {session.PendingChanges.Count} pending changes");
    }

    // ============================================================================
    // Apply edits (staged into the session)
    // ============================================================================

    /// <summary>
    /// Applies a keycode change from the picker. Mirrors the change into the affected
    /// LayerViewModel's pending overrides and bumps <see cref="MainWindowViewModel.DirtyCount"/>.
    /// </summary>
    public void ApplyKeyEdit(int layer, int row, int col, ushort newCode)
    {
        if (_editSession is null) return;

        var oldCode = _editSession.GetCurrent(layer, row, col);
        if (oldCode == newCode) return;

        _editSession.Apply(new SetKeyOp(layer, row, col, oldCode, newCode));

        var baseline = _editSession.GetBaseline(layer, row, col);
        var layerVm = _vm.Layers.FirstOrDefault(l => l.Index == layer);
        if (layerVm is not null)
        {
            if (newCode == baseline)
            {
                layerVm.ClearPendingOverride(row, col);
            }
            else
            {
                var info = _vm.KeycodeService.Resolve(newCode);
                layerVm.ApplyPendingOverride(row, col, newCode, info.Label, info.SecondaryLabel);
            }
        }

        _vm.DirtyCount = ComputeDirtyCount();
        DiagnosticLog.Info("Edit", $"ApplyKeyEdit L{layer} R{row} C{col}: 0x{oldCode:X4} → 0x{newCode:X4} (dirty={_vm.DirtyCount})");
    }

    /// <summary>
    /// Stages a QMK setting edit in the current edit session. Called from the
    /// Device tab in SettingsWindow when the user changes a firmware setting value.
    /// </summary>
    public void ApplySettingEdit(ushort settingId, ushort newValue)
    {
        if (_editSession is null) return;

        var oldValue = _editSession.GetCurrentSetting(settingId) ?? 0;
        if (oldValue == newValue) return;

        _editSession.Apply(new SetQmkSettingOp(settingId, oldValue, newValue));
        _vm.DirtyCount = ComputeDirtyCount();
        DiagnosticLog.Info("Edit", $"ApplySettingEdit 0x{settingId:X4}: {oldValue} → {newValue} (dirty={_vm.DirtyCount})");
    }

    /// <summary>
    /// Applies a macro buffer edit (whole-buffer swap). Called by the macro editor VM.
    /// </summary>
    public void ApplyMacroEdit(byte[] newEncodedBuffer)
    {
        if (_editSession is null) return;

        var oldBuffer = _editSession.GetCurrentMacroBuffer();
        if (oldBuffer is null) return;

        _editSession.Apply(new SetMacroBufferOp(oldBuffer, newEncodedBuffer));
        _vm.DirtyCount = ComputeDirtyCount();
        DiagnosticLog.Info("Edit", $"ApplyMacroEdit: buffer {newEncodedBuffer.Length}B (dirty={_vm.DirtyCount})");
    }

    /// <summary>
    /// Applies a single combo entry edit (index + 10-byte payload) as a
    /// <see cref="SetComboOp"/>. Called by the combo editor dialog.
    /// </summary>
    public void ApplyComboEdit(int index, byte[] newBytes)
    {
        if (_editSession is null) return;
        if (index < 0 || index >= _editSession.ComboCount) return;

        var oldBytes = _editSession.GetCurrentCombo(index);
        if (oldBytes.AsSpan().SequenceEqual(newBytes)) return;

        _editSession.Apply(new SetComboOp(index, oldBytes, (byte[])newBytes.Clone()));
        _vm.DirtyCount = ComputeDirtyCount();
        DiagnosticLog.Info("Edit", $"ApplyComboEdit #{index} (dirty={_vm.DirtyCount})");
    }

    /// <summary>
    /// Applies a single tap-dance entry edit (index + 10-byte payload) as a
    /// <see cref="SetTapDanceOp"/>. Called by the tap-dance editor dialog.
    /// </summary>
    public void ApplyTapDanceEdit(int index, byte[] newBytes)
    {
        if (_editSession is null) return;
        if (index < 0 || index >= _editSession.TapDanceCount) return;

        var oldBytes = _editSession.GetCurrentTapDance(index);
        if (oldBytes.AsSpan().SequenceEqual(newBytes)) return;

        _editSession.Apply(new SetTapDanceOp(index, oldBytes, (byte[])newBytes.Clone()));
        _vm.DirtyCount = ComputeDirtyCount();
        DiagnosticLog.Info("Edit", $"ApplyTapDanceEdit #{index} (dirty={_vm.DirtyCount})");
    }

    /// <summary>
    /// Returns the working combo list — from the edit session if active,
    /// otherwise the device baseline. Used by the combo editor dialog.
    /// </summary>
    public IReadOnlyList<byte[]> GetCurrentCombos()
    {
        if (_editSession is not null)
        {
            var list = new List<byte[]>(_editSession.ComboCount);
            for (var i = 0; i < _editSession.ComboCount; i++)
                list.Add(_editSession.GetCurrentCombo(i));
            return list;
        }
        return _vm.KeyboardConfig?.Combos ?? [];
    }

    /// <summary>
    /// Returns the working tap-dance list — from the edit session if active,
    /// otherwise the device baseline. Used by the tap-dance editor dialog.
    /// </summary>
    public IReadOnlyList<byte[]> GetCurrentTapDances()
    {
        if (_editSession is not null)
        {
            var list = new List<byte[]>(_editSession.TapDanceCount);
            for (var i = 0; i < _editSession.TapDanceCount; i++)
                list.Add(_editSession.GetCurrentTapDance(i));
            return list;
        }
        return _vm.KeyboardConfig?.TapDances ?? [];
    }

    /// <summary>
    /// Returns the current working macro buffer — from the edit session if active,
    /// otherwise from the loaded KeyboardConfig. Used by the macro editor dialog
    /// so it shows in-progress edits, not stale baseline data.
    /// </summary>
    public MacroBuffer? GetCurrentMacros()
    {
        if (_editSession?.GetCurrentMacroBuffer() is { } buffer && _vm.KeyboardConfig?.Macros is { } original)
            return MacroCodec.Decode(buffer, original.Macros.Count, original.BufferCapacity);
        return _vm.KeyboardConfig?.Macros;
    }

    private int ComputeDirtyCount() =>
        _editSession is null ? 0
        : _editSession.PendingChanges.Count
          + _editSession.PendingSettingsChanges.Count
          + (_editSession.HasPendingMacroChanges ? 1 : 0)
          + (_editSession.HasPendingComboChanges ? 1 : 0)
          + (_editSession.HasPendingTapDanceChanges ? 1 : 0);

    // ============================================================================
    // Discard / save / disconnect
    // ============================================================================

    /// <summary>
    /// Discards all pending changes and exits edit mode. Restores polling if it was on.
    /// </summary>
    public void DiscardEdit()
    {
        if (!_vm.IsEditMode) return;

        _editSession?.Discard();
        _editSession = null;
        _vm.DirtyCount = 0;
        _vm.IsEditMode = false;
        _wasLockedOnEnterEdit = false;

        // Rebuild layer VMs so empty layers are hidden again in view mode,
        // and clear edit-mode state on remaining layers.
        var currentLayer = _vm.SelectedLayerIndex;
        _vm.BuildLayerViewModels(_vm.Settings.Load());
        _vm.SelectedLayerIndex = -1;
        _vm.SelectedLayerIndex = Math.Clamp(currentLayer, 0, Math.Max(0, _vm.Layers.Count - 1));

        // Resume polling if it was enabled before edit mode
        _vm.ResumePolling();

        _vm.RefreshStatusMessage();
        DiagnosticLog.Info("Edit", "Edit session discarded");
    }

    /// <summary>
    /// Flushes every pending edit to the device. Full pipeline:
    /// pre-save safety check → pre-save snapshot → per-op write loop →
    /// reload device state → reconcile → post-save snapshot → optional re-lock.
    ///
    /// On success the edit session is cleared but edit mode stays active so
    /// the user can keep editing. On a mid-batch failure the session is
    /// rebuilt from the reloaded baseline and refilled with the still-pending
    /// writes, so the user can fix the problem and press Save again without
    /// re-typing anything.
    /// </summary>
    public async Task SaveEditAsync()
    {
        if (_editSession is null || _vm.KeyboardConfig is null) return;

        _vm.IsSaving = true;
        _saveCts = new CancellationTokenSource();
        var ct = _saveCts.Token;
        var totalPending = ComputeDirtyCount();

        try
        {
            if (!await RunPreflightAsync()) return;

            _vm.StatusMessage = Loc.Instance.Format("Status_SaveInProgressFormat", totalPending);

            var flowCtx = new SaveFlow.Context(
                Protocol: _vm.ProtocolService,
                SnapshotService: _vm.SnapshotService,
                EditSession: _editSession,
                KeyboardId: _vm.GetKeyboardId(),
                DeviceName: _vm.ConnectedDeviceName ?? _vm.KeyboardConfig.DeviceName,
                KeepFirstConnectDays: _vm.Settings.Load().KeepFirstConnectDays,
                CurrentDeviceSnapshot: _vm.CurrentDeviceSnapshot,
                ReloadAsync: (macroBuf, skip, token) => ReloadKeymapFromDeviceAsync(token, macroBuf, skip));

            var flow = await SaveFlow.RunAsync(flowCtx, ct);

            SaveResult result;
            if (flow.Execution.Cancelled)
                result = new SaveCancelled(flow.Execution.Applied.Count);
            else if (flow.Execution.Failure is null)
                result = new SaveSuccess(flow.Execution.Applied.Count);
            else
                result = BuildPartialResult(flow.Execution, flow.Intended, totalPending);

            ApplySaveResult(result, totalPending);

            if (result is SaveSuccess && _wasLockedOnEnterEdit)
            {
                await SaveFlow.ReLockAsync(_vm.ProtocolService);
                _wasLockedOnEnterEdit = false;
            }

            _vm.Shell.SaveCompleted(result);
        }
        catch (OperationCanceledException)
        {
            DiagnosticLog.Warn("Save", "Save cancelled");
            try { await ReloadKeymapFromDeviceAsync(CancellationToken.None); }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("Save", $"Post-cancel reload failed: {ex.Message}");
            }
            _vm.StatusMessage = Loc.Instance["Status_SaveCancelled"];
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("Save", $"Unexpected save error: {ex}");
            _vm.StatusMessage = Loc.Instance.Format("Status_SaveErrorFormat", ex.Message);
        }
        finally
        {
            _vm.IsSaving = false;
            _saveCts?.Dispose();
            _saveCts = null;
        }
    }

    /// <summary>
    /// Pre-save gates: safety-warning confirmation + macro buffer overflow.
    /// Returns false (and notifies the shell with
    /// <see cref="SaveAborted"/>) when the save must not proceed.
    /// </summary>
    private async Task<bool> RunPreflightAsync()
    {
        var intended = _editSession!.CloneCurrent();
        var warnings = PreSaveSafetyCheck.Check(intended);
        if (warnings.Count > 0 && !await _vm.Dialogs.ConfirmSafetyWarningsAsync(warnings))
        {
            _vm.StatusMessage = Loc.Instance["Status_SaveCancelledSafety"];
            DiagnosticLog.Info("Save",
                $"Save aborted by user: {warnings.Count} safety warning(s)");
            _vm.Shell.SaveCompleted(new SaveAborted("safety warnings declined"));
            return false;
        }

        if (_editSession.HasPendingMacroChanges && _vm.KeyboardConfig!.Macros is not null)
        {
            var currentBuf = _editSession.GetCurrentMacroBuffer();
            if (currentBuf is not null && currentBuf.Length > _vm.KeyboardConfig.Macros.BufferCapacity)
            {
                _vm.StatusMessage = Loc.Instance["Edit_MacroBufferOverflow"];
                DiagnosticLog.Warn("Save",
                    $"Save blocked: macro buffer {currentBuf.Length} > capacity {_vm.KeyboardConfig.Macros.BufferCapacity}");
                _vm.Shell.SaveCompleted(new SaveAborted("macro buffer overflow"));
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Tears down the edit session because the device disconnected mid-edit.
    /// Called by the VM's OnIsConnectedChanged hook.
    /// </summary>
    internal void AbortForDisconnect()
    {
        _saveCts?.Cancel();
        _editSession?.Discard();
        _editSession = null;
        _vm.DirtyCount = 0;
        _vm.IsEditMode = false;
        _wasLockedOnEnterEdit = false;

        foreach (var layerVm in _vm.Layers)
        {
            layerVm.ClearAllPendingOverrides();
            layerVm.SetEditMode(false);
        }

        _vm.StatusMessage = Loc.Instance["Status_EditAbortedDisconnect"];
        DiagnosticLog.Warn("Edit", "Edit session aborted: device disconnected");
    }

    // ============================================================================
    // Save helpers
    // ============================================================================

    private Dictionary<ushort, ushort> BuildQmkSettingsDict() =>
        _vm.KeyboardConfig?.QmkSettings.ToDictionary(s => s.SettingId, s => s.Value)
        ?? new Dictionary<ushort, ushort>();

    private Dictionary<ushort, byte> BuildQmkSettingsWidths() =>
        _vm.KeyboardConfig?.QmkSettings.ToDictionary(s => s.SettingId, s => s.Width)
        ?? new Dictionary<ushort, byte>();

    private ushort[,,] GetDeviceKeymap() => _vm.CurrentDeviceSnapshot().Keymap;

    /// <summary>
    /// Re-fetches the keymap from the connected device without re-running
    /// device enumeration or the LED/definition probes. Runs on a background
    /// thread (the protocol service is synchronous); UI updates happen on
    /// the calling thread once it resumes.
    /// </summary>
    private async Task ReloadKeymapFromDeviceAsync(CancellationToken ct, byte[]? knownMacroBuffer = null, bool skipMacroReload = false)
    {
        if (_vm.KeyboardConfig is null) return;

        var refresh = await Task.Run(
            () => DeviceRefreshService.Fetch(_vm.ProtocolService, _vm.KeyboardConfig, knownMacroBuffer, skipMacroReload), ct);

        var physical = SvalboardLayout.GetKeyPositions();
        var updatedLayers = new List<Layer>(_vm.KeyboardConfig.Layers.Count);
        foreach (var layer in _vm.KeyboardConfig.Layers)
        {
            var updatedKeys = new List<Key>(layer.Keys.Count);
            foreach (var key in layer.Keys)
            {
                var raw = refresh.Keymap[layer.Index, key.Row, key.Col];
                var info = _vm.KeycodeService.Resolve(raw);
                var position = physical.FirstOrDefault(p => p.Row == key.Row && p.Col == key.Col);
                updatedKeys.Add(key with
                {
                    RawKeycode = raw,
                    DisplayLabel = info.Label,
                    SecondaryLabel = info.SecondaryLabel,
                    IsTransparent = info.IsTransparent,
                    IsLayerSwitch = info.IsLayerSwitch,
                    TargetLayer = info.TargetLayer,
                    SwitchType = info.SwitchType,
                    IsUnknown = info.IsUnknown,
                    ShiftedLabel = info.ShiftedLabel,
                    X = position?.X ?? key.X,
                    Y = position?.Y ?? key.Y,
                    Width = position?.Width ?? key.Width,
                    Height = position?.Height ?? key.Height,
                });
            }
            updatedLayers.Add(layer with { Keys = updatedKeys });
        }

        LayerActivationGraph.ResolveInto(updatedLayers);

        _vm.KeyboardConfig = _vm.KeyboardConfig with
        {
            Layers = updatedLayers,
            QmkSettings = refresh.QmkSettings,
            Macros = refresh.Macros ?? _vm.KeyboardConfig.Macros,
            Combos = refresh.Combos,
            TapDances = refresh.TapDances,
        };

        // Update existing layer/key VMs in place. Rebuilding the VM tree
        // (the previous approach) replaced every KeyViewModel instance, and
        // the creative thumb-cluster bindings ended up pointing at stale
        // instances — making the first post-save edit's label appear frozen.
        // Keeping instances stable lets Avalonia bindings keep working and
        // preserves OnClickAction / SetEditMode wiring.
        if (_vm.Layers.Count == 0)
        {
            _vm.BuildLayerViewModels(_vm.Settings.Load());
        }
        else
        {
            foreach (var layerVm in _vm.Layers)
            {
                var updatedLayer = updatedLayers.FirstOrDefault(l => l.Index == layerVm.Index);
                if (updatedLayer is not null)
                    layerVm.UpdateBaselineFromLayer(updatedLayer);
            }
        }
    }

    /// <summary>
    /// Reconciles a mid-batch failure against the reloaded device state.
    /// Each cell the user intended to change falls into one of three buckets:
    /// <list type="bullet">
    /// <item>applied — device now matches intent;</item>
    /// <item>still pending — device still matches baseline, so the write never
    ///   landed and can be retried;</item>
    /// <item>diverged — device matches neither (unexpected external write or
    ///   partial write). Flagged for the user but not auto-retried.</item>
    /// </list>
    /// Rebuilds <see cref="_editSession"/> from the reloaded baseline with
    /// <see cref="SetKeyOp"/>s for each still-pending cell so the user can
    /// press Save again with zero re-typing.
    /// </summary>
    private SavePartial BuildPartialResult(
        SaveFlowExecutor.ExecutionOutcome outcome,
        ushort[,,] intended,
        int totalPending)
    {
        var device = GetDeviceKeymap();
        var baseline = _editSession!.CloneBaseline();
        var pendingSettings = _editSession.PendingSettingsChanges;

        var reconciled = SaveReconciliation.Reconcile(device, baseline, intended);

        // Rebuild the edit session on top of the reloaded device state so
        // the user can hit Save again without re-editing.
        _editSession = new KeymapEditSession(device, BuildQmkSettingsDict(), BuildQmkSettingsWidths(),
            macroBuffer: null,
            combos: _vm.KeyboardConfig?.Combos,
            tapDances: _vm.KeyboardConfig?.TapDances);
        foreach (var w in reconciled.StillPending)
        {
            var old = _editSession.GetCurrent(w.Layer, w.Row, w.Col);
            _editSession.Apply(new SetKeyOp(w.Layer, w.Row, w.Col, old, w.Keycode));
        }

        foreach (var (id, _, newVal) in pendingSettings)
        {
            var current = _editSession.GetCurrentSetting(id) ?? 0;
            if (current != newVal)
                _editSession.Apply(new SetQmkSettingOp(id, current, newVal));
        }

        var message = outcome.Failure?.Message ?? "unknown error";
        DiagnosticLog.Warn("Save",
            $"Partial save: {reconciled.Applied} applied, {reconciled.StillPending.Count} pending, {reconciled.Diverged} diverged — {message}");

        return new SavePartial(
            Applied: reconciled.Applied,
            StillPending: reconciled.StillPending.Count,
            Diverged: reconciled.Diverged,
            FailureMessage: message,
            Remaining: reconciled.StillPending);
    }

    /// <summary>
    /// Pushes a <see cref="SaveResult"/> into the VM: updates the status
    /// message, rebuilds layer pending overlays, and clears the session on
    /// full success. Keeps edit mode on so the user can keep working.
    /// </summary>
    private void ApplySaveResult(SaveResult result, int totalPending)
    {
        switch (result)
        {
            case SaveSuccess s:
                // Rebuild a fresh edit session on top of the now-current device
                // state so the user can immediately make another edit. The session
                // must stay non-null while IsEditMode is true — OnKeyClicked
                // early-returns otherwise, which makes the app appear frozen.
                foreach (var layerVm in _vm.Layers)
                    layerVm.ClearAllPendingOverrides();
                RebuildEditSessionFromCurrentConfig(preserveIntent: false);
                _vm.StatusMessage = Loc.Instance.Format("Status_SaveCompleteFormat", s.Applied);
                DiagnosticLog.Info("Save", $"Save complete: {s.Applied} change(s) written");
                break;

            case SavePartial p:
                // _editSession was already rebuilt by BuildPartialResult.
                _vm.DirtyCount = p.StillPending;
                RebuildPendingOverlaysFromSession();
                _vm.StatusMessage = Loc.Instance.Format(
                    "Status_SavePartialFormat", p.Applied, totalPending, p.StillPending);
                break;

            case SaveCancelled c:
                // Rebuild session on top of whatever is now on the device so
                // the user can retry without re-typing the still-unsaved ones.
                RebuildEditSessionFromCurrentConfig(preserveIntent: true);
                _vm.StatusMessage = Loc.Instance["Status_SaveCancelled"];
                DiagnosticLog.Info("Save", $"Save cancelled after {c.Applied} write(s)");
                break;
        }
    }

    /// <summary>
    /// Rebuilds layer pending overlays from the current <see cref="_editSession"/>
    /// so the visible UI reflects the (possibly reconciled) pending set.
    /// </summary>
    private void RebuildPendingOverlaysFromSession()
    {
        foreach (var layerVm in _vm.Layers)
            layerVm.ClearAllPendingOverrides();
        if (_editSession is null) return;

        foreach (var (layer, row, col, _, newCode) in _editSession.PendingChanges)
        {
            var layerVm = _vm.Layers.FirstOrDefault(l => l.Index == layer);
            if (layerVm is null) continue;
            var info = _vm.KeycodeService.Resolve(newCode);
            layerVm.ApplyPendingOverride(row, col, newCode, info.Label, info.SecondaryLabel);
        }
    }

    /// <summary>
    /// Rebuilds <see cref="_editSession"/> from the current <see cref="MainWindowViewModel.KeyboardConfig"/>.
    /// If <paramref name="preserveIntent"/> is true and the previous session
    /// had pending changes, those (layer, row, col, newCode) tuples are
    /// re-applied on top of the new baseline — used by the cancellation path
    /// so the user doesn't lose their unsaved work when they hit Save again.
    /// </summary>
    private void RebuildEditSessionFromCurrentConfig(bool preserveIntent)
    {
        if (_vm.KeyboardConfig is null) return;

        var previousPending = preserveIntent && _editSession is not null
            ? _editSession.PendingChanges.ToList()
            : new List<(int Layer, int Row, int Col, ushort OldCode, ushort NewCode)>();
        var previousSettingsPending = preserveIntent && _editSession is not null
            ? _editSession.PendingSettingsChanges.ToList()
            : new List<(ushort SettingId, ushort OldValue, ushort NewValue)>();
        var previousMacroBuffer = preserveIntent && _editSession is not null
            ? _editSession.GetCurrentMacroBuffer() : null;

        var layers = _vm.KeyboardConfig.Layers.Count;
        var rows = _vm.KeyboardConfig.MatrixRows;
        var cols = _vm.KeyboardConfig.MatrixCols;
        var baseline = new ushort[layers, rows, cols];
        foreach (var layer in _vm.KeyboardConfig.Layers)
            foreach (var key in layer.Keys)
                baseline[layer.Index, key.Row, key.Col] = key.RawKeycode;

        byte[]? macroBuffer = _vm.KeyboardConfig.Macros is not null
            ? MacroCodec.Encode(_vm.KeyboardConfig.Macros) : null;
        _editSession = new KeymapEditSession(baseline, BuildQmkSettingsDict(), BuildQmkSettingsWidths(), macroBuffer,
            _vm.KeyboardConfig.Combos, _vm.KeyboardConfig.TapDances);
        foreach (var (l, r, c, _, newCode) in previousPending)
        {
            // Only re-apply edits whose target cell actually still diverges
            // from the new baseline — anything the device already has is done.
            var old = _editSession.GetCurrent(l, r, c);
            if (old != newCode)
                _editSession.Apply(new SetKeyOp(l, r, c, old, newCode));
        }
        foreach (var (id, _, newVal) in previousSettingsPending)
        {
            var current = _editSession.GetCurrentSetting(id) ?? 0;
            if (current != newVal)
                _editSession.Apply(new SetQmkSettingOp(id, current, newVal));
        }
        if (previousMacroBuffer is not null)
        {
            var baselineMacro = _editSession.GetBaselineMacroBuffer();
            if (baselineMacro is not null && !baselineMacro.AsSpan().SequenceEqual(previousMacroBuffer))
                _editSession.Apply(new SetMacroBufferOp(baselineMacro, previousMacroBuffer));
        }
        _vm.DirtyCount = ComputeDirtyCount();
        RebuildPendingOverlaysFromSession();
    }
}
