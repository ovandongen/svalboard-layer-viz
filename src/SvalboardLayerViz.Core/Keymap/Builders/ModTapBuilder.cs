namespace SvalboardLayerViz.Core.Keymap.Builders;

/// <summary>
/// Builds a Mod-Tap keycode: hold = modifier(s), tap = basic key.
/// </summary>
public sealed class ModTapBuilder : KeycodeBuilderBase, IHasTapKey
{
    private ModFlags _mods;
    private ushort? _tapKey;

    public override string DisplayName => BuilderLocalization.Get("Builder_ModTap_Name");
    public override string Description => BuilderLocalization.Get("Builder_ModTap_Desc");
    public override BuilderCategory Category => BuilderCategory.Modifier;

    public ModFlags Mods
    {
        get => _mods;
        set
        {
            if (_mods == value) return;
            _mods = value;
            Notify(nameof(Mods), nameof(IsCtrl), nameof(IsShift), nameof(IsAlt), nameof(IsGui), nameof(CanBuild), nameof(PreviewLabel));
        }
    }

    public ushort? TapKey
    {
        get => _tapKey;
        set
        {
            if (_tapKey == value) return;
            _tapKey = value;
            Notify(nameof(TapKey), nameof(CanBuild), nameof(PreviewLabel));
        }
    }

    public bool IsCtrl  { get => (Mods & ModFlags.Ctrl)  != 0; set => Mods = value ? Mods | ModFlags.Ctrl  : Mods & ~ModFlags.Ctrl;  }
    public bool IsShift { get => (Mods & ModFlags.Shift) != 0; set => Mods = value ? Mods | ModFlags.Shift : Mods & ~ModFlags.Shift; }
    public bool IsAlt   { get => (Mods & ModFlags.Alt)   != 0; set => Mods = value ? Mods | ModFlags.Alt   : Mods & ~ModFlags.Alt;   }
    public bool IsGui   { get => (Mods & ModFlags.Gui)   != 0; set => Mods = value ? Mods | ModFlags.Gui   : Mods & ~ModFlags.Gui;   }

    public override bool CanBuild => Mods != ModFlags.None && TapKey.HasValue;

    public override string PreviewLabel => CanBuild
        ? $"MT({Mods}, {KeycodeCatalog.BasicKeycodes.GetValueOrDefault(TapKey!.Value, $"0x{TapKey.Value:X2}")})"
        : "Mod-Tap: ...";

    public override KeycodeDescriptor Build()
    {
        if (!CanBuild) throw new InvalidOperationException("Not all slots are filled");
        return new ModTapKeycode(Mods, TapKey!.Value);
    }
}
