namespace SvalboardLayerViz.Core.Keymap.Builders;

/// <summary>
/// Registry of all available keycode builders, grouped by category.
/// Creates fresh builder instances for each picker session.
/// </summary>
public static class BuilderRegistry
{
    /// <summary>
    /// Returns a fresh set of all available builders.
    /// Each call returns new instances so picker sessions don't share state.
    /// </summary>
    public static IReadOnlyList<IKeycodeBuilder> CreateAll() =>
    [
        new ModTapBuilder(),
        new LayerTapBuilder(),
        new LayerModBuilder(),
        new LayerFunctionBuilder(),
        new OneShotModBuilder(),
        new CustomKeycodeBuilder(),
    ];

    /// <summary>
    /// Returns builders filtered by category.
    /// </summary>
    public static IReadOnlyList<IKeycodeBuilder> CreateByCategory(BuilderCategory category) =>
        CreateAll().Where(b => b.Category == category).ToList();
}
