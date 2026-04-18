using SvalboardLayerViz.Core.Protocol;
using Xunit;

namespace SvalboardLayerViz.Tests.Protocol;

/// <summary>
/// Tests parser correctness by subclassing VialProtocolService and overriding
/// SendCommand with canned responses — no real HID stream required.
/// Covers: big-endian keymap parse, matrix bit-unpack, all-zero QMK heuristic,
/// XZ magic offset detection, settings-list terminator, multi-chunk paging.
/// </summary>
public class VialProtocolServiceParserTests
{
    private class TestableProtocol : VialProtocolService
    {
        public Queue<byte[]> Responses { get; } = new();
        public List<byte[]> SentCommands { get; } = new();

        protected override byte[] SendCommand(byte[] command)
        {
            SentCommands.Add((byte[])command.Clone());
            if (Responses.Count == 0)
                throw new InvalidOperationException(
                    $"No canned response for command: {BitConverter.ToString(command[..Math.Min(8, command.Length)])}");
            return Responses.Dequeue();
        }

        public void EnqueueResponse(byte[] payload)
        {
            var full = new byte[VialCommands.ReportSize];
            Array.Copy(payload, 0, full, 0, Math.Min(payload.Length, full.Length));
            Responses.Enqueue(full);
        }
    }

    // --- GetKeymapBuffer: BE u16 parse + chunked offsets ---

    [Fact]
    public void GetKeymapBuffer_ParsesBigEndianKeycodes()
    {
        var svc = new TestableProtocol();
        // layers=1, rows=1, cols=2 → 4 bytes → single chunk.
        // BE: 0x0004 → [0x00, 0x04], 0x1234 → [0x12, 0x34]
        var chunk = new byte[VialCommands.ReportSize];
        chunk[4] = 0x00; chunk[5] = 0x04;
        chunk[6] = 0x12; chunk[7] = 0x34;
        svc.Responses.Enqueue(chunk);

        var keymap = svc.GetKeymapBuffer(1, 1, 2);

        Assert.Equal((ushort)0x0004, keymap[0, 0, 0]);
        Assert.Equal((ushort)0x1234, keymap[0, 0, 1]);
    }

    [Fact]
    public void GetKeymapBuffer_SplitsAcrossMultipleChunks()
    {
        var svc = new TestableProtocol();
        // Need > PayloadSize (28) bytes → forces two chunks.
        // layers=1, rows=10, cols=6 → 120 bytes → 5 chunks of 28, 28, 28, 28, 8.
        for (var chunk = 0; chunk < 5; chunk++)
        {
            var resp = new byte[VialCommands.ReportSize];
            // Fill payload area with a distinct marker per chunk so we can
            // verify chunk ordering.
            for (var i = 0; i < VialCommands.PayloadSize; i++)
                resp[4 + i] = (byte)(chunk * VialCommands.PayloadSize + i);
            svc.Responses.Enqueue(resp);
        }

        var keymap = svc.GetKeymapBuffer(1, 10, 6);

        // Byte 0 of chunk 0 → keymap[0,0,0] high byte = 0, low byte = 1 → 0x0001.
        Assert.Equal((ushort)0x0001, keymap[0, 0, 0]);
        // Verify chunk boundary — byte 28 (first byte of chunk 1) → keymap[0,2,2] high.
        // idx = (2*6 + 2) * 2 = 28 → high=28, low=29 → 0x1C1D
        Assert.Equal((ushort)0x1C1D, keymap[0, 2, 2]);
    }

    [Fact]
    public void GetKeymapBuffer_EncodesOffsetAsBigEndian()
    {
        var svc = new TestableProtocol();
        // 1 layer × 10 rows × 6 cols = 120 bytes → chunk 2 starts at offset 56.
        for (var i = 0; i < 5; i++)
            svc.Responses.Enqueue(new byte[VialCommands.ReportSize]);

        svc.GetKeymapBuffer(1, 10, 6);

        // Third command: offset 56 → BE → [0x00, 0x38]
        var cmd = svc.SentCommands[2];
        Assert.Equal(VialCommands.KeymapGetBuffer, cmd[0]);
        Assert.Equal(0x00, cmd[1]);
        Assert.Equal(0x38, cmd[2]);
    }

