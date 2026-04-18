using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.Protocol;

namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Runs the per-write loop for the save pipeline. Pure Core, no UI deps.
///
/// Given an ordered list of <see cref="DeviceWrite"/> commands, dispatches
/// each to the appropriate protocol method. Captures success, first failure,
/// and cancellation into an <see cref="ExecutionOutcome"/> — never throws for
/// protocol errors, only for <see cref="OperationCanceledException"/>
/// (the orchestrator is responsible for catching that).
/// </summary>
public static class SaveFlowExecutor
{
    public sealed record ExecutionOutcome(
        IReadOnlyList<DeviceWrite> Applied,
        DeviceWrite? FailedAt,
        Exception? Failure,
        bool Cancelled);

    public static ExecutionOutcome Execute(
        IVialProtocolService protocol,
        IReadOnlyList<DeviceWrite> writes,
        CancellationToken ct)
    {
        var applied = new List<DeviceWrite>(writes.Count);

        for (var i = 0; i < writes.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                DiagnosticLog.Warn("Save",
                    $"Save cancelled after {applied.Count}/{writes.Count} writes");
                return new ExecutionOutcome(applied, FailedAt: null, Failure: null, Cancelled: true);
            }

            var w = writes[i];
            try
            {
                switch (w)
                {
                    case SetKeycodeWrite sk:
                        protocol.SetKeycode(sk.Layer, sk.Row, sk.Col, sk.Keycode);
                        DiagnosticLog.Info("Save",
                            $"wrote L{sk.Layer} [{sk.Row},{sk.Col}] = 0x{sk.Keycode:X4} ({applied.Count + 1}/{writes.Count})");
                        break;
                    case SetQmkSettingWrite qs:
                        protocol.SetQmkSetting(qs.SettingId, qs.Value, qs.Width);
                        DiagnosticLog.Info("Save",
                            $"wrote setting 0x{qs.SettingId:X4} = {qs.Value} ({applied.Count + 1}/{writes.Count})");
                        break;
                    case MacroBufferWrite mw:
                        var buffer = mw.EncodedBuffer;
                        var chunkSize = VialCommands.PayloadSize;
                        for (var off = 0; off < buffer.Length; off += chunkSize)
                        {
                            if (ct.IsCancellationRequested)
                            {
                                DiagnosticLog.Warn("Save",
                                    $"Save cancelled during macro buffer write at offset {off}");
                                return new ExecutionOutcome(applied, FailedAt: null,
                                    Failure: null, Cancelled: true);
                            }
                            var remaining = Math.Min(chunkSize, buffer.Length - off);
                            var chunk = new byte[remaining];
                            Array.Copy(buffer, off, chunk, 0, remaining);
                            protocol.MacroSetBuffer(off, chunk);
                        }
                        DiagnosticLog.Info("Save",
                            $"wrote macro buffer ({buffer.Length} bytes, " +
                            $"{(buffer.Length + chunkSize - 1) / chunkSize} chunks) " +
                            $"({applied.Count + 1}/{writes.Count})");
                        break;
                    case ComboEntryWrite cw:
                        protocol.SetComboEntry(cw.Index, cw.Entry);
                        DiagnosticLog.Info("Save",
                            $"wrote combo[{cw.Index}] ({applied.Count + 1}/{writes.Count})");
                        break;
                    case TapDanceEntryWrite tw:
                        protocol.SetTapDanceEntry(tw.Index, tw.Entry);
                        DiagnosticLog.Info("Save",
                            $"wrote tapdance[{tw.Index}] ({applied.Count + 1}/{writes.Count})");
                        break;
                    default:
                        throw new NotSupportedException($"Unknown write type: {w.GetType().Name}");
                }
                applied.Add(w);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("Save",
                    $"write failed at index {i}: {ex.Message}");
                return new ExecutionOutcome(applied, FailedAt: w, Failure: ex, Cancelled: false);
            }
        }

        return new ExecutionOutcome(applied, FailedAt: null, Failure: null, Cancelled: false);
    }
}
