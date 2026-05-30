using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Resolves transparent keys (KC_TRNS) to the label that would actually fire
/// at runtime. QMK only consults layers that are active when a key is pressed,
/// so TRNS must walk the layer's active stack — not every lower layer.
///
/// The active stack is derived from a pre-built
/// <see cref="LayerActivationGraph"/>. When no activation path is known
/// (orphan layers like sparse mouse layers), we fall back to [L0, layer].
/// </summary>
public static class TransparentKeyResolver
{
    /// <summary>
    /// For each transparent key on layers 1+, walks the active-layer stack
    /// top-down to find the nearest non-TRNS key and records its label plus
    /// the source layer on <see cref="Key.ResolvedFromLayer"/>.
    ///
    /// <paramref name="activationPaths"/> comes from
    /// <see cref="LayerActivationGraph.Build"/>; pass an empty map to treat
    /// every layer as orphan (fallback stack [0, layer]). Production callers go
    /// through <see cref="LayerActivationGraph.ResolveInto"/>.
    /// </summary>
    public static void Resolve(
        List<Layer> layers,
        IReadOnlyDictionary<int, IReadOnlyList<ActivationHop>> activationPaths)
    {
        // Per-layer (row, col) → Key lookup. Built once so the resolver runs
        // O(total-keys * stack-depth) instead of O(keys²).
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
            var path = activationPaths.TryGetValue(layerIdx, out var p) ? p : Array.Empty<ActivationHop>();
            var stack = LayerActivationGraph.GetActiveStack(layerIdx, path);

            var resolvedKeys = layer.Keys.ToList();

            for (var keyIdx = 0; keyIdx < resolvedKeys.Count; keyIdx++)
            {
                var key = resolvedKeys[keyIdx];
                if (!key.IsTransparent) continue;

                // Walk the active stack from topmost-below-current downward,
                // stopping at the first non-TRNS hit. Skip the current layer
                // itself; it's TRNS at this position by construction.
                for (var stackIdx = stack.Count - 1; stackIdx >= 0; stackIdx--)
                {
                    var below = stack[stackIdx];
                    if (below == layerIdx) continue;
                    if (below < 0 || below >= indexByLayer.Length) continue;
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
                        ResolvedFromLayer = below,
                    };
                    break;
                }
            }

            layers[layerIdx] = layer with { Keys = resolvedKeys };
        }
    }
}
