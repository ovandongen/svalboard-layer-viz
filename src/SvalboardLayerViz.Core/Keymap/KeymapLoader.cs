using System.Text.Json;
using SvalboardLayerViz.Core.Device;
using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Models;
using SvalboardLayerViz.Core.Protocol;
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

        // 6. Get physical layout positions
        var physicalLayout = SvalboardLayout.GetKeyPositions();

        // 7. Probe Svalboard custom sub-protocol for per-layer colors.
        //    Null result means older firmware; LayerColorService falls back to
        //    algorithmic colors automatically.
        var hasSvalColors = _protocol.GetSvalProtoVersion() is not null;

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
                    var position = physicalLayout.FirstOrDefault(p => p.Row == row && p.Col == col);
                    if (position is null) continue;

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

        // 9. Resolve transparent keys (KC_TRNS falls through to layer below)
        TransparentKeyResolver.Resolve(layers);

        return new KeyboardConfig
        {
            DeviceName = device.ProductName,
            KeyboardId = keyboardId,
            MatrixRows = rows,
            MatrixCols = cols,
            Layers = layers,
            CustomKeycodes = customKeycodes,
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
