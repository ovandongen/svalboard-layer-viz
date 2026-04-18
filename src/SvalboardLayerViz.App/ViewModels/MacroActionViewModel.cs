using CommunityToolkit.Mvvm.ComponentModel;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Macros;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// Wraps a single <see cref="MacroAction"/> for display and inline editing
/// in the macro editor dialog.
/// </summary>
public partial class MacroActionViewModel : ObservableObject
{
    private MacroAction _action;

    /// <summary>Fires when the underlying action changes (parent recalculates buffer size).</summary>
    public Action? Changed { get; set; }

    public MacroActionViewModel(MacroAction action)
    {
        _action = action;

        // Initialize editable fields from the action
        if (action is MacroDelayAction delay)
            _delayMs = delay.DelayMs;
        else if (action is MacroTextAction text)
            _textContent = text.Text;
    }

    /// <summary>The current underlying action. Reconstructed when editable properties change.</summary>
    public MacroAction Action => _action;

    public string TypeLabel => _action switch
    {
        MacroTapAction => "Tap",
        MacroModTapAction => "Mod Tap",
        MacroDownAction => "Hold",
        MacroUpAction => "Release",
        MacroDelayAction => "Delay",
        MacroTextAction => "Text",
        _ => "?"
    };

    public string ValueLabel => _action switch
    {
        MacroTapAction t => ResolveKeyName(t.Keycode),
        MacroModTapAction mt => $"{MacroPreviewHelper.FormatMods(mt.Mods)}+{ResolveKeyName(mt.Keycode)}",
        MacroDownAction d => ResolveKeyName(d.Keycode),
        MacroUpAction u => ResolveKeyName(u.Keycode),
        MacroDelayAction d => $"{d.DelayMs} ms",
        MacroTextAction t => t.Text.Length > 30 ? t.Text[..30] + "…" : t.Text,
        _ => ""
    };

    /// <summary>Whether this action's value is an inline-editable delay.</summary>
    public bool IsDelay => _action is MacroDelayAction;

    /// <summary>Whether this action's value is inline-editable text.</summary>
    public bool IsText => _action is MacroTextAction;

    /// <summary>Whether this action's value is a keycode (not inline-editable).</summary>
    public bool IsKey => _action is MacroTapAction or MacroModTapAction or MacroDownAction or MacroUpAction;

    [ObservableProperty]
    private int _delayMs;

    partial void OnDelayMsChanged(int value)
    {
        if (_action is MacroDelayAction)
        {
            var clamped = Math.Clamp(value, 0, 9999);
            _action = new MacroDelayAction(clamped);
            OnPropertyChanged(nameof(ValueLabel));
            Changed?.Invoke();
        }
    }

    [ObservableProperty]
    private string _textContent = "";

    partial void OnTextContentChanged(string value)
    {
        if (_action is MacroTextAction)
        {
            _action = new MacroTextAction(value);
            OnPropertyChanged(nameof(ValueLabel));
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Replaces the underlying action (used when key picker returns a new keycode).
    /// </summary>
    public void ReplaceAction(MacroAction newAction)
    {
        _action = newAction;
        OnPropertyChanged(nameof(Action));
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(ValueLabel));
        OnPropertyChanged(nameof(IsDelay));
        OnPropertyChanged(nameof(IsText));
        OnPropertyChanged(nameof(IsKey));
        Changed?.Invoke();
    }

    private static string ResolveKeyName(byte keycode) =>
        KeycodeCatalog.BasicKeycodes.GetValueOrDefault((ushort)keycode, $"0x{keycode:X2}");
}
