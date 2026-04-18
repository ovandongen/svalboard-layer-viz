namespace SvalboardLayerViz.Core.Keymap.Builders;

/// <summary>
/// Resolves localized display strings for builders without dragging a UI
/// resource dependency into Core. The App registers a resolver at startup
/// that reads keys from its ResourceManager; when unregistered (tests,
/// Core-only scenarios) the key itself is returned so things stay
/// self-describing instead of going blank.
/// </summary>
public static class BuilderLocalization
{
    public static Func<string, string> Resolve { get; set; } = key => key;

    public static string Get(string key) => Resolve(key);
}
