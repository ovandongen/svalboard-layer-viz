using SvalboardLayerViz.Core.Dynamic;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Macros;

namespace SvalboardLayerViz.Core.Models;

/// <summary>
/// Complete keyboard configuration loaded from the device.
/// This is the top-level model that the UI binds to.
/// </summary>
public record KeyboardConfig
{
    /// <summary>Device name as reported by the HID descriptor.</summary>
    public required string DeviceName { get; init; }

    /// <summary>USB vendor ID.</summary>
    public required int VendorId { get; init; }

    /// <summary>USB product ID.</summary>
    public required int ProductId { get; init; }

    /// <summary>Vial keyboard ID.</summary>
    public required ulong KeyboardId { get; init; }

    /// <summary>Number of matrix rows (10 for Svalboard).</summary>
    public required int MatrixRows { get; init; }

    /// <summary>Number of matrix columns (6 for Svalboard).</summary>
    public required int MatrixCols { get; init; }

    /// <summary>All layers with their key assignments.</summary>
    public required IReadOnlyList<Layer> Layers { get; init; }

    /// <summary>Custom keycodes defined on this keyboard.</summary>
    public IReadOnlyList<CustomKeycode> CustomKeycodes { get; init; } = [];

    /// <summary>
    /// QMK settings discovered on this device via Vial QMK Settings protocol.
    /// Empty if the firmware doesn't support QMK settings.
    /// </summary>
    public IReadOnlyList<QmkSettingValue> QmkSettings { get; init; } = [];

    /// <summary>
    /// Decoded macro buffer from the device. Null if the firmware reports zero
    /// macro slots or zero buffer capacity.
    /// </summary>
    public MacroBuffer? Macros { get; init; }

    /// <summary>
    /// Counts of dynamic-entry tables (combos, tap-dance, key overrides, alt repeat)
    /// reported by the firmware. Zero counts mean the table is unavailable.
    /// </summary>
    public DynamicEntryCounts DynamicEntryCounts { get; init; } = new(0, 0, 0, 0);

    /// <summary>Raw 10-byte combo entries read from the device, indexed by slot.</summary>
    public IReadOnlyList<byte[]> Combos { get; init; } = [];

    /// <summary>Raw 10-byte tap-dance entries read from the device, indexed by slot.</summary>
    public IReadOnlyList<byte[]> TapDances { get; init; } = [];

    /// <summary>
    /// Returns a config with every key's display label re-resolved via
    /// <paramref name="keycodeService"/> (which must already hold the desired
    /// custom-label state) and layer names overlaid from
    /// <paramref name="layerNames"/>. Keys whose resolution matches the current
    /// baseline return unchanged — the caller sees a new top-level record only
    /// when something actually differs.
    /// </summary>
    public KeyboardConfig WithResolvedLabels(
        KeycodeService keycodeService,
        IReadOnlyDictionary<int, string>? layerNames)
    {
        var changed = false;
        var updatedLayers = new List<Layer>(Layers.Count);
        foreach (var layer in Layers)
        {
            // A null map means "leave names alone"; a non-null map with no
            // entry for this index means "clear the user name".
            string? newName = layer.Name;
            if (layerNames is not null)
                newName = layerNames.TryGetValue(layer.Index, out var n) ? n : null;

            var keysChanged = false;
            var newKeys = new List<Key>(layer.Keys.Count);
            foreach (var key in layer.Keys)
            {
                var info = keycodeService.Resolve(key.RawKeycode);
                if (info.Label == key.DisplayLabel
                    && info.SecondaryLabel == key.SecondaryLabel
                    && info.IsUnknown == key.IsUnknown)
                {
                    newKeys.Add(key);
                    continue;
                }
                keysChanged = true;
                newKeys.Add(key with
                {
                    DisplayLabel = info.Label,
                    SecondaryLabel = info.SecondaryLabel,
                    IsUnknown = info.IsUnknown,
                });
            }

            if (!keysChanged && newName == layer.Name)
            {
                updatedLayers.Add(layer);
                continue;
            }
            changed = true;
            updatedLayers.Add(layer with
            {
                Name = newName,
                Keys = keysChanged ? newKeys : layer.Keys,
            });
        }

        return changed ? this with { Layers = updatedLayers } : this;
    }
}

/// <summary>
/// A QMK setting value discovered on the device.
/// </summary>
/// <param name="Width">Byte width reported by firmware query (1 = u8/bool, 2 = u16). Defaults to 2.</param>
public record QmkSettingValue(ushort SettingId, ushort Value, byte Width = 2);

/// <summary>
/// A custom keycode defined in the keyboard's Vial configuration.
/// </summary>
public record CustomKeycode
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    public required string ShortName { get; init; }
}
