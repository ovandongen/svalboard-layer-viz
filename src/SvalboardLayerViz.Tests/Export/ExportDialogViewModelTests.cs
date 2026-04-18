using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Export;
using SvalboardLayerViz.Core.Models;
using Xunit;

namespace SvalboardLayerViz.Tests.Export;

public class ExportDialogViewModelTests
{
    private static List<LayerViewModel> MakeLayers()
    {
        var layerRecords = Enumerable.Range(0, 3)
            .Select(i => new Layer { Index = i, Name = $"Layer {i}", Keys = [] })
            .ToList();
        var palette = new LayerColorPalette(layerRecords, totalLayers: 3);
        return layerRecords.Select(l => new LayerViewModel(l, palette, _ => { })).ToList();
    }

    [Fact]
    public void Constructor_PopulatesAllLayers()
    {
        var vm = new ExportDialogViewModel(MakeLayers());
        Assert.Equal(3, vm.LayerItems.Count);
        Assert.All(vm.LayerItems, item => Assert.True(item.IsSelected));
    }

    [Fact]
    public void SelectNone_DeselectsAll()
    {
        var vm = new ExportDialogViewModel(MakeLayers());
        vm.SelectNoneCommand.Execute(null);
        Assert.All(vm.LayerItems, item => Assert.False(item.IsSelected));
    }

    [Fact]
    public void SelectAll_ReselectsAll()
    {
        var vm = new ExportDialogViewModel(MakeLayers());
        vm.SelectNoneCommand.Execute(null);
        vm.SelectAllCommand.Execute(null);
        Assert.All(vm.LayerItems, item => Assert.True(item.IsSelected));
    }

    [Fact]
    public void ExportCommand_DisabledWhenNoneSelected()
    {
        var vm = new ExportDialogViewModel(MakeLayers());
        vm.SelectNoneCommand.Execute(null);
        Assert.False(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public void ExportCommand_EnabledWhenSomeSelected()
    {
        var vm = new ExportDialogViewModel(MakeLayers());
        Assert.True(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public void BuildOptions_IncludesSelectedLayersOnly()
    {
        var vm = new ExportDialogViewModel(MakeLayers());
        vm.LayerItems[1].IsSelected = false;

        var options = vm.BuildOptions("/tmp/test.png");
        Assert.Equal(2, options.SelectedLayerIndices.Count);
        Assert.Contains(0, options.SelectedLayerIndices);
        Assert.Contains(2, options.SelectedLayerIndices);
        Assert.DoesNotContain(1, options.SelectedLayerIndices);
    }

    [Fact]
    public void BuildOptions_IncludesHideThumbClusterFlags()
    {
        var vm = new ExportDialogViewModel(MakeLayers());
        vm.LayerItems[0].HideThumbCluster = true;

        var options = vm.BuildOptions("/tmp/test.png");
        Assert.True(options.HideThumbClusters.ContainsKey(0));
        Assert.False(options.HideThumbClusters.ContainsKey(1));
    }

    [Fact]
    public void BuildOptions_UsesSelectedFormat()
    {
        var vm = new ExportDialogViewModel(MakeLayers());
        vm.SelectedFormat = ExportFormat.Svg;

        var options = vm.BuildOptions("/tmp/test.svg");
        Assert.Equal(ExportFormat.Svg, options.Format);
    }

    [Fact]
    public void DefaultFormat_IsPng()
    {
        var vm = new ExportDialogViewModel(MakeLayers());
        Assert.Equal(ExportFormat.Png, vm.SelectedFormat);
    }
}
