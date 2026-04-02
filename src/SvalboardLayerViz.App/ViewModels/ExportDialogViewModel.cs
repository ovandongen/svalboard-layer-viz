using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.Core.Export;

namespace SvalboardLayerViz.App.ViewModels;

public partial class ExportDialogViewModel : ObservableObject
{
    public ObservableCollection<LayerExportItem> LayerItems { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPdfSelected))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private ExportFormat _selectedFormat = ExportFormat.Png;

    [ObservableProperty]
    private PdfPageSize _selectedPageSize = PdfPageSize.A4Landscape;

    public bool IsPdfSelected => SelectedFormat == ExportFormat.Pdf;

    public IReadOnlyList<ExportFormat> AvailableFormats { get; } =
        [ExportFormat.Png, ExportFormat.Pdf, ExportFormat.Svg];

    public IReadOnlyList<PdfPageSize> AvailablePageSizes { get; } =
        [PdfPageSize.A4Landscape, PdfPageSize.LetterLandscape];

    public Action? ExportRequested { get; set; }
    public Action? Cancelled { get; set; }

    public ExportDialogViewModel(IReadOnlyList<LayerViewModel> layers)
    {
        LayerItems = new ObservableCollection<LayerExportItem>(
            layers.Select(l => new LayerExportItem
            {
                Index = l.Index,
                DisplayName = l.DisplayName,
                TabColor = l.TabColor,
                IsSelected = true,
            }));

        foreach (var item in LayerItems)
            item.PropertyChanged += (_, _) => ExportCommand.NotifyCanExecuteChanged();
    }

    public bool HasSelectedLayers => LayerItems.Any(l => l.IsSelected);

    [RelayCommand(CanExecute = nameof(HasSelectedLayers))]
    private void Export() => ExportRequested?.Invoke();

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in LayerItems)
            item.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var item in LayerItems)
            item.IsSelected = false;
    }

    public ExportOptions BuildOptions(string outputPath) => new()
    {
        Format = SelectedFormat,
        SelectedLayerIndices = LayerItems.Where(l => l.IsSelected).Select(l => l.Index).ToList(),
        OutputPath = outputPath,
        HideThumbClusters = LayerItems
            .Where(l => l.IsSelected && l.HideThumbCluster)
            .ToDictionary(l => l.Index, _ => true),
        PdfPageSize = SelectedPageSize,
    };
}

public partial class LayerExportItem : ObservableObject
{
    public required int Index { get; init; }
    public required string DisplayName { get; init; }
    public required string TabColor { get; init; }

    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private bool _hideThumbCluster;
}
