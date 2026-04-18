using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.Macros;
using SvalboardLayerViz.Core.Models;
using SvalboardLayerViz.Core.Protocol;
using SvalboardLayerViz.Core.QmkSettings;

namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Re-fetches dynamic device state (keymap, QMK settings, macros, combos,
/// tap-dances) after a save. The dimensions + layer count come from the
/// already-loaded <see cref="KeyboardConfig"/>; a full reload (definition,
/// LED probe) stays the responsibility of <see cref="KeymapLoader"/>.
///
/// All protocol calls are synchronous; callers are expected to wrap this in
/// <c>Task.Run</c> to keep the UI thread free. Macro reload honours an
/// optional <c>knownMacroBuffer</c> to skip the slow chunked USB re-read
/// right after a save that wrote the buffer.
/// </summary>
public static class DeviceRefreshService
{
    public sealed record Result(
        ushort[,,] Keymap,
        IReadOnlyList<QmkSettingValue> QmkSettings,
        MacroBuffer? Macros,
        IReadOnlyList<byte[]> Combos,
        IReadOnlyList<byte[]> TapDances);

    public static Result Fetch(
        IVialProtocolService protocol,
        KeyboardConfig currentConfig,
        byte[]? knownMacroBuffer = null,
        bool skipMacroReload = false)
    {
        var rows = currentConfig.MatrixRows;
        var cols = currentConfig.MatrixCols;
        var layerCount = currentConfig.Layers.Count;

        var keymap = protocol.GetKeymapBuffer(layerCount, rows, cols);

        var settingIds = protocol.GetQmkSettingsList();
        var qmkSettings = settingIds
            .Select(id =>
            {
                var w = QmkSettingsCatalog.GetWidth(id);
                return new QmkSettingValue(id, protocol.GetQmkSetting(id, w) ?? 0, w);
            })
            .ToList();

        var macros = FetchMacros(protocol, currentConfig.Macros, knownMacroBuffer, skipMacroReload);

        var counts = protocol.GetDynamicEntryCounts();
        var combos = new List<byte[]>(counts.ComboCount);
        for (var i = 0; i < counts.ComboCount; i++)
            combos.Add(protocol.GetComboEntry(i));
        var tapDances = new List<byte[]>(counts.TapDanceCount);
        for (var i = 0; i < counts.TapDanceCount; i++)
            tapDances.Add(protocol.GetTapDanceEntry(i));

        return new Result(keymap, qmkSettings, macros, combos, tapDances);
    }

    private static MacroBuffer? FetchMacros(
        IVialProtocolService protocol,
        MacroBuffer? existing,
        byte[]? knownMacroBuffer,
        bool skipMacroReload)
    {
        if (skipMacroReload || existing is null) return null;

        if (knownMacroBuffer is not null)
            return MacroCodec.Decode(knownMacroBuffer, existing.Macros.Count, existing.BufferCapacity);

        try
        {
            var rawBuffer = protocol.GetMacroBuffer(existing.BufferCapacity);
            return MacroCodec.Decode(rawBuffer, existing.Macros.Count, existing.BufferCapacity);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("Save", $"Macro reload failed: {ex.Message}");
            return existing;
        }
    }
}
