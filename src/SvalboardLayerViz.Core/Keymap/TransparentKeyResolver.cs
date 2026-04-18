using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Resolves transparent keys (KC_TRNS) by walking down the layer stack
/// to find the effective key label from a lower layer.
/// </summary>
public static class TransparentKeyResolver
{
    /// <summary>
    /// For each transparent key on layers 1+, walk down the layer stack to find
    /// the effective key and record its label. Mutates the list in place using record 'with'.
    /// </summary>
    public static void Resolve(List<Layer> layers)
    {
        // Build a (row, col) lookup per layer once — the naive version did a
        // linear Keys.FirstOrDefault per TRNS per lower layer, which is O(keys²)
        // in the worst case.
        var indexByLayer = new Dictionary<(int, int), Key>[layers.Count];
        for (var i = 0; i < layers.Count; i++)
        {
            var map = new Dictionary<(int, int), Key>(layers[i].Keys.Count);
            foreach (var key in layers[i].Keys)
                map[(key.Row, key.Col)] = key;
            indexByLayer[i] = map;
        }

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
                    if (!indexByLayer[below].TryGetValue((key.Row, key.Col), out var lowerKey))
                        continue;
                    if (lowerKey.IsTransparent) continue;

                    resolvedKeys[keyIdx] = key with
                    {
                        EffectiveLabel = lowerKey.DisplayLabel,
                        IsLayerSwitch = lowerKey.IsLayerSwitch,
                        TargetLayer = lowerKey.TargetLayer,
                        SwitchType = lowerKey.SwitchType,
                        SecondaryLabel = lowerKey.SecondaryLabel,
                    };
                    break;
                }
            }

            layers[layerIdx] = layer with { Keys = resolvedKeys };
        }
    }
}
