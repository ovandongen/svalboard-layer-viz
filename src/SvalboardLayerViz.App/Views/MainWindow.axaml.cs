using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SvalboardLayerViz.App.Localization;

namespace SvalboardLayerViz.App.Views;

public partial class MainWindow : Window
{
    private bool _barsOnBottom;
    private const double ResizeEdge = 6;

    public MainWindow()
    {
        InitializeComponent();
        PositionChanged += OnPositionChanged;

        if (OperatingSystem.IsMacOS())
            SystemDecorations = SystemDecorations.Full;

        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;

        ApplyToolbarTooltips();
        Loc.CultureChanged += ApplyToolbarTooltips;
    }

    private void ApplyToolbarTooltips()
    {
        var loc = Loc.Instance;
        ToolTip.SetTip(QuitButton, loc["Tooltip_Quit"]);
        ToolTip.SetTip(MinimizeButton, loc["Tooltip_Minimize"]);
        ToolTip.SetTip(LiveButton, loc["Tooltip_LiveHighlighting"]);
        ToolTip.SetTip(AutoLayerButton, loc["Tooltip_AutoLayerSwitch"]);
        ToolTip.SetTip(ResetButton, loc["Tooltip_ResetLayerTracking"]);
        ToolTip.SetTip(DiagnosticsButton, loc["Tooltip_Diagnostics"]);
        ToolTip.SetTip(ExportButton, loc["Tooltip_Export"]);
        ToolTip.SetTip(VerticalLayoutButton, loc["Tooltip_VerticalLayout"]);
        ToolTip.SetTip(PinButton, loc["Tooltip_AlwaysOnTop"]);
        ToolTip.SetTip(HelpButton, loc["Tooltip_Help"]);
        ToolTip.SetTip(SettingsButton, loc["Tooltip_Settings"]);
        ToolTip.SetTip(RefreshButton, loc["Tooltip_Refresh"]);
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

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        Cursor = GetResizeCursor(pos) ?? new Cursor(StandardCursorType.Arrow);
    }

    /// <summary>
    /// Handles window drag (bars area) and resize (edges).
    /// SystemDecorations="None" removes OS title bar and resize handles — we replace both here.
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(this);

        var edge = GetResizeEdge(pos);
        if (edge.HasValue) { BeginResizeDrag(edge.Value, e); return; }

        var barsHeight = BarsPanel.Bounds.Height;
        bool inBars = _barsOnBottom
            ? pos.Y > Bounds.Height - barsHeight
            : pos.Y < barsHeight;

        if (inBars)
            BeginMoveDrag(e);
    }

    private WindowEdge? GetResizeEdge(Point pos)
    {
        bool left   = pos.X < ResizeEdge;
        bool right  = pos.X > Bounds.Width - ResizeEdge;
        bool top    = pos.Y < ResizeEdge;
        bool bottom = pos.Y > Bounds.Height - ResizeEdge;

        if (left  && top)    return WindowEdge.NorthWest;
        if (right && top)    return WindowEdge.NorthEast;
        if (left  && bottom) return WindowEdge.SouthWest;
        if (right && bottom) return WindowEdge.SouthEast;
        if (left)            return WindowEdge.West;
        if (right)           return WindowEdge.East;
        if (top)             return WindowEdge.North;
        if (bottom)          return WindowEdge.South;
        return null;
    }

    private Cursor? GetResizeCursor(Point pos) => GetResizeEdge(pos) switch
    {
        WindowEdge.NorthWest => new Cursor(StandardCursorType.TopLeftCorner),
        WindowEdge.NorthEast => new Cursor(StandardCursorType.TopRightCorner),
        WindowEdge.SouthWest => new Cursor(StandardCursorType.BottomLeftCorner),
        WindowEdge.SouthEast => new Cursor(StandardCursorType.BottomRightCorner),
        WindowEdge.West or WindowEdge.East  => new Cursor(StandardCursorType.SizeWestEast),
        WindowEdge.North or WindowEdge.South => new Cursor(StandardCursorType.SizeNorthSouth),
        _ => null
    };
}