    // --- GetSwitchMatrixState: bit-packed row parsing ---

    [Fact]
    public void GetSwitchMatrixState_UnpacksColumnBits()
    {
        var svc = new TestableProtocol();
        var resp = new byte[VialCommands.ReportSize];
        // Matrix data starts at byte 2. Row 0 = 0b00000101 → cols 0 and 2 pressed.
        resp[2] = 0b00000101;
        resp[3] = 0b00000010; // Row 1 → col 1 pressed.
        svc.Responses.Enqueue(resp);

        var state = svc.GetSwitchMatrixState(2, 6);

        Assert.True(state[0, 0]);
        Assert.False(state[0, 1]);
        Assert.True(state[0, 2]);
        Assert.False(state[1, 0]);
        Assert.True(state[1, 1]);
    }

    [Fact]
    public void GetSwitchMatrixState_AllZero_NoKeysPressed()
    {
        var svc = new TestableProtocol();
        svc.Responses.Enqueue(new byte[VialCommands.ReportSize]);

        var state = svc.GetSwitchMatrixState(10, 6);

        for (var r = 0; r < 10; r++)
            for (var c = 0; c < 6; c++)
                Assert.False(state[r, c]);
    }

    [Fact]
    public void GetSwitchMatrixState_RowsOverflowReport_Throws()
    {
        // Bytes 2..31 = 30 rows max. Asking for 40 rows would silently truncate
        // in the old code, making phantom "not pressed" keys. Now it throws.
        var svc = new TestableProtocol();
        svc.Responses.Enqueue(new byte[VialCommands.ReportSize]);

        Assert.Throws<InvalidOperationException>(() => svc.GetSwitchMatrixState(40, 6));
    }

    [Fact]
    public void GetSwitchMatrixState_ColsOverflowByte_Throws()
    {
        var svc = new TestableProtocol();
        svc.Responses.Enqueue(new byte[VialCommands.ReportSize]);

        Assert.Throws<InvalidOperationException>(() => svc.GetSwitchMatrixState(6, 9));
    }

    // --- GetQmkSetting: all-zero heuristic + width handling ---

    [Fact]
    public void GetQmkSetting_AllZeroResponse_ReturnsNull()
    {
        var svc = new TestableProtocol();
        svc.Responses.Enqueue(new byte[VialCommands.ReportSize]);

        var result = svc.GetQmkSetting(0x1234);

        Assert.Null(result);
    }

    [Fact]
    public void GetQmkSetting_ParsesU16LittleEndian()
    {
        var svc = new TestableProtocol();
        var resp = new byte[VialCommands.ReportSize];
        resp[0] = 0x00;          // status OK
        resp[1] = 0x34; resp[2] = 0x12; // value 0x1234 LE
        svc.Responses.Enqueue(resp);

        var result = svc.GetQmkSetting(0x0010);

        Assert.Equal((ushort)0x1234, result);
    }

    [Fact]
    public void GetQmkSetting_Width1_OnlyReadsOneByte()
    {
        var svc = new TestableProtocol();
        var resp = new byte[VialCommands.ReportSize];
        resp[0] = 0x00;
        resp[1] = 0x7F;
        resp[2] = 0xFF; // should be ignored when width=1
        svc.Responses.Enqueue(resp);

        var result = svc.GetQmkSetting(0x0010, width: 1);

        Assert.Equal((ushort)0x7F, result);
    }

    // --- GetQmkSettingsList: 0xFFFF terminator + paging ---

    [Fact]
    public void GetQmkSettingsList_StopsAtFfffTerminator()
    {
        var svc = new TestableProtocol();
        var resp = new byte[VialCommands.ReportSize];
        // Two IDs followed by 0xFFFF terminator.
        resp[0] = 0x10; resp[1] = 0x00; // 0x0010
        resp[2] = 0x20; resp[3] = 0x00; // 0x0020
        resp[4] = 0xFF; resp[5] = 0xFF; // terminator
        svc.Responses.Enqueue(resp);

        var list = svc.GetQmkSettingsList();

        Assert.Equal(2, list.Count);
        Assert.Equal((ushort)0x0010, list[0]);
        Assert.Equal((ushort)0x0020, list[1]);
    }

