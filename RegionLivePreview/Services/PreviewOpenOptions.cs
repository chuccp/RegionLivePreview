using Avalonia;

namespace RegionLivePreview.Services;

public sealed class PreviewOpenOptions
{
    public PixelRect Region { get; init; }

    public int Fps { get; init; } = 30;

    public double CaptureScale { get; init; } = 1.0;

    public double Opacity { get; init; } = 1.0;

    public bool ClickThrough { get; init; }

    public bool LockAspectRatio { get; init; } = true;

    public bool Topmost { get; init; } = true;

    public int WindowX { get; init; }

    public int WindowY { get; init; }

    public double WindowWidth { get; init; }

    public double WindowHeight { get; init; }
}
