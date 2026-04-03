using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.App.Localization;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Settings;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// ViewModel for the Settings window.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly int _totalLayers;

    public ObservableCollection<LayerSettingViewModel> LayerSettings { get; } = [];
    public ObservableCollection<CustomKeyLabelViewModel> CustomKeyLabels { get; } = [];

    [ObservableProperty]
    private string _newKeycodeHex = "";

    /// <summary>Available UI languages.</summary>
    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
    [
        new("en", "English"),
        new("nl", "Nederlands"),
    ];

    [ObservableProperty]
    private LanguageOption _selectedLanguage = null!;

    [ObservableProperty]
    private string _hotkeyKey = "F12";

    [ObservableProperty]
    private bool _hotkeyCtrl;

    [ObservableProperty]
    private bool _hotkeyShift;

    [ObservableProperty]
    private bool _hotkeyAlt;

    [ObservableProperty]
    private bool _hotkeyGui;

    [ObservableProperty]
    private double _backgroundOpacity;

    public bool IsHotkeySupported { get; } = !OperatingSystem.IsLinux();

    public string OpacityPercent => $"{(int)(BackgroundOpacity * 100)}%";

    partial void OnBackgroundOpacityChanged(double value) => OnPropertyChanged(nameof(OpacityPercent));

    [ObservableProperty]
    private int _layerHoldThresholdMs;

    public string ThresholdDisplay => $"{LayerHoldThresholdMs} ms";

    partial void OnLayerHoldThresholdMsChanged(int value) => OnPropertyChanged(nameof(ThresholdDisplay));

    /// <summary>Fired when the user saves settings successfully.</summary>
    public Action? SettingsSaved { get; set; }

    /// <summary>Fired when the user cancels.</summary>
    public Action? Cancelled { get; set; }

    /// <summary>Tapping term from device, shown as hint in the settings UI. Null if unavailable.</summary>
    public int? DeviceTappingTermMs { get; }

    public string DeviceTappingTermHint => DeviceTappingTermMs.HasValue
        ? Loc.Instance.Format("Settings_DeviceTappingTermFormat", DeviceTappingTermMs.Value)
        : Loc.Instance["Settings_DeviceTappingTermUnavailable"];

    public SettingsViewModel(ISettingsService settingsService, int totalLayers,
        IReadOnlyList<(string HexKeycode, string CurrentLabel)>? unknownKeycodes = null,
        int? deviceTappingTermMs = null)
    {
        _settingsService = settingsService;
        _totalLayers = totalLayers;
        DeviceTappingTermMs = deviceTappingTermMs;

        var settings = settingsService.Load();

        // Populate layer settings
        for (var i = 0; i < totalLayers; i++)
        {
            var name = settings.LayerNames.GetValueOrDefault(i, "");
            var color = settings.LayerColors.GetValueOrDefault(i, "");
            var defaultColor = LayerColorService.GetLayerColors(i, totalLayers).Accent;

            var layerSetting = new LayerSettingViewModel
            {
                Index = i,
                Name = name,
                HexColor = color,
                DefaultColor = defaultColor,
            };
            layerSetting.SyncPickerFromHex();
            LayerSettings.Add(layerSetting);
        }

        // Populate custom key labels — unknown keycodes from board
        var seenKeycodes = new HashSet<string>();
        if (unknownKeycodes is not null)
        {
            foreach (var (hex, label) in unknownKeycodes)
            {
                seenKeycodes.Add(hex);
                var userLabel = settings.CustomKeyLabels.GetValueOrDefault(hex, "");
                AddCustomKeyLabelVm(hex, label, userLabel, isManual: false);
            }
        }

        // Also show previously-saved manual labels not found on board
        foreach (var (hex, label) in settings.CustomKeyLabels)
        {
            if (!seenKeycodes.Contains(hex))
                AddCustomKeyLabelVm(hex, Loc.Instance["Settings_ManualLabel"], label, isManual: true);
        }

        // Populate opacity and threshold
        BackgroundOpacity = settings.BackgroundOpacity;
        LayerHoldThresholdMs = settings.LayerHoldThresholdMs;

        // Populate language
        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == settings.Language)
                           ?? AvailableLanguages[0];

        // Populate hotkey
        HotkeyKey = settings.HotkeyKey;
        var mods = settings.HotkeyModifiers;
        HotkeyCtrl = mods.Contains("Ctrl");
        HotkeyShift = mods.Contains("Shift");
        HotkeyAlt = mods.Contains("Alt");
        HotkeyGui = mods.Contains("GUI");
    }

    [RelayCommand]
    private void Save()
    {
        var layerColors = new Dictionary<int, string>();
        var layerNames = new Dictionary<int, string>();
        foreach (var ls in LayerSettings)
        {
            if (!string.IsNullOrWhiteSpace(ls.HexColor))
                layerColors[ls.Index] = ls.HexColor;
            if (!string.IsNullOrWhiteSpace(ls.Name))
                layerNames[ls.Index] = ls.Name;
        }

        var customLabels = new Dictionary<string, string>();
        foreach (var ck in CustomKeyLabels)
        {
            if (!string.IsNullOrWhiteSpace(ck.UserLabel))
                customLabels[ck.HexKeycode] = ck.UserLabel;
        }

        var modParts = new List<string>();
        if (HotkeyCtrl) modParts.Add("Ctrl");
        if (HotkeyShift) modParts.Add("Shift");
        if (HotkeyAlt) modParts.Add("Alt");
        if (HotkeyGui) modParts.Add("GUI");
        var modsString = modParts.Count > 0 ? string.Join("+", modParts) : "None"; // "None" is a settings key, not UI text

        var existing = _settingsService.Load();
        var settings = existing with
        {
            LayerColors = layerColors,
            LayerNames = layerNames,
            CustomKeyLabels = customLabels,
            HotkeyKey = HotkeyKey,
            HotkeyModifiers = modsString,
            BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0.0, 1.0),
            LayerHoldThresholdMs = Math.Clamp(LayerHoldThresholdMs, 0, 1000),
            Language = SelectedLanguage.Code,
        };

        // Apply language change immediately
        Loc.Instance.SetCulture(SelectedLanguage.Code);

        _settingsService.Save(settings);
        SettingsSaved?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Cancelled?.Invoke();
    }

    [RelayCommand]
    private void ResetColors()
    {
        foreach (var ls in LayerSettings)
        {
            ls.HexColor = "";
            ls.SyncPickerFromHex();
        }
    }

    private void AddCustomKeyLabelVm(string hex, string currentLabel, string userLabel, bool isManual)
    {
        var vm = new CustomKeyLabelViewModel
        {
            HexKeycode = hex,
            CurrentLabel = currentLabel,
            UserLabel = userLabel,
            IsManualEntry = isManual,
            RemoveRequested = item => CustomKeyLabels.Remove(item),
        };
        CustomKeyLabels.Add(vm);
    }

    [RelayCommand]
    private void AddCustomLabel()
    {
        var hex = NewKeycodeHex.Trim();
        if (string.IsNullOrWhiteSpace(hex)) return;

        // Normalize to 0xNNNN format
        if (!hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = "0x" + hex;
        hex = hex.ToUpperInvariant().Replace("0X", "0x");

        // Don't add duplicates
        if (CustomKeyLabels.Any(c => c.HexKeycode == hex)) return;

        AddCustomKeyLabelVm(hex, "(manual)", "", isManual: true);
        NewKeycodeHex = "";
    }
}

