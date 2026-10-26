using System.Reactive.Disposables;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData.Binding;
using OhMyLockScreen.Models.Features.Appearance;
using Windows.UI.Xaml;

namespace OhMyLockScreen.ViewModels;

public sealed class BorderSettingsViewModel(BorderFeatureSettings settings) : ObservableObject, IDisposable
{
    private CompositeDisposable? _subscriptions;

    public double ThicknessLeft
    {
        get => settings.BorderThickness.Left;
        set => SetThickness(value, ThicknessTop, ThicknessRight, ThicknessBottom);
    }

    public double ThicknessTop
    {
        get => settings.BorderThickness.Top;
        set => SetThickness(ThicknessLeft, value, ThicknessRight, ThicknessBottom);
    }

    public double ThicknessRight
    {
        get => settings.BorderThickness.Right;
        set => SetThickness(ThicknessLeft, ThicknessTop, value, ThicknessBottom);
    }

    public double ThicknessBottom
    {
        get => settings.BorderThickness.Bottom;
        set => SetThickness(ThicknessLeft, ThicknessTop, ThicknessRight, value);
    }

    public double RadiusTopLeft
    {
        get => settings.CornerRadius.TopLeft;
        set => SetRadius(value, RadiusTopRight, RadiusBottomRight, RadiusBottomLeft);
    }

    public double RadiusTopRight
    {
        get => settings.CornerRadius.TopRight;
        set => SetRadius(RadiusTopLeft, value, RadiusBottomRight, RadiusBottomLeft);
    }

    public double RadiusBottomRight
    {
        get => settings.CornerRadius.BottomRight;
        set => SetRadius(RadiusTopLeft, RadiusTopRight, value, RadiusBottomLeft);
    }

    public double RadiusBottomLeft
    {
        get => settings.CornerRadius.BottomLeft;
        set => SetRadius(RadiusTopLeft, RadiusTopRight, RadiusBottomRight, value);
    }

    public double PaddingLeft
    {
        get => settings.Padding.Left;
        set => SetPadding(value, PaddingTop, PaddingRight, PaddingBottom);
    }

    public double PaddingTop
    {
        get => settings.Padding.Top;
        set => SetPadding(PaddingLeft, value, PaddingRight, PaddingBottom);
    }

    public double PaddingRight
    {
        get => settings.Padding.Right;
        set => SetPadding(PaddingLeft, PaddingTop, value, PaddingBottom);
    }

    public double PaddingBottom
    {
        get => settings.Padding.Bottom;
        set => SetPadding(PaddingLeft, PaddingTop, PaddingRight, value);
    }

    private static bool IsValid(double a, double b, double c, double d) =>
        IsValid(a) && IsValid(b) && IsValid(c) && IsValid(d);

    private static bool IsValid(double value) => double.IsFinite(value) && value is >= 0 and <= 100000;

    private void SetThickness(double left, double top, double right, double bottom)
    {
        if (IsValid(left, top, right, bottom))
        {
            settings.BorderThickness = new Thickness(left, top, right, bottom);
        }
    }

    private void SetRadius(double topLeft, double topRight, double bottomRight, double bottomLeft)
    {
        if (IsValid(topLeft, topRight, bottomRight, bottomLeft))
        {
            settings.CornerRadius = new CornerRadius(topLeft, topRight, bottomRight, bottomLeft);
        }
    }

    private void SetPadding(double left, double top, double right, double bottom)
    {
        if (IsValid(left, top, right, bottom))
        {
            settings.Padding = new Thickness(left, top, right, bottom);
        }
    }

    public void Attach()
    {
        if (_subscriptions != null)
        {
            return;
        }

        _subscriptions = new CompositeDisposable();
        _subscriptions.Add(settings.WhenValueChanged(source => source.BorderThickness).Subscribe(_ =>
        {
            OnPropertyChanged(nameof(ThicknessLeft));
            OnPropertyChanged(nameof(ThicknessTop));
            OnPropertyChanged(nameof(ThicknessRight));
            OnPropertyChanged(nameof(ThicknessBottom));
        }));
        _subscriptions.Add(settings.WhenValueChanged(source => source.CornerRadius).Subscribe(_ =>
        {
            OnPropertyChanged(nameof(RadiusTopLeft));
            OnPropertyChanged(nameof(RadiusTopRight));
            OnPropertyChanged(nameof(RadiusBottomRight));
            OnPropertyChanged(nameof(RadiusBottomLeft));
        }));
        _subscriptions.Add(settings.WhenValueChanged(source => source.Padding).Subscribe(_ =>
        {
            OnPropertyChanged(nameof(PaddingLeft));
            OnPropertyChanged(nameof(PaddingTop));
            OnPropertyChanged(nameof(PaddingRight));
            OnPropertyChanged(nameof(PaddingBottom));
        }));
    }

    public void Dispose()
    {
        _subscriptions?.Dispose();
        _subscriptions = null;
    }
}
