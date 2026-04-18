using SvalboardLayerViz.Core.Keymap;

namespace SvalboardLayerViz.Core.Macros;

/// <summary>
/// Generates human-readable preview strings from macro actions.
/// Shared between the macro editor slot list and board key labels.
/// </summary>
public static class MacroPreviewHelper
{
    /// <summary>
    /// Builds a preview string from a list of macro actions, truncated to <paramref name="maxLength"/>.
    /// Returns null for empty action lists.
    /// </summary>
    public static string? GetPreview(IReadOnlyList<MacroAction> actions, int maxLength = 40)
    {
        if (actions.Count == 0)
            return null;

        var parts = new List<string>();
        var totalLen = 0;

        foreach (var action in actions)
        {
            var part = action switch
            {
                MacroTextAction t => t.Text,
                MacroTapAction t => ResolveKeyName(t.Keycode),
                MacroModTapAction mt => $"{FormatMods(mt.Mods)}+{ResolveKeyName(mt.Keycode)}",
                MacroDownAction d => $"↓{ResolveKeyName(d.Keycode)}",
                MacroUpAction u => $"↑{ResolveKeyName(u.Keycode)}",
                MacroDelayAction d => $"{d.DelayMs}ms",
                _ => "?"
            };

            if (totalLen + part.Length > maxLength && parts.Count > 0)
            {
                parts.Add("…");
                break;
            }

            parts.Add(part);
            totalLen += part.Length + 1; // +1 for separator
        }

        return string.Join(" ", parts);
    }

    private static string ResolveKeyName(byte keycode) =>
        KeycodeCatalog.BasicKeycodes.GetValueOrDefault((ushort)keycode, $"0x{keycode:X2}");

    /// <summary>Formats a QMK modifier bitmask as a readable string like "Shift" or "Ctrl+Alt".</summary>
    public static string FormatMods(byte mods)
    {
        var names = new List<string>(4);
        if ((mods & 0x01) != 0) names.Add("Ctrl");
        if ((mods & 0x02) != 0) names.Add("Shift");
        if ((mods & 0x04) != 0) names.Add("Alt");
        if ((mods & 0x08) != 0) names.Add("Gui");
        if ((mods & 0x10) != 0) names.Add("RCtrl");
        if ((mods & 0x20) != 0) names.Add("RShift");
        if ((mods & 0x40) != 0) names.Add("RAlt");
        if ((mods & 0x80) != 0) names.Add("RGui");
        return names.Count > 0 ? string.Join("+", names) : "Mod";
    }
}