/// <summary>Per-layer settings row.</summary>
public partial class LayerSettingViewModel : ObservableObject
{
    private bool _updating;

    public int Index { get; init; }

    /// <summary>Localized "Layer N" label for the settings row.</summary>
    public string LocalizedLayerLabel => Loc.Instance.Format("Settings_LayerFormat", Index);

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _hexColor = "";

    [ObservableProperty]
    private Color _pickerColor = Colors.Gray;

    /// <summary>Default algorithmic color (shown as placeholder/preview when no override).</summary>
    public string DefaultColor { get; init; } = "";

    /// <summary>The effective color: user override or default.</summary>
    public string EffectiveColor => string.IsNullOrWhiteSpace(HexColor) ? DefaultColor : HexColor;

    partial void OnHexColorChanged(string value)
    {
        OnPropertyChanged(nameof(EffectiveColor));
        if (_updating) return;
        _updating = true;
        try
        {
            var effective = string.IsNullOrWhiteSpace(value) ? DefaultColor : value;
            if (!string.IsNullOrWhiteSpace(effective))
            {
                var hex = effective.StartsWith('#') ? effective : "#" + effective;
                if (Color.TryParse(hex, out var c))
                    PickerColor = c;
            }
        }
        finally { _updating = false; }
    }

    partial void OnPickerColorChanged(Color value)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            HexColor = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
        }
        finally { _updating = false; }
    }

    /// <summary>Initializes PickerColor from the current effective color.</summary>
    public void SyncPickerFromHex()
    {
        var effective = string.IsNullOrWhiteSpace(HexColor) ? DefaultColor : HexColor;
        if (!string.IsNullOrWhiteSpace(effective))
        {
            var hex = effective.StartsWith('#') ? effective : "#" + effective;
            if (Color.TryParse(hex, out var c))
                PickerColor = c;
        }
    }
}

/// <summary>Unknown keycode with optional user label.</summary>
public partial class CustomKeyLabelViewModel : ObservableObject
{
    public string HexKeycode { get; init; } = "";
    public string CurrentLabel { get; init; } = "";
    public bool IsManualEntry { get; init; }

    /// <summary>Called when the user clicks remove. Set by parent SettingsViewModel.</summary>
    public Action<CustomKeyLabelViewModel>? RemoveRequested { get; set; }

    [ObservableProperty]
    private string _userLabel = "";

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this);
}

/// <summary>Language option for the settings UI language picker.</summary>
public record LanguageOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}
