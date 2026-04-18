namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// QMK 16-bit keycode range bases and inclusive maxes. Single source of truth —
/// <see cref="KeycodeEncoder"/> and <see cref="KeycodeDecoder"/> both reference
/// these so encode/decode stay in lockstep.
/// </summary>
internal static class KeycodeRanges
{
    public const ushort QK_MOD_TAP          = 0x2000;
    public const ushort QK_MOD_TAP_MAX      = 0x3FFF;
    public const ushort QK_LAYER_TAP        = 0x4000;
    public const ushort QK_LAYER_TAP_MAX    = 0x4FFF;
    public const ushort QK_LAYER_MOD        = 0x5000;
    public const ushort QK_LAYER_MOD_MAX    = 0x51FF;
    public const ushort QK_TO               = 0x5200;
    public const ushort QK_TO_MAX           = 0x521F;
    public const ushort QK_MO               = 0x5220;
    public const ushort QK_MO_MAX           = 0x523F;
    public const ushort QK_DF               = 0x5240;
    public const ushort QK_DF_MAX           = 0x525F;
    public const ushort QK_TG               = 0x5260;
    public const ushort QK_TG_MAX           = 0x527F;
    public const ushort QK_OSL              = 0x5280;
    public const ushort QK_OSL_MAX          = 0x529F;
    public const ushort QK_ONE_SHOT_MOD     = 0x52A0;
    public const ushort QK_ONE_SHOT_MOD_MAX = 0x52BF;
    public const ushort QK_TT               = 0x52C0;
    public const ushort QK_TT_MAX           = 0x52DF;
    public const ushort QK_TAP_DANCE        = 0x5700;
    public const ushort QK_TAP_DANCE_MAX    = 0x57FF;
    public const ushort QK_MACRO            = 0x7700;
    public const ushort QK_MACRO_MAX        = 0x77FF;
    public const ushort QK_KB               = 0x7E00;
    public const ushort QK_KB_MAX           = 0x7FFF;
}
