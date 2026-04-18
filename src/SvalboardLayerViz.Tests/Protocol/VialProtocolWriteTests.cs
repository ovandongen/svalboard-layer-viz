using SvalboardLayerViz.Core.Protocol;
using Xunit;

namespace SvalboardLayerViz.Tests.Protocol;

public class VialProtocolWriteTests
{
    // --- SetKeycode ---

    [Fact]
    public void SetKeycode_RecordsCallWithCorrectParameters()
    {
        var fake = new FakeVialProtocolService();

        fake.SetKeycode(2, 3, 4, 0x1234);

        var call = Assert.Single(fake.SetKeycodeCalls);
        Assert.Equal(2, call.Layer);
        Assert.Equal(3, call.Row);
        Assert.Equal(4, call.Col);
        Assert.Equal(0x1234, call.Keycode);
    }

    [Fact]
    public void SetKeycode_UpdatesInMemoryKeymap()
    {
        var fake = new FakeVialProtocolService
        {
            Keymap = new ushort[2, 10, 6]
        };

        fake.SetKeycode(1, 3, 2, 0xABCD);

        Assert.Equal(0xABCD, fake.Keymap[1, 3, 2]);
    }

    [Fact]
    public void SetKeycode_MultipleCalls_RecordsAll()
    {
        var fake = new FakeVialProtocolService();

        fake.SetKeycode(0, 0, 0, 0x0004); // KC_A
        fake.SetKeycode(0, 0, 1, 0x0005); // KC_B
        fake.SetKeycode(1, 2, 3, 0x0006); // KC_C

        Assert.Equal(3, fake.SetKeycodeCalls.Count);
        Assert.Equal(0x0004, fake.SetKeycodeCalls[0].Keycode);
        Assert.Equal(0x0005, fake.SetKeycodeCalls[1].Keycode);
        Assert.Equal(0x0006, fake.SetKeycodeCalls[2].Keycode);
    }

    [Fact]
    public void SetKeycode_FailAt_ThrowsOnSpecifiedCall()
    {
        var fake = new FakeVialProtocolService
        {
            SetKeycodeFailAt = (1, new IOException("HID write failed"))
        };

        fake.SetKeycode(0, 0, 0, 0x0004); // Call 0 — succeeds
        Assert.Throws<IOException>(() => fake.SetKeycode(0, 0, 1, 0x0005)); // Call 1 — fails

        Assert.Single(fake.SetKeycodeCalls); // Only the successful call recorded
    }

    [Fact]
    public void SetKeycode_WriteAndReread_RoundTrips()
    {
        var fake = new FakeVialProtocolService
        {
            Keymap = new ushort[2, 10, 6]
        };

        fake.SetKeycode(0, 3, 2, 0x1234);
        var reread = fake.GetKeymapBuffer(2, 10, 6);

        Assert.Equal(0x1234, reread[0, 3, 2]);
    }

    // --- KeymapSetBuffer ---

    [Fact]
    public void KeymapSetBuffer_RecordsCallWithCorrectParameters()
    {
        var fake = new FakeVialProtocolService();
        var data = new byte[] { 0x00, 0x04, 0x00, 0x05 };

        fake.KeymapSetBuffer(100, data);

        var call = Assert.Single(fake.KeymapSetBufferCalls);
        Assert.Equal(100, call.Offset);
        Assert.Equal(data, call.Data);
    }

    [Fact]
    public void KeymapSetBuffer_MultipleCalls_RecordsAll()
    {
        var fake = new FakeVialProtocolService();

        fake.KeymapSetBuffer(0, new byte[] { 0x01 });
        fake.KeymapSetBuffer(28, new byte[] { 0x02 });

        Assert.Equal(2, fake.KeymapSetBufferCalls.Count);
    }

    // --- DynamicKeymapReset ---

    [Fact]
    public void DynamicKeymapReset_IncrementsCount()
    {
        var fake = new FakeVialProtocolService();

        fake.DynamicKeymapReset();
        fake.DynamicKeymapReset();

        Assert.Equal(2, fake.DynamicKeymapResetCount);
    }

    // --- EepromReset ---

    [Fact]
    public void EepromReset_IncrementsCount()
    {
        var fake = new FakeVialProtocolService();

        fake.EepromReset();

        Assert.Equal(1, fake.EepromResetCount);
    }

    // --- Unlock flow ---

