using SvalboardLayerViz.Core.Protocol;
using SvalboardLayerViz.Core.QmkSettings;
using Xunit;

namespace SvalboardLayerViz.Tests.QmkSettings;

public class QmkSettingsCatalogTests
{
    [Fact]
    public void TappingTerm_IsInCatalog()
    {
        Assert.True(QmkSettingsCatalog.ById.ContainsKey(VialCommands.QmkSettingTappingTerm));
        var desc = QmkSettingsCatalog.ById[VialCommands.QmkSettingTappingTerm];
        Assert.Equal("QmkSetting_TappingTerm", desc.NameKey);
        Assert.Equal("TapHold", desc.GroupKey);
        Assert.Equal(QmkSettingType.UInt16, desc.Type);
    }

    [Fact]
    public void ScalarIds_AreUnique_BitfieldIds_ShareQsid()
    {
        // Scalar (non-bitfield) entries must have unique IDs.
        // Bitfield entries share the same QSID but must have unique (Id, BitIndex) pairs.
        var scalarIds = new HashSet<ushort>();
        var bitfieldKeys = new HashSet<(ushort, int)>();
        foreach (var (_, entries) in QmkSettingsCatalog.AllGroups)
            foreach (var e in entries)
            {
                if (e.IsBitfield)
                    Assert.True(bitfieldKeys.Add((e.Id, e.BitIndex)),
                        $"Duplicate bitfield entry: 0x{e.Id:X4} bit {e.BitIndex}");
                else
                    Assert.True(scalarIds.Add(e.Id), $"Duplicate scalar setting ID: 0x{e.Id:X4}");
            }
    }

    [Fact]
    public void GetOrFallback_KnownId_ReturnsDescriptor()
    {
        var desc = QmkSettingsCatalog.GetOrFallback(VialCommands.QmkSettingTappingTerm);
        Assert.Equal(VialCommands.QmkSettingTappingTerm, desc.Id);
        Assert.Equal("QmkSetting_TappingTerm", desc.NameKey);
    }

    [Fact]
    public void GetOrFallback_UnknownId_ReturnsFallback()
    {
        var desc = QmkSettingsCatalog.GetOrFallback(0xFFFF);
        Assert.Equal((ushort)0xFFFF, desc.Id);
        Assert.Contains("QSID 0xFFFF", desc.NameKey);
        Assert.Equal("Misc", desc.GroupKey);
    }

    [Fact]
    public void FallbackDescriptor_HasUInt16Type_FullRange()
    {
        var desc = QmkSettingsCatalog.GetOrFallback(0x9999);
        Assert.Equal(QmkSettingType.UInt16, desc.Type);
        Assert.Equal((ushort)0, desc.Min);
        Assert.Equal((ushort)65535, desc.Max);
    }

    [Fact]
    public void AllGroups_ContainsExpectedGroupNames()
    {
        var names = QmkSettingsCatalog.AllGroups.Select(g => g.GroupName).ToList();
        Assert.Contains("Magic", names);
        Assert.Contains("Grave Escape", names);
        Assert.Contains("Tap/Hold", names);
        Assert.Contains("One-shot", names);
        Assert.Contains("Combos", names);
        Assert.Contains("Auto-shift", names);
        Assert.Contains("Mouse Keys", names);
        Assert.Contains("Misc", names);
    }

    [Fact]
    public void ById_ContainsFirstEntryForEachQsid()
    {
        foreach (var (_, entries) in QmkSettingsCatalog.AllGroups)
            foreach (var e in entries)
                Assert.True(QmkSettingsCatalog.ById.ContainsKey(e.Id),
                    $"QSID 0x{e.Id:X4} not in ById");
    }

    [Fact]
    public void GetAllForId_ReturnsBitfieldEntries()
    {
        var magicEntries = QmkSettingsCatalog.GetAllForId(VialCommands.QmkSettingMagic);
        Assert.NotNull(magicEntries);
        Assert.Equal(10, magicEntries.Count);
        Assert.All(magicEntries, e => Assert.True(e.IsBitfield));
        // Each bit index should be unique
        var bitIndices = magicEntries.Select(e => e.BitIndex).ToHashSet();
        Assert.Equal(10, bitIndices.Count);
    }

    [Fact]
    public void GetAllForId_ScalarSetting_ReturnsSingleEntry()
    {
        var entries = QmkSettingsCatalog.GetAllForId(VialCommands.QmkSettingTappingTerm);
        Assert.NotNull(entries);
        Assert.Single(entries);
        Assert.False(entries[0].IsBitfield);
    }

    [Fact]
    public void GetAllForId_UnknownId_ReturnsNull()
    {
        Assert.Null(QmkSettingsCatalog.GetAllForId(0xFFFF));
    }
}
