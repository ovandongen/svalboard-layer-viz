using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Matches a polled rgblight hue+sat against the stored per-layer colors to
/// decide which layer is currently active. Handles collisions (multiple
/// layers sharing the same color) by keeping the current selection if it's
/// among the matches, otherwise picking the lowest-index match.
/// </summary>
public static class LedColorLayerResolver
{
    /// <summary>Hue tolerance (±) for considering two colors a match, on a 0-255 scale.</summary>
    public const int HueTolerance = 4;

    /// <summary>Saturation tolerance (±) for considering two colors a match.</summary>
    public const int SatTolerance = 16;

    /// <summary>
    /// Finds the best-matching layer for a given rgblight hue+sat.
    /// </summary>
    /// <param name="layers">All layers; only those with ColorHue/ColorSat populated are considered.</param>
    /// <param name="ledHue">Current rgblight hue reported by the device.</param>
    /// <param name="ledSat">Current rgblight saturation reported by the device.</param>
    /// <param name="currentLayer">
    /// The currently selected layer index. If it's among the matching layers,
    /// we keep it — this gives stable behavior when multiple layers share a color.
    /// </param>
    /// <returns>
    /// The layer index to switch to, or null if no layer matches within tolerance
    /// (caller should leave the current selection alone).
    /// </returns>
    public static int? Resolve(IReadOnlyList<Layer> layers, byte ledHue, byte ledSat, int currentLayer)
    {
        // Gather all layers (by Layer.Index, not list position) whose stored
        // color matches within tolerance.
        var matches = new List<int>();
        foreach (var layer in layers)
        {
            if (layer.ColorHue is null || layer.ColorSat is null) continue;

            var hueDist = CircularDistance(layer.ColorHue.Value, ledHue);
            var satDist = Math.Abs(layer.ColorSat.Value - ledSat);
            if (hueDist <= HueTolerance && satDist <= SatTolerance)
                matches.Add(layer.Index);
        }

        if (matches.Count == 0)
            return null;

        // If the current layer is among the matches, keep it — stable on collisions.
        if (matches.Contains(currentLayer))
            return currentLayer;

        // Otherwise pick the lowest Layer.Index match (deterministic).
        matches.Sort();
        return matches[0];
    }

    /// <summary>
    /// Circular distance on a 0-255 hue wheel: min(|a-b|, 256-|a-b|).
    /// </summary>
    public static int CircularDistance(byte a, byte b)
    {
        var diff = Math.Abs(a - b);
        return Math.Min(diff, 256 - diff);
    }
}
