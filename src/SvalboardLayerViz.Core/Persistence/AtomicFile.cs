using System.Text;

namespace SvalboardLayerViz.Core.Persistence;

/// <summary>
/// Crash-safe file writes. Writes go to <c>{path}.tmp</c>, are flushed to
/// disk (not just the OS page cache), and only then renamed over the
/// destination. A crash mid-write leaves the original file intact and an
/// orphan <c>.tmp</c> alongside, instead of a zero-byte destination that
/// would deserialize to defaults.
/// </summary>
public static class AtomicFile
{
    public static async Task WriteAllTextAsync(string path, string content, CancellationToken ct = default)
    {
        var tmp = path + ".tmp";
        try
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await fs.WriteAsync(bytes, ct);
                // Force data to disk before rename. Without this, File.Move
                // can succeed at the metadata level while the bytes still sit
                // in the OS write cache — power loss then yields a 0-byte
                // destination file, which is the exact failure this helper
                // exists to prevent.
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    public static void WriteAllText(string path, string content)
    {
        var tmp = path + ".tmp";
        try
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }
}
