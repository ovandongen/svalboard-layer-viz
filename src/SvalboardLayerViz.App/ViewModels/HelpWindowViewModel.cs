using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SvalboardLayerViz.App.ViewModels;

public partial class HelpWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _dontShowAgain;

    public Action? Closed { get; set; }

    [RelayCommand]
    private void Close() => Closed?.Invoke();
}
