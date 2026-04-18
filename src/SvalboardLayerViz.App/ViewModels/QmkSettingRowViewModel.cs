using CommunityToolkit.Mvvm.ComponentModel;
using SvalboardLayerViz.App.Localization;
using SvalboardLayerViz.Core.QmkSettings;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// Wraps a single QMK firmware setting for display in the Device tab.
/// When in edit mode, value changes are staged as <see cref="Core.Keymap.SetQmkSettingOp"/>
/// via the <see cref="_onValueChanged"/> callback.
/// </summary>
public partial class QmkSettingRowViewModel : ObservableObject
{
    private readonly QmkSettingDescriptor _descriptor;
    private readonly ushort _baselineValue;
    private readonly Action<ushort, ushort>? _onValueChanged;
    private bool _initialized;

    [ObservableProperty]
    private ushort _value;

    public QmkSettingRowViewModel(
        QmkSettingDescriptor descriptor,
        ushort currentValue,
        bool isEditMode,
        Action<ushort, ushort>? onValueChanged)
    {
        _descriptor = descriptor;
        _baselineValue = currentValue;
        _value = currentValue;
        IsReadOnly = !isEditMode;
        _onValueChanged = onValueChanged;
    }

    /// <summary>
    /// Must be called after the VM is bound to the UI. Prevents Slider two-way
    /// binding init noise (Avalonia writes 0 before Min/Max bindings apply) from
    /// clobbering the baseline value.
    /// </summary>
    public void MarkInitialized() => _initialized = true;

    /// <summary>Localized display name for the setting.</summary>
    public string Name
    {
        get
        {
            var localized = Loc.Instance[_descriptor.NameKey];
            return localized != _descriptor.NameKey ? localized : _descriptor.NameKey;
        }
    }

    /// <summary>Localized plain-language description of what this setting does.</summary>
    public string Description
    {
        get
        {
            var key = _descriptor.NameKey + "_Desc";
            var localized = Loc.Instance[key];
            return localized != key ? localized : string.Empty;
        }
    }

    /// <summary>Unit suffix for display (e.g. "ms"). Empty for booleans/counts.</summary>
    public string UnitSuffix
    {
        get
        {
            if (_descriptor.Type == QmkSettingType.Boolean) return string.Empty;
            var key = _descriptor.NameKey + "_Unit";
            var localized = Loc.Instance[key];
            return localized != key ? localized : string.Empty;
        }
    }

    /// <summary>Formatted value with unit suffix for display.</summary>
    public string FormattedValue => string.IsNullOrEmpty(UnitSuffix)
        ? Value.ToString()
        : $"{Value} {UnitSuffix}";

    public QmkSettingType SettingType => _descriptor.Type;
    public bool IsBoolean => _descriptor.Type == QmkSettingType.Boolean;
    public ushort Min => _descriptor.Min;
    public ushort Max => _descriptor.Max;
    public ushort DefaultValue => _descriptor.DefaultValue;
    public ushort SettingId => _descriptor.Id;
    public string GroupKey => _descriptor.GroupKey;
    public bool IsReadOnly { get; }

    /// <summary>True when the current value differs from the baseline (device state at session start).</summary>
    public bool IsPending => Value != _baselineValue;

    /// <summary>Convenience for Slider binding (Avalonia Slider.Value is double).</summary>
    public double SliderValue
    {
        get => Value;
        set
        {
            // Reject writes before MarkInitialized() — Avalonia Slider two-way binding
            // writes 0 (or default max) during DataContext assignment before Min/Max apply.
            if (!_initialized) return;
            var clamped = (ushort)Math.Clamp((int)Math.Round(value), Min, Max);
            Value = clamped;
        }
    }

    /// <summary>Convenience for Boolean settings (CheckBox binding).</summary>
    public bool BoolValue
    {
        get => Value != 0;
        set => Value = (ushort)(value ? 1 : 0);
    }

    partial void OnValueChanged(ushort oldValue, ushort newValue)
    {
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(SliderValue));
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(FormattedValue));

        // Only fire callback for genuine user edits, not Slider binding init noise.
        // Slider two-way binding can briefly write 0 during DataContext assignment
        // before Min/Max bindings apply, then restore the real value — skip both.
        if (!IsReadOnly && oldValue != newValue && IsPending)
            _onValueChanged?.Invoke(_descriptor.Id, newValue);
    }
}
