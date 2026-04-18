using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Settings;
using SvalboardLayerViz.Tests.Device;
using SvalboardLayerViz.Tests.Protocol;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

/// <summary>
/// Lifecycle tests for <see cref="MainWindowViewModel"/> — exercises the
/// connect / no-device / failure / shutdown paths that were hardened across
/// the hostile review sessions but had no regression coverage. Uses
/// <see cref="FakeDeviceConnectionService"/> + <see cref="FakeVialProtocolService"/>
/// so nothing touches real HID.
/// </summary>
public class MainWindowViewModelLifecycleTests : IDisposable
{
    private readonly string _tempPath;
    private readonly SettingsService _settings;

    public MainWindowViewModelLifecycleTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"svz-life-{Guid.NewGuid():N}.json");
        _settings = new SettingsService(_tempPath);
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath))
            File.Delete(_tempPath);
    }

    [Fact]
    public void Constructor_AppliesPersistedSettings_ToObservableProperties()
    {
        // Persist non-default values so we can observe the constructor reading them
        _settings.Save(new UserSettings
        {
            AlwaysOnTop = true,
            BackgroundOpacity = 0.42,
            VerticalLayout = true,
            VerticalLayoutTopHand = "Right",
        });

        var vm = new MainWindowViewModel(_settings, new FakeVialProtocolService(),
            deviceService: new FakeDeviceConnectionService());

        Assert.True(vm.IsAlwaysOnTop);
        Assert.Equal(0.42, vm.BackgroundOpacity);
        Assert.True(vm.IsVerticalLayout);
        Assert.Equal("Right", vm.VerticalLayoutTopHand);
        Assert.False(vm.IsConnected);
    }

    [Fact]
    public void Constructor_CorruptSettingsFile_FallsBackToDefaultsWithoutThrowing()
    {
        // Write a malformed JSON file; SettingsService.Load logs and returns defaults.
        // The VM must still construct cleanly.
        File.WriteAllText(_tempPath, "{ this is not valid json");

        var ex = Record.Exception(() => new MainWindowViewModel(
            _settings, new FakeVialProtocolService(),
            deviceService: new FakeDeviceConnectionService()));

        Assert.Null(ex);

        var vm = new MainWindowViewModel(_settings, new FakeVialProtocolService(),
            deviceService: new FakeDeviceConnectionService());
        // Defaults — verifies the corrupt file did not silently corrupt the VM either.
        // Compare against new UserSettings() so this test stays correct even if
        // the defaults change later.
        var defaults = new UserSettings();
        Assert.Equal(defaults.AlwaysOnTop, vm.IsAlwaysOnTop);
        Assert.Equal(defaults.VerticalLayoutTopHand, vm.VerticalLayoutTopHand);
        Assert.Equal(defaults.VerticalLayout, vm.IsVerticalLayout);
    }

    [Fact]
    public async Task TryConnectAsync_NoDevicesFound_SetsNoDeviceStatusMessage()
    {
        var devices = new FakeDeviceConnectionService(); // empty Devices list
        var vm = new MainWindowViewModel(_settings, new FakeVialProtocolService(),
            deviceService: devices);

        await vm.TryConnectAsync();

        Assert.False(vm.IsConnected);
        Assert.Contains("No Svalboard found", vm.StatusMessage);
        Assert.Equal(1, devices.FindCallCount);
    }

    [Fact]
    public async Task TryConnectAsync_HidEnumerationThrows_StatusMessageReflectsFailure()
    {
        var devices = new FakeDeviceConnectionService
        {
            EnumerationException = new InvalidOperationException("boom"),
        };
        var vm = new MainWindowViewModel(_settings, new FakeVialProtocolService(),
            deviceService: devices);

        await vm.TryConnectAsync();

        Assert.False(vm.IsConnected);
        Assert.Contains("Connection error", vm.StatusMessage);
        Assert.Contains("boom", vm.StatusMessage);
    }

    [Fact]
    public async Task ShutdownAsync_CalledTwice_DisposesProtocolOnlyOnce()
    {
        // Verifies the Interlocked.CompareExchange gate added during the
        // re-entrant Closing-during-Quit hardening — without it, a second call
        // would double-dispose the protocol service and the connect CTS.
        var protocol = new FakeVialProtocolService();
        var vm = new MainWindowViewModel(_settings, protocol,
            deviceService: new FakeDeviceConnectionService());

        await vm.ShutdownAsync();
        await vm.ShutdownAsync();

        Assert.Equal(1, protocol.DisposeCount);
    }
}
