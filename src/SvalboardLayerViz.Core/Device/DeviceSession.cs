using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Models;
using SvalboardLayerViz.Core.Protocol;
using SvalboardLayerViz.Core.Settings;

namespace SvalboardLayerViz.Core.Device;

/// <summary>
/// Hydrated device state produced by a single connect call: picked device,
/// loaded keyboard config, and probed capabilities (tapping term, LED poll).
/// Returned by <see cref="Connect"/> so the VM can update all observable
/// properties in one shot instead of interleaving protocol I/O with UI state.
/// </summary>
public sealed record DeviceSession(
    DeviceInfo Device,
    KeyboardConfig Config,
    ushort? TappingTerm,
    bool LedPollSupported)
{
    /// <summary>
    /// Discovers Vial devices, picks the preferred one, loads the full keymap,
    /// and probes for LED/tapping-term capabilities. Returns <c>null</c> when no
    /// device is found. All protocol I/O is synchronous — callers should wrap
    /// in <c>Task.Run</c> to stay off the UI thread.
    /// </summary>
    public static DeviceSession? Connect(
        IDeviceConnectionService deviceService,
        IVialProtocolService protocol,
        KeycodeService keycodeService,
        UserSettings settings)
    {
        var devices = deviceService.FindVialDevices();
        DiagnosticLog.Info("Device", $"Device enumeration complete: {devices.Count} device(s) found");
        if (devices.Count == 0) return null;

        var device = PickPreferred(devices);
        DiagnosticLog.Info("Device", $"Selected device: {device.ProductName} (out of {devices.Count})");

        var loader = new KeymapLoader(protocol, keycodeService);
        DiagnosticLog.Info("Device", $"Loading keymap from {device.ProductName}...");
        var config = loader.Load(device, settings);
        DiagnosticLog.Info("Device", "Keymap loaded successfully");

        var tappingTerm = config.QmkSettings
            .FirstOrDefault(s => s.SettingId == VialCommands.QmkSettingTappingTerm);

        // Capability probe: does the firmware respond to VIA rgblight color
        // queries? If yes, we can drive the active layer from the LED instead
        // of the matrix-based MO/TG heuristic.
        var ledProbe = protocol.GetCurrentLedHueSat();

        return new DeviceSession(device, config, tappingTerm?.Value, ledProbe is not null);
    }

    /// <summary>
    /// Prefers a Svalboard ("lightly") device when multiple Vial devices are
    /// connected; falls back to the first device otherwise.
    /// </summary>
    private static DeviceInfo PickPreferred(IReadOnlyList<DeviceInfo> devices)
    {
        var preferred = devices.FirstOrDefault(d =>
            d.ProductName.StartsWith("lightly", StringComparison.OrdinalIgnoreCase));
        return preferred ?? devices[0];
    }
}
