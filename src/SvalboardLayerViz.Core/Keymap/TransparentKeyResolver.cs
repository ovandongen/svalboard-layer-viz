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
                        resolvedKeys[keyIdx] = key with
                        {
                            EffectiveLabel = lowerKey.DisplayLabel,
                            IsLayerSwitch = lowerKey.IsLayerSwitch,
                            TargetLayer = lowerKey.TargetLayer,
                            SecondaryLabel = lowerKey.SecondaryLabel,
                        };
                        break;
                    }
                }
            }

            layers[layerIdx] = layer with { Keys = resolvedKeys };
        }
    }
}
