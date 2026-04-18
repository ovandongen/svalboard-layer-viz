namespace SvalboardLayerViz.Core.Keymap.Builders;

/// <summary>
/// Builds a layer function keycode: MO/TG/TO/DF/TT/OSL with a target layer.
/// </summary>
public sealed class LayerFunctionBuilder : KeycodeBuilderBase
{
    private LayerFunctionKind? _kind;
    private int? _layer;

    public override string DisplayName => BuilderLocalization.Get("Builder_LayerFunction_Name");
    public override string Description => BuilderLocalization.Get("Builder_LayerFunction_Desc");
    public override BuilderCategory Category => BuilderCategory.Layer;

    /// <summary>
    /// Human-readable explanation for the currently selected Kind. Updates live
    /// as Kind changes so the picker can show per-function help in the UI.
    /// </summary>
    public string KindDescription => BuilderLocalization.Get(Kind switch
    {
        LayerFunctionKind.MO  => "LayerFn_MO_Desc",
        LayerFunctionKind.TG  => "LayerFn_TG_Desc",
        LayerFunctionKind.TO  => "LayerFn_TO_Desc",
        LayerFunctionKind.DF  => "LayerFn_DF_Desc",
        LayerFunctionKind.OSL => "LayerFn_OSL_Desc",
        LayerFunctionKind.TT  => "LayerFn_TT_Desc",
        _ => "LayerFn_None_Desc",
    });

    public LayerFunctionKind? Kind
    {
        get => _kind;
        set
        {
            if (_kind == value) return;
            _kind = value;
            Notify(nameof(Kind), nameof(KindDescription), nameof(CanBuild), nameof(PreviewLabel));
        }
    }

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

    public override bool CanBuild => Kind.HasValue && Layer.HasValue;

    public override string PreviewLabel => CanBuild
        ? $"{Kind!.Value}({Layer!.Value})"
        : "Layer Function: ...";

    public override KeycodeDescriptor Build()
    {
        if (!CanBuild) throw new InvalidOperationException("Not all slots are filled");
        return new LayerFunctionKeycode(Kind!.Value, Layer!.Value);
    }
}