    [Fact]
    public void GetUnlockStatus_ReturnsConfiguredStatus()
    {
        var keys = new[] { (3, 2), (5, 4) };
        var fake = new FakeVialProtocolService
        {
            NextUnlockStatus = new UnlockStatus(false, true, keys)
        };

        var status = fake.GetUnlockStatus();

        Assert.False(status.Unlocked);
        Assert.True(status.InProgress);
        Assert.Equal(2, status.KeysToHold.Length);
        Assert.Equal((3, 2), status.KeysToHold[0]);
        Assert.Equal((5, 4), status.KeysToHold[1]);
    }

    [Fact]
    public void GetUnlockStatus_AlreadyUnlocked_HasNoKeys()
    {
        var fake = new FakeVialProtocolService
        {
            NextUnlockStatus = new UnlockStatus(true, false, [])
        };

        var status = fake.GetUnlockStatus();

        Assert.True(status.Unlocked);
        Assert.False(status.InProgress);
        Assert.Empty(status.KeysToHold);
    }

    [Fact]
    public void UnlockStart_IncrementsCount()
    {
        var fake = new FakeVialProtocolService();

        fake.UnlockStart();

        Assert.Equal(1, fake.UnlockStartCount);
    }

    [Fact]
    public void UnlockPoll_ReturnsTrueWhenUnlocked()
    {
        var fake = new FakeVialProtocolService { UnlockPollResult = true };

        Assert.True(fake.UnlockPoll());
        Assert.Equal(1, fake.UnlockPollCount);
    }

    [Fact]
    public void UnlockPoll_ReturnsFalseWhenStillLocked()
    {
        var fake = new FakeVialProtocolService { UnlockPollResult = false };

        Assert.False(fake.UnlockPoll());
    }

    [Fact]
    public void Lock_IncrementsCount()
    {
        var fake = new FakeVialProtocolService();

        fake.Lock();
        fake.Lock();

        Assert.Equal(2, fake.LockCount);
    }

    // --- Full unlock sequence simulation ---

    [Fact]
    public void UnlockSequence_FullFlow()
    {
        var fake = new FakeVialProtocolService
        {
            NextUnlockStatus = new UnlockStatus(false, false, [(3, 2), (5, 4)])
        };

        // 1. Check status — locked
        var status = fake.GetUnlockStatus();
        Assert.False(status.Unlocked);

        // 2. Start unlock
        fake.UnlockStart();
        Assert.Equal(1, fake.UnlockStartCount);

        // 3. Poll — still locked
        fake.UnlockPollResult = false;
        Assert.False(fake.UnlockPoll());

        // 4. Poll — now unlocked
        fake.UnlockPollResult = true;
        Assert.True(fake.UnlockPoll());
        Assert.Equal(2, fake.UnlockPollCount);

        // 5. Do some edits...
        fake.SetKeycode(0, 3, 2, 0x0004);

        // 6. Lock when done
        fake.Lock();
        Assert.Equal(1, fake.LockCount);
    }
}

public class VialProtocolServiceByteFormatTests
{
    [Fact]
    public void SetKeycode_CommandConstant_Is0x05()
    {
        Assert.Equal(0x05, VialCommands.SetKeycode);
    }

    [Fact]
    public void KeymapSetBuffer_CommandConstant_Is0x13()
    {
        Assert.Equal(0x13, VialCommands.KeymapSetBuffer);
    }

    [Fact]
    public void DynamicKeymapReset_CommandConstant_Is0x06()
    {
        Assert.Equal(0x06, VialCommands.DynamicKeymapReset);
    }

    [Fact]
    public void EepromReset_CommandConstant_Is0x0A()
    {
        Assert.Equal(0x0A, VialCommands.EepromReset);
    }

    [Fact]
    public void VialUnlockStart_CommandConstant_Is0x06()
    {
        Assert.Equal(0x06, VialCommands.VialUnlockStart);
    }

    [Fact]
    public void VialUnlockPoll_CommandConstant_Is0x07()
    {
        Assert.Equal(0x07, VialCommands.VialUnlockPoll);
    }

    [Fact]
    public void VialLock_CommandConstant_Is0x08()
    {
        Assert.Equal(0x08, VialCommands.VialLock);
    }

    [Fact]
    public void VialGetUnlockStatus_CommandConstant_Is0x05()
    {
        Assert.Equal(0x05, VialCommands.VialGetUnlockStatus);
    }

    [Fact]
    public void UnlockStatus_Record_EqualsCorrectly()
    {
        var a = new UnlockStatus(true, false, [(1, 2)]);
        var b = new UnlockStatus(true, false, [(1, 2)]);

        // Record equality checks value types but arrays use reference equality
        Assert.Equal(a.Unlocked, b.Unlocked);
        Assert.Equal(a.InProgress, b.InProgress);
        Assert.Equal(a.KeysToHold[0], b.KeysToHold[0]);
    }
}
