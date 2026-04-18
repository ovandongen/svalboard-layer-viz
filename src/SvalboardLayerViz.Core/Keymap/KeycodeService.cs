using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Translates raw 16-bit keycodes to human-readable display labels.
/// Uses <see cref="KeycodeCatalog"/> as the single source of truth for
/// basic keycode labels and shifted symbols, and <see cref="KeycodeDecoder"/>
/// for structured decoding of composite keycodes.
///
/// Reference: keybard-ng/src/services/key.service.ts, keybard-ng/src/constants/keygen.ts
/// </summary>
public class KeycodeService
{

    private IReadOnlyList<CustomKeycode>? _customKeycodes;
    private Dictionary<string, string>? _customKeyLabels;
    private IReadOnlyList<string>? _macroPreviews;

    /// <summary>
    /// Builds a fully configured service. Prefer this over the parameterless
    /// constructor + setters — it closes the silent-fallback window where
    /// Resolve() runs before SetCustomKeycodes() / SetCustomKeyLabels() /
    /// SetMacroPreviews() and custom keycodes resolve to raw hex.
    /// Setters remain available for live updates (macros reloaded, settings
    /// changed) after construction.
    /// </summary>
    public KeycodeService(
        IReadOnlyList<CustomKeycode>? customKeycodes = null,
        Dictionary<string, string>? customKeyLabels = null,
        IReadOnlyList<string>? macroPreviews = null)
    {
        _customKeycodes = customKeycodes;
        _customKeyLabels = customKeyLabels;
        _macroPreviews = macroPreviews;
    }

    /// <summary>
    /// Sets preview strings for macro slots (e.g. "Hello…", "Ctrl+C Ctrl+V").
    /// Index = macro slot index, value = preview text. Used for secondary labels.
    /// </summary>
    public void SetMacroPreviews(IReadOnlyList<string>? previews) => _macroPreviews = previews;

    /// <summary>
    /// Sets custom keycodes from the device definition for resolution.
    /// Call before resolving keys.
    /// </summary>
    public void SetCustomKeycodes(IReadOnlyList<CustomKeycode> keycodes)
    {
        _customKeycodes = keycodes;
    }

    /// <summary>
    /// Sets user-defined labels for unknown keycodes from settings.
    /// Key = hex string (e.g. "0x5300"), Value = display label.
    /// </summary>
    public void SetCustomKeyLabels(Dictionary<string, string>? labels)
    {
        _customKeyLabels = labels;
    }

    /// <summary>
    /// Converts a raw keycode to a display label.
    /// Delegates structural decoding to <see cref="KeycodeDecoder"/> and
    /// label lookup to <see cref="KeycodeCatalog"/>.
    /// </summary>
    public KeycodeInfo Resolve(ushort keycode)
    {
        // User-defined custom label takes priority over all other resolution
        if (_customKeyLabels is not null)
        {
            var hexKey = $"0x{keycode:X4}";
            if (_customKeyLabels.TryGetValue(hexKey, out var userLabel))
                return new KeycodeInfo(userLabel);
        }

        var descriptor = KeycodeDecoder.Decode(keycode);

        return descriptor switch
        {
            NoKeycode => new KeycodeInfo("", IsEmpty: true),
            TransparentKeycode => new KeycodeInfo("___", IsTransparent: true),

            BasicKeycode b => new KeycodeInfo(
                KeycodeCatalog.BasicKeycodes.GetValueOrDefault(b.BaseCode, $"0x{keycode:X4}"),
                ShiftedLabel: KeycodeCatalog.ShiftedSymbols.GetValueOrDefault(b.BaseCode)),

            ModTapKeycode mt => new KeycodeInfo(
                LookupBasic(mt.BaseCode),
                SecondaryLabel: $"MT({FormatModFlags(mt.Mods)})"),

            LayerTapKeycode lt when lt.BaseCode == 0x00 =>
                new KeycodeInfo($"LT({lt.Layer})", IsLayerSwitch: true, TargetLayer: lt.Layer, SwitchType: LayerSwitchType.Momentary),

            LayerTapKeycode lt => new KeycodeInfo(
                LookupBasic(lt.BaseCode),
                SecondaryLabel: $"LT({lt.Layer})", IsLayerSwitch: true, TargetLayer: lt.Layer, SwitchType: LayerSwitchType.Momentary),

            LayerModKeycode lm => new KeycodeInfo(
                $"LM({lm.Layer})", SecondaryLabel: FormatModFlags(lm.Mods),
                IsLayerSwitch: true, TargetLayer: lm.Layer, SwitchType: LayerSwitchType.Momentary),

            LayerFunctionKeycode lf => ResolveLayerFunction(lf),

            OneShotModKeycode osm => new KeycodeInfo($"OSM({FormatModFlags(osm.Mods)})"),

            MacroKeycode mk => new KeycodeInfo($"M{mk.MacroIndex}",
                SecondaryLabel: GetMacroPreview(mk.MacroIndex)),

            TapDanceKeycode td => new KeycodeInfo($"TD{td.Index}",
                SecondaryLabel: "TapDance"),

            CustomKeycodeDescriptor c => ResolveCustom(c),

            SpecialKeycode s => new KeycodeInfo(
                KeycodeCatalog.NamedSpecialKeycodes.GetValueOrDefault(s.Code, $"0x{s.Code:X4}")),

            ModifiedKeycode m => ResolveModified(m),

            RawKeycode => new KeycodeInfo($"0x{keycode:X4}", IsUnknown: true),

            _ => new KeycodeInfo($"0x{keycode:X4}", IsUnknown: true),
        };
    }

