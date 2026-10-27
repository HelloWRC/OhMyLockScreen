using System.Globalization;
using OhMyLockScreen.Shared.Attributes.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Controls.Components;

[ComponentInfo(Id, "日期", FluentIcons.Common.Icon.Calendar, "显示本地日期。")]
[ComponentDefaultFeatures(typeof(DateDefaultFeaturesProvider))]
public sealed partial class DateComponent : DateComponentBase, ITextStyleTarget
{
    public const string Id = "omls.components.date";
    public FrameworkElement TextElement => TextContent;
    private static readonly CultureInfo DateCulture = CultureInfo.GetCultureInfo("zh-CN");
    private readonly TimeProvider _timeProvider;
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMinutes(1)
    };

    public string DisplayText { get; private set; } = "";

    public DateComponent(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        InitializeComponent();
        _timer.Tick += OnTick;
    }

    protected override void OnSettingsChanged() => Bindings.Update();

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Refresh();
        _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _timer.Stop();
    }

    private void OnTick(object? sender, object args) => Refresh();

    private void Refresh()
    {
        var text = _timeProvider.GetLocalNow().ToString("yyyy年M月d日 dddd", DateCulture);
        if (DisplayText != text)
        {
            DisplayText = text;
            OnPropertyChanged(nameof(DisplayText));
        }
    }
}
