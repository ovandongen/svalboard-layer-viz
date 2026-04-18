using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.App.Localization;
using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.History;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Layout;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// A single line in the diff change list.
/// </summary>
public record DiffLineViewModel(
    int Layer, int Row, int Col,
    string OldLabel, string NewLabel, string FormattedText);

/// <summary>
/// Display record for a single key on the mini-board in the diff dialog.
/// Uses orange highlight for changed keys (vs. red in <see cref="MiniKeyDisplay"/>).
/// </summary>
public record DiffMiniKeyDisplay(
    double X, double Y, double W, double H,
    bool IsChanged, string Label)
{
    public string Background => IsChanged ? "#FAB387" : "#45475A";
    public string BorderBrush => IsChanged ? "#FAB387" : "Transparent";
    public double BorderThickness => IsChanged ? 2.0 : 0.0;
}

/// <summary>
/// Drives the snapshot diff dialog: computes differences between a snapshot
/// and the current device keymap, renders side-by-side mini-boards with
/// changed keys highlighted, and provides the Restore command that builds
/// a <see cref="KeymapEditSession"/> for the caller.
/// </summary>
public partial class SnapshotDiffDialogViewModel : ObservableObject
{
    private const double MiniScale = 18.0;
    private const double MiniKeySize = 14.0;
    private const string ChangedKeyBackground = "#FAB387";
    private const string NormalKeyBackground = "#45475A";

    private readonly KeymapSnapshot _snapshot;
    private readonly ushort[,,] _deviceKeymap;
    private readonly ushort[,,] _snapshotKeymap;
    private readonly KeycodeService _keycodeService;
    private readonly IReadOnlyList<KeyPosition> _keyPositions;
    private readonly HashSet<(int Layer, int Row, int Col)> _changedKeys;

    [ObservableProperty] private int _snapshotSelectedLayerIndex;
    [ObservableProperty] private int _deviceSelectedLayerIndex;
    [ObservableProperty] private bool _syncLayers = true;
    [ObservableProperty] private IReadOnlyList<DiffMiniKeyDisplay> _snapshotKeys = [];
    [ObservableProperty] private IReadOnlyList<DiffMiniKeyDisplay> _deviceKeys = [];
    [ObservableProperty] private bool _canRestore;
    [ObservableProperty] private string _statusMessage = "";

    public string Title { get; }
    public string WarningBanner { get; }
    public bool HasWarning => !string.IsNullOrEmpty(WarningBanner);
    public IReadOnlyList<SnapshotDiffComputer.KeyDiff> Diffs { get; }
    public IReadOnlyList<DiffLineViewModel> ChangeLines { get; }
    public int AuxChangeCount { get; private set; }
    public int ChangeCount => Diffs.Count + AuxChangeCount;
    public int LayerCount { get; }
    public int MaxLayerIndex => Math.Max(0, LayerCount - 1);
    public bool HasChanges => ChangeCount > 0;

    public string RestoreDisabledReason
    {
        get
        {
            if (!_isDeviceConnected)
                return Loc.Instance["Diff_RestoreDisabledNoDevice"];
            if (_hasPendingChanges)
                return Loc.Instance["Diff_RestoreDisabledPending"];
            return "";
        }
    }

    // Callbacks (wired by App.axaml.cs)
    public Action<KeymapEditSession>? RestoreCompleted { get; set; }
    public Action? CloseRequested { get; set; }
    public Func<string, Task<string?>>? RequestExportFilePath { get; set; }
    public Func<string, Task<bool>>? ConfirmDelete { get; set; }
    public Func<Task<bool>>? ConfirmRestore { get; set; }

    /// <summary>Exposes the loaded snapshot for export/delete operations.</summary>
    public KeymapSnapshot Snapshot => _snapshot;

