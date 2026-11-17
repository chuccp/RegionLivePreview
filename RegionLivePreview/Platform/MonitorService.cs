using System.Runtime.InteropServices;
using Avalonia;
using RegionLivePreview.Core;
using RegionLivePreview.Platform.Mac;
using RegionLivePreview.Platform.Windows;

namespace RegionLivePreview.Platform;

public sealed record MonitorInfo(
    int X,
    int Y,
    int Width,
    int Height,
    int WorkX,
    int WorkY,
    int WorkWidth,
    int WorkHeight,
    double Scaling,
    bool IsPrimary,
    long NativeId = 0,
    int? WindowOriginX = null,
    int? WindowOriginY = null)
{
    public PixelRect Bounds => new(X, Y, Width, Height);

    public PixelRect WorkArea => new(WorkX, WorkY, WorkWidth, WorkHeight);

    public PixelPoint WindowPosition => new(WindowOriginX ?? X, WindowOriginY ?? Y);

    public PixelPoint PhysicalToWindowPosition(PixelPoint point)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return point;
        }

        return new PixelPoint(
            WindowPosition.X + (int)Math.Round((point.X - X) / Scaling),
            WindowPosition.Y + (int)Math.Round((point.Y - Y) / Scaling));
    }

    public PixelPoint WindowToPhysicalPosition(PixelPoint point)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return point;
        }

        return new PixelPoint(
            X + (int)Math.Round((point.X - WindowPosition.X) * Scaling),
            Y + (int)Math.Round((point.Y - WindowPosition.Y) * Scaling));
    }

    public bool Contains(PixelPoint point)
    {
        return point.X >= X && point.X < X + Width && point.Y >= Y && point.Y < Y + Height;
    }
}

public static class MonitorService
{
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        if (OperatingSystem.IsMacOS())
        {
            return GetMacMonitors();
        }

        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<MonitorInfo>();
        }

        var monitors = new List<MonitorInfo>();

        try
        {
            var callback = new NativeMethods.MonitorEnumDelegate((hMonitor, _, _, _) =>
            {
                var info = new NativeMethods.MONITORINFO
                {
                    cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
                };

                if (!NativeMethods.GetMonitorInfoW(hMonitor, ref info))
                {
                    return true;
                }

                var dpi = 96u;
                try
                {
                    if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
                    {
                        dpi = dpiX;
                    }
                }
                catch (DllNotFoundException)
                {
                    // 极旧的系统没有 Shcore，按 96 DPI 处理。
                }

                monitors.Add(new MonitorInfo(
                    info.rcMonitor.Left,
                    info.rcMonitor.Top,
                    Math.Max(1, info.rcMonitor.Right - info.rcMonitor.Left),
                    Math.Max(1, info.rcMonitor.Bottom - info.rcMonitor.Top),
                    info.rcWork.Left,
                    info.rcWork.Top,
                    Math.Max(1, info.rcWork.Right - info.rcWork.Left),
                    Math.Max(1, info.rcWork.Bottom - info.rcWork.Top),
                    dpi / 96.0,
                    (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));
                return true;
            });

            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Logger.Error("枚举显示器失败。", ex);
        }

        if (monitors.Count == 0)
        {
            var width = Math.Max(1, NativeMethods.GetSystemMetrics(0));
            var height = Math.Max(1, NativeMethods.GetSystemMetrics(1));
            monitors.Add(new MonitorInfo(0, 0, width, height, 0, 0, width, height, 1.0, true));
        }

        return monitors
            .OrderByDescending(static m => m.IsPrimary)
            .ThenBy(static m => m.X)
            .ThenBy(static m => m.Y)
            .ToArray();
    }

    private static IReadOnlyList<MonitorInfo> GetMacMonitors()
    {
        try
        {
            return MacNative.GetScreens()
                .Select(static screen => new MonitorInfo(
                    screen.X,
                    screen.Y,
                    screen.Width,
                    screen.Height,
                    screen.WorkX,
                    screen.WorkY,
                    screen.WorkWidth,
                    screen.WorkHeight,
                    screen.Scaling > 0 ? screen.Scaling : 1.0,
                    screen.IsPrimary != 0,
                    screen.DisplayId,
                    screen.WindowX,
                    screen.WindowY))
                .OrderByDescending(static monitor => monitor.IsPrimary)
                .ThenBy(static monitor => monitor.X)
                .ThenBy(static monitor => monitor.Y)
                .ToArray();
        }
        catch (Exception ex)
        {
            Logger.Error("枚举 macOS 显示器失败。", ex);
            return Array.Empty<MonitorInfo>();
        }
    }

    public static MonitorInfo? Primary => GetMonitors().FirstOrDefault(static m => m.IsPrimary) ?? GetMonitors().FirstOrDefault();

    public static MonitorInfo? GetMonitorFromPoint(PixelPoint point)
    {
        return GetMonitors().FirstOrDefault(m => m.Contains(point));
    }

    public static MonitorInfo? GetMonitorForRegion(PixelRect region)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            return null;
        }

        MonitorInfo? best = null;
        long bestArea = 0;

        foreach (var monitor in GetMonitors())
        {
            var left = Math.Max(region.X, monitor.X);
            var top = Math.Max(region.Y, monitor.Y);
            var right = Math.Min(region.X + region.Width, monitor.X + monitor.Width);
            var bottom = Math.Min(region.Y + region.Height, monitor.Y + monitor.Height);

            if (right <= left || bottom <= top)
            {
                continue;
            }

            var area = (long)(right - left) * (bottom - top);
            if (area > bestArea)
            {
                bestArea = area;
                best = monitor;
            }
        }

        return best ?? Primary;
    }

    public static bool IsPointOnAnyMonitor(PixelPoint point)
    {
        return GetMonitors().Any(m => m.Contains(point));
    }
}
