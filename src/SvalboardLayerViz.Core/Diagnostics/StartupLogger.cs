using System.Runtime.InteropServices;

namespace SvalboardLayerViz.Core.Diagnostics;

/// <summary>
/// Lightweight file logger for startup diagnostics. Writes to a rolling log file
/// in the app data directory so users can share it when reporting rendering issues.
/// </summary>
public static class StartupLogger
{
    private static readonly string LogPath = GetLogPath();
    private static readonly object Lock = new();

    /// <summary>Logs a timestamped message to the startup log file.</summary>
    public static void Log(string message)
    {
        try
        {
            lock (Lock)
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (dir is not null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.AppendAllText(LogPath,
                    $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }

    /// <summary>Logs basic environment info at startup.</summary>
    public static void LogEnvironment()
    {
        TruncateIfNeeded();
        Log("--- Application starting ---");
        Log($"OS: {RuntimeInformation.OSDescription}");
        Log($"Architecture: {RuntimeInformation.OSArchitecture}");
        Log($"Runtime: {RuntimeInformation.FrameworkDescription}");
        Log($"Process arch: {RuntimeInformation.ProcessArchitecture}");
    }

    /// <summary>Keeps the log file from growing without bound (~100 KB max).</summary>
    private static void TruncateIfNeeded()
    {
        try
        {
            if (!File.Exists(LogPath)) return;
            var info = new FileInfo(LogPath);
            if (info.Length > 100_000)
                File.WriteAllText(LogPath,
                    $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Log truncated{Environment.NewLine}");
        }
        catch
        {
            // Best-effort.
        }
    }

    private static string GetLogPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "SvalboardLayerViz", "startup.log");
    }
}
