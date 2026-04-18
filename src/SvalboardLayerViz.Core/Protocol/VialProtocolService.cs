using HidSharp;
using SvalboardLayerViz.Core.Diagnostics;
using SvalboardLayerViz.Core.Dynamic;

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
    private readonly object _sendLock = new();
    private bool _disposed;

    /// <summary>
    /// Opens a connection to the given HID device.
    /// </summary>
    public void Connect(HidDevice device)
    {
        // Close + Dispose: Close cleanly shuts down the protocol-level handle,
        // Dispose frees managed wrappers. Stream.Close calls Dispose(true) so
        // calling both is idempotent — but skipping Close can leave the OS
        // handle lingering until GC, blocking re-enumeration after a cable yank.
        _stream?.Close();
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
    /// <remarks>
    /// <c>protected virtual</c> so tests can subclass and swap in fake
    /// responses without needing a real USB device.
    /// </remarks>
    protected virtual byte[] SendCommand(byte[] command)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VialProtocolService));
        if (_stream is null)
            throw new InvalidOperationException("Not connected to a device.");

        // Lock guarantees that the write→read pair is atomic. Without this,
        // concurrent callers (e.g. MatrixPollingService + LedPollingService on
        // separate threads) can interleave their writes and reads, causing one
        // service to receive the other's response — manifesting as phantom key
        // presses or missed LED color changes.
        lock (_sendLock)
        {
            // HidSharp expects [reportId, ...payload] for writes
            var writeBuffer = new byte[_maxOutputLength];
            writeBuffer[0] = 0x00; // Report ID
            Array.Copy(command, 0, writeBuffer, 1, Math.Min(command.Length, _maxOutputLength - 1));

            _stream.Write(writeBuffer);

            var readBuffer = new byte[_maxInputLength];
            var bytesRead = _stream.Read(readBuffer);
            return ExtractResponse(readBuffer, bytesRead);
        }
    }

    /// <summary>
    /// Extracts the 32-byte response payload from an HID read buffer.
    /// HidSharp returns reads with the report ID byte prepended on some platforms
    /// (Windows) and without it on others (macOS/Linux). Detect by length: if the
    /// total read exceeds <see cref="VialCommands.ReportSize"/>, byte 0 is the report ID.
    /// Throws <see cref="IOException"/> on a short read so a truncated firmware
    /// response surfaces as a hard error instead of silently zero-padded garbage.
    /// </summary>
    internal static byte[] ExtractResponse(byte[] readBuffer, int bytesRead)
    {
        var dataOffset = bytesRead > VialCommands.ReportSize ? 1 : 0;
        var requiredBytes = dataOffset + VialCommands.ReportSize;
        if (bytesRead < requiredBytes)
            throw new IOException(
                $"Short HID read: expected {requiredBytes} bytes, got {bytesRead}");

        var response = new byte[VialCommands.ReportSize];
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
        // Sanity-cap. Vial definitions are XZ-compressed JSON, a few KB at most.
        // A garbage size (negative, int.MinValue, or multi-MB) would OOM the
        // allocation or loop forever copying zero-byte chunks.
        if (totalSize <= 0 || totalSize > MaxDefinitionSize)
            throw new InvalidDataException(
                $"Firmware reported implausible definition size {totalSize} bytes " +
                $"(must be 1..{MaxDefinitionSize}).");
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
            if (bytesToCopy <= 0)
                throw new InvalidDataException(
                    $"Device returned empty chunk at block {blockIndex} " +
                    $"(dataOffset={dataOffset}); aborting to avoid infinite loop.");
            Array.Copy(response, dataOffset, data, offset, bytesToCopy);
            offset += bytesToCopy;
            blockIndex++;
        }

        return data;
    }

    // Sanity-caps on network-derived allocations. Real firmware values are
    // a few KB at most for definitions, a few hundred bytes for the keymap
    // (10x6x2 ≈ 120 bytes per layer × ~16 layers max), and up to ~1 KB for
    // the macro buffer. The caps below are generous ceilings that still fail
    // loud on garbage size reports, matching entry 350's defence-in-depth
    // for GetDefinition.
    private const int MaxDefinitionSize = 64 * 1024;
    private const int MaxKeymapSize = 64 * 1024;
    private const int MaxMacroSize = 64 * 1024;

    /// <summary>
    /// Reads the full keymap buffer from the device.
    /// Returns a flat array of 16-bit keycodes in big-endian order.
    /// Total size = layers * rows * cols * 2 bytes.
    /// </summary>
    public ushort[,,] GetKeymapBuffer(int layers, int rows, int cols)
    {
        // Guard against firmware reporting implausible dimensions — or caller
        // passing values that would cause int overflow. `layers`, `rows`,
        // `cols` all flow from network-sourced device metadata; we don't
        // control them.
        if (layers <= 0 || rows <= 0 || cols <= 0)
            throw new InvalidDataException(
                $"Invalid keymap dimensions (layers={layers}, rows={rows}, cols={cols}); " +
                "all must be positive.");
        var totalBytesLong = (long)layers * rows * cols * 2;
        if (totalBytesLong > MaxKeymapSize)
            throw new InvalidDataException(
                $"Firmware reported implausible keymap size {totalBytesLong} bytes " +
                $"(layers={layers}, rows={rows}, cols={cols}, max={MaxKeymapSize}).");
        var totalBytes = (int)totalBytesLong;
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
        const int matrixDataOffset = 2;
        if (rows + matrixDataOffset > VialCommands.ReportSize)
            throw new InvalidOperationException(
                $"Matrix with {rows} rows does not fit in {VialCommands.ReportSize}-byte report " +
                $"(max {VialCommands.ReportSize - matrixDataOffset} rows).");
        if (cols > 8)
            throw new InvalidOperationException(
                $"Matrix with {cols} cols does not fit in single byte row bitmask (max 8).");

        var response = SendCommand([VialCommands.GetKeyboardValue, VialCommands.SwitchMatrixState]);

        var state = new bool[rows, cols];
        for (var row = 0; row < rows; row++)
        {
            var rowByte = response[row + matrixDataOffset];
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
    public ushort? GetQmkSetting(ushort settingId, byte width = 2)
    {
        try
        {
            var response = SendCommand([
                VialCommands.VialPrefix, VialCommands.VialQmkSettingsGet,
                (byte)(settingId & 0xFF), (byte)((settingId >> 8) & 0xFF)
            ]);

            // Check if response is all zeros (command not supported)
            if (response.All(b => b == 0))
            {
                DiagnosticLog.Debug("Proto", $"QmkGet 0x{settingId:X4} w={width}: all-zero response (unsupported)");
                return null;
            }

            // Byte 0 = status, value as LE at byte 1. For u8 settings only
            // read 1 byte to avoid garbage in byte 2.
            var result = width == 1 ? response[1] : BitConverter.ToUInt16(response, 1);
            DiagnosticLog.Debug("Proto",
                $"QmkGet 0x{settingId:X4} w={width}: raw=[{HexDump(response)}] → {result}");
            return result;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        {
            DiagnosticLog.Warn("Proto", $"QmkGet 0x{settingId:X4} failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Discovers which QMK setting IDs the firmware supports via paginated query.
    /// Sends [0xFE, 0x09, offset_lo, offset_hi] with offset = last seen ID.
    /// Response: u16 LE QSIDs (2 bytes each), pre-filled with 0xFFFF terminator.
    /// Returns empty list if the firmware doesn't support QMK settings.
    /// </summary>
    public IReadOnlyList<ushort> GetQmkSettingsList()
    {
        try
        {
            var result = new List<ushort>();
            ushort offset = 0;

            while (true)
            {
                var response = SendCommand([
                    VialCommands.VialPrefix, VialCommands.VialQmkSettingsQuery,
                    (byte)(offset & 0xFF), (byte)((offset >> 8) & 0xFF)
                ]);

                var foundAny = false;
                DiagnosticLog.Debug("Proto",
                    $"QmkQuery offset={offset}: raw=[{HexDump(response, 16)}]");
                // Each entry is 2 bytes: QSID u16 LE, terminated by 0xFFFF
                for (var i = 0; i + 1 < VialCommands.ReportSize; i += 2)
                {
                    var id = BitConverter.ToUInt16(response, i);
                    if (id == 0xFFFF)
                        return result;

                    DiagnosticLog.Debug("Proto", $"  QSID 0x{id:X4}");
                    result.Add(id);
                    foundAny = true;
                }

                if (!foundAny || result.Count == 0)
                    return result;

                // Next page: ask for IDs greater than the last one we received
                offset = result[^1];
            }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        {
            DiagnosticLog.Warn("Proto", $"GetQmkSettingsList failed: {ex.GetType().Name}: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Sets a QMK setting value on the device.
    /// Sends [0xFE, 0x0B, id_lo, id_hi, val_lo, val_hi].
    /// Throws on non-zero status byte in response.
    /// </summary>
    public void SetQmkSetting(ushort settingId, ushort value, byte width = 2)
    {
        // Build command: [0xFE, 0x0B, id_lo, id_hi, val_lo, (val_hi if width==2)]
        var cmd = width == 1
            ? new byte[] { VialCommands.VialPrefix, VialCommands.VialQmkSettingsSet,
                (byte)(settingId & 0xFF), (byte)((settingId >> 8) & 0xFF),
                (byte)(value & 0xFF) }
            : [VialCommands.VialPrefix, VialCommands.VialQmkSettingsSet,
                (byte)(settingId & 0xFF), (byte)((settingId >> 8) & 0xFF),
                (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF)];
        var response = SendCommand(cmd);
        DiagnosticLog.Debug("Proto",
            $"QmkSet 0x{settingId:X4} w={width} val={value}: cmd=[{HexDump(cmd)}] resp=[{HexDump(response)}]");

        if (response[0] != 0)
            throw new InvalidOperationException(
                $"SetQmkSetting failed for setting 0x{settingId:X4}: status byte {response[0]}");
    }

    /// <summary>
    /// Resets all QMK settings to firmware defaults. Sends [0xFE, 0x0C].
    /// </summary>
    public void ResetQmkSettings()
    {
        SendCommand([VialCommands.VialPrefix, VialCommands.VialQmkSettingsReset]);
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
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        {
            DiagnosticLog.Warn("Proto", $"GetSvalProtoVersion failed: {ex.GetType().Name}: {ex.Message}");
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
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        {
            DiagnosticLog.Warn("Proto", $"GetLayerColor({layer}) failed: {ex.GetType().Name}: {ex.Message}");
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
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        {
            DiagnosticLog.Warn("Proto", $"GetCurrentLedHueSat failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // --- Write commands ---

    /// <summary>
    /// Sets a single keycode on the device.
    /// Command format: [0x05, layer, row, col, kc_hi, kc_lo]
    /// </summary>
    public void SetKeycode(int layer, int row, int col, ushort keycode)
    {
        SendCommand([
            VialCommands.SetKeycode,
            (byte)layer,
            (byte)row,
            (byte)col,
            (byte)((keycode >> 8) & 0xFF),
            (byte)(keycode & 0xFF)
        ]);
    }

    /// <summary>
    /// Writes a chunk of keymap data to the device buffer.
    /// Command format: [0x13, offset_hi, offset_lo, size, ...payload]
    /// </summary>
    public void KeymapSetBuffer(int offset, byte[] data)
    {
        if (data.Length > VialCommands.PayloadSize)
            throw new ArgumentException(
                $"Chunk size {data.Length} exceeds maximum payload of {VialCommands.PayloadSize} bytes.",
                nameof(data));

        var cmd = new byte[VialCommands.ReportSize];
        cmd[0] = VialCommands.KeymapSetBuffer;
        cmd[1] = (byte)((offset >> 8) & 0xFF); // offset big-endian
        cmd[2] = (byte)(offset & 0xFF);
        cmd[3] = (byte)data.Length;
        Array.Copy(data, 0, cmd, 4, data.Length);

        SendCommand(cmd);
    }

    /// <summary>
    /// Resets the dynamic keymap to firmware defaults.
    /// </summary>
    public void DynamicKeymapReset()
    {
        SendCommand([VialCommands.DynamicKeymapReset]);
    }

    /// <summary>
    /// Resets the full EEPROM to defaults.
    /// </summary>
    public void EepromReset()
    {
        SendCommand([VialCommands.EepromReset]);
    }

    // --- Macro commands ---

    /// <summary>
    /// Gets the number of macro slots configured on the device.
    /// </summary>
    public int GetMacroCount()
    {
        var response = SendCommand([VialCommands.MacroGetCount]);
        return response[1];
    }

    /// <summary>
    /// Gets the total macro buffer size in bytes.
    /// Response: byte 1-2 = size as BE16.
    /// </summary>
    public int GetMacroBufferSize()
    {
        var response = SendCommand([VialCommands.MacroGetBufferSize]);
        return (response[1] << 8) | response[2];
    }

    /// <summary>
    /// Reads the full macro buffer as raw bytes via chunked reads.
    /// Same chunked protocol as GetKeymapBuffer: [0x0E, offset_hi, offset_lo, size].
    /// </summary>
    public byte[] GetMacroBuffer(int bufferSize)
    {
        // bufferSize comes from GetMacroBufferSize() (network-sourced).
        // Same OOM guard as GetKeymapBuffer / GetDefinition.
        if (bufferSize <= 0 || bufferSize > MaxMacroSize)
            throw new InvalidDataException(
                $"Firmware reported implausible macro buffer size {bufferSize} bytes " +
                $"(must be 1..{MaxMacroSize}).");
        var rawData = new byte[bufferSize];
        var offset = 0;

        while (offset < bufferSize)
        {
            var chunkSize = Math.Min(VialCommands.PayloadSize, bufferSize - offset);

            var cmd = new byte[VialCommands.ReportSize];
            cmd[0] = VialCommands.MacroGetBuffer;
            cmd[1] = (byte)((offset >> 8) & 0xFF);
            cmd[2] = (byte)(offset & 0xFF);
            cmd[3] = (byte)chunkSize;

            var response = SendCommand(cmd);
            Array.Copy(response, 4, rawData, offset, chunkSize);
            offset += chunkSize;
        }

        return rawData;
    }

    /// <summary>
    /// Writes a chunk of macro data to the device buffer.
    /// Command format: [0x0F, offset_hi, offset_lo, size, ...payload]
    /// </summary>
    public void MacroSetBuffer(int offset, byte[] data)
    {
        if (data.Length > VialCommands.PayloadSize)
            throw new ArgumentException(
                $"Chunk size {data.Length} exceeds maximum payload of {VialCommands.PayloadSize} bytes.",
                nameof(data));

        var cmd = new byte[VialCommands.ReportSize];
        cmd[0] = VialCommands.MacroSetBuffer;
        cmd[1] = (byte)((offset >> 8) & 0xFF);
        cmd[2] = (byte)(offset & 0xFF);
        cmd[3] = (byte)data.Length;
        Array.Copy(data, 0, cmd, 4, data.Length);

        SendCommand(cmd);
    }

    /// <summary>
    /// Resets all macros to firmware defaults.
    /// </summary>
    public void DynamicKeymapMacroReset()
    {
        SendCommand([VialCommands.DynamicKeymapMacroReset]);
    }

    // --- Dynamic entry commands (combos + tap-dance) ---

    /// <summary>
    /// Queries dynamic-entry counts. Returns all-zeros on unsupported firmware.
    /// </summary>
    public DynamicEntryCounts GetDynamicEntryCounts()
    {
        try
        {
            var response = SendCommand([
                VialCommands.VialPrefix,
                VialCommands.VialDynamicEntryOp,
                VialCommands.DynamicEntryGetNumberOfEntries
            ]);
            DiagnosticLog.Info("Proto",
                $"DynamicEntryCounts raw: [{response[0]:X2} {response[1]:X2} {response[2]:X2} {response[3]:X2} {response[4]:X2} {response[5]:X2} {response[6]:X2} {response[7]:X2}]");
            return new DynamicEntryCounts(response[0], response[1], response[2], response[3]);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("Proto", $"GetDynamicEntryCounts failed: {ex.Message}");
            return new DynamicEntryCounts(0, 0, 0, 0);
        }
    }

    /// <summary>
    /// Reads a combo entry. Byte 0 of the response is status; bytes 1..10 are the
    /// 10-byte entry payload.
    /// </summary>
    public byte[] GetComboEntry(int index) =>
        ReadDynamicEntry(VialCommands.DynamicEntryComboGet, index, Combo.EntryBytes);

    /// <summary>
    /// Writes a combo entry. Throws on non-zero status.
    /// </summary>
    public void SetComboEntry(int index, byte[] entry) =>
        WriteDynamicEntry(VialCommands.DynamicEntryComboSet, index, entry, Combo.EntryBytes);

    /// <summary>
    /// Reads a tap-dance entry.
    /// </summary>
    public byte[] GetTapDanceEntry(int index) =>
        ReadDynamicEntry(VialCommands.DynamicEntryTapDanceGet, index, TapDance.EntryBytes);

    /// <summary>
    /// Writes a tap-dance entry. Throws on non-zero status.
    /// </summary>
    public void SetTapDanceEntry(int index, byte[] entry) =>
        WriteDynamicEntry(VialCommands.DynamicEntryTapDanceSet, index, entry, TapDance.EntryBytes);

    private byte[] ReadDynamicEntry(byte subOp, int index, int entryBytes)
    {
        var response = SendCommand([
            VialCommands.VialPrefix,
            VialCommands.VialDynamicEntryOp,
            subOp,
            (byte)index
        ]);
        // vial-qmk quantum/vial.c: msg[0] is status, 10-byte payload at msg[1].
        var entry = new byte[entryBytes];
        Array.Copy(response, 1, entry, 0, entryBytes);
        return entry;
    }

    private void WriteDynamicEntry(byte subOp, int index, byte[] entry, int entryBytes)
    {
        if (entry.Length != entryBytes)
            throw new ArgumentException(
                $"Dynamic entry must be {entryBytes} bytes, got {entry.Length}.", nameof(entry));

        var cmd = new byte[VialCommands.ReportSize];
        cmd[0] = VialCommands.VialPrefix;
        cmd[1] = VialCommands.VialDynamicEntryOp;
        cmd[2] = subOp;
        cmd[3] = (byte)index;
        Array.Copy(entry, 0, cmd, 4, entryBytes);
        SendCommand(cmd);
    }

    // --- Unlock commands ---

    /// <summary>
    /// Queries the current Vial unlock status.
    /// Response: byte 0 = unlocked (1/0), byte 1 = in-progress (1/0),
    /// byte 2 onward = (row, col) pairs for keys to hold.
    /// </summary>
    public UnlockStatus GetUnlockStatus()
    {
        var response = SendCommand([VialCommands.VialPrefix, VialCommands.VialGetUnlockStatus]);

        var unlocked = response[0] != 0;
        var inProgress = response[1] != 0;

        var keys = new List<(int Row, int Col)>();
        // Keys start at byte 2, as (row, col) pairs — 0xFF terminates
        for (var i = 2; i + 1 < VialCommands.ReportSize; i += 2)
        {
            if (response[i] == 0xFF && response[i + 1] == 0xFF)
                break;
            // Skip zero-zero padding at the end
            if (response[i] == 0 && response[i + 1] == 0 && i > 2)
                break;
            keys.Add((response[i], response[i + 1]));
        }

        return new UnlockStatus(unlocked, inProgress, keys.ToArray());
    }

    /// <summary>
    /// Begins the Vial unlock sequence.
    /// </summary>
    public void UnlockStart()
    {
        SendCommand([VialCommands.VialPrefix, VialCommands.VialUnlockStart]);
    }

    /// <summary>
    /// Polls the unlock sequence progress.
    /// Returns true when the keyboard is fully unlocked.
    /// </summary>
    public bool UnlockPoll()
    {
        var response = SendCommand([VialCommands.VialPrefix, VialCommands.VialUnlockPoll]);
        return response[0] != 0;
    }

    /// <summary>
    /// Re-locks the keyboard after editing.
    /// </summary>
    public void Lock()
    {
        SendCommand([VialCommands.VialPrefix, VialCommands.VialLock]);
    }

    private static string HexDump(byte[] data, int count = 8) =>
        string.Join(" ", data.Take(count).Select(b => $"{b:X2}"));

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
        _disposed = true;
        _stream?.Close();
        _stream?.Dispose();
        _stream = null;
    }
}
