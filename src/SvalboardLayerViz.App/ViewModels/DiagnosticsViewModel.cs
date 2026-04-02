using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// Represents a single pressed key with all its details for the diagnostics grid.
/// </summary>
public record PressedKeyEntry(
    int Row,
    int Col,
    string Label,
    string? Modifier,
    string Keycode,
    int Layer,
    string LayerName,
    bool IsTransparent,
    bool IsLayerSwitch,
    int? TargetLayer)
{
    public string Flags
    {
        get
        {
            var parts = new List<string>();
            if (IsTransparent) parts.Add("TRNS");
            if (IsLayerSwitch) parts.Add($"->L{TargetLayer}");
            return string.Join(" ", parts);
        }
    }
}

/// <summary>
/// ViewModel for the matrix diagnostics popup window.
/// Shows a live grid of currently pressed keys and a scrolling event log.
/// </summary>
public partial class DiagnosticsViewModel : ObservableObject
{
    private const int MaxLogEntries = 500;

    [ObservableProperty]
    private int _eventCount;

    [ObservableProperty]
    private int _pressedCount;

    public ObservableCollection<PressedKeyEntry> PressedKeys { get; } = [];
    public ObservableCollection<string> LogEntries { get; } = [];

    /// <summary>
    /// Logs a matrix event with full key details from all layers.
    /// </summary>
    public void LogMatrixEvent(IReadOnlyList<(KeyViewModel Key, string LayerName)> pressedKeys)
    {
        EventCount++;
        PressedCount = pressedKeys.Count;

        // Update the live grid
        PressedKeys.Clear();
        foreach (var (kvm, layerName) in pressedKeys)
        {
            PressedKeys.Add(new PressedKeyEntry(
                Row: kvm.Key.Row,
                Col: kvm.Key.Col,
                Label: kvm.DisplayLabel,
                Modifier: kvm.SecondaryLabel,
                Keycode: kvm.HexKeycode,
                Layer: kvm.Layer.Index,
                LayerName: layerName,
                IsTransparent: kvm.IsTransparent,
                IsLayerSwitch: kvm.IsLayerSwitch,
                TargetLayer: kvm.TargetLayer));
        }

        // Build log entry
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        if (pressedKeys.Count == 0)
        {
            LogEntries.Insert(0, $"[{timestamp}]  #{EventCount,-5}  --- all released ---");
        }
        else
        {
            foreach (var (kvm, layerName) in pressedKeys)
            {
                var mod = kvm.SecondaryLabel is not null ? $"{kvm.SecondaryLabel}+" : "";
                var trans = kvm.IsTransparent ? " (trns)" : "";
                var lswitch = kvm.IsLayerSwitch ? $" -> L{kvm.TargetLayer}" : "";
                LogEntries.Insert(0,
                    $"[{timestamp}]  #{EventCount,-5}  R{kvm.Key.Row}C{kvm.Key.Col}  " +
                    $"{mod}{kvm.DisplayLabel,-12}  {kvm.HexKeycode}  " +
                    $"L{kvm.Layer.Index} {layerName}{trans}{lswitch}");
            }
        }

        while (LogEntries.Count > MaxLogEntries)
            LogEntries.RemoveAt(LogEntries.Count - 1);
    }

    [RelayCommand]
    private void Clear()
    {
        LogEntries.Clear();
        PressedKeys.Clear();
        EventCount = 0;
        PressedCount = 0;
    }
}
