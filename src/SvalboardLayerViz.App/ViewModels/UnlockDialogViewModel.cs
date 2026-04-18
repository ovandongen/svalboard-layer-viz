using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Protocol;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// Display record for a single key on the mini-board in the unlock dialog.
/// </summary>
public record MiniKeyDisplay(
    double X, double Y, double W, double H,
    bool IsHighlighted, string Label)
{
    /// <summary>Background color string for AXAML binding.</summary>
    public string Background => IsHighlighted ? "#F38BA8" : "#45475A";
}

/// <summary>
/// Drives the unlock dialog: polls the device at 250ms intervals,
/// tracks which keys the user must hold, and auto-dismisses on unlock.
/// </summary>
public partial class UnlockDialogViewModel : ObservableObject
{
    private const double MiniScale = 18.0; // pixels per layout unit
    private const double MiniKeySize = 14.0; // key square size in pixels

    private readonly IVialProtocolService _protocol;
    private readonly Func<TimeSpan, Action, IDisposable> _timerFactory;
    private IDisposable? _pollTimer;
    private bool _pollInFlight;

    [ObservableProperty] private bool _isUnlocked;
    [ObservableProperty] private bool _isPolling;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private IReadOnlyList<MiniKeyDisplay> _allKeyPositions = [];

    /// <summary>Invoked when the keyboard is successfully unlocked.</summary>
    public Action? UnlockCompleted { get; set; }

    /// <summary>Invoked when the user cancels the unlock flow.</summary>
    public Action? Cancelled { get; set; }

    public UnlockDialogViewModel(
        IVialProtocolService protocol,
        Func<TimeSpan, Action, IDisposable>? timerFactory = null)
    {
        _protocol = protocol;
        _timerFactory = timerFactory ?? CreateDefaultTimer;
    }

    /// <summary>
    /// Begins the unlock flow. Call after the dialog is shown.
    /// </summary>
    public async Task BeginAsync()
    {
        var status = await Task.Run(() => _protocol.GetUnlockStatus());

        if (status.Unlocked)
        {
            IsUnlocked = true;
            StatusText = "Keyboard unlocked!";
            UnlockCompleted?.Invoke();
            return;
        }

        // Build the mini-board with highlighted keys
        var keysToHold = new HashSet<(int Row, int Col)>(status.KeysToHold);
        BuildMiniBoard(keysToHold);

        StatusText = "Hold the highlighted keys to unlock...";

        // Start the unlock sequence on the device
        await Task.Run(() => _protocol.UnlockStart());

        // Begin polling
        IsPolling = true;
        _pollTimer = _timerFactory(TimeSpan.FromMilliseconds(250), OnPollTick);
    }

    [RelayCommand]
    private void Cancel()
    {
        StopPolling();
        Cancelled?.Invoke();
    }

    private async void OnPollTick()
    {
        if (_pollInFlight) return;
        _pollInFlight = true;

        try
        {
            var unlocked = await Task.Run(() => _protocol.UnlockPoll());
            if (unlocked)
            {
                StopPolling();
                IsUnlocked = true;
                StatusText = "Keyboard unlocked!";
                UnlockCompleted?.Invoke();
            }
        }
        finally
        {
            _pollInFlight = false;
        }
    }

    private void StopPolling()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
        IsPolling = false;
    }

    private void BuildMiniBoard(HashSet<(int Row, int Col)> keysToHold)
    {
        var positions = SvalboardLayout.GetKeyPositions();
        var displays = new List<MiniKeyDisplay>(positions.Count);

        foreach (var pos in positions)
        {
            var highlighted = keysToHold.Contains((pos.Row, pos.Col));
            var label = highlighted ? $"{pos.Cluster} {pos.Direction}" : "";
            displays.Add(new MiniKeyDisplay(
                X: pos.X * MiniScale,
                Y: pos.Y * MiniScale,
                W: MiniKeySize,
                H: MiniKeySize,
                IsHighlighted: highlighted,
                Label: label));
        }

        AllKeyPositions = displays;
    }

    /// <summary>
    /// Default timer factory using System.Threading.Timer for non-UI scenarios.
    /// Production code should inject a DispatcherTimer-based factory.
    /// </summary>
    private static IDisposable CreateDefaultTimer(TimeSpan interval, Action tick)
    {
        var timer = new System.Threading.Timer(_ => tick(), null, interval, interval);
        return timer;
    }
}
