using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RegionLivePreview.Core;
using RegionLivePreview.Platform;
using RegionLivePreview.Services;

namespace RegionLivePreview.Views;

public partial class PreviewWindow : Window
{
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));

    private readonly PixelRect _region;
    private readonly double _aspectRatio;
    private readonly int _fps;
    private readonly double _captureScale;
    private readonly MonitorInfo? _monitor;

    private readonly Image _frameImage;
    private readonly Border _chromeBar;
    private readonly Button _optionsButton;
    private readonly Button _closeButton;
    private readonly Border _statusPanel;
    private readonly Border _errorPanel;
    private readonly TextBlock _errorText;

    private ToggleSwitch _clickThroughToggle = null!;
    private Slider _opacitySlider = null!;
    private TextBlock _opacityValue = null!;

    private ScreenCaptureService? _capture;
    private WriteableBitmap? _bitmap;
    private bool _clickThrough;
    private bool _hasFrame;
    private bool _closed;
    private bool _initializing = true;

    public event Action? LayoutChanged;

    public PreviewWindow(PreviewOpenOptions options)
    {
        InitializeComponent();

        _region = options.Region;
        _aspectRatio = Math.Max(1, options.Region.Width) / (double)Math.Max(1, options.Region.Height);
        _fps = options.Fps;
        _captureScale = options.CaptureScale;
        _monitor = MonitorService.GetMonitorForRegion(options.Region);

        Width = Math.Max(100, Math.Round(options.WindowWidth));
        Height = Math.Max(60, Math.Round(options.WindowHeight));
        Position = new PixelPoint(options.WindowX, options.WindowY);
        Topmost = options.Topmost;
        Opacity = Math.Clamp(options.Opacity, 0.3, 1.0);
        Title = $"RegionLivePreview 实时预览 {options.Region.Width}×{options.Region.Height}";
        Icon = UiAssets.AppIcon;

        _frameImage = this.FindControl<Image>("FrameImage")!;
        _chromeBar = this.FindControl<Border>("ChromeBar")!;
        _optionsButton = this.FindControl<Button>("OptionsButton")!;
        _closeButton = this.FindControl<Button>("CloseButton")!;
        _statusPanel = this.FindControl<Border>("StatusPanel")!;
        _errorPanel = this.FindControl<Border>("ErrorPanel")!;
        _errorText = this.FindControl<TextBlock>("ErrorText")!;

        _clickThrough = options.ClickThrough;
        _chromeBar.IsVisible = !_clickThrough;

        BuildOptionsFlyout();

        _closeButton.Click += OnCloseClick;
        Opened += OnOpened;
        Closed += OnClosed;
        PositionChanged += (_, _) => NotifyLayoutChanged();
        SizeChanged += (_, _) => NotifyLayoutChanged();

        _initializing = false;
    }

    public PixelRect CaptureRegion => _region;

    public bool IsClickThrough => _clickThrough;

    public void SetClickThrough(bool enabled)
    {
        ApplyClickThrough(enabled, notify: true);
    }

    public PreviewWindowState CreateState()
    {
        var physicalPosition = _monitor?.WindowToPhysicalPosition(Position) ?? Position;
        return new PreviewWindowState
        {
            RegionX = _region.X,
            RegionY = _region.Y,
            RegionWidth = _region.Width,
            RegionHeight = _region.Height,
            WindowX = physicalPosition.X,
            WindowY = physicalPosition.Y,
            WindowWidth = double.IsFinite(Width) ? Width : 0,
            WindowHeight = double.IsFinite(Height) ? Height : 0,
            Opacity = Opacity,
            ClickThrough = _clickThrough
        };
    }

    private void BuildOptionsFlyout()
    {
        _clickThroughToggle = new ToggleSwitch
        {
            IsChecked = _clickThrough,
            VerticalAlignment = VerticalAlignment.Center
        };

        _opacityValue = new TextBlock
        {
            Text = $"{Opacity:P0}",
            FontSize = 12,
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center
        };

        _opacitySlider = new Slider
        {
            Minimum = 0.3,
            Maximum = 1.0,
            TickFrequency = 0.05,
            IsSnapToTickEnabled = true,
            Value = Opacity
        };

        var closeButton = new Button
        {
            Content = "关闭此预览窗",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        closeButton.Click += (_, _) =>
        {
            _optionsButton.Flyout?.Hide();
            Close();
        };

        var panel = new StackPanel
        {
            Width = 238,
            Spacing = 10
        };

        panel.Children.Add(CreateLabeledRow("鼠标穿透", _clickThroughToggle));
        panel.Children.Add(new TextBlock
        {
            Text = "开启后鼠标点击会直接穿透到下层窗口；可在托盘菜单中统一关闭穿透。",
            FontSize = 11,
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new Separator { Height = 1 });
        panel.Children.Add(CreateLabeledRow("不透明度", _opacityValue));
        panel.Children.Add(_opacitySlider);
        panel.Children.Add(new Separator { Height = 1 });
        panel.Children.Add(closeButton);

        _optionsButton.Flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = panel
        };

        _clickThroughToggle.IsCheckedChanged += OnClickThroughToggleChanged;
        _opacitySlider.ValueChanged += OnOpacitySliderValueChanged;
    }

    private static Grid CreateLabeledRow(string labelText, Control trailing)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions
            {
                new() { Width = new GridLength(1, GridUnitType.Star) },
                new() { Width = GridLength.Auto }
            }
        };

        var label = new TextBlock
        {
            Text = labelText,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(trailing, 1);
        grid.Children.Add(label);
        grid.Children.Add(trailing);
        return grid;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_closed)
        {
            return;
        }

        Logger.Info($"预览窗口已打开：pos={Position} size={Width}x{Height} renderScaling={RenderScaling} native={WindowInterop.GetNativeBounds(this)} region={_region}");

        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            ShowCaptureError("当前平台暂不支持屏幕捕获。");
            return;
        }

        if (_clickThrough)
        {
            WindowInterop.SetClickThrough(this, true);
        }

        _capture?.Dispose();
        _capture = new ScreenCaptureService(_region, _fps, _captureScale, OnFrame, ShowCaptureError);
        _capture.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Logger.Info($"预览窗口 Closed：pos={Position}");
        _closed = true;
        _capture?.Dispose();
        _capture = null;
        _bitmap = null;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnClickThroughToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        ApplyClickThrough(_clickThroughToggle.IsChecked == true, notify: true);
    }

    private void OnOpacitySliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        var value = Math.Clamp(e.NewValue, 0.3, 1.0);
        Opacity = value;
        _opacityValue.Text = $"{value:P0}";
        NotifyLayoutChanged();
    }

    private void ApplyClickThrough(bool enabled, bool notify)
    {
        var changed = _clickThrough != enabled;
        _clickThrough = enabled;

        if (_clickThroughToggle.IsChecked != enabled)
        {
            _clickThroughToggle.IsChecked = enabled;
        }

        _chromeBar.IsVisible = !enabled;

        if (TryGetPlatformHandle() is not null)
        {
            WindowInterop.SetClickThrough(this, enabled);
        }

        if (enabled)
        {
            _optionsButton.Flyout?.Hide();
        }

        if (changed && notify)
        {
            NotifyLayoutChanged();
        }
    }

    private void OnDragSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        try
        {
            BeginMoveDrag(e);
        }
        catch
        {
            // 拖动失败不影响预览。
        }

        e.Handled = true;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (Math.Abs(e.Delta.Y) < 0.001)
        {
            return;
        }

        ZoomBy(Math.Pow(1.12, e.Delta.Y));
        e.Handled = true;
    }

    private void ZoomBy(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0)
        {
            return;
        }

        var oldWidth = Width;
        var oldHeight = Height;
        if (!double.IsFinite(oldWidth) || oldWidth <= 0 || !double.IsFinite(oldHeight) || oldHeight <= 0)
        {
            return;
        }

        const double maxWidth = 3600;
        const double maxHeight = 2400;

        var newWidth = Math.Clamp(oldWidth * factor, 120.0, maxWidth);
        var newHeight = newWidth / _aspectRatio;

        if (newHeight > maxHeight)
        {
            newHeight = maxHeight;
            newWidth = newHeight * _aspectRatio;
        }

        if (newHeight < 60)
        {
            newHeight = 60;
            newWidth = newHeight * _aspectRatio;
        }

        newWidth = Math.Clamp(newWidth, 100, maxWidth);

        if (Math.Abs(newWidth - oldWidth) < 0.5 && Math.Abs(newHeight - oldHeight) < 0.5)
        {
            return;
        }

        // Windows Position uses desktop pixels while macOS exposes window positions in logical points.
        var positionScale = OperatingSystem.IsMacOS()
            ? 1.0
            : RenderScaling > 0 ? RenderScaling : 1.0;
        var centerX = Position.X + oldWidth * positionScale / 2.0;
        var centerY = Position.Y + oldHeight * positionScale / 2.0;

        Width = newWidth;
        Height = newHeight;

        var centerOffsetX = newWidth * positionScale / 2.0;
        var centerOffsetY = newHeight * positionScale / 2.0;
        Position = new PixelPoint(
            (int)Math.Round(centerX - centerOffsetX),
            (int)Math.Round(centerY - centerOffsetY));

        NotifyLayoutChanged();
    }

    private void OnFrame(byte[] frame, PixelSize size)
    {
        if (_closed)
        {
            return;
        }

        if (_bitmap is null || _bitmap.PixelSize != size)
        {
            _bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            _frameImage.Source = _bitmap;
        }

        using (var buffer = _bitmap.Lock())
        {
            var rowBytes = size.Width * 4;
            if (buffer.RowBytes == rowBytes)
            {
                Marshal.Copy(frame, 0, buffer.Address, rowBytes * size.Height);
            }
            else
            {
                for (var y = 0; y < size.Height; y++)
                {
                    Marshal.Copy(
                        frame,
                        y * rowBytes,
                        IntPtr.Add(buffer.Address, y * buffer.RowBytes),
                        rowBytes);
                }
            }
        }

        _frameImage.InvalidateVisual();

        if (!_hasFrame)
        {
            _hasFrame = true;
            _statusPanel.IsVisible = false;
        }

        if (_errorPanel.IsVisible)
        {
            _errorPanel.IsVisible = false;
        }
    }

    private void ShowCaptureError(string message)
    {
        if (_closed)
        {
            return;
        }

        _statusPanel.IsVisible = false;
        _errorText.Text = message;
        _errorPanel.IsVisible = true;
    }

    private void NotifyLayoutChanged()
    {
        if (_initializing)
        {
            return;
        }

        LayoutChanged?.Invoke();
    }
}
