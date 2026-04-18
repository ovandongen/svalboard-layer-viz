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
    public Key Key { get; private set; }
    public Layer Layer { get; }
    private KeyStyle _style;
    private readonly Action<KeyViewModel>? _setLabelRequested;

    // Captured at construction so _style can be recomputed in UpdateBaseline
    // when the new key changes category (plain ↔ layer-switch ↔ transparent).
    private readonly LayerColors _ownLayerColors;
    private readonly LayerColorPalette _palette;

    /// <summary>
    /// Replaces the underlying <see cref="Key"/> model after a post-save reload
    /// and fires a broadcast PropertyChanged so every binding (labels, tooltip,
    /// hex, colors, etc.) re-evaluates. This keeps the KeyViewModel *instance*
    /// stable, which is critical for creative-layout views bound via hardcoded
    /// paths: replacing the VM tree wholesale leaves those bindings pointing at
    /// stale instances and makes subsequent edits appear to not update.
    /// </summary>
    public void UpdateBaseline(Key newKey)
    {
        Key = newKey;
        _style = KeyStyleResolver.Resolve(newKey, Layer.Index, _ownLayerColors, ResolveTargetLayerColors(newKey));
        OnPropertyChanged(string.Empty);
    }

    private LayerColors? ResolveTargetLayerColors(Key key)
    {
        if (!key.IsLayerSwitch || !key.TargetLayer.HasValue) return null;
        return _palette.Get(key.TargetLayer.Value);
    }

    /// <summary>Baseline label (device state, ignoring pending edits). Used in tooltip diff.</summary>
    public string BaselineLabel => Key.IsTransparent
        ? Key.EffectiveLabel ?? "___"
        : Key.DisplayLabel;

    /// <summary>Primary label shown on the key face. Returns pending label when in edit mode with pending change.</summary>
    public string DisplayLabel => IsPending && PendingLabel is not null
        ? PendingLabel
        : BaselineLabel;

    /// <summary>Baseline secondary label (modifier prefix on the device-state key).</summary>
    public string? BaselineSecondaryLabel => Key.SecondaryLabel;

    /// <summary>Secondary label shown on the key face and tooltip. Swaps to pending when IsPending.</summary>
    public string? SecondaryLabel => IsPending ? PendingSecondaryLabel : BaselineSecondaryLabel;

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

    /// <summary>True if this key has a pending (unsaved) edit relative to the device baseline.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    [NotifyPropertyChangedFor(nameof(SecondaryLabel))]
    [NotifyPropertyChangedFor(nameof(Tooltip))]
    [NotifyPropertyChangedFor(nameof(BackgroundColor))]
    [NotifyPropertyChangedFor(nameof(BorderColor))]
    private bool _isPending;

    /// <summary>Human-readable label for the pending keycode, shown in place of the baseline when IsPending.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    [NotifyPropertyChangedFor(nameof(Tooltip))]
    private string? _pendingLabel;

    /// <summary>Secondary (modifier) label for the pending keycode, e.g. "MT(Ctrl)".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SecondaryLabel))]
    [NotifyPropertyChangedFor(nameof(Tooltip))]
    private string? _pendingSecondaryLabel;

    /// <summary>Raw pending keycode (for building the picker seed and edit ops).</summary>
    [ObservableProperty]
    private ushort _pendingKeycode;

    /// <summary>True when the app is in edit mode — enables click-to-edit on this key.</summary>
    [ObservableProperty]
    private bool _isEditMode;

    /// <summary>Invoked when the key is clicked in edit mode. Wired up by MainWindowViewModel after layer build.</summary>
    public Action<KeyViewModel>? OnClickAction { get; set; }

    /// <summary>Tooltip with full key details.</summary>
    public string Tooltip => BuildTooltip();

    // --- Visual styling ---

    /// <summary>Border thickness: thicker when key is pressed.</summary>
    public Avalonia.Thickness ActiveBorderThickness => IsPressed
        ? new Avalonia.Thickness(3.0)
        : new Avalonia.Thickness(1.5);

    /// <summary>Background color: accent tint when pending, target layer for layer-switch, dimmer for transparent.</summary>
    public string BackgroundColor => IsPending ? "#89B4FA" : _style.Background;

    /// <summary>Border color: bright white when pressed, accent when pending, target layer for layer-switch, lighter for transparent.</summary>
    public string BorderColor => IsPressed ? "#FFFFFF" : IsPending ? "#FAB387" : _style.Border;

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

    public KeyViewModel(PositionedKey posKey, Layer layer,
        double clusterOriginX, double clusterOriginY,
        LayerColorPalette palette,
        Action<KeyViewModel>? setLabelRequested = null)
    {
        Key = posKey.Key;
        Layer = layer;
        _setLabelRequested = setLabelRequested;
        _palette = palette;

        Left = posKey.BoardX - clusterOriginX;
        Top = posKey.BoardY - clusterOriginY;
        Width = posKey.Width;
        Height = posKey.Height;

        _ownLayerColors = palette.Get(layer.Index);
        _style = KeyStyleResolver.Resolve(posKey.Key, layer.Index, _ownLayerColors, ResolveTargetLayerColors(posKey.Key));
    }

    [RelayCommand]
    private void SetLabel() => _setLabelRequested?.Invoke(this);

    [RelayCommand]
    private void Click() => OnClickAction?.Invoke(this);

    private string BuildTooltip()
    {
        var loc = Loc.Instance;
        var parts = new List<string>();

        if (IsPending)
        {
            var oldLabel = Compose(BaselineSecondaryLabel, BaselineLabel);
            var newLabel = Compose(PendingSecondaryLabel, PendingLabel ?? "");
            parts.Add(loc.Format("Key_TooltipPendingFormat", oldLabel, newLabel));
        }

        parts.Add(loc.Format("Key_TooltipLayerFormat", Layer.Index, DisplayLabel));

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

    private static string Compose(string? secondary, string label) =>
        string.IsNullOrEmpty(secondary) ? label : $"{secondary} {label}";
}
