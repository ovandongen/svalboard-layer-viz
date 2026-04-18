namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Outcome of a save pipeline run. Discriminated by subtype so callers can
/// branch on whether every pending write landed, the user cancelled, the
/// pre-save safety check blocked the run, or the device returned mid-batch.
/// </summary>
public abstract record SaveResult(int Applied);

/// <summary>Every pending write succeeded.</summary>
public sealed record SaveSuccess(int Applied) : SaveResult(Applied);

/// <summary>
/// The user cancelled the save. <see cref="SaveResult.Applied"/> reports how
/// many writes had landed on the device before cancellation fired.
/// </summary>
public sealed record SaveCancelled(int Applied) : SaveResult(Applied);

/// <summary>
/// Pre-save guard aborted the run before any writes were issued — e.g. the
/// user declined a safety-warning prompt.
/// </summary>
public sealed record SaveAborted(string Reason) : SaveResult(0);

/// <summary>
/// A mid-batch write failed. After reconciling against the reloaded device
/// state, <paramref name="Applied"/> writes are confirmed persisted,
/// <paramref name="StillPending"/> writes remain for a retry, and
/// <paramref name="Diverged"/> counts cells where the device value matches
/// neither the baseline nor the user's intent (someone else wrote, or a
/// partial write landed unexpectedly). <paramref name="Remaining"/> is the
/// set of pending writes still needed to realise the user's intent.
/// </summary>
public sealed record SavePartial(
    int Applied,
    int StillPending,
    int Diverged,
    string FailureMessage,
    IReadOnlyList<DeviceWrite> Remaining) : SaveResult(Applied);