    private readonly byte[]? _currentDeviceMacroBuffer;
    private readonly IReadOnlyList<byte[]>? _currentDeviceCombos;
    private readonly IReadOnlyList<byte[]>? _currentDeviceTapDances;
    private readonly bool _isDeviceConnected;
    private readonly bool _hasPendingChanges;
    private readonly bool _layersTruncated;
    private readonly int _snapshotOriginalLayerCount;

    public SnapshotDiffDialogViewModel(
        KeymapSnapshot snapshot,
        ushort[,,]? currentDeviceKeymap,
        KeyboardId? currentKeyboardId,
        KeycodeService keycodeService,
        bool isDeviceConnected = true,
        bool hasPendingChanges = false,
        byte[]? currentDeviceMacroBuffer = null,
        IReadOnlyList<byte[]>? currentDeviceCombos = null,
        IReadOnlyList<byte[]>? currentDeviceTapDances = null)
    {
        _snapshot = snapshot;
        _keycodeService = keycodeService;
        _isDeviceConnected = isDeviceConnected;
        _hasPendingChanges = hasPendingChanges;
        _currentDeviceMacroBuffer = currentDeviceMacroBuffer;
        _currentDeviceCombos = currentDeviceCombos;
        _currentDeviceTapDances = currentDeviceTapDances;
        _keyPositions = SvalboardLayout.GetKeyPositions();

        CanRestore = isDeviceConnected && !hasPendingChanges;

        // Build title
        var label = snapshot.UserLabel ?? snapshot.Reason switch
        {
            SnapshotReason.PreSave => Loc.Instance["History_PreSaveLabel"],
            SnapshotReason.PostSave => Loc.Instance["History_PostSaveLabel"],
            SnapshotReason.FirstConnect => Loc.Instance["History_FirstConnectLabel"],
            _ => Loc.Instance["History_DefaultManualLabel"],
        };
        Title = $"{Loc.Instance["Diff_Title"]} — {snapshot.CapturedAt:g} — {label}";

        // Handle null device keymap (disconnected)
        var snapshotKm = snapshot.ToKeymap();
        _snapshotOriginalLayerCount = snapshotKm.GetLength(0);

        if (currentDeviceKeymap is null)
        {
            // No device: show empty diff, restore disabled
            _deviceKeymap = snapshotKm;
            _snapshotKeymap = snapshotKm;
            Diffs = [];
            ChangeLines = [];
            _changedKeys = [];
            LayerCount = _snapshotOriginalLayerCount;
            WarningBanner = "";
            RebuildMiniBoards();
            return;
        }

        // Truncate layers if needed
        var deviceLayers = currentDeviceKeymap.GetLength(0);
        _layersTruncated = _snapshotOriginalLayerCount != deviceLayers;
        var minLayers = Math.Min(_snapshotOriginalLayerCount, deviceLayers);
        LayerCount = minLayers;

        (_deviceKeymap, _snapshotKeymap) = TruncateToMinLayers(currentDeviceKeymap, snapshotKm);

        // Compute diffs
        Diffs = SnapshotDiffComputer.Diff(_deviceKeymap, _snapshotKeymap);
        _changedKeys = new HashSet<(int, int, int)>(
            Diffs.Select(d => (d.Layer, d.Row, d.Col)));

        // Build change lines
        var lines = Diffs.Select(d =>
        {
            var oldInfo = _keycodeService.Resolve(d.OldCode);
            var newInfo = _keycodeService.Resolve(d.NewCode);
            var pos = _keyPositions.FirstOrDefault(p => p.Row == d.Row && p.Col == d.Col);
            var posLabel = pos is not null ? $" ({pos.Cluster}, {pos.Direction})" : "";
            var text = $"L{d.Layer} R{d.Row} C{d.Col}{posLabel}: {oldInfo.Label} → {newInfo.Label}";
            return new DiffLineViewModel(d.Layer, d.Row, d.Col, oldInfo.Label, newInfo.Label, text);
        }).ToList();

        // Fold combo/tap-dance/macro diffs into the visible change list so
        // restore-worthy changes aren't hidden behind "No changes".
        if (snapshot.Combos is not null && currentDeviceCombos is not null)
        {
            var n = Math.Min(snapshot.Combos.Length, currentDeviceCombos.Count);
            for (var i = 0; i < n; i++)
                if (!currentDeviceCombos[i].AsSpan().SequenceEqual(snapshot.Combos[i]))
                {
                    lines.Add(new DiffLineViewModel(-1, -1, -1, "", "",
                        $"Combo C{i}: modified"));
                    AuxChangeCount++;
                }
        }
        if (snapshot.TapDances is not null && currentDeviceTapDances is not null)
        {
            var n = Math.Min(snapshot.TapDances.Length, currentDeviceTapDances.Count);
            for (var i = 0; i < n; i++)
                if (!currentDeviceTapDances[i].AsSpan().SequenceEqual(snapshot.TapDances[i]))
                {
                    lines.Add(new DiffLineViewModel(-1, -1, -1, "", "",
                        $"Tap Dance TD{i}: modified"));
                    AuxChangeCount++;
                }
        }
        if (snapshot.MacroBuffer is not null && currentDeviceMacroBuffer is not null
            && !currentDeviceMacroBuffer.AsSpan().SequenceEqual(snapshot.MacroBuffer))
        {
            lines.Add(new DiffLineViewModel(-1, -1, -1, "", "", "Macros: modified"));
            AuxChangeCount++;
        }
        ChangeLines = lines;

        // Build warning banner
        var warnings = new List<string>();
        if (currentKeyboardId is not null &&
            snapshot.KeyboardId.Uid != currentKeyboardId.Uid)
        {
            warnings.Add(Loc.Instance["Diff_FirmwareWarning"]);
        }
        if (_layersTruncated && _snapshotOriginalLayerCount > deviceLayers)
        {
            warnings.Add(Loc.Instance.Format("Diff_LayerTruncateWarning",
                _snapshotOriginalLayerCount, deviceLayers, deviceLayers - 1));
        }
        WarningBanner = string.Join("\n", warnings);

        RebuildMiniBoards();
    }

