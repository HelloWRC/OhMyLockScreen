// Layout adapted from ClassIsland's WeatherComponent.axaml, GPL-3.0.
using System.ComponentModel;
using System.Globalization;
using FluentIcons.Common;
using OhMyLockScreen.Controls.Weather;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.Weather;
using OhMyLockScreen.Shared.Attributes.Scene;
using OhMyLockScreen.Shared.Controls.Weather;
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Weather;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Toolkit = CommunityToolkit.WinUI.Controls;

namespace OhMyLockScreen.Controls.Components;

[ComponentInfo(Id, "天气简报", Icon.WeatherSunny, "显示当前的天气概况和气象预警。")]
[ComponentDefaultFeatures(typeof(WeatherDefaultFeaturesProvider))]
public sealed class WeatherComponent : ComponentBase<WeatherComponentSettings>, ITextStyleTarget
{
    public const string Id = "omls.components.weather";
    private readonly IWeatherService _weather;
    private readonly SettingsService _settingsService;
    private readonly CoreDispatcher _dispatcher;
    private readonly StackPanel _root = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _summary = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl _symbol = new() { VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Toolkit.WrapPanel _alerts = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalSpacing = 4,
        VerticalSpacing = 2,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _rainText = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _updated = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _rain = new()
    {
        Padding = new Thickness(8, 0, 8, 0), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _rainIcon = WeatherVisuals.Lucide("\ue092", 14);
    private Brush? _lastForeground;
    private WeatherComponentSettings? _observed;
    private IReadOnlyList<WeatherAlert> _shownAlerts = [];
    private WeatherAlertTextMode _shownAlertMode;
    private (WeatherSummaryKind Kind, string? Code, string? Wind, string? Aqi, WeatherIconTemplateRegistryInfo? Template)? _shownIcon;
    private Windows.UI.Color? _rainColor;
    private int _generation;
    private bool _loaded;

    public WeatherComponent(IWeatherService weather, SettingsService settingsService)
    {
        _weather = weather;
        _settingsService = settingsService;
        _dispatcher = Dispatcher;
        _summary.Children.Add(_symbol);
        _summary.Children.Add(_text);
        var rainContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        rainContent.Children.Add(_rainIcon);
        rainContent.Children.Add(_rainText);
        _rain.Child = rainContent;
        _root.Children.Add(_summary);
        _root.Children.Add(_alerts);
        _root.Children.Add(_rain);
        _root.Children.Add(_updated);
        Content = _root;
        ActualThemeChanged += (_, _) => { if (_loaded) Render(); };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public FrameworkElement TextElement => _text;

    protected override void OnSettingsChanged()
    {
        ObserveSettings();
        Render();
    }

    protected override void OnIsEditingChanged() => Render();

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        LayoutUpdated += OnForegroundLayoutUpdated;
        ++_generation;
        _weather.PropertyChanged += OnValueChanged;
        ObserveSettings();
        Render();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded)
        {
            return;
        }

        _loaded = false;
        LayoutUpdated -= OnForegroundLayoutUpdated;
        ++_generation;
        _weather.PropertyChanged -= OnValueChanged;
        ObserveSettings();
    }

    private void ObserveSettings()
    {
        if (_observed != null) _observed.PropertyChanged -= OnValueChanged;
        _observed = _loaded ? SettingsInternal as WeatherComponentSettings : null;
        if (_observed != null) _observed.PropertyChanged += OnValueChanged;
    }

    private async void OnValueChanged(object? sender, PropertyChangedEventArgs args)
    {
        var generation = _generation;
        try
        {
            if (_dispatcher.HasThreadAccess) Render();
            else await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { if (_loaded && generation == _generation) Render(); });
        }
        catch (Exception exception) { System.Diagnostics.Debug.WriteLine($"无法更新天气组件：{exception}"); }
    }

