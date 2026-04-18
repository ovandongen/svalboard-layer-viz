using System.ComponentModel;

namespace SvalboardLayerViz.Core.Keymap.Builders;

/// <summary>
/// Interface for keycode builder wizards. Each builder constructs one
/// family of keycodes (ModTap, LayerTap, etc.) from observable, typed
/// slot properties. The picker UI data-templates against concrete
/// builder types and two-way-binds to those properties.
/// </summary>
public interface IKeycodeBuilder : INotifyPropertyChanged
{
    string DisplayName { get; }
    string Description { get; }
    BuilderCategory Category { get; }
    bool CanBuild { get; }
    string PreviewLabel { get; }
    KeycodeDescriptor Build();
}

/// <summary>
/// Builders with a "tap key" slot (ModTap, LayerTap). The picker VM exposes a
/// single SelectTapKey command that targets whichever of these is active.
/// </summary>
public interface IHasTapKey
{
    ushort? TapKey { get; set; }
}

/// <summary>
/// Shared INotifyPropertyChanged plumbing for concrete builders.
/// </summary>
public abstract class KeycodeBuilderBase : IKeycodeBuilder
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Notify(params string[] names)
    {
        var handler = PropertyChanged;
        if (handler is null) return;
        foreach (var n in names)
            handler(this, new PropertyChangedEventArgs(n));
    }

    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public abstract BuilderCategory Category { get; }
    public abstract bool CanBuild { get; }
    public abstract string PreviewLabel { get; }
    public abstract KeycodeDescriptor Build();
}

/// <summary>
/// Categories for grouping builders in the picker UI.
/// </summary>
public enum BuilderCategory
{
    Modifier,
    Layer,
    Custom,
}
