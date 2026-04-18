namespace SvalboardLayerViz.Core.Keymap.Builders;

/// <summary>
/// Builds a One-Shot Modifier keycode: next key pressed includes modifier(s).
/// </summary>
public sealed class OneShotModBuilder : KeycodeBuilderBase
{
    private ModFlags _mods;

    public override string DisplayName => BuilderLocalization.Get("Builder_OneShotMod_Name");
    public override string Description => BuilderLocalization.Get("Builder_OneShotMod_Desc");
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

    public bool IsCtrl  { get => (Mods & ModFlags.Ctrl)  != 0; set => Mods = value ? Mods | ModFlags.Ctrl  : Mods & ~ModFlags.Ctrl;  }
    public bool IsShift { get => (Mods & ModFlags.Shift) != 0; set => Mods = value ? Mods | ModFlags.Shift : Mods & ~ModFlags.Shift; }
    public bool IsAlt   { get => (Mods & ModFlags.Alt)   != 0; set => Mods = value ? Mods | ModFlags.Alt   : Mods & ~ModFlags.Alt;   }
    public bool IsGui   { get => (Mods & ModFlags.Gui)   != 0; set => Mods = value ? Mods | ModFlags.Gui   : Mods & ~ModFlags.Gui;   }

    public override bool CanBuild => Mods != ModFlags.None;

    public override string PreviewLabel => CanBuild ? $"OSM({Mods})" : "One-Shot Mod: ...";

    public override KeycodeDescriptor Build()
    {
        if (!CanBuild) throw new InvalidOperationException("Not all slots are filled");
        return new OneShotModKeycode(Mods);
    }
}
