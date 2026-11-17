using System.Runtime.InteropServices;
using System.Text;

namespace RegionLivePreview.Platform.Mac;

internal static class MacNative
{
    private const string LibraryName = "RegionLivePreviewMac";

    [StructLayout(LayoutKind.Sequential)]
    public struct ScreenInfo
    {
        public uint DisplayId;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int WorkX;
        public int WorkY;
        public int WorkWidth;
        public int WorkHeight;
        public double Scaling;
        public int IsPrimary;
        public int WindowX;
        public int WindowY;
    }

    public delegate void HotkeyCallback(IntPtr context);

    public static IReadOnlyList<ScreenInfo> GetScreens()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return Array.Empty<ScreenInfo>();
        }

        try
        {
            var count = RLPGetScreens(null, 0);
            if (count <= 0)
            {
                return Array.Empty<ScreenInfo>();
            }

            var screens = new ScreenInfo[count];
            var actualCount = RLPGetScreens(screens, screens.Length);
            return actualCount == count
                ? screens
                : screens.AsSpan(0, Math.Max(0, actualCount)).ToArray();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Core.Logger.Error("加载 macOS 原生桥接库失败。", ex);
            return Array.Empty<ScreenInfo>();
        }
    }

    public static bool HasScreenCaptureAccess()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            return RLPPreflightScreenCaptureAccess() != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Core.Logger.Error("检查 macOS 屏幕录制权限失败。", ex);
            return false;
        }
    }

    public static bool RequestScreenCaptureAccess()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            return RLPRequestScreenCaptureAccess() != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Core.Logger.Error("请求 macOS 屏幕录制权限失败。", ex);
            return false;
        }
    }

    public static bool SetWindowClickThrough(IntPtr window, bool enabled)
    {
        return window != IntPtr.Zero && RLPSetWindowClickThrough(window, enabled ? 1 : 0) != 0;
    }

    public static (int X, int Y, int Width, int Height)? GetWindowBounds(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return null;
        }

        return RLPGetWindowBounds(window, out var x, out var y, out var width, out var height) != 0
            ? (x, y, width, height)
            : null;
    }

    public static int RegisterHotkey(
        uint keyCode,
        uint modifiers,
        HotkeyCallback callback,
        out string? error)
    {
        var buffer = new byte[1024];
        var status = RLPRegisterHotkey(
            keyCode,
            modifiers,
            callback,
            IntPtr.Zero,
            buffer,
            (UIntPtr)buffer.Length);
        error = status == 0 ? null : ReadError(buffer);
        return status;
    }

    public static void UnregisterHotkey()
    {
        RLPUnregisterHotkey();
    }

    public static IntPtr CreateCapture(
        uint displayId,
        double sourceX,
        double sourceY,
        double sourceWidth,
        double sourceHeight,
        int outputWidth,
        int outputHeight,
        int fps,
        out string? error)
    {
        var buffer = new byte[1024];
        var context = RLPSCKCreate(
            displayId,
            sourceX,
            sourceY,
            sourceWidth,
            sourceHeight,
            outputWidth,
            outputHeight,
            fps,
            buffer,
            (UIntPtr)buffer.Length);
        error = context == IntPtr.Zero ? ReadError(buffer) : null;
        return context;
    }

    public static bool StartCapture(IntPtr context, out string? error)
    {
        var buffer = new byte[1024];
        var result = RLPSCKStart(context, buffer, (UIntPtr)buffer.Length);
        error = result == 0 ? null : ReadError(buffer);
        return result == 0;
    }

    public static int CopyLatestFrame(
        IntPtr context,
        byte[] destination,
        out int width,
        out int height,
        out string? error)
    {
        var buffer = new byte[1024];
        var result = RLPSCKCopyLatestFrame(
            context,
            destination,
            destination.Length,
            out width,
            out height,
            buffer,
            (UIntPtr)buffer.Length);
        error = result < 0 ? ReadError(buffer) : null;
        return result;
    }

    public static void DestroyCapture(IntPtr context)
    {
        if (context != IntPtr.Zero)
        {
            RLPSCKDestroy(context);
        }
    }

    private static string ReadError(byte[] buffer)
    {
        var length = Array.IndexOf(buffer, (byte)0);
        if (length < 0)
        {
            length = buffer.Length;
        }

        return length == 0
            ? "未知原生错误。"
            : Encoding.UTF8.GetString(buffer, 0, length);
    }

    [DllImport(LibraryName, EntryPoint = "RLPGetScreens")]
    private static extern int RLPGetScreens(
        [Out] ScreenInfo[]? screens,
        int capacity);

    [DllImport(LibraryName, EntryPoint = "RLPPreflightScreenCaptureAccess")]
    private static extern int RLPPreflightScreenCaptureAccess();

    [DllImport(LibraryName, EntryPoint = "RLPRequestScreenCaptureAccess")]
    private static extern int RLPRequestScreenCaptureAccess();

    [DllImport(LibraryName, EntryPoint = "RLPSetWindowClickThrough")]
    private static extern int RLPSetWindowClickThrough(IntPtr window, int enabled);

    [DllImport(LibraryName, EntryPoint = "RLPGetWindowBounds")]
    private static extern int RLPGetWindowBounds(
        IntPtr window,
        out int x,
        out int y,
        out int width,
        out int height);

    [DllImport(LibraryName, EntryPoint = "RLPRegisterHotkey")]
    private static extern int RLPRegisterHotkey(
        uint keyCode,
        uint modifiers,
        HotkeyCallback callback,
        IntPtr context,
        [Out] byte[] errorBuffer,
        UIntPtr errorCapacity);

    [DllImport(LibraryName, EntryPoint = "RLPUnregisterHotkey")]
    private static extern void RLPUnregisterHotkey();

    [DllImport(LibraryName, EntryPoint = "RLPSCKCreate")]
    private static extern IntPtr RLPSCKCreate(
        uint displayId,
        double sourceX,
        double sourceY,
        double sourceWidth,
        double sourceHeight,
        int outputWidth,
        int outputHeight,
        int fps,
        [Out] byte[] errorBuffer,
        UIntPtr errorCapacity);

    [DllImport(LibraryName, EntryPoint = "RLPSCKStart")]
    private static extern int RLPSCKStart(
        IntPtr context,
        [Out] byte[] errorBuffer,
        UIntPtr errorCapacity);

    [DllImport(LibraryName, EntryPoint = "RLPSCKCopyLatestFrame")]
    private static extern int RLPSCKCopyLatestFrame(
        IntPtr context,
        [Out] byte[] destination,
        int destinationCapacity,
        out int width,
        out int height,
        [Out] byte[] errorBuffer,
        UIntPtr errorCapacity);

    [DllImport(LibraryName, EntryPoint = "RLPSCKDestroy")]
    private static extern void RLPSCKDestroy(IntPtr context);
}
