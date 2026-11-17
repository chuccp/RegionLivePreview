using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using RegionLivePreview.Core;
using RegionLivePreview.Platform;
using RegionLivePreview.Platform.Mac;
using RegionLivePreview.Platform.Windows;

namespace RegionLivePreview.Services;

/// <summary>
/// Windows 使用 GDI BitBlt，macOS 使用 ScreenCaptureKit。
/// 捕获循环运行在后台线程，帧缓冲通过 Dispatcher 投递到 UI 线程，避免阻塞渲染线程。
/// </summary>
public sealed class ScreenCaptureService : IDisposable
{
    private readonly PixelRect _region;
    private readonly int _fps;
    private readonly double _scale;
    private readonly Action<byte[], PixelSize> _onFrame;
    private readonly Action<string> _onError;
    private readonly object _sync = new();
    private readonly Stack<byte[]> _freeBuffers = new();

    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private byte[]? _pending;
    private volatile bool _disposed;
    private int _errorReported;
    private int _started;

    public ScreenCaptureService(
        PixelRect region,
        int fps,
        double scale,
        Action<byte[], PixelSize> onFrame,
        Action<string> onError)
    {
        _region = region;
        _fps = Math.Clamp(fps, 1, 120);
        _scale = Math.Clamp(scale, 0.1, 1.0);
        _onFrame = onFrame;
        _onError = onError;

        var width = Math.Max(1, (int)Math.Round(region.Width * _scale));
        var height = Math.Max(1, (int)Math.Round(region.Height * _scale));
        OutputSize = new PixelSize(width, height);
    }

