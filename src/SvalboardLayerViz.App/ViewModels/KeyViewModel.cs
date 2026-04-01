using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.Core.Keymap;
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
    private readonly LayerColors _colors;
    private readonly LayerColors? _targetLayerColors;
    private readonly Action<KeyViewModel>? _setLabelRequested;

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

    /// <summary>True if this key activates another layer.</summary>
    public bool IsLayerSwitch => Key.IsLayerSwitch;

    /// <summary>Target layer index for layer-switching keys.</summary>
    public int? TargetLayer => Key.TargetLayer;

    /// <summary>True if the keycode could not be resolved to a known label.</summary>
    public bool IsUnknown => Key.IsUnknown;

    /// <summary>Tooltip with full key details.</summary>
    public string Tooltip => BuildTooltip();

    // --- Visual styling ---

    /// <summary>Background color: target layer color for layer-switch keys, dimmer for transparent keys.</summary>
    public string BackgroundColor
    {
        get
        {
            if (IsLayerSwitch && _targetLayerColors is not null)
                return _targetLayerColors.Background;
            return IsTransparent ? _colors.TransparentBackground : _colors.Background;
        }
    }

    /// <summary>Border color: target layer color for layer-switch keys, lighter for transparent keys.</summary>
    public string BorderColor
    {
        get
        {
            if (IsLayerSwitch && _targetLayerColors is not null)
                return _targetLayerColors.Accent;
            return IsTransparent ? _colors.TransparentBorder : _colors.Border;
        }
    }

    /// <summary>Text color: auto-contrasts against background.</summary>
    public string TextColor
    {
        get
        {
            if (IsLayerSwitch && _targetLayerColors is not null)
                return _targetLayerColors.TextColor;
            return IsTransparent ? _colors.TransparentTextColor : _colors.TextColor;
        }
    }

    /// <summary>Opacity: reduced for transparent keys.</summary>
    public double KeyOpacity => IsTransparent ? 0.55 : 1.0;

    // --- Layout positioning (in pixels, scaled from layout units) ---

    private const double Scale = 60.0; // 1 layout unit = 60 pixels (matching keybard-ng)

    public double Left => Key.X * Scale;
    public double Top => Key.Y * Scale;
    public double Width => Key.Width * Scale;
    public double Height => Key.Height * Scale;

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
        _colors = LayerColorService.GetLayerColors(layer.Index, totalLayers,
            layer.ColorHue, layer.ColorSat, layer.ColorVal, userColor);

        if (key.IsLayerSwitch && key.TargetLayer.HasValue)
        {
            var targetColor = userLayerColors?.GetValueOrDefault(key.TargetLayer.Value);
            _targetLayerColors = LayerColorService.GetLayerColors(key.TargetLayer.Value, totalLayers,
                userHexColor: targetColor);
        }
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
