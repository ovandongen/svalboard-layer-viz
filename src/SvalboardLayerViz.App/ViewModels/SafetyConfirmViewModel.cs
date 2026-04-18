using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.App.Localization;
using SvalboardLayerViz.Core.Keymap;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// Display record for a single safety warning in the confirm dialog.
/// </summary>
public record SafetyWarningItem(string Display);

/// <summary>
/// Drives the safety-warning confirm dialog. Wraps the list of
/// <see cref="SafetyWarning"/>s emitted by <see cref="PreSaveSafetyCheck"/>
/// and resolves their kind labels through <see cref="Loc"/> so the dialog
/// is fully localized.
/// </summary>
public partial class SafetyConfirmViewModel : ObservableObject
{
    public IReadOnlyList<SafetyWarningItem> Warnings { get; }

    /// <summary>Invoked when the user closes the dialog. True = proceed, false = cancel.</summary>
    public Action<bool>? Closed { get; set; }

    public SafetyConfirmViewModel(IReadOnlyList<SafetyWarning> warnings)
    {
        Warnings = warnings.Select(Format).ToList();
    }

    private static SafetyWarningItem Format(SafetyWarning warning)
    {
        var label = Loc.Instance[$"SafetyWarning_{warning.Kind}"];
        var display = warning.Layer is int layer
            ? $"Layer {layer}: {label}"
            : label;
        if (!string.IsNullOrWhiteSpace(warning.Detail))
            display = $"{display} — {warning.Detail}";
        return new SafetyWarningItem(display);
    }

    [RelayCommand]
    private void Confirm() => Closed?.Invoke(true);

    [RelayCommand]
    private void Cancel() => Closed?.Invoke(false);
}
