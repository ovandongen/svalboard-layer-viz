using HidSharp;
using SvalboardLayerViz.Core.Protocol;

Console.WriteLine("=== Svalboard Definition Download Debug ===\n");

// Find the Vial device
var allDevices = DeviceList.Local.GetHidDevices();
HidDevice? vialDevice = null;

foreach (var dev in allDevices)
{
    try
    {
        var rd = dev.GetReportDescriptor();
        foreach (var item in rd.DeviceItems)
        {
            uint usage1 = ((uint)VialCommands.VialUsagePage << 16) | VialCommands.VialUsage1;
            if (item.Usages.ContainsValue(usage1))
            {
                vialDevice = dev;
                break;
            }
        }
        if (vialDevice is not null) break;
    }
    catch { }
}

if (vialDevice is null) { Console.WriteLine("No Vial device found."); return; }
Console.WriteLine($"Device: {vialDevice.GetProductName()}");
Console.WriteLine($"MaxIn={vialDevice.GetMaxInputReportLength()} MaxOut={vialDevice.GetMaxOutputReportLength()}");

using var stream = vialDevice.Open();
stream.ReadTimeout = 3000;
stream.WriteTimeout = 3000;

var maxOut = vialDevice.GetMaxOutputReportLength();
var maxIn = vialDevice.GetMaxInputReportLength();

byte[] SendCmd(byte[] command)
{
    var writeBuffer = new byte[maxOut];
    writeBuffer[0] = 0x00;
    Array.Copy(command, 0, writeBuffer, 1, Math.Min(command.Length, maxOut - 1));
    stream.Write(writeBuffer);

    var readBuffer = new byte[maxIn];
    var bytesRead = stream.Read(readBuffer);

    var response = new byte[VialCommands.ReportSize];
    var offset = bytesRead > VialCommands.ReportSize ? 1 : 0;
    Array.Copy(readBuffer, offset, response, 0, VialCommands.ReportSize);
    return response;
}

// Test switch matrix state
Console.WriteLine("\n--- GetSwitchMatrixState (raw dump) ---");
Console.WriteLine("Press and hold a key on the Svalboard, then press Enter here...");
Console.ReadLine();

var matrixResp = SendCmd([VialCommands.GetKeyboardValue, VialCommands.SwitchMatrixState]);
Console.WriteLine($"  Full response ({matrixResp.Length} bytes):");
for (var i = 0; i < matrixResp.Length; i += 16)
{
    var end = Math.Min(i + 16, matrixResp.Length);
    var hex = string.Join(" ", matrixResp.Skip(i).Take(end - i).Select(b => $"{b:X2}"));
    Console.WriteLine($"    [{i:D2}] {hex}");
}

// Interpret as matrix at different offsets
foreach (var startOffset in new[] { 0, 1, 2, 3, 4 })
{
    Console.WriteLine($"\n  Matrix interpretation (data starting at byte {startOffset}):");
    for (var row = 0; row < 10 && (row + startOffset) < 32; row++)
    {
        var rowByte = matrixResp[row + startOffset];
        var bits = Convert.ToString(rowByte, 2).PadLeft(6, '0');
        var pressed = rowByte != 0 ? $" <-- row {row} has pressed keys" : "";
        Console.WriteLine($"    Row {row}: 0x{rowByte:X2} = {bits}{pressed}");
    }
}

Console.WriteLine("\nNow release all keys and press Enter...");
Console.ReadLine();

var matrixResp2 = SendCmd([VialCommands.GetKeyboardValue, VialCommands.SwitchMatrixState]);
Console.WriteLine($"  Full response (no keys pressed):");
for (var i = 0; i < matrixResp2.Length; i += 16)
{
    var end = Math.Min(i + 16, matrixResp2.Length);
    var hex = string.Join(" ", matrixResp2.Skip(i).Take(end - i).Select(b => $"{b:X2}"));
    Console.WriteLine($"    [{i:D2}] {hex}");
}

// Get definition size
Console.WriteLine("\n--- GetDefinitionSize ---");
var sizeResp = SendCmd([VialCommands.VialPrefix, VialCommands.VialGetSize]);
Console.WriteLine($"  Raw response: [{string.Join(" ", sizeResp.Take(16).Select(b => $"{b:X2}"))}]");
var totalSize = BitConverter.ToInt32(sizeResp, 0);
Console.WriteLine($"  Definition size: {totalSize} bytes");

