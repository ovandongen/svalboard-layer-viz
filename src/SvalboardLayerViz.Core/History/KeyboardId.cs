namespace SvalboardLayerViz.Core.History;

/// <summary>
/// Identifies a specific keyboard for snapshot matching.
/// </summary>
public sealed record KeyboardId(string VendorId, string ProductId, string Uid)
{
    /// <summary>
    /// Creates a KeyboardId from a Vial keyboard UID (ulong) and HID device info.
    /// </summary>
    public static KeyboardId From(int vendorId, int productId, ulong vialUid) =>
        new(vendorId.ToString("X4"), productId.ToString("X4"), vialUid.ToString("X16"));
}
