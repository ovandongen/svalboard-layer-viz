namespace SvalboardLayerViz.Core.Protocol;

/// <summary>
/// Polls the keyboard's current rgblight hue+sat at ~10Hz as a proxy for the
/// active layer. On Svalboards where the layer indicator LED is driven through
/// QMK rgblight, the displayed color uniquely (or near-uniquely) identifies
/// which layer is currently active — including layers entered by means the
/// matrix-based heuristic can't observe (e.g. the mouse layer).
///
/// Uses the standard VIA id_lighting_get_value + id_qmk_rgblight_color command
/// (0x08, 0x83). Probe confirmed on Svalboard Lightly firmware (sval proto 3).
/// </summary>
public class LedPollingService : IDisposable
{
    private readonly IVialProtocolService _protocol;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private (byte H, byte S)? _lastColor;

    /// <summary>Fired when the polled rgblight hue+sat changes.</summary>
    public event Action<byte, byte>? LedColorChanged;

    /// <summary>Fired when an error occurs during polling. Argument is the exception message.</summary>
    public event Action<string>? PollError;

    public bool IsRunning => _pollTask is not null && !_pollTask.IsCompleted;

    public LedPollingService(IVialProtocolService protocol)
    {
        _protocol = protocol;
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
        _lastColor = null;
    }

    private async Task PollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var color = _protocol.GetCurrentLedHueSat();
                if (color is not null &&
                    (_lastColor is null ||
                     _lastColor.Value.H != color.Value.H ||
                     _lastColor.Value.S != color.Value.S))
                {
                    _lastColor = color;
                    LedColorChanged?.Invoke(color.Value.H, color.Value.S);
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

    public void Dispose()
    {
        Stop();
    }
}
