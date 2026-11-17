using Avalonia;
using RegionLivePreview.Core;
using RegionLivePreview.Platform;
using RegionLivePreview.Views;

namespace RegionLivePreview.Services;

public sealed class PreviewManager
{
    private readonly App _app;
    private readonly List<PreviewWindow> _windows = new();
    private bool _suppressSync;

    public PreviewManager(App app)
    {
        _app = app;
    }

    public IReadOnlyList<PreviewWindow> Windows => _windows;

    public int Count => _windows.Count;

    public bool AnyClickThrough => _windows.Any(static window => window.IsClickThrough);

    public PreviewWindow? OpenForRegion(PixelRect region)
    {
        var monitor = MonitorService.GetMonitorForRegion(region) ?? MonitorService.Primary;
        if (monitor is null)
        {
            return null;
        }

        var geometry = CalculateGeometry(region, monitor);
        var config = _app.Config;
        Logger.Info($"创建预览：region={region} monitor=({monitor.X},{monitor.Y},{monitor.Width}x{monitor.Height}) scale={monitor.Scaling} work={monitor.WorkArea} geometry={geometry.Width}x{geometry.Height}@{geometry.Position}");

        return OpenInternal(new PreviewOpenOptions
        {
            Region = region,
            Fps = config.DefaultFps,
            CaptureScale = config.CaptureScale,
            Opacity = config.DefaultOpacity,
            ClickThrough = config.DefaultClickThrough,
            LockAspectRatio = config.DefaultLockAspectRatio,
            Topmost = IsTopmostForSharing(),
            WindowX = geometry.Position.X,
            WindowY = geometry.Position.Y,
            WindowWidth = geometry.Width,
            WindowHeight = geometry.Height
        });
    }

    public void Restore(PreviewWindowState state)
    {
        if (!state.IsUsable)
        {
            return;
        }

        var region = new PixelRect(state.RegionX, state.RegionY, state.RegionWidth, state.RegionHeight);
        var monitor = MonitorService.GetMonitorForRegion(region);
        if (monitor is null)
        {
            return;
        }

        var fallback = CalculateGeometry(region, monitor);
        var width = state.WindowWidth >= 80 ? state.WindowWidth : fallback.Width;
        var height = state.WindowHeight >= 50 ? state.WindowHeight : fallback.Height;
        var physicalPosition = new PixelPoint(state.WindowX, state.WindowY);
        var position = MonitorService.IsPointOnAnyMonitor(new PixelPoint(physicalPosition.X + 8, physicalPosition.Y + 8))
            ? monitor.PhysicalToWindowPosition(physicalPosition)
            : fallback.Position;

        var config = _app.Config;
        OpenInternal(new PreviewOpenOptions
        {
            Region = region,
            Fps = config.DefaultFps,
            CaptureScale = config.CaptureScale,
            Opacity = state.Opacity,
            ClickThrough = state.ClickThrough,
            LockAspectRatio = state.LockAspectRatio,
            Topmost = IsTopmostForSharing(),
            WindowX = position.X,
            WindowY = position.Y,
            WindowWidth = Math.Min(width, 4000),
            WindowHeight = Math.Min(height, 4000)
        });
    }

    public void ShowAll()
    {
        foreach (var window in _windows)
        {
            window.Topmost = true;

            if (!window.IsVisible)
            {
                window.Show();
            }

            window.Activate();
        }
    }

    public void CloseAll()
    {
        _suppressSync = true;
        try
        {
            foreach (var window in _windows.ToArray())
            {
                window.Close();
            }

            _windows.Clear();
        }
        finally
        {
            _suppressSync = false;
        }

        _app.Config.OpenPreviews.Clear();
        _app.ScheduleConfigSave();
    }

    public void SetTopmost(bool topmost)
    {
        foreach (var window in _windows)
        {
            window.Topmost = topmost;
        }
    }

    private static bool IsTopmostForSharing()
    {
        // Chrome/Meet excludes floating-level macOS windows from its sharing list.
        return !OperatingSystem.IsMacOS();
    }

