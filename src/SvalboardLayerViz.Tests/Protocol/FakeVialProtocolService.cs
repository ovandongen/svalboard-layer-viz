using SvalboardLayerViz.Core.Dynamic;
using SvalboardLayerViz.Core.Protocol;
using HidSharp;

namespace SvalboardLayerViz.Tests.Protocol;

/// <summary>
/// Fake protocol service for testing without a real HID device.
/// Records all write/unlock calls for assertion and supports configurable
/// responses for read commands.
/// </summary>
public class FakeVialProtocolService : IVialProtocolService
{
    // --- Read-side state ---

    public bool[,]? NextMatrixState { get; set; }
    public int MatrixPollCount { get; private set; }
    public int LayerCount { get; set; } = 1;
    public ushort[,,]? Keymap { get; set; }

    /// <summary>Configurable QMK settings store. Keys = setting IDs, values = current values.</summary>
    public Dictionary<ushort, ushort> QmkSettingsStore { get; set; } = new();

    // --- Macro read-side state ---

    public int MacroCount { get; set; }
    public int MacroBufferSize { get; set; }
    public byte[]? MacroBufferData { get; set; }

    // --- Write-side recording ---

    public record SetKeycodeCall(int Layer, int Row, int Col, ushort Keycode);
    public record KeymapSetBufferCall(int Offset, byte[] Data);

    public List<SetKeycodeCall> SetKeycodeCalls { get; } = [];
    public List<KeymapSetBufferCall> KeymapSetBufferCalls { get; } = [];
    public int DynamicKeymapResetCount { get; private set; }
    public int EepromResetCount { get; private set; }

    // --- QMK Settings write recording ---

    public record SetQmkSettingCall(ushort SettingId, ushort Value);
    public List<SetQmkSettingCall> SetQmkSettingCalls { get; } = [];
    public int ResetQmkSettingsCount { get; private set; }

    // --- Macro write recording ---

    public record MacroSetBufferCall(int Offset, byte[] Data);
    public List<MacroSetBufferCall> MacroSetBufferCalls { get; } = [];
    public int DynamicKeymapMacroResetCount { get; private set; }

    /// <summary>
    /// When non-null, MacroSetBuffer will throw this exception on the Nth call
    /// (0-indexed). Use to simulate mid-save HID failures.
    /// </summary>
    public (int CallIndex, Exception Exception)? MacroSetBufferFailAt { get; set; }

    /// <summary>
    /// When non-null, SetKeycode will throw this exception on the Nth call
    /// (0-indexed). Use to simulate mid-save HID failures.
    /// </summary>
    public (int CallIndex, Exception Exception)? SetKeycodeFailAt { get; set; }

    /// <summary>
    /// When non-null, SetQmkSetting will throw this exception on the Nth call
    /// (0-indexed). Use to simulate mid-save settings write failures.
    /// </summary>
    public (int CallIndex, Exception Exception)? SetQmkSettingFailAt { get; set; }

    // --- Dynamic entry (combo + tap-dance) state ---

    public DynamicEntryCounts DynamicEntryCounts { get; set; } = new(0, 0, 0, 0);

    /// <summary>Combo entries keyed by index; reads return all-zero when missing.</summary>
    public Dictionary<int, byte[]> ComboEntries { get; } = new();

    /// <summary>Tap-dance entries keyed by index; reads return all-zero when missing.</summary>
    public Dictionary<int, byte[]> TapDanceEntries { get; } = new();

    public record SetComboCall(int Index, byte[] Entry);
    public record SetTapDanceCall(int Index, byte[] Entry);
    public List<SetComboCall> SetComboCalls { get; } = [];
    public List<SetTapDanceCall> SetTapDanceCalls { get; } = [];

    /// <summary>When non-null, SetComboEntry throws on the Nth call.</summary>
    public (int CallIndex, Exception Exception)? SetComboFailAt { get; set; }

    /// <summary>When non-null, SetTapDanceEntry throws on the Nth call.</summary>
    public (int CallIndex, Exception Exception)? SetTapDanceFailAt { get; set; }

    // --- Unlock-side state ---

    public UnlockStatus NextUnlockStatus { get; set; } = new(true, false, []);
    public bool UnlockPollResult { get; set; } = true;
    public int UnlockStartCount { get; private set; }
    public int UnlockPollCount { get; private set; }
    public int LockCount { get; private set; }

    // --- Read implementations ---

    public void Connect(HidDevice device) { }
    public int GetLayerCount() => LayerCount;
    public ulong GetKeyboardId() => 0;
    public int GetDefinitionSize() => 0;
    public byte[] GetDefinition() => [];

    public ushort[,,] GetKeymapBuffer(int layers, int rows, int cols)
    {
        return Keymap ?? new ushort[layers, rows, cols];
    }

    private readonly object _pollLock = new();
    private readonly ManualResetEventSlim _pollHappened = new();

    public bool[,] GetSwitchMatrixState(int rows, int cols)
    {
        lock (_pollLock)
        {
            MatrixPollCount++;
            _pollHappened.Set();
        }
        return NextMatrixState ?? new bool[rows, cols];
    }

