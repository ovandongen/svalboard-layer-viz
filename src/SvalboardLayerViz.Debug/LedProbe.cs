using HidSharp;
using SvalboardLayerViz.Core.Device;
using SvalboardLayerViz.Core.Protocol;

namespace SvalboardLayerViz.Debug;

/// <summary>
/// Standalone probe that verifies which LED/layer-color commands the connected
/// Svalboard firmware actually responds to. Does not modify any device state.
///
/// Probes:
///   1. Svalboard custom sub-protocol handshake (0xEE, 0x01) — expects "sval".
///   2. Per-layer color read (0xEE, 0x10, layer) — returns (h, s, v).
///   3. Current LED color via standard VIA:
///        a. id_qmk_rgblight_color     (0x08, 0x83)
///        b. id_qmk_rgb_matrix_color   (0x08, 0x89)
///
/// Prints raw responses so we can decide which commands are viable before
/// changing anything in Core / App.
/// </summary>
public static class LedProbe
{
    private const int ReportSize = 32;

    public static int Run()
    {
        Console.WriteLine("=== Svalboard LED / layer-color probe ===");
        Console.WriteLine();

        var discovery = new DeviceConnectionService();
        var devices = discovery.FindVialDevices();
        if (devices.Count == 0)
        {
            Console.WriteLine("No Vial-compatible devices found. Is the keyboard plugged in?");
            return 1;
        }

        Console.WriteLine($"Found {devices.Count} Vial device(s):");
        for (var i = 0; i < devices.Count; i++)
            Console.WriteLine($"  [{i}] {devices[i]}");
        Console.WriteLine();

        var target = devices[0];
        Console.WriteLine($"Using [0] {target}");
        Console.WriteLine();

        using var stream = target.HidDevice.Open();
        stream.ReadTimeout = 2000;
        stream.WriteTimeout = 2000;
        var maxOut = target.HidDevice.GetMaxOutputReportLength();
        var maxIn = target.HidDevice.GetMaxInputReportLength();

        // Sanity: use Core's GetLayerCount via a fresh VialProtocolService so
        // we know basic comms work before probing anything exotic.
        int layerCount;
        try
        {
            using var proto = new VialProtocolService();
            proto.Connect(target.HidDevice);
            layerCount = proto.GetLayerCount();
            Console.WriteLine($"[sanity] GetLayerCount() = {layerCount}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[sanity] failed: {ex.Message}");
            return 2;
        }
        Console.WriteLine();

        // Re-open our own stream after VialProtocolService disposed its own.
        using var stream2 = target.HidDevice.Open();
        stream2.ReadTimeout = 2000;
        stream2.WriteTimeout = 2000;

        // --- Probe 1: Svalboard handshake ---
        Console.WriteLine("--- Probe 1: Svalboard handshake (0xEE, 0x01) ---");
        var handshake = Send(stream2, maxOut, maxIn, [0xEE, 0x01]);
        DumpHex("  response", handshake);
        var isSval = handshake[0] == (byte)'s'
                  && handshake[1] == (byte)'v'
                  && handshake[2] == (byte)'a'
                  && handshake[3] == (byte)'l';
        if (isSval)
        {
            var protoVersion = BitConverter.ToUInt32(handshake, 4);
            Console.WriteLine($"  -> sval sub-protocol detected, proto version = {protoVersion}");
        }
        else
        {
            Console.WriteLine("  -> NOT a sval firmware (or command not supported)");
        }
        Console.WriteLine();

        // --- Probe 2: Per-layer color read ---
        if (isSval)
        {
            Console.WriteLine("--- Probe 2: Per-layer color read (0xEE, 0x10, layer) ---");
            for (var l = 0; l < layerCount; l++)
            {
                var resp = Send(stream2, maxOut, maxIn, [0xEE, 0x10, (byte)l]);
                var h = resp[0];
                var s = resp[1];
                var v = resp[2];
                Console.WriteLine($"  layer {l}: H={h,3} S={s,3} V={v,3}   (first 8 bytes: {FormatHex(resp, 8)})");
            }
            Console.WriteLine();
        }
        else
        {
            Console.WriteLine("--- Probe 2: skipped (no sval handshake) ---");
            Console.WriteLine();
        }

        // --- Probe 3a: Current LED color via rgblight ---
        Console.WriteLine("--- Probe 3a: id_qmk_rgblight_color (0x08, 0x83) ---");
        var rgbLight = Send(stream2, maxOut, maxIn, [0x08, 0x83]);
        DumpHex("  response", rgbLight);
        Console.WriteLine();

        // --- Probe 3b: Current LED color via rgb_matrix ---
        Console.WriteLine("--- Probe 3b: id_qmk_rgb_matrix_color (0x08, 0x89) ---");
        var rgbMatrix = Send(stream2, maxOut, maxIn, [0x08, 0x89]);
        DumpHex("  response", rgbMatrix);
        Console.WriteLine();

        Console.WriteLine("=== Probe complete ===");
        Console.WriteLine();
        Console.WriteLine("Next: physically switch layers (including mouse layer) and re-run.");
        Console.WriteLine("If probe 3a or 3b returns different values per layer, that's our");
        Console.WriteLine("active-layer detection channel.");
        return 0;
    }

    /// <summary>
    /// Sends a raw command (padded to 32 bytes) and returns the 32-byte response.
    /// Intentionally duplicated from VialProtocolService.SendCommand so the probe
    /// stays self-contained and we don't have to touch Core during investigation.
    /// </summary>
    private static byte[] Send(HidStream stream, int maxOut, int maxIn, byte[] command)
    {
        var writeBuffer = new byte[maxOut];
        writeBuffer[0] = 0x00; // report ID
        Array.Copy(command, 0, writeBuffer, 1, Math.Min(command.Length, maxOut - 1));

        stream.Write(writeBuffer);

        var readBuffer = new byte[maxIn];
        var bytesRead = stream.Read(readBuffer);

        var response = new byte[ReportSize];
        var dataOffset = bytesRead > ReportSize ? 1 : 0;
        Array.Copy(readBuffer, dataOffset, response, 0, ReportSize);
        return response;
    }

    private static void DumpHex(string label, byte[] bytes)
    {
        Console.WriteLine($"{label}: {FormatHex(bytes, bytes.Length)}");
    }

    private static string FormatHex(byte[] bytes, int count)
    {
        var hex = new System.Text.StringBuilder();
        for (var i = 0; i < count && i < bytes.Length; i++)
        {
            if (i > 0) hex.Append(' ');
            hex.Append(bytes[i].ToString("X2"));
        }
        return hex.ToString();
    }
}
