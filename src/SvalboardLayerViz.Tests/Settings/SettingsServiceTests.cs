using SvalboardLayerViz.Core.Settings;
using Xunit;

namespace SvalboardLayerViz.Tests.Settings;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempFile;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"svlviz-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tempFile = Path.Combine(_tempDir, "settings.json");
        _service = new SettingsService(_tempFile);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var settings = _service.Load();

        Assert.NotNull(settings);
        Assert.Empty(settings.LayerColors);
        Assert.Empty(settings.LayerNames);
        Assert.Empty(settings.CustomKeyLabels);
        Assert.Equal("F12", settings.HotkeyKey);
        Assert.Equal("None", settings.HotkeyModifiers);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        var original = new UserSettings
        {
            LayerColors = new Dictionary<int, string> { [0] = "#FF0000", [2] = "#00FF00" },
            LayerNames = new Dictionary<int, string> { [0] = "Base", [1] = "Nav" },
            CustomKeyLabels = new Dictionary<string, string> { ["0x5300"] = "My Macro" },
            HotkeyKey = "F10",
            HotkeyModifiers = "Ctrl",
        };

        _service.Save(original);
        var loaded = _service.Load();

        Assert.Equal(original.LayerColors, loaded.LayerColors);
        Assert.Equal(original.LayerNames, loaded.LayerNames);
        Assert.Equal(original.CustomKeyLabels, loaded.CustomKeyLabels);
        Assert.Equal("F10", loaded.HotkeyKey);
        Assert.Equal("Ctrl", loaded.HotkeyModifiers);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        File.WriteAllText(_tempFile, "not valid json {{{");
        var settings = _service.Load();

        Assert.NotNull(settings);
        Assert.Equal("F12", settings.HotkeyKey);
    }

    [Fact]
    public void Load_PartialJson_FillsDefaults()
    {
        File.WriteAllText(_tempFile, """{"HotkeyKey": "F5"}""");
        var settings = _service.Load();

        Assert.Equal("F5", settings.HotkeyKey);
        Assert.Equal("None", settings.HotkeyModifiers);
        Assert.Empty(settings.LayerColors);
    }

    [Fact]
    public void Save_CreatesDirectoryIfNeeded()
    {
        var nestedDir = Path.Combine(_tempDir, "sub", "dir");
        var nestedFile = Path.Combine(nestedDir, "settings.json");
        var svc = new SettingsService(nestedFile);

        svc.Save(new UserSettings());

        Assert.True(File.Exists(nestedFile));
    }
}
