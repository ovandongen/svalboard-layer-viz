using HidSharp;
using SvalboardLayerViz.Core.Protocol;

namespace SvalboardLayerViz.Core.Device;

/// <summary>
/// Manages discovery and connection to Vial-compatible HID devices.
/// Wraps HidSharp's device enumeration with Vial-specific filtering.
/// </summary>
public class DeviceConnectionService
{
    /// <summary>
    /// Finds all connected Vial-compatible HID devices.
    /// Filters by the Vial-specific HID usage page (0xFF60).
    /// </summary>
    public IReadOnlyList<DeviceInfo> FindVialDevices()
    {
        var devices = new List<DeviceInfo>();

        foreach (var device in DeviceList.Local.GetHidDevices())
        {
            try
            {
                var reportDescriptor = device.GetReportDescriptor();

                foreach (var deviceItem in reportDescriptor.DeviceItems)
                {
                    uint usage1 = ((uint)VialCommands.VialUsagePage << 16) | VialCommands.VialUsage1;
                    uint usage2 = ((uint)VialCommands.VialUsagePage << 16) | VialCommands.VialUsage2;

                    if (deviceItem.Usages.ContainsValue(usage1) ||
                        deviceItem.Usages.ContainsValue(usage2))
                    {
                        devices.Add(new DeviceInfo
                        {
                            DevicePath = device.DevicePath,
                            ProductName = device.GetProductName() ?? "Unknown",
                            VendorId = device.VendorID,
                            ProductId = device.ProductID,
                            HidDevice = device
                        });
                    }
                }
            }
            catch
            {
                // Skip devices we can't read descriptors from
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
