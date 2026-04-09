using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.App.Localization;
using SvalboardLayerViz.Core.Device;
using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Layout;
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
    private MatrixPollingService? _matrixPolling;
    private LedPollingService? _ledPolling;

    /// <summary>
    /// True if the firmware responds to the standard VIA rgblight color query
    /// (0x08, 0x83). When true, we drive SelectedLayerIndex from the polled LED
    /// color instead of the matrix-based MO/TG heuristic — this catches layers
    /// the heuristic can't see (e.g. the mouse layer).
    /// </summary>
    private bool _ledPollSupported;

    private string? _connectedDeviceName;
    private CancellationTokenSource? _connectCts;
    private bool _isConnecting;

    /// <summary>
    /// Cached lookup: (row, col) → target layer for momentary layer-switch keys (MO, LT, TT).
    /// Built once on connect from layer 0, used during polling to auto-switch the visible layer.
    /// Momentary keys: layer active while physically held down.
    /// </summary>
    private Dictionary<(int Row, int Col), int> _momentaryLayerCache = new();

    /// <summary>
    /// Cached lookup: (row, col) → target layer for toggle layer-switch keys (TG).
    /// Built once on connect from layer 0.
    /// Toggle keys: each press flips the layer on/off. Tracked via edge detection.
    /// WARNING: Toggle state is tracked locally and can drift from firmware state.
    /// See <see cref="_toggledLayers"/> and design doc for limitations.
    /// </summary>
    private Dictionary<(int Row, int Col), int> _toggleLayerCache = new();

    /// <summary>
    /// Previous matrix state, used for edge detection on toggle keys.
    /// A toggle fires on the rising edge (key was not pressed → now pressed).
    /// </summary>
    private bool[,]? _previousMatrixState;

    /// <summary>
    /// Locally tracked set of toggled-on layers. Flipped on each TG key press.
    /// WARNING: This is a best-effort mirror of firmware state. It can drift if:
    /// - The app starts while a layer is already toggled on
    /// - A matrix poll is missed (e.g., very fast double-tap within one poll cycle)
    /// - Firmware has complex layer logic (combos, macros) that we can't see
    /// Use ResetLayerState command to re-sync to base layer.
    /// </summary>
    private readonly HashSet<int> _toggledLayers = new();

    /// <summary>
    /// Tracks when each momentary layer key was first detected as pressed.
    /// Used to implement hold threshold: the key must be held for at least
    /// <see cref="LayerHoldThresholdMs"/> before the layer switch activates.
    /// This prevents brief taps on dual-function keys (e.g., LT — tap for Enter,
    /// hold for layer) from causing the visualization to flicker.
    /// </summary>
    private readonly Dictionary<(int Row, int Col), long> _momentaryPressTimestamps = new();

    [ObservableProperty]
    private int _layerHoldThresholdMs = 200;

    /// <summary>
    /// Tapping term read from the device via QMK Settings protocol (setting 0x0007).
    /// Null if the device doesn't support QMK settings or the value couldn't be read.
    /// Displayed in the settings UI so users know what their device is configured to.
    /// </summary>
    [ObservableProperty]
    private int? _deviceTappingTermMs;

    public bool IsLinux { get; } = OperatingSystem.IsLinux();

    /// <summary>Diagnostics log for matrix polling events. Shared with the diagnostics popup.</summary>
    public DiagnosticsViewModel Diagnostics { get; } = new();

    /// <summary>Callback to open the diagnostics window. Wired up by App.axaml.cs.</summary>
    public Action? OpenDiagnosticsRequested { get; set; }

    [ObservableProperty]
    private KeyboardConfig? _keyboardConfig;

    [ObservableProperty]
    private int _selectedLayerIndex;

    public LayerViewModel? SelectedLayer => Layers.FirstOrDefault(l => l.Index == SelectedLayerIndex);

    partial void OnSelectedLayerIndexChanged(int value) => OnPropertyChanged(nameof(SelectedLayer));

    [ObservableProperty]
    private string _statusMessage = Loc.Instance["Status_LookingForDevice"];

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isAlwaysOnTop;

    [ObservableProperty]
    private bool _isLiveHighlightingEnabled;

    [ObservableProperty]
    private bool _isAutoLayerSwitchEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasWidth))]
    [NotifyPropertyChangedFor(nameof(CanvasHeight))]
    [NotifyPropertyChangedFor(nameof(LeftHandX))]
    [NotifyPropertyChangedFor(nameof(LeftHandY))]
    [NotifyPropertyChangedFor(nameof(RightHandX))]
    [NotifyPropertyChangedFor(nameof(RightHandY))]
    private bool _isVerticalLayout;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftHandX))]
    [NotifyPropertyChangedFor(nameof(LeftHandY))]
    [NotifyPropertyChangedFor(nameof(RightHandX))]
    [NotifyPropertyChangedFor(nameof(RightHandY))]
    private string _verticalLayoutTopHand = "Left";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoardBackground))]
    [NotifyPropertyChangedFor(nameof(TabBackground))]
    private double _backgroundOpacity;

    /// <summary>Background color for the board area, with alpha from the slider.</summary>
    public string BoardBackground
    {
        get
        {
            var alpha = (int)(BackgroundOpacity * 255);
            return $"#{alpha:X2}181825";
        }
    }

    /// <summary>Background color for the layer tabs, blending base transparency with the slider.</summary>
    public string TabBackground
    {
        get
        {
            // Base is #66181825 (40% alpha). Slider adds on top, up to fully solid.
            var baseAlpha = 0x66;
            var alpha = Math.Min(255, baseAlpha + (int)(BackgroundOpacity * (255 - baseAlpha)));
            return $"#{alpha:X2}181825";
        }
    }

    /// <summary>Canvas width in pixels, changes based on vertical layout mode.</summary>
    public double CanvasWidth => IsVerticalLayout
        ? SvalboardLayout.HandWidth * SvalboardLayout.Scale
        : (SvalboardLayout.HandWidth * 2 + SvalboardLayout.HandGap) * SvalboardLayout.Scale;

    /// <summary>Canvas height in pixels, changes based on vertical layout mode.</summary>
    public double CanvasHeight => IsVerticalLayout
        ? (SvalboardLayout.HandHeight * 2 + 1.0) * SvalboardLayout.Scale
        : SvalboardLayout.HandHeight * SvalboardLayout.Scale;

    // --- Hand container positioning (in pixels) ---

    public double LeftHandX => 0;

    public double LeftHandY => IsVerticalLayout && VerticalLayoutTopHand == "Right"
        ? (SvalboardLayout.HandHeight + 1.0) * SvalboardLayout.Scale
        : 0;

    public double RightHandX => IsVerticalLayout
        ? 0
        : SvalboardLayout.RightHandOriginX * SvalboardLayout.Scale;

    public double RightHandY => IsVerticalLayout && VerticalLayoutTopHand != "Right"
        ? (SvalboardLayout.HandHeight + 1.0) * SvalboardLayout.Scale
        : 0;

    [ObservableProperty]
    private ObservableCollection<LayerViewModel> _layers = [];

    public IReadOnlyList<ClusterViewModel> Clusters { get; } = ClusterViewModel.BuildFromLayout();

    /// <summary>Callback to show/focus the main window. Wired up by App.axaml.cs.</summary>
    public Action? ShowWindowRequested { get; set; }

    /// <summary>Callback to toggle window visibility. Wired up by App.axaml.cs.</summary>
    public Action? ToggleWindowRequested { get; set; }

    /// <summary>Callback to open the settings window. Wired up by App.axaml.cs.</summary>
    public Action? OpenSettingsRequested { get; set; }

    /// <summary>Callback to open the export dialog. Wired up by App.axaml.cs.</summary>
    public Action? OpenExportRequested { get; set; }

    /// <summary>Callback to open the help window. Wired up by App.axaml.cs.</summary>
    public Action? OpenHelpRequested { get; set; }

    /// <summary>Callback when hotkey settings change. Wired up by App.axaml.cs. Args: (key, modifiers).</summary>
    public Action<string, string>? HotkeyChangeRequested { get; set; }

    /// <summary>Callback to show a label editor for a key. Wired up by App.axaml.cs. Args: KeyViewModel.</summary>
    public Action<KeyViewModel>? SetKeyLabelRequested { get; set; }

    /// <summary>Invoked by QuitCommand so the App layer can save window state and shut down cleanly.</summary>
    public Action? QuitRequested { get; set; }

    public MainWindowViewModel(ISettingsService? settingsService = null)
    {
        _settingsService = settingsService ?? new SettingsService();
        var initialSettings = _settingsService.Load();
        IsAlwaysOnTop = initialSettings.AlwaysOnTop;
        BackgroundOpacity = Math.Clamp(initialSettings.BackgroundOpacity, 0.0, 1.0);
        VerticalLayoutTopHand = initialSettings.VerticalLayoutTopHand ?? "Left";
        IsVerticalLayout = initialSettings.VerticalLayout;
    }

    /// <summary>
    /// Deferred initialization: connects to device and starts monitoring.
    /// Call after the window is shown to avoid blocking the UI thread during HID enumeration.
    /// </summary>
    public void InitializeDeviceConnection()
    {
        _ = TryConnectAsync();
        _deviceSubscription = _deviceService.OnDeviceListChanged(() => _ = TryConnectAsync());
    }

    private async Task TryConnectAsync()
    {
        // Prevent concurrent connection attempts — HidSharp fires bursts of
        // device-changed events and concurrent HID reads corrupt each other.
        if (_isConnecting) return;
        _isConnecting = true;
        _connectCts?.Cancel();
        _connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = _connectCts.Token;

        try
        {
            StopMatrixPolling();
            StopLedPolling();

            var settings = _settingsService.Load();
            StatusMessage = Loc.Instance["Status_LookingForDevice"];
            StartupLogger.Log("Device enumeration starting...");

            // Run ALL heavy I/O on a background thread with a timeout.
            // HidSharp enumeration can hang on some Windows configurations,
            // and the Vial protocol exchange (connect, read keymap, decompress)
            // can also block for extended periods with certain devices.
            var result = await Task.Run(() =>
            {
                var devices = _deviceService.FindVialDevices();
                StartupLogger.Log($"Device enumeration complete: {devices.Count} device(s) found");

                if (devices.Count == 0) return null;

                var device = PickPreferredDevice(devices);
                StartupLogger.Log($"Selected device: {device.ProductName} (out of {devices.Count})");

                var loader = new KeymapLoader(_protocolService, _keycodeService);
                StartupLogger.Log($"Loading keymap from {device.ProductName}...");
                var config = loader.Load(device, settings);
                StartupLogger.Log("Keymap loaded successfully");

                var tappingTerm = _protocolService.GetQmkSetting(VialCommands.QmkSettingTappingTerm);

                // Capability check: does the firmware respond to VIA rgblight
                // color queries? If yes, we can drive the active layer from the
                // LED instead of the matrix-based heuristic.
                var ledProbe = _protocolService.GetCurrentLedHueSat();

                return new
                {
                    Device = device,
                    Config = config,
                    TappingTerm = tappingTerm,
                    LedPollSupported = ledProbe is not null
                };
            }, ct);

            // Back on UI thread — safe to update observable properties and collections
            if (result is null)
            {
                StatusMessage = Loc.Instance["Status_NoDeviceFound"];
                IsConnected = false;
                return;
            }

            KeyboardConfig = result.Config;
            _ledPollSupported = result.LedPollSupported;

            BuildLayerViewModels(settings);
            BuildLayerSwitchCache();

            // Force property change notification even if already 0 (default),
            // so the UI picks up the now-valid SelectedLayer after async connect.
            SelectedLayerIndex = -1;
            SelectedLayerIndex = 0;
            IsConnected = true;
            IsLiveHighlightingEnabled = settings.LiveKeyHighlighting;
            IsAutoLayerSwitchEnabled = settings.AutoLayerSwitch;

            if (result.TappingTerm.HasValue && result.TappingTerm.Value is > 0 and <= 1000)
            {
                DeviceTappingTermMs = result.TappingTerm.Value;
                if (settings.LayerHoldThresholdMs == 200)
                    LayerHoldThresholdMs = result.TappingTerm.Value;
                else
                    LayerHoldThresholdMs = Math.Clamp(settings.LayerHoldThresholdMs, 0, 1000);
            }
            else
            {
                DeviceTappingTermMs = null;
                LayerHoldThresholdMs = Math.Clamp(settings.LayerHoldThresholdMs, 0, 1000);
            }

            BackgroundOpacity = Math.Clamp(settings.BackgroundOpacity, 0.0, 1.0);
            _connectedDeviceName = result.Device.ProductName;
            StatusMessage = Loc.Instance.Format("Status_ConnectedFormat", result.Device.ProductName, KeyboardConfig.Layers.Count);

            if (IsLiveHighlightingEnabled)
                StartMatrixPolling();

            if (_ledPollSupported && IsAutoLayerSwitchEnabled)
                StartLedPolling();
        }
        catch (OperationCanceledException)
        {
            StartupLogger.Log("Device connection timed out after 30s");
            StatusMessage = Loc.Instance["Status_DeviceSearchTimedOut"];
            IsConnected = false;
        }
        catch (Exception ex)
        {
            StopMatrixPolling();
            StartupLogger.Log($"Device connection error: {ex.Message}");
            StatusMessage = Loc.Instance.Format("Status_ConnectionErrorFormat", ex.Message);
            IsConnected = false;
        }
        finally
        {
            _isConnecting = false;
        }
    }

    /// <summary>
    /// Prefers a Svalboard ("lightly") device when multiple Vial devices are connected.
    /// Falls back to the first device if no Svalboard is found.
    /// </summary>
    private static DeviceInfo PickPreferredDevice(IReadOnlyList<DeviceInfo> devices)
    {
        var preferred = devices.FirstOrDefault(d =>
            d.ProductName.StartsWith("lightly", StringComparison.OrdinalIgnoreCase));
        return preferred ?? devices[0];
    }

    private void BuildLayerViewModels(UserSettings settings)
    {
        Layers.Clear();
        if (KeyboardConfig is null) return;

        var totalLayers = KeyboardConfig.Layers.Count;
        var userColors = settings.LayerColors.Count > 0 ? settings.LayerColors : null;

        // Device-HSV map keyed by Layer.Index, so KeyViewModel can resolve the
        // *target* layer's firmware color when rendering layer-switch keys.
        var deviceLayerColors = KeyboardConfig.Layers
            .ToDictionary(l => l.Index, l => (l.ColorHue, l.ColorSat, l.ColorVal))
            as IReadOnlyDictionary<int, (byte? H, byte? S, byte? V)>;

        foreach (var layer in KeyboardConfig.Layers)
        {
            // Skip layers where every key is empty (KC_NO)
            if (layer.Keys.All(k => k.RawKeycode == 0x0000))
                continue;

            Layers.Add(new LayerViewModel(layer, i => SelectedLayerIndex = i, totalLayers, userColors,
                keyVm => SetKeyLabelRequested?.Invoke(keyVm), deviceLayerColors));
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
        BackgroundOpacity = Math.Clamp(settings.BackgroundOpacity, 0.0, 1.0);
        LayerHoldThresholdMs = Math.Clamp(settings.LayerHoldThresholdMs, 0, 1000);
        VerticalLayoutTopHand = settings.VerticalLayoutTopHand ?? "Left";
        IsVerticalLayout = settings.VerticalLayout;

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
        // Force property-changed even if index unchanged, so the UI rebinds
        SelectedLayerIndex = -1;
        SelectedLayerIndex = currentLayer;

        // Refresh status message for new language
        RefreshStatusMessage();
    }

    /// <summary>Re-generates StatusMessage in the current locale.</summary>
    private void RefreshStatusMessage()
    {
        if (IsConnected && KeyboardConfig is not null && _connectedDeviceName is not null)
        {
            var status = Loc.Instance.Format("Status_ConnectedFormat", _connectedDeviceName, KeyboardConfig.Layers.Count);
            if (IsLiveHighlightingEnabled)
                status += Loc.Instance["Status_LiveKeysOn"];
            StatusMessage = status;
        }
        else if (!IsConnected)
        {
            StatusMessage = Loc.Instance["Status_NoDeviceFound"];
        }
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

    private void StartMatrixPolling()
    {
        StopMatrixPolling();
        if (KeyboardConfig is null) return;

        _matrixPolling = new MatrixPollingService(
            _protocolService, KeyboardConfig.MatrixRows, KeyboardConfig.MatrixCols);
        _matrixPolling.MatrixStateChanged += OnMatrixStateChanged;
        _matrixPolling.PollError += msg =>
            Dispatcher.UIThread.Post(() => StatusMessage = Loc.Instance.Format("Status_PollingErrorFormat", msg));
        _matrixPolling.Start();
        RefreshStatusMessage();
    }

    private void StopMatrixPolling()
    {
        if (_matrixPolling is null) return;
        _matrixPolling.MatrixStateChanged -= OnMatrixStateChanged;
        _matrixPolling.Dispose();
        _matrixPolling = null;

        // Clear all pressed states
        ClearPressedStates();
    }

    private void StartLedPolling()
    {
        StopLedPolling();
        if (KeyboardConfig is null || !_ledPollSupported) return;

        _ledPolling = new LedPollingService(_protocolService);
        _ledPolling.LedColorChanged += OnLedColorChanged;
        _ledPolling.PollError += msg =>
            Dispatcher.UIThread.Post(() => StatusMessage = Loc.Instance.Format("Status_PollingErrorFormat", msg));
        _ledPolling.Start();
    }

    private void StopLedPolling()
    {
        if (_ledPolling is null) return;
        _ledPolling.LedColorChanged -= OnLedColorChanged;
        _ledPolling.Dispose();
        _ledPolling = null;
    }

    /// <summary>
    /// Called on the polling thread whenever the device's rgblight hue+sat
    /// changes. Resolves the closest matching layer and, if auto-switch is on,
    /// updates SelectedLayerIndex on the UI thread.
    /// </summary>
    private void OnLedColorChanged(byte hue, byte sat)
    {
        if (KeyboardConfig is null) return;

        var match = LedColorLayerResolver.Resolve(
            KeyboardConfig.Layers, hue, sat, SelectedLayerIndex);
        if (match is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (IsAutoLayerSwitchEnabled && match.Value != SelectedLayerIndex)
                SelectedLayerIndex = match.Value;
        });
    }

    /// <summary>
    /// Builds lookups of (row, col) → target layer from layer-switch keys on layer 0.
    /// Separates momentary (hold) keys from toggle (press) keys.
    /// Called once on connect — the keymap doesn't change while connected.
    /// </summary>
    private void BuildLayerSwitchCache()
    {
        _momentaryLayerCache.Clear();
        _toggleLayerCache.Clear();
        _toggledLayers.Clear();
        _momentaryPressTimestamps.Clear();
        _previousMatrixState = null;
        if (KeyboardConfig is null) return;

        var baseLayer = KeyboardConfig.Layers.FirstOrDefault(l => l.Index == 0);
        if (baseLayer is null) return;

        foreach (var key in baseLayer.Keys)
        {
            if (!key.IsLayerSwitch || !key.TargetLayer.HasValue) continue;

            switch (key.SwitchType)
            {
                case LayerSwitchType.Momentary:
                    _momentaryLayerCache[(key.Row, key.Col)] = key.TargetLayer.Value;
                    break;
                case LayerSwitchType.Toggle:
                    _toggleLayerCache[(key.Row, key.Col)] = key.TargetLayer.Value;
                    break;
                // Activate (TO, DF) and OneShot (OSL) are not tracked — would need
                // firmware-side state to handle reliably.
            }
        }
    }

    private void OnMatrixStateChanged(bool[,] state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Update pressed states and collect pressed keys for diagnostics
            var pressedKeys = new List<(KeyViewModel Key, string LayerName)>();
            foreach (var layerVm in Layers)
            {
                foreach (var keyVm in layerVm.Keys)
                {
                    var pressed = state[keyVm.Key.Row, keyVm.Key.Col];
                    keyVm.IsPressed = pressed;
                    if (pressed)
                        pressedKeys.Add((keyVm, layerVm.DisplayName));
                }
            }

            Diagnostics.LogMatrixEvent(pressedKeys);

            // Auto-switch layer based on held/toggled layer-switch keys.
            // Skipped when LED polling is active — the LED is authoritative
            // and catches layers the matrix heuristic can't see (mouse layer,
            // firmware-internal switches, etc.).
            if (IsAutoLayerSwitchEnabled && !_ledPollSupported)
            {
                // 1. Detect toggle edges: TG key was NOT pressed last poll, IS pressed now → flip
                if (_previousMatrixState is not null)
                {
                    foreach (var (pos, layer) in _toggleLayerCache)
                    {
                        var wasPressed = _previousMatrixState[pos.Row, pos.Col];
                        var isPressed = state[pos.Row, pos.Col];
                        if (isPressed && !wasPressed)
                        {
                            // Rising edge — toggle this layer
                            if (!_toggledLayers.Remove(layer))
                                _toggledLayers.Add(layer);
                        }
                    }
                }

                // Save state for next edge detection
                _previousMatrixState = (bool[,])state.Clone();

                // 2. Update momentary hold timestamps and resolve with threshold
                var now = Environment.TickCount64;
                foreach (var (pos, _) in _momentaryLayerCache)
                {
                    if (state[pos.Row, pos.Col])
                    {
                        // Track when the key was first pressed
                        _momentaryPressTimestamps.TryAdd(pos, now);
                    }
                    else
                    {
                        // Key released — clear timestamp
                        _momentaryPressTimestamps.Remove(pos);
                    }
                }

                // Resolve active layer: highest of (momentary held past threshold | toggled on)
                var targetLayer = 0;
                foreach (var (pos, layer) in _momentaryLayerCache)
                {
                    if (!state[pos.Row, pos.Col]) continue;
                    if (!_momentaryPressTimestamps.TryGetValue(pos, out var pressedAt)) continue;
                    var heldMs = now - pressedAt;
                    if (heldMs >= LayerHoldThresholdMs && layer > targetLayer)
                        targetLayer = layer;
                }
                foreach (var layer in _toggledLayers)
                {
                    if (layer > targetLayer)
                        targetLayer = layer;
                }

                if (targetLayer != SelectedLayerIndex)
                    SelectedLayerIndex = targetLayer;
            }
        });
    }

    private void ClearPressedStates()
    {
        foreach (var layerVm in Layers)
            foreach (var keyVm in layerVm.Keys)
                keyVm.IsPressed = false;
    }

    /// <summary>
    /// Resets locally tracked toggle state and returns to base layer.
    /// Use this when the visualization gets out of sync with the actual keyboard layer.
    /// This can happen because toggle (TG) tracking is a best-effort local mirror —
    /// see _toggledLayers field documentation for details.
    /// </summary>
    [RelayCommand]
    private void ResetLayerState()
    {
        _toggledLayers.Clear();
        _momentaryPressTimestamps.Clear();
        _previousMatrixState = null;
        SelectedLayerIndex = 0;
    }

    [RelayCommand]
    private void ToggleAutoLayerSwitch()
    {
        IsAutoLayerSwitchEnabled = !IsAutoLayerSwitchEnabled;

        if (IsConnected && _ledPollSupported)
        {
            if (IsAutoLayerSwitchEnabled)
                StartLedPolling();
            else
                StopLedPolling();
        }

        var settings = _settingsService.Load();
        _settingsService.Save(settings with { AutoLayerSwitch = IsAutoLayerSwitchEnabled });
    }

    [RelayCommand]
    private void ToggleLiveHighlighting()
    {
        IsLiveHighlightingEnabled = !IsLiveHighlightingEnabled;

        if (IsLiveHighlightingEnabled && IsConnected)
            StartMatrixPolling();
        else
            StopMatrixPolling();

        RefreshStatusMessage();

        var settings = _settingsService.Load();
        _settingsService.Save(settings with { LiveKeyHighlighting = IsLiveHighlightingEnabled });
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
    private void OpenDiagnostics()
    {
        OpenDiagnosticsRequested?.Invoke();
    }

    [RelayCommand]
    private void Export()
    {
        OpenExportRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenHelp()
    {
        OpenHelpRequested?.Invoke();
    }

    [RelayCommand]
    private void ToggleVerticalLayout()
    {
        IsVerticalLayout = !IsVerticalLayout;
        var settings = _settingsService.Load();
        _settingsService.Save(settings with { VerticalLayout = IsVerticalLayout });
    }

    [RelayCommand]
    private void TogglePin()
    {
        IsAlwaysOnTop = !IsAlwaysOnTop;
        var settings = _settingsService.Load();
        _settingsService.Save(settings with { AlwaysOnTop = IsAlwaysOnTop });
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await TryConnectAsync();
    }

    [RelayCommand]
    private void Quit()
    {
        StopMatrixPolling();
        StopLedPolling();
        _deviceSubscription?.Dispose();
        _protocolService.Dispose();
        if (QuitRequested is not null)
            QuitRequested.Invoke();
        else
            Environment.Exit(0);
    }
}
