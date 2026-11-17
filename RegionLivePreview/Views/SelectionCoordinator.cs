using Avalonia;
using RegionLivePreview.Core;
using RegionLivePreview.Platform;

namespace RegionLivePreview.Views;

public sealed record SelectionResult(PixelRect Region);

/// <summary>
/// 管理一次跨屏选区会话。每个显示器一个遮罩窗口，选区坐标以虚拟桌面物理像素统一表示。
/// </summary>
public sealed class SelectionCoordinator
{
    private readonly List<SelectionOverlayWindow> _overlays = new();
    private MonitorInfo? _origin;
    private PixelPoint _startPhysical;
    private PixelPoint _endPhysical;
    private PixelRect _captureBounds;
    private PixelRect _virtualBounds;
    private bool _ending;

    public event Action<SelectionResult>? SelectionCompleted;

    public event Action? Cancelled;

    public bool IsActive { get; private set; }

    public bool IsDragging { get; private set; }

    public bool Begin()
    {
        if (IsActive)
        {
            BringToFront();
            return true;
        }

        var monitors = MonitorService.GetMonitors();
        if (monitors.Count == 0)
        {
            return false;
        }

        IsActive = true;
        _ending = false;
        IsDragging = false;
        _origin = null;
        _virtualBounds = GetVirtualBounds(monitors);
        Logger.Info($"开始选区：monitors={string.Join("; ", monitors.Select(static m => $"{m.X},{m.Y},{m.Width}x{m.Height}@{m.Scaling}x"))} virtual={_virtualBounds}");

        foreach (var monitor in monitors)
        {
            var overlay = new SelectionOverlayWindow(this, monitor);
            overlay.Closed += OnOverlayClosed;
            _overlays.Add(overlay);
            overlay.Show();
        }

        var primary = _overlays.FirstOrDefault(static x => x.Monitor.IsPrimary) ?? _overlays[0];
        primary.Activate();
        primary.FocusInput();
        return true;
    }

    public void BeginDrag(SelectionOverlayWindow overlay, Point localPoint)
    {
        if (!IsActive || IsDragging)
        {
            return;
        }

        _origin = overlay.Monitor;
        _captureBounds = overlay.CaptureBounds;
        _startPhysical = ClampToDesktop(ToPhysical(overlay, localPoint));
        _endPhysical = _startPhysical;
        IsDragging = true;
        Logger.Info($"选区起点：local={localPoint} physical={_startPhysical} monitor={overlay.Monitor.X},{overlay.Monitor.Y}@{overlay.Monitor.Scaling}x capture={_captureBounds}");

        foreach (var item in _overlays)
        {
            item.HideHint();
        }

        RefreshVisuals();
    }

    public void UpdateDrag(SelectionOverlayWindow overlay, Point localPoint)
    {
        if (!IsActive || !IsDragging || overlay.Monitor != _origin)
        {
            return;
        }

        _endPhysical = ClampToDesktop(ToPhysical(overlay, localPoint));
        RefreshVisuals();
    }

    public void CompleteDrag(SelectionOverlayWindow overlay, Point localPoint)
    {
        if (!IsActive || !IsDragging || overlay.Monitor != _origin)
        {
            return;
        }

        UpdateDrag(overlay, localPoint);
        IsDragging = false;

        var selection = NormalizeSelection(_startPhysical, _endPhysical);
        Logger.Info($"选区结束：physical={selection}");
        if (selection.Width >= 12 && selection.Height >= 12)
        {
            End();
            SelectionCompleted?.Invoke(new SelectionResult(selection));
        }
        else
        {
            Cancel();
        }
    }

    public void Cancel()
    {
        if (!IsActive)
        {
            return;
        }

        IsDragging = false;
        End();
        Cancelled?.Invoke();
    }

    public void BringToFront()
    {
        if (!IsActive)
        {
            return;
        }

        var primary = _overlays.FirstOrDefault(static x => x.Monitor.IsPrimary) ?? _overlays.FirstOrDefault();
        primary?.Activate();
        primary?.FocusInput();
    }

    private void RefreshVisuals()
    {
        PixelRect? selection = IsDragging ? NormalizeSelection(_startPhysical, _endPhysical) : null;

        foreach (var overlay in _overlays)
        {
            var showBadge = selection is not null && overlay.Monitor == _origin;
            overlay.ShowSelection(selection, showBadge);
        }
    }

    private void End()
    {
        IsActive = false;
        _ending = true;

        foreach (var overlay in _overlays)
        {
            overlay.Closed -= OnOverlayClosed;
            overlay.Close();
        }

        _overlays.Clear();
        _origin = null;
        _ending = false;
    }

    private void OnOverlayClosed(object? sender, EventArgs e)
    {
        if (IsActive && !_ending)
        {
            // 遮罩被系统或用户意外关闭（例如 Alt+F4）时取消本次选区。
            Cancel();
        }
    }

    private static PixelRect NormalizeSelection(PixelPoint start, PixelPoint end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);
        return new PixelRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static PixelPoint ToPhysical(SelectionOverlayWindow overlay, Point localPoint)
    {
        var bounds = overlay.CaptureBounds;
        var scale = overlay.Monitor.Scaling;
        return new PixelPoint(
            bounds.X + (int)Math.Round(localPoint.X * scale),
            bounds.Y + (int)Math.Round(localPoint.Y * scale));
    }

    private PixelPoint ClampToDesktop(PixelPoint point)
    {
        return new PixelPoint(
            Math.Clamp(point.X, _virtualBounds.X, _virtualBounds.X + _virtualBounds.Width - 1),
            Math.Clamp(point.Y, _virtualBounds.Y, _virtualBounds.Y + _virtualBounds.Height - 1));
    }

    private static PixelRect GetVirtualBounds(IReadOnlyList<MonitorInfo> monitors)
    {
        var left = monitors.Min(static monitor => monitor.X);
        var top = monitors.Min(static monitor => monitor.Y);
        var right = monitors.Max(static monitor => monitor.X + monitor.Width);
        var bottom = monitors.Max(static monitor => monitor.Y + monitor.Height);
        return new PixelRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }
}
