using FluentIcons.Common;
using OhMyLockScreen.Shared.Abstractions.Services.Weather;
using OhMyLockScreen.Shared.Models.Weather;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Controls.Weather;

public sealed partial class WeatherCitySearchDrawer : UserControl
{
    private IWeatherService? _weather;
    private Action<WeatherCity>? _selectCity;
    private CancellationTokenSource? _search;
    private string? _selectedCity;
    private bool _updating;

    public WeatherCitySearchDrawer()
    {
        InitializeComponent();
        EmptyIcon.Content = WeatherVisuals.Icon(Icon.BorderNone, 64);
        Loaded += (_, _) => QueueInitialSearch();
        Unloaded += (_, _) => CancelSearch();
    }

    public void Initialize(IWeatherService weather, string? selectedCity, Action<WeatherCity> selectCity)
    {
        _weather = weather;
        _selectedCity = selectedCity;
        _selectCity = selectCity;
        if (IsLoaded) QueueInitialSearch();
    }

    private void QueueInitialSearch() => _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
    {
        if (IsLoaded) _ = SearchCitiesAsync(SearchText.Text, debounce: false);
    });

    private async void OnSearchTextChanged(object sender, TextChangedEventArgs args) => await SearchCitiesAsync(SearchText.Text, debounce: true);

    private async Task SearchCitiesAsync(string input, bool debounce)
    {
        if (_weather is not { } weather) return;
        CancelSearch();
        _search = new CancellationTokenSource();
        var token = _search.Token;
        var dispatcher = Dispatcher;
        Empty.Visibility = Visibility.Collapsed;
        SearchProgress.Visibility = Visibility.Visible;
        SearchProgress.IsActive = true;
        try
        {
            if (debounce) await Task.Delay(300, token).ConfigureAwait(false);
            var cities = await weather.GetCitiesByName(input).WaitAsync(token).ConfigureAwait(false);
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (token.IsCancellationRequested || SearchText.Text != input) return;
                _updating = true;
                try
                {
                    Cities.ItemsSource = cities;
                    var selected = cities.FirstOrDefault(city => city.Id == _selectedCity);
                    Cities.SelectedItem = selected;
                    if (selected != null) Cities.ScrollIntoView(selected);
                    Empty.Visibility = cities.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    SearchProgress.IsActive = false;
                    SearchProgress.Visibility = Visibility.Collapsed;
                }
                finally { _updating = false; }
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { System.Diagnostics.Debug.WriteLine($"无法显示城市搜索结果：{exception}"); }
    }

    private void OnCitySelected(object sender, SelectionChangedEventArgs args)
    {
        if (_updating || Cities.SelectedItem is not WeatherCity city) return;
        _selectedCity = city.Id;
        _selectCity?.Invoke(city);
    }

    private void CancelSearch()
    {
        _search?.Cancel();
        _search?.Dispose();
        _search = null;
    }
}
