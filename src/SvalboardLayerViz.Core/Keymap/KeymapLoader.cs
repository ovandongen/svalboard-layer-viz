using System.Text.Json;
using SvalboardLayerViz.Core.Device;
using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Models;
using SvalboardLayerViz.Core.Protocol;

namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Orchestrates loading a complete keyboard configuration from a connected device.
/// Combines protocol communication, definition parsing, and keycode resolution.
/// </summary>
public class KeymapLoader
{
    private readonly VialProtocolService _protocol;
    private readonly KeycodeService _keycodeService;

    public KeymapLoader(VialProtocolService protocol, KeycodeService keycodeService)
    {
        _protocol = protocol;
        _keycodeService = keycodeService;
    }

    /// <summary>
    /// Loads the full keyboard configuration from a connected device.
    /// </summary>
    public KeyboardConfig Load(DeviceInfo device)
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

        // 4. Get the full keymap
        var keymap = _protocol.GetKeymapBuffer(layerCount, rows, cols);

        // 5. Get physical layout positions
        var physicalLayout = SvalboardLayout.GetKeyPositions();

        // 6. Build layer models
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
                        X = position.X,
                        Y = position.Y,
                        Width = position.Width,
                        Height = position.Height,
                    });
                }
            }

            layers.Add(new Layer
            {
                Index = layerIdx,
                Keys = keys,
                // TODO: Parse layer_colors from Svalboard-specific config
            });
        }

        // 7. Resolve transparent keys (KC_TRNS falls through to layer below)
        ResolveTransparentKeys(layers);

        return new KeyboardConfig
        {
            DeviceName = device.ProductName,
            KeyboardId = keyboardId,
            MatrixRows = rows,
            MatrixCols = cols,
            Layers = layers,
            CustomKeycodes = ParseCustomKeycodes(definition),
        };
    }

    /// <summary>
    /// For each transparent key, walk down the layer stack to find the effective key
    /// and record its label. Mutates the layer's Keys list in place using record 'with'.
    /// </summary>
    private static void ResolveTransparentKeys(List<Layer> layers)
    {
        for (var layerIdx = 1; layerIdx < layers.Count; layerIdx++)
        {
            var layer = layers[layerIdx];
            var resolvedKeys = layer.Keys.ToList();

            for (var keyIdx = 0; keyIdx < resolvedKeys.Count; keyIdx++)
            {
                var key = resolvedKeys[keyIdx];
                if (!key.IsTransparent) continue;

                for (var below = layerIdx - 1; below >= 0; below--)
                {
                    var lowerKey = layers[below].Keys
                        .FirstOrDefault(k => k.Row == key.Row && k.Col == key.Col);

                    if (lowerKey is not null && !lowerKey.IsTransparent)
                    {
                        resolvedKeys[keyIdx] = key with { EffectiveLabel = lowerKey.DisplayLabel };
                        break;
                    }
                }
            }

            layers[layerIdx] = layer with { Keys = resolvedKeys };
        }
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
