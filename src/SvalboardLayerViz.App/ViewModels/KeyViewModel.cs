using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.App.Localization;
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

    // --- Layout positioning (cluster-relative, in pixels) ---

    public double Left { get; }
    public double Top { get; }
    public double Width { get; }
    public double Height { get; }

    /// <summary>Hex keycode string for display (e.g. "0x5300").</summary>
    public string HexKeycode => $"0x{Key.RawKeycode:X4}";

    /// <summary>
    /// Creates a KeyViewModel from a pre-computed PositionedKey with cluster-relative offsets.
    /// </summary>
    public KeyViewModel(PositionedKey posKey, Layer layer,
        double clusterOriginX, double clusterOriginY,
        int totalLayers = 8, Dictionary<int, string>? userLayerColors = null,
        Action<KeyViewModel>? setLabelRequested = null,
        IReadOnlyDictionary<int, (byte? H, byte? S, byte? V)>? deviceLayerColors = null)
    {
        Key = posKey.Key;
        Layer = layer;
        _setLabelRequested = setLabelRequested;

        // Cluster-relative positioning (pixels)
        Left = posKey.BoardX - clusterOriginX;
        Top = posKey.BoardY - clusterOriginY;
        Width = posKey.Width;
        Height = posKey.Height;

        var userColor = userLayerColors?.GetValueOrDefault(layer.Index);
        var colors = LayerColorService.GetLayerColors(layer.Index, totalLayers,
            layer.ColorHue, layer.ColorSat, layer.ColorVal, userColor);

        LayerColors? targetLayerColors = null;
        if (posKey.Key.IsLayerSwitch && posKey.Key.TargetLayer.HasValue)
        {
            var targetIdx = posKey.Key.TargetLayer.Value;
            var targetColor = userLayerColors?.GetValueOrDefault(targetIdx);
            var targetHsv = deviceLayerColors is not null && deviceLayerColors.TryGetValue(targetIdx, out var hsv)
                ? hsv
                : (null, null, null);
            targetLayerColors = LayerColorService.GetLayerColors(targetIdx, totalLayers,
                targetHsv.Item1, targetHsv.Item2, targetHsv.Item3, targetColor);
        }

        _style = KeyStyleResolver.Resolve(posKey.Key, layer.Index, colors, targetLayerColors);
    }

    [RelayCommand]
    private void SetLabel() => _setLabelRequested?.Invoke(this);

    private string BuildTooltip()
    {
        var loc = Loc.Instance;
        var parts = new List<string>
        {
            loc.Format("Key_TooltipLayerFormat", Layer.Index, DisplayLabel)
        };

        if (SecondaryLabel is not null)
            parts.Add(loc.Format("Key_TooltipModifierFormat", SecondaryLabel));

        if (IsTransparent)
            parts.Add(loc["Key_TooltipTransparent"]);

        if (IsLayerSwitch && TargetLayer.HasValue)
            parts.Add(loc.Format("Key_TooltipTargetLayerFormat", TargetLayer.Value));

        if (IsUnknown)
            parts.Add(loc["Key_TooltipUnknown"]);

        parts.Add(loc.Format("Key_TooltipRowColFormat", Key.Row, Key.Col));
        parts.Add($"Raw: 0x{Key.RawKeycode:X4}");

        return string.Join("\n", parts);
    }
}
