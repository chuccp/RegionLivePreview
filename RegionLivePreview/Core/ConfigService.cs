using System.Text;
using System.Text.Json;

namespace RegionLivePreview.Core;

public sealed class ConfigService
{
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _options;

    public ConfigService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        ConfigDirectory = Path.Combine(appData, "RegionLivePreview");
        ConfigPath = Path.Combine(ConfigDirectory, "config.json");

        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }

    public string ConfigDirectory { get; }

    public string ConfigPath { get; }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new AppConfig();
            }

            var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
            var config = JsonSerializer.Deserialize<AppConfig>(json, _options) ?? new AppConfig();
            config.Normalize();
            return config;
        }
        catch (Exception ex)
        {
            Logger.Error("读取配置失败，已使用默认设置。", ex);
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                var tempPath = ConfigPath + ".tmp";
                var json = JsonSerializer.Serialize(config, _options);
                File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Move(tempPath, ConfigPath, overwrite: true);
            }
            catch (Exception ex)
            {
                Logger.Error("保存配置失败。", ex);
            }
        }
    }
}
