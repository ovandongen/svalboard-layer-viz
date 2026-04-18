namespace SvalboardLayerViz.Core.History;

/// <summary>
/// How well a snapshot matches the currently connected keyboard.
/// </summary>
public enum SnapshotCompatibility
{
    /// <summary>VID + PID + UID all match — safe to restore.</summary>
    Exact,

    /// <summary>VID + PID match but UID differs — restorable with warning (lenient mode).</summary>
    Firmware,

    /// <summary>VID or PID differs — not restorable to this device.</summary>
    None,
}

public static class SnapshotCompatibilityChecker
{
    /// <summary>
    /// Determines how compatible a snapshot's keyboard ID is with the current device.
    /// </summary>
    public static SnapshotCompatibility Check(KeyboardId snapshot, KeyboardId device)
    {
        if (snapshot.VendorId == device.VendorId &&
            snapshot.ProductId == device.ProductId &&
            snapshot.Uid == device.Uid)
            return SnapshotCompatibility.Exact;

        if (snapshot.VendorId == device.VendorId &&
            snapshot.ProductId == device.ProductId)
            return SnapshotCompatibility.Firmware;

        return SnapshotCompatibility.None;
    }
}
