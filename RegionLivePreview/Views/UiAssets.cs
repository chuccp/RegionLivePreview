using Avalonia.Controls;
using Avalonia.Platform;

namespace RegionLivePreview.Views;

public static class UiAssets
{
    private static WindowIcon? _appIcon;

    public static WindowIcon AppIcon
    {
        get
        {
            return _appIcon ??= new WindowIcon(
                AssetLoader.Open(new Uri("avares://RegionLivePreview/Assets/app-icon.png")));
        }
    }
}
