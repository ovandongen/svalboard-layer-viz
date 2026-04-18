namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// In-memory edit state for a keymap and QMK settings. Tracks all changes
/// relative to a baseline snapshot (the state read from the device), supports
/// undo/redo, and generates the minimal set of device writes needed to persist
/// pending changes.
/// </summary>
public class KeymapEditSession
{
    private readonly ushort[,,] _baseline;
    private readonly ushort[,,] _current;
    private readonly Dictionary<ushort, ushort> _baselineSettings;
    private readonly Dictionary<ushort, ushort> _currentSettings;
    private readonly Dictionary<ushort, byte> _settingWidths;
    private byte[]? _baselineMacroBuffer;
    private byte[]? _currentMacroBuffer;
    private readonly List<byte[]> _baselineCombos;
    private readonly List<byte[]> _currentCombos;
    private readonly List<byte[]> _baselineTapDances;
    private readonly List<byte[]> _currentTapDances;
    private readonly Stack<EditOp> _undoStack = new();
    private readonly Stack<EditOp> _redoStack = new();

    public int Layers { get; }
    public int Rows { get; }
    public int Cols { get; }

    /// <summary>
    /// Creates an edit session from a baseline keymap snapshot (no settings).
    /// The baseline is cloned so the original is not mutated.
    /// </summary>
    public KeymapEditSession(ushort[,,] baseline)
        : this(baseline, null) { }

    /// <summary>
    /// Creates an edit session from a baseline keymap snapshot and optional
    /// QMK settings baseline. Both are cloned so the originals are not mutated.
    /// </summary>
    /// <param name="settingWidths">Byte widths per setting ID (from firmware query). Used for device writes.</param>
    public KeymapEditSession(
        ushort[,,] baseline,
        IReadOnlyDictionary<ushort, ushort>? settings,
        IReadOnlyDictionary<ushort, byte>? settingWidths = null,
        byte[]? macroBuffer = null,
        IReadOnlyList<byte[]>? combos = null,
        IReadOnlyList<byte[]>? tapDances = null)
    {
        Layers = baseline.GetLength(0);
        Rows = baseline.GetLength(1);
        Cols = baseline.GetLength(2);

        _baseline = (ushort[,,])baseline.Clone();
        _current = (ushort[,,])baseline.Clone();
        _baselineSettings = settings is not null
            ? new Dictionary<ushort, ushort>(settings)
            : new Dictionary<ushort, ushort>();
        _currentSettings = new Dictionary<ushort, ushort>(_baselineSettings);
        _settingWidths = settingWidths is not null
            ? new Dictionary<ushort, byte>(settingWidths)
            : new Dictionary<ushort, byte>();
        _baselineMacroBuffer = macroBuffer is not null ? (byte[])macroBuffer.Clone() : null;
        _currentMacroBuffer = macroBuffer is not null ? (byte[])macroBuffer.Clone() : null;
        _baselineCombos = CloneEntries(combos);
        _currentCombos = CloneEntries(combos);
        _baselineTapDances = CloneEntries(tapDances);
        _currentTapDances = CloneEntries(tapDances);
    }

    private static List<byte[]> CloneEntries(IReadOnlyList<byte[]>? source) =>
        source is null ? [] : source.Select(e => (byte[])e.Clone()).ToList();

    /// <summary>
    /// Creates an edit session from a flat snapshot array (e.g. from JSON history).
    /// </summary>
    public static KeymapEditSession FromSnapshot(ushort[] flat, int layers, int rows, int cols)
    {
        if (flat.Length != layers * rows * cols)
            throw new ArgumentException(
                $"Snapshot length {flat.Length} doesn't match dimensions {layers}x{rows}x{cols}");

        var baseline = new ushort[layers, rows, cols];
        var i = 0;
        for (var l = 0; l < layers; l++)
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                    baseline[l, r, c] = flat[i++];

        return new KeymapEditSession(baseline);
    }

    /// <summary>True if any key or setting differs from the baseline.</summary>
    public bool HasPendingChanges
    {
        get
        {
            for (var l = 0; l < Layers; l++)
                for (var r = 0; r < Rows; r++)
                    for (var c = 0; c < Cols; c++)
                        if (_current[l, r, c] != _baseline[l, r, c])
                            return true;
            return HasPendingSettingsChanges || HasPendingMacroChanges
                || HasPendingComboChanges || HasPendingTapDanceChanges;
        }
    }