    public PixelSize OutputSize { get; }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            ReportError("当前平台暂不支持屏幕捕获。");
            return;
        }

        if (_region.Width <= 0 || _region.Height <= 0)
        {
            ReportError("所选区域无效，无法启动捕获。");
            return;
        }

        Logger.Info($"开始捕获：region={_region} output={OutputSize} fps={_fps} scale={_scale}");
        _cts = new CancellationTokenSource();
        var cancellation = _cts;
        _thread = new Thread(() =>
        {
            if (OperatingSystem.IsMacOS())
            {
                CaptureMacLoop(cancellation.Token);
            }
            else
            {
                CaptureWindowsLoop(cancellation.Token);
            }
        })
        {
            IsBackground = true,
            Name = "RegionLivePreview.ScreenCapture",
            Priority = ThreadPriority.BelowNormal
        };
        _thread.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts?.Cancel();

        var thread = _thread;
        if (thread is not null && thread != Thread.CurrentThread)
        {
            try
            {
                thread.Join(800);
            }
            catch
            {
                // 忽略退出等待异常。
            }
        }

        _cts?.Dispose();
        _cts = null;
        _thread = null;

        lock (_sync)
        {
            _pending = null;
            _freeBuffers.Clear();
        }
    }

    private void CaptureWindowsLoop(CancellationToken token)
    {
        var width = OutputSize.Width;
        var height = OutputSize.Height;
        var bufferLength = width * height * 4;

        IntPtr screenDc = IntPtr.Zero;
        IntPtr memoryDc = IntPtr.Zero;
        IntPtr dib = IntPtr.Zero;
        IntPtr previousObject = IntPtr.Zero;

        try
        {
            screenDc = NativeMethods.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
            {
                throw new InvalidOperationException("无法获取屏幕设备上下文（GetDC 失败）。");
            }

            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                throw new InvalidOperationException("无法创建捕获用的内存设备上下文。");
            }

            var bitmapInfo = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height, // 负高度表示自上而下的像素顺序
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                    biSizeImage = (uint)bufferLength
                }
            };

            dib = NativeMethods.CreateDIBSection(
                screenDc,
                ref bitmapInfo,
                0,
                out var pixelBits,
                IntPtr.Zero,
                0);

            if (dib == IntPtr.Zero || pixelBits == IntPtr.Zero)
            {
                throw new InvalidOperationException("无法创建捕获位图。");
            }

            previousObject = NativeMethods.SelectObject(memoryDc, dib);
            if (previousObject == IntPtr.Zero)
            {
                throw new InvalidOperationException("无法选择捕获位图。");
            }

            // COLORONCOLOR 比 HALFTONE 快得多，适合 30/60 fps 的实时预览。
            NativeMethods.SetStretchBltMode(memoryDc, NativeMethods.COLORONCOLOR);

            var frameMilliseconds = 1000.0 / _fps;
            var clock = Stopwatch.StartNew();
            var nextFrameAt = 0.0;

            while (!token.IsCancellationRequested)
            {
                bool hasPending;
                lock (_sync)
                {
                    hasPending = _pending is not null;
                }

                if (!hasPending)
                {
                    bool captured;
                    if (width == _region.Width && height == _region.Height)
                    {
                        captured = NativeMethods.BitBlt(
                            memoryDc,
                            0,
                            0,
                            width,
                            height,
                            screenDc,
                            _region.X,
                            _region.Y,
                            NativeMethods.SRCCOPY);
                    }
                    else
                    {
                        captured = NativeMethods.StretchBlt(
                            memoryDc,
                            0,
                            0,
                            width,
                            height,
                            screenDc,
                            _region.X,
                            _region.Y,
                            _region.Width,
                            _region.Height,
                            NativeMethods.SRCCOPY);
                    }

                    if (!captured)
                    {
                        ReportError("屏幕捕获失败，可能是显示器配置发生变化或系统限制了桌面捕获。程序会继续重试。");
                    }
                    else
                    {
                        var buffer = RentBuffer(bufferLength);
                        Marshal.Copy(pixelBits, buffer, 0, bufferLength);

                        lock (_sync)
                        {
                            _pending = buffer;
                        }

                        Dispatcher.UIThread.Post(ConsumePendingFrame);
                    }
                }

                nextFrameAt += frameMilliseconds;
                var now = clock.Elapsed.TotalMilliseconds;
                var wait = nextFrameAt - now;

                if (wait > 1)
                {
                    token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(Math.Min(wait, 1000)));
                }
                else if (wait < -200)
                {
                    nextFrameAt = now;
                }
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                Logger.Error("捕获线程异常退出。", ex);
                ReportError("捕获线程异常：" + ex.Message);
            }
        }
        finally
        {
            if (memoryDc != IntPtr.Zero && previousObject != IntPtr.Zero)
            {
                NativeMethods.SelectObject(memoryDc, previousObject);
            }

            if (dib != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(dib);
            }

            if (memoryDc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(memoryDc);
            }

            if (screenDc != IntPtr.Zero)
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    private void CaptureMacLoop(CancellationToken token)
    {
        var monitor = MonitorService.GetMonitorForRegion(_region);
        if (monitor is null)
        {
            ReportError("未找到所选区域所在的显示器，请重新选择预览区域。");
            return;
        }

        var screen = MacNative.GetScreens()
            .FirstOrDefault(candidate => candidate.DisplayId == monitor.NativeId);

        if (screen.DisplayId == 0)
        {
            ReportError("无法确定所选区域对应的 macOS 显示器，请重新选择预览区域。");
            return;
        }

        var scaling = monitor.Scaling > 0 ? monitor.Scaling : 1.0;
        var sourceX = (_region.X - monitor.X) / scaling;
        var sourceY = (_region.Y - monitor.Y) / scaling;
        var sourceWidth = _region.Width / scaling;
        var sourceHeight = _region.Height / scaling;

        IntPtr capture = IntPtr.Zero;
        try
        {
            capture = MacNative.CreateCapture(
                screen.DisplayId,
                sourceX,
                sourceY,
                sourceWidth,
                sourceHeight,
                OutputSize.Width,
                OutputSize.Height,
                _fps,
                out var createError);

            if (capture == IntPtr.Zero)
            {
                ReportError(FormatMacCaptureError(createError));
                return;
            }

            if (!MacNative.StartCapture(capture, out var startError))
            {
                ReportError(FormatMacCaptureError(startError));
                return;
            }

            var bufferLength = OutputSize.Width * OutputSize.Height * 4;
            var frameMilliseconds = 1000.0 / _fps;
            var clock = Stopwatch.StartNew();
            var nextFrameAt = 0.0;

            while (!token.IsCancellationRequested)
            {
                bool hasPending;
                lock (_sync)
                {
                    hasPending = _pending is not null;
                }

                if (!hasPending)
                {
                    var buffer = RentBuffer(bufferLength);
                    var result = MacNative.CopyLatestFrame(
                        capture,
                        buffer,
                        out _,
                        out _,
                        out var frameError);

                    if (result > 0)
                    {
                        lock (_sync)
                        {
                            _pending = buffer;
                        }

                        Dispatcher.UIThread.Post(ConsumePendingFrame);
                    }
                    else
                    {
                        lock (_sync)
                        {
                            if (_freeBuffers.Count < 3)
                            {
                                _freeBuffers.Push(buffer);
                            }
                        }

                        if (result < 0)
                        {
                            ReportError(FormatMacCaptureError(frameError));
                            break;
                        }
                    }
                }

                nextFrameAt += frameMilliseconds;
                var now = clock.Elapsed.TotalMilliseconds;
                var wait = nextFrameAt - now;

                if (wait > 1)
                {
                    token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(Math.Min(wait, 1000)));
                }
                else if (wait < -200)
                {
                    nextFrameAt = now;
                }
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                Logger.Error("ScreenCaptureKit 捕获线程异常退出。", ex);
                ReportError("ScreenCaptureKit 捕获异常：" + ex.Message);
            }
        }
        finally
        {
            MacNative.DestroyCapture(capture);
        }
    }

    private static string FormatMacCaptureError(string? error)
    {
        var permissionHint = MacNative.HasScreenCaptureAccess()
            ? string.Empty
            : "\n请在“系统设置 → 隐私与安全性 → 屏幕与系统音频录制”中授权，并重新启动程序。";
        return "ScreenCaptureKit 无法捕获画面："
            + (string.IsNullOrWhiteSpace(error) ? "未知错误。" : error)
            + permissionHint;
    }

    private byte[] RentBuffer(int length)
    {
        lock (_sync)
        {
            if (_freeBuffers.Count > 0)
            {
                var reusable = _freeBuffers.Pop();
                if (reusable.Length == length)
                {
                    return reusable;
                }
            }
        }

        return GC.AllocateUninitializedArray<byte>(length);
    }

    private void ConsumePendingFrame()
    {
        byte[]? frame;
        lock (_sync)
        {
            frame = _pending;
            _pending = null;
        }

        if (frame is null)
        {
            return;
        }

        try
        {
            if (!_disposed)
            {
                _onFrame(frame, OutputSize);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("渲染捕获帧失败。", ex);
        }
        finally
        {
            lock (_sync)
            {
                if (_freeBuffers.Count < 3 && frame.Length == OutputSize.Width * OutputSize.Height * 4)
                {
                    _freeBuffers.Push(frame);
                }
            }
        }
    }

    private void ReportError(string message)
    {
        Logger.Warn(message);

        if (Interlocked.Exchange(ref _errorReported, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                try
                {
                    _onError(message);
                }
                catch
                {
                    // 错误提示失败不能影响捕获线程。
                }
            }
        });
    }
}
