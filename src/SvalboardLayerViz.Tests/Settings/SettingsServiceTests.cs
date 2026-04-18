using System.Text.Json;
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
    public void SaveAndLoad_VerticalLayout_RoundTrip()
    {
        var original = new UserSettings
        {
            VerticalLayout = true,
            VerticalLayoutTopHand = "Right",
        };

        _service.Save(original);
        var loaded = _service.Load();

        Assert.True(loaded.VerticalLayout);
        Assert.Equal("Right", loaded.VerticalLayoutTopHand);
    }

    [Fact]
    public void Load_MissingVerticalLayout_DefaultsFalseAndLeft()
    {
        File.WriteAllText(_tempFile, """{"HotkeyKey": "F5"}""");
        var settings = _service.Load();

        Assert.False(settings.VerticalLayout);
        Assert.Equal("Left", settings.VerticalLayoutTopHand);
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

    [Fact]
    public void Load_TruncatedJson_ReturnsDefaults()
    {
        // Truncated mid-string — JsonException path. Behavior must be identical
        // to the prior catch-all: defaults returned, no throw to caller. The
        // difference is that the parse failure is now logged via DiagnosticLog.
        File.WriteAllText(_tempFile, """{"HotkeyKey":"F1""");
        var settings = _service.Load();

        Assert.NotNull(settings);
        Assert.Equal("F12", settings.HotkeyKey);
    }

    [Fact]
    public void Save_OverwritesViaAtomicMove_NoOrphanTmp()
    {
        _service.Save(new UserSettings { HotkeyKey = "F9" });
        _service.Save(new UserSettings { HotkeyKey = "F10" });

        Assert.Equal("F10", _service.Load().HotkeyKey);
        Assert.False(File.Exists(_tempFile + ".tmp"));
    }

    [Fact]
    public void Load_FutureSchemaVersion_BacksUpAndReturnsDefaults()
    {
        File.WriteAllText(_tempFile, """{"SchemaVersion": 999, "HotkeyKey": "F4"}""");
        var settings = _service.Load();

        Assert.Equal("F12", settings.HotkeyKey);
        Assert.True(File.Exists(_tempFile + ".v999.bak"));
    }

    [Fact]
    public void Load_CurrentSchemaVersion_PreservesFields()
    {
        var original = new UserSettings { HotkeyKey = "F7", LayerHoldThresholdMs = 250 };
        _service.Save(original);

        var loaded = _service.Load();

        Assert.Equal(UserSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal("F7", loaded.HotkeyKey);
        Assert.Equal(250, loaded.LayerHoldThresholdMs);
    }

    [Fact]
    public void Load_OlderSchemaVersion_MigratesAndPersists()
    {
        // Write a payload that omits SchemaVersion entirely — JSON deserialization
        // would normally fill it with the field's default (CurrentSchemaVersion),
        // so to genuinely test the migration branch we explicitly set 0.
        File.WriteAllText(_tempFile, """{"SchemaVersion": 0, "HotkeyKey": "F3"}""");
        var loaded = _service.Load();

        Assert.Equal("F3", loaded.HotkeyKey);
        Assert.Equal(UserSettings.CurrentSchemaVersion, loaded.SchemaVersion);

        // Assert on disk, not via re-Load: the previous version of this test
        // called Load() again to verify persistence, but that can pass if
        // Save() silently failed (the migration branch would just rerun in
        // memory). Parse the on-disk JSON directly so the migration-persist
        // path is the only thing under test.
        var raw = File.ReadAllText(_tempFile);
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(UserSettings.CurrentSchemaVersion, doc.RootElement.GetProperty("SchemaVersion").GetInt32());

        // And the re-Load still works too — kept for regression coverage.
        var rereread = _service.Load();
        Assert.Equal(UserSettings.CurrentSchemaVersion, rereread.SchemaVersion);
    }
}
