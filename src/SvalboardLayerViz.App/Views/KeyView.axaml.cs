using Avalonia.Controls;
using Avalonia.Input;
using SvalboardLayerViz.App.ViewModels;

namespace SvalboardLayerViz.App.Views;

public partial class KeyView : UserControl
{
    public KeyView()
    {
        InitializeComponent();
    }

    private void OnKeyTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is KeyViewModel kvm && kvm.IsEditMode)
        {
            kvm.ClickCommand.Execute(null);
            e.Handled = true;
        }
    }
}
