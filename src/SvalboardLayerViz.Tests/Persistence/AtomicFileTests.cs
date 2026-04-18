using SvalboardLayerViz.Core.Persistence;
using Xunit;

namespace SvalboardLayerViz.Tests.Persistence;

public class AtomicFileTests : IDisposable
{
    private readonly string _tempDir;

    public AtomicFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"svlviz-atomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void WriteAllText_NewFile_CreatesAtomically()
    {
        var path = Path.Combine(_tempDir, "out.json");
        AtomicFile.WriteAllText(path, "hello");

        Assert.Equal("hello", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void WriteAllText_ExistingFile_Replaces()
    {
        var path = Path.Combine(_tempDir, "out.json");
        File.WriteAllText(path, "original");
        AtomicFile.WriteAllText(path, "replaced");

        Assert.Equal("replaced", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task WriteAllTextAsync_ExistingFile_Replaces()
    {
        var path = Path.Combine(_tempDir, "out.json");
        await File.WriteAllTextAsync(path, "original");
        await AtomicFile.WriteAllTextAsync(path, "replaced");

        Assert.Equal("replaced", await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void WriteAllText_DestinationDirMissing_Throws_OriginalUntouched()
    {
        var path = Path.Combine(_tempDir, "missing-subdir", "out.json");
        // Destination dir does not exist — File.WriteAllText on the .tmp will
        // throw DirectoryNotFoundException. The .tmp must be cleaned up; there
        // was no original, so nothing else to verify beyond no orphan tmp.
        Assert.Throws<DirectoryNotFoundException>(() => AtomicFile.WriteAllText(path, "data"));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void WriteAllText_WriteFails_OriginalPreserved()
    {
        // Pre-create {path}.tmp as a *directory* so the FileStream Create call
        // fails before any data is written. Exercises the "write throws before
        // File.Move" path: the original must be untouched, since the whole
        // point of the helper is that a failed write cannot corrupt the dest.
        // (True mid-flush crash simulation needs OS hooks — still out of reach
        // in unit tests; see AtomicFile docstring.)
        var path = Path.Combine(_tempDir, "out.json");
        File.WriteAllText(path, "original");
        Directory.CreateDirectory(path + ".tmp");

        // On POSIX opening a FileStream over a directory raises
        // UnauthorizedAccessException; on Windows the same condition raises
        // UnauthorizedAccessException as well. Either way, the specific
        // exception type isn't the claim under test — the preservation
        // invariant is. Broad catch.
        Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllText(path, "replaced"));
        Assert.Equal("original", File.ReadAllText(path));

        Directory.Delete(path + ".tmp");
    }
}
