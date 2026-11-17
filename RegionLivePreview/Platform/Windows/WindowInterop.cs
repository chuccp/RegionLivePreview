using Avalonia.Controls;

namespace RegionLivePreview.Platform.Windows;

public static class WindowInterop
{
    public static string GetNativeBounds(Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "n/a";
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return "no-handle";
        }

        return NativeMethods.GetWindowRect(handle, out var rect)
            ? $"{rect.Left},{rect.Top} {rect.Right - rect.Left}x{rect.Bottom - rect.Top}"
            : "GetWindowRect-failed";
    }

    /// <summary>
    /// 切换窗口鼠标穿透（WS_EX_TRANSPARENT）。开启后点击会落到下层窗口。
    /// </summary>
    public static bool SetClickThrough(Window window, bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var stylePtr = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE);
        var style = stylePtr.ToInt64();

        if (enabled)
        {
            style |= NativeMethods.WS_EX_TRANSPARENT;
        }
        else
        {
            style &= ~NativeMethods.WS_EX_TRANSPARENT;
        }

        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
        NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);

        return true;
    }
}
