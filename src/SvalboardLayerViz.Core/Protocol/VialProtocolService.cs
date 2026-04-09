using HidSharp;

namespace SvalboardLayerViz.Core.Protocol;

/// <summary>
/// Handles Vial protocol communication over USB HID.
/// Encodes commands into 32-byte reports and decodes responses.
///
/// Reference: keybard-ng/src/services/usb.service.ts and vial.service.ts
/// </summary>
public class VialProtocolService : IVialProtocolService
{
    private HidStream? _stream;
    private int _maxOutputLength;
    private int _maxInputLength;

    /// <summary>
    /// Opens a connection to the given HID device.
    /// </summary>
    public void Connect(HidDevice device)
    {
        _stream?.Dispose();
        _stream = device.Open();
        _stream.ReadTimeout = 5000;
        _stream.WriteTimeout = 5000;
        _maxOutputLength = device.GetMaxOutputReportLength();
        _maxInputLength = device.GetMaxInputReportLength();
    }

    /// <summary>
    /// Sends a raw 32-byte command and returns the 32-byte response.
    /// HidSharp requires the report ID (0x00) as the first byte of writes,
    /// and may include it in reads depending on platform.
    /// </summary>
    private byte[] SendCommand(byte[] command)
    {
        if (_stream is null)
            throw new InvalidOperationException("Not connected to a device.");

        // HidSharp expects [reportId, ...payload] for writes
        var writeBuffer = new byte[_maxOutputLength];
        writeBuffer[0] = 0x00; // Report ID
        Array.Copy(command, 0, writeBuffer, 1, Math.Min(command.Length, _maxOutputLength - 1));

        _stream.Write(writeBuffer);

        var readBuffer = new byte[_maxInputLength];
        var bytesRead = _stream.Read(readBuffer);

        // Extract the 32-byte payload, skipping report ID if present
        var response = new byte[VialCommands.ReportSize];
        var dataOffset = bytesRead > VialCommands.ReportSize ? 1 : 0;
        Array.Copy(readBuffer, dataOffset, response, 0, VialCommands.ReportSize);
        return response;
    }

    /// <summary>
    /// Gets the number of layers configured on the keyboard.
    /// </summary>
    public int GetLayerCount()
    {
        var response = SendCommand([VialCommands.GetLayerCount]);
        return response[1];
    }

    /// <summary>
    /// Gets the Vial keyboard ID (8 bytes).
    /// </summary>
    public ulong GetKeyboardId()
    {
        var response = SendCommand([VialCommands.VialPrefix, VialCommands.VialGetKeyboardId]);
        // Keyboard ID starts at byte 0 of response, 8 bytes
        return BitConverter.ToUInt64(response, 0);
    }

    /// <summary>
    /// Gets the size of the compressed keyboard definition payload.
    /// </summary>
    public int GetDefinitionSize()
    {
        var response = SendCommand([VialCommands.VialPrefix, VialCommands.VialGetSize]);
        return BitConverter.ToInt32(response, 0);
    }

    /// <summary>
    /// Downloads the full compressed keyboard definition as a byte array.
    /// The result is XZ-compressed JSON.
    /// </summary>
    public byte[] GetDefinition()
    {
        var totalSize = GetDefinitionSize();
        var data = new byte[totalSize];
        var offset = 0;
        var blockIndex = 0;

        while (offset < totalSize)
        {
            var cmd = new byte[VialCommands.ReportSize];
            cmd[0] = VialCommands.VialPrefix;
            cmd[1] = VialCommands.VialGetDefinition;
            // Block index as 16-bit LE
            cmd[2] = (byte)(blockIndex & 0xFF);
            cmd[3] = (byte)((blockIndex >> 8) & 0xFF);

            var response = SendCommand(cmd);

            // Device may echo command prefix — detect by looking for XZ magic bytes
            var dataOffset = 0;
            if (blockIndex == 0)
            {
                dataOffset = FindXzMagicOffset(response);
            }

            var bytesToCopy = Math.Min(VialCommands.ReportSize - dataOffset, totalSize - offset);
            Array.Copy(response, dataOffset, data, offset, bytesToCopy);
            offset += bytesToCopy;
            blockIndex++;
        }

        return data;
    }