    /// <summary>True if any combo entry differs from the baseline.</summary>
    public bool HasPendingComboChanges => PendingComboIndices().Any();

    /// <summary>True if any tap-dance entry differs from the baseline.</summary>
    public bool HasPendingTapDanceChanges => PendingTapDanceIndices().Any();

    private IEnumerable<int> PendingComboIndices()
    {
        for (var i = 0; i < _currentCombos.Count; i++)
            if (!_baselineCombos[i].AsSpan().SequenceEqual(_currentCombos[i]))
                yield return i;
    }

    private IEnumerable<int> PendingTapDanceIndices()
    {
        for (var i = 0; i < _currentTapDances.Count; i++)
            if (!_baselineTapDances[i].AsSpan().SequenceEqual(_currentTapDances[i]))
                yield return i;
    }

    /// <summary>True if any QMK setting differs from the baseline.</summary>
    public bool HasPendingSettingsChanges
    {
        get
        {
            foreach (var (id, val) in _currentSettings)
                if (!_baselineSettings.TryGetValue(id, out var bv) || bv != val)
                    return true;
            return false;
        }
    }

    /// <summary>True if the macro buffer has been modified from the baseline.</summary>
    public bool HasPendingMacroChanges
    {
        get
        {
            if (_baselineMacroBuffer is null || _currentMacroBuffer is null)
                return false;
            return !_baselineMacroBuffer.AsSpan().SequenceEqual(_currentMacroBuffer);
        }
    }

    /// <summary>
    /// Returns all keys that differ from the baseline, as (layer, row, col, oldCode, newCode) tuples.
    /// </summary>
    public IReadOnlyList<(int Layer, int Row, int Col, ushort OldCode, ushort NewCode)> PendingChanges
    {
        get
        {
            var changes = new List<(int, int, int, ushort, ushort)>();
            for (var l = 0; l < Layers; l++)
                for (var r = 0; r < Rows; r++)
                    for (var c = 0; c < Cols; c++)
                    {
                        var old = _baseline[l, r, c];
                        var cur = _current[l, r, c];
                        if (cur != old)
                            changes.Add((l, r, c, old, cur));
                    }
            return changes;
        }
    }

