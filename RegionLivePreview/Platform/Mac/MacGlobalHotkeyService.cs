using Avalonia.Threading;
using RegionLivePreview.Core;

namespace RegionLivePreview.Platform.Mac;

internal sealed class MacGlobalHotkeyService : IGlobalHotkeyService
{
    private readonly MacNative.HotkeyCallback _callback;
    private HotkeyDefinition? _current;
    private bool _disposed;

    public MacGlobalHotkeyService()
    {
        _callback = OnHotkeyPressed;
    }

    public event Action? Pressed;

    public HotkeyDefinition? Current => _current?.Clone();

    public bool TryRegister(HotkeyDefinition definition, out string? error)
    {
        error = null;
        if (_disposed)
        {
            error = "快捷键服务已关闭。";
            return false;
        }

        definition.Normalize();
        var keyCode = VirtualKeyMap.ToMacVirtualKey(definition.KeyCode);
        if (keyCode == 0)
        {
            error = "这个按键暂不支持 macOS 全局快捷键，请换一个组合键。";
            return false;
        }

        var modifiers = ToCarbonModifiers(definition.Modifiers);
        var previous = _current;
        _current = null;

        var status = MacNative.RegisterHotkey(keyCode, modifiers, _callback, out error);
        if (status == 0)
        {
            _current = definition.Clone();
            Logger.Info($"macOS 全局快捷键已注册：{HotkeyFormatter.Format(definition)}");
            return true;
        }

        if (previous is not null)
        {
            var previousKeyCode = VirtualKeyMap.ToMacVirtualKey(previous.KeyCode);
            if (previousKeyCode != 0
                && MacNative.RegisterHotkey(
                    previousKeyCode,
                    ToCarbonModifiers(previous.Modifiers),
                    _callback,
                    out _) == 0)
            {
                _current = previous;
            }
        }

        error = $"快捷键 {HotkeyFormatter.Format(definition)} 注册失败"
            + (string.IsNullOrWhiteSpace(error) ? "。" : $"：{error}");
        Logger.Warn(error);
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _current = null;
        MacNative.UnregisterHotkey();
    }

    private void OnHotkeyPressed(IntPtr context)
    {
        Dispatcher.UIThread.Post(() => Pressed?.Invoke());
    }

    private static uint ToCarbonModifiers(HotkeyModifiers modifiers)
    {
        const uint commandKey = 0x0100;
        const uint shiftKey = 0x0200;
        const uint optionKey = 0x0800;
        const uint controlKey = 0x1000;

        var result = 0u;
        if ((modifiers & HotkeyModifiers.Win) != 0) result |= commandKey;
        if ((modifiers & HotkeyModifiers.Shift) != 0) result |= shiftKey;
        if ((modifiers & HotkeyModifiers.Alt) != 0) result |= optionKey;
        if ((modifiers & HotkeyModifiers.Control) != 0) result |= controlKey;
        return result;
    }
}