    /// <summary>
    /// Reads the full keymap buffer from the device.
    /// Returns a flat array of 16-bit keycodes in big-endian order.
    /// Total size = layers * rows * cols * 2 bytes.
    /// </summary>
    public ushort[,,] GetKeymapBuffer(int layers, int rows, int cols)
    {
        var totalBytes = layers * rows * cols * 2;
        var rawData = new byte[totalBytes];
        var offset = 0;

        while (offset < totalBytes)
        {
            var chunkSize = Math.Min(VialCommands.PayloadSize, totalBytes - offset);

            var cmd = new byte[VialCommands.ReportSize];
            cmd[0] = VialCommands.KeymapGetBuffer;
            // Offset as 16-bit BE
            cmd[1] = (byte)((offset >> 8) & 0xFF);
            cmd[2] = (byte)(offset & 0xFF);
            // Size
            cmd[3] = (byte)chunkSize;

            var response = SendCommand(cmd);
            Array.Copy(response, 4, rawData, offset, chunkSize);
            offset += chunkSize;
        }

        // Parse into 3D array [layer, row, col] — values are big-endian 16-bit
        var keymap = new ushort[layers, rows, cols];
        for (var layer = 0; layer < layers; layer++)
        {
            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < cols; col++)
                {
                    var idx = ((layer * rows * cols) + (row * cols) + col) * 2;
                    keymap[layer, row, col] = (ushort)((rawData[idx] << 8) | rawData[idx + 1]);
                }
            }
        }

        return keymap;
    }

    /// <summary>
    /// Reads the current switch matrix state (which physical keys are pressed).
    /// Uses VIA id_switch_matrix_state: send [0x02, 0x03], response has row bytes
    /// where each bit represents a column (bit set = key pressed).
    /// </summary>
    public bool[,] GetSwitchMatrixState(int rows, int cols)
    {
        var response = SendCommand([VialCommands.GetKeyboardValue, VialCommands.SwitchMatrixState]);

        var state = new bool[rows, cols];
        // Response: bytes 0-1 are echoed command, matrix data starts at byte 2
        for (var row = 0; row < rows && (row + 2) < VialCommands.ReportSize; row++)
        {
            var rowByte = response[row + 2];
            for (var col = 0; col < cols; col++)
            {
                state[row, col] = (rowByte & (1 << col)) != 0;
            }
        }

        return state;
    }

    /// <summary>
    /// Reads a QMK setting value from the device via Vial's QMK Settings protocol.
    /// Response format: byte 0 = status (0x00 = ok), value as u16 LE at byte 1.
    /// Returns null if the device doesn't support QMK settings or the setting is unavailable.
    /// </summary>
    public ushort? GetQmkSetting(ushort settingId)
    {
        try
        {
            var response = SendCommand([
                VialCommands.VialPrefix, VialCommands.VialQmkSettingsGet,
                (byte)(settingId & 0xFF), (byte)((settingId >> 8) & 0xFF)
            ]);

            // Check if response is all zeros (command not supported)
            if (response.All(b => b == 0))
                return null;

            // Byte 0 = status, value as u16 LE at byte 1
            return BitConverter.ToUInt16(response, 1);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Probes the Svalboard custom sub-protocol. Sends [0xEE, 0x01] and checks
    /// for the ASCII "sval" handshake followed by a u32 LE proto version.
    /// Returns null if the handshake fails or the command isn't supported.
    /// </summary>
    public uint? GetSvalProtoVersion()
    {
        try
        {
            var response = SendCommand([VialCommands.SvalPrefix, VialCommands.SvalGetProtoVersion]);
            if (response[0] != (byte)'s' || response[1] != (byte)'v' ||
                response[2] != (byte)'a' || response[3] != (byte)'l')
                return null;
            return BitConverter.ToUInt32(response, 4);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the stored HSV color for a single layer via [0xEE, 0x10, layer].
    /// Returns null if the device doesn't respond or responds with all zeros.
    /// </summary>
    public (byte H, byte S, byte V)? GetLayerColor(int layer)
    {
        try
        {
            var response = SendCommand([
                VialCommands.SvalPrefix,
                VialCommands.SvalLayerColorGet,
                (byte)layer
            ]);
            // All-zero response is treated as "unsupported" — a legitimate
            // layer color never has V=0 because the LED would be off.
            if (response[0] == 0 && response[1] == 0 && response[2] == 0)
                return null;
            return (response[0], response[1], response[2]);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the current rgblight hue+sat via standard VIA [0x08, 0x83].
    /// Response format: [0x08, 0x83, H, S, ...]. Returns null if the firmware
    /// doesn't implement this lighting sub-command.
    /// </summary>
    public (byte H, byte S)? GetCurrentLedHueSat()
    {
        try
        {
            var response = SendCommand([VialCommands.LightingGetValue, VialCommands.QmkRgblightColor]);
            // Non-supporting firmware returns all zeros or doesn't echo the
            // command bytes. A valid response echoes [0x08, 0x83, H, S].
            if (response[0] != VialCommands.LightingGetValue ||
                response[1] != VialCommands.QmkRgblightColor)
                return null;
            return (response[2], response[3]);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the offset of XZ magic bytes in a response buffer.
    /// The device may echo the command prefix before the actual data.
    /// </summary>
    private static int FindXzMagicOffset(byte[] buffer)
    {
        for (var i = 0; i <= buffer.Length - VialCommands.XzMagicBytes.Length; i++)
        {
            var match = true;
            for (var j = 0; j < VialCommands.XzMagicBytes.Length; j++)
            {
                if (buffer[i + j] != VialCommands.XzMagicBytes[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return 0; // Fallback: assume no echo
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
