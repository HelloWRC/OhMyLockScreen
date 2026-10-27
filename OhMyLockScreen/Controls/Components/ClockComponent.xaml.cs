using System.ComponentModel;
using System.Globalization;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Attributes.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Controls.Components;

[ComponentInfo(Id, "时钟", FluentIcons.Common.Icon.Clock, "显示本地时间。")]
[ComponentDefaultFeatures(typeof(ClockDefaultFeaturesProvider))]
public sealed partial class ClockComponent : ClockComponentBase, ITextStyleTarget
{
    public const string Id = "omls.components.clock";
    public FrameworkElement TextElement => TextContent;
    private static readonly CultureInfo TimeCulture = CultureInfo.GetCultureInfo("zh-CN");
    private readonly TimeProvider _timeProvider;
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500)
    };
    private ClockComponentSettings? _observed;
    private bool _loaded;
    public string DisplayText { get; private set; } = "";

    public ClockComponent(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        InitializeComponent();
        _timer.Tick += OnTick;
    }

    protected override void OnSettingsChanged()
    {
        Bindings.Update();
        ObserveSettings();
        Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _loaded = true;
        ObserveSettings();
        Refresh();
        _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _loaded = false;
        _timer.Stop();
        ObserveSettings();
    }

    private void ObserveSettings()
    {
        if (_observed != null)
        {
            _observed.PropertyChanged -= OnPropertyChanged;
        }

        _observed = _loaded ? Settings : null;
        if (_observed != null)
        {
            _observed.PropertyChanged += OnPropertyChanged;
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args) => Refresh();
    private void OnTick(object? sender, object args) => Refresh();
    private void Refresh()
    {
        var now = _timeProvider.GetLocalNow();
        var text = now.ToString(Settings.ShowSeconds ? "HH:mm:ss" : "HH:mm", TimeCulture);
        if (Settings.FlashSeparator && now.Millisecond >= 500)
        {
            text = text.Replace(':', '\u2008');
        }

        if (DisplayText != text)
        {
            DisplayText = text;
            OnPropertyChanged(nameof(DisplayText));
        }
    }
}