    [Fact]
    public void GetQmkSettingsList_PaginatesAcrossResponses()
    {
        var svc = new TestableProtocol();
        // First page: 16 IDs, no terminator → triggers second page request.
        var page1 = new byte[VialCommands.ReportSize];
        for (var i = 0; i < 16; i++)
        {
            page1[i * 2] = (byte)(i + 1);
            page1[i * 2 + 1] = 0x00;
        }
        svc.Responses.Enqueue(page1);

        // Second page: one ID + terminator.
        var page2 = new byte[VialCommands.ReportSize];
        page2[0] = 0x20; page2[1] = 0x00;
        page2[2] = 0xFF; page2[3] = 0xFF;
        svc.Responses.Enqueue(page2);

        var list = svc.GetQmkSettingsList();

        Assert.Equal(17, list.Count);
        Assert.Equal((ushort)0x0020, list[^1]);
        // Second request should be sent with offset = last id of first page = 16.
        var secondCmd = svc.SentCommands[1];
        Assert.Equal(VialCommands.VialPrefix, secondCmd[0]);
        Assert.Equal(VialCommands.VialQmkSettingsQuery, secondCmd[1]);
        Assert.Equal(0x10, secondCmd[2]); // offset_lo
        Assert.Equal(0x00, secondCmd[3]); // offset_hi
    }

    [Fact]
    public void GetQmkSettingsList_AllZeroFirstPage_ReturnsEmpty()
    {
        var svc = new TestableProtocol();
        // All-zero means no entries; 0x0000 is a valid ID so loop would recurse
        // forever without the `foundAny || result.Count == 0` guard.
        var resp = new byte[VialCommands.ReportSize];
        resp[0] = 0xFF; resp[1] = 0xFF; // immediate terminator
        svc.Responses.Enqueue(resp);

        var list = svc.GetQmkSettingsList();

        Assert.Empty(list);
    }

    // --- GetSvalProtoVersion: ASCII handshake detection ---

    [Fact]
    public void GetSvalProtoVersion_ReturnsVersionWhenHandshakeMatches()
    {
        var svc = new TestableProtocol();
        var resp = new byte[VialCommands.ReportSize];
        resp[0] = (byte)'s'; resp[1] = (byte)'v';
        resp[2] = (byte)'a'; resp[3] = (byte)'l';
        resp[4] = 0x02; resp[5] = 0x00; resp[6] = 0x00; resp[7] = 0x00; // version 2 LE
        svc.Responses.Enqueue(resp);

        var version = svc.GetSvalProtoVersion();

        Assert.Equal((uint)2, version);
    }

    [Fact]
    public void GetSvalProtoVersion_ReturnsNullOnMismatch()
    {
        var svc = new TestableProtocol();
        svc.Responses.Enqueue(new byte[VialCommands.ReportSize]);

        Assert.Null(svc.GetSvalProtoVersion());
    }

    // --- GetLayerColor: all-zero = unsupported ---

    [Fact]
    public void GetLayerColor_AllZeroResponse_ReturnsNull()
    {
        var svc = new TestableProtocol();
        svc.Responses.Enqueue(new byte[VialCommands.ReportSize]);

        Assert.Null(svc.GetLayerColor(0));
    }

    [Fact]
    public void GetLayerColor_ReturnsHsvTuple()
    {
        var svc = new TestableProtocol();
        var resp = new byte[VialCommands.ReportSize];
        resp[0] = 170; resp[1] = 200; resp[2] = 255;
        svc.Responses.Enqueue(resp);

        var color = svc.GetLayerColor(1);

        Assert.NotNull(color);
        Assert.Equal(170, color!.Value.H);
        Assert.Equal(200, color.Value.S);
        Assert.Equal(255, color.Value.V);
    }

    // --- GetCurrentLedHueSat: echo validation ---

