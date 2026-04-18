namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// The kind of safety warning detected.
/// </summary>
public enum SafetyWarningKind
{
    /// <summary>Base layer has no usable keys (all KC_NO / KC_TRNS).</summary>
    BaseLayerUnusable,

    /// <summary>A layer is unreachable — no key on any other layer switches to it.</summary>
    UnreachableLayer,

    /// <summary>Base layer is entirely transparent (KC_TRNS) — nothing to fall through to.</summary>
    AllTransparentBaseLayer,
}

/// <summary>
/// A single safety warning with context.
/// </summary>
public record SafetyWarning(SafetyWarningKind Kind, int? Layer = null, string? Detail = null);

/// <summary>
/// Pure-function static analysis of a keymap to detect dangerous configurations
/// before saving to the device. No I/O — operates entirely on the keycode arrays.
/// </summary>
public static class PreSaveSafetyCheck
{
    /// <summary>
    /// Analyzes a keymap and returns any safety warnings.
    /// An empty list means the keymap is considered safe.
    /// </summary>
    /// <param name="keymap">The keymap as [layers, rows, cols] of raw keycodes.</param>
    public static IReadOnlyList<SafetyWarning> Check(ushort[,,] keymap)
    {
        var warnings = new List<SafetyWarning>();
        var layers = keymap.GetLength(0);
        var rows = keymap.GetLength(1);
        var cols = keymap.GetLength(2);

        // Track which layers are referenced by layer-switch keys
        var reachableLayers = new HashSet<int> { 0 }; // base layer is always reachable

        for (var l = 0; l < layers; l++)
        {
            var hasUsableKey = false;
            var allTransparent = true;

            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < cols; c++)
                {
                    var code = keymap[l, r, c];
                    var descriptor = KeycodeDecoder.Decode(code);

                    // Check if this key is usable (not KC_NO and not KC_TRNS)
                    if (descriptor is not NoKeycode and not TransparentKeycode)
                    {
                        hasUsableKey = true;
                        allTransparent = false;
                    }
                    else if (descriptor is TransparentKeycode)
                    {
                        // Transparent counts as "has something" for usability
                        // (it falls through), but track all-transparent separately
                        if (l > 0) hasUsableKey = true;
                    }

                    // Track layer switches for reachability analysis
                    int? targetLayer = GetTargetLayer(descriptor);
                    if (targetLayer.HasValue)
                        reachableLayers.Add(targetLayer.Value);
                }
            }

            // Base layer checks
            if (l == 0)
            {
                if (!hasUsableKey)
                    warnings.Add(new SafetyWarning(SafetyWarningKind.BaseLayerUnusable, 0,
                        "Base layer has no usable keys"));

                if (allTransparent && !hasUsableKey)
                    warnings.Add(new SafetyWarning(SafetyWarningKind.AllTransparentBaseLayer, 0,
                        "Base layer is entirely transparent — nothing to fall through to"));
            }
        }

        // Unreachable layer check
        for (var l = 1; l < layers; l++)
        {
            if (!reachableLayers.Contains(l))
            {
                // Only warn if the layer has any non-empty keys
                var hasContent = false;
                for (var r = 0; r < rows && !hasContent; r++)
                    for (var c = 0; c < cols && !hasContent; c++)
                        if (keymap[l, r, c] != 0x0000)
                            hasContent = true;

                // Skip layers that look like auto-mouse layers. QMK's
                // auto-mouse feature activates a dedicated layer on trackball
                // movement without any layer-switch keycode referencing it,
                // so such layers are "unreachable" by static analysis but
                // perfectly fine in practice. Detect heuristically: a layer
                // is treated as the auto-mouse target if it contains several
                // mouse basic keycodes (0xCD..0xDC).
                if (hasContent && !LooksLikeMouseLayer(keymap, l, rows, cols))
                    warnings.Add(new SafetyWarning(SafetyWarningKind.UnreachableLayer, l,
                        $"Layer {l} is not referenced by any layer-switch key"));
            }
        }

        return warnings;
    }

    private const int MouseKeyThreshold = 2;

    private static bool LooksLikeMouseLayer(ushort[,,] keymap, int layer, int rows, int cols)
    {
        var mouseKeyCount = 0;
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
                if (keymap[layer, r, c] is >= 0x00CD and <= 0x00DC)
                    mouseKeyCount++;
        return mouseKeyCount >= MouseKeyThreshold;
    }

    /// <summary>
    /// Returns the target layer for a layer-switching descriptor, or null if not a layer switch.
    /// </summary>
    private static int? GetTargetLayer(KeycodeDescriptor descriptor) => descriptor switch
    {
        LayerTapKeycode lt => lt.Layer,
        LayerModKeycode lm => lm.Layer,
        LayerFunctionKeycode lf => lf.Layer,
        _ => null,
    };
}
