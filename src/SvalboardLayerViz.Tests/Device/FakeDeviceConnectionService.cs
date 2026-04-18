using SvalboardLayerViz.Core.Device;

namespace SvalboardLayerViz.Tests.Device;

/// <summary>
/// In-memory <see cref="IDeviceConnectionService"/> for VM lifecycle tests.
/// Lets each test seed the device list (or an enumeration exception) and
/// drive the device-list-changed callback by hand instead of waiting for
/// real HID hotplug events.
/// </summary>
public class FakeDeviceConnectionService : IDeviceConnectionService
{
    /// <summary>Devices returned by <see cref="FindVialDevices"/>. Default empty.</summary>
    public List<DeviceInfo> Devices { get; } = [];

    /// <summary>When set, <see cref="FindVialDevices"/> throws this instead of returning.</summary>
    public Exception? EnumerationException { get; set; }

    public int FindCallCount { get; private set; }
    public int SubscriptionCount { get; private set; }
    public int DisposedSubscriptionCount { get; private set; }

    /// <summary>Last callback registered via <see cref="OnDeviceListChanged"/>. Tests can fire it.</summary>
    public Action? LastCallback { get; private set; }

    public IReadOnlyList<DeviceInfo> FindVialDevices()
    {
        FindCallCount++;
        if (EnumerationException is not null) throw EnumerationException;
        return Devices;
    }

    public IDisposable OnDeviceListChanged(Action callback)
    {
        SubscriptionCount++;
        LastCallback = callback;
        return new Subscription(this);
    }

    private class Subscription(FakeDeviceConnectionService owner) : IDisposable
    {
        public void Dispose() => owner.DisposedSubscriptionCount++;
    }
}