    private void Render()
    {
        if (SettingsInternal is not WeatherComponentSettings settings) return;
        var saved = _settingsService.Settings.LastWeatherInfo;
        var query = (_settingsService.Settings.WeatherLocation ?? new WeatherLocation()).Query;
        var reading = IsEditing ? WeatherPresentation.Sample : saved?.Location == query ? saved : null;
        var weather = reading?.Summary;
        var fresh = IsEditing || _weather.IsWeatherRefreshed;
        _summary.Visibility = Visible(settings.ShowSummary);
        var template = _weather.SelectedWeatherIconTemplate;
        var symbol = (settings.SummaryKind, weather?.Condition, weather?.WindDirection, weather?.AirQuality, template);
        if (_shownIcon != symbol)
        {
            _symbol.Content = CreateSymbol(settings.SummaryKind, weather);
            SetIndependentForeground(_symbol, ForegroundProperty);
            _shownIcon = symbol;
        }

        _text.Margin = settings.SummaryKind == WeatherSummaryKind.Wind ? new Thickness(0) : new Thickness(6, 0, 0, 0);
        _text.Text = (settings.SummaryKind switch
        {
            WeatherSummaryKind.Humidity => weather?.Humidity,
            WeatherSummaryKind.Wind => weather?.Wind,
            WeatherSummaryKind.AirQuality => weather?.AirQuality,
            WeatherSummaryKind.Pressure => weather?.Pressure,
            WeatherSummaryKind.FeelsLike => weather?.FeelsLike,
            _ => weather?.Temperature
        }) ?? "—";
        if (settings.SummaryKind == WeatherSummaryKind.Condition)
        {
            AutomationProperties.SetName(_text, $"{_weather.GetWeatherTextByCode(weather?.Condition)} {_text.Text}");
        }
        else
        {
            AutomationProperties.SetName(_text, _text.Text);
        }

        var alerts = settings.ShowAlerts && fresh ? reading?.Alerts ?? [] : [];
        if (_shownAlertMode != settings.AlertTextMode || !_shownAlerts.SequenceEqual(alerts))
        {
            _shownAlertMode = settings.AlertTextMode;
            _shownAlerts = alerts;
            _alerts.Children.Clear();
            foreach (var alert in alerts)
            {
                var badge = new WeatherAlertBadge(alert, settings.AlertTextMode);
                SetIndependentForeground(badge, ForegroundProperty);
                _alerts.Children.Add(badge);
            }
        }
        _alerts.Visibility = Visible(alerts.Count > 0);

        var rainMinutes = reading?.RainMinutes ?? 0;
        _rain.Visibility = Visible(settings.ShowRainTime && fresh && rainMinutes != 0);
        _rainText.Text = WeatherPresentation.RainText(rainMinutes, settings.ApproximateLongRainTime);
        if (_rain.Visibility == Visibility.Visible)
        {
            var rainColor = WeatherVisuals.RainColor(this);
            if (_rainColor != rainColor || _rain.Background is not AcrylicBrush)
            {
                _rainColor = rainColor;
                _rain.BorderBrush = WeatherVisuals.RainBrush(this);
                _rain.Background = WeatherVisuals.RainAcrylicBrush(this);
            }
        }

        _updated.Visibility = Visible(settings.ShowSummary && weather != null && !fresh);
        _updated.Text = weather != null && !fresh ? WeatherPresentation.UpdateText(weather) : string.Empty;
    }

    private void OnForegroundLayoutUpdated(object? sender, object args)
    {
        if (ReferenceEquals(_lastForeground, Foreground)) return;
        _lastForeground = Foreground;
        SetIndependentForeground(_text, TextBlock.ForegroundProperty);
        SetIndependentForeground(_updated, TextBlock.ForegroundProperty);
        SetIndependentForeground(_rainText, TextBlock.ForegroundProperty);
        SetIndependentForeground(_rainIcon, TextBlock.ForegroundProperty);
        SetIndependentForeground(_symbol, ForegroundProperty);
        foreach (var badge in _alerts.Children) SetIndependentForeground(badge, ForegroundProperty);
    }

    // Hiding a badge or replacing an icon disconnects a shared acrylic brush even on labels that stay visible.
    private void SetIndependentForeground(DependencyObject target, DependencyProperty property)
    {
        if (Foreground is AcrylicBrush brush)
        {
            target.SetValue(property, BrushEditorOperations.Copy(brush));
        }
        else
        {
            target.ClearValue(property);
        }
    }

    private FrameworkElement CreateSymbol(WeatherSummaryKind kind, WeatherSummary? weather)
    {
        if (kind is WeatherSummaryKind.Condition) return WeatherVisuals.Pack(_weather, weather?.Condition, 22);
        var icon = WeatherVisuals.Icon(kind switch
        {
            WeatherSummaryKind.Humidity => Icon.Drop,
            WeatherSummaryKind.Wind => Icon.WeatherBlowingSnow,
            WeatherSummaryKind.AirQuality => Icon.LeafTwo,
            WeatherSummaryKind.Pressure => Icon.Gauge,
            _ => Icon.Temperature
        }, 22, filled: kind != WeatherSummaryKind.Wind);
        if (kind == WeatherSummaryKind.AirQuality && WeatherVisuals.AqiBrush(this, weather?.AirQuality) is { } color)
        {
            icon.Foreground = color;
        }
        if (kind != WeatherSummaryKind.Wind) return icon;
        var direction = WeatherVisuals.Icon(Icon.ArrowUp, 22, filled: true);
        direction.Margin = new Thickness(6, 0, 0, 0);
        direction.RenderTransformOrigin = new Point(0.5, 0.5);
        if (double.TryParse(weather?.WindDirection, NumberStyles.Float, CultureInfo.InvariantCulture, out var angle) && double.IsFinite(angle))
        {
            direction.RenderTransform = new RotateTransform { Angle = (angle + 180) % 360 };
        }
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, direction } };
    }

    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class WeatherDefaultFeaturesProvider : IComponentDefaultFeaturesProvider
{
    public IEnumerable<FeatureInfo> CreateDefaultFeatures() => [TextDefaults.Size(16)];
}
