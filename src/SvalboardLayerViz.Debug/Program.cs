using HidSharp;
using SvalboardLayerViz.Core.Device;
using SvalboardLayerViz.Core.Protocol;

Console.WriteLine("=== Svalboard Tapping Term Reader ===\n");

var deviceService = new DeviceConnectionService();
var protocol = new VialProtocolService();

var devices = deviceService.FindVialDevices();
if (devices.Count == 0) { Console.WriteLine("No Vial device found."); return; }

var device = devices[0];
Console.WriteLine($"Device: {device.ProductName}");

protocol.Connect(device.HidDevice);

// Read tapping term using the production code path
var tappingTerm = protocol.GetQmkSetting(VialCommands.QmkSettingTappingTerm);
Console.WriteLine($"\nTapping term (setting 0x{VialCommands.QmkSettingTappingTerm:X4}): {(tappingTerm.HasValue ? $"{tappingTerm.Value} ms" : "not available")}");

// Read a few more times to confirm consistency
Console.WriteLine("\nConsistency check (5 reads):");
for (var i = 0; i < 5; i++)
{
    var value = protocol.GetQmkSetting(VialCommands.QmkSettingTappingTerm);
    Console.WriteLine($"  Read {i + 1}: {(value.HasValue ? $"{value.Value} ms" : "null")}");
}

protocol.Dispose();
Console.WriteLine("\nDone.");
