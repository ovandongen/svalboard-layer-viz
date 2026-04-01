using System.Text.Json;

namespace SvalboardLayerViz.Core.Settings;

/// <summary>
/// Persists user settings as JSON in the application data directory.
/// </summary>
public class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _filePath;

    public SettingsService()
        : this(GetDefaultPath())
    {
    }

    /// <summary>Constructor with explicit path, for testing.</summary>
    public SettingsService(string filePath)
    {
        _filePath = filePath;
    }

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new UserSettings();

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "SvalboardLayerViz", "settings.json");
    }
}
