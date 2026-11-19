// Adapted from ClassIsland.Services.WeatherService, GPL-3.0.
// https://github.com/ClassIsland/ClassIsland/blob/fb3ad30221d4140bae52cb058439a0668a3cfcdd/ClassIsland/Services/WeatherService.cs
using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Models;
using OhMyLockScreen.Shared.Abstractions.Services.Weather;
using OhMyLockScreen.Shared.Models.Weather;
using Windows.Devices.Geolocation;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Services;

internal sealed class WeatherService : ObservableRecipient, IWeatherService, IHostedService, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _clock;
    private readonly ILogger<WeatherService> _logger;
    private readonly CoreDispatcher? _dispatcher;
    private DispatcherTimer? _updateTimer;
    private readonly CancellationTokenSource _shutdown = new();
    private Settings _observedSettings;
    private WeatherIconTemplateRegistryInfo? _selectedWeatherIconTemplate;
    private bool _isWeatherRefreshed;
    private bool _isPosUpdated;
    private bool _locating;
    private bool _started;
    private bool _disposed;
    private int _queryVersion;
    private string? _automaticMessage;

    private Settings Settings => _settingsService.Settings;
    public IReadOnlyList<WeatherStatusCode> WeatherStatusList => WeatherSymbols.StatusList;
    public WeatherIconTemplateRegistryInfo? SelectedWeatherIconTemplate
    {
        get => _selectedWeatherIconTemplate;
        private set => SetProperty(ref _selectedWeatherIconTemplate, value);
    }
    public bool IsWeatherRefreshed
    {
        get => _isWeatherRefreshed;
        set => SetProperty(ref _isWeatherRefreshed, value);
    }
    public bool IsPosUpdated
    {
        get => _isPosUpdated;
        set => SetProperty(ref _isPosUpdated, value);
    }
    public string? AutomaticMessage
    {
        get => _automaticMessage;
        private set => SetProperty(ref _automaticMessage, value);
    }
    public WeatherReading? LastWeatherInfo => Settings.LastWeatherInfo is { } data && data.Location == Location.Query ? data : null;
    private WeatherLocation Location => Settings.WeatherLocation ?? new WeatherLocation();

    public WeatherService(SettingsService settings, HttpClient httpClient, TimeProvider clock, ILogger<WeatherService> logger,
        CoreDispatcher? dispatcher = null)
    {
        _settingsService = settings;
        _httpClient = httpClient;
        _clock = clock;
        _logger = logger;
        _dispatcher = dispatcher ?? Window.Current?.Dispatcher;
        _observedSettings = settings.Settings;
        _observedSettings.PropertyChanged += OnSettingsChanged;
        _settingsService.PropertyChanged += OnSettingsReplaced;
        IWeatherService.RegisteredTemplates.CollectionChanged += OnIconTemplatesChanged;
        LoadWeatherIconTemplate();
    }

    public Task StartAsync(CancellationToken cancellationToken) => OnUiAsync(() =>
    {
        if (_disposed || _started) return;
        _started = true;
        if (_updateTimer == null)
        {
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            _updateTimer.Tick += OnUpdateTimerTick;
        }
        _updateTimer.Start();
        _ = QueryWeatherAsync();
    });

    public Task StopAsync(CancellationToken cancellationToken) => OnUiAsync(Stop);

    private void Stop()
    {
        _started = false;
        _updateTimer?.Stop();
    }

    private async void OnUpdateTimerTick(object? sender, object args) => await QueryWeatherAsync();

    public async Task QueryWeatherAsync() => await QueryWeatherWithResultAsync();

    public async Task<WeatherQueryResult> QueryWeatherWithResultAsync()
    {
        if (_disposed) return new();
        var version = Interlocked.Increment(ref _queryVersion);
        var query = Location.Query;
        if (!query.IsConfigured) return new();

        var result = new WeatherQueryResult();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            var (reading, precision) = await ReadWeatherAsync(query, timeout.Token).ConfigureAwait(false);
            await OnUiAsync(() =>
            {
                if (_disposed || version != _queryVersion || query != Location.Query) return;
                var exclusions = (Settings.ExcludedWeatherAlerts ?? string.Empty)
                    .Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var alerts = reading.Alerts
                    .Where(alert => !exclusions.Any(word => alert.Title.Contains(word, StringComparison.Ordinal) || alert.Detail.Contains(word, StringComparison.Ordinal)))
                    .GroupBy(alert => alert.Type, StringComparer.Ordinal)
                    .Select(group => group.MaxBy(alert => alert.PublishedAt)!).ToArray();
                Settings.LastWeatherInfo = reading with { Alerts = alerts };
                IsWeatherRefreshed = true;
                result = new WeatherQueryResult(true, IsPrecisionDegraded: precision < 4, DegradedPrecision: precision);
            });
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            var error = exception switch
            {
                OperationCanceledException => "天气查询超时。",
                HttpRequestException => "无法连接小米天气服务。",
                InvalidDataException dataError => dataError.Message,
                _ => "无法读取小米天气数据。"
            };
            _logger.LogWarning("天气刷新失败：{ErrorType}", exception.GetType().Name);
            await OnUiAsync(() =>
            {
                if (_disposed || version != _queryVersion || query != Location.Query) return;
                IsWeatherRefreshed = false;
                result = new WeatherQueryResult(ErrorMessage: error);
            });
        }
        return result;
    }

    public string GetWeatherTextByCode(string? code) => WeatherSymbols.ConditionText(code);

    public Task<List<WeatherCity>> GetCitiesByName(string text) => GetCitiesByName(text, CancellationToken.None);

    public async Task<List<WeatherCity>> GetCitiesByName(string text, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            return (await SearchProviderCitiesAsync(text, timeout.Token).ConfigureAwait(false)).ToList();
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            _logger.LogWarning("搜索城市失败：{ErrorType}", exception.GetType().Name);
            return [];
        }
    }

    public async Task TryUpdateAutomaticallyAsync(CoreDispatcher dispatcher, Func<bool> isWindowAvailable)
    {
        if (IsPosUpdated) return;
        IsPosUpdated = true;
        if (Settings.AutoRefreshWeatherLocation && Location.Mode == WeatherLocationMode.Coordinates)
        {
            var message = await UpdateLocationAsync(dispatcher, isWindowAvailable, _shutdown.Token).ConfigureAwait(false);
            await OnUiAsync(() => AutomaticMessage = message);
        }
    }

    public Task<string?> UpdateFromSettingsAsync(CoreDispatcher dispatcher, CancellationToken token) =>
        UpdateLocationAsync(dispatcher, () => !token.IsCancellationRequested, token);

    private async Task<string?> UpdateLocationAsync(CoreDispatcher dispatcher, Func<bool> isWindowAvailable, CancellationToken token)
    {
        if (_locating) return "正在获取当前位置。";
        _locating = true;
        try
        {
            var settings = Settings;
            var previous = Location;
            var position = await ReadWindowsPositionAsync(token).ConfigureAwait(false);
            if (!position.IsValid) return "Windows 返回了无效的位置。";
            var saved = false;
            await OnUiAsync(() =>
            {
                if (!_disposed && isWindowAvailable() && ReferenceEquals(settings, Settings) && previous == Location)
                {
                    settings.WeatherLocation = previous with { Mode = WeatherLocationMode.Coordinates, Coordinates = position.Round(4) };
                    AutomaticMessage = null;
                    saved = true;
                }
            }, dispatcher);
            return saved ? null : "天气位置已变化，请重新定位。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
        catch (WeatherPositionException exception) { return exception.Message; }
        catch (Exception exception)
        {
            _logger.LogWarning("Windows 定位失败：{ErrorType}", exception.GetType().Name);
            return "无法获取当前位置，请检查 Windows 定位设置后重试。";
        }
        finally { _locating = false; }
    }

    private void LoadWeatherIconTemplate()
    {
        var id = Settings.WeatherIconId ?? (Settings.WeatherIconStyle switch
        {
            WeatherIconStyle.Fluent => WeatherIconIds.FluentDesign,
            WeatherIconStyle.Text => WeatherIconIds.SimpleText,
            _ => WeatherIconIds.Lucide
        });
        SelectedWeatherIconTemplate = IWeatherService.RegisteredTemplates.FirstOrDefault(entry => entry.Id == id)
            ?? IWeatherService.RegisteredTemplates.FirstOrDefault(entry => entry.Id == WeatherIconIds.Lucide);
    }

    private void OnIconTemplatesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args) =>
        _ = OnUiAsync(() => { LoadWeatherIconTemplate(); OnPropertyChanged(nameof(IWeatherService.RegisteredTemplates)); });

    private void OnSettingsReplaced(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SettingsService.Settings)) return;
        _observedSettings.PropertyChanged -= OnSettingsChanged;
        _observedSettings = Settings;
        _observedSettings.PropertyChanged += OnSettingsChanged;
        ReloadView();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(Settings.WeatherIconId):
            case nameof(Settings.WeatherIconStyle):
                _ = OnUiAsync(LoadWeatherIconTemplate);
                break;
            case nameof(Settings.LastWeatherInfo):
                OnPropertyChanged(nameof(LastWeatherInfo));
                break;
            case nameof(Settings.WeatherLocation):
                ReloadView();
                break;
        }
    }

    private void ReloadView() => _ = OnUiAsync(() =>
    {
        if (_disposed) return;
        LoadWeatherIconTemplate();
        OnPropertyChanged(nameof(LastWeatherInfo));
        if (_started) _ = QueryWeatherAsync();
    });

    private async Task OnUiAsync(Action update, CoreDispatcher? dispatcher = null)
    {
        dispatcher ??= _dispatcher;
        if (dispatcher == null || dispatcher.HasThreadAccess) update();
        else await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => update());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = OnUiAsync(() =>
        {
            Stop();
            if (_updateTimer != null)
            {
                _updateTimer.Tick -= OnUpdateTimerTick;
                _updateTimer = null;
            }
        });
        _observedSettings.PropertyChanged -= OnSettingsChanged;
        _settingsService.PropertyChanged -= OnSettingsReplaced;
        IWeatherService.RegisteredTemplates.CollectionChanged -= OnIconTemplatesChanged;
        _shutdown.Cancel();
        _httpClient.Dispose();
        _shutdown.Dispose();
    }

    private const string Api = "https://weatherapi.market.xiaomi.com/wtr-v3/";
    private const string WeatherParameters = "&days=15&appKey=weather20151024&sign=zUFJoAR2ZVrDy1vF3D07&isGlobal=false&locale=zh_cn";

    private async Task<IReadOnlyList<WeatherCity>> SearchProviderCitiesAsync(string text, CancellationToken cancellationToken)
    {
        var uri = string.IsNullOrWhiteSpace(text)
            ? "location/city/hots?locale=zh_cn"
            : $"location/city/search?locale=zh_cn&name={Uri.EscapeDataString(text.Trim())}";
        var cities = await GetAsync<CityResponse[]>(uri, cancellationToken).ConfigureAwait(false);
        return cities.OfType<CityResponse>()
            .Where(city => !string.IsNullOrWhiteSpace(city.LocationKey) && !string.IsNullOrWhiteSpace(city.Name))
            .DistinctBy(city => city.LocationKey)
            .Select(city => new WeatherCity(city.LocationKey!, city.DisplayName)).ToArray();
    }

    private async Task<(WeatherReading Reading, int Precision)> ReadWeatherAsync(WeatherQuery query, CancellationToken cancellationToken)
    {
        var (city, coordinates, precision) = query.Coordinates is { } position
            ? await ResolveCoordinatesAsync(position, cancellationToken).ConfigureAwait(false)
            : await ResolveCityAsync(query.CityId!, cancellationToken).ConfigureAwait(false);
        var coordinatesQuery = FormatCoordinates(coordinates);
        var escapedId = Uri.EscapeDataString(city.LocationKey!);
        var data = await GetAsync<WeatherResponse>(
            $"weather/all?{coordinatesQuery}&locationKey={escapedId}{WeatherParameters}", cancellationToken).ConfigureAwait(false);
        if (data.Current is not { } current ||
            (string.IsNullOrWhiteSpace(current.Weather) && current.Temperature?.Display == null))
        {
            throw new InvalidDataException("天气服务缺少当前天气数据。");
        }

        var updatedAt = data.UpdateTime is > 0 and <= 253402300799999
            ? DateTimeOffset.FromUnixTimeMilliseconds(data.UpdateTime.Value)
            : ParseTime(current.PubTime) ?? _clock.GetUtcNow();
        var summary = new WeatherSummary(city.DisplayName, current.Weather, current.Temperature?.Display,
            current.Humidity?.Display, current.Wind?.Speed?.Display, data.Aqi?.Aqi,
            current.Pressure?.Display, current.FeelsLike?.Display, updatedAt, current.Wind?.Direction?.Value);
        var alerts = (data.Alerts ?? []).OfType<AlertResponse>().Where(alert => !string.IsNullOrWhiteSpace(alert.Title))
            .Select(CreateAlert).ToList().AsReadOnly();
        return (new WeatherReading(summary, alerts, GetRainMinutes(data.Minutely?.Precipitation?.Value), query), precision);
    }

    private async Task<(CityResponse City, WeatherCoordinates Coordinates, int Precision)> ResolveCityAsync(
        string cityId, CancellationToken cancellationToken)
    {
        var cities = await GetAsync<CityResponse[]>(
            $"location/city/info?locale=zh_cn&locationKey={Uri.EscapeDataString(cityId)}", cancellationToken).ConfigureAwait(false);
        var city = cities.OfType<CityResponse>().FirstOrDefault(candidate => candidate.LocationKey == cityId);
        if (city != null && double.TryParse(city.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) &&
            double.TryParse(city.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) &&
            new WeatherCoordinates(latitude, longitude) is { IsValid: true } coordinates)
        {
            return (city, coordinates, 4);
        }

        throw new InvalidDataException("无法读取所选城市的位置。");
    }

    private async Task<(CityResponse City, WeatherCoordinates Coordinates, int Precision)> ResolveCoordinatesAsync(
        WeatherCoordinates position, CancellationToken cancellationToken)
    {
        var tried = new HashSet<WeatherCoordinates>();
        for (var precision = 4; precision >= 0; precision--)
        {
            var multiplier = Math.Pow(10, precision);
            var rounded = position.Round(precision);
            var truncated = new WeatherCoordinates(Math.Truncate(position.Latitude * multiplier) / multiplier,
                Math.Truncate(position.Longitude * multiplier) / multiplier);
            foreach (var candidate in precision == 0 ? [rounded] : new[] { rounded, truncated })
            {
                if (!tried.Add(candidate))
                {
                    continue;
                }

                var cities = await GetAsync<CityResponse[]>(
                    $"location/city/geo?locale=zh_cn&{FormatCoordinates(candidate)}", cancellationToken).ConfigureAwait(false);
                var city = cities.OfType<CityResponse>()
                    .FirstOrDefault(item => item.Status == 0 && !string.IsNullOrWhiteSpace(item.LocationKey));
                if (city != null)
                {
                    return (city, candidate, precision);
                }
            }
        }

        throw new InvalidDataException("无法通过经纬度找到对应城市。");
    }

    private static string FormatCoordinates(WeatherCoordinates coordinates) =>
        FormattableString.Invariant($"latitude={coordinates.Latitude:0.########}&longitude={coordinates.Longitude:0.########}");

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        using var response = await _httpClient.GetAsync(Api + path, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("天气服务返回空响应。");
    }

    private static WeatherAlert CreateAlert(AlertResponse alert)
    {
        var type = string.IsNullOrWhiteSpace(alert.Type) ? alert.Title! : alert.Type;
        Uri? icon = null;
        if (alert.Images?.TryGetValue("icon", out var value) == true && Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme is "http" or "https") && IsAllowedXiaomiImageHost(uri.Host))
        {
            icon = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
        }

        var defaultIcon = icon?.Segments is [.., var folder, _] && folder.TrimEnd('/') is
            "0ac110d2ee20a454ab44f5df30f9fa6ff650e0b72" or "072013febeb1944da85649e5e547ec5a8284816a2"
            or "06db501333e6d4075a3364a66cdf23ba5733111b3" or "03e3e096d3d9e485fa33bbf833fc3b3c96c23d014";
        return new WeatherAlert(type, alert.Title!, alert.Detail ?? string.Empty, ParseTime(alert.PubTime) ?? DateTimeOffset.MinValue,
            icon, defaultIcon, string.IsNullOrWhiteSpace(alert.Level) ? null : alert.Level);
    }

    internal static bool IsAllowedXiaomiImageHost(string host) =>
        host.Equals("xiaomi.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".xiaomi.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("mi-img.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".mi-img.com", StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? date : null;

    internal static int GetRainMinutes(IReadOnlyList<double>? precipitation)
    {
        if (precipitation is not { Count: > 0 })
        {
            return 0;
        }

        var isRaining = precipitation[0] > 0;
        for (var minute = 1; minute < precipitation.Count; minute++)
        {
            if ((precipitation[minute] > 0) != isRaining)
            {
                return isRaining ? -minute : minute;
            }
        }

        return 0;
    }

    private sealed record CityResponse(string? LocationKey, string? Name, string? Affiliation,
        string? Latitude, string? Longitude, int Status = -1)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(Affiliation) ? Name ?? string.Empty : $"{Name}（{Affiliation}）";
    }

    private sealed record WeatherResponse(CurrentResponse? Current, AqiResponse? Aqi, List<AlertResponse?>? Alerts,
        MinutelyResponse? Minutely, long? UpdateTime);
    private sealed record CurrentResponse(string? Weather, Measurement? Temperature, Measurement? Humidity,
        WindResponse? Wind, Measurement? Pressure, Measurement? FeelsLike, string? PubTime);
    private sealed record WindResponse(Measurement? Speed, Measurement? Direction);
    private sealed record AqiResponse(string? Aqi);
    private sealed record AlertResponse(string? Type, string? Title, string? Detail, string? PubTime,
        Dictionary<string, string>? Images, string? Level = null);
    private sealed record MinutelyResponse(PrecipitationResponse? Precipitation);
    private sealed record PrecipitationResponse(List<double>? Value);
    private sealed record Measurement(string? Value, string? Unit)
    {
        public string? Display => string.IsNullOrWhiteSpace(Value) ? null : Value + Unit;
    }

    private static async Task<WeatherCoordinates> ReadWindowsPositionAsync(CancellationToken cancellationToken)
    {
        if (CoreWindow.GetForCurrentThread()?.ActivationMode != CoreWindowActivationMode.ActivatedInForeground)
        {
            throw new WeatherPositionException("请在前台窗口重新定位。");
        }

        var access = await Geolocator.RequestAccessAsync().AsTask(cancellationToken).ConfigureAwait(false);
        if (access != GeolocationAccessStatus.Allowed)
        {
            throw new WeatherPositionException("Windows 定位不可用。请开启定位服务并允许此应用访问位置。");
        }

        var locator = new Geolocator();
        try
        {
            var position = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(12))
                .AsTask(cancellationToken).ConfigureAwait(false);
            var point = position.Coordinate.Point.Position;
            return new WeatherCoordinates(point.Latitude, point.Longitude);
        }
        catch (Exception exception) when (exception.HResult == unchecked((int)0x800705B4) || exception is TimeoutException)
        {
            throw new WeatherPositionException("获取当前位置超时，请重试。");
        }
        catch (UnauthorizedAccessException)
        {
            throw new WeatherPositionException("Windows 定位权限已关闭。请检查 Windows 定位设置。");
        }
    }

    private sealed class WeatherPositionException(string message) : Exception(message);
}
