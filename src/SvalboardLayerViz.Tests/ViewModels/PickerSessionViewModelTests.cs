using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Keymap.Builders;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

public class PickerSessionViewModelTests
{
    private PickerSessionViewModel CreateVm() => new(BuilderRegistry.CreateAll());

    private PickerSessionViewModel CreateVmWithCustoms(params (string Name, string ShortName)[] customs)
    {
        var list = customs
            .Select(c => new CustomKeycode { Name = c.Name, Title = c.Name, ShortName = c.ShortName })
            .ToList();
        return new PickerSessionViewModel(BuilderRegistry.CreateAll(), new KeycodeService(), list);
    }

    // --- Initial state ---

    [Fact]
    public void InitialState_IsCategoryStep()
    {
        var vm = CreateVm();
        Assert.Equal(PickerStep.Category, vm.CurrentStep);
        Assert.Null(vm.SelectedCategory);
        Assert.Null(vm.ActiveBuilder);
        Assert.Null(vm.Result);
    }

    [Fact]
    public void InitialState_CannotGoNext()
    {
        var vm = CreateVm();
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public void InitialState_CannotGoBack()
    {
        var vm = CreateVm();
        Assert.False(vm.CanGoBack);
    }

    // --- Category selection ---

    [Fact]
    public void SelectCategory_Basic_GoesToSelection()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Basic);

