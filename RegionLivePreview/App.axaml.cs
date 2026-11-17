using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using RegionLivePreview.Core;
using RegionLivePreview.Platform;
using RegionLivePreview.Platform.Windows;
using RegionLivePreview.Services;
using RegionLivePreview.Views;

namespace RegionLivePreview;

public partial class App : Application
{
    private readonly ConfigService _configService = new();
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _newSelectionItem;
    private GlobalHotkeyService? _hotkeyService;
    private PreviewManager? _previewManager;
    private SelectionCoordinator? _selection;
    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;
    private DispatcherTimer? _saveTimer;
    private bool _exiting;

    public AppConfig Config { get; private set; } = new();

    public ConfigService ConfigService => _configService;

    public PreviewManager Previews => _previewManager!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += OnDesktopExit;

            Config = _configService.Load();
            Logger.Enabled = Config.LoggingEnabled;
            Logger.Info("RegionLivePreview 启动。");

            _previewManager = new PreviewManager(this);
            _selection = new SelectionCoordinator();
            _selection.SelectionCompleted += OnSelectionCompleted;
            _selection.Cancelled += OnSelectionCancelled;

            InitializeTray();
            InitializeHotkey();
            SyncAutoStart();

            if (OperatingSystem.IsWindows())
            {
                RestorePreviews();
            }