    [Fact]
    public void GetCurrentLedHueSat_MissingEcho_ReturnsNull()
    {
        var svc = new TestableProtocol();
        var resp = new byte[VialCommands.ReportSize];
        // Bytes 0-1 != [0x08, 0x83] → firmware doesn't support this.
        resp[0] = 0x00; resp[1] = 0x00;
        svc.Responses.Enqueue(resp);

        Assert.Null(svc.GetCurrentLedHueSat());
    }

    [Fact]
    public void GetCurrentLedHueSat_ParsesEchoedResponse()
    {
        var svc = new TestableProtocol();
        var resp = new byte[VialCommands.ReportSize];
        resp[0] = VialCommands.LightingGetValue;
        resp[1] = VialCommands.QmkRgblightColor;
        resp[2] = 120; resp[3] = 200;
        svc.Responses.Enqueue(resp);

        var result = svc.GetCurrentLedHueSat();

        Assert.NotNull(result);
        Assert.Equal(120, result!.Value.H);
        Assert.Equal(200, result.Value.S);
    }

    // --- SetQmkSetting: non-zero status throws ---

    [Fact]
    public void SetQmkSetting_NonZeroStatus_Throws()
    {
        var svc = new TestableProtocol();
        var resp = new byte[VialCommands.ReportSize];
        resp[0] = 0x01; // non-zero = error
        svc.Responses.Enqueue(resp);

        Assert.Throws<InvalidOperationException>(() => svc.SetQmkSetting(0x1234, 0x0001));
    }

    [Fact]
    public void SetQmkSetting_Width1_SendsFiveBytePayload()
    {
        var svc = new TestableProtocol();
        svc.Responses.Enqueue(new byte[VialCommands.ReportSize]);

        svc.SetQmkSetting(0x00AB, 0x42, width: 1);

        var cmd = svc.SentCommands[0];
        Assert.Equal(VialCommands.VialPrefix, cmd[0]);
        Assert.Equal(VialCommands.VialQmkSettingsSet, cmd[1]);
        Assert.Equal(0xAB, cmd[2]);
        Assert.Equal(0x00, cmd[3]);
        Assert.Equal(0x42, cmd[4]);
    }

    // --- Command formatting: SetKeycode byte order ---

    [Fact]
    public void SetKeycode_SendsKeycodeAsBigEndian()
    {
        var svc = new TestableProtocol();
        svc.Responses.Enqueue(new byte[VialCommands.ReportSize]);

        svc.SetKeycode(layer: 1, row: 2, col: 3, keycode: 0xABCD);

        var cmd = svc.SentCommands[0];
        Assert.Equal(VialCommands.SetKeycode, cmd[0]);
        Assert.Equal(1, cmd[1]);
        Assert.Equal(2, cmd[2]);
        Assert.Equal(3, cmd[3]);
        Assert.Equal(0xAB, cmd[4]); // high byte
        Assert.Equal(0xCD, cmd[5]); // low byte
    }

    [Fact]
    public void KeymapSetBuffer_OversizedChunk_Throws()
    {
        var svc = new TestableProtocol();
        var tooBig = new byte[VialCommands.PayloadSize + 1];

        Assert.Throws<ArgumentException>(() => svc.KeymapSetBuffer(0, tooBig));
    }

    // --- ExtractResponse: HID short-read bounds (tier 2.1) ---

    [Fact]
    public void ExtractResponse_FullReadWithoutReportId_ReturnsBufferAsIs()
    {
        var buffer = new byte[VialCommands.ReportSize];
        for (var i = 0; i < buffer.Length; i++) buffer[i] = (byte)(i + 1);

        var response = VialProtocolService.ExtractResponse(buffer, VialCommands.ReportSize);

        Assert.Equal(VialCommands.ReportSize, response.Length);
        Assert.Equal(1, response[0]);
        Assert.Equal(VialCommands.ReportSize, response[VialCommands.ReportSize - 1]);
    }