    partial void OnSnapshotSelectedLayerIndexChanged(int value)
    {
        if (SyncLayers)
            DeviceSelectedLayerIndex = value;
        RebuildMiniBoards();
    }

    partial void OnDeviceSelectedLayerIndexChanged(int value)
    {
        if (SyncLayers)
            SnapshotSelectedLayerIndex = value;
        RebuildMiniBoards();
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (!CanRestore || ConfirmRestore is null) return;

        var confirmed = await ConfirmRestore();
        if (!confirmed) return;

        var session = new KeymapEditSession(
            _deviceKeymap, settings: null, settingWidths: null,
            macroBuffer: _currentDeviceMacroBuffer,
            combos: _currentDeviceCombos,
            tapDances: _currentDeviceTapDances);
        var ops = SnapshotDiffComputer.ToEditOps(Diffs);
        foreach (var op in ops)
            session.Apply(op);

        if (_snapshot.MacroBuffer is not null && _currentDeviceMacroBuffer is not null
            && !_currentDeviceMacroBuffer.AsSpan().SequenceEqual(_snapshot.MacroBuffer))
        {
            session.Apply(new SetMacroBufferOp(_currentDeviceMacroBuffer, _snapshot.MacroBuffer));
        }

        if (_snapshot.Combos is not null && _currentDeviceCombos is not null)
        {
            var count = Math.Min(_snapshot.Combos.Length, _currentDeviceCombos.Count);
            for (var i = 0; i < count; i++)
            {
                var cur = _currentDeviceCombos[i];
                var snap = _snapshot.Combos[i];
                if (!cur.AsSpan().SequenceEqual(snap))
                    session.Apply(new SetComboOp(i, cur, snap));
            }
        }

        if (_snapshot.TapDances is not null && _currentDeviceTapDances is not null)
        {
            var count = Math.Min(_snapshot.TapDances.Length, _currentDeviceTapDances.Count);
            for (var i = 0; i < count; i++)
            {
                var cur = _currentDeviceTapDances[i];
                var snap = _snapshot.TapDances[i];
                if (!cur.AsSpan().SequenceEqual(snap))
                    session.Apply(new SetTapDanceOp(i, cur, snap));
            }
        }

        DiagnosticLog.Info("Snapshot",
            $"Restore initiated: {_snapshot.CapturedAt:s} → device ({Diffs.Count} changes)");

        RestoreCompleted?.Invoke(session);
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (RequestExportFilePath is null) return;
        var suggested = $"snapshot-{_snapshot.CapturedAt:yyyy-MM-dd-HHmmss}.json";
        var path = await RequestExportFilePath(suggested);
        if (path is not null)
        {
            StatusMessage = Loc.Instance.Format("History_ExportedStatus",
                _snapshot.UserLabel ?? _snapshot.CapturedAt.ToString("g"));
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (ConfirmDelete is null) return;
        var label = _snapshot.UserLabel ?? _snapshot.CapturedAt.ToString("g");
        var confirmed = await ConfirmDelete(label);
        if (confirmed)
            CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    private void RebuildMiniBoards()
    {
        var snapshotLayer = Math.Clamp(SnapshotSelectedLayerIndex, 0, Math.Max(0, LayerCount - 1));
        var deviceLayer = Math.Clamp(DeviceSelectedLayerIndex, 0, Math.Max(0, LayerCount - 1));

        var snapshotDisplays = new List<DiffMiniKeyDisplay>(_keyPositions.Count);
        var deviceDisplays = new List<DiffMiniKeyDisplay>(_keyPositions.Count);

        foreach (var pos in _keyPositions)
        {
            var isChanged = _changedKeys.Contains((snapshotLayer, pos.Row, pos.Col));
            var snapshotCode = _snapshotKeymap[snapshotLayer, pos.Row, pos.Col];
            var snapshotLabel = isChanged ? _keycodeService.Resolve(snapshotCode).Label : "";

            snapshotDisplays.Add(new DiffMiniKeyDisplay(
                X: pos.X * MiniScale,
                Y: pos.Y * MiniScale,
                W: MiniKeySize,
                H: MiniKeySize,
                IsChanged: isChanged,
                Label: snapshotLabel));

            var isChangedDevice = _changedKeys.Contains((deviceLayer, pos.Row, pos.Col));
            var deviceCode = _deviceKeymap[deviceLayer, pos.Row, pos.Col];
            var deviceLabel = isChangedDevice ? _keycodeService.Resolve(deviceCode).Label : "";

            deviceDisplays.Add(new DiffMiniKeyDisplay(
                X: pos.X * MiniScale,
                Y: pos.Y * MiniScale,
                W: MiniKeySize,
                H: MiniKeySize,
                IsChanged: isChangedDevice,
                Label: deviceLabel));
        }

        SnapshotKeys = snapshotDisplays;
        DeviceKeys = deviceDisplays;
    }

    private static (ushort[,,] Device, ushort[,,] Snapshot) TruncateToMinLayers(
        ushort[,,] device, ushort[,,] snapshot)
    {
        var deviceLayers = device.GetLength(0);
        var snapshotLayers = snapshot.GetLength(0);
        var rows = device.GetLength(1);
        var cols = device.GetLength(2);
        var minLayers = Math.Min(deviceLayers, snapshotLayers);

        if (deviceLayers == snapshotLayers)
            return (device, snapshot);

        var d = new ushort[minLayers, rows, cols];
        var s = new ushort[minLayers, rows, cols];

        for (var l = 0; l < minLayers; l++)
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                {
                    d[l, r, c] = device[l, r, c];
                    s[l, r, c] = snapshot[l, r, c];
                }

        return (d, s);
    }
}
