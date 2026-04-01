using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SvalboardLayerViz.App.Views;

public partial class MainWindow : Window
{
    private bool _barsOnBottom;

    public MainWindow()
    {
        InitializeComponent();
        PositionChanged += OnPositionChanged;
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen is null) return;

        var windowMidY = e.Point.Y + (Height / 2);
        var screenMidY = screen.WorkingArea.Height / 2;
        var shouldBeBottom = windowMidY > screenMidY;

        if (shouldBeBottom == _barsOnBottom) return;
        _barsOnBottom = shouldBeBottom;

        // Move the whole bars panel to top or bottom of the outer grid
        Grid.SetRow(BarsPanel, shouldBeBottom ? 2 : 0);

        // Swap inner order: status bar always at the outer edge
        if (shouldBeBottom)
        {
            // Bottom: tabs first (row 0), status bar at very bottom (row 1)
            Grid.SetRow(LayerTabs, 0);
            Grid.SetRow(StatusBar, 1);
            StatusBar.CornerRadius = new CornerRadius(0, 0, 8, 8);
        }
        else
        {
            // Top: status bar at very top (row 0), tabs below (row 1)
            Grid.SetRow(StatusBar, 0);
            Grid.SetRow(LayerTabs, 1);
            StatusBar.CornerRadius = new CornerRadius(8, 8, 0, 0);
        }
    }

    /// <summary>
    /// Allows dragging the window by clicking the bars area (status bar + tabs).
    /// Needed because SystemDecorations="None" removes the title bar.
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(this);
        var barsHeight = BarsPanel.Bounds.Height;

        bool inBars = _barsOnBottom
            ? pos.Y > Bounds.Height - barsHeight
            : pos.Y < barsHeight;

        if (inBars)
            BeginMoveDrag(e);
    }
}
