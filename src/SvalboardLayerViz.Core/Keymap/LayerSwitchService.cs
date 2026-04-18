using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Auto-layer-switch logic: tracks momentary (MO/LT/TT) and toggle (TG) layer
/// keys by matrix position, applies hold-threshold + rising-edge detection,
/// and resolves the highest active layer. Kept out of <c>MainWindowViewModel</c>
/// so matrix-polling callers can test layer resolution without Avalonia.
/// </summary>
public sealed class LayerSwitchService
{
    private Dictionary<(int Row, int Col), int> _momentary = new();
    private Dictionary<(int Row, int Col), int> _toggle = new();
    private readonly HashSet<int> _toggledLayers = new();
    private readonly Dictionary<(int Row, int Col), long> _momentaryPressTimestamps = new();
    private bool[,]? _previousMatrix;

    /// <summary>Rebuilds (row,col)→layer caches from the base layer. Clears runtime state.</summary>
    public void BuildCache(Layer? baseLayer)
    {
        Reset();
        if (baseLayer is null) return;
        foreach (var key in baseLayer.Keys)
        {
            if (!key.IsLayerSwitch || !key.TargetLayer.HasValue) continue;
            switch (key.SwitchType)
            {
                case LayerSwitchType.Momentary:
                    _momentary[(key.Row, key.Col)] = key.TargetLayer.Value;
                    break;
                case LayerSwitchType.Toggle:
                    _toggle[(key.Row, key.Col)] = key.TargetLayer.Value;
                    break;
            }
        }
    }

    /// <summary>Drops all caches and runtime state. Use on disconnect.</summary>
    public void Reset()
    {
        _momentary.Clear();
        _toggle.Clear();
        _toggledLayers.Clear();
        _momentaryPressTimestamps.Clear();
        _previousMatrix = null;
    }

    /// <summary>Drops runtime state (toggles, timestamps, edge-detect prev). Keeps caches.</summary>
    public void ResetRuntimeState()
    {
        _toggledLayers.Clear();
        _momentaryPressTimestamps.Clear();
        _previousMatrix = null;
    }

    /// <summary>
    /// Updates internal toggle/momentary state from the new matrix snapshot and
    /// returns the highest active layer (0 if none active).
    /// </summary>
    public int ResolveTargetLayer(bool[,] state, long nowTicks, int holdThresholdMs)
    {
        var rows = state.GetLength(0);
        var cols = state.GetLength(1);

        if (_previousMatrix is not null
            && _previousMatrix.GetLength(0) == rows
            && _previousMatrix.GetLength(1) == cols)
        {
            foreach (var (pos, layer) in _toggle)
            {
                if (pos.Row >= rows || pos.Col >= cols) continue;
                var wasPressed = _previousMatrix[pos.Row, pos.Col];
                var isPressed = state[pos.Row, pos.Col];
                if (isPressed && !wasPressed)
                {
                    if (!_toggledLayers.Remove(layer))
                        _toggledLayers.Add(layer);
                }
            }
        }
        _previousMatrix = (bool[,])state.Clone();

        foreach (var (pos, _) in _momentary)
        {
            if (pos.Row >= rows || pos.Col >= cols) continue;
            if (state[pos.Row, pos.Col])
                _momentaryPressTimestamps.TryAdd(pos, nowTicks);
            else
                _momentaryPressTimestamps.Remove(pos);
        }

        var target = 0;
        foreach (var (pos, layer) in _momentary)
        {
            if (pos.Row >= rows || pos.Col >= cols) continue;
            if (!state[pos.Row, pos.Col]) continue;
            if (!_momentaryPressTimestamps.TryGetValue(pos, out var pressedAt)) continue;
            if (nowTicks - pressedAt >= holdThresholdMs && layer > target)
                target = layer;
        }
        foreach (var layer in _toggledLayers)
            if (layer > target) target = layer;
        return target;
    }
}
