namespace SvalboardLayerViz.Core.Device;

/// <summary>
/// Discovery + hotplug-notification surface for Vial HID devices. Extracted so
/// the VM can be tested with a fake that doesn't touch the real HID subsystem.
/// </summary>
public interface IDeviceConnectionService
{
    /// <summary>Enumerates currently connected Vial-compatible HID devices.</summary>
    IReadOnlyList<DeviceInfo> FindVialDevices();

    /// <summary>Subscribes to OS-level device-list-changed events. Dispose to unsubscribe.</summary>
    IDisposable OnDeviceListChanged(Action callback);
}
