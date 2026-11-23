using System.ComponentModel;
using System.Globalization;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Attributes.Scene;
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
    public string HoursText { get; private set; } = "";
    public string MinutesText { get; private set; } = "";
    public string SecondsText { get; private set; } = "";
    public Visibility SecondsVisibility { get; private set; } = Visibility.Collapsed;
    public double SeparatorOpacity { get; private set; } = 1;
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
        if (IsLoaded)
        {
            return;
        }

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
        var showSeconds = Settings.ShowSeconds;
        var hours = now.ToString("HH", TimeCulture);
        var minutes = now.ToString("mm", TimeCulture);
        var seconds = showSeconds ? now.ToString("ss", TimeCulture) : "";
        var secondsVisibility = showSeconds ? Visibility.Visible : Visibility.Collapsed;
        var text = showSeconds ? $"{hours}:{minutes}:{seconds}" : $"{hours}:{minutes}";
        var separatorOpacity = Settings.FlashSeparator && now.Millisecond >= 500 ? 0d : 1d;

        if (HoursText != hours)
        {
            HoursText = hours;
            OnPropertyChanged(nameof(HoursText));
        }

        if (MinutesText != minutes)
        {
            MinutesText = minutes;
            OnPropertyChanged(nameof(MinutesText));
        }

        if (SecondsText != seconds)
        {
            SecondsText = seconds;
            OnPropertyChanged(nameof(SecondsText));
        }

        if (SecondsVisibility != secondsVisibility)
        {
            SecondsVisibility = secondsVisibility;
            OnPropertyChanged(nameof(SecondsVisibility));
        }

        if (DisplayText != text)
        {
            DisplayText = text;
            OnPropertyChanged(nameof(DisplayText));
        }

        if (SeparatorOpacity != separatorOpacity)
        {
            SeparatorOpacity = separatorOpacity;
            OnPropertyChanged(nameof(SeparatorOpacity));
        }
    }

    private HorizontalAlignment ToHorizontalAlignment(TextAlignment alignment) => alignment switch
    {
        TextAlignment.Center => HorizontalAlignment.Center,
        TextAlignment.Right => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Left
    };
}
