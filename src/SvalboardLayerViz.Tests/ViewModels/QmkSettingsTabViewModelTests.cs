using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Models;
using SvalboardLayerViz.Core.Protocol;
using SvalboardLayerViz.Core.QmkSettings;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

public class QmkSettingsTabViewModelTests
{
    private static QmkSettingValue TappingTerm(ushort value = 200) =>
        new(VialCommands.QmkSettingTappingTerm, value);

    // --- Construction ---

    [Fact]
    public void EmptySettings_HasSettingsFalse_GroupsEmpty()
    {
        var vm = new QmkSettingsTabViewModel([], isEditMode: false, onSettingChanged: null);

        Assert.False(vm.HasSettings);
        Assert.Empty(vm.Groups);
        Assert.Empty(vm.Settings);
    }

    [Fact]
    public void TappingTerm_CreatesOneTapHoldGroup()
    {
        var vm = new QmkSettingsTabViewModel([TappingTerm()], isEditMode: false, onSettingChanged: null);

        Assert.True(vm.HasSettings);
        Assert.Single(vm.Groups);
        Assert.Single(vm.Settings);

        var group = vm.Groups[0];
        Assert.Single(group.Items);
        Assert.Equal(200, group.Items[0].Value);
    }

    [Fact]
    public void OnlyNonEmptyGroupsRendered()
    {
        // Only tapping term exists — should only see TapHold group, not OneShot/Combos/etc.
        var vm = new QmkSettingsTabViewModel([TappingTerm()], isEditMode: false, onSettingChanged: null);

        Assert.Single(vm.Groups);
    }

    // --- Edit mode ---

    [Fact]
    public void ReadOnly_WhenNotInEditMode()
    {
        var vm = new QmkSettingsTabViewModel([TappingTerm()], isEditMode: false, onSettingChanged: null);

        Assert.False(vm.IsEditMode);
        Assert.True(vm.Settings[0].IsReadOnly);
    }

    [Fact]
    public void Editable_WhenInEditMode()
    {
        var vm = new QmkSettingsTabViewModel(
            [TappingTerm()], isEditMode: true, onSettingChanged: (_, _) => { });

        Assert.True(vm.IsEditMode);
        Assert.False(vm.Settings[0].IsReadOnly);
    }

    // --- Value changes ---

    [Fact]
    public void ValueChange_InEditMode_InvokesCallback()
    {
        ushort? callbackId = null;
        ushort? callbackValue = null;

        var vm = new QmkSettingsTabViewModel(
            [TappingTerm()], isEditMode: true,
            onSettingChanged: (id, val) => { callbackId = id; callbackValue = val; });

        vm.Settings[0].Value = 250;

        Assert.Equal(VialCommands.QmkSettingTappingTerm, callbackId);
        Assert.Equal((ushort)250, callbackValue);
    }

    [Fact]
    public void ValueChange_WhenReadOnly_NoCallback()
    {
        var callbackInvoked = false;

        var vm = new QmkSettingsTabViewModel(
            [TappingTerm()], isEditMode: false,
            onSettingChanged: (_, _) => callbackInvoked = true);

        // Value change still happens (no field revert anymore) but callback should not fire
        vm.Settings[0].Value = 250;

        Assert.False(callbackInvoked);
    }

    [Fact]
    public void IsPending_ReflectsValueChange()
    {
        var vm = new QmkSettingsTabViewModel(
            [TappingTerm()], isEditMode: true, onSettingChanged: (_, _) => { });

        Assert.False(vm.Settings[0].IsPending);

        vm.Settings[0].Value = 250;
        Assert.True(vm.Settings[0].IsPending);

        vm.Settings[0].Value = 200; // back to baseline
        Assert.False(vm.Settings[0].IsPending);
    }

    // --- Slider clamping ---

    [Fact]
    public void SliderValue_ClampsToMinMax()
    {
        var vm = new QmkSettingsTabViewModel(
            [TappingTerm()], isEditMode: true, onSettingChanged: (_, _) => { });

        var row = vm.Settings[0];
        row.MarkInitialized();

        // Tapping term descriptor: min=0, max=10000
        row.SliderValue = 20000;
        Assert.Equal(10000, row.Value);

        row.SliderValue = -5;
        Assert.Equal(0, row.Value);
    }

