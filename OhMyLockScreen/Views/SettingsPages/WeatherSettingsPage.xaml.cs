using System.Globalization;
using System.ComponentModel;
using FluentIcons.Common;
using OhMyLockScreen.Services;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.Services.Weather;
using OhMyLockScreen.Shared.Attributes;
using OhMyLockScreen.Shared.Controls.Weather;
using OhMyLockScreen.Shared.Enums.SettingsPages;
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.Shared.Models.Weather;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Input;
using Muxc = Microsoft.UI.Xaml.Controls;

namespace OhMyLockScreen.Views.SettingsPages;

[SettingsPageInfo("weather", "天气", Icon.WeatherPartlyCloudyDay, SettingsPageCategory.Internal)]
public sealed partial class WeatherSettingsPage : SettingsPageBase
{
    private readonly SettingsService _settings = IAppHost.GetRequiredService<SettingsService>();
    private readonly WeatherService _weather = IAppHost.GetRequiredService<WeatherService>();
    private readonly CoreDispatcher _dispatcher;
    private CancellationTokenSource? _lifetime;
    private WeatherLocation? _renderedLocation;
    private IReadOnlyList<WeatherAlert> _renderedAlerts = [];
    private WeatherIconTemplateRegistryInfo[] _renderedTemplates = [];
    private (string? Code, WeatherIconTemplateRegistryInfo? Template)? _renderedIcon;
    private bool _rendering;
    private bool _locating;
    private bool _refreshing;
    private bool _showCoordinates;
    private bool _automaticMessageShown;

    private WeatherLocation Location => _settings.Settings.WeatherLocation ?? new();

