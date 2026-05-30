using Avalonia.Threading;
using SvalboardLayerViz.App.Localization;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Protocol;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// Owns the live-polling subsystem extracted from <see cref="MainWindowViewModel"/>:
/// matrix polling (key-press highlighting + matrix-heuristic layer switching) and
/// LED polling (rgblight-color layer detection), plus the layer-switch heuristic
/// state. An implementation detail of the VM — holds a concrete back-reference and
/// reads/writes the VM's observable surface directly; the VM keeps the thin
/// <c>[RelayCommand]</c>s (ResetLayerState / ToggleAutoLayerSwitch /
/// ToggleLiveHighlighting) as delegators and exposes StopAllPolling/ResumePolling
/// so the edit flow can pause polling without referencing this class.
/// </summary>
internal sealed class LivePollingCoordinator
{
    private readonly MainWindowViewModel _vm;
    private MatrixPollingService? _matrixPolling;
    private LedPollingService? _ledPolling;

    /// <summary>
    /// Owns momentary/toggle layer caches, edge detection, and hold-threshold
    /// state. Rebuilt from layer 0 on connect; reset via ResetLayerState.
    /// WARNING: Toggle state is a best-effort local mirror of firmware; see
    /// design doc for drift sources (fast double-taps, firmware-side combos).
    /// </summary>
    private readonly LayerSwitchService _layerSwitch = new();

    /// <summary>
    /// True if the firmware responds to the standard VIA rgblight color query
    /// (0x08, 0x83). When true, we drive SelectedLayerIndex from the polled LED
    /// color instead of the matrix-based MO/TG heuristic — this catches layers
    /// the heuristic can't see (e.g. the mouse layer). Set by the VM connect path.
    /// </summary>
    public bool LedPollSupported { get; set; }

    public LivePollingCoordinator(MainWindowViewModel vm) => _vm = vm;

    public void StartMatrixPolling()
    {
        StopMatrixPolling();
        if (_vm.KeyboardConfig is null) return;

        _matrixPolling = new MatrixPollingService(
            _vm.ProtocolService, _vm.KeyboardConfig.MatrixRows, _vm.KeyboardConfig.MatrixCols);
        _matrixPolling.MatrixStateChanged += OnMatrixStateChanged;
        _matrixPolling.PollError += OnMatrixPollError;
        _matrixPolling.Start();
        _vm.RefreshStatusMessage();
    }

    private void OnMatrixPollError(string msg) =>
        Dispatcher.UIThread.Post(() => _vm.StatusMessage = Loc.Instance.Format("Status_PollingErrorFormat", msg));

    public void StopMatrixPolling()
    {
        if (_matrixPolling is null) return;
        _matrixPolling.MatrixStateChanged -= OnMatrixStateChanged;
        _matrixPolling.PollError -= OnMatrixPollError;
        _matrixPolling.Dispose();
        _matrixPolling = null;

        // Clear all pressed states
        ClearPressedStates();
    }

    public void StartLedPolling()
    {
        StopLedPolling();
        if (_vm.KeyboardConfig is null || !LedPollSupported) return;

        _ledPolling = new LedPollingService(_vm.ProtocolService);
        _ledPolling.LedColorChanged += OnLedColorChanged;
        _ledPolling.PollError += OnLedPollError;
        _ledPolling.Start();
    }

    private void OnLedPollError(string msg) =>
        Dispatcher.UIThread.Post(() => _vm.StatusMessage = Loc.Instance.Format("Status_PollingErrorFormat", msg));

    public void StopLedPolling()
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
        if (_vm.KeyboardConfig is null) return;
        // Suppress auto-switch while a save is in flight. The save reloads
        // the keymap and flips SelectedLayerIndex itself; racing LED-driven
        // posts can clobber that and leave the UI on the wrong layer.
        if (_vm.IsSaving) return;

        var match = LedColorLayerResolver.Resolve(
            _vm.KeyboardConfig.Layers, hue, sat, _vm.SelectedLayerIndex);
        if (match is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_vm.IsSaving) return;
            if (_vm.IsAutoLayerSwitchEnabled && match.Value != _vm.SelectedLayerIndex)
                _vm.SelectedLayerIndex = match.Value;
        });
    }

    public void BuildLayerSwitchCache() =>
        _layerSwitch.BuildCache(_vm.KeyboardConfig?.Layers.FirstOrDefault(l => l.Index == 0));

    private void OnMatrixStateChanged(bool[,] state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var rows = state.GetLength(0);
            var cols = state.GetLength(1);

            // Update pressed states and collect pressed keys for diagnostics
            var pressedKeys = new List<(KeyViewModel Key, string LayerName)>();
            foreach (var layerVm in _vm.Layers)
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

            _vm.Diagnostics.LogMatrixEvent(pressedKeys);

            // Skipped when LED polling is active — the LED is authoritative
            // and catches layers the matrix heuristic can't see (mouse layer,
            // firmware-internal switches, etc.).
            if (_vm.IsAutoLayerSwitchEnabled && !LedPollSupported)
            {
                var targetLayer = _layerSwitch.ResolveTargetLayer(
                    state, Environment.TickCount64, _vm.LayerHoldThresholdMs);
                if (targetLayer != _vm.SelectedLayerIndex)
                    _vm.SelectedLayerIndex = targetLayer;
            }
        });
    }

    private void ClearPressedStates()
    {
        foreach (var layerVm in _vm.Layers)
            foreach (var keyVm in layerVm.Keys)
                keyVm.IsPressed = false;
    }

    /// <summary>
    /// Resets locally tracked toggle state and returns to base layer.
    /// Use this when the visualization gets out of sync with the actual keyboard layer.
    /// </summary>
    public void ResetLayerState()
    {
        _layerSwitch.ResetRuntimeState();
        _vm.SelectedLayerIndex = 0;
    }

    public void ToggleAutoLayerSwitch()
    {
        _vm.IsAutoLayerSwitchEnabled = !_vm.IsAutoLayerSwitchEnabled;

        if (_vm.IsConnected && LedPollSupported)
        {
            if (_vm.IsAutoLayerSwitchEnabled)
                StartLedPolling();
            else
                StopLedPolling();
        }

        var settings = _vm.Settings.Load();
        _vm.Settings.Save(settings with { AutoLayerSwitch = _vm.IsAutoLayerSwitchEnabled });
    }

    public void ToggleLiveHighlighting()
    {
        _vm.IsLiveHighlightingEnabled = !_vm.IsLiveHighlightingEnabled;

        if (_vm.IsLiveHighlightingEnabled && _vm.IsConnected && !_vm.IsEditMode)
            StartMatrixPolling();
        else
            StopMatrixPolling();

        _vm.RefreshStatusMessage();

        var settings = _vm.Settings.Load();
        _vm.Settings.Save(settings with { LiveKeyHighlighting = _vm.IsLiveHighlightingEnabled });
    }

    /// <summary>Stops both polling loops. Called at device teardown (ShutdownAsync).</summary>
    public void Dispose()
    {
        StopMatrixPolling();
        StopLedPolling();
    }
}