    /// <summary>
    /// Blocks until at least <paramref name="count"/> poll calls have happened,
    /// or <paramref name="timeout"/> elapses. Returns true if the count was
    /// reached. Tests use this instead of Thread.Sleep so they don't depend on
    /// wall-clock pacing.
    /// </summary>
    public bool WaitForPolls(int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            lock (_pollLock)
            {
                if (MatrixPollCount >= count) return true;
                _pollHappened.Reset();
            }
            _pollHappened.Wait(deadline - DateTime.UtcNow);
        }
        lock (_pollLock) return MatrixPollCount >= count;
    }

    public ushort? GetQmkSetting(ushort settingId, byte width = 2) =>
        QmkSettingsStore.TryGetValue(settingId, out var val) ? val : null;

    public IReadOnlyList<ushort> GetQmkSettingsList() =>
        QmkSettingsStore.Keys.OrderBy(k => k).ToList();

    public void SetQmkSetting(ushort settingId, ushort value, byte width = 2)
    {
        if (SetQmkSettingFailAt is var (failIdx, ex) && SetQmkSettingCalls.Count == failIdx)
            throw ex;
        SetQmkSettingCalls.Add(new SetQmkSettingCall(settingId, value));
        QmkSettingsStore[settingId] = value;
    }

    public void ResetQmkSettings()
    {
        ResetQmkSettingsCount++;
        QmkSettingsStore.Clear();
    }

    public uint? GetSvalProtoVersion() => null;
    public (byte H, byte S, byte V)? GetLayerColor(int layer) => null;
    public (byte H, byte S)? GetCurrentLedHueSat() => null;

    // --- Write implementations ---

    public void SetKeycode(int layer, int row, int col, ushort keycode)
    {
        if (SetKeycodeFailAt is var (failIdx, ex) && SetKeycodeCalls.Count == failIdx)
            throw ex;

        SetKeycodeCalls.Add(new SetKeycodeCall(layer, row, col, keycode));

        // Also update the in-memory keymap if present, so re-reads reflect writes
        if (Keymap is not null &&
            layer < Keymap.GetLength(0) &&
            row < Keymap.GetLength(1) &&
            col < Keymap.GetLength(2))
        {
            Keymap[layer, row, col] = keycode;
        }
    }

    public void KeymapSetBuffer(int offset, byte[] data)
    {
        KeymapSetBufferCalls.Add(new KeymapSetBufferCall(offset, data));
    }

    public void DynamicKeymapReset()
    {
        DynamicKeymapResetCount++;
    }

    public void EepromReset()
    {
        EepromResetCount++;
    }

    // --- Unlock implementations ---

    public UnlockStatus GetUnlockStatus() => NextUnlockStatus;

    public void UnlockStart()
    {
        UnlockStartCount++;
    }

    public bool UnlockPoll()
    {
        UnlockPollCount++;
        return UnlockPollResult;
    }

    public void Lock()
    {
        LockCount++;
    }

    // --- Macro implementations ---

    public int GetMacroCount() => MacroCount;
    public int GetMacroBufferSize() => MacroBufferSize;

    public byte[] GetMacroBuffer(int bufferSize)
    {
        return MacroBufferData ?? new byte[bufferSize];
    }

    public void MacroSetBuffer(int offset, byte[] data)
    {
        if (MacroSetBufferFailAt is var (failIdx, ex) && MacroSetBufferCalls.Count == failIdx)
            throw ex;
        MacroSetBufferCalls.Add(new MacroSetBufferCall(offset, data));
    }

    public void DynamicKeymapMacroReset()
    {
        DynamicKeymapMacroResetCount++;
    }

    // --- Dynamic entry implementations ---

    public DynamicEntryCounts GetDynamicEntryCounts() => DynamicEntryCounts;

    public byte[] GetComboEntry(int index) =>
        ComboEntries.TryGetValue(index, out var e) ? (byte[])e.Clone() : new byte[Combo.EntryBytes];

    public void SetComboEntry(int index, byte[] entry)
    {
        if (SetComboFailAt is var (failIdx, ex) && SetComboCalls.Count == failIdx)
            throw ex;
        SetComboCalls.Add(new SetComboCall(index, (byte[])entry.Clone()));
        ComboEntries[index] = (byte[])entry.Clone();
    }

    public byte[] GetTapDanceEntry(int index) =>
        TapDanceEntries.TryGetValue(index, out var e) ? (byte[])e.Clone() : new byte[TapDance.EntryBytes];

    public void SetTapDanceEntry(int index, byte[] entry)
    {
        if (SetTapDanceFailAt is var (failIdx, ex) && SetTapDanceCalls.Count == failIdx)
            throw ex;
        SetTapDanceCalls.Add(new SetTapDanceCall(index, (byte[])entry.Clone()));
        TapDanceEntries[index] = (byte[])entry.Clone();
    }

    /// <summary>Number of times <see cref="Dispose"/> was called. Used to
    /// assert idempotency of the VM's shutdown gate.</summary>
    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}
