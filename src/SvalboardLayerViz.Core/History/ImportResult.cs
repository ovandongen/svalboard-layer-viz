namespace SvalboardLayerViz.Core.History;

/// <summary>
/// Result of an <see cref="ISnapshotService.ImportAsync"/> call.
/// Counts are per-file (0 or 1 for a single-file import); the History UI
/// aggregates across multiple files when importing a folder.
/// </summary>
public sealed record ImportResult(
    int Imported,
    int SkippedDuplicates,
    int RefusedWrongKeyboard,
    IReadOnlyList<string> Warnings);
