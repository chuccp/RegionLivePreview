using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RegionLivePreview.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Icon = UiAssets.AppIcon;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
