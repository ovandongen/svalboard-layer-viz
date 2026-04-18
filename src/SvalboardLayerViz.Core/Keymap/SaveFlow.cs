using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.History;
using SvalboardLayerViz.Core.Protocol;

namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Orchestrates the save pipeline: pre-snapshot → write → reload → post-snapshot
/// → prune → optional re-lock. Factored out of <c>MainWindowViewModel</c> so the
/// sequencing and retention policy live in one testable place. The VM retains:
/// safety-warning confirmation, macro-buffer overflow guard, status/dirty
/// bookkeeping, partial-result session rebuild, and <c>ApplySaveResult</c>.
/// </summary>
public static class SaveFlow
{
    public sealed record Context(
        IVialProtocolService Protocol,
        ISnapshotService SnapshotService,
        KeymapEditSession EditSession,
        KeyboardId KeyboardId,
        string DeviceName,
        int KeepFirstConnectDays,
        Func<DeviceSnapshot> CurrentDeviceSnapshot,
        Func<byte[]?, bool, CancellationToken, Task> ReloadAsync);

    public sealed record Outcome(
        SaveFlowExecutor.ExecutionOutcome Execution,
        ushort[,,] Intended);

    public static async Task<Outcome> RunAsync(Context ctx, CancellationToken ct)
    {
        var intended = ctx.EditSession.CloneCurrent();
        var writes = ctx.EditSession.BuildDeviceWrites();

        // 1. Pre-save snapshot — captures the device baseline before any writes.
        var baseline = new DeviceSnapshot(
            ctx.EditSession.CloneBaseline(),
            ctx.EditSession.GetBaselineMacroBuffer(),
            ctx.EditSession.CloneBaselineCombos(),
            ctx.EditSession.CloneBaselineTapDances());
        await ctx.SnapshotService.CaptureAsync(
            SnapshotReason.PreSave, ctx.KeyboardId, ctx.DeviceName, baseline, ct: ct);

        DiagnosticLog.Info("Save", $"Save starting: {writes.Count} write(s)");

        // 2. Flush writes via the executor. Sync protocol calls → Task.Run.
        var execution = await Task.Run(
            () => SaveFlowExecutor.Execute(ctx.Protocol, writes, ct), ct);

        // 3. Reload device state — ground truth for reconciliation.
        //    Pass written macro buffer so we skip the slow USB re-read.
        //    Skip macro reload entirely when no macros were changed.
        var macroWrite = writes.OfType<MacroBufferWrite>().FirstOrDefault();
        await ctx.ReloadAsync(macroWrite?.EncodedBuffer, macroWrite is null, ct);

        // 4. Post-save snapshot of whatever is now on the device.
        await ctx.SnapshotService.CaptureAsync(
            SnapshotReason.PostSave, ctx.KeyboardId, ctx.DeviceName,
            ctx.CurrentDeviceSnapshot(), ct: ct);

        // 5. Background prune — do not await.
        _ = ctx.SnapshotService.PruneAsync(
            maxSaveSnapshots: 20,
            keepFirstConnectDays: ctx.KeepFirstConnectDays,
            CancellationToken.None);

        return new Outcome(execution, intended);
    }

    /// <summary>Re-locks the device if it was locked on enter-edit. Swallows failures.</summary>
    public static async Task ReLockAsync(IVialProtocolService protocol)
    {
        try
        {
            await Task.Run(() => protocol.Lock(), CancellationToken.None);
            DiagnosticLog.Info("Save", "Device re-locked after save");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("Save", $"Re-lock failed: {ex.Message}");
        }
    }
}