    private static KeycodeInfo ResolveLayerFunction(LayerFunctionKeycode lf)
    {
        var (label, switchType) = lf.Kind switch
        {
            LayerFunctionKind.TO  => ($"TO({lf.Layer})",  LayerSwitchType.Activate),
            LayerFunctionKind.MO  => ($"MO({lf.Layer})",  LayerSwitchType.Momentary),
            LayerFunctionKind.DF  => ($"DF({lf.Layer})",  LayerSwitchType.Activate),
            LayerFunctionKind.TG  => ($"TG({lf.Layer})",  LayerSwitchType.Toggle),
            LayerFunctionKind.OSL => ($"OSL({lf.Layer})", LayerSwitchType.OneShot),
            LayerFunctionKind.TT  => ($"TT({lf.Layer})",  LayerSwitchType.Momentary),
            _ => ($"0x{KeycodeEncoder.Encode(lf):X4}", LayerSwitchType.None),
        };
        return new KeycodeInfo(label, IsLayerSwitch: true, TargetLayer: lf.Layer, SwitchType: switchType);
    }

    private KeycodeInfo ResolveCustom(CustomKeycodeDescriptor c)
    {
        if (_customKeycodes is not null && c.Index < _customKeycodes.Count)
        {
            var custom = _customKeycodes[c.Index];
            var label = !string.IsNullOrEmpty(custom.ShortName) ? custom.ShortName : custom.Name;
            return new KeycodeInfo(label);
        }
        return new KeycodeInfo($"KB{c.Index}");
    }

    private static KeycodeInfo ResolveModified(ModifiedKeycode m)
    {
        // Shift-only + key with a known symbol → show the symbol directly
        if (m.Mods == ModFlags.Shift && KeycodeCatalog.ShiftedSymbols.TryGetValue(m.BaseCode, out var symbol))
            return new KeycodeInfo(symbol);

        var baseLabel = LookupBasic(m.BaseCode);
        var modLabel = FormatModFlags(m.Mods);
        return new KeycodeInfo(baseLabel, SecondaryLabel: modLabel);
    }

    private static string LookupBasic(ushort code) =>
        KeycodeCatalog.BasicKeycodes.GetValueOrDefault(code, $"0x{code:X2}");

    private static string FormatModFlags(ModFlags mods)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModFlags.Ctrl))  parts.Add("Ctrl");
        if (mods.HasFlag(ModFlags.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModFlags.Alt))   parts.Add("Alt");
        if (mods.HasFlag(ModFlags.Gui))   parts.Add("GUI");
        return string.Join("+", parts);
    }

    private string? GetMacroPreview(int index)
    {
        if (_macroPreviews is null || index < 0 || index >= _macroPreviews.Count)
            return null;
        var preview = _macroPreviews[index];
        return string.IsNullOrEmpty(preview) ? null : preview;
    }
}

/// <summary>
/// Resolved information about a keycode, ready for display.
/// </summary>
public record KeycodeInfo(
    string Label,
    string? SecondaryLabel = null,
    bool IsTransparent = false,
    bool IsEmpty = false,
    bool IsLayerSwitch = false,
    int? TargetLayer = null,
    LayerSwitchType SwitchType = LayerSwitchType.None,
    bool IsUnknown = false,
    string? ShiftedLabel = null
);
