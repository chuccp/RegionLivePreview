using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RegionLivePreview.Core;

namespace RegionLivePreview.Views;

public partial class SettingsWindow : Window
{
    private readonly App _app;
    private readonly TextBox _hotkeyBox;
    private readonly ComboBox _fpsCombo;
    private readonly ComboBox _qualityCombo;
    private readonly Slider _opacitySlider;
    private readonly TextBlock _opacityLabel;
    private readonly CheckBox _defaultClickThroughCheck;
    private readonly CheckBox _defaultLockAspectRatioCheck;
    private readonly CheckBox _restorePreviewsCheck;
    private readonly CheckBox _autoOpenSelectionCheck;
    private readonly CheckBox _autoStartCheck;
    private readonly CheckBox _loggingCheck;
    private readonly TextBlock _hotkeyError;
    private readonly TextBlock _hotkeyHint;
    private readonly TextBlock _logPathHint;
    private readonly TextBlock _statusText;

    private HotkeyDefinition _pendingHotkey;
    private bool _recording;

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();
        Icon = UiAssets.AppIcon;

        _hotkeyBox = this.FindControl<TextBox>("HotkeyBox")!;
        _fpsCombo = this.FindControl<ComboBox>("FpsCombo")!;
        _qualityCombo = this.FindControl<ComboBox>("QualityCombo")!;
        _opacitySlider = this.FindControl<Slider>("OpacitySlider")!;
        _opacityLabel = this.FindControl<TextBlock>("OpacityLabel")!;
        _defaultClickThroughCheck = this.FindControl<CheckBox>("DefaultClickThroughCheck")!;
        _defaultLockAspectRatioCheck = this.FindControl<CheckBox>("DefaultLockAspectRatioCheck")!;
        _restorePreviewsCheck = this.FindControl<CheckBox>("RestorePreviewsCheck")!;
        _autoOpenSelectionCheck = this.FindControl<CheckBox>("AutoOpenSelectionCheck")!;
        _autoStartCheck = this.FindControl<CheckBox>("AutoStartCheck")!;
        _loggingCheck = this.FindControl<CheckBox>("LoggingCheck")!;
        _hotkeyError = this.FindControl<TextBlock>("HotkeyError")!;
        _hotkeyHint = this.FindControl<TextBlock>("HotkeyHint")!;
        _logPathHint = this.FindControl<TextBlock>("LogPathHint")!;
        _statusText = this.FindControl<TextBlock>("StatusText")!;

        if (OperatingSystem.IsMacOS())
        {
            _hotkeyHint.Text = "点击快捷键框或“重新录制”，然后按下新的组合键；Esc 取消录制。"
                + "其中 Alt 显示为 Option，Win 显示为 Command。"
                + "为避免误触，建议使用至少一个修饰键。";
            _autoStartCheck.Content = "登录时自动启动（LaunchAgent）";
            _logPathHint.Text = "日志保存在 ~/Library/Application Support/RegionLivePreview/logs，"
                + "只记录运行错误，不包含任何画面内容。";
        }
        var config = app.Config;
        _pendingHotkey = config.Hotkey.Clone();

        _hotkeyBox.Text = HotkeyFormatter.Format(_pendingHotkey);
        _fpsCombo.SelectedIndex = config.DefaultFps switch
        {
            10 => 0,
            15 => 1,
            30 => 2,
            60 => 3,
            _ => 2
        };
        _qualityCombo.SelectedIndex = config.CaptureScale switch
        {
            <= 0.60 => 2,
            <= 0.88 => 1,
            _ => 0
        };
        _opacitySlider.Value = config.DefaultOpacity;
        _opacityLabel.Text = $"{config.DefaultOpacity:P0}";
        _defaultClickThroughCheck.IsChecked = config.DefaultClickThrough;
        _defaultLockAspectRatioCheck.IsChecked = config.DefaultLockAspectRatio;
        _restorePreviewsCheck.IsChecked = config.RestorePreviewsOnStartup;
        _autoOpenSelectionCheck.IsChecked = config.AutoOpenSelectionOnStartup;
        _autoStartCheck.IsChecked = config.AutoStart;
        _loggingCheck.IsChecked = config.LoggingEnabled;

        _hotkeyBox.AddHandler(
            InputElement.PointerPressedEvent,
            OnHotkeyPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _hotkeyBox.AddHandler(
            InputElement.KeyDownEvent,
            OnHotkeyKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        _opacitySlider.ValueChanged += (_, e) =>
        {
            _opacityLabel.Text = $"{Math.Clamp(e.NewValue, 0.3, 1.0):P0}";
        };
    }

    private void OnHotkeyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_recording)
        {
            BeginRecording();
        }

