using System.Runtime.InteropServices;
using Avalonia.Threading;
using RegionLivePreview.Core;

namespace RegionLivePreview.Platform.Windows;

/// <summary>
/// 使用 Win32 RegisterHotKey 注册系统级快捷键。
/// 消息窗口使用消息专用（HWND_MESSAGE）窗口，不占用任务栏。
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0xB15;

    private readonly object _gate = new();
    private IntPtr _window;
    private NativeMethods.WndProcDelegate? _wndProc;
    private HotkeyDefinition? _current;
    private bool _disposed;

    public event Action? Pressed;

    public HotkeyDefinition? Current
    {
        get
        {
            lock (_gate)
            {
                return _current?.Clone();
            }
        }
    }

    public bool TryRegister(HotkeyDefinition definition, out string? error)
    {
        error = null;

        if (!OperatingSystem.IsWindows())
        {
            error = "当前平台暂不支持全局快捷键。";
            return false;
        }

        definition.Normalize();

        lock (_gate)
        {
            if (_disposed)
            {
                error = "快捷键服务已关闭。";
                return false;
            }

            if (!EnsureWindow())
            {
                error = "无法创建全局快捷键消息窗口。";
                return false;
            }

            var previous = _current;
            if (previous is not null)
            {
                NativeMethods.UnregisterHotKey(_window, HotkeyId);
                _current = null;
            }

            if (TryRegisterNative(definition))
            {
                _current = definition.Clone();
                Logger.Info($"全局快捷键已注册：{HotkeyFormatter.Format(definition)}");
                return true;
            }

            var win32Error = Marshal.GetLastWin32Error();

            // 注册失败时尽量恢复原来的快捷键。
            if (previous is not null && TryRegisterNative(previous))
            {
                _current = previous;
            }

            error = win32Error == 1409
                ? $"快捷键 {HotkeyFormatter.Format(definition)} 已被其他程序占用，请更换组合键。"
                : $"快捷键注册失败（Win32 错误码 {win32Error}），请尝试其他组合键。";
            Logger.Warn(error);
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_window != IntPtr.Zero)
            {
                if (_current is not null)
                {
                    NativeMethods.UnregisterHotKey(_window, HotkeyId);
                    _current = null;
                }

                NativeMethods.DestroyWindow(_window);
                _window = IntPtr.Zero;
            }

            _wndProc = null;
        }
    }

    private bool EnsureWindow()
    {
        if (_window != IntPtr.Zero)
        {
            return true;
        }

        _wndProc = WndProc;
        var module = NativeMethods.GetModuleHandleW(null);
        _window = NativeMethods.CreateWindowExW(
            0,
            "STATIC",
            "RegionLivePreviewHotkeyWindow",
            0,
            0,
            0,
            0,
            0,
            NativeMethods.HwndMessage,
            IntPtr.Zero,
            module,
            IntPtr.Zero);

        if (_window == IntPtr.Zero)
        {
            return false;
        }

        var previousProc = NativeMethods.SetWindowLongPtr(
            _window,
            NativeMethods.GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_wndProc));

        if (previousProc == IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_window);
            _window = IntPtr.Zero;
            _wndProc = null;
            return false;
        }

        return true;
    }

    private bool TryRegisterNative(HotkeyDefinition definition)
    {
        var modifiers = (uint)definition.Modifiers | NativeMethods.MOD_NOREPEAT;
        return NativeMethods.RegisterHotKey(_window, HotkeyId, modifiers, (uint)definition.KeyCode);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Dispatcher.UIThread.Post(() => Pressed?.Invoke());
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
    }
}
