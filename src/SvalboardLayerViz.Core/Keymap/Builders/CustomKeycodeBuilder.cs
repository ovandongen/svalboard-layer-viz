namespace SvalboardLayerViz.Core.Keymap.Builders;

/// <summary>
/// Builds a custom/keyboard-specific keycode by index.
/// </summary>
public sealed class CustomKeycodeBuilder : KeycodeBuilderBase
{
    private int? _index;

    public override string DisplayName => BuilderLocalization.Get("Builder_CustomKeycode_Name");
    public override string Description => BuilderLocalization.Get("Builder_CustomKeycode_Desc");
    public override BuilderCategory Category => BuilderCategory.Custom;

    public int? Index
    {
        get => _index;
        set
        {
            if (_index == value) return;
            _index = value;
            Notify(nameof(Index), nameof(CanBuild), nameof(PreviewLabel));
        }
    }

    public override bool CanBuild => Index.HasValue;

    public override string PreviewLabel => CanBuild
        ? $"CUSTOM({Index!.Value})"
        : "Custom: ...";

    public override KeycodeDescriptor Build()
    {
        if (!CanBuild) throw new InvalidOperationException("Not all slots are filled");
        return new CustomKeycodeDescriptor(Index!.Value);
    }
}