    [Fact]
    public void ExtractResponse_FullReadWithReportId_StripsLeadingByte()
    {
        var buffer = new byte[VialCommands.ReportSize + 1];
        buffer[0] = 0xAA; // simulated report ID
        for (var i = 1; i < buffer.Length; i++) buffer[i] = (byte)i;

        var response = VialProtocolService.ExtractResponse(buffer, VialCommands.ReportSize + 1);

        Assert.Equal(VialCommands.ReportSize, response.Length);
        Assert.Equal(1, response[0]);
        Assert.NotEqual(0xAA, response[0]);
    }

    [Fact]
    public void ExtractResponse_ShortRead_ThrowsIOException()
    {
        var buffer = new byte[VialCommands.ReportSize];

        var ex = Assert.Throws<IOException>(
            () => VialProtocolService.ExtractResponse(buffer, 16));

        Assert.Contains("Short HID read", ex.Message);
        Assert.Contains("16", ex.Message);
    }

    // --- GetDefinition: sanity-cap totalSize (tier 4.1) ---

    [Fact]
    public void GetDefinition_NegativeSize_Throws()
    {
        var svc = new TestableProtocol();
        // GetDefinitionSize reads BitConverter.ToInt32(response, 0).
        // int.MinValue LE = 0x00 0x00 0x00 0x80.
        var resp = new byte[VialCommands.ReportSize];
        resp[3] = 0x80;
        svc.Responses.Enqueue(resp);

        Assert.Throws<InvalidDataException>(() => svc.GetDefinition());
    }

    [Fact]
    public void GetDefinition_ZeroSize_Throws()
    {
        var svc = new TestableProtocol();
        svc.Responses.Enqueue(new byte[VialCommands.ReportSize]); // all zero → size=0

        Assert.Throws<InvalidDataException>(() => svc.GetDefinition());
    }

    [Fact]
    public void GetDefinition_OversizedSize_Throws()
    {
        var svc = new TestableProtocol();
        // 100_000 bytes > 64 KB cap. LE: 0xA0 0x86 0x01 0x00.
        var resp = new byte[VialCommands.ReportSize];
        resp[0] = 0xA0; resp[1] = 0x86; resp[2] = 0x01; resp[3] = 0x00;
        svc.Responses.Enqueue(resp);

        Assert.Throws<InvalidDataException>(() => svc.GetDefinition());
    }

    // --- GetKeymapBuffer: sanity-cap dimensions (session A.3) ---

    [Fact]
    public void GetKeymapBuffer_NegativeDimension_Throws()
    {
        var svc = new TestableProtocol();
        Assert.Throws<InvalidDataException>(() => svc.GetKeymapBuffer(-1, 6, 10));
        Assert.Throws<InvalidDataException>(() => svc.GetKeymapBuffer(4, 0, 10));
        Assert.Throws<InvalidDataException>(() => svc.GetKeymapBuffer(4, 6, 0));
    }

    [Fact]
    public void GetKeymapBuffer_ZeroLayers_Throws()
    {
        var svc = new TestableProtocol();
        Assert.Throws<InvalidDataException>(() => svc.GetKeymapBuffer(0, 6, 10));
    }

    [Fact]
    public void GetKeymapBuffer_OversizedDimensions_Throws()
    {
        var svc = new TestableProtocol();
        // 100000 × 10 × 6 × 2 = 12 GB — well over the 64 KB cap. Also
        // verifies the long-math prevents int overflow hiding the blow-up.
        Assert.Throws<InvalidDataException>(() => svc.GetKeymapBuffer(100_000, 10, 6));
    }

    // --- GetMacroBuffer: sanity-cap bufferSize (session A.3) ---

    [Fact]
    public void GetMacroBuffer_NegativeSize_Throws()
    {
        var svc = new TestableProtocol();
        Assert.Throws<InvalidDataException>(() => svc.GetMacroBuffer(-1));
    }

    [Fact]
    public void GetMacroBuffer_ZeroSize_Throws()
    {
        var svc = new TestableProtocol();
        Assert.Throws<InvalidDataException>(() => svc.GetMacroBuffer(0));
    }

    [Fact]
    public void GetMacroBuffer_OversizedSize_Throws()
    {
        var svc = new TestableProtocol();
        Assert.Throws<InvalidDataException>(() => svc.GetMacroBuffer(100_000));
    }
}
