using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Shared.Models.Media;
using System.Diagnostics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;

namespace OhMyLockScreen.Shared.Controls.MediaControls;

public sealed class MediaController : MediaPartControl
{
    public static readonly DependencyProperty ActionProperty = DependencyProperty.Register(
        nameof(Action), typeof(MediaAction), typeof(MediaController),
        new PropertyMetadata(MediaAction.PlayPause, (sender, _) => ((MediaController)sender).UpdateState()));

    private readonly FontIcon _icon = new();
    private readonly Button _button = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    private bool _busy;
    private bool _active;
    private int _operationVersion;
    public IMediaStateService? Service { get; set; }
    public bool IsPlaceholder { get; set; }
    public MediaAction Action
    {
        get => (MediaAction)GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    public MediaController()
    {
        _button.Content = _icon;
        _button.Click += OnClick;
        _button.SetBinding(Control.ForegroundProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)), Mode = BindingMode.OneWay });
        _button.SetBinding(Control.FontSizeProperty, new Binding { Source = this, Path = new PropertyPath(nameof(FontSize)), Mode = BindingMode.OneWay });
        _icon.SetBinding(FontIcon.FontSizeProperty, new Binding { Source = this, Path = new PropertyPath(nameof(FontSize)), Mode = BindingMode.OneWay });
        Content = _button;
        Loaded += (_, _) =>
        {
            _active = true;
            UpdateState();
        };
        Unloaded += (_, _) =>
        {
            _active = false;
            _busy = false;
            ++_operationVersion;
        };
        UpdateState();
    }

    /// <summary>组合控件显式提供设计样式，独立控件保留系统默认按钮。</summary>
    public void SetButtonStyle(Style style) => _button.Style = style;

    protected override void UpdateState()
    {
        var (symbol, label, enabled) = Action switch
        {
            MediaAction.Previous => (Symbol.Previous, "上一首", State.CanPrevious),
            MediaAction.Next => (Symbol.Next, "下一首", State.CanNext),
            _ => (State.IsPlaying ? Symbol.Pause : Symbol.Play, State.IsPlaying ? "暂停" : "播放", State.CanPlayPause)
        };
        _icon.Glyph = ((char)symbol).ToString();
        _button.IsEnabled = _active && !_busy && !IsPlaceholder && State.HasSession && enabled;
        ToolTipService.SetToolTip(_button, label);
        AutomationProperties.SetName(_button, label);
    }

    private async void OnClick(object sender, RoutedEventArgs args)
    {
        if (!_active || _busy || IsPlaceholder || Service == null)
        {
            return;
        }

        _busy = true;
        var version = ++_operationVersion;
        UpdateState();
        try
        {
            try
            {
                await Service.ExecuteAsync(Action).ConfigureAwait(false);
            }
            finally
            {
                await RunOnViewAsync(() =>
                {
                    if (version != _operationVersion)
                    {
                        return;
                    }

                    _busy = false;
                    if (_active)
                    {
                        UpdateState();
                    }
                }).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法完成媒体控制：{exception}");
        }
    }
}
