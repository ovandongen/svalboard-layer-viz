using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.App.Localization;
using SvalboardLayerViz.App.Services;
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

    // Host-provided dialog + shell collaborators. Default to headless no-ops so
    // the VM is fully constructible (and testable) without a windowing system;
    // App swaps in the real Desktop* impls once the window exists (AttachHost).
    private IDialogService _dialogs = new NoopDialogService();
    private IShellService _shell = new NoopShellService();

    /// <summary>
    /// Wires the App-layer host services. Called once by the composition root
    /// after the main window is built — replaces the old per-callback property
    /// assignments. Tests pass fakes here to observe dialog/shell interactions.
    /// </summary>
    internal void AttachHost(IDialogService dialogs, IShellService shell)
    {
        _dialogs = dialogs;
        _shell = shell;
    }

    /// <summary>Protocol service exposed for dialogs (unlock flow) that need direct device access.</summary>
    public IVialProtocolService ProtocolService => _protocolService;

    // Narrow internal accessors for the extracted collaborators
    // (LivePollingCoordinator / EditSessionController) — same assembly, impl detail.
    internal IDialogService Dialogs => _dialogs;
    internal IShellService Shell => _shell;
    internal ISettingsService Settings => _settingsService;
    internal KeycodeService KeycodeService => _keycodeService;
    internal string? ConnectedDeviceName => _connectedDeviceName;
    private IDisposable? _deviceSubscription;

    /// <summary>
    /// Live-polling subsystem: matrix + LED polling and the layer-switch
    /// heuristic. The VM keeps the thin polling commands as delegators and
    /// exposes <see cref="StopAllPolling"/>/<see cref="ResumePolling"/> for the
    /// edit flow; everything else lives here.
    /// </summary>
    private readonly LivePollingCoordinator _polling;

    private string? _connectedDeviceName;
    private CancellationTokenSource? _connectCts;
    private bool _isConnecting;

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
    /// BackgroundOpacity captured on entering edit mode so it can be restored on exit.
    /// Edit mode forces the background fully opaque (solid) to maximize contrast while
    /// the user is clicking keys; the original transparency comes back on Discard/Save.
    /// </summary>
    private double? _preEditBackgroundOpacity;

    /// <summary>True only when live polling can be toggled — blocked while editing to avoid interaction conflicts.</summary>
    public bool CanToggleLivePolling => !IsEditMode;

    /// <summary>
    /// Keymap edit/save subsystem: session lifecycle, Apply*Edit staging, save
    /// pipeline. The VM keeps the public edit API + edit commands as delegators.
    /// </summary>
    private readonly EditSessionController _edit;

    /// <summary>Exposed for tests to verify pending-change state.</summary>
    public KeymapEditSession? EditSession => _edit.Session;

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

    /// <summary>Snapshot service used by both this VM and the History window.</summary>
    public ISnapshotService SnapshotService => _snapshotService;

    /// <summary>Current keyboard's identity, if connected.</summary>
    public KeyboardId? CurrentKeyboardId =>
        KeyboardConfig is null ? null : GetKeyboardId();

    /// <summary>
    /// Fired when deferred macro load completes. Lets an open macro editor
    /// dialog update its slots from the freshly loaded data.
    /// </summary>
    public event Action<MacroBuffer>? MacrosLoaded;

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
        _polling = new LivePollingCoordinator(this);
        _edit = new EditSessionController(this);
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
            StopAllPolling();

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
            _polling.LedPollSupported = session.LedPollSupported;

            BuildLayerViewModels(settings);
            _polling.BuildLayerSwitchCache();

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

            ResumePolling();
        }
        catch (OperationCanceledException)
        {
            DiagnosticLog.Warn("Device", "Device connection timed out after 30s");
            StatusMessage = Loc.Instance["Status_DeviceSearchTimedOut"];
            IsConnected = false;
        }
        catch (Exception ex)
        {
            StopAllPolling();
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
                if (_edit.Session?.GetCurrentMacroBuffer() is null)
                {
                    var encoded = MacroCodec.Encode(macros);
                    _edit.Session?.SetMacroBaseline(encoded);
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
                setLabelRequested: keyVm => _dialogs.ShowKeyLabelEditor(keyVm)));
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
        if (!IsEditMode || _edit.Session is null) return;

        var layer = keyVm.Layer.Index;
        var row = keyVm.Key.Row;
        var col = keyVm.Key.Col;
        var currentCode = _edit.Session.GetCurrent(layer, row, col);

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

        _dialogs.OpenKeyPicker(request);
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
        _shell.HotkeyChanged(settings.HotkeyKey, settings.HotkeyModifiers);
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
    internal void RefreshStatusMessage()
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

    /// <summary>
    /// Pauses both polling loops. Called at connect entry, on connection error,
    /// and when entering edit mode (polling interferes with edit clicks).
    /// </summary>
    internal void StopAllPolling()
    {
        _polling.StopMatrixPolling();
        _polling.StopLedPolling();
    }

    /// <summary>
    /// Restarts polling that should be active given the current state: matrix
    /// polling when live-highlighting is on, LED polling when auto-switch is on
    /// and the firmware supports the color query. No-op while editing or
    /// disconnected. Called at end of connect and when leaving edit mode.
    /// </summary>
    internal void ResumePolling()
    {
        if (IsLiveHighlightingEnabled && IsConnected && !IsEditMode)
            _polling.StartMatrixPolling();
        if (_polling.LedPollSupported && IsAutoLayerSwitchEnabled && IsConnected && !IsEditMode)
            _polling.StartLedPolling();
    }

    [RelayCommand]
    private void ResetLayerState() => _polling.ResetLayerState();

    [RelayCommand]
    private void ToggleAutoLayerSwitch() => _polling.ToggleAutoLayerSwitch();

    [RelayCommand]
    private void ToggleLiveHighlighting() => _polling.ToggleLiveHighlighting();

    // ============================================================================
    // Edit mode — Session 10
    // ============================================================================

    /// <summary>
    /// Enters edit mode. Checks unlock status first; if locked, opens the unlock dialog
    /// and defers session creation until the user completes the unlock sequence.
    /// </summary>
    [RelayCommand]
    private Task EnterEdit() => _edit.EnterEditAsync();

    /// <summary>
    /// Enters edit mode with a pre-populated session from a snapshot restore
    /// (called by the History window). Delegates to the edit controller.
    /// </summary>
    public void RestoreFromSnapshot(KeymapEditSession session) => _edit.RestoreFromSnapshot(session);

    /// <summary>Snapshot of current device state for diff/restore dialogs, or null if disconnected.</summary>
    public DeviceSnapshot? GetDeviceSnapshotForDiff() =>
        KeyboardConfig is not null && IsConnected ? CurrentDeviceSnapshot() : null;

    public byte[]? GetDeviceMacroBufferForDiff() => GetDeviceSnapshotForDiff()?.MacroBuffer;
    public IReadOnlyList<byte[]>? GetDeviceCombosForDiff() => GetDeviceSnapshotForDiff()?.Combos;
    public IReadOnlyList<byte[]>? GetDeviceTapDancesForDiff() => GetDeviceSnapshotForDiff()?.TapDances;
    public ushort[,,]? GetDeviceKeymapForDiff() => GetDeviceSnapshotForDiff()?.Keymap;

    // ---- Edit/save public API: thin delegators onto the EditSessionController ----

    /// <summary>Applies a keycode change from the picker. Delegates to the edit controller.</summary>
    public void ApplyKeyEdit(int layer, int row, int col, ushort newCode) => _edit.ApplyKeyEdit(layer, row, col, newCode);

    /// <summary>Stages a QMK setting edit. Called from the Device tab in SettingsWindow.</summary>
    public void ApplySettingEdit(ushort settingId, ushort newValue) => _edit.ApplySettingEdit(settingId, newValue);

    /// <summary>Applies a macro buffer edit (whole-buffer swap). Called by the macro editor.</summary>
    public void ApplyMacroEdit(byte[] newEncodedBuffer) => _edit.ApplyMacroEdit(newEncodedBuffer);

    /// <summary>Applies a single combo entry edit. Called by the combo editor dialog.</summary>
    public void ApplyComboEdit(int index, byte[] newBytes) => _edit.ApplyComboEdit(index, newBytes);

    /// <summary>Applies a single tap-dance entry edit. Called by the tap-dance editor dialog.</summary>
    public void ApplyTapDanceEdit(int index, byte[] newBytes) => _edit.ApplyTapDanceEdit(index, newBytes);

    /// <summary>Working combo list — from the edit session if active, else device baseline.</summary>
    public IReadOnlyList<byte[]> GetCurrentCombos() => _edit.GetCurrentCombos();

    /// <summary>Working tap-dance list — from the edit session if active, else device baseline.</summary>
    public IReadOnlyList<byte[]> GetCurrentTapDances() => _edit.GetCurrentTapDances();

    /// <summary>Working macro buffer — from the edit session if active, else loaded config.</summary>
    public MacroBuffer? GetCurrentMacros() => _edit.GetCurrentMacros();

    /// <summary>Discards all pending changes and exits edit mode. Delegates to the edit controller.</summary>
    [RelayCommand]
    private void DiscardEdit() => _edit.DiscardEdit();

    /// <summary>Flushes every pending edit to the device. Delegates to the edit controller.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveEdit))]
    private Task SaveEdit() => _edit.SaveEditAsync();

    private bool CanSaveEdit() =>
        IsEditMode && DirtyCount > 0 && !IsSaving && KeyboardConfig is not null;

    internal KeyboardId GetKeyboardId() =>
        KeyboardId.From(KeyboardConfig!.VendorId, KeyboardConfig.ProductId, KeyboardConfig.KeyboardId);

    internal DeviceSnapshot CurrentDeviceSnapshot() => DeviceSnapshot.From(KeyboardConfig!);

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

    [RelayCommand]
    private async Task CaptureManualSnapshotWithPromptAsync()
    {
        var label = await _dialogs.PromptSnapshotLabelAsync();
        if (label is null) return; // user cancelled
        await CaptureManualSnapshot(label.Length > 0 ? label : null);
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
            _edit.AbortForDisconnect();
    }

    [RelayCommand]
    private void SelectLayer(int index) => SelectedLayerIndex = index;

    [RelayCommand]
    private void Show()
    {
        _shell.ShowWindow();
    }

    [RelayCommand]
    private void ToggleOverlay()
    {
        _shell.ToggleWindow();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        _dialogs.OpenSettings(null);
    }

    [RelayCommand]
    private void OpenSettingsToDevice()
    {
        _dialogs.OpenSettings(2);
    }

    [RelayCommand]
    private void OpenHistory()
    {
        _dialogs.OpenHistory();
    }

    [RelayCommand]
    private void OpenMacros()
    {
        if (KeyboardConfig?.Macros is null)
        {
            StatusMessage = Loc.Instance["Edit_MacroLoading"];
            return;
        }

        _dialogs.OpenMacroEditor();
    }

    [RelayCommand]
    private void OpenCombos()
    {
        if (KeyboardConfig is null || KeyboardConfig.Combos.Count == 0)
        {
            StatusMessage = Loc.Instance["ComboEditor_None"];
            return;
        }
        _dialogs.OpenComboEditor();
    }

    [RelayCommand]
    private void OpenTapDance()
    {
        if (KeyboardConfig is null || KeyboardConfig.TapDances.Count == 0)
        {
            StatusMessage = Loc.Instance["TapDanceEditor_None"];
            return;
        }
        _dialogs.OpenTapDanceEditor();
    }

    [RelayCommand]
    private void OpenDiagnostics()
    {
        _dialogs.OpenDiagnostics();
    }

    [RelayCommand]
    private void Export()
    {
        _dialogs.OpenExport();
    }

    [RelayCommand]
    private void OpenHelp()
    {
        _dialogs.OpenHelp();
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
        _shell.CopyDiagnostics();
    }

    [RelayCommand]
    private async Task Quit()
    {
        await ShutdownAsync();
        _shell.Quit();
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
        _polling.Dispose();
        _deviceSubscription?.Dispose();
        _deviceSubscription = null;
        _connectCts?.Cancel();
        _connectCts?.Dispose();
        _connectCts = null;
        _protocolService.Dispose();
        return Task.CompletedTask;
    }

}
