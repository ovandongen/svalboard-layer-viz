using SvalboardLayerViz.Core.Protocol;
using Xunit;

namespace SvalboardLayerViz.Tests.Protocol;

public class VialProtocolQmkSettingsTests
{
    // --- GetQmkSettingsList ---

    [Fact]
    public void GetQmkSettingsList_ReturnsDiscoveredIds()
    {
        var fake = new FakeVialProtocolService();
        fake.QmkSettingsStore[0x0007] = 200; // tapping term
        fake.QmkSettingsStore[0x0015] = 1;   // some boolean setting

        var entries = fake.GetQmkSettingsList();

        Assert.Equal(2, entries.Count);
        Assert.Contains((ushort)0x0007, entries);
        Assert.Contains((ushort)0x0015, entries);
    }

    [Fact]
    public void GetQmkSettingsList_EmptyWhenNoSettings()
    {
        var fake = new FakeVialProtocolService();

        var entries = fake.GetQmkSettingsList();

        Assert.Empty(entries);
    }

    [Fact]
    public void GetQmkSettingsList_ReturnsSortedIds()
    {
        var fake = new FakeVialProtocolService();
        fake.QmkSettingsStore[0x0020] = 0;
        fake.QmkSettingsStore[0x0007] = 200;
        fake.QmkSettingsStore[0x0010] = 1;

        var entries = fake.GetQmkSettingsList();

        Assert.Equal(new ushort[] { 0x0007, 0x0010, 0x0020 }, entries.ToArray());
    }

    // --- GetQmkSetting ---

    [Fact]
    public void GetQmkSetting_ReturnsValueFromStore()
    {
        var fake = new FakeVialProtocolService();
        fake.QmkSettingsStore[0x0007] = 250;

        var value = fake.GetQmkSetting(0x0007);

        Assert.Equal((ushort)250, value);
    }

    [Fact]
    public void GetQmkSetting_ReturnsNullForMissingSetting()
    {
        var fake = new FakeVialProtocolService();

        var value = fake.GetQmkSetting(0x0007);

        Assert.Null(value);
    }

    // --- SetQmkSetting ---

    [Fact]
    public void SetQmkSetting_RecordsCallAndUpdatesStore()
    {
        var fake = new FakeVialProtocolService();
        fake.QmkSettingsStore[0x0007] = 200;

        fake.SetQmkSetting(0x0007, 300);

        var call = Assert.Single(fake.SetQmkSettingCalls);
        Assert.Equal((ushort)0x0007, call.SettingId);
        Assert.Equal((ushort)300, call.Value);
        Assert.Equal((ushort)300, fake.QmkSettingsStore[0x0007]);
    }

    [Fact]
    public void SetQmkSetting_MultipleCalls_RecordsAll()
    {
        var fake = new FakeVialProtocolService();

        fake.SetQmkSetting(0x0007, 200);
        fake.SetQmkSetting(0x0015, 1);

        Assert.Equal(2, fake.SetQmkSettingCalls.Count);
        Assert.Equal((ushort)200, fake.SetQmkSettingCalls[0].Value);
        Assert.Equal((ushort)1, fake.SetQmkSettingCalls[1].Value);
    }

    // --- ResetQmkSettings ---

    [Fact]
    public void ResetQmkSettings_ClearsStoreAndIncrementsCounter()
    {
        var fake = new FakeVialProtocolService();
        fake.QmkSettingsStore[0x0007] = 200;
        fake.QmkSettingsStore[0x0015] = 1;

        fake.ResetQmkSettings();

        Assert.Empty(fake.QmkSettingsStore);
        Assert.Equal(1, fake.ResetQmkSettingsCount);
    }

    [Fact]
    public void ResetQmkSettings_MultipleCalls_IncrementCounter()
    {
        var fake = new FakeVialProtocolService();

        fake.ResetQmkSettings();
        fake.ResetQmkSettings();

        Assert.Equal(2, fake.ResetQmkSettingsCount);
    }

    // --- Integration: settings discovery populates correctly ---

    [Fact]
    public void SettingsRoundTrip_DiscoverThenRead()
    {
        var fake = new FakeVialProtocolService();
        fake.QmkSettingsStore[VialCommands.QmkSettingTappingTerm] = 175;
        fake.QmkSettingsStore[0x0015] = 1;

        // Discover
        var entries = fake.GetQmkSettingsList();
        Assert.Equal(2, entries.Count);

        // Read each
        var values = new Dictionary<ushort, ushort>();
        foreach (var id in entries)
        {
            var val = fake.GetQmkSetting(id);
            Assert.NotNull(val);
            values[id] = val.Value;
        }

        Assert.Equal((ushort)175, values[VialCommands.QmkSettingTappingTerm]);
        Assert.Equal((ushort)1, values[0x0015]);
    }

    [Fact]
    public void SettingsRoundTrip_SetThenRead()
    {
        var fake = new FakeVialProtocolService();
        fake.QmkSettingsStore[VialCommands.QmkSettingTappingTerm] = 200;

        fake.SetQmkSetting(VialCommands.QmkSettingTappingTerm, 150);

        var value = fake.GetQmkSetting(VialCommands.QmkSettingTappingTerm);
        Assert.Equal((ushort)150, value);
    }
}
