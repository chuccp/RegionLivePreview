using System.Text;
using RegionLivePreview.Core;

namespace RegionLivePreview.Platform.Windows;

/// <summary>
/// 开机自启：写入 HKCU\Software\Microsoft\Windows\CurrentVersion\Run。
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RegionLivePreview";

    public static bool TrySetEnabled(bool enabled, out string? error)
    {
        error = null;

        if (!OperatingSystem.IsWindows())
        {
            error = "当前平台暂不支持开机自启。";
            return false;
        }

        IntPtr key = IntPtr.Zero;
        try
        {
            var result = NativeMethods.RegOpenKeyExW(
                NativeMethods.HkeyCurrentUser,
                RunKeyPath,
                0,
                NativeMethods.KEY_SET_VALUE,
                out key);

            if (result != 0)
            {
                error = $"无法打开开机自启注册表项（错误码 {result}）。";
                return false;
            }

            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                {
                    error = "无法确定可执行文件路径，开机自启设置失败。";
                    return false;
                }

                var value = $"\"{executable}\"";
                var bytes = Encoding.Unicode.GetBytes(value + '\0');
                result = NativeMethods.RegSetValueExW(
                    key,
                    ValueName,
                    0,
                    NativeMethods.REG_SZ,
                    bytes,
                    (uint)bytes.Length);

                if (result != 0)
                {
                    error = $"写入开机自启设置失败（错误码 {result}）。";
                    return false;
                }

                Logger.Info("已开启开机自启。");
            }
            else
            {
                // 不存在时返回 2（ERROR_FILE_NOT_FOUND），视为成功。
                result = NativeMethods.RegDeleteValueW(key, ValueName);
                if (result is not (0 or 2))
                {
                    error = $"移除开机自启设置失败（错误码 {result}）。";
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("设置开机自启失败。", ex);
            error = "设置开机自启失败，请检查系统权限。";
            return false;
        }
        finally
        {
            if (key != IntPtr.Zero)
            {
                NativeMethods.RegCloseKey(key);
            }
        }
    }
}
