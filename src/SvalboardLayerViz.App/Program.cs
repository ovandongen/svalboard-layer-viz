using Avalonia;
using SvalboardLayerViz.Core.Diagnostics;

namespace SvalboardLayerViz.App;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "SvalboardLayerViz-SingleInstance", out bool isNew);
        if (!isNew) return;

        StartupLogger.LogEnvironment();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

        // Allow users to force software rendering via environment variable
        // (workaround for GPU driver issues on some NVIDIA/AMD configurations).
        if (OperatingSystem.IsWindows())
        {
            var renderMode = Environment.GetEnvironmentVariable("SVALBOARD_RENDER_MODE");
            if (renderMode?.Equals("software", StringComparison.OrdinalIgnoreCase) == true)
            {
                StartupLogger.Log("Rendering mode: software (forced via SVALBOARD_RENDER_MODE)");
                builder = builder.With(new Win32PlatformOptions
                {
                    RenderingMode = [Win32RenderingMode.Software]
                });
            }
            else
            {
                StartupLogger.Log($"Rendering mode: auto (SVALBOARD_RENDER_MODE={renderMode ?? "unset"})");
            }
        }

        return builder;
    }
}
