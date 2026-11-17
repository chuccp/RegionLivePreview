namespace RegionLivePreview.Platform.Mac;

internal static class MacScreenCaptureAccess
{
    public static bool EnsureAccess(out string? error)
    {
        error = null;
        if (!OperatingSystem.IsMacOS() || MacNative.HasScreenCaptureAccess())
        {
            return true;
        }

        if (MacNative.RequestScreenCaptureAccess())
        {
            return true;
        }

        error = "需要在“系统设置 → 隐私与安全性 → 屏幕与系统音频录制”中允许 RegionLivePreview。"
            + "授权后请重新启动程序。";
        return false;
    }
}
