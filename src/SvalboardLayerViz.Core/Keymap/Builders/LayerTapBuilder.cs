namespace SvalboardLayerViz.Core.Keymap.Builders;

/// <summary>
/// Builds a Layer-Tap keycode: hold = activate layer, tap = basic key.
/// </summary>
public sealed class LayerTapBuilder : KeycodeBuilderBase, IHasTapKey
{
    private int? _layer;
    private ushort? _tapKey;

    public override string DisplayName => BuilderLocalization.Get("Builder_LayerTap_Name");
    public override string Description => BuilderLocalization.Get("Builder_LayerTap_Desc");
    public override BuilderCategory Category => BuilderCategory.Layer;

    public int? Layer
    {
        get => _layer;
        set
        {
            if (_layer == value) return;
            _layer = value;
            Notify(nameof(Layer), nameof(CanBuild), nameof(PreviewLabel));
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

    public override bool CanBuild => Layer.HasValue && TapKey.HasValue;

    public override string PreviewLabel => CanBuild
        ? $"LT({Layer!.Value}, {KeycodeCatalog.BasicKeycodes.GetValueOrDefault(TapKey!.Value, $"0x{TapKey.Value:X2}")})"
        : "Layer-Tap: ...";

    public override KeycodeDescriptor Build()
    {
        if (!CanBuild) throw new InvalidOperationException("Not all slots are filled");
        return new LayerTapKeycode(Layer!.Value, TapKey!.Value);
    }
}
