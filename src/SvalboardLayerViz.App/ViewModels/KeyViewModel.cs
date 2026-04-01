using CommunityToolkit.Mvvm.ComponentModel;
using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// ViewModel for a single key in the board visualization.
/// Handles display logic like positioning, colors, and tooltip text.
/// </summary>
public partial class KeyViewModel : ObservableObject
{
    public Key Key { get; }
    public Layer Layer { get; }

    /// <summary>Primary label shown on the key face.</summary>
    public string DisplayLabel => Key.IsTransparent
        ? Key.EffectiveLabel ?? "___"
        : Key.DisplayLabel;

    /// <summary>Secondary label (modifier prefix, etc.).</summary>
    public string? SecondaryLabel => Key.SecondaryLabel;

    /// <summary>True if this key is transparent (showing a key from a lower layer).</summary>
    public bool IsTransparent => Key.IsTransparent;

    /// <summary>True if this key has no assignment.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Key.DisplayLabel) && !Key.IsTransparent;

    /// <summary>Tooltip with full key details.</summary>
    public string Tooltip => BuildTooltip();

    // --- Layout positioning (in pixels, scaled from layout units) ---

    private const double Scale = 60.0; // 1 layout unit = 60 pixels (matching keybard-ng)

    public double Left => Key.X * Scale;
    public double Top => Key.Y * Scale;
    public double Width => Key.Width * Scale;
    public double Height => Key.Height * Scale;

    public KeyViewModel(Key key, Layer layer)
    {
        Key = key;
        Layer = layer;
    }

    private string BuildTooltip()
    {
        var parts = new List<string>
        {
            $"Layer {Layer.Index}: {DisplayLabel}"
        };

        if (SecondaryLabel is not null)
            parts.Add($"Modifier: {SecondaryLabel}");

        if (IsTransparent)
            parts.Add("(Transparent — inherited from layer below)");

        parts.Add($"Row: {Key.Row}, Col: {Key.Col}");
        parts.Add($"Raw: 0x{Key.RawKeycode:X4}");

        return string.Join("\n", parts);
    }
}