    /// <summary>
    /// Returns all QMK settings that differ from the baseline.
    /// </summary>
    public IReadOnlyList<(ushort SettingId, ushort OldValue, ushort NewValue)> PendingSettingsChanges
    {
        get
        {
            var changes = new List<(ushort, ushort, ushort)>();
            foreach (var (id, cur) in _currentSettings)
            {
                var old = _baselineSettings.GetValueOrDefault(id);
                if (cur != old)
                    changes.Add((id, old, cur));
            }
            return changes;
        }
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public int UndoCount => _undoStack.Count;
    public int RedoCount => _redoStack.Count;

    /// <summary>
    /// Gets the current keycode at the given position (reflecting any pending edits).
    /// </summary>
    public ushort GetCurrent(int layer, int row, int col) => _current[layer, row, col];

    /// <summary>
    /// Gets the baseline keycode at the given position (as read from device).
    /// </summary>
    public ushort GetBaseline(int layer, int row, int col) => _baseline[layer, row, col];

    /// <summary>
    /// Gets the current value of a QMK setting, or null if the setting is not tracked.
    /// </summary>
    public ushort? GetCurrentSetting(ushort id) =>
        _currentSettings.TryGetValue(id, out var v) ? v : null;

    /// <summary>
    /// Gets the baseline value of a QMK setting, or null if the setting is not tracked.
    /// </summary>
    public ushort? GetBaselineSetting(ushort id) =>
        _baselineSettings.TryGetValue(id, out var v) ? v : null;

    /// <summary>Returns a defensive copy of the current macro buffer, or null if no macros.</summary>
    public byte[]? GetCurrentMacroBuffer() => _currentMacroBuffer is not null
        ? (byte[])_currentMacroBuffer.Clone() : null;

    /// <summary>Returns a defensive copy of the baseline macro buffer, or null if no macros.</summary>
    public byte[]? GetBaselineMacroBuffer() => _baselineMacroBuffer is not null
        ? (byte[])_baselineMacroBuffer.Clone() : null;

    /// <summary>Number of combo slots tracked (0 when firmware has no combos).</summary>
    public int ComboCount => _currentCombos.Count;

    /// <summary>Number of tap-dance slots tracked.</summary>
    public int TapDanceCount => _currentTapDances.Count;

    /// <summary>Defensive copy of the current combo entry bytes at the given index.</summary>
    public byte[] GetCurrentCombo(int index) => (byte[])_currentCombos[index].Clone();

    /// <summary>Defensive copy of the baseline combo entry bytes at the given index.</summary>
    public byte[] GetBaselineCombo(int index) => (byte[])_baselineCombos[index].Clone();

    /// <summary>Defensive copy of the current tap-dance entry bytes at the given index.</summary>
    public byte[] GetCurrentTapDance(int index) => (byte[])_currentTapDances[index].Clone();

    /// <summary>Defensive copy of the baseline tap-dance entry bytes at the given index.</summary>
    public byte[] GetBaselineTapDance(int index) => (byte[])_baselineTapDances[index].Clone();

    /// <summary>Defensive copies of all baseline combo entries (empty list if none).</summary>
    public IReadOnlyList<byte[]> CloneBaselineCombos() =>
        _baselineCombos.Select(e => (byte[])e.Clone()).ToList();

    /// <summary>Defensive copies of all baseline tap-dance entries (empty list if none).</summary>
    public IReadOnlyList<byte[]> CloneBaselineTapDances() =>
        _baselineTapDances.Select(e => (byte[])e.Clone()).ToList();

    /// <summary>
    /// Sets the macro baseline and current buffer when macros arrive after
    /// the session was created (deferred macro load). No-op if already set.
    /// </summary>
    public void SetMacroBaseline(byte[] macroBuffer)
    {
        if (_baselineMacroBuffer is not null) return; // already initialized
        _baselineMacroBuffer = (byte[])macroBuffer.Clone();
        _currentMacroBuffer = (byte[])macroBuffer.Clone();
    }

    /// <summary>Returns a defensive copy of the baseline keymap (device state at session start).</summary>
    public ushort[,,] CloneBaseline() => (ushort[,,])_baseline.Clone();

    /// <summary>Returns a defensive copy of the current keymap (baseline + applied edits).</summary>
    public ushort[,,] CloneCurrent() => (ushort[,,])_current.Clone();

    /// <summary>
    /// Applies an edit operation. Pushes onto the undo stack and clears redo.
    /// </summary>
    public void Apply(EditOp op)
    {
        ExecuteForward(op);
        _undoStack.Push(op);
        _redoStack.Clear();
    }

    /// <summary>
    /// Undoes the most recent operation.
    /// </summary>
    public void Undo()
    {
        if (!CanUndo) return;
        var op = _undoStack.Pop();
        ExecuteReverse(op);
        _redoStack.Push(op);
    }

    /// <summary>
    /// Redoes the most recently undone operation.
    /// </summary>
    public void Redo()
    {
        if (!CanRedo) return;
        var op = _redoStack.Pop();
        ExecuteForward(op);
        _undoStack.Push(op);
    }

    /// <summary>
    /// Discards all pending changes. Resets current state to baseline and clears undo/redo.
    /// </summary>
    public void Discard()
    {
        Array.Copy(_baseline, _current, _baseline.Length);
        _currentSettings.Clear();
        foreach (var (k, v) in _baselineSettings)
            _currentSettings[k] = v;
        if (_baselineMacroBuffer is not null && _currentMacroBuffer is not null)
            Array.Copy(_baselineMacroBuffer, _currentMacroBuffer, _baselineMacroBuffer.Length);
        for (var i = 0; i < _currentCombos.Count; i++)
            _currentCombos[i] = (byte[])_baselineCombos[i].Clone();
        for (var i = 0; i < _currentTapDances.Count; i++)
            _currentTapDances[i] = (byte[])_baselineTapDances[i].Clone();
        _undoStack.Clear();
        _redoStack.Clear();
    }

    /// <summary>
    /// Builds the set of device write commands needed to persist all pending changes.
    /// Returns one <see cref="SetKeycodeWrite"/> per changed key and one
    /// <see cref="SetQmkSettingWrite"/> per changed setting.
    /// </summary>
    public IReadOnlyList<DeviceWrite> BuildDeviceWrites()
    {
        var writes = new List<DeviceWrite>();
        foreach (var (layer, row, col, _, newCode) in PendingChanges)
            writes.Add(new SetKeycodeWrite(layer, row, col, newCode));
        foreach (var (id, _, newVal) in PendingSettingsChanges)
        {
            var width = _settingWidths.GetValueOrDefault(id, (byte)2);
            writes.Add(new SetQmkSettingWrite(id, newVal, width));
        }
        if (HasPendingMacroChanges && _currentMacroBuffer is not null)
            writes.Add(new MacroBufferWrite((byte[])_currentMacroBuffer.Clone()));
        foreach (var i in PendingComboIndices())
            writes.Add(new ComboEntryWrite(i, (byte[])_currentCombos[i].Clone()));
        foreach (var i in PendingTapDanceIndices())
            writes.Add(new TapDanceEntryWrite(i, (byte[])_currentTapDances[i].Clone()));
        return writes;
    }

    private void ExecuteForward(EditOp op)
    {
        switch (op)
        {
            case SetKeyOp sk:
                _current[sk.Layer, sk.Row, sk.Col] = sk.NewCode;
                break;
            case SetQmkSettingOp so:
                ValidateSettingId(so.SettingId);
                _currentSettings[so.SettingId] = so.NewValue;
                break;
            case SetMacroBufferOp mo:
                _currentMacroBuffer = (byte[])mo.NewBuffer.Clone();
                break;
            case SetComboOp co:
                ValidateIndex(co.Index, _currentCombos.Count, nameof(SetComboOp));
                _currentCombos[co.Index] = (byte[])co.NewBytes.Clone();
                break;
            case SetTapDanceOp td:
                ValidateIndex(td.Index, _currentTapDances.Count, nameof(SetTapDanceOp));
                _currentTapDances[td.Index] = (byte[])td.NewBytes.Clone();
                break;
            default:
                throw new InvalidOperationException(
                    $"Unhandled EditOp subtype: {op.GetType().Name}");
        }
    }

    private void ExecuteReverse(EditOp op)
    {
        switch (op)
        {
            case SetKeyOp sk:
                _current[sk.Layer, sk.Row, sk.Col] = sk.OldCode;
                break;
            case SetQmkSettingOp so:
                ValidateSettingId(so.SettingId);
                _currentSettings[so.SettingId] = so.OldValue;
                break;
            case SetMacroBufferOp mo:
                _currentMacroBuffer = (byte[])mo.OldBuffer.Clone();
                break;
            case SetComboOp co:
                ValidateIndex(co.Index, _currentCombos.Count, nameof(SetComboOp));
                _currentCombos[co.Index] = (byte[])co.OldBytes.Clone();
                break;
            case SetTapDanceOp td:
                ValidateIndex(td.Index, _currentTapDances.Count, nameof(SetTapDanceOp));
                _currentTapDances[td.Index] = (byte[])td.OldBytes.Clone();
                break;
            default:
                throw new InvalidOperationException(
                    $"Unhandled EditOp subtype: {op.GetType().Name}");
        }
    }

    private static void ValidateIndex(int index, int count, string opName)
    {
        if (index < 0 || index >= count)
            throw new InvalidOperationException(
                $"{opName} index {index} out of range [0, {count}).");
    }

    // Dictionary indexer-assignment silently creates entries on unknown keys,
    // so a typo'd SettingId would append a phantom setting to the edit state
    // with no diagnostic. Settings are loaded from the device at session
    // construction, so any ID not already present is by definition wrong.
    private void ValidateSettingId(ushort id)
    {
        if (!_currentSettings.ContainsKey(id))
            throw new InvalidOperationException(
                $"SetQmkSettingOp setting ID 0x{id:X4} not present in session state.");
    }
}
