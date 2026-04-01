using HidSharp;

namespace SvalboardLayerViz.Core.Device;

/// <summary>
/// Information about a discovered Vial-compatible HID device.
/// </summary>
public record DeviceInfo
{
    public required string DevicePath { get; init; }
    public required string ProductName { get; init; }
    public required int VendorId { get; init; }
    public required int ProductId { get; init; }

    /// <summary>
    /// The underlying HidSharp device handle. Used to open a connection.
    /// </summary>
    public required HidDevice HidDevice { get; init; }

    public override string ToString() =>
        $"{ProductName} (VID:{VendorId:X4} PID:{ProductId:X4})";
}
