using Avalonia.Controls;
using RegionLivePreview.Platform.Mac;
using RegionLivePreview.Platform.Windows;

namespace RegionLivePreview.Platform;

public static class WindowInterop
{
    public static string GetNativeBounds(Window window)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return "no-handle";
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsWindowInterop.GetNativeBounds(window);
        }

        if (OperatingSystem.IsMacOS())
        {
            var bounds = MacNative.GetWindowBounds(handle);
            return bounds is { } value
                ? $"{value.X},{value.Y} {value.Width}x{value.Height}"
                : "GetWindowBounds-failed";
        }

        return "n/a";
    }

    public static bool SetClickThrough(Window window, bool enabled)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsWindowInterop.SetClickThrough(window, enabled);
        }

        return OperatingSystem.IsMacOS()
            && MacNative.SetWindowClickThrough(handle, enabled);
    }
}
