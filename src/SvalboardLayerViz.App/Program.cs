using Avalonia;
using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.Settings;

namespace SvalboardLayerViz.App;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "SvalboardLayerViz-SingleInstance", out bool isNew);
        if (!isNew) return;

        DiagnosticLog.LogEnvironment();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

        // Allow users to force software rendering via environment variable or settings.json.
        // Env var takes precedence over settings file.
        if (OperatingSystem.IsWindows())
        {
            var renderMode = Environment.GetEnvironmentVariable("SVALBOARD_RENDER_MODE");
            var source = "SVALBOARD_RENDER_MODE env";

            if (string.IsNullOrEmpty(renderMode))
            {
                try
                {
                    renderMode = new SettingsService().Load().RenderingMode;
                    source = "settings.json";
                }
                catch
                {
                    renderMode = "auto";
                    source = "default (settings read failed)";
                }
            }

            if (renderMode?.Equals("software", StringComparison.OrdinalIgnoreCase) == true)
            {
                DiagnosticLog.Info("Startup", $"Rendering mode: software (via {source})");
                builder = builder.With(new Win32PlatformOptions
                {
                    RenderingMode = [Win32RenderingMode.Software]
                });
            }
            else
            {
                DiagnosticLog.Info("Startup", $"Rendering mode: {renderMode ?? "auto"} (via {source})");
            }
        }

        return builder;
    }
}
