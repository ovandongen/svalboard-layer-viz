using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.Core.Export;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Layout;
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
    private readonly KeyStyle _style;
    private readonly Action<KeyViewModel>? _setLabelRequested;

    /// <summary>Primary label shown on the key face.</summary>
    public string DisplayLabel => Key.IsTransparent
        ? Key.EffectiveLabel ?? "___"
        : Key.DisplayLabel;

    /// <summary>Secondary label (modifier prefix, etc.).</summary>
    public string? SecondaryLabel => Key.SecondaryLabel;

    /// <summary>Shifted symbol for keycap-style display (e.g., "@" for "2").</summary>
    public string? ShiftedLabel => Key.ShiftedLabel;

    /// <summary>True if this key is transparent (showing a key from a lower layer).</summary>
    public bool IsTransparent => Key.IsTransparent;

    /// <summary>True if this transparent key activates the layer it's displayed on (e.g., MO(3) on layer 3).</summary>
    public bool IsActivatorForCurrentLayer => Key.IsTransparent && Key.IsLayerSwitch && Key.TargetLayer == Layer.Index;

    /// <summary>True if this key has no assignment.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Key.DisplayLabel) && !Key.IsTransparent;

    /// <summary>True if this key activates another layer.</summary>
    public bool IsLayerSwitch => Key.IsLayerSwitch;

    /// <summary>Target layer index for layer-switching keys.</summary>
    public int? TargetLayer => Key.TargetLayer;

    /// <summary>True if the keycode could not be resolved to a known label.</summary>
    public bool IsUnknown => Key.IsUnknown;

    /// <summary>True if this key is currently physically pressed on the keyboard.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BorderColor))]
    [NotifyPropertyChangedFor(nameof(ActiveBorderThickness))]
    private bool _isPressed;

    /// <summary>Tooltip with full key details.</summary>
    public string Tooltip => BuildTooltip();

    // --- Visual styling ---

    /// <summary>Border thickness: thicker when key is pressed.</summary>
    public Avalonia.Thickness ActiveBorderThickness => IsPressed
        ? new Avalonia.Thickness(3.0)
        : new Avalonia.Thickness(1.5);

    /// <summary>Background color: target layer for layer-switch, dimmer for transparent.</summary>
    public string BackgroundColor => _style.Background;

    /// <summary>Border color: bright white when pressed, target layer for layer-switch, lighter for transparent.</summary>
    public string BorderColor => IsPressed ? "#FFFFFF" : _style.Border;

    /// <summary>Text color: auto-contrasts against background.</summary>
    public string TextColor => _style.Text;

    /// <summary>Opacity: reduced for transparent keys, full for layer activators.</summary>
    public double KeyOpacity => _style.Opacity;

    // --- Layout positioning (in pixels, scaled from layout units) ---

    public double Left => Key.X * SvalboardLayout.Scale;
    public double Top => Key.Y * SvalboardLayout.Scale;
    public double Width => Key.Width * SvalboardLayout.Scale;
    public double Height => Key.Height * SvalboardLayout.Scale;

    /// <summary>Hex keycode string for display (e.g. "0x5300").</summary>
    public string HexKeycode => $"0x{Key.RawKeycode:X4}";

    public KeyViewModel(Key key, Layer layer, int totalLayers = 8,
        Dictionary<int, string>? userLayerColors = null,
        Action<KeyViewModel>? setLabelRequested = null)
    {
        Key = key;
        Layer = layer;
        _setLabelRequested = setLabelRequested;

        var userColor = userLayerColors?.GetValueOrDefault(layer.Index);
        var colors = LayerColorService.GetLayerColors(layer.Index, totalLayers,
            layer.ColorHue, layer.ColorSat, layer.ColorVal, userColor);

        LayerColors? targetLayerColors = null;
        if (key.IsLayerSwitch && key.TargetLayer.HasValue)
        {
            var targetColor = userLayerColors?.GetValueOrDefault(key.TargetLayer.Value);
            targetLayerColors = LayerColorService.GetLayerColors(key.TargetLayer.Value, totalLayers,
                userHexColor: targetColor);
        }

        _style = KeyStyleResolver.Resolve(key, layer.Index, colors, targetLayerColors);
    }

    [RelayCommand]
    private void SetLabel() => _setLabelRequested?.Invoke(this);

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

        if (IsLayerSwitch && TargetLayer.HasValue)
            parts.Add($"→ Layer {TargetLayer.Value}");

        if (IsUnknown)
            parts.Add("(Unknown keycode — assign a label in Settings)");

        parts.Add($"Row: {Key.Row}, Col: {Key.Col}");
        parts.Add($"Raw: 0x{Key.RawKeycode:X4}");

        return string.Join("\n", parts);
    }
}