        Assert.Equal(PickerStep.Selection, vm.CurrentStep);
        Assert.Equal(PickerCategory.Basic, vm.SelectedCategory);
        Assert.True(vm.FilteredCatalog.Count > 0);
    }

    [Fact]
    public void SelectCategory_Media_GoesToSelection()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Media);

        Assert.Equal(PickerStep.Selection, vm.CurrentStep);
        Assert.True(vm.FilteredCatalog.Count > 0);
    }

    [Fact]
    public void SelectCategory_Layer_ShowsBuilders()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Layer);

        Assert.Equal(PickerStep.Selection, vm.CurrentStep);
        Assert.Equal(3, vm.AvailableBuilders.Count); // LayerTap, LayerMod, LayerFunction
    }

    [Fact]
    public void SelectCategory_Modifier_ShowsBuilders()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Modifier);

        Assert.Equal(2, vm.AvailableBuilders.Count); // ModTap, OneShotMod
    }

    [Fact]
    public void SelectCategory_Mouse_GoesToCatalog()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Mouse);

        Assert.Equal(PickerStep.Selection, vm.CurrentStep);
        Assert.True(vm.IsCatalogMode);
        Assert.False(vm.IsBuilderMode);
        // KeycodeCatalog.MouseKeys: Btn1-5, wheel, Acl0-2, ms-arrows = 16 entries
        Assert.Equal(KeycodeCatalog.MouseKeys.Count, vm.FilteredCatalog.Count);
        // Spot-check that mouse Btn1 (0xD1) is selectable
        Assert.Contains(vm.FilteredCatalog, e => e.Code == 0xD1);
    }

    [Fact]
    public void SelectCategory_Basic_DoesNotIncludeMouseKeys()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Basic);

        // Mouse keys live in their own category now — Basic should not include them.
        Assert.DoesNotContain(vm.FilteredCatalog, e => e.Code == 0xD1);
    }

    [Fact]
    public void SelectCategory_Custom_WithoutDeviceCustoms_FallsBackToBuilder()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Custom);

        Assert.Equal(PickerStep.Selection, vm.CurrentStep);
        Assert.False(vm.IsCatalogMode);
        Assert.True(vm.IsBuilderMode);
        Assert.Single(vm.AvailableBuilders);
        Assert.IsType<CustomKeycodeBuilder>(vm.AvailableBuilders[0]);
    }

    [Fact]
    public void SelectCategory_Custom_WithDeviceCustoms_BrowsesCatalog()
    {
        var vm = CreateVmWithCustoms(
            ("SV_SNIPER", "Snip"),
            ("SV_DPI_UP", "DPI+"),
            ("SV_DPI_DN", "DPI-"));
        vm.SelectCategoryCommand.Execute(PickerCategory.Custom);

        Assert.Equal(PickerStep.Selection, vm.CurrentStep);
        Assert.True(vm.IsCatalogMode);
        Assert.False(vm.IsBuilderMode);
        Assert.Equal(3, vm.FilteredCatalog.Count);
        Assert.Equal("Snip", vm.FilteredCatalog[0].Label);
        Assert.Equal((ushort)0x7E00, vm.FilteredCatalog[0].Code);
        Assert.Equal((ushort)0x7E02, vm.FilteredCatalog[2].Code);
    }

    [Fact]
    public void SelectCatalogEntry_CustomKeycode_PreviewsAndConfirms()
    {
        var vm = CreateVmWithCustoms(("SV_SNIPER", "Snip"));
        vm.SelectCategoryCommand.Execute(PickerCategory.Custom);

        var entry = vm.FilteredCatalog[0];
        vm.SelectCatalogEntryCommand.Execute(entry);

        Assert.Equal(PickerStep.Preview, vm.CurrentStep);
        Assert.Equal((ushort)0x7E00, vm.PreviewRawCode);
    }

    [Fact]
    public void SetRawKeycode_MouseButton_HydratesAsMouseCategory()
    {
        var vm = CreateVm();
        vm.SetRawKeycode(0xD1); // Btn1

        Assert.Equal(PickerCategory.Mouse, vm.SelectedCategory);
        Assert.NotEmpty(vm.FilteredCatalog);

        vm.GoBackCommand.Execute(null);
        Assert.Equal(PickerStep.Selection, vm.CurrentStep);
        Assert.True(vm.IsCatalogMode);
    }

    [Fact]
    public void SetRawKeycode_CustomKeycode_WithDeviceCustoms_HydratesAsCatalog()
    {
        var vm = CreateVmWithCustoms(("SV_SNIPER", "Snip"));
        vm.SetRawKeycode(0x7E00);

        Assert.Equal(PickerCategory.Custom, vm.SelectedCategory);
        Assert.True(vm.IsCatalogMode);
        Assert.NotEmpty(vm.FilteredCatalog);

        vm.GoBackCommand.Execute(null);
        Assert.Equal(PickerStep.Selection, vm.CurrentStep);
    }

    [Fact]
    public void SelectCategory_Advanced_GoesToPreview()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Advanced);

        Assert.Equal(PickerStep.Preview, vm.CurrentStep);
    }

    // --- Builder selection ---

    [Fact]
    public void SelectBuilder_GoesToArguments()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Layer);
        var builder = vm.AvailableBuilders[0];

        vm.SelectBuilderCommand.Execute(builder);
        Assert.Equal(PickerStep.Arguments, vm.CurrentStep);
        Assert.Same(builder, vm.ActiveBuilder);
    }

    // --- Catalog entry selection ---

    [Fact]
    public void SelectCatalogEntry_GoesToPreview()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Basic);

        var entry = vm.FilteredCatalog[0];
        vm.SelectCatalogEntryCommand.Execute(entry);

        Assert.Equal(PickerStep.Preview, vm.CurrentStep);
        Assert.NotNull(vm.Result);
        Assert.IsType<BasicKeycode>(vm.Result);
    }

    [Fact]
    public void SelectCatalogEntry_Empty_EmitsNoKeycode()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Basic);

        var entry = vm.FilteredCatalog.First(e => e.Code == 0x0000);
        vm.SelectCatalogEntryCommand.Execute(entry);

        Assert.Equal(PickerStep.Preview, vm.CurrentStep);
        Assert.IsType<NoKeycode>(vm.Result);
        Assert.Equal((ushort)0x0000, vm.PreviewRawCode);
    }

    [Fact]
    public void SelectCatalogEntry_Transparent_EmitsTransparentKeycode()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Basic);

        var entry = vm.FilteredCatalog.First(e => e.Code == 0x0001);
        vm.SelectCatalogEntryCommand.Execute(entry);

        Assert.IsType<TransparentKeycode>(vm.Result);
        Assert.Equal((ushort)0x0001, vm.PreviewRawCode);
    }

    [Fact]
    public void SelectCatalogEntry_Repeat_EmitsSpecialKeycode()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Basic);

        var entry = vm.FilteredCatalog.First(e => e.Code == 0x7C79);
        vm.SelectCatalogEntryCommand.Execute(entry);

        var s = Assert.IsType<SpecialKeycode>(vm.Result);
        Assert.Equal((ushort)0x7C79, s.Code);
        Assert.Equal((ushort)0x7C79, vm.PreviewRawCode);
    }

    [Fact]
    public void SetRawKeycode_NoKeycode_HydratesToBasicCategory()
    {
        var vm = CreateVm();
        vm.SetRawKeycode(0x0000);

        Assert.Equal(PickerStep.Preview, vm.CurrentStep);
        Assert.Equal(PickerCategory.Basic, vm.SelectedCategory);
        Assert.IsType<NoKeycode>(vm.Result);
        Assert.NotNull(vm.SelectedCatalogEntry);
        Assert.Equal((ushort)0x0000, vm.SelectedCatalogEntry!.Code);
    }

    [Fact]
    public void SetRawKeycode_Repeat_HydratesToBasicCategoryWithSpecialKeycode()
    {
        var vm = CreateVm();
        vm.SetRawKeycode(0x7C79);

        Assert.Equal(PickerCategory.Basic, vm.SelectedCategory);
        var s = Assert.IsType<SpecialKeycode>(vm.Result);
        Assert.Equal((ushort)0x7C79, s.Code);
        Assert.NotNull(vm.SelectedCatalogEntry);
        Assert.Equal((ushort)0x7C79, vm.SelectedCatalogEntry!.Code);
    }

    // --- GoNext from Arguments ---

    [Fact]
    public void GoNext_FromArguments_BuildsAndGoesToPreview()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Layer);

        // Pick LayerFunction builder
        var lfBuilder = vm.AvailableBuilders.OfType<LayerFunctionBuilder>().First();
        vm.SelectBuilderCommand.Execute(lfBuilder);

        lfBuilder.Kind = LayerFunctionKind.MO;
        lfBuilder.Layer = 1;

        vm.GoNextCommand.Execute(null);
        Assert.Equal(PickerStep.Preview, vm.CurrentStep);
        Assert.NotNull(vm.Result);
        Assert.IsType<LayerFunctionKeycode>(vm.Result);
    }

    // --- GoBack ---

    [Fact]
    public void GoBack_FromSelection_ReturnsToCategoryAndResets()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Basic);
        Assert.Equal(PickerStep.Selection, vm.CurrentStep);

        vm.GoBackCommand.Execute(null);
        Assert.Equal(PickerStep.Category, vm.CurrentStep);
        Assert.Null(vm.SelectedCategory);
    }

    [Fact]
    public void GoBack_FromArguments_ReturnsToSelection()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Modifier);
        vm.SelectBuilderCommand.Execute(vm.AvailableBuilders[0]);

        vm.GoBackCommand.Execute(null);
        Assert.Equal(PickerStep.Selection, vm.CurrentStep);
    }

    [Fact]
    public void GoBack_FromPreview_WithBuilder_ReturnsToArguments()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Layer);
        var lfBuilder = vm.AvailableBuilders.OfType<LayerFunctionBuilder>().First();
        vm.SelectBuilderCommand.Execute(lfBuilder);
        lfBuilder.Kind = LayerFunctionKind.TG;
        lfBuilder.Layer = 0;
        vm.GoNextCommand.Execute(null);
        Assert.Equal(PickerStep.Preview, vm.CurrentStep);

        vm.GoBackCommand.Execute(null);
        Assert.Equal(PickerStep.Arguments, vm.CurrentStep);
    }

    [Fact]
    public void GoBack_FromPreview_WithoutBuilder_ReturnsToSelection()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Basic);
        vm.SelectCatalogEntryCommand.Execute(vm.FilteredCatalog[0]);
        Assert.Equal(PickerStep.Preview, vm.CurrentStep);

        vm.GoBackCommand.Execute(null);
        Assert.Equal(PickerStep.Selection, vm.CurrentStep);
    }

    // --- Search ---

    [Fact]
    public void Search_FiltersCatalog()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Basic);
        var allCount = vm.FilteredCatalog.Count;

        vm.SearchText = "Bksp";
        Assert.True(vm.FilteredCatalog.Count < allCount);
        Assert.True(vm.FilteredCatalog.Count > 0);
    }

    [Fact]
    public void Search_EmptyString_ShowsAll()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Basic);
        var allCount = vm.FilteredCatalog.Count;

        vm.SearchText = "Z";
        vm.SearchText = "";
        Assert.Equal(allCount, vm.FilteredCatalog.Count);
    }

    // --- Raw keycode (Advanced) ---

    [Fact]
    public void SetRawKeycode_SetsResultAndPreview()
    {
        var vm = CreateVm();
        vm.SetRawKeycode(0x5221); // MO(1)

        Assert.NotNull(vm.Result);
        Assert.Equal(0x5221, vm.PreviewRawCode);
        Assert.Equal(PickerStep.Preview, vm.CurrentStep);
    }

    [Fact]
    public void SetRawKeycode_HydratesBuilderFromLayerFunction()
    {
        var vm = CreateVm();
        vm.SetRawKeycode(0x5221); // MO(1)

        Assert.Equal(PickerCategory.Layer, vm.SelectedCategory);
        var builder = Assert.IsType<LayerFunctionBuilder>(vm.ActiveBuilder);
        Assert.True(builder.CanBuild);
        Assert.Equal("MO(1)", builder.PreviewLabel);
    }

    [Fact]
    public void SetRawKeycode_BackFromPreview_LandsOnArgumentsWithSeededProperties()
    {
        var vm = CreateVm();
        vm.SetRawKeycode(0x5221); // MO(1)

        vm.GoBackCommand.Execute(null);

        Assert.Equal(PickerStep.Arguments, vm.CurrentStep);
        var builder = Assert.IsType<LayerFunctionBuilder>(vm.ActiveBuilder);
        Assert.Equal(LayerFunctionKind.MO, builder.Kind);
        Assert.Equal(1, builder.Layer);
    }

    [Fact]
    public void SetRawKeycode_HydratesModTapBuilder()
    {
        var vm = CreateVm();
        // MT(Ctrl, A) — mods=Ctrl(0x01) shifted into bits 8–12, base=A(0x04)
        vm.SetRawKeycode(0x2104);

        Assert.Equal(PickerCategory.Modifier, vm.SelectedCategory);
        var builder = Assert.IsType<ModTapBuilder>(vm.ActiveBuilder);
        Assert.True(builder.CanBuild);
        Assert.Equal(ModFlags.Ctrl, builder.Mods);
        Assert.Equal((ushort)0x04, builder.TapKey);
    }

    [Fact]
    public void SetRawKeycode_BasicKey_PopulatesCatalogForBackNavigation()
    {
        var vm = CreateVm();
        vm.SetRawKeycode(0x0004); // KC_A

        Assert.Equal(PickerCategory.Basic, vm.SelectedCategory);
        Assert.NotEmpty(vm.FilteredCatalog);

        vm.GoBackCommand.Execute(null);
        Assert.Equal(PickerStep.Selection, vm.CurrentStep);
        Assert.True(vm.IsCatalogMode);
    }

    // --- Confirm and recently-used ---

    [Fact]
    public void Confirm_AddsToRecentlyUsed()
    {
        PickerSessionViewModel.RecentlyUsed.Clear();
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Basic);
        vm.SelectCatalogEntryCommand.Execute(vm.FilteredCatalog[0]);

        vm.ConfirmCommand.Execute(null);
        Assert.Single(PickerSessionViewModel.RecentlyUsed);
    }

    [Fact]
    public void Confirm_MovesToFrontIfAlreadyRecent()
    {
        PickerSessionViewModel.RecentlyUsed.Clear();
        PickerSessionViewModel.RecentlyUsed.Add(0x0004);
        PickerSessionViewModel.RecentlyUsed.Add(0x0005);

        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Basic);
        // Find entry with code 0x0005
        var entry = vm.FilteredCatalog.FirstOrDefault(e => e.Code == 0x0005);
        if (entry is not null)
        {
            vm.SelectCatalogEntryCommand.Execute(entry);
            vm.ConfirmCommand.Execute(null);
            Assert.Equal(0x0005, PickerSessionViewModel.RecentlyUsed[0]);
        }
    }

    [Fact]
    public void Confirm_LimitsTo20()
    {
        PickerSessionViewModel.RecentlyUsed.Clear();
        for (var i = 0; i < 25; i++)
            PickerSessionViewModel.RecentlyUsed.Add((ushort)(0x0004 + i));

        // Confirm will trim to 20 + 1 new = 21, then trim to 20
        var vm = CreateVm();
        vm.SetRawKeycode(0xFFFF);
        vm.ConfirmCommand.Execute(null);

        Assert.Equal(20, PickerSessionViewModel.RecentlyUsed.Count);
    }

    // --- Preview info ---

    [Fact]
    public void PreviewRawCode_MatchesEncodedResult()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Layer);
        var lfBuilder = vm.AvailableBuilders.OfType<LayerFunctionBuilder>().First();
        vm.SelectBuilderCommand.Execute(lfBuilder);
        lfBuilder.Kind = LayerFunctionKind.MO;
        lfBuilder.Layer = 2;
        vm.GoNextCommand.Execute(null);

        var expectedRaw = KeycodeEncoder.Encode(new LayerFunctionKeycode(LayerFunctionKind.MO, 2));
        Assert.Equal(expectedRaw, vm.PreviewRawCode);
    }

    // --- State preservation on Back (the bugs this refactor fixes) ---

    [Fact]
    public void GoBack_FromPreview_PreservesActiveBuilderSlotValues()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Layer);
        var lfBuilder = vm.AvailableBuilders.OfType<LayerFunctionBuilder>().First();
        vm.SelectBuilderCommand.Execute(lfBuilder);
        lfBuilder.Kind = LayerFunctionKind.MO;
        lfBuilder.Layer = 1;
        vm.GoNextCommand.Execute(null);
        Assert.Equal(PickerStep.Preview, vm.CurrentStep);

        // Back to Arguments — typed properties must still be populated,
        // so CanGoNext is immediately true without any re-entry.
        vm.GoBackCommand.Execute(null);

        Assert.Equal(PickerStep.Arguments, vm.CurrentStep);
        Assert.Equal(LayerFunctionKind.MO, lfBuilder.Kind);
        Assert.Equal(1, lfBuilder.Layer);
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public void GoBack_FromPreview_ToSelection_PreservesResult_AndNextReturnsToPreview()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Basic);
        var entry = vm.FilteredCatalog[0];
        vm.SelectCatalogEntryCommand.Execute(entry);
        Assert.Equal(PickerStep.Preview, vm.CurrentStep);
        var resultBefore = vm.Result;

        // Back: Preview → Selection. Result should survive so the user can resume.
        vm.GoBackCommand.Execute(null);
        Assert.Equal(PickerStep.Selection, vm.CurrentStep);
        Assert.Same(resultBefore, vm.Result);
        Assert.True(vm.CanGoNext);
        Assert.True(vm.ShowNextButton);

        // Next from Selection with retained Result → back at Preview.
        vm.GoNextCommand.Execute(null);
        Assert.Equal(PickerStep.Preview, vm.CurrentStep);
        Assert.Same(resultBefore, vm.Result);
    }

    [Fact]
    public void GoBack_AllTheWayFromPreview_WithBuilder_ReachesSelectionWithRetainedBuilder()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Layer);
        var lfBuilder = vm.AvailableBuilders.OfType<LayerFunctionBuilder>().First();
        vm.SelectBuilderCommand.Execute(lfBuilder);
        lfBuilder.Kind = LayerFunctionKind.MO;
        lfBuilder.Layer = 1;
        vm.GoNextCommand.Execute(null);

        vm.GoBackCommand.Execute(null); // Preview → Arguments
        vm.GoBackCommand.Execute(null); // Arguments → Selection

        Assert.Equal(PickerStep.Selection, vm.CurrentStep);
        Assert.Same(lfBuilder, vm.ActiveBuilder);

        // Forward again should land straight back in Arguments with slots intact.
        vm.GoNextCommand.Execute(null);
        Assert.Equal(PickerStep.Arguments, vm.CurrentStep);
        Assert.Equal(LayerFunctionKind.MO, lfBuilder.Kind);
        Assert.Equal(1, lfBuilder.Layer);
    }

    [Fact]
    public void BuilderPropertyChange_LiveUpdatesCanGoNext()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Layer);
        var lfBuilder = vm.AvailableBuilders.OfType<LayerFunctionBuilder>().First();
        vm.SelectBuilderCommand.Execute(lfBuilder);
        Assert.False(vm.CanGoNext);

        var raised = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.CanGoNext)) raised++;
        };

        lfBuilder.Kind = LayerFunctionKind.MO;
        lfBuilder.Layer = 1;

        Assert.True(vm.CanGoNext);
        Assert.True(raised > 0, "CanGoNext should fire PropertyChanged when builder slots change");
    }

    [Fact]
    public void SelectTapKey_Command_SetsTapKeyOnActiveBuilder()
    {
        var vm = CreateVm();
        vm.SelectCategoryCommand.Execute(PickerCategory.Modifier);
        var mt = vm.AvailableBuilders.OfType<ModTapBuilder>().First();
        vm.SelectBuilderCommand.Execute(mt);

        vm.SelectTapKeyCommand.Execute((ushort)0x04); // KC_A

        Assert.Equal((ushort)0x04, mt.TapKey);
    }

    [Fact]
    public void LayerOptions_AreExposedFromConstructor()
    {
        var opts = new List<LayerOption>
        {
            new(0, "Base"),
            new(1, "Symbols"),
        };
        var vm = new PickerSessionViewModel(BuilderRegistry.CreateAll(), new KeycodeService(), [], opts);

        Assert.Equal(2, vm.LayerOptions.Count);
        Assert.Equal("Symbols", vm.LayerOptions[1].DisplayName);
    }

    // --- Macro category ---

    private PickerSessionViewModel CreateVmWithMacros(int macroCount) =>
        new(BuilderRegistry.CreateAll(), new KeycodeService(), [], [], macroCount: macroCount);

    [Fact]
    public void HasMacros_WhenCountPositive_ReturnsTrue()
    {
        var vm = CreateVmWithMacros(4);
        Assert.True(vm.HasMacros);
    }

    [Fact]
    public void HasMacros_WhenCountZero_ReturnsFalse()
    {
        var vm = CreateVmWithMacros(0);
        Assert.False(vm.HasMacros);
    }

    [Fact]
    public void SelectMacroCategory_ShowsCatalogEntries()
    {
        var vm = CreateVmWithMacros(3);
        vm.SelectCategoryCommand.Execute(PickerCategory.Macro);

        Assert.True(vm.IsCatalogMode);
        Assert.Equal(PickerStep.Selection, vm.CurrentStep);
        Assert.Equal(3, vm.FilteredCatalog.Count);
        Assert.Equal("M0", vm.FilteredCatalog[0].Label);
        Assert.Equal((ushort)0x7700, vm.FilteredCatalog[0].Code);
        Assert.Equal("M2", vm.FilteredCatalog[2].Label);
        Assert.Equal((ushort)0x7702, vm.FilteredCatalog[2].Code);
    }

    [Fact]
    public void SelectMacroCatalogEntry_AdvancesToPreview()
    {
        var vm = CreateVmWithMacros(2);
        vm.SelectCategoryCommand.Execute(PickerCategory.Macro);
        vm.SelectCatalogEntryCommand.Execute(vm.FilteredCatalog[1]);

        Assert.Equal(PickerStep.Preview, vm.CurrentStep);
        Assert.IsType<MacroKeycode>(vm.Result);
        Assert.Equal(1, ((MacroKeycode)vm.Result!).MacroIndex);
    }

    [Fact]
    public void HydrateMacroKeycode_SetsCategory()
    {
        var vm = CreateVmWithMacros(4);
        // Hydrate from M2 (0x7702)
        vm.SetRawKeycode(0x7702);

        Assert.Equal(PickerCategory.Macro, vm.SelectedCategory);
        Assert.Equal(PickerStep.Preview, vm.CurrentStep);
    }

    // --- Modifier toggles on catalog selection ---

    [Fact]
    public void SelectBasicKey_WithShiftMod_ReturnsModifiedKeycode()
    {
        var vm = new PickerSessionViewModel();
        vm.SelectCategoryCommand.Execute(PickerCategory.Basic);
        vm.ModShift = true;

        // Select 'A' (0x04)
        var entry = vm.FilteredCatalog.First(e => e.Code == 0x04);
        vm.SelectedCatalogEntry = entry;

        // Should produce LSFT(KC_A) = 0x0204
        Assert.Equal(PickerStep.Preview, vm.CurrentStep);
        Assert.Equal(0x0204, vm.PreviewRawCode);
        Assert.Contains("Shift", vm.PreviewLabel);
        Assert.IsType<ModifiedKeycode>(vm.Result);
    }

    [Fact]
    public void SelectBasicKey_NoMods_ReturnsPlainKeycode()
    {
        var vm = new PickerSessionViewModel();
        vm.SelectCategoryCommand.Execute(PickerCategory.Basic);

        var entry = vm.FilteredCatalog.First(e => e.Code == 0x04);
        vm.SelectedCatalogEntry = entry;

        Assert.Equal(0x04, vm.PreviewRawCode);
        Assert.IsType<BasicKeycode>(vm.Result);
    }

    [Fact]
    public void SelectBasicKey_WithMultipleMods_CombinesBitmask()
    {
        var vm = new PickerSessionViewModel();
        vm.SelectCategoryCommand.Execute(PickerCategory.Basic);
        vm.ModCtrl = true;
        vm.ModAlt = true;

        var entry = vm.FilteredCatalog.First(e => e.Code == 0x04);
        vm.SelectedCatalogEntry = entry;

        // (Ctrl|Alt)<<8 | 0x04 = 0x0504
        Assert.Equal(0x0504, vm.PreviewRawCode);
        Assert.Contains("Ctrl", vm.PreviewLabel);
        Assert.Contains("Alt", vm.PreviewLabel);
    }
}
