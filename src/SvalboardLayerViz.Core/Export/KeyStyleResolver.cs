using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.Core.Export;

/// <summary>
/// Resolved visual style for a single key. Shared by KeyViewModel (UI) and BoardRenderer (export).
/// </summary>
public record KeyStyle(string Background, string Border, string Text, double Opacity);

/// <summary>
/// Computes the visual style (colors, opacity) for a key given its layer colors.
/// Extracted from KeyViewModel so the same logic drives both screen and export rendering.
/// </summary>
public static class KeyStyleResolver
{
    public static KeyStyle Resolve(Key key, int layerIndex, LayerColors colors, LayerColors? targetLayerColors)
    {
        var isTransparent = key.IsTransparent;
        var isLayerSwitch = key.IsLayerSwitch;
        var isActivator = isTransparent && isLayerSwitch && key.TargetLayer == layerIndex;

        // Background
        string bg;
        if (isLayerSwitch && targetLayerColors is not null)
            bg = targetLayerColors.Background;
        else if (isActivator)
            bg = colors.Background;
        else
            bg = isTransparent ? colors.TransparentBackground : colors.Background;

        // Border (export never shows "pressed" state)
        string border;
        if (isLayerSwitch && targetLayerColors is not null)
            border = targetLayerColors.Accent;
        else if (isActivator)
            border = colors.Border;
        else
            border = isTransparent ? colors.TransparentBorder : colors.Border;

        // Text
        string text;
        if (isLayerSwitch && targetLayerColors is not null)
            text = targetLayerColors.TextColor;
        else if (isActivator)
            text = colors.TextColor;
        else
            text = isTransparent ? colors.TransparentTextColor : colors.TextColor;

        // Opacity
        var opacity = (isTransparent && !isActivator) ? 0.55 : 1.0;

        return new KeyStyle(bg, border, text, opacity);
    }
}
