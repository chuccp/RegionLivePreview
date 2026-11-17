using RegionLivePreview.Platform.Mac;
using RegionLivePreview.Platform.Windows;

namespace RegionLivePreview.Platform;

public static class AutoStartService
{
    public static bool TrySetEnabled(bool enabled, out string? error)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsAutoStartService.TrySetEnabled(enabled, out error);
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacAutoStartService.TrySetEnabled(enabled, out error);
        }

        error = "当前平台暂不支持开机自启。";
        return false;
    }
}