        e.Handled = true;
    }

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_recording)
        {
            return;
        }

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            _recording = false;
            _hotkeyBox.Text = HotkeyFormatter.Format(_pendingHotkey);
            HideHotkeyError();
            return;
        }

        var modifiers = ToHotkeyModifiers(e.KeyModifiers);

        if (VirtualKeyMap.IsModifierKey(e.Key))
        {
            var preview = FormatModifiers(modifiers);
            _hotkeyBox.Text = string.IsNullOrEmpty(preview) ? "请按下新的组合键…" : preview + " + …";
            return;
        }

        if (e.Key == Key.None)
        {
            return;
        }

        var virtualKey = VirtualKeyMap.FromAvaloniaKey(e.Key);
        if (virtualKey == 0)
        {
            ShowHotkeyError("暂不支持这个按键，请换一个组合键。");
            return;
        }

        var isFunctionKey = virtualKey is >= 0x70 and <= 0x87;
        if (modifiers == HotkeyModifiers.None && !isFunctionKey)
        {
            ShowHotkeyError(
                $"为避免误触，请至少按下一个修饰键（{HotkeyFormatter.ModifierHelp}），"
                + "或改用 F1–F12。");
            return;
        }

        _pendingHotkey = new HotkeyDefinition
        {
            Modifiers = modifiers,
            KeyCode = virtualKey
        };
        _hotkeyBox.Text = HotkeyFormatter.Format(_pendingHotkey);
        HideHotkeyError();
        _recording = false;
    }

    private void OnRecordHotkeyClick(object? sender, RoutedEventArgs e)
    {
        BeginRecording();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_recording)
        {
            ShowStatus("请先按下完整组合键，或按 Esc 取消录制。");
            return;
        }

        var newConfig = new AppConfig
        {
            Hotkey = _pendingHotkey.Clone(),
            DefaultFps = _fpsCombo.SelectedIndex switch
            {
                0 => 10,
                1 => 15,
                2 => 30,
                3 => 60,
                _ => 30
            },
            CaptureScale = _qualityCombo.SelectedIndex switch
            {
                0 => 1.0,
                1 => 0.75,
                2 => 0.5,
                _ => 1.0
            },
            DefaultOpacity = Math.Clamp(_opacitySlider.Value, 0.3, 1.0),
            DefaultClickThrough = _defaultClickThroughCheck.IsChecked == true,
            DefaultLockAspectRatio = _defaultLockAspectRatioCheck.IsChecked == true,
            AutoStart = _autoStartCheck.IsChecked == true,
            LoggingEnabled = _loggingCheck.IsChecked == true,
            RestorePreviewsOnStartup = _restorePreviewsCheck.IsChecked == true,
            AutoOpenSelectionOnStartup = _autoOpenSelectionCheck.IsChecked == true,
            Language = "zh-CN",
            OpenPreviews = _app.Config.OpenPreviews.ToList()
        };

        if (!_app.TryApplySettings(newConfig, out var error))
        {
            ShowStatus(error ?? "保存设置失败。");
            return;
        }

        Close();
    }

    private void BeginRecording()
    {
        _recording = true;
        _hotkeyBox.Text = "请按下新的组合键…";
        HideHotkeyError();
        _hotkeyBox.Focus();
    }

    private void ShowHotkeyError(string message)
    {
        _hotkeyError.Text = message;
        _hotkeyError.IsVisible = true;
    }

    private void HideHotkeyError()
    {
        _hotkeyError.IsVisible = false;
    }

    private void ShowStatus(string message)
    {
        _statusText.Text = message;
        _statusText.IsVisible = true;
    }

    private static HotkeyModifiers ToHotkeyModifiers(KeyModifiers modifiers)
    {
        var result = HotkeyModifiers.None;

        if ((modifiers & KeyModifiers.Control) != 0)
        {
            result |= HotkeyModifiers.Control;
        }

        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            result |= HotkeyModifiers.Alt;
        }

        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            result |= HotkeyModifiers.Shift;
        }

        if ((modifiers & KeyModifiers.Meta) != 0)
        {
            result |= HotkeyModifiers.Win;
        }

        return result;
    }

    private static string FormatModifiers(HotkeyModifiers modifiers)
    {
        var parts = new List<string>(4);
        if ((modifiers & HotkeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & HotkeyModifiers.Alt) != 0) parts.Add(OperatingSystem.IsMacOS() ? "Option" : "Alt");
        if ((modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((modifiers & HotkeyModifiers.Win) != 0) parts.Add(OperatingSystem.IsMacOS() ? "Command" : "Win");
        return string.Join(" + ", parts);
    }
}
