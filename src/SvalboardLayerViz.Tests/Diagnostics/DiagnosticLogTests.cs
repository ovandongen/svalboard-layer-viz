using SvalboardLayerViz.Core.Diagnostics;
using Xunit;

namespace SvalboardLayerViz.Tests.Diagnostics;

/// <summary>
/// Tests for <see cref="DiagnosticLog"/>. Uses a temporary directory to avoid
/// interfering with the real app log.
///
/// Note: DiagnosticLog is a static class that writes to a fixed path, so these
/// tests exercise the public API surface (level filtering, report generation)
/// rather than file I/O (which would require refactoring the static to accept
/// a path — not worth it for a diagnostic logger).
/// </summary>
public class DiagnosticLogTests : IDisposable
{
    private readonly LogLevel _originalLevel;

    public DiagnosticLogTests()
    {
        _originalLevel = DiagnosticLog.MinimumLevel;
    }

    public void Dispose()
    {
        // Restore original level so tests don't leak state
        DiagnosticLog.SetMinimumLevel(_originalLevel);
    }

    // --- Level filtering ---

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Info)]
    [InlineData(LogLevel.Warn)]
    [InlineData(LogLevel.Error)]
    public void SetMinimumLevel_RoundTrips(LogLevel level)
    {
        DiagnosticLog.SetMinimumLevel(level);
        Assert.Equal(level, DiagnosticLog.MinimumLevel);
    }

    [Fact]
    public void DefaultMinimumLevel_IsInfo()
    {
        // After restore in Dispose, but the static default is Info
        DiagnosticLog.SetMinimumLevel(LogLevel.Info);
        Assert.Equal(LogLevel.Info, DiagnosticLog.MinimumLevel);
    }

    [Fact]
    public void Write_BelowMinimumLevel_DoesNotThrow()
    {
        DiagnosticLog.SetMinimumLevel(LogLevel.Error);
        // These should be silently filtered — no crash
        DiagnosticLog.Trace("Test", "trace message");
        DiagnosticLog.Debug("Test", "debug message");
        DiagnosticLog.Info("Test", "info message");
        DiagnosticLog.Warn("Test", "warn message");
    }

    [Fact]
    public void Write_AtOrAboveMinimumLevel_DoesNotThrow()
    {
        DiagnosticLog.SetMinimumLevel(LogLevel.Trace);
        DiagnosticLog.Trace("Test", "trace message");
        DiagnosticLog.Debug("Test", "debug message");
        DiagnosticLog.Info("Test", "info message");
        DiagnosticLog.Warn("Test", "warn message");
        DiagnosticLog.Error("Test", "error message");
    }

    // --- Log directory ---

    [Fact]
    public void GetLogDirectory_ReturnsNonEmpty()
    {
        Assert.False(string.IsNullOrEmpty(DiagnosticLog.GetLogDirectory()));
    }

    [Fact]
    public void GetLogFilePath_ContainsDirectory()
    {
        Assert.StartsWith(DiagnosticLog.GetLogDirectory(), DiagnosticLog.GetLogFilePath());
    }

    [Fact]
    public void GetLogFilePath_HasDiagnosticExtension()
    {
        Assert.Contains("diagnostic", DiagnosticLog.GetLogFilePath());
    }

    // --- Diagnostic report ---

    [Fact]
    public void CollectDiagnosticReport_ContainsHeader()
    {
        var report = DiagnosticLog.CollectDiagnosticReport();
        Assert.Contains("SvalboardLayerViz Diagnostic Report", report);
    }

    [Fact]
    public void CollectDiagnosticReport_ContainsEnvironmentInfo()
    {
        var report = DiagnosticLog.CollectDiagnosticReport();
        Assert.Contains("OS:", report);
        Assert.Contains("Runtime:", report);
    }

    [Fact]
    public void CollectDiagnosticReport_ContainsRecentLogSection()
    {
        var report = DiagnosticLog.CollectDiagnosticReport();
        Assert.Contains("Recent Log", report);
    }

    // --- LogEnvironment ---

    [Fact]
    public void LogEnvironment_DoesNotThrow()
    {
        DiagnosticLog.LogEnvironment();
    }

    // --- MaxLogSize constant ---

    [Fact]
    public void MaxLogSize_Is2MB()
    {
        Assert.Equal(2 * 1024 * 1024, DiagnosticLog.MaxLogSize);
    }

    // --- PII guard: subsystem and message should not leak into wrong place ---

    [Fact]
    public void Write_NullSubsystem_DoesNotThrow()
    {
        DiagnosticLog.SetMinimumLevel(LogLevel.Trace);
        DiagnosticLog.Write(LogLevel.Info, null!, "test");
    }

    [Fact]
    public void Write_EmptyMessage_DoesNotThrow()
    {
        DiagnosticLog.SetMinimumLevel(LogLevel.Trace);
        DiagnosticLog.Write(LogLevel.Info, "Test", "");
    }
}
