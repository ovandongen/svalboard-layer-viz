namespace SvalboardLayerViz.App.ViewModels;

public enum ThumbRenderMode
{
    Default,
    Creative
}

public static class ThumbRenderSettings
{
    public static ThumbRenderMode Mode { get; set; } = ThumbRenderMode.Creative;
}
