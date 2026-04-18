using SvalboardLayerViz.Core.Keymap;

namespace SvalboardLayerViz.Core.History;

/// <summary>
/// Computes the differences between two keymap snapshots,
/// producing <see cref="EditOp"/> instances suitable for replay
/// through a <see cref="KeymapEditSession"/>.
/// </summary>
public static class SnapshotDiffComputer
{
    /// <summary>
    /// A single key difference between two snapshots.
    /// </summary>
    public record KeyDiff(int Layer, int Row, int Col, ushort OldCode, ushort NewCode);

    /// <summary>
    /// Computes all key differences between two keymaps.
    /// Both must have the same dimensions.
    /// </summary>
    public static IReadOnlyList<KeyDiff> Diff(ushort[,,] from, ushort[,,] to)
    {
        var layers = from.GetLength(0);
        var rows = from.GetLength(1);
        var cols = from.GetLength(2);

        if (to.GetLength(0) != layers || to.GetLength(1) != rows || to.GetLength(2) != cols)
            throw new ArgumentException(
                $"Keymap dimensions differ: from [{layers},{rows},{cols}] vs to [{to.GetLength(0)},{to.GetLength(1)},{to.GetLength(2)}]");

        var diffs = new List<KeyDiff>();
        for (var l = 0; l < layers; l++)
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                {
                    var oldCode = from[l, r, c];
                    var newCode = to[l, r, c];
                    if (oldCode != newCode)
                        diffs.Add(new KeyDiff(l, r, c, oldCode, newCode));
                }

        return diffs;
    }

    /// <summary>
    /// Converts diffs into <see cref="SetKeyOp"/> instances for replay through an edit session.
    /// </summary>
    public static IReadOnlyList<SetKeyOp> ToEditOps(IReadOnlyList<KeyDiff> diffs) =>
        diffs.Select(d => new SetKeyOp(d.Layer, d.Row, d.Col, d.OldCode, d.NewCode)).ToList();
}
