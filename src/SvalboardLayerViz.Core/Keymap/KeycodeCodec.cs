using static SvalboardLayerViz.Core.Keymap.KeycodeRanges;

namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Structured encode/decode for raw 16-bit QMK keycodes. The two halves are
/// inverse operations and live together so the range mapping stays in one place.
/// </summary>
public static class KeycodeDecoder
{
    public static KeycodeDescriptor Decode(ushort keycode)
    {
        if (keycode == 0x0000)
            return new NoKeycode();

        if (keycode == 0x0001)
            return new TransparentKeycode();

        // Named QMK specials that don't fit any structured range
        // (e.g. QK_REPEAT_KEY 0x7C79, QK_LAYER_LOCK 0x7C7B).
        if (KeycodeCatalog.NamedSpecialKeycodes.ContainsKey(keycode))
            return new SpecialKeycode(keycode);

        if (keycode <= 0x00FF)
            return new BasicKeycode(keycode);

        if (keycode is >= QK_MOD_TAP and <= QK_MOD_TAP_MAX)
        {
            var mods = (ModFlags)((keycode >> 8) & 0x1F);
            var baseKey = (ushort)(keycode & 0x00FF);
            return new ModTapKeycode(mods, baseKey);
        }

        if (keycode is >= QK_LAYER_TAP and <= QK_LAYER_TAP_MAX)
        {
            var layer = (keycode >> 8) & 0x0F;
            var baseKey = (ushort)(keycode & 0x00FF);
            return new LayerTapKeycode(layer, baseKey);
        }

        if (keycode is >= QK_LAYER_MOD and <= QK_LAYER_MOD_MAX)
        {
            var layer = (keycode >> 4) & 0x0F;
            var mods = (ModFlags)(keycode & 0x0F);
            return new LayerModKeycode(layer, mods);
        }

        if (keycode is >= QK_TO and <= QK_TO_MAX)
            return new LayerFunctionKeycode(LayerFunctionKind.TO, keycode - QK_TO);

        if (keycode is >= QK_MO and <= QK_MO_MAX)
            return new LayerFunctionKeycode(LayerFunctionKind.MO, keycode - QK_MO);

        if (keycode is >= QK_DF and <= QK_DF_MAX)
            return new LayerFunctionKeycode(LayerFunctionKind.DF, keycode - QK_DF);

        if (keycode is >= QK_TG and <= QK_TG_MAX)
            return new LayerFunctionKeycode(LayerFunctionKind.TG, keycode - QK_TG);

        if (keycode is >= QK_OSL and <= QK_OSL_MAX)
            return new LayerFunctionKeycode(LayerFunctionKind.OSL, keycode - QK_OSL);

        if (keycode is >= QK_ONE_SHOT_MOD and <= QK_ONE_SHOT_MOD_MAX)
        {
            var mods = (ModFlags)(keycode - QK_ONE_SHOT_MOD);
            return new OneShotModKeycode(mods);
        }

        if (keycode is >= QK_TT and <= QK_TT_MAX)
            return new LayerFunctionKeycode(LayerFunctionKind.TT, keycode - QK_TT);

        if (keycode is >= QK_TAP_DANCE and <= QK_TAP_DANCE_MAX)
            return new TapDanceKeycode(keycode - QK_TAP_DANCE);

        if (keycode is >= QK_MACRO and <= QK_MACRO_MAX)
            return new MacroKeycode(keycode - QK_MACRO);

        if (keycode is >= QK_KB and <= QK_KB_MAX)
            return new CustomKeycodeDescriptor(keycode - QK_KB);

        if ((keycode & 0xFF00) != 0 && (keycode & 0x00FF) != 0 && keycode < QK_MOD_TAP)
        {
            var mods = (ModFlags)((keycode >> 8) & 0x1F);
            var baseKey = (ushort)(keycode & 0x00FF);
            return new ModifiedKeycode(mods, baseKey);
        }

        return new RawKeycode(keycode);
    }
}

public static class KeycodeEncoder
{
    public static ushort Encode(KeycodeDescriptor descriptor) => descriptor switch
    {
        NoKeycode => 0x0000,
        TransparentKeycode => 0x0001,
        BasicKeycode b => b.BaseCode,
        ModifiedKeycode m => (ushort)(((int)m.Mods << 8) | m.BaseCode),
        ModTapKeycode mt => (ushort)(QK_MOD_TAP | ((int)mt.Mods << 8) | mt.BaseCode),
        LayerTapKeycode lt => (ushort)(QK_LAYER_TAP | (lt.Layer << 8) | lt.BaseCode),
        LayerModKeycode lm => (ushort)(QK_LAYER_MOD | (lm.Layer << 4) | (int)lm.Mods),
        LayerFunctionKeycode lf => EncodeLayerFunction(lf),
        OneShotModKeycode osm => (ushort)(QK_ONE_SHOT_MOD | (int)osm.Mods),
        MacroKeycode mk => (ushort)(QK_MACRO + mk.MacroIndex),
        TapDanceKeycode td => (ushort)(QK_TAP_DANCE + td.Index),
        CustomKeycodeDescriptor c => (ushort)(QK_KB + c.Index),
        SpecialKeycode s => s.Code,
        RawKeycode r => r.Value,
        _ => throw new ArgumentException($"Unknown descriptor type: {descriptor.GetType().Name}")
    };

    private static ushort EncodeLayerFunction(LayerFunctionKeycode lf) => lf.Kind switch
    {
        LayerFunctionKind.TO  => (ushort)(QK_TO  + lf.Layer),
        LayerFunctionKind.MO  => (ushort)(QK_MO  + lf.Layer),
        LayerFunctionKind.DF  => (ushort)(QK_DF  + lf.Layer),
        LayerFunctionKind.TG  => (ushort)(QK_TG  + lf.Layer),
        LayerFunctionKind.OSL => (ushort)(QK_OSL + lf.Layer),
        LayerFunctionKind.TT  => (ushort)(QK_TT  + lf.Layer),
        _ => throw new ArgumentException($"Unknown layer function kind: {lf.Kind}")
    };
}
