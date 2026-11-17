using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using RegionLivePreview.Core;
using RegionLivePreview.Platform;
using RegionLivePreview.Platform.Windows;

namespace RegionLivePreview.Views;

public partial class SelectionOverlayWindow : Window
{
    private readonly SelectionCoordinator _coordinator;
    private readonly Grid _root;
    private readonly Rectangle _dimTop;
    private readonly Rectangle _dimBottom;
    private readonly Rectangle _dimLeft;
    private readonly Rectangle _dimRight;
    private readonly Rectangle _selectionBorder;
    private readonly Border _sizeBadge;
    private readonly TextBlock _sizeText;
    private readonly Border _hintBadge;

    private PixelRect? _lastSelection;
    private bool _lastShowBadge;
    private Point _lastPointerPosition;

    public SelectionOverlayWindow(SelectionCoordinator coordinator, MonitorInfo monitor)
    {
        _coordinator = coordinator;
        Monitor = monitor;

        InitializeComponent();

        Width = Math.Ceiling(monitor.Width / monitor.Scaling);
        Height = Math.Ceiling(monitor.Height / monitor.Scaling);
        Position = new PixelPoint(monitor.X, monitor.Y);

        _root = this.FindControl<Grid>("Root")!;
        _dimTop = this.FindControl<Rectangle>("DimTop")!;
        _dimBottom = this.FindControl<Rectangle>("DimBottom")!;
        _dimLeft = this.FindControl<Rectangle>("DimLeft")!;
        _dimRight = this.FindControl<Rectangle>("DimRight")!;
        _selectionBorder = this.FindControl<Rectangle>("SelectionBorder")!;
        _sizeBadge = this.FindControl<Border>("SizeBadge")!;
        _sizeText = this.FindControl<TextBlock>("SizeText")!;
        _hintBadge = this.FindControl<Border>("HintBadge")!;

        Opened += OnOpened;
    }

    public MonitorInfo Monitor { get; }

    public void FocusInput()
    {
        Activate();
        _root.Focus();
    }

    public void HideHint()
    {
        _hintBadge.IsVisible = false;
    }

    public void ShowSelection(PixelRect? selection, bool showBadge)
    {
        _lastSelection = selection;
        _lastShowBadge = showBadge;
        ApplySelection(selection, showBadge);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Logger.Info($"遮罩已打开：monitor=({Monitor.X},{Monitor.Y},{Monitor.Width}x{Monitor.Height})@{Monitor.Scaling}x pos={Position} size={Width}x{Height} bounds={Bounds} client={ClientSize} renderScaling={RenderScaling} native={WindowInterop.GetNativeBounds(this)}");
        FocusInput();
        ApplySelection(_lastSelection, _lastShowBadge);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplySelection(_lastSelection, _lastShowBadge);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _coordinator.Cancel();
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_root);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _root.Focus();
        _lastPointerPosition = e.GetPosition(_root);
        Logger.Info($"遮罩收到按下：local={_lastPointerPosition} native={WindowInterop.GetNativeBounds(this)}");
        _coordinator.BeginDrag(this, _lastPointerPosition);
        e.Pointer.Capture(_root);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        _lastPointerPosition = e.GetPosition(_root);

        if (_coordinator.IsDragging)
        {
            _coordinator.UpdateDrag(this, _lastPointerPosition);
            e.Handled = true;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_coordinator.IsDragging)
        {
            return;
        }

        _lastPointerPosition = e.GetPosition(_root);
        _coordinator.CompleteDrag(this, _lastPointerPosition);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_coordinator.IsDragging)
        {
            _coordinator.CompleteDrag(this, _lastPointerPosition);
        }
    }

    private void ApplySelection(PixelRect? selection, bool showBadge)
    {
        var viewWidth = Width;
        var viewHeight = Height;

        if (!double.IsFinite(viewWidth) || viewWidth <= 0)
        {
            viewWidth = Bounds.Width;
        }

        if (!double.IsFinite(viewHeight) || viewHeight <= 0)
        {
            viewHeight = Bounds.Height;
        }

        if (viewWidth <= 0 || viewHeight <= 0)
        {
            return;
        }

        if (selection is null || selection.Value.Width <= 0 || selection.Value.Height <= 0)
        {
            SetRect(_dimTop, 0, 0, viewWidth, viewHeight);
            SetRect(_dimBottom, 0, 0, 0, 0);
            SetRect(_dimLeft, 0, 0, 0, 0);
            SetRect(_dimRight, 0, 0, 0, 0);
            _selectionBorder.IsVisible = false;
            _sizeBadge.IsVisible = false;
            return;
        }

        var selected = selection.Value;
        var scale = Monitor.Scaling;

        var left = (selected.X - Monitor.X) / scale;
        var top = (selected.Y - Monitor.Y) / scale;
        var right = (selected.X + selected.Width - Monitor.X) / scale;
        var bottom = (selected.Y + selected.Height - Monitor.Y) / scale;

        var holeLeft = Math.Clamp(left, 0, viewWidth);
        var holeTop = Math.Clamp(top, 0, viewHeight);
        var holeRight = Math.Clamp(right, 0, viewWidth);
        var holeBottom = Math.Clamp(bottom, 0, viewHeight);

        var holeWidth = Math.Max(0, holeRight - holeLeft);
        var holeHeight = Math.Max(0, holeBottom - holeTop);

        SetRect(_dimTop, 0, 0, viewWidth, holeTop);
        SetRect(_dimBottom, 0, holeBottom, viewWidth, Math.Max(0, viewHeight - holeBottom));
        SetRect(_dimLeft, 0, holeTop, holeLeft, holeHeight);
        SetRect(_dimRight, holeRight, holeTop, Math.Max(0, viewWidth - holeRight), holeHeight);

        Canvas.SetLeft(_selectionBorder, left);
        Canvas.SetTop(_selectionBorder, top);
        _selectionBorder.Width = Math.Max(0, right - left);
        _selectionBorder.Height = Math.Max(0, bottom - top);
        _selectionBorder.IsVisible = true;

        if (!showBadge)
        {
            _sizeBadge.IsVisible = false;
            return;
        }

        _sizeText.Text = $"{selected.Width} × {selected.Height}";
        _sizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var badgeWidth = _sizeBadge.DesiredSize.Width;
        var badgeHeight = _sizeBadge.DesiredSize.Height;

        var badgeX = Math.Clamp(right - badgeWidth, 8, Math.Max(8, viewWidth - badgeWidth - 8));
        var badgeY = bottom + 8;

        if (badgeY + badgeHeight + 8 > viewHeight)
        {
            badgeY = Math.Max(8, top - badgeHeight - 8);
        }

        Canvas.SetLeft(_sizeBadge, badgeX);
        Canvas.SetTop(_sizeBadge, badgeY);
        _sizeBadge.IsVisible = true;
    }

    private static void SetRect(Rectangle rectangle, double x, double y, double width, double height)
    {
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
        rectangle.Width = Math.Max(0, width);
        rectangle.Height = Math.Max(0, height);
    }
}
