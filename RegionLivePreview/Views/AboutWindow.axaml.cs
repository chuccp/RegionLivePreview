using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RegionLivePreview.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Icon = UiAssets.AppIcon;

        if (OperatingSystem.IsMacOS())
        {
            this.FindControl<TextBlock>("PlatformText")!.Text =
                "macOS 14+（Apple Silicon / Intel）";
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