    // --- Unknown settings ---

    [Fact]
    public void UnknownSettingId_FallbackDescriptor_InMiscGroup()
    {
        var unknownSetting = new QmkSettingValue(0xFFFF, 42);
        var vm = new QmkSettingsTabViewModel([unknownSetting], isEditMode: false, onSettingChanged: null);

        Assert.True(vm.HasSettings);
        Assert.Single(vm.Groups);
        Assert.Single(vm.Settings);

        // Fallback descriptor creates a name like "Unknown (QSID 0xFFFF)"
        var row = vm.Settings[0];
        Assert.Equal((ushort)0xFFFF, row.SettingId);
        Assert.Equal((ushort)42, row.Value);
    }

    [Fact]
    public void MixedKnownAndUnknown_SeparateGroups()
    {
        var settings = new List<QmkSettingValue>
        {
            TappingTerm(),
            new(0xFFFF, 42),
        };
        var vm = new QmkSettingsTabViewModel(settings, isEditMode: false, onSettingChanged: null);

        // Should have 2 groups: TapHold and Misc
        Assert.Equal(2, vm.Groups.Count);
        Assert.Equal(2, vm.Settings.Count);
    }

    // --- Bitfield expansion ---

    [Fact]
    public void BitfieldQsid_ExpandsToPerBitRows()
    {
        // QSID 0x01 (GraveEscOverride) has 4 bits
        var settings = new List<QmkSettingValue>
        {
            new(VialCommands.QmkSettingGraveEscOverride, 0b0101, Width: 1),
        };
        var vm = new QmkSettingsTabViewModel(settings, isEditMode: false, onSettingChanged: null);

        Assert.Equal(4, vm.Settings.Count); // 4 bits
        Assert.Single(vm.Groups); // All in GraveEscape group

        // Bit 0 (Alt) = 1, Bit 1 (Ctrl) = 0, Bit 2 (GUI) = 1, Bit 3 (Shift) = 0
        Assert.Equal((ushort)1, vm.Settings[0].Value);
        Assert.Equal((ushort)0, vm.Settings[1].Value);
        Assert.Equal((ushort)1, vm.Settings[2].Value);
        Assert.Equal((ushort)0, vm.Settings[3].Value);
    }

    [Fact]
    public void BitfieldToggle_WritesPackedValue()
    {
        ushort? callbackId = null;
        ushort? callbackValue = null;

        var settings = new List<QmkSettingValue>
        {
            new(VialCommands.QmkSettingGraveEscOverride, 0b0000, Width: 1),
        };
        var vm = new QmkSettingsTabViewModel(settings, isEditMode: true,
            onSettingChanged: (id, val) => { callbackId = id; callbackValue = val; });

        // Toggle bit 2 (GUI) on
        vm.Settings[2].BoolValue = true;

        Assert.Equal(VialCommands.QmkSettingGraveEscOverride, callbackId);
        Assert.Equal((ushort)0b0100, callbackValue); // Only bit 2 set
    }

    [Fact]
    public void BitfieldToggle_PreservesOtherBits()
    {
        ushort? callbackValue = null;

        var settings = new List<QmkSettingValue>
        {
            new(VialCommands.QmkSettingGraveEscOverride, 0b0101, Width: 1), // bits 0 and 2 set
        };
        var vm = new QmkSettingsTabViewModel(settings, isEditMode: true,
            onSettingChanged: (_, val) => callbackValue = val);

        // Toggle bit 1 (Ctrl) on — should preserve bits 0 and 2
        vm.Settings[1].BoolValue = true;

        Assert.Equal((ushort)0b0111, callbackValue); // bits 0, 1, 2 all set
    }