// Download definition blocks
Console.WriteLine($"\n--- Downloading definition ({totalSize} bytes) ---");
var data = new byte[totalSize];
var dataOffset2 = 0;
var blockIndex = 0;

while (dataOffset2 < totalSize)
{
    var cmd = new byte[VialCommands.ReportSize];
    cmd[0] = VialCommands.VialPrefix;
    cmd[1] = VialCommands.VialGetDefinition;
    cmd[2] = (byte)(blockIndex & 0xFF);
    cmd[3] = (byte)((blockIndex >> 8) & 0xFF);

    var response = SendCmd(cmd);

    if (blockIndex < 3)
    {
        Console.WriteLine($"  Block {blockIndex}: [{string.Join(" ", response.Select(b => $"{b:X2}"))}]");
    }

    // For the first block, check if XZ magic is present and where
    var copyFrom = 0;
    if (blockIndex == 0)
    {
        // Look for XZ magic bytes: FD 37 7A 58 5A 00
        for (var i = 0; i <= response.Length - 6; i++)
        {
            if (response[i] == 0xFD && response[i + 1] == 0x37 && response[i + 2] == 0x7A &&
                response[i + 3] == 0x58 && response[i + 4] == 0x5A && response[i + 5] == 0x00)
            {
                Console.WriteLine($"  XZ magic found at offset {i}");
                copyFrom = i;
                break;
            }
        }
        if (copyFrom == 0)
            Console.WriteLine($"  XZ magic at offset 0 (or not found)");
    }

    var bytesToCopy = Math.Min(VialCommands.ReportSize - copyFrom, totalSize - dataOffset2);
    Array.Copy(response, copyFrom, data, dataOffset2, bytesToCopy);
    dataOffset2 += bytesToCopy;
    blockIndex++;
}

Console.WriteLine($"\n  Downloaded {dataOffset2} bytes in {blockIndex} blocks");
Console.WriteLine($"  First 16 bytes: [{string.Join(" ", data.Take(16).Select(b => $"{b:X2}"))}]");
Console.WriteLine($"  Last 16 bytes:  [{string.Join(" ", data.Skip(Math.Max(0, totalSize - 16)).Select(b => $"{b:X2}"))}]");

// Try decompressing
Console.WriteLine("\n--- Attempting XZ decompression ---");
try
{
    var json = XzDecompressor.DecompressToString(data);
    Console.WriteLine($"  Success! Decompressed to {json.Length} chars");
    Console.WriteLine($"  First 200 chars: {json[..Math.Min(200, json.Length)]}");
}
catch (Exception ex)
{
    Console.WriteLine($"  FAILED: {ex.GetType().Name}: {ex.Message}");

    // Try with PayloadSize (28) instead of ReportSize (32) to see if that's the issue
    Console.WriteLine("\n--- Retrying with PayloadSize (28 bytes per block) ---");
    data = new byte[totalSize];
    dataOffset2 = 0;
    blockIndex = 0;

    while (dataOffset2 < totalSize)
    {
        var cmd = new byte[VialCommands.ReportSize];
        cmd[0] = VialCommands.VialPrefix;
        cmd[1] = VialCommands.VialGetDefinition;
        cmd[2] = (byte)(blockIndex & 0xFF);
        cmd[3] = (byte)((blockIndex >> 8) & 0xFF);

        var response = SendCmd(cmd);

        var copyFrom = 0;
        if (blockIndex == 0)
        {
            for (var i = 0; i <= response.Length - 6; i++)
            {
                if (response[i] == 0xFD && response[i + 1] == 0x37)
                {
                    copyFrom = i;
                    break;
                }
            }
        }

        var bytesToCopy = Math.Min(VialCommands.PayloadSize, totalSize - dataOffset2);
        Array.Copy(response, copyFrom, data, dataOffset2, bytesToCopy);
        dataOffset2 += bytesToCopy;
        blockIndex++;
    }

    Console.WriteLine($"  Downloaded {dataOffset2} bytes in {blockIndex} blocks");
    Console.WriteLine($"  First 16 bytes: [{string.Join(" ", data.Take(16).Select(b => $"{b:X2}"))}]");

    try
    {
        var json = XzDecompressor.DecompressToString(data);
        Console.WriteLine($"  Success with PayloadSize! Decompressed to {json.Length} chars");
        Console.WriteLine($"  First 200 chars: {json[..Math.Min(200, json.Length)]}");
    }
    catch (Exception ex2)
    {
        Console.WriteLine($"  Also FAILED: {ex2.Message}");
    }
}
