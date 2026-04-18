using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.App.Localization;
using SvalboardLayerViz.Core.Device;
using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.History;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Models;
using SvalboardLayerViz.Core.Protocol;
using SvalboardLayerViz.Core.Macros;
using SvalboardLayerViz.Core.Settings;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// Carries context from MainWindow to the key picker dialog so the App layer
/// can construct a PickerSessionViewModel and hand back the result.
/// </summary>
public record KeyEditRequest(
    int Layer,
    int Row,
    int Col,
    ushort CurrentCode,
    string CurrentLabel,
    IReadOnlyList<CustomKeycode> CustomKeycodes,
    IReadOnlyList<LayerOption> LayerOptions,
    Action<ushort> OnApply);

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IDeviceConnectionService _deviceService;
    private readonly IVialProtocolService _protocolService;
    private readonly KeycodeService _keycodeService;
    private readonly ISettingsService _settingsService;
    private readonly ISnapshotService _snapshotService;

    /// <summary>Protocol service exposed for dialogs (unlock flow) that need direct device access.</summary>
    public IVialProtocolService ProtocolService => _protocolService;
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
    /// Owns momentary/toggle layer caches, edge detection, and hold-threshold
    /// state. Rebuilt from layer 0 on connect; reset via ResetLayerState.
    /// WARNING: Toggle state is a best-effort local mirror of firmware; see
    /// design doc for drift sources (fast double-taps, firmware-side combos).
    /// </summary>
    private readonly LayerSwitchService _layerSwitch = new();

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

    /// <summary>True when the user is editing the keymap. Disables polling and enables key-click-to-edit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleLivePolling))]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    private bool _isEditMode;

    /// <summary>Number of unsaved pending changes in the current edit session.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    private int _dirtyCount;

    /// <summary>True while <see cref="SaveEdit"/> is mid-flight. Disables the button and re-entry.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    private bool _isSaving;

    /// <summary>
    /// Remembers whether the device was locked when the user entered edit mode.
    /// Used to decide whether to re-lock after a successful save: if the user
    /// had to go through the unlock dialog to edit, we put the device back the
    /// way we found it. If they started unlocked (e.g. firmware shipped
    /// unlocked, or they unlocked in Vial earlier), we leave it alone.
    /// </summary>
    private bool _wasLockedOnEnterEdit;

    /// <summary>
    /// BackgroundOpacity captured on entering edit mode so it can be restored on exit.
    /// Edit mode forces the background fully opaque (solid) to maximize contrast while
    /// the user is clicking keys; the original transparency comes back on Discard/Save.
    /// </summary>
    private double? _preEditBackgroundOpacity;

    /// <summary>Cancellation source for an in-flight save. Null when not saving.</summary>
    private CancellationTokenSource? _saveCts;

    /// <summary>True only when live polling can be toggled — blocked while editing to avoid interaction conflicts.</summary>
    public bool CanToggleLivePolling => !IsEditMode;

    /// <summary>In-memory edit state while <see cref="IsEditMode"/> is true; null otherwise.</summary>
    private KeymapEditSession? _editSession;

    /// <summary>Exposed for tests to verify pending-change state.</summary>
    public KeymapEditSession? EditSession => _editSession;

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

    /// <summary>Callback to show/focus the main window. Wired up by App.axaml.cs.</summary>
    public Action? ShowWindowRequested { get; set; }

    /// <summary>Callback to toggle window visibility. Wired up by App.axaml.cs.</summary>
    public Action? ToggleWindowRequested { get; set; }

    /// <summary>Callback to open the settings window. Wired up by App.axaml.cs.</summary>
    public Action<int?>? OpenSettingsRequested { get; set; }

    /// <summary>Callback to open the snapshot history window. Wired up by App.axaml.cs.</summary>
    public Action? OpenHistoryRequested { get; set; }

    /// <summary>Snapshot service used by both this VM and the History window.</summary>
    public ISnapshotService SnapshotService => _snapshotService;

    /// <summary>Current keyboard's identity, if connected.</summary>
    public KeyboardId? CurrentKeyboardId =>
        KeyboardConfig is null ? null : GetKeyboardId();

    /// <summary>Callback to open the macro editor dialog. Wired up by App.axaml.cs.</summary>
    public Action? OpenMacrosRequested { get; set; }

    /// <summary>Callback to open the combo editor dialog. Wired up by App.axaml.cs.</summary>
    public Action? OpenCombosRequested { get; set; }

    /// <summary>Callback to open the tap-dance editor dialog. Wired up by App.axaml.cs.</summary>
    public Action? OpenTapDanceRequested { get; set; }

    /// <summary>
    /// Fired when deferred macro load completes. Lets an open macro editor
    /// dialog update its slots from the freshly loaded data.
    /// </summary>
    public event Action<MacroBuffer>? MacrosLoaded;

    /// <summary>Callback to open the export dialog. Wired up by App.axaml.cs.</summary>
    public Action? OpenExportRequested { get; set; }

    /// <summary>Callback to open the help window. Wired up by App.axaml.cs.</summary>
    public Action? OpenHelpRequested { get; set; }

    /// <summary>Callback when hotkey settings change. Wired up by App.axaml.cs. Args: (key, modifiers).</summary>
    public Action<string, string>? HotkeyChangeRequested { get; set; }

    /// <summary>Callback to show a label editor for a key. Wired up by App.axaml.cs. Args: KeyViewModel.</summary>
    public Action<KeyViewModel>? SetKeyLabelRequested { get; set; }

    /// <summary>Callback to open the Vial unlock dialog. Wired up by App.axaml.cs. Arg: success callback to run on unlock.</summary>
    public Action<Action>? OpenUnlockRequested { get; set; }

    /// <summary>Callback to open the key picker dialog. Wired up by App.axaml.cs.</summary>
    public Action<KeyEditRequest>? OpenKeyPickerRequested { get; set; }

    /// <summary>
    /// Pre-save confirmation hook. Called when <see cref="PreSaveSafetyCheck"/>
    /// returns a non-empty warning list. Return <c>true</c> to proceed, <c>false</c>
    /// to abort. Null means auto-confirm (tests; also, the first-cut run before
    /// the dialog view is wired up).
    /// </summary>
    public Func<IReadOnlyList<SafetyWarning>, Task<bool>>? ConfirmSafetyWarningsRequested { get; set; }

    /// <summary>
    /// Fired at the end of every <see cref="SaveEdit"/> run with the final
    /// outcome so the App layer can surface a modal for partials / errors.
    /// Optional; status-message updates happen regardless.
    /// </summary>
    public Action<SaveResult>? SaveCompletedCallback { get; set; }

    /// <summary>Invoked by QuitCommand so the App layer can save window state and shut down cleanly.</summary>
    public Action? QuitRequested { get; set; }

    public MainWindowViewModel(
        ISettingsService? settingsService = null,
        IVialProtocolService? protocolService = null,
        ISnapshotService? snapshotService = null,
        IDeviceConnectionService? deviceService = null,
        KeycodeService? keycodeService = null)
    {
        _settingsService = settingsService ?? new SettingsService();
        _protocolService = protocolService ?? new VialProtocolService();
        _snapshotService = snapshotService ?? new SnapshotService();
        _deviceService = deviceService ?? new DeviceConnectionService();
        _keycodeService = keycodeService ?? new KeycodeService();
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

    /// <remarks>
    /// CTS lifecycle: dispose-and-replace on entry (assign new before disposing
    /// old so concurrent observers never see null mid-swap), then dispose in
    /// finally only if this attempt's CTS is still the field's current value.
    /// Prevents leaking one CancellationTokenSource per device-list event.
    /// Internal so lifecycle tests can await a connect attempt directly
    /// instead of fire-and-forget through InitializeDeviceConnection.
    /// </remarks>
    internal async Task TryConnectAsync()
    {
        // Prevent concurrent connection attempts — HidSharp fires bursts of
        // device-changed events and concurrent HID reads corrupt each other.
        if (_isConnecting) return;
        _isConnecting = true;

        var oldCts = _connectCts;
        var attemptCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _connectCts = attemptCts;
        oldCts?.Cancel();
        oldCts?.Dispose();
        var ct = attemptCts.Token;

        try
        {
            StopMatrixPolling();
            StopLedPolling();

            var settings = _settingsService.Load();
            StatusMessage = Loc.Instance["Status_LookingForDevice"];
            DiagnosticLog.Info("Device", "Device enumeration starting...");

            // Run ALL heavy I/O on a background thread with a timeout.
            // HidSharp enumeration can hang on some Windows configurations,
            // and the Vial protocol exchange (connect, read keymap, decompress)
            // can also block for extended periods with certain devices.
            var session = await Task.Run(
                () => DeviceSession.Connect(_deviceService, _protocolService, _keycodeService, settings), ct);

            // Back on UI thread — safe to update observable properties and collections
            if (session is null)
            {
                StatusMessage = Loc.Instance["Status_NoDeviceFound"];
                IsConnected = false;
                return;
            }

            KeyboardConfig = session.Config;
            _ledPollSupported = session.LedPollSupported;

            BuildLayerViewModels(settings);
            BuildLayerSwitchCache();

            // Force property change notification even if already 0 (default),
            // so the UI picks up the now-valid SelectedLayer after async connect.
            SelectedLayerIndex = -1;
            SelectedLayerIndex = 0;
            IsConnected = true;
            IsLiveHighlightingEnabled = settings.LiveKeyHighlighting;
            IsAutoLayerSwitchEnabled = settings.AutoLayerSwitch;

            if (session.TappingTerm is > 0 and <= 1000)
            {
                DeviceTappingTermMs = session.TappingTerm.Value;
                if (settings.LayerHoldThresholdMs == 200)
                    LayerHoldThresholdMs = session.TappingTerm.Value;
                else
                    LayerHoldThresholdMs = Math.Clamp(settings.LayerHoldThresholdMs, 0, 1000);
            }
            else
            {
                DeviceTappingTermMs = null;
                LayerHoldThresholdMs = Math.Clamp(settings.LayerHoldThresholdMs, 0, 1000);
            }

            BackgroundOpacity = Math.Clamp(settings.BackgroundOpacity, 0.0, 1.0);
            _connectedDeviceName = session.Device.ProductName;
            StatusMessage = Loc.Instance.Format("Status_ConnectedFormat", session.Device.ProductName, KeyboardConfig.Layers.Count);

            await TryCaptureFirstConnectSnapshotAsync();

            // Deferred macro load — non-blocking, UI is already up.
            _ = LoadMacrosAsync();

            if (IsLiveHighlightingEnabled && !IsEditMode)
                StartMatrixPolling();

            if (_ledPollSupported && IsAutoLayerSwitchEnabled && !IsEditMode)
                StartLedPolling();
        }
        catch (OperationCanceledException)
        {
            DiagnosticLog.Warn("Device", "Device connection timed out after 30s");
            StatusMessage = Loc.Instance["Status_DeviceSearchTimedOut"];
            IsConnected = false;
        }
        catch (Exception ex)
        {
            StopMatrixPolling();
            DiagnosticLog.Error("Device", $"Device connection error: {ex.Message}");
            StatusMessage = Loc.Instance.Format("Status_ConnectionErrorFormat", ex.Message);
            IsConnected = false;
        }
        finally
        {
            _isConnecting = false;
            // Only dispose if a newer attempt hasn't already supplanted us via
            // the dispose-and-replace path above; in that case the newer attempt
            // already disposed our CTS as its `oldCts`.
            if (ReferenceEquals(_connectCts, attemptCts))
            {
                attemptCts.Dispose();
                _connectCts = null;
            }
        }
    }

    /// <summary>
    /// Loads macro buffer from device in the background after connection.
    /// Updates KeyboardConfig.Macros when done — non-blocking for startup.
    /// </summary>
    private async Task LoadMacrosAsync()
    {
        if (KeyboardConfig is null || !IsConnected) return;
        try
        {
            var macros = await Task.Run(() =>
            {
                var count = _protocolService.GetMacroCount();
                var bufSize = _protocolService.GetMacroBufferSize();
                if (count <= 0 || bufSize <= 0)
                {
                    DiagnosticLog.Info("Proto", $"Macros skipped: count={count} bufferSize={bufSize}");
                    return (MacroBuffer?)null;
                }
                var raw = _protocolService.GetMacroBuffer(bufSize);
                var decoded = MacroCodec.Decode(raw, count, bufSize);
                DiagnosticLog.Info("Proto",
                    $"Macros loaded: {decoded.Macros.Count} slots, {decoded.UsedBytes}/{decoded.BufferCapacity} bytes used");
                return decoded;
            });

            if (KeyboardConfig is not null && macros is not null)
            {
                KeyboardConfig = KeyboardConfig with { Macros = macros };

                // If already in edit mode, patch the session's macro baseline
                // so ApplyMacroEdit has a non-null buffer to work with.
                if (_editSession?.GetCurrentMacroBuffer() is null)
                {
                    var encoded = MacroCodec.Encode(macros);
                    _editSession?.SetMacroBaseline(encoded);
                }

                // Clear "still loading" status if it's showing
                if (StatusMessage == Loc.Instance["Edit_MacroLoading"])
                    StatusMessage = "";

                MacrosLoaded?.Invoke(macros);
                UpdateMacroPreviews(macros);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("Proto", $"Deferred macro load failed (non-fatal): {ex.Message}");
        }
    }

    private void UpdateMacroPreviews(MacroBuffer macros)
    {
        var previews = macros.Macros
            .Select(m => MacroPreviewHelper.GetPreview(m.Actions, maxLength: 12) ?? "")
            .ToList();
        _keycodeService.SetMacroPreviews(previews);
        RefreshAllKeyLabels();
    }

    /// <summary>
    /// Re-resolves display labels for all keys in all layers using the current
    /// KeycodeService state. Called when macro previews or custom labels change.
    /// Skips the expensive VM rebuild when Core tells us nothing actually changed.
    /// </summary>
    private void RefreshAllKeyLabels()
    {
        if (KeyboardConfig is null) return;

        var updated = KeyboardConfig.WithResolvedLabels(_keycodeService, layerNames: null);
        if (ReferenceEquals(updated, KeyboardConfig))
            return;

        KeyboardConfig = updated;

        var settings = _settingsService.Load();
        var currentLayer = SelectedLayerIndex;
        BuildLayerViewModels(settings);
        SelectedLayerIndex = -1;
        SelectedLayerIndex = currentLayer;
    }

    internal void BuildLayerViewModels(UserSettings settings)
    {
        Layers.Clear();
        if (KeyboardConfig is null) return;

        var userColors = settings.LayerColors.Count > 0 ? settings.LayerColors : null;
        var palette = new LayerColorPalette(KeyboardConfig.Layers, userColors);

        foreach (var layer in KeyboardConfig.Layers)
        {
            // Skip layers where every key is empty (KC_NO) — but keep them
            // visible in edit mode so the user can populate empty layers.
            if (!IsEditMode && layer.Keys.All(k => k.RawKeycode == 0x0000))
                continue;

            Layers.Add(new LayerViewModel(layer, palette,
                selectLayer: i => SelectedLayerIndex = i,
                setLabelRequested: keyVm => SetKeyLabelRequested?.Invoke(keyVm)));
        }

        // Wire per-key click routing so edit-mode clicks flow through OnKeyClicked.
        foreach (var layerVm in Layers)
        {
            layerVm.SetEditMode(IsEditMode);
            foreach (var keyVm in layerVm.Keys)
                keyVm.OnClickAction = OnKeyClicked;
        }
    }

    /// <summary>
    /// Handles a left-click on a key while in edit mode. Raises the picker dialog request
    /// with a completion callback that applies the chosen keycode to the edit session.
    /// </summary>
    private void OnKeyClicked(KeyViewModel keyVm)
    {
        if (!IsEditMode || _editSession is null) return;

        var layer = keyVm.Layer.Index;
        var row = keyVm.Key.Row;
        var col = keyVm.Key.Col;
        var currentCode = _editSession.GetCurrent(layer, row, col);

        // Use KeyboardConfig.Layers (all device layers) not the filtered Layers VM
        // collection — BuildLayerViewModels drops empty (all-KC_NO) layers, but the
        // picker must expose them so a user can bind MO(5) before layer 5 has any
        // keys set.
        var layerOptions = KeyboardConfig?.Layers
            .Select(l => new LayerOption(l.Index, l.DisplayName))
            .ToList() ?? [];

        var request = new KeyEditRequest(
            Layer: layer,
            Row: row,
            Col: col,
            CurrentCode: currentCode,
            CurrentLabel: keyVm.DisplayLabel,
            CustomKeycodes: KeyboardConfig?.CustomKeycodes ?? [],
            LayerOptions: layerOptions,
            OnApply: newCode => ApplyKeyEdit(layer, row, col, newCode));

        OpenKeyPickerRequested?.Invoke(request);
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

        // Re-resolve custom labels; Core is the single authority on how the
        // keycode → label mapping is produced, so hand it the current map and
        // ask for an updated config back.
        _keycodeService.SetCustomKeyLabels(settings.CustomKeyLabels);
        KeyboardConfig = KeyboardConfig.WithResolvedLabels(_keycodeService, settings.LayerNames);

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
        _matrixPolling.PollError += OnMatrixPollError;
        _matrixPolling.Start();
        RefreshStatusMessage();
    }

    private void OnMatrixPollError(string msg) =>
        Dispatcher.UIThread.Post(() => StatusMessage = Loc.Instance.Format("Status_PollingErrorFormat", msg));

    private void StopMatrixPolling()
    {
        if (_matrixPolling is null) return;
        _matrixPolling.MatrixStateChanged -= OnMatrixStateChanged;
        _matrixPolling.PollError -= OnMatrixPollError;
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
        _ledPolling.PollError += OnLedPollError;
        _ledPolling.Start();
    }

    private void OnLedPollError(string msg) =>
        Dispatcher.UIThread.Post(() => StatusMessage = Loc.Instance.Format("Status_PollingErrorFormat", msg));

    private void StopLedPolling()
    {
        if (_ledPolling is null) return;
        _ledPolling.LedColorChanged -= OnLedColorChanged;
        _ledPolling.PollError -= OnLedPollError;
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
        // Suppress auto-switch while a save is in flight. The save reloads
        // the keymap and flips SelectedLayerIndex itself; racing LED-driven
        // posts can clobber that and leave the UI on the wrong layer.
        if (IsSaving) return;

        var match = LedColorLayerResolver.Resolve(
            KeyboardConfig.Layers, hue, sat, SelectedLayerIndex);
        if (match is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (IsSaving) return;
            if (IsAutoLayerSwitchEnabled && match.Value != SelectedLayerIndex)
                SelectedLayerIndex = match.Value;
        });
    }

    private void BuildLayerSwitchCache() =>
        _layerSwitch.BuildCache(KeyboardConfig?.Layers.FirstOrDefault(l => l.Index == 0));

    private void OnMatrixStateChanged(bool[,] state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var rows = state.GetLength(0);
            var cols = state.GetLength(1);

            // Update pressed states and collect pressed keys for diagnostics
            var pressedKeys = new List<(KeyViewModel Key, string LayerName)>();
            foreach (var layerVm in Layers)
            {
                foreach (var keyVm in layerVm.Keys)
                {
                    if (keyVm.Key.Row >= rows || keyVm.Key.Col >= cols) continue;
                    var pressed = state[keyVm.Key.Row, keyVm.Key.Col];
                    keyVm.IsPressed = pressed;
                    if (pressed)
                        pressedKeys.Add((keyVm, layerVm.DisplayName));
                }
            }

            Diagnostics.LogMatrixEvent(pressedKeys);

            // Skipped when LED polling is active — the LED is authoritative
            // and catches layers the matrix heuristic can't see (mouse layer,
            // firmware-internal switches, etc.).
            if (IsAutoLayerSwitchEnabled && !_ledPollSupported)
            {
                var targetLayer = _layerSwitch.ResolveTargetLayer(
                    state, Environment.TickCount64, LayerHoldThresholdMs);
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
    /// </summary>
    [RelayCommand]
    private void ResetLayerState()
    {
        _layerSwitch.ResetRuntimeState();
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

        if (IsLiveHighlightingEnabled && IsConnected && !IsEditMode)
            StartMatrixPolling();
        else
            StopMatrixPolling();

        RefreshStatusMessage();

        var settings = _settingsService.Load();
        _settingsService.Save(settings with { LiveKeyHighlighting = IsLiveHighlightingEnabled });
    }

    // ============================================================================
    // Edit mode — Session 10
    // ============================================================================

    /// <summary>
    /// Enters edit mode. Checks unlock status first; if locked, opens the unlock dialog
    /// and defers session creation until the user completes the unlock sequence.
    /// </summary>
    [RelayCommand]
    private async Task EnterEdit()
    {
        if (IsEditMode || !IsConnected || KeyboardConfig is null) return;

        UnlockStatus status;
        try
        {
            status = await Task.Run(() => _protocolService.GetUnlockStatus());
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("Edit", $"GetUnlockStatus failed: {ex.Message}");
            StatusMessage = Loc.Instance.Format("Status_EditUnlockErrorFormat", ex.Message);
            return;
        }

        _wasLockedOnEnterEdit = !status.Unlocked;

        if (status.Unlocked)
        {
            BeginEditSession();
        }
        else
        {
            if (OpenUnlockRequested is null)
            {
                // No dialog wired up (e.g. tests) — just begin.
                BeginEditSession();
                return;
            }
            OpenUnlockRequested.Invoke(BeginEditSession);
        }
    }

    /// <summary>
    /// Starts the in-memory edit session. Stops polling (which interferes with edit clicks)
    /// and flips every layer/key into edit mode for click handling.
    /// </summary>
    private void BeginEditSession()
    {
        if (KeyboardConfig is null) return;

        var layers = KeyboardConfig.Layers.Count;
        var rows = KeyboardConfig.MatrixRows;
        var cols = KeyboardConfig.MatrixCols;
        var baseline = new ushort[layers, rows, cols];
        foreach (var layer in KeyboardConfig.Layers)
        {
            foreach (var key in layer.Keys)
                baseline[layer.Index, key.Row, key.Col] = key.RawKeycode;
        }

        byte[]? macroBuffer = KeyboardConfig.Macros is not null
            ? MacroCodec.Encode(KeyboardConfig.Macros) : null;

        var session = new KeymapEditSession(baseline, BuildQmkSettingsDict(), BuildQmkSettingsWidths(), macroBuffer,
            KeyboardConfig.Combos, KeyboardConfig.TapDances);
        ActivateEditSession(session);
        DirtyCount = 0;
        StatusMessage = Loc.Instance["Status_EditModeActive"];
        DiagnosticLog.Info("Edit", $"Edit session started: {layers}x{rows}x{cols}");
    }

    /// <summary>
    /// Shared activation: sets the edit session, stops polling, and flips layer VMs
    /// into edit mode. Called by both <see cref="BeginEditSession"/> and
    /// <see cref="RestoreFromSnapshot"/>.
    /// </summary>
    private void ActivateEditSession(KeymapEditSession session)
    {
        StopMatrixPolling();
        StopLedPolling();
        _editSession = session;
        IsEditMode = true;

        // Rebuild layer VMs so that previously hidden empty layers become
        // visible — BuildLayerViewModels skips the empty-layer filter when
        // IsEditMode is true, and wires SetEditMode + click routing.
        var currentLayer = SelectedLayerIndex;
        BuildLayerViewModels(_settingsService.Load());
        SelectedLayerIndex = -1;
        SelectedLayerIndex = Math.Min(currentLayer, Layers.Count - 1);
    }

    /// <summary>
    /// Enters edit mode with a pre-populated session from a snapshot restore.
    /// Each pending change is reflected as a visual override on the corresponding key.
    /// If already in edit mode (e.g. after a save that kept edit mode active),
    /// the existing session is cleanly replaced.
    /// </summary>
    public void RestoreFromSnapshot(KeymapEditSession session)
    {
        if (KeyboardConfig is null) return;

        // If already in edit mode, tear down the existing session first
        if (IsEditMode)
        {
            _editSession?.Discard();
            _editSession = null;
            foreach (var layerVm in Layers)
                layerVm.ClearAllPendingOverrides();
            IsEditMode = false;
        }

        ActivateEditSession(session);

        // Apply visual overrides for each pending change
        foreach (var (layer, row, col, _, newCode) in session.PendingChanges)
        {
            var layerVm = Layers.FirstOrDefault(l => l.Index == layer);
            if (layerVm is not null)
            {
                var info = _keycodeService.Resolve(newCode);
                layerVm.ApplyPendingOverride(row, col, newCode, info.Label, info.SecondaryLabel);
            }
        }

        DirtyCount = ComputeDirtyCount();
        StatusMessage = Loc.Instance.Format("Status_RestoreLoaded", DirtyCount);
        DiagnosticLog.Info("Snapshot", $"Restore session loaded: {session.PendingChanges.Count} pending changes");
    }

    /// <summary>Snapshot of current device state for diff/restore dialogs, or null if disconnected.</summary>
    public DeviceSnapshot? GetDeviceSnapshotForDiff() =>
        KeyboardConfig is not null && IsConnected ? CurrentDeviceSnapshot() : null;

    public byte[]? GetDeviceMacroBufferForDiff() => GetDeviceSnapshotForDiff()?.MacroBuffer;
    public IReadOnlyList<byte[]>? GetDeviceCombosForDiff() => GetDeviceSnapshotForDiff()?.Combos;
    public IReadOnlyList<byte[]>? GetDeviceTapDancesForDiff() => GetDeviceSnapshotForDiff()?.TapDances;
    public ushort[,,]? GetDeviceKeymapForDiff() => GetDeviceSnapshotForDiff()?.Keymap;

    /// <summary>
    /// Applies a keycode change from the picker. Mirrors the change into the affected
    /// LayerViewModel's pending overrides and bumps <see cref="DirtyCount"/>.
    /// </summary>
    public void ApplyKeyEdit(int layer, int row, int col, ushort newCode)
    {
        if (_editSession is null) return;

        var oldCode = _editSession.GetCurrent(layer, row, col);
        if (oldCode == newCode) return;

        _editSession.Apply(new SetKeyOp(layer, row, col, oldCode, newCode));

        var baseline = _editSession.GetBaseline(layer, row, col);
        var layerVm = Layers.FirstOrDefault(l => l.Index == layer);
        if (layerVm is not null)
        {
            if (newCode == baseline)
            {
                layerVm.ClearPendingOverride(row, col);
            }
            else
            {
                var info = _keycodeService.Resolve(newCode);
                layerVm.ApplyPendingOverride(row, col, newCode, info.Label, info.SecondaryLabel);
            }
        }

        DirtyCount = ComputeDirtyCount();
        DiagnosticLog.Info("Edit", $"ApplyKeyEdit L{layer} R{row} C{col}: 0x{oldCode:X4} → 0x{newCode:X4} (dirty={DirtyCount})");
    }

    /// <summary>
    /// Stages a QMK setting edit in the current edit session. Called from the
    /// Device tab in SettingsWindow when the user changes a firmware setting value.
    /// </summary>
    public void ApplySettingEdit(ushort settingId, ushort newValue)
    {
        if (_editSession is null) return;

        var oldValue = _editSession.GetCurrentSetting(settingId) ?? 0;
        if (oldValue == newValue) return;

        _editSession.Apply(new SetQmkSettingOp(settingId, oldValue, newValue));
        DirtyCount = ComputeDirtyCount();
        DiagnosticLog.Info("Edit", $"ApplySettingEdit 0x{settingId:X4}: {oldValue} → {newValue} (dirty={DirtyCount})");
    }

    /// <summary>
    /// Applies a macro buffer edit (whole-buffer swap).
    /// Called by the future macro editor VM.
    /// </summary>
    public void ApplyMacroEdit(byte[] newEncodedBuffer)
    {
        if (_editSession is null) return;

        var oldBuffer = _editSession.GetCurrentMacroBuffer();
        if (oldBuffer is null) return;

        _editSession.Apply(new SetMacroBufferOp(oldBuffer, newEncodedBuffer));
        DirtyCount = ComputeDirtyCount();
        DiagnosticLog.Info("Edit", $"ApplyMacroEdit: buffer {newEncodedBuffer.Length}B (dirty={DirtyCount})");
    }

    /// <summary>
    /// Applies a single combo entry edit (index + 10-byte payload) as a
    /// <see cref="SetComboOp"/>. Called by the combo editor dialog.
    /// </summary>
    public void ApplyComboEdit(int index, byte[] newBytes)
    {
        if (_editSession is null) return;
        if (index < 0 || index >= _editSession.ComboCount) return;

        var oldBytes = _editSession.GetCurrentCombo(index);
        if (oldBytes.AsSpan().SequenceEqual(newBytes)) return;

        _editSession.Apply(new SetComboOp(index, oldBytes, (byte[])newBytes.Clone()));
        DirtyCount = ComputeDirtyCount();
        DiagnosticLog.Info("Edit", $"ApplyComboEdit #{index} (dirty={DirtyCount})");
    }

    /// <summary>
    /// Applies a single tap-dance entry edit (index + 10-byte payload) as a
    /// <see cref="SetTapDanceOp"/>. Called by the tap-dance editor dialog.
    /// </summary>
    public void ApplyTapDanceEdit(int index, byte[] newBytes)
    {
        if (_editSession is null) return;
        if (index < 0 || index >= _editSession.TapDanceCount) return;

        var oldBytes = _editSession.GetCurrentTapDance(index);
        if (oldBytes.AsSpan().SequenceEqual(newBytes)) return;

        _editSession.Apply(new SetTapDanceOp(index, oldBytes, (byte[])newBytes.Clone()));
        DirtyCount = ComputeDirtyCount();
        DiagnosticLog.Info("Edit", $"ApplyTapDanceEdit #{index} (dirty={DirtyCount})");
    }

    /// <summary>
    /// Returns the working combo list — from the edit session if active,
    /// otherwise the device baseline. Used by the combo editor dialog.
    /// </summary>
    public IReadOnlyList<byte[]> GetCurrentCombos()
    {
        if (_editSession is not null)
        {
            var list = new List<byte[]>(_editSession.ComboCount);
            for (var i = 0; i < _editSession.ComboCount; i++)
                list.Add(_editSession.GetCurrentCombo(i));
            return list;
        }
        return KeyboardConfig?.Combos ?? [];
    }

    /// <summary>
    /// Returns the working tap-dance list — from the edit session if active,
    /// otherwise the device baseline. Used by the tap-dance editor dialog.
    /// </summary>
    public IReadOnlyList<byte[]> GetCurrentTapDances()
    {
        if (_editSession is not null)
        {
            var list = new List<byte[]>(_editSession.TapDanceCount);
            for (var i = 0; i < _editSession.TapDanceCount; i++)
                list.Add(_editSession.GetCurrentTapDance(i));
            return list;
        }
        return KeyboardConfig?.TapDances ?? [];
    }

    private int ComputeDirtyCount() =>
        _editSession is null ? 0
        : _editSession.PendingChanges.Count
          + _editSession.PendingSettingsChanges.Count
          + (_editSession.HasPendingMacroChanges ? 1 : 0)
          + (_editSession.HasPendingComboChanges ? 1 : 0)
          + (_editSession.HasPendingTapDanceChanges ? 1 : 0);

    /// <summary>
    /// Discards all pending changes and exits edit mode. Restores polling if it was on.
    /// </summary>
    [RelayCommand]
    private void DiscardEdit()
    {
        if (!IsEditMode) return;

        _editSession?.Discard();
        _editSession = null;
        DirtyCount = 0;
        IsEditMode = false;
        _wasLockedOnEnterEdit = false;

        // Rebuild layer VMs so empty layers are hidden again in view mode,
        // and clear edit-mode state on remaining layers.
        var currentLayer = SelectedLayerIndex;
        BuildLayerViewModels(_settingsService.Load());
        SelectedLayerIndex = -1;
        SelectedLayerIndex = Math.Clamp(currentLayer, 0, Math.Max(0, Layers.Count - 1));

        // Resume polling if it was enabled before edit mode
        if (IsLiveHighlightingEnabled && IsConnected)
            StartMatrixPolling();
        if (_ledPollSupported && IsAutoLayerSwitchEnabled && IsConnected)
            StartLedPolling();

        RefreshStatusMessage();
        DiagnosticLog.Info("Edit", "Edit session discarded");
    }

    /// <summary>
    /// Flushes every pending edit to the device. Full pipeline:
    /// pre-save safety check → pre-save snapshot → per-op write loop →
    /// reload device state → reconcile → post-save snapshot → optional re-lock.
    ///
    /// On success the edit session is cleared but edit mode stays active so
    /// the user can keep editing. On a mid-batch failure the session is
    /// rebuilt from the reloaded baseline and refilled with the still-pending
    /// writes, so the user can fix the problem and press Save again without
    /// re-typing anything.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveEdit))]
    private async Task SaveEdit()
    {
        if (_editSession is null || KeyboardConfig is null) return;

        IsSaving = true;
        _saveCts = new CancellationTokenSource();
        var ct = _saveCts.Token;
        var totalPending = ComputeDirtyCount();

        try
        {
            if (!await RunPreflightAsync()) return;

            StatusMessage = Loc.Instance.Format("Status_SaveInProgressFormat", totalPending);

            var flowCtx = new SaveFlow.Context(
                Protocol: _protocolService,
                SnapshotService: _snapshotService,
                EditSession: _editSession,
                KeyboardId: GetKeyboardId(),
                DeviceName: _connectedDeviceName ?? KeyboardConfig.DeviceName,
                KeepFirstConnectDays: _settingsService.Load().KeepFirstConnectDays,
                CurrentDeviceSnapshot: CurrentDeviceSnapshot,
                ReloadAsync: (macroBuf, skip, token) => ReloadKeymapFromDeviceAsync(token, macroBuf, skip));

            var flow = await SaveFlow.RunAsync(flowCtx, ct);

            SaveResult result;
            if (flow.Execution.Cancelled)
                result = new SaveCancelled(flow.Execution.Applied.Count);
            else if (flow.Execution.Failure is null)
                result = new SaveSuccess(flow.Execution.Applied.Count);
            else
                result = BuildPartialResult(flow.Execution, flow.Intended, totalPending);

            ApplySaveResult(result, totalPending);

            if (result is SaveSuccess && _wasLockedOnEnterEdit)
            {
                await SaveFlow.ReLockAsync(_protocolService);
                _wasLockedOnEnterEdit = false;
            }

            SaveCompletedCallback?.Invoke(result);
        }
        catch (OperationCanceledException)
        {
            DiagnosticLog.Warn("Save", "Save cancelled");
            try { await ReloadKeymapFromDeviceAsync(CancellationToken.None); }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("Save", $"Post-cancel reload failed: {ex.Message}");
            }
            StatusMessage = Loc.Instance["Status_SaveCancelled"];
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("Save", $"Unexpected save error: {ex}");
            StatusMessage = Loc.Instance.Format("Status_SaveErrorFormat", ex.Message);
        }
        finally
        {
            IsSaving = false;
            _saveCts?.Dispose();
            _saveCts = null;
        }
    }

    /// <summary>
    /// Pre-save gates: safety-warning confirmation + macro buffer overflow.
    /// Returns false (and fires <see cref="SaveCompletedCallback"/> with
    /// <see cref="SaveAborted"/>) when the save must not proceed.
    /// </summary>
    private async Task<bool> RunPreflightAsync()
    {
        var intended = _editSession!.CloneCurrent();
        var warnings = PreSaveSafetyCheck.Check(intended);
        if (warnings.Count > 0 && ConfirmSafetyWarningsRequested is not null)
        {
            var proceed = await ConfirmSafetyWarningsRequested(warnings);
            if (!proceed)
            {
                StatusMessage = Loc.Instance["Status_SaveCancelledSafety"];
                DiagnosticLog.Info("Save",
                    $"Save aborted by user: {warnings.Count} safety warning(s)");
                SaveCompletedCallback?.Invoke(new SaveAborted("safety warnings declined"));
                return false;
            }
        }

        if (_editSession.HasPendingMacroChanges && KeyboardConfig!.Macros is not null)
        {
            var currentBuf = _editSession.GetCurrentMacroBuffer();
            if (currentBuf is not null && currentBuf.Length > KeyboardConfig.Macros.BufferCapacity)
            {
                StatusMessage = Loc.Instance["Edit_MacroBufferOverflow"];
                DiagnosticLog.Warn("Save",
                    $"Save blocked: macro buffer {currentBuf.Length} > capacity {KeyboardConfig.Macros.BufferCapacity}");
                SaveCompletedCallback?.Invoke(new SaveAborted("macro buffer overflow"));
                return false;
            }
        }

        return true;
    }

    private bool CanSaveEdit() =>
        IsEditMode && DirtyCount > 0 && !IsSaving && KeyboardConfig is not null;

    private KeyboardId GetKeyboardId() =>
        KeyboardId.From(KeyboardConfig!.VendorId, KeyboardConfig.ProductId, KeyboardConfig.KeyboardId);

    private Dictionary<ushort, ushort> BuildQmkSettingsDict() =>
        KeyboardConfig?.QmkSettings.ToDictionary(s => s.SettingId, s => s.Value)
        ?? new Dictionary<ushort, ushort>();

    private Dictionary<ushort, byte> BuildQmkSettingsWidths() =>
        KeyboardConfig?.QmkSettings.ToDictionary(s => s.SettingId, s => s.Width)
        ?? new Dictionary<ushort, byte>();

    private DeviceSnapshot CurrentDeviceSnapshot() => DeviceSnapshot.From(KeyboardConfig!);
    private ushort[,,] GetDeviceKeymap() => CurrentDeviceSnapshot().Keymap;

    /// <summary>
    /// Returns the current working macro buffer — from the edit session if active,
    /// otherwise from the loaded KeyboardConfig. Used by the macro editor dialog
    /// so it shows in-progress edits, not stale baseline data.
    /// </summary>
    public MacroBuffer? GetCurrentMacros()
    {
        if (_editSession?.GetCurrentMacroBuffer() is { } buffer && KeyboardConfig?.Macros is { } original)
            return MacroCodec.Decode(buffer, original.Macros.Count, original.BufferCapacity);
        return KeyboardConfig?.Macros;
    }

    private async Task TryCaptureFirstConnectSnapshotAsync()
    {
        if (KeyboardConfig is null) return;
        var keyboardId = GetKeyboardId();
        try
        {
            var existing = await _snapshotService.ListAsync(keyboardId);
            if (existing.Count > 0) return;

            var deviceName = _connectedDeviceName ?? KeyboardConfig.DeviceName;
            await _snapshotService.CaptureAsync(
                SnapshotReason.FirstConnect, keyboardId, deviceName,
                CurrentDeviceSnapshot());
            DiagnosticLog.Info("Snapshot", $"FirstConnect snapshot captured for {keyboardId}");
        }
        catch (Exception ex)
        {
            // Best-effort: pre-save capture is load-bearing for partial-failure
            // recovery, but a missing first-connect snapshot just means the user
            // has no "factory restore" point — never block the connect on it.
            DiagnosticLog.Warn("Snapshot", $"FirstConnect snapshot failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CaptureManualSnapshot(string? userLabel)
    {
        if (KeyboardConfig is null || !IsConnected) return;
        var keyboardId = GetKeyboardId();
        var deviceName = _connectedDeviceName ?? KeyboardConfig.DeviceName;
        try
        {
            await _snapshotService.CaptureAsync(
                SnapshotReason.Manual, keyboardId, deviceName,
                CurrentDeviceSnapshot(), userLabel: userLabel);
            DiagnosticLog.Info("Snapshot", $"Manual snapshot captured: {userLabel ?? "(no label)"}");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("Snapshot", $"Manual capture failed: {ex.Message}");
            StatusMessage = $"Snapshot capture failed: {ex.Message}";
        }
    }

    /// <summary>Prompts the user for a label, then takes a manual snapshot.</summary>
    public Func<Task<string?>>? RequestManualSnapshotLabel { get; set; }

    [RelayCommand]
    private async Task CaptureManualSnapshotWithPromptAsync()
    {
        if (RequestManualSnapshotLabel is null) return;
        var label = await RequestManualSnapshotLabel();
        if (label is null) return; // user cancelled
        await CaptureManualSnapshot(label.Length > 0 ? label : null);
    }

    /// <summary>
    /// Re-fetches the keymap from the connected device without re-running
    /// device enumeration or the LED/definition probes. Runs on a background
    /// thread (the protocol service is synchronous); UI updates happen on
    /// the calling thread once it resumes.
    /// </summary>
    private async Task ReloadKeymapFromDeviceAsync(CancellationToken ct, byte[]? knownMacroBuffer = null, bool skipMacroReload = false)
    {
        if (KeyboardConfig is null) return;

        var refresh = await Task.Run(
            () => DeviceRefreshService.Fetch(_protocolService, KeyboardConfig, knownMacroBuffer, skipMacroReload), ct);

        var physical = SvalboardLayout.GetKeyPositions();
        var updatedLayers = new List<Layer>(KeyboardConfig.Layers.Count);
        foreach (var layer in KeyboardConfig.Layers)
        {
            var updatedKeys = new List<Key>(layer.Keys.Count);
            foreach (var key in layer.Keys)
            {
                var raw = refresh.Keymap[layer.Index, key.Row, key.Col];
                var info = _keycodeService.Resolve(raw);
                var position = physical.FirstOrDefault(p => p.Row == key.Row && p.Col == key.Col);
                updatedKeys.Add(key with
                {
                    RawKeycode = raw,
                    DisplayLabel = info.Label,
                    SecondaryLabel = info.SecondaryLabel,
                    IsTransparent = info.IsTransparent,
                    IsLayerSwitch = info.IsLayerSwitch,
                    TargetLayer = info.TargetLayer,
                    SwitchType = info.SwitchType,
                    IsUnknown = info.IsUnknown,
                    ShiftedLabel = info.ShiftedLabel,
                    X = position?.X ?? key.X,
                    Y = position?.Y ?? key.Y,
                    Width = position?.Width ?? key.Width,
                    Height = position?.Height ?? key.Height,
                });
            }
            updatedLayers.Add(layer with { Keys = updatedKeys });
        }

        TransparentKeyResolver.Resolve(updatedLayers);

        KeyboardConfig = KeyboardConfig with
        {
            Layers = updatedLayers,
            QmkSettings = refresh.QmkSettings,
            Macros = refresh.Macros ?? KeyboardConfig.Macros,
            Combos = refresh.Combos,
            TapDances = refresh.TapDances,
        };

        // Update existing layer/key VMs in place. Rebuilding the VM tree
        // (the previous approach) replaced every KeyViewModel instance, and
        // the creative thumb-cluster bindings ended up pointing at stale
        // instances — making the first post-save edit's label appear frozen.
        // Keeping instances stable lets Avalonia bindings keep working and
        // preserves OnClickAction / SetEditMode wiring.
        if (Layers.Count == 0)
        {
            BuildLayerViewModels(_settingsService.Load());
        }
        else
        {
            foreach (var layerVm in Layers)
            {
                var updatedLayer = updatedLayers.FirstOrDefault(l => l.Index == layerVm.Index);
                if (updatedLayer is not null)
                    layerVm.UpdateBaselineFromLayer(updatedLayer);
            }
        }
    }

    /// <summary>
    /// Reconciles a mid-batch failure against the reloaded device state.
    /// Each cell the user intended to change falls into one of three buckets:
    /// <list type="bullet">
    /// <item>applied — device now matches intent;</item>
    /// <item>still pending — device still matches baseline, so the write never
    ///   landed and can be retried;</item>
    /// <item>diverged — device matches neither (unexpected external write or
    ///   partial write). Flagged for the user but not auto-retried.</item>
    /// </list>
    /// Rebuilds <see cref="_editSession"/> from the reloaded baseline with
    /// <see cref="SetKeyOp"/>s for each still-pending cell so the user can
    /// press Save again with zero re-typing.
    /// </summary>
    private SavePartial BuildPartialResult(
        SaveFlowExecutor.ExecutionOutcome outcome,
        ushort[,,] intended,
        int totalPending)
    {
        var device = GetDeviceKeymap();
        var baseline = _editSession!.CloneBaseline();
        var pendingSettings = _editSession.PendingSettingsChanges;

        var reconciled = SaveReconciliation.Reconcile(device, baseline, intended);

        // Rebuild the edit session on top of the reloaded device state so
        // the user can hit Save again without re-editing.
        _editSession = new KeymapEditSession(device, BuildQmkSettingsDict(), BuildQmkSettingsWidths(),
            macroBuffer: null,
            combos: KeyboardConfig?.Combos,
            tapDances: KeyboardConfig?.TapDances);
        foreach (var w in reconciled.StillPending)
        {
            var old = _editSession.GetCurrent(w.Layer, w.Row, w.Col);
            _editSession.Apply(new SetKeyOp(w.Layer, w.Row, w.Col, old, w.Keycode));
        }

        foreach (var (id, _, newVal) in pendingSettings)
        {
            var current = _editSession.GetCurrentSetting(id) ?? 0;
            if (current != newVal)
                _editSession.Apply(new SetQmkSettingOp(id, current, newVal));
        }

        var message = outcome.Failure?.Message ?? "unknown error";
        DiagnosticLog.Warn("Save",
            $"Partial save: {reconciled.Applied} applied, {reconciled.StillPending.Count} pending, {reconciled.Diverged} diverged — {message}");

        return new SavePartial(
            Applied: reconciled.Applied,
            StillPending: reconciled.StillPending.Count,
            Diverged: reconciled.Diverged,
            FailureMessage: message,
            Remaining: reconciled.StillPending);
    }

    /// <summary>
    /// Pushes a <see cref="SaveResult"/> into the VM: updates the status
    /// message, rebuilds layer pending overlays, and clears the session on
    /// full success. Keeps edit mode on so the user can keep working.
    /// </summary>
    private void ApplySaveResult(SaveResult result, int totalPending)
    {
        switch (result)
        {
            case SaveSuccess s:
                // Rebuild a fresh edit session on top of the now-current device
                // state so the user can immediately make another edit. The session
                // must stay non-null while IsEditMode is true — OnKeyClicked
                // early-returns otherwise, which makes the app appear frozen.
                foreach (var layerVm in Layers)
                    layerVm.ClearAllPendingOverrides();
                RebuildEditSessionFromCurrentConfig(preserveIntent: false);
                StatusMessage = Loc.Instance.Format("Status_SaveCompleteFormat", s.Applied);
                DiagnosticLog.Info("Save", $"Save complete: {s.Applied} change(s) written");
                break;

            case SavePartial p:
                // _editSession was already rebuilt by BuildPartialResult.
                DirtyCount = p.StillPending;
                RebuildPendingOverlaysFromSession();
                StatusMessage = Loc.Instance.Format(
                    "Status_SavePartialFormat", p.Applied, totalPending, p.StillPending);
                break;

            case SaveCancelled c:
                // Rebuild session on top of whatever is now on the device so
                // the user can retry without re-typing the still-unsaved ones.
                RebuildEditSessionFromCurrentConfig(preserveIntent: true);
                StatusMessage = Loc.Instance["Status_SaveCancelled"];
                DiagnosticLog.Info("Save", $"Save cancelled after {c.Applied} write(s)");
                break;
        }
    }

    /// <summary>
    /// Rebuilds layer pending overlays from the current <see cref="_editSession"/>
    /// so the visible UI reflects the (possibly reconciled) pending set.
    /// </summary>
    private void RebuildPendingOverlaysFromSession()
    {
        foreach (var layerVm in Layers)
            layerVm.ClearAllPendingOverrides();
        if (_editSession is null) return;

        foreach (var (layer, row, col, _, newCode) in _editSession.PendingChanges)
        {
            var layerVm = Layers.FirstOrDefault(l => l.Index == layer);
            if (layerVm is null) continue;
            var info = _keycodeService.Resolve(newCode);
            layerVm.ApplyPendingOverride(row, col, newCode, info.Label, info.SecondaryLabel);
        }
    }

    /// <summary>
    /// Rebuilds <see cref="_editSession"/> from the current <see cref="KeyboardConfig"/>.
    /// If <paramref name="preserveIntent"/> is true and the previous session
    /// had pending changes, those (layer, row, col, newCode) tuples are
    /// re-applied on top of the new baseline — used by the cancellation path
    /// so the user doesn't lose their unsaved work when they hit Save again.
    /// </summary>
    private void RebuildEditSessionFromCurrentConfig(bool preserveIntent)
    {
        if (KeyboardConfig is null) return;

        var previousPending = preserveIntent && _editSession is not null
            ? _editSession.PendingChanges.ToList()
            : new List<(int Layer, int Row, int Col, ushort OldCode, ushort NewCode)>();
        var previousSettingsPending = preserveIntent && _editSession is not null
            ? _editSession.PendingSettingsChanges.ToList()
            : new List<(ushort SettingId, ushort OldValue, ushort NewValue)>();
        var previousMacroBuffer = preserveIntent && _editSession is not null
            ? _editSession.GetCurrentMacroBuffer() : null;

        var layers = KeyboardConfig.Layers.Count;
        var rows = KeyboardConfig.MatrixRows;
        var cols = KeyboardConfig.MatrixCols;
        var baseline = new ushort[layers, rows, cols];
        foreach (var layer in KeyboardConfig.Layers)
            foreach (var key in layer.Keys)
                baseline[layer.Index, key.Row, key.Col] = key.RawKeycode;

        byte[]? macroBuffer = KeyboardConfig.Macros is not null
            ? MacroCodec.Encode(KeyboardConfig.Macros) : null;
        _editSession = new KeymapEditSession(baseline, BuildQmkSettingsDict(), BuildQmkSettingsWidths(), macroBuffer,
            KeyboardConfig.Combos, KeyboardConfig.TapDances);
        foreach (var (l, r, c, _, newCode) in previousPending)
        {
            // Only re-apply edits whose target cell actually still diverges
            // from the new baseline — anything the device already has is done.
            var old = _editSession.GetCurrent(l, r, c);
            if (old != newCode)
                _editSession.Apply(new SetKeyOp(l, r, c, old, newCode));
        }
        foreach (var (id, _, newVal) in previousSettingsPending)
        {
            var current = _editSession.GetCurrentSetting(id) ?? 0;
            if (current != newVal)
                _editSession.Apply(new SetQmkSettingOp(id, current, newVal));
        }
        if (previousMacroBuffer is not null)
        {
            var baselineMacro = _editSession.GetBaselineMacroBuffer();
            if (baselineMacro is not null && !baselineMacro.AsSpan().SequenceEqual(previousMacroBuffer))
                _editSession.Apply(new SetMacroBufferOp(baselineMacro, previousMacroBuffer));
        }
        DirtyCount = ComputeDirtyCount();
        RebuildPendingOverlaysFromSession();
    }

    partial void OnIsEditModeChanged(bool value)
    {
        if (value)
        {
            _preEditBackgroundOpacity = BackgroundOpacity;
            BackgroundOpacity = 1.0;
        }
        else if (_preEditBackgroundOpacity is { } saved)
        {
            BackgroundOpacity = saved;
            _preEditBackgroundOpacity = null;
        }
    }

    partial void OnIsConnectedChanged(bool value)
    {
        // If the device disconnects mid-edit, tear down the session and warn the user.
        if (!value && IsEditMode)
        {
            _saveCts?.Cancel();
            _editSession?.Discard();
            _editSession = null;
            DirtyCount = 0;
            IsEditMode = false;
            _wasLockedOnEnterEdit = false;

            foreach (var layerVm in Layers)
            {
                layerVm.ClearAllPendingOverrides();
                layerVm.SetEditMode(false);
            }

            StatusMessage = Loc.Instance["Status_EditAbortedDisconnect"];
            DiagnosticLog.Warn("Edit", "Edit session aborted: device disconnected");
        }
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
        OpenSettingsRequested?.Invoke(null);
    }

    [RelayCommand]
    private void OpenSettingsToDevice()
    {
        OpenSettingsRequested?.Invoke(2);
    }

    [RelayCommand]
    private void OpenHistory()
    {
        OpenHistoryRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenMacros()
    {
        if (KeyboardConfig?.Macros is null)
        {
            StatusMessage = Loc.Instance["Edit_MacroLoading"];
            return;
        }

        OpenMacrosRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenCombos()
    {
        if (KeyboardConfig is null || KeyboardConfig.Combos.Count == 0)
        {
            StatusMessage = Loc.Instance["ComboEditor_None"];
            return;
        }
        OpenCombosRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenTapDance()
    {
        if (KeyboardConfig is null || KeyboardConfig.TapDances.Count == 0)
        {
            StatusMessage = Loc.Instance["TapDanceEditor_None"];
            return;
        }
        OpenTapDanceRequested?.Invoke();
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
    private void OpenLogFolder()
    {
        var dir = DiagnosticLog.GetLogDirectory();
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open log folder: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CopyDiagnostics()
    {
        CopyDiagnosticsRequested?.Invoke();
    }

    /// <summary>Invoked by CopyDiagnosticsCommand so the App layer can access the clipboard.</summary>
    public Action? CopyDiagnosticsRequested { get; set; }

    [RelayCommand]
    private async Task Quit()
    {
        await ShutdownAsync();
        QuitRequested?.Invoke();
    }

    // int, not bool, because Interlocked has no bool overload. CompareExchange
    // serialises the gate against a re-entrant Closing-during-Quit interleave:
    // Quit awaits work that yields; a Closing event on the resumed UI thread
    // would otherwise also see _isShutdown == 0 and run the body twice,
    // double-disposing _connectCts and _protocolService.
    private int _isShutdown;

    /// <summary>
    /// Tears down all device-facing resources. Idempotent: safe to call from
    /// both the Quit command and the window's Closing handler. Without this,
    /// closing via the title-bar X bypassed Quit and left polling threads,
    /// the device-list subscription, and the HID stream alive after the VM
    /// became unreachable. Task-returning so any future async cleanup can be
    /// awaited at process exit instead of becoming fire-and-forget.
    /// </summary>
    public Task ShutdownAsync()
    {
        if (Interlocked.CompareExchange(ref _isShutdown, 1, 0) != 0) return Task.CompletedTask;
        StopMatrixPolling();
        StopLedPolling();
        _deviceSubscription?.Dispose();
        _deviceSubscription = null;
        _connectCts?.Cancel();
        _connectCts?.Dispose();
        _connectCts = null;
        _protocolService.Dispose();
        return Task.CompletedTask;
    }

}
