namespace SvalboardLayerViz.Core.Protocol;

/// <summary>
/// Polls the keyboard's switch matrix state at ~10Hz to detect which physical keys
/// are currently pressed. Uses the standard VIA id_switch_matrix_state command.
/// </summary>
public class MatrixPollingService : IDisposable
{
    private readonly IVialProtocolService _protocol;
    private readonly int _rows;
    private readonly int _cols;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private bool[,]? _lastState;

    /// <summary>Fired when the matrix state changes. Argument is bool[rows, cols].</summary>
    public event Action<bool[,]>? MatrixStateChanged;

    /// <summary>Fired when an error occurs during polling. Argument is the exception message.</summary>
    public event Action<string>? PollError;

    public bool IsRunning => _pollTask is not null && !_pollTask.IsCompleted;

    public MatrixPollingService(IVialProtocolService protocol, int rows, int cols)
    {
        _protocol = protocol;
        _rows = rows;
        _cols = cols;
    }

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _pollTask = Task.Run(async () => await PollLoop(token), token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _pollTask?.Wait(500); } catch { /* expected on cancel */ }
        _cts?.Dispose();
        _cts = null;
        _pollTask = null;
        _lastState = null;
    }

    private async Task PollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var state = _protocol.GetSwitchMatrixState(_rows, _cols);

                // Fire on state change, OR while any key is pressed (so hold
                // timers in auto-layer-switch can accumulate elapsed time).
                if (!MatrixEquals(state, _lastState) || AnyKeyPressed(state))
                {
                    _lastState = state;
                    MatrixStateChanged?.Invoke(state);
                }

                await Task.Delay(100, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                PollError?.Invoke(ex.Message);
                break;
            }
        }
    }

    private bool AnyKeyPressed(bool[,] state)
    {
        for (var row = 0; row < _rows; row++)
            for (var col = 0; col < _cols; col++)
                if (state[row, col])
                    return true;
        return false;
    }

    private bool MatrixEquals(bool[,] a, bool[,]? b)
    {
        if (b is null) return false;
        for (var row = 0; row < _rows; row++)
            for (var col = 0; col < _cols; col++)
                if (a[row, col] != b[row, col])
                    return false;
        return true;
    }

    public void Dispose()
    {
        Stop();
    }
}