            Dispatcher.UIThread.Post(() =>
            {
                ApplyStartupArguments();
                AutoStartSelectionIfNeeded();
            }, DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void BeginSelection()
    {
        if (_exiting || _selection is null || _previewManager is null)
        {
            return;
        }

        if (_selection.IsActive)
        {
            _selection.BringToFront();
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            MessageDialog.Show(
                "暂不支持",
                "当前版本仅提供 Windows 屏幕捕获。macOS 屏幕捕获后端将在后续版本提供。");
            return;
        }

        _previewManager.SetTopmost(false);

        if (!_selection.Begin())
        {
            _previewManager.SetTopmost(true);
            MessageDialog.Show("无法开始选区", "未检测到可用显示器，请稍后重试。");
        }
    }

    public void ShowAllPreviews()
    {
        if (_previewManager is null)
        {
            return;
        }

        if (_previewManager.Count == 0)
        {
            MessageDialog.Show("没有预览窗口", "按快捷键或使用托盘菜单“新建选区”创建一个实时预览窗口。");
            return;
        }

        _previewManager.ShowAll();
    }

    public void CloseAllPreviews()
    {
        _previewManager?.CloseAll();
    }

    public void ToggleClickThroughForAll()
    {
        if (_previewManager is null)
        {
            return;
        }

        var enable = !_previewManager.AnyClickThrough;
        _previewManager.SetClickThroughForAll(enable);
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ShowAbout()
    {
        if (_aboutWindow is { IsVisible: true })
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow();
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    public bool TryApplySettings(AppConfig newConfig, out string? error)
    {
        error = null;
        newConfig.Normalize();

        var previous = Config;
        var autoStartChanged = newConfig.AutoStart != previous.AutoStart;

        if (autoStartChanged && !AutoStartService.TrySetEnabled(newConfig.AutoStart, out error))
        {
            return false;
        }

        var hotkeyChanged = !newConfig.Hotkey.IsSameAs(previous.Hotkey)
            || _hotkeyService?.Current is null;

        if (hotkeyChanged && _hotkeyService is not null)
        {
            if (!_hotkeyService.TryRegister(newConfig.Hotkey, out error))
            {
                if (autoStartChanged)
                {
                    AutoStartService.TrySetEnabled(previous.AutoStart, out _);
                }

                return false;
            }
        }

        Config = newConfig;
        Logger.Enabled = Config.LoggingEnabled;
        UpdateTrayHotkeyLabel();
        ScheduleConfigSave();
        Logger.Info("设置已保存。");
        return true;
    }

    public void ScheduleConfigSave()
    {
        if (_exiting)
        {
            return;
        }

        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(700)
            };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer?.Stop();
                SaveConfigNow();
            };
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveConfigNow()
    {
        _configService.Save(Config);
    }

    public void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        Logger.Info("用户退出程序。");

        try
        {
            _previewManager?.PersistCurrentWindows();
            SaveConfigNow();
            _previewManager?.CloseAllForExit();
        }
        catch (Exception ex)
        {
            Logger.Error("退出时保存配置失败。", ex);
        }

        Cleanup();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void InitializeTray()
    {
        _newSelectionItem = new NativeMenuItem { Header = "新建选区" };
        _newSelectionItem.Click += (_, _) => BeginSelection();

        var showAllItem = new NativeMenuItem { Header = "显示所有预览窗口" };
        showAllItem.Click += (_, _) => ShowAllPreviews();

        var closeAllItem = new NativeMenuItem { Header = "关闭全部预览窗口" };
        closeAllItem.Click += (_, _) => CloseAllPreviews();

        var clickThroughItem = new NativeMenuItem { Header = "切换所有窗口鼠标穿透" };
        clickThroughItem.Click += (_, _) => ToggleClickThroughForAll();

        var settingsItem = new NativeMenuItem { Header = "设置…" };
        settingsItem.Click += (_, _) => ShowSettings();

        var aboutItem = new NativeMenuItem { Header = "关于 RegionLivePreview" };
        aboutItem.Click += (_, _) => ShowAbout();

        var exitItem = new NativeMenuItem { Header = "退出" };
        exitItem.Click += (_, _) => ExitApplication();

        var menu = new NativeMenu();
        menu.Items.Add(_newSelectionItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(showAllItem);
        menu.Items.Add(closeAllItem);
        menu.Items.Add(clickThroughItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(aboutItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            Icon = UiAssets.AppIcon,
            ToolTipText = "RegionLivePreview — 区域实时悬浮预览",
            Menu = menu,
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => BeginSelection();

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private void InitializeHotkey()
    {
        _hotkeyService = new GlobalHotkeyService();
        _hotkeyService.Pressed += BeginSelection;

        if (_hotkeyService.TryRegister(Config.Hotkey, out var error))
        {
            UpdateTrayHotkeyLabel();
            return;
        }

        Logger.Warn(error ?? "全局快捷键注册失败。");
        Dispatcher.UIThread.Post(() => MessageDialog.Show(
            "快捷键不可用",
            (error ?? "全局快捷键注册失败。") + "\n\n可以在托盘菜单中打开“设置”更换快捷键。"));
    }

    private void SyncAutoStart()
    {
        // 同步注册表路径，避免程序被移动后仍指向旧位置。
        if (!AutoStartService.TrySetEnabled(Config.AutoStart, out var error) && error is not null)
        {
            Logger.Warn("同步开机自启状态失败：" + error);
        }
    }

    private void RestorePreviews()
    {
        if (!Config.RestorePreviewsOnStartup || Config.OpenPreviews.Count == 0)
        {
            return;
        }

        foreach (var state in Config.OpenPreviews.ToList())
        {
            _previewManager?.Restore(state);
        }
    }

    private void AutoStartSelectionIfNeeded()
    {
        if (!Config.AutoOpenSelectionOnStartup || !OperatingSystem.IsWindows())
        {
            return;
        }

        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();

        // --autostart、--settings、--preview 等启动方式不自动弹出选区。
        if (args.Any(static arg => arg.StartsWith("--", StringComparison.Ordinal)))
        {
            return;
        }

        // 快捷键注册失败时已有错误提示，避免选区遮罩和错误弹窗叠加。
        if (_hotkeyService?.Current is null)
        {
            return;
        }

        BeginSelection();
    }

    private void ApplyStartupArguments()
    {
        var args = Environment.GetCommandLineArgs();

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--settings":
                    ShowSettings();
                    break;

                case "--about":
                    ShowAbout();
                    break;

                case "--preview":
                    if (i + 1 < args.Length && TryParseRegion(args[++i], out var region))
                    {
                        _previewManager?.OpenForRegion(region);
                    }
                    break;
            }
        }
    }

    private static bool TryParseRegion(string value, out PixelRect region)
    {
        region = default;
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4
            || !int.TryParse(parts[0], out var x)
            || !int.TryParse(parts[1], out var y)
            || !int.TryParse(parts[2], out var width)
            || !int.TryParse(parts[3], out var height)
            || width < 4
            || height < 4)
        {
            return false;
        }

        region = new PixelRect(x, y, width, height);
        return true;
    }

    private void OnSelectionCompleted(SelectionResult result)
    {
        _previewManager?.SetTopmost(true);
        Logger.Info($"选区完成：{result.Region.Width}x{result.Region.Height} @ {result.Region.X},{result.Region.Y}");

        // 等遮罩窗口从屏幕上消失后再启动捕获，避免首帧把遮罩拍进预览。
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            if (!_exiting)
            {
                _previewManager?.OpenForRegion(result.Region);
            }
        };
        timer.Start();
    }

    private void OnSelectionCancelled()
    {
        _previewManager?.SetTopmost(true);
    }

    private void UpdateTrayHotkeyLabel()
    {
        if (_newSelectionItem is not null)
        {
            _newSelectionItem.ToolTip = $"快捷键：{HotkeyFormatter.Format(Config.Hotkey)}";
        }
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (!_exiting)
        {
            _exiting = true;

            try
            {
                _previewManager?.PersistCurrentWindows();
                SaveConfigNow();
            }
            catch (Exception ex)
            {
                Logger.Error("退出时保存状态失败。", ex);
            }
        }

        Cleanup();
    }

    private void Cleanup()
    {
        _saveTimer?.Stop();
        _saveTimer = null;

        _hotkeyService?.Dispose();
        _hotkeyService = null;

        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        Logger.Info("程序退出。");
    }
}