    public WeatherSettingsPage()
    {
        _dispatcher = Dispatcher;
        InitializeComponent();
        PreviewSectionIcon.Content = WeatherVisuals.Icon(Icon.WeatherSunny, 16);
        LocationSectionIcon.Content = WeatherVisuals.Icon(Icon.Earth, 16);
        AppearanceSectionIcon.Content = WeatherVisuals.Icon(Icon.DocumentPageTopCenter, 16);
        PreviewHumidityIcon.Content = WeatherVisuals.Icon(Icon.Drop, 14, filled: true);
        PreviewAirIcon.Content = WeatherVisuals.Icon(Icon.LeafTwo, 14, filled: true);
        PreviewWindIcon.Content = WeatherVisuals.Icon(Icon.WeatherBlowingSnow, 14);
        RefreshButton.Content = WeatherVisuals.Icon(Icon.ArrowClockwise, 16);
        EditCityButton.Content = WeatherVisuals.Icon(Icon.Edit, 16);
        LocateButton.Content = WeatherVisuals.Icon(Icon.MyLocation, 16);
        var eye = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        eye.Children.Add(WeatherVisuals.Icon(Icon.Eye, 16));
        eye.Children.Add(new TextBlock { Text = "显示坐标" });
        RevealCoordinates.Content = eye;
        LocationExpander.HeaderIcon = WeatherVisuals.Icon(Icon.Earth, 20);
        CityPanel.HeaderIcon = WeatherVisuals.Icon(Icon.City, 20);
        CoordinatePanel.HeaderIcon = WeatherVisuals.Icon(Icon.Map, 20);
        AutomaticPanel.HeaderIcon = WeatherVisuals.Icon(Icon.LocationRipple, 20);
        IconStyleCard.HeaderIcon = WeatherVisuals.Icon(Icon.Icons, 20);
        AlertExpander.HeaderIcon = WeatherVisuals.Icon(Icon.FilterDismiss, 20);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_lifetime != null) return;
        _lifetime = new CancellationTokenSource();
        _weather.PropertyChanged += OnWeatherChanged;
        Render();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _weather.PropertyChanged -= OnWeatherChanged;
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _locating = false;
        _refreshing = false;
        _renderedLocation = null;
    }

    private async void OnWeatherChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_lifetime is { } lifetime) await UpdateViewAsync(lifetime.Token, Render);
    }

    private void Render()
    {
        var settings = _settings.Settings;
        var location = Location;
        var coordinates = location.Mode == WeatherLocationMode.Coordinates;
        var weatherInfo = _weather.LastWeatherInfo;
        _rendering = true;
        try
        {
            LocationMode.SelectedIndex = coordinates ? 1 : 0;
            CityPanel.Visibility = Visible(!coordinates);
            CoordinatePanel.Visibility = AutomaticPanel.Visibility = Visible(coordinates);
            SelectedCity.Text = location.City?.Name ?? WeatherCity.Beijing.Name;
            AutomaticLocation.IsOn = settings.AutoRefreshWeatherLocation;
            LocateButton.IsEnabled = !_locating;
            RenderCoordinates(location, weatherInfo?.Summary.CityName);
            var templates = IWeatherService.RegisteredTemplates.ToArray();
            if (!_renderedTemplates.SequenceEqual(templates))
            {
                _renderedTemplates = templates;
                IconStyle.ItemsSource = templates;
            }
            IconStyle.SelectedItem = _weather.SelectedWeatherIconTemplate;
            if (AlertExclusions.Text != (settings.ExcludedWeatherAlerts ?? string.Empty))
            {
                AlertExclusions.Text = settings.ExcludedWeatherAlerts ?? string.Empty;
            }
        }
        finally
        {
            _rendering = false;
        }

        var summary = weatherInfo?.Summary;
        var icon = (summary?.Condition, _weather.SelectedWeatherIconTemplate);
        if (_renderedIcon != icon)
        {
            PreviewIcon.Content = WeatherVisuals.Pack(_weather, icon.Item1, 52);
            _renderedIcon = icon;
        }
        SetMeasurement(TemperatureValue, TemperatureUnit, summary?.Temperature);
        SetMeasurement(HumidityValue, HumidityUnit, summary?.Humidity);
        SetMeasurement(WindValue, WindUnit, summary?.Wind);
        PreviewAir.Text = summary?.AirQuality ?? "—";
        PreviewUpdated.Text = summary == null ? string.Empty : $"更新于 {summary.UpdatedAt.ToLocalTime():HH:mm}";
        RefreshButton.Visibility = Visible(!_refreshing);
        RefreshProgress.Visibility = Visible(_refreshing);
        RefreshProgress.IsActive = _refreshing;
        RenderAlerts(_weather.IsWeatherRefreshed ? weatherInfo?.Alerts ?? [] : []);
        if (coordinates && settings.AutoRefreshWeatherLocation && !_automaticMessageShown && !_locating &&
            _weather.AutomaticMessage is { } message)
        {
            _automaticMessageShown = true;
            this.ShowWarningToast($"自动定位未完成。{message}");
        }
    }

    private void RenderCoordinates(WeatherLocation location, string? city)
    {
        CoordinateFields.IsHitTestVisible = _showCoordinates;
        Longitude.IsTabStop = Latitude.IsTabStop = _showCoordinates;
        var accessibility = _showCoordinates ? AccessibilityView.Control : AccessibilityView.Raw;
        AutomationProperties.SetAccessibilityView(Longitude, accessibility);
        AutomationProperties.SetAccessibilityView(Latitude, accessibility);
        CoordinateBlurOverlay.Visibility = Visible(!_showCoordinates);
        RevealCoordinates.Visibility = Visible(!_showCoordinates);
        RevealCoordinates.IsEnabled = !_locating;
        Longitude.IsEnabled = Latitude.IsEnabled = !_locating;
        if (_renderedLocation?.Coordinates != location.Coordinates || _renderedLocation == null)
        {
            Longitude.Text = FormatCoordinate(location.Coordinates?.Longitude);
            Latitude.Text = FormatCoordinate(location.Coordinates?.Latitude);
        }
        _renderedLocation = location;
        SetText(CoordinateCity, _showCoordinates ? city : null);

        static string FormatCoordinate(double? value) => value?.ToString("0.####", CultureInfo.CurrentCulture) ?? string.Empty;
    }

    private void RenderAlerts(IReadOnlyList<WeatherAlert> alerts)
    {
        PreviewAlerts.Visibility = Visible(alerts.Count > 0);
        if (_renderedAlerts.SequenceEqual(alerts)) return;
        _renderedAlerts = alerts;
        PreviewAlerts.Children.Clear();
        foreach (var alert in alerts)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            header.Children.Add(new WeatherAlertBadge(alert, WeatherAlertTextMode.Never));
            header.Children.Add(new TextBlock { Text = alert.Title, FontWeight = Windows.UI.Text.FontWeights.Medium, TextWrapping = TextWrapping.Wrap });
            var details = new TextBlock { Text = alert.Detail, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(36, 4, 8, 4) };
            if (Application.Current.Resources.TryGetValue("TextFillColorSecondaryBrush", out var brush) && brush is Windows.UI.Xaml.Media.Brush foreground)
            {
                details.Foreground = foreground;
            }
            PreviewAlerts.Children.Add(new Muxc.Expander { Header = header, Content = details, Margin = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(12, 8, 12, 8), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
    }

    private async void OnRefresh(object sender, RoutedEventArgs args)
    {
        if (_lifetime is not { } lifetime || _refreshing) return;
        var token = lifetime.Token;
        _refreshing = true;
        Render();
        var result = new WeatherQueryResult();
        try { result = await _weather.QueryWeatherWithResultAsync().WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        finally
        {
            await UpdateViewAsync(token, () =>
            {
                _refreshing = false;
                Render();
                if (result.IsPrecisionDegraded) this.ShowWarningToast($"此天气信息降级了坐标精度：{result.DegradedPrecision} 位小数");
                if (result.IsSuccess) this.ShowSuccessToast("天气刷新成功");
                else if (result.ErrorMessage is { } error) this.ShowErrorToast($"天气刷新失败：{error}");
            });
        }
    }

    private void OnEditCity(object sender, RoutedEventArgs args)
    {
        var drawer = new WeatherCitySearchDrawer();
        drawer.Initialize(_weather, Location.City?.Id, city =>
            _settings.Settings.WeatherLocation = Location with { Mode = WeatherLocationMode.City, City = city });
        OpenDrawer(drawer, useGlobalDataContext: true);
    }

    private void OnLocationModeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_rendering || _lifetime == null || LocationMode.SelectedIndex < 0) return;
        _settings.Settings.WeatherLocation = Location with { Mode = (WeatherLocationMode)LocationMode.SelectedIndex };
    }

    private void OnRevealCoordinates(object sender, RoutedEventArgs args)
    {
        _showCoordinates = true;
        _renderedLocation = null;
        Render();
        Longitude.Focus(FocusState.Programmatic);
    }

    private void OnCoordinatesLostFocus(object sender, RoutedEventArgs args) => SaveCoordinates(false);

    private void OnCoordinateKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter) return;
        args.Handled = true;
        SaveCoordinates(true);
    }

    private void SaveCoordinates(bool showError)
    {
        if (_locating || !_showCoordinates || _rendering || _lifetime == null || Location.Mode != WeatherLocationMode.Coordinates) return;
        if (!TryReadNumber(Latitude.Text, out var latitude) || !TryReadNumber(Longitude.Text, out var longitude) ||
            new WeatherCoordinates(latitude, longitude) is not { IsValid: true } coordinates)
        {
            if (showError || Latitude.Text.Length > 0 && Longitude.Text.Length > 0)
            {
                SetText(CoordinateStatus, "请输入有效的纬度（−90 至 90）和经度（−180 至 180）。");
            }
            return;
        }
        SetText(CoordinateStatus, null);
        _settings.Settings.WeatherLocation = Location with { Coordinates = coordinates.Round(4) };
    }

    private async void OnLocate(object sender, RoutedEventArgs args)
    {
        if (_lifetime is not { } lifetime || _locating) return;
        var token = lifetime.Token;
        _locating = true;
        _automaticMessageShown = true;
        Render();
        var toast = this.ShowToastRef(new OhMyLockScreen.Shared.Models.UI.ToastMessage("正在定位…") { AutoClose = false });
        var message = await _weather.UpdateFromSettingsAsync(_dispatcher, token).ConfigureAwait(false);
        await UpdateViewAsync(CancellationToken.None, () =>
        {
            toast.Close();
            if (token.IsCancellationRequested) return;
            _locating = false;
            Render();
            if (message == null) this.ShowSuccessToast("定位成功");
            else this.ShowErrorToast(message);
        });
    }

    private void OnAutomaticLocationChanged(object sender, RoutedEventArgs args)
    {
        if (!_rendering && _lifetime != null) _settings.Settings.AutoRefreshWeatherLocation = AutomaticLocation.IsOn;
    }

    private void OnIconStyleChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_rendering && _lifetime != null && IconStyle.SelectedItem is WeatherIconTemplateRegistryInfo entry)
        {
            _settings.Settings.WeatherIconId = entry.Id;
        }
    }

    private void OnAlertExclusionsChanged(object sender, TextChangedEventArgs args)
    {
        if (!_rendering && _lifetime != null) _settings.Settings.ExcludedWeatherAlerts = AlertExclusions.Text;
    }

    private async Task UpdateViewAsync(CancellationToken token, Action update)
    {
        try
        {
            if (!token.IsCancellationRequested)
            {
                await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { if (!token.IsCancellationRequested) update(); });
            }
        }
        catch (Exception exception) { System.Diagnostics.Debug.WriteLine($"无法更新天气设置页：{exception}"); }
    }

    private static bool TryReadNumber(string text, out double number) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out number) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    private static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static void SetText(TextBlock target, string? text)
    {
        target.Text = text ?? string.Empty;
        target.Visibility = Visible(!string.IsNullOrEmpty(text));
    }

    private static void SetMeasurement(Run value, Run unit, string? measurement)
    {
        var text = measurement ?? "—";
        var length = 0;
        while (length < text.Length && (char.IsAsciiDigit(text[length]) || text[length] is '+' or '-' or '.' or ',')) length++;
        value.Text = length == 0 ? text : text[..length];
        unit.Text = length == 0 ? string.Empty : text[length..];
    }
}
