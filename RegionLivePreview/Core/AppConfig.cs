using System.Text.Json.Serialization;

namespace RegionLivePreview.Core;

public sealed class AppConfig
{
    public HotkeyDefinition Hotkey { get; set; } = new();

    public int DefaultFps { get; set; } = 30;

    public double DefaultOpacity { get; set; } = 1.0;

    public bool DefaultClickThrough { get; set; }

    /// <summary>捕获分辨率缩放：0.5 / 0.75 / 1.0。</summary>
    public double CaptureScale { get; set; } = 1.0;

    public bool AutoStart { get; set; }

    public bool LoggingEnabled { get; set; }

    public bool RestorePreviewsOnStartup { get; set; } = true;

    /// <summary>启动后是否自动进入选区模式。</summary>
    public bool AutoOpenSelectionOnStartup { get; set; } = true;

    public string Language { get; set; } = "zh-CN";

    public List<PreviewWindowState> OpenPreviews { get; set; } = new();

    public void Normalize()
    {
        if (DefaultFps is not (10 or 15 or 30 or 60))
        {
            DefaultFps = 30;
        }

        DefaultOpacity = Math.Clamp(DefaultOpacity, 0.3, 1.0);

        CaptureScale = CaptureScale switch
        {
            <= 0.60 => 0.5,
            <= 0.88 => 0.75,
            _ => 1.0
        };

        Language = "zh-CN";

        if (Hotkey is null)
        {
            Hotkey = new HotkeyDefinition();
        }

        Hotkey.Normalize();

        OpenPreviews ??= new List<PreviewWindowState>();
        OpenPreviews.RemoveAll(static x => x is null);
        foreach (var preview in OpenPreviews)
        {
            preview.Normalize();
        }

        OpenPreviews.RemoveAll(static x => !x.IsUsable);
    }
}

public sealed class PreviewWindowState
{
    public int RegionX { get; set; }

    public int RegionY { get; set; }

    public int RegionWidth { get; set; }

    public int RegionHeight { get; set; }

    public int WindowX { get; set; }

    public int WindowY { get; set; }

    public double WindowWidth { get; set; }

    public double WindowHeight { get; set; }

    public double Opacity { get; set; } = 1.0;

    public bool ClickThrough { get; set; }

    [JsonIgnore]
    public bool IsUsable => RegionWidth >= 4 && RegionHeight >= 4;

    public void Normalize()
    {
        Opacity = Math.Clamp(Opacity, 0.3, 1.0);

        if (!double.IsFinite(WindowWidth) || WindowWidth < 80)
        {
            WindowWidth = 0;
        }

        if (!double.IsFinite(WindowHeight) || WindowHeight < 50)
        {
            WindowHeight = 0;
        }
    }
}
