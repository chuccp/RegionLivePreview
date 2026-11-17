using System.Text;

namespace RegionLivePreview.Core;

/// <summary>
/// 极简本地日志。默认关闭，只记录运行时错误，不记录画面内容。
/// </summary>
public static class Logger
{
    private static readonly object Gate = new();

    public static bool Enabled { get; set; }

    public static string LogDirectory
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "RegionLivePreview", "logs");
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
            var builder = new StringBuilder()
                .Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] [")
                .Append(level).Append("] ").AppendLine(message);

            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            lock (Gate)
            {
                File.AppendAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            // 日志失败不能影响主程序。
        }
    }
}
