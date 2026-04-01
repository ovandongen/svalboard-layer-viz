using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.Core.Device;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Models;
using SvalboardLayerViz.Core.Protocol;
using SvalboardLayerViz.Core.Settings;

namespace SvalboardLayerViz.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly DeviceConnectionService _deviceService = new();
    private readonly VialProtocolService _protocolService = new();
    private readonly KeycodeService _keycodeService = new();
    private readonly ISettingsService _settingsService;
    private IDisposable? _deviceSubscription;

    [ObservableProperty]
    private KeyboardConfig? _keyboardConfig;

    [ObservableProperty]
    private int _selectedLayerIndex;

    public LayerViewModel? SelectedLayer => Layers.FirstOrDefault(l => l.Index == SelectedLayerIndex);

    partial void OnSelectedLayerIndexChanged(int value) => OnPropertyChanged(nameof(SelectedLayer));

    [ObservableProperty]
    private string _statusMessage = "Looking for Svalboard...";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isAlwaysOnTop;

    [ObservableProperty]
    private ObservableCollection<LayerViewModel> _layers = [];

    public IReadOnlyList<ClusterViewModel> Clusters { get; } = ClusterViewModel.BuildFromLayout();

    /// <summary>Callback to show/focus the main window. Wired up by App.axaml.cs.</summary>
    public Action? ShowWindowRequested { get; set; }

    /// <summary>Callback to toggle window visibility. Wired up by App.axaml.cs.</summary>
    public Action? ToggleWindowRequested { get; set; }

    /// <summary>Callback to open the settings window. Wired up by App.axaml.cs.</summary>
    public Action? OpenSettingsRequested { get; set; }

    /// <summary>Callback when hotkey settings change. Wired up by App.axaml.cs. Args: (key, modifiers).</summary>
    public Action<string, string>? HotkeyChangeRequested { get; set; }

    /// <summary>Callback to show a label editor for a key. Wired up by App.axaml.cs. Args: KeyViewModel.</summary>
    public Action<KeyViewModel>? SetKeyLabelRequested { get; set; }

    public MainWindowViewModel(ISettingsService? settingsService = null)
    {
        _settingsService = settingsService ?? new SettingsService();
        IsAlwaysOnTop = _settingsService.Load().AlwaysOnTop;

        // Try to connect on startup
        TryConnect();

        // Monitor for device changes (store subscription to prevent GC)
        _deviceSubscription = _deviceService.OnDeviceListChanged(TryConnect);
    }

    private void TryConnect()
    {
        try
        {
            var settings = _settingsService.Load();
            var devices = _deviceService.FindVialDevices();
            if (devices.Count == 0)
            {
                StatusMessage = "No Svalboard found. Connect your device via USB.";
                IsConnected = false;
                return;
            }

            var device = devices[0];
            StatusMessage = $"Connecting to {device.ProductName}...";

            var loader = new KeymapLoader(_protocolService, _keycodeService);
            KeyboardConfig = loader.Load(device, settings);

            BuildLayerViewModels(settings);

            SelectedLayerIndex = 0;
            IsConnected = true;
            StatusMessage = $"Connected: {device.ProductName} — {KeyboardConfig.Layers.Count} layers";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection error: {ex.Message}";
            IsConnected = false;
        }
    }

    private void BuildLayerViewModels(UserSettings settings)
    {
        Layers.Clear();
        if (KeyboardConfig is null) return;

        var totalLayers = KeyboardConfig.Layers.Count;
        var userColors = settings.LayerColors.Count > 0 ? settings.LayerColors : null;

        foreach (var layer in KeyboardConfig.Layers)
        {
            // Skip layers where every key is empty (KC_NO)
            if (layer.Keys.All(k => k.RawKeycode == 0x0000))
                continue;

            Layers.Add(new LayerViewModel(layer, i => SelectedLayerIndex = i, totalLayers, userColors,
                keyVm => SetKeyLabelRequested?.Invoke(keyVm)));
        }
    }

    /// <summary>
    /// Re-applies settings (colors, names, labels) and rebuilds the UI.
    /// Called after the settings window saves.
    /// Does NOT re-read the device — applies changes to the existing config in memory.
    /// </summary>
    public void ApplySettings()
    {
        var settings = _settingsService.Load();

        // Notify hotkey change
        HotkeyChangeRequested?.Invoke(settings.HotkeyKey, settings.HotkeyModifiers);

        if (KeyboardConfig is null) return;

        // Re-resolve custom labels
        _keycodeService.SetCustomKeyLabels(settings.CustomKeyLabels);

        // Update layers with new names and re-resolved key labels
        var updatedLayers = KeyboardConfig.Layers.Select(layer =>
        {
            settings.LayerNames.TryGetValue(layer.Index, out var name);
            var updatedKeys = layer.Keys.Select(key =>
            {
                var info = _keycodeService.Resolve(key.RawKeycode);
                return key with
                {
                    DisplayLabel = info.Label,
                    SecondaryLabel = info.SecondaryLabel,
                    IsUnknown = info.IsUnknown,
                };
            }).ToList();
            return layer with { Name = name, Keys = updatedKeys };
        }).ToList();

        KeyboardConfig = KeyboardConfig with { Layers = updatedLayers };

        var currentLayer = SelectedLayerIndex;
        BuildLayerViewModels(settings);
        // Force re-selection even if index unchanged, so the UI rebinds
        _selectedLayerIndex = -1;
        SelectedLayerIndex = currentLayer;
    }

    /// <summary>
    /// Collects unknown keycodes from the current config for the settings UI.
    /// </summary>
    public IReadOnlyList<(string HexKeycode, string CurrentLabel)> GetUnknownKeycodes()
    {
        if (KeyboardConfig is null) return [];

        var seen = new HashSet<string>();
        var result = new List<(string, string)>();

        foreach (var layer in KeyboardConfig.Layers)
        {
            foreach (var key in layer.Keys)
            {
                if (key.IsUnknown)
                {
                    var hex = $"0x{key.RawKeycode:X4}";
                    if (seen.Add(hex))
                        result.Add((hex, key.DisplayLabel));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Saves a custom label for a keycode and refreshes the display.
    /// </summary>
    public void SaveCustomKeyLabel(string hexKeycode, string label)
    {
        var settings = _settingsService.Load();
        if (string.IsNullOrWhiteSpace(label))
            settings.CustomKeyLabels.Remove(hexKeycode);
        else
            settings.CustomKeyLabels[hexKeycode] = label;
        _settingsService.Save(settings);
        ApplySettings();
    }

    [RelayCommand]
    private void SelectLayer(int index) => SelectedLayerIndex = index;

    [RelayCommand]
    private void Show()
    {
        ShowWindowRequested?.Invoke();
    }

    [RelayCommand]
    private void ToggleOverlay()
    {
        ToggleWindowRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        OpenSettingsRequested?.Invoke();
    }

    [RelayCommand]
    private void TogglePin()
    {
        IsAlwaysOnTop = !IsAlwaysOnTop;
        var settings = _settingsService.Load();
        _settingsService.Save(settings with { AlwaysOnTop = IsAlwaysOnTop });
    }

    [RelayCommand]
    private void Refresh()
    {
        TryConnect();
    }

    [RelayCommand]
    private void Quit()
    {
        _deviceSubscription?.Dispose();
        _protocolService.Dispose();
        Environment.Exit(0);
    }
}
