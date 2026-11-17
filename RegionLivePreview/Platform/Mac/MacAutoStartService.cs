using System.Security;
using System.Text;
using RegionLivePreview.Core;

namespace RegionLivePreview.Platform.Mac;

internal static class MacAutoStartService
{
    private const string Label = "com.regionlivepreview.app";

    private static string LaunchAgentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library",
        "LaunchAgents",
        Label + ".plist");

    public static bool TrySetEnabled(bool enabled, out string? error)
    {
        error = null;
        if (!OperatingSystem.IsMacOS())
        {
            error = "当前平台暂不支持开机自启。";
            return false;
        }

        try
        {
            if (!enabled)
            {
                if (File.Exists(LaunchAgentPath))
                {
                    File.Delete(LaunchAgentPath);
                }

                Logger.Info("已关闭 macOS 登录自启。");
                return true;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            {
                error = "无法确定可执行文件路径，登录自启设置失败。";
                return false;
            }

            var directory = Path.GetDirectoryName(LaunchAgentPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                error = "无法确定 LaunchAgent 目录。";
                return false;
            }

            Directory.CreateDirectory(directory);
            var plist = BuildPlist(executable);
            var tempPath = LaunchAgentPath + ".tmp";
            File.WriteAllText(
                tempPath,
                plist,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, LaunchAgentPath, overwrite: true);
            Logger.Info($"已开启 macOS 登录自启：{LaunchAgentPath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("设置 macOS 登录自启失败。", ex);
            error = "设置登录自启失败，请检查 ~/Library/LaunchAgents 的写入权限。";
            return false;
        }
    }

    private static string BuildPlist(string executable)
    {
        var appIndex = executable.IndexOf(
            ".app/Contents/MacOS/",
            StringComparison.OrdinalIgnoreCase);

        IReadOnlyList<string> arguments;
        if (appIndex > 0)
        {
            var appPath = executable[..(appIndex + 4)];
            arguments = new[] { "/usr/bin/open", "-g", "-a", appPath, "--args", "--autostart" };
        }
        else
        {
            arguments = new[] { executable, "--autostart" };
        }

        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
        builder.AppendLine("<plist version=\"1.0\">");
        builder.AppendLine("<dict>");
        builder.AppendLine("  <key>Label</key>");
        builder.AppendLine($"  <string>{Label}</string>");
        builder.AppendLine("  <key>ProgramArguments</key>");
        builder.AppendLine("  <array>");
        foreach (var argument in arguments)
        {
            builder.AppendLine($"    <string>{SecurityElement.Escape(argument)}</string>");
        }
        builder.AppendLine("  </array>");
        builder.AppendLine("  <key>RunAtLoad</key>");
        builder.AppendLine("  <true/>");
        builder.AppendLine("  <key>ProcessType</key>");
        builder.AppendLine("  <string>Interactive</string>");
        builder.AppendLine("</dict>");
        builder.AppendLine("</plist>");
        return builder.ToString();
    }
}
