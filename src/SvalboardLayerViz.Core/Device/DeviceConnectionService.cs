using HidSharp;
using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.Protocol;

namespace SvalboardLayerViz.Core.Device;

/// <summary>
/// Manages discovery and connection to Vial-compatible HID devices.
/// Wraps HidSharp's device enumeration with Vial-specific filtering.
/// </summary>
public class DeviceConnectionService : IDeviceConnectionService
{
    /// <summary>
    /// Finds all connected Vial-compatible HID devices.
    /// Filters by the Vial-specific HID usage page (0xFF60).
    /// </summary>
    public IReadOnlyList<DeviceInfo> FindVialDevices()
    {
        var devices = new List<DeviceInfo>();

        DiagnosticLog.Info("HID", "Getting device list...");
        var hidDevices = DeviceList.Local.GetHidDevices();
        DiagnosticLog.Info("HID", $"{hidDevices.Count()} device(s) found, scanning descriptors...");

        foreach (var device in hidDevices)
        {
            // Log VID/PID before any USB I/O — if the next call hangs,
            // this line identifies which device is the culprit.
            DiagnosticLog.Debug("HID", $"Checking VID={device.VendorID:X4} PID={device.ProductID:X4}");
            try
            {
                string friendlyName;
                try { friendlyName = device.GetFriendlyName(); }
                catch { friendlyName = "(name unavailable)"; }

                var reportDescriptor = device.GetReportDescriptor();

                bool matched = false;
                foreach (var deviceItem in reportDescriptor.DeviceItems)
                {
                    uint usage1 = ((uint)VialCommands.VialUsagePage << 16) | VialCommands.VialUsage1;
                    uint usage2 = ((uint)VialCommands.VialUsagePage << 16) | VialCommands.VialUsage2;

                    if (deviceItem.Usages.ContainsValue(usage1) ||
                        deviceItem.Usages.ContainsValue(usage2))
                    {
                        matched = true;
                        var name = device.GetProductName() ?? "Unknown";
                        DiagnosticLog.Info("HID", $"Vial device \"{name}\"");
                        devices.Add(new DeviceInfo
                        {
                            DevicePath = device.DevicePath,
                            ProductName = name,
                            VendorId = device.VendorID,
                            ProductId = device.ProductID,
                            HidDevice = device
                        });
                    }
                }

                if (!matched)
                    DiagnosticLog.Debug("HID", $"Skip \"{friendlyName}\"");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("HID", $"Error: {ex.Message}");
            }
        }

        return devices;
    }

    /// <summary>
    /// Registers a callback for device list changes (connect/disconnect).
    /// </summary>
    public IDisposable OnDeviceListChanged(Action callback)
    {
        var subscription = new DeviceListChangedSubscription(callback);
        DeviceList.Local.Changed += subscription.Handler;
        return subscription;
    }

    private class DeviceListChangedSubscription(Action callback) : IDisposable
    {
        public void Handler(object? sender, DeviceListChangedEventArgs e) => callback();
        public void Dispose() => DeviceList.Local.Changed -= Handler;
    }
}
