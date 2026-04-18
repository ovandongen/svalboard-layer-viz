using SvalboardLayerViz.Core.Macros;
using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.Core.History;

/// <summary>
/// Flat device-state tuple sent into <see cref="ISnapshotService.CaptureAsync"/>.
/// Built from a <see cref="KeyboardConfig"/> (post-save / connect) or directly
/// from a <see cref="KeymapEditSession"/> baseline (pre-save).
/// </summary>
public sealed record DeviceSnapshot(
    ushort[,,] Keymap,
    byte[]? MacroBuffer,
    IReadOnlyList<byte[]>? Combos,
    IReadOnlyList<byte[]>? TapDances)
{
    public static DeviceSnapshot From(KeyboardConfig config)
    {
        var km = new ushort[config.Layers.Count, config.MatrixRows, config.MatrixCols];
        foreach (var layer in config.Layers)
            foreach (var key in layer.Keys)
                km[layer.Index, key.Row, key.Col] = key.RawKeycode;
        return new DeviceSnapshot(
            km,
            config.Macros is not null ? MacroCodec.Encode(config.Macros) : null,
            config.Combos.Count > 0 ? config.Combos : null,
            config.TapDances.Count > 0 ? config.TapDances : null);
    }
}

public static class SnapshotServiceExtensions
{
    public static Task<KeymapSnapshot> CaptureAsync(
        this ISnapshotService service,
        SnapshotReason reason,
        KeyboardId keyboardId,
        string deviceName,
        DeviceSnapshot snapshot,
        string? userLabel = null,
        CancellationToken ct = default) =>
        service.CaptureAsync(
            reason, keyboardId, deviceName,
            snapshot.Keymap,
            macroBuffer: snapshot.MacroBuffer,
            combos: snapshot.Combos,
            tapDances: snapshot.TapDances,
            userLabel: userLabel,
            ct: ct);
}
