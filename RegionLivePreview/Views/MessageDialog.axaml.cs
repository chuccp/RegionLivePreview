using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace RegionLivePreview.Views;

public partial class MessageDialog : Window
{
    private readonly TextBlock _titleText;
    private readonly TextBlock _messageText;

    public MessageDialog()
    {
        InitializeComponent();
        _titleText = this.FindControl<TextBlock>("TitleText")!;
        _messageText = this.FindControl<TextBlock>("MessageText")!;
        Icon = UiAssets.AppIcon;
    }

    public static void Show(string title, string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ShowCore(title, message);
        }
        else
        {
            Dispatcher.UIThread.Post(() => ShowCore(title, message));
        }
    }

    private static void ShowCore(string title, string message)
    {
        var dialog = new MessageDialog
        {
            Title = title
        };
        dialog._titleText.Text = title;
        dialog._messageText.Text = message;
        dialog.Show();
        dialog.Activate();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