    public void SetClickThroughForAll(bool enabled)
    {
        foreach (var window in _windows)
        {
            window.SetClickThrough(enabled);
        }
    }

    public void PersistCurrentWindows()
    {
        _app.Config.OpenPreviews = _windows.Select(static window => window.CreateState()).ToList();
    }

    public void CloseAllForExit()
    {
        _suppressSync = true;
        try
        {
            foreach (var window in _windows.ToArray())
            {
                window.Close();
            }

            _windows.Clear();
        }
        finally
        {
            _suppressSync = false;
        }
    }

    private PreviewWindow? OpenInternal(PreviewOpenOptions options)
    {
        try
        {
            var window = new PreviewWindow(options);
            window.Closed += OnWindowClosed;
            window.LayoutChanged += OnWindowLayoutChanged;
            _windows.Add(window);
            window.Show();

            SyncToConfig();
            _app.ScheduleConfigSave();
            return window;
        }
        catch (Exception ex)
        {
            Logger.Error("创建预览窗口失败。", ex);
            return null;
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not PreviewWindow window)
        {
            return;
        }

        window.Closed -= OnWindowClosed;
        window.LayoutChanged -= OnWindowLayoutChanged;
        _windows.Remove(window);
        Logger.Info($"预览管理器：窗口关闭，剩余 {_windows.Count} 个。");

        if (!_suppressSync)
        {
            SyncToConfig();
            _app.ScheduleConfigSave();
        }
    }

    private void OnWindowLayoutChanged()
    {
        if (_suppressSync)
        {
            return;
        }

        SyncToConfig();
        _app.ScheduleConfigSave();
    }

    private void SyncToConfig()
    {
        _app.Config.OpenPreviews = _windows.Select(static window => window.CreateState()).ToList();
    }

    private static (double Width, double Height, PixelPoint Position) CalculateGeometry(
        PixelRect region,
        MonitorInfo monitor)
    {
        var scaling = monitor.Scaling;
        var work = monitor.WorkArea;

        var width = region.Width / scaling;
        var height = region.Height / scaling;

        var maxWidth = Math.Max(160, work.Width / scaling * 0.7);
        var maxHeight = Math.Max(100, work.Height / scaling * 0.7);

        var fit = Math.Min(1.0, Math.Min(maxWidth / width, maxHeight / height));
        width = Math.Max(120, width * fit);
        height = Math.Max(70, height * fit);

        var pixelWidth = (int)Math.Ceiling(width * scaling);
        var pixelHeight = (int)Math.Ceiling(height * scaling);
        var gap = (int)Math.Ceiling(12 * scaling);

        var candidates = new[]
        {
            new PixelRect(region.X + region.Width + gap, region.Y, pixelWidth, pixelHeight),
            new PixelRect(region.X, region.Y + region.Height + gap, pixelWidth, pixelHeight),
            new PixelRect(region.X - pixelWidth - gap, region.Y, pixelWidth, pixelHeight),
            new PixelRect(region.X, region.Y - pixelHeight - gap, pixelWidth, pixelHeight)
        };

        PixelPoint position;

        var fitting = candidates.FirstOrDefault(candidate => IsInside(work, candidate));
        if (fitting.Width > 0)
        {
            position = fitting.Position;
        }
        else
        {
            var centeredX = work.X + Math.Max(0, (work.Width - pixelWidth) / 2);
            var centeredY = work.Y + Math.Max(0, (work.Height - pixelHeight) / 2);
            position = new PixelPoint(
                Math.Clamp(centeredX, work.X, work.X + Math.Max(0, work.Width - pixelWidth)),
                Math.Clamp(centeredY, work.Y, work.Y + Math.Max(0, work.Height - pixelHeight)));
        }

        return (
            Math.Round(width),
            Math.Round(height),
            monitor.PhysicalToWindowPosition(position));
    }

    private static bool IsInside(PixelRect container, PixelRect candidate)
    {
        return candidate.X >= container.X
            && candidate.Y >= container.Y
            && candidate.X + candidate.Width <= container.X + container.Width
            && candidate.Y + candidate.Height <= container.Y + container.Height;
    }
}