    [Fact]
    public void BitfieldToggle_ClearBit_PreservesOthers()
    {
        ushort? callbackValue = null;

        var settings = new List<QmkSettingValue>
        {
            new(VialCommands.QmkSettingGraveEscOverride, 0b1111, Width: 1), // all bits set
        };
        var vm = new QmkSettingsTabViewModel(settings, isEditMode: true,
            onSettingChanged: (_, val) => callbackValue = val);

        // Toggle bit 0 (Alt) off
        vm.Settings[0].BoolValue = false;

        Assert.Equal((ushort)0b1110, callbackValue); // bit 0 cleared, rest preserved
    }

    [Fact]
    public void MagicBitfield_Expands10Bits()
    {
        var settings = new List<QmkSettingValue>
        {
            new(VialCommands.QmkSettingMagic, 0x0080, Width: 2), // bit 7 (NKRO) set
        };
        var vm = new QmkSettingsTabViewModel(settings, isEditMode: false, onSettingChanged: null);

        Assert.Equal(10, vm.Settings.Count);
        Assert.Single(vm.Groups);

        // Only bit 7 (NKRO) should be 1
        for (var i = 0; i < 10; i++)
            Assert.Equal(i == 7 ? (ushort)1 : (ushort)0, vm.Settings[i].Value);
    }

    [Fact]
    public void AutoShiftBitfield_Expands7Bits()
    {
        var settings = new List<QmkSettingValue>
        {
            new(VialCommands.QmkSettingAutoShiftFlags, 0, Width: 1),
        };
        var vm = new QmkSettingsTabViewModel(settings, isEditMode: false, onSettingChanged: null);

        Assert.Equal(7, vm.Settings.Count);
    }

    [Fact]
    public void MixedBitfieldAndScalar_InSameGroup()
    {
        // AutoShift flags (bitfield, QSID 0x03) + AutoShift timeout (scalar, QSID 0x04)
        // both go in AutoShift group
        var settings = new List<QmkSettingValue>
        {
            new(VialCommands.QmkSettingAutoShiftFlags, 0, Width: 1),
            new(VialCommands.QmkSettingAutoShiftTimeout, 175),
        };
        var vm = new QmkSettingsTabViewModel(settings, isEditMode: false, onSettingChanged: null);

        Assert.Single(vm.Groups); // Both in AutoShift
        Assert.Equal(8, vm.Settings.Count); // 7 bits + 1 scalar
    }
}

public class QmkSettingRowViewModelTests
{
    private static QmkSettingDescriptor TappingTermDescriptor() =>
        new(0x0007, "QmkSetting_TappingTerm", "TapHold", QmkSettingType.UInt16, 0, 10000, 200);

    [Fact]
    public void BoolValue_MapsToZeroOne()
    {
        var desc = new QmkSettingDescriptor(0x0009, "Test", "TapHold", QmkSettingType.Boolean, 0, 1, 0);
        var row = new QmkSettingRowViewModel(desc, 0, isEditMode: true, onValueChanged: (_, _) => { });

        Assert.False(row.BoolValue);

        row.BoolValue = true;
        Assert.Equal((ushort)1, row.Value);
        Assert.True(row.BoolValue);

        row.BoolValue = false;
        Assert.Equal((ushort)0, row.Value);
    }

    [Fact]
    public void SliderValue_RoundsToNearest()
    {
        var row = new QmkSettingRowViewModel(
            TappingTermDescriptor(), 200, isEditMode: true, onValueChanged: (_, _) => { });
        row.MarkInitialized();

        row.SliderValue = 205.7;
        Assert.Equal((ushort)206, row.Value);
    }

    [Fact]
    public void DescriptorProperties_Exposed()
    {
        var row = new QmkSettingRowViewModel(
            TappingTermDescriptor(), 200, isEditMode: false, onValueChanged: null);

        Assert.Equal(QmkSettingType.UInt16, row.SettingType);
        Assert.Equal((ushort)0, row.Min);
        Assert.Equal((ushort)10000, row.Max);
        Assert.Equal((ushort)200, row.DefaultValue);
        Assert.Equal((ushort)0x0007, row.SettingId);
        Assert.True(row.IsReadOnly);
    }
}
