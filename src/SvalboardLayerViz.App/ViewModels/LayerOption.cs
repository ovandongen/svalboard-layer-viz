namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// Layer identifier + user-facing name for the picker's layer dropdowns.
/// Populated from <see cref="LayerViewModel.DisplayName"/> when the picker opens.
/// </summary>
public record LayerOption(int Index, string DisplayName);
