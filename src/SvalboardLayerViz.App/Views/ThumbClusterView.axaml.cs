using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SvalboardLayerViz.App.ViewModels;

namespace SvalboardLayerViz.App.Views;

public partial class ThumbClusterView : UserControl
{
    private const double CreativeScale = 1.4;

    public ThumbClusterView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnThumbKeyTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: KeyViewModel kvm } && kvm.IsEditMode)
        {
            kvm.ClickCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not KeyClusterViewModel vm)
            return;

        var useCreative = ThumbRenderSettings.Mode == ThumbRenderMode.Creative;

        DefaultLayout.IsVisible = !useCreative;
        LeftCreativeLayout.IsVisible = useCreative && !vm.IsRightHand;
        RightCreativeLayout.IsVisible = useCreative && vm.IsRightHand;

        if (useCreative)
        {
            // Scale from the inner edge so growth pushes outward:
            // Left hand: scale from right edge (1,0.5) → grows left
            // Right hand: scale from left edge (0,0.5) → grows right
            var originX = vm.IsRightHand ? 0.0 : 1.0;
            RenderTransformOrigin = new Avalonia.RelativePoint(originX, 0.5, Avalonia.RelativeUnit.Relative);
            RenderTransform = new ScaleTransform(CreativeScale, CreativeScale);
        }
    }
}
