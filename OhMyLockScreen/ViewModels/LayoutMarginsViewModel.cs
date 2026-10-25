using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Models.Features.Layout;
using Windows.UI.Xaml;

namespace OhMyLockScreen.ViewModels;

public sealed class LayoutMarginsViewModel(LayoutFeatureSettings settings) : ObservableObject, IDisposable
{
    private bool _attached;
    public double Left
    {
        get => settings.Margin.Left; set => Set(value, Top, Right, Bottom);
    }
    public double Top
    {
        get => settings.Margin.Top; set => Set(Left, value, Right, Bottom);
    }
    public double Right
    {
        get => settings.Margin.Right; set => Set(Left, Top, value, Bottom);
    }
    public double Bottom
    {
        get => settings.Margin.Bottom; set => Set(Left, Top, Right, value);
    }

    private void Set(double left, double top, double right, double bottom)
    {
        if (double.IsFinite(left) && double.IsFinite(top) && double.IsFinite(right) && double.IsFinite(bottom))
        {
            settings.Margin = new Thickness(left, top, right, bottom);
        }
    }

    public void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        settings.PropertyChanged += OnSettingsChanged;
        Refresh();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(settings.Margin))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(Left));
        OnPropertyChanged(nameof(Top));
        OnPropertyChanged(nameof(Right));
        OnPropertyChanged(nameof(Bottom));
    }

    public void Dispose()
    {
        if (_attached)
        {
            settings.PropertyChanged -= OnSettingsChanged;
        }

        _attached = false;
    }
}