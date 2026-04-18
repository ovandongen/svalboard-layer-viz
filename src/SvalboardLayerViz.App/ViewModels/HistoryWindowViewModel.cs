using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.App.Localization;
using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.History;
using SvalboardLayerViz.Core.Keymap;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// Drives the HistoryWindow: lists snapshots for the connected keyboard,
/// supports filter chips (Manual / Auto / Initial) + search, bulk
/// import / export / delete, and diff preview + restore.
/// </summary>
public partial class HistoryWindowViewModel : ObservableObject
{
    private readonly ISnapshotService _snapshots;
    private readonly KeyboardId? _keyboardId;
    private readonly ushort[,,]? _currentDeviceKeymap;
    private readonly KeycodeService? _keycodeService;
    private readonly byte[]? _currentDeviceMacroBuffer;
    private readonly IReadOnlyList<byte[]>? _currentDeviceCombos;
    private readonly IReadOnlyList<byte[]>? _currentDeviceTapDances;
    private readonly List<SnapshotRowViewModel> _allRows = [];

    public ObservableCollection<SnapshotRowViewModel> Rows { get; } = [];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _showManual = true;
    [ObservableProperty] private bool _showAuto = true;
    [ObservableProperty] private bool _showInitial = true;
    [ObservableProperty] private SnapshotRowViewModel? _selectedRow;
    [ObservableProperty] private string _statusMessage = "";

    /// <summary>Folder picker for export-all. App layer wires StorageProvider; tests inject fake.</summary>
    public Func<Task<string?>>? RequestExportFolderPath { get; set; }

    /// <summary>Save-file picker for single-row export. Receives a suggested filename.</summary>
    public Func<string, Task<string?>>? RequestExportFilePath { get; set; }

    /// <summary>Folder picker for import.</summary>
    public Func<Task<string?>>? RequestImportFolderPath { get; set; }

    /// <summary>Confirms a destructive delete. Receives the row's display label.</summary>
    public Func<string, Task<bool>>? ConfirmDelete { get; set; }

    /// <summary>Surfaces an import summary modal. Optional.</summary>
    public Func<ImportResult, Task>? ShowImportSummary { get; set; }

    /// <summary>Closes the dialog. Wired to Window.Close in the App layer.</summary>
    public Action? CloseRequested { get; set; }

    /// <summary>Opens the diff dialog for a snapshot. App layer wires Window creation.</summary>
    public Func<SnapshotDiffDialogViewModel, Task>? OpenDiffDialog { get; set; }

    /// <summary>Fires when user confirms a restore. Bubbles up to MainWindowViewModel.</summary>
    public Action<KeymapEditSession>? RestoreCompleted { get; set; }

    /// <summary>Whether the device is currently connected (controls restore eligibility).</summary>
    public bool IsDeviceConnected { get; set; }

    /// <summary>Whether the main edit session has pending unsaved changes.</summary>
    public bool HasPendingChanges { get; set; }

    public HistoryWindowViewModel(
        ISnapshotService snapshots,
        KeyboardId? keyboardId,
        ushort[,,]? currentDeviceKeymap = null,
        KeycodeService? keycodeService = null,
        byte[]? currentDeviceMacroBuffer = null,
        IReadOnlyList<byte[]>? currentDeviceCombos = null,
        IReadOnlyList<byte[]>? currentDeviceTapDances = null)
    {
        _snapshots = snapshots;
        _keyboardId = keyboardId;
        _currentDeviceKeymap = currentDeviceKeymap;
        _keycodeService = keycodeService;
        _currentDeviceMacroBuffer = currentDeviceMacroBuffer;
        _currentDeviceCombos = currentDeviceCombos;
        _currentDeviceTapDances = currentDeviceTapDances;
    }

    /// <summary>
    /// Loads the snapshot list for the current keyboard. Call once after construction.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var list = await _snapshots.ListAsync(_keyboardId, ct);
        _allRows.Clear();
        foreach (var meta in list)
            _allRows.Add(new SnapshotRowViewModel(meta));
        ApplyFilter();
        DiagnosticLog.Info("History", $"Loaded {_allRows.Count} snapshot(s)");
    }

    [RelayCommand]
    private Task RefreshAsync(CancellationToken ct) => LoadAsync(ct);

    [RelayCommand]
    private async Task DeleteAsync(SnapshotRowViewModel? row)
    {
        if (row is null) return;
        if (ConfirmDelete is not null)
        {
            var ok = await ConfirmDelete(row.DisplayLabel);
            if (!ok) return;
        }

        try
        {
            await _snapshots.DeleteAsync(row.FilePath);
            _allRows.Remove(row);
            ApplyFilter();
            StatusMessage = Loc.Instance.Format("History_DeletedStatus", row.DisplayLabel);
            DiagnosticLog.Info("History", $"Deleted snapshot: {row.FilePath}");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            DiagnosticLog.Error("History", $"Delete failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ExportAsync(SnapshotRowViewModel? row)
    {
        if (row is null || RequestExportFilePath is null) return;
        var suggested = Path.GetFileName(row.FilePath);
        var dest = await RequestExportFilePath(suggested);
        if (string.IsNullOrEmpty(dest)) return;

        try
        {
            var snapshot = await _snapshots.LoadAsync(row.FilePath);
            await _snapshots.ExportAsync(snapshot, dest);
            StatusMessage = Loc.Instance.Format("History_ExportedStatus", row.DisplayLabel);
            DiagnosticLog.Info("History", $"Exported snapshot to {dest}");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            DiagnosticLog.Error("History", $"Export failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ExportAllAsync()
    {
        if (RequestExportFolderPath is null) return;
        var folder = await RequestExportFolderPath();
        if (string.IsNullOrEmpty(folder)) return;

        var exported = 0;
        foreach (var row in _allRows)
        {
            try
            {
                var snapshot = await _snapshots.LoadAsync(row.FilePath);
                var dest = Path.Combine(folder, Path.GetFileName(row.FilePath));
                await _snapshots.ExportAsync(snapshot, dest);
                exported++;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("History", $"Export-all skipped {row.FilePath}: {ex.Message}");
            }
        }
        StatusMessage = Loc.Instance.Format("History_ExportedAllStatus", exported);
        DiagnosticLog.Info("History", $"Exported {exported} snapshot(s) to {folder}");
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (RequestImportFolderPath is null) return;
        if (_keyboardId is null)
        {
            StatusMessage = Loc.Instance["History_NoDevice"];
            return;
        }
        var folder = await RequestImportFolderPath();
        if (string.IsNullOrEmpty(folder)) return;

        var totalImported = 0;
        var totalSkipped = 0;
        var totalRefused = 0;
        var warnings = new List<string>();

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "snapshot-*.json"))
            {
                var result = await _snapshots.ImportAsync(file, _keyboardId);
                totalImported += result.Imported;
                totalSkipped += result.SkippedDuplicates;
                totalRefused += result.RefusedWrongKeyboard;
                warnings.AddRange(result.Warnings);
            }
        }
        catch (Exception ex)
        {
            warnings.Add(ex.Message);
            DiagnosticLog.Error("History", $"Import enumeration failed: {ex.Message}");
        }

        var combined = new ImportResult(totalImported, totalSkipped, totalRefused, warnings);
        if (ShowImportSummary is not null)
            await ShowImportSummary(combined);

        StatusMessage = Loc.Instance.Format("History_ImportSummary",
            combined.Imported, combined.SkippedDuplicates, combined.RefusedWrongKeyboard);
        DiagnosticLog.Info("History",
            $"Imported {combined.Imported}, skipped {combined.SkippedDuplicates}, refused {combined.RefusedWrongKeyboard}");

        if (combined.Imported > 0)
            await LoadAsync();
    }

    [RelayCommand]
    private async Task OpenDiffAsync(SnapshotRowViewModel? row)
    {
        if (row is null || OpenDiffDialog is null) return;

        try
        {
            var snapshot = await _snapshots.LoadAsync(row.FilePath);
            var diffVm = new SnapshotDiffDialogViewModel(
                snapshot,
                _currentDeviceKeymap,
                _keyboardId,
                _keycodeService ?? new KeycodeService(),
                isDeviceConnected: IsDeviceConnected,
                hasPendingChanges: HasPendingChanges,
                currentDeviceMacroBuffer: _currentDeviceMacroBuffer,
                currentDeviceCombos: _currentDeviceCombos,
                currentDeviceTapDances: _currentDeviceTapDances);

            diffVm.RestoreCompleted = session =>
            {
                RestoreCompleted?.Invoke(session);
            };

            await OpenDiffDialog(diffVm);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            DiagnosticLog.Error("History", $"OpenDiff failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnShowManualChanged(bool value) => ApplyFilter();
    partial void OnShowAutoChanged(bool value) => ApplyFilter();
    partial void OnShowInitialChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        Rows.Clear();
        var search = SearchText?.Trim() ?? "";
        foreach (var row in _allRows)
        {
            if (!ChipAllows(row.Reason)) continue;
            if (search.Length > 0 &&
                row.DisplayLabel.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            Rows.Add(row);
        }

        if (Rows.Count == 0)
        {
            StatusMessage = _allRows.Count == 0
                ? Loc.Instance["History_Empty"]
                : Loc.Instance["History_NoMatch"];
        }
    }

    private bool ChipAllows(SnapshotReason reason) => reason switch
    {
        SnapshotReason.Manual => ShowManual,
        SnapshotReason.PreSave => ShowAuto,
        SnapshotReason.PostSave => ShowAuto,
        SnapshotReason.FirstConnect => ShowInitial,
        _ => true,
    };
}
