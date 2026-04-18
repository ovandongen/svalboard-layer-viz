namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Pure reconciliation of a save's intended-vs-device state. For every cell
/// that the user changed from baseline (want != was):
/// <list type="bullet">
///   <item>device already matches (have == want) → <c>Applied</c></item>
///   <item>device still at baseline (have == was) → <c>StillPending</c> (retryable)</item>
///   <item>anything else → <c>Diverged</c> (user or other tool changed it mid-save)</item>
/// </list>
/// Extracted from <c>MainWindowViewModel</c> so partial-save reasoning is
/// testable without VM scaffolding.
/// </summary>
public static class SaveReconciliation
{
    public record Result(
        int Applied,
        IReadOnlyList<SetKeycodeWrite> StillPending,
        int Diverged);

    public static Result Reconcile(
        ushort[,,] device,
        ushort[,,] baseline,
        ushort[,,] intended)
    {
        var applied = 0;
        var stillPending = new List<SetKeycodeWrite>();
        var diverged = 0;

        for (var l = 0; l < intended.GetLength(0); l++)
        {
            for (var r = 0; r < intended.GetLength(1); r++)
            {
                for (var c = 0; c < intended.GetLength(2); c++)
                {
                    var want = intended[l, r, c];
                    var have = device[l, r, c];
                    var was = baseline[l, r, c];
                    if (want == was) continue; // untouched cell — not part of this save

                    if (have == want) applied++;
                    else if (have == was) stillPending.Add(new SetKeycodeWrite(l, r, c, want));
                    else diverged++;
                }
            }
        }

        return new Result(applied, stillPending, diverged);
    }
}
