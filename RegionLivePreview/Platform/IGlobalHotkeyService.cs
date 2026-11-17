using RegionLivePreview.Core;

namespace RegionLivePreview.Platform;

internal interface IGlobalHotkeyService : IDisposable
{
    event Action? Pressed;

    HotkeyDefinition? Current { get; }

    bool TryRegister(HotkeyDefinition definition, out string? error);
}

internal static class GlobalHotkeyServiceFactory
{
    public static IGlobalHotkeyService Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new Windows.WindowsGlobalHotkeyService();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new Mac.MacGlobalHotkeyService();
        }

        return new UnsupportedGlobalHotkeyService();
    }

    private sealed class UnsupportedGlobalHotkeyService : IGlobalHotkeyService
    {
        public event Action? Pressed
        {
            add { }
            remove { }
        }

        public HotkeyDefinition? Current => null;

        public bool TryRegister(HotkeyDefinition definition, out string? error)
        {
            error = "当前平台暂不支持全局快捷键。";
            return false;
        }

        public void Dispose()
        {
        }
    }
}
