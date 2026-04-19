using System.Text.Json;
using SvalboardLayerViz.Core.Device;
using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.Dynamic;
using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Models;
using SvalboardLayerViz.Core.Protocol;
using SvalboardLayerViz.Core.QmkSettings;
using SvalboardLayerViz.Core.Settings;

namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Orchestrates loading a complete keyboard configuration from a connected device.
/// Combines protocol communication, definition parsing, and keycode resolution.
/// </summary>
public class KeymapLoader
{
    private readonly IVialProtocolService _protocol;
    private readonly KeycodeService _keycodeService;

    public KeymapLoader(IVialProtocolService protocol, KeycodeService keycodeService)
    {
        _protocol = protocol;
        _keycodeService = keycodeService;
    }

    /// <summary>
    /// Loads the full keyboard configuration from a connected device.
    /// </summary>
    public KeyboardConfig Load(DeviceInfo device, UserSettings? settings = null)
    {
        _protocol.Connect(device.HidDevice);

        // 1. Get device identity
        var keyboardId = _protocol.GetKeyboardId();

        // 2. Get layer count
        var layerCount = _protocol.GetLayerCount();

        // 3. Get and parse the keyboard definition (compressed JSON)
        var definitionBytes = _protocol.GetDefinition();
        var definitionJson = XzDecompressor.DecompressToString(definitionBytes);
        var definition = JsonSerializer.Deserialize<LayoutDefinition>(definitionJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (definition is null)
            throw new InvalidOperationException("Failed to parse keyboard definition.");

        var rows = definition.Matrix.Rows;
        var cols = definition.Matrix.Cols;

        // 4. Set custom keycodes so they resolve during key processing
        var customKeycodes = ParseCustomKeycodes(definition);
        _keycodeService.SetCustomKeycodes(customKeycodes);
        _keycodeService.SetCustomKeyLabels(settings?.CustomKeyLabels);

        // 5. Get the full keymap
        var keymap = _protocol.GetKeymapBuffer(layerCount, rows, cols);

        // 6. Get physical layout positions, indexed by (row, col) so the layer
        //    build loop is O(rows*cols) instead of O(rows*cols*positions).
        var physicalLayout = SvalboardLayout.GetKeyPositions()
            .ToDictionary(p => (p.Row, p.Col));

        // 7. Probe Svalboard custom sub-protocol for per-layer colors.
        //    Null result means older firmware; LayerColorService falls back to
        //    algorithmic colors automatically.
        var hasSvalColors = _protocol.GetSvalProtoVersion() is not null;

        // 7a. Discover QMK settings: query which IDs the firmware supports,
        //     then read each value. Width comes from catalog (firmware query
        //     returns QSIDs only, no width). Empty list if unsupported.
        var settingIds = _protocol.GetQmkSettingsList();
        var qmkSettings = new List<QmkSettingValue>(settingIds.Count);
        foreach (var id in settingIds)
        {
            var width = QmkSettingsCatalog.GetWidth(id);
            var value = _protocol.GetQmkSetting(id, width);
            if (value.HasValue)
                qmkSettings.Add(new QmkSettingValue(id, value.Value, width));
        }

        DiagnosticLog.Info("Proto", $"QMK settings loaded: {qmkSettings.Count} entries from {settingIds.Count} discovered");
        foreach (var s in qmkSettings)
        {
            var desc = QmkSettingsCatalog.GetOrFallback(s.SettingId);
            DiagnosticLog.Info("Proto",
                $"  0x{s.SettingId:X4} w={s.Width} val={s.Value} catalog={desc.NameKey} type={desc.Type}");
        }

        // 7b. Macro buffer loading is deferred to after connection completes
        //     (MainWindowViewModel.LoadMacrosAsync) to keep startup fast.

        // 7c. Dynamic entries (combos + tap-dance). Read counts, then each entry.
        //     These tables are small (≤32 entries each) so we fetch eagerly.
        var dynCounts = _protocol.GetDynamicEntryCounts();
        var combos = new List<byte[]>(dynCounts.ComboCount);
        for (var i = 0; i < dynCounts.ComboCount; i++)
            combos.Add(_protocol.GetComboEntry(i));
        var tapDances = new List<byte[]>(dynCounts.TapDanceCount);
        for (var i = 0; i < dynCounts.TapDanceCount; i++)
            tapDances.Add(_protocol.GetTapDanceEntry(i));
        DiagnosticLog.Info("Proto",
            $"Dynamic entries: td={dynCounts.TapDanceCount} combo={dynCounts.ComboCount} " +
            $"keyOverride={dynCounts.KeyOverrideCount} altRepeat={dynCounts.AltRepeatCount}");

        // 8. Build layer models
        var layers = new List<Layer>();
        for (var layerIdx = 0; layerIdx < layerCount; layerIdx++)
        {
            var keys = new List<Key>();
            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < cols; col++)
                {
                    var rawKeycode = keymap[layerIdx, row, col];
                    var info = _keycodeService.Resolve(rawKeycode);

                    // Look up physical position; skip matrix slots with no layout entry
                    if (!physicalLayout.TryGetValue((row, col), out var position))
                        continue;

                    keys.Add(new Key
                    {
                        Row = row,
                        Col = col,
                        RawKeycode = rawKeycode,
                        DisplayLabel = info.Label,
                        SecondaryLabel = info.SecondaryLabel,
                        IsTransparent = info.IsTransparent,
                        IsLayerSwitch = info.IsLayerSwitch,
                        TargetLayer = info.TargetLayer,
                        SwitchType = info.SwitchType,
                        IsUnknown = info.IsUnknown,
                        ShiftedLabel = info.ShiftedLabel,
                        X = position.X,
                        Y = position.Y,
                        Width = position.Width,
                        Height = position.Height,
                    });
                }
            }

            string? layerName = null;
            settings?.LayerNames.TryGetValue(layerIdx, out layerName);

            byte? hue = null, sat = null, val = null;
            if (hasSvalColors)
            {
                var color = _protocol.GetLayerColor(layerIdx);
                if (color is not null)
                {
                    hue = color.Value.H;
                    sat = color.Value.S;
                    val = color.Value.V;
                }
            }

            layers.Add(new Layer
            {
                Index = layerIdx,
                Name = layerName,
                Keys = keys,
                ColorHue = hue,
                ColorSat = sat,
                ColorVal = val,
            });
        }

        // 9. Build the layer activation graph, attach per-layer ActivationPath,
        //    then resolve TRNS via the active stack (matches QMK runtime).
        var activationPaths = LayerActivationGraph.Build(layers);
        for (var i = 0; i < layers.Count; i++)
        {
            if (activationPaths.TryGetValue(i, out var path))
                layers[i] = layers[i] with { ActivationPath = path };
        }
        TransparentKeyResolver.Resolve(layers, activationPaths);

        return new KeyboardConfig
        {
            DeviceName = device.ProductName,
            VendorId = device.VendorId,
            ProductId = device.ProductId,
            KeyboardId = keyboardId,
            MatrixRows = rows,
            MatrixCols = cols,
            Layers = layers,
            CustomKeycodes = customKeycodes,
            QmkSettings = qmkSettings,
            Macros = null,
            DynamicEntryCounts = dynCounts,
            Combos = combos,
            TapDances = tapDances,
        };
    }

    private static List<CustomKeycode> ParseCustomKeycodes(LayoutDefinition definition)
    {
        return definition.CustomKeycodes?.Select(ck => new CustomKeycode
        {
            Name = ck.Name ?? "",
            Title = ck.Title ?? "",
            ShortName = ck.ShortName ?? "",
        }).ToList() ?? [];
    }
}
