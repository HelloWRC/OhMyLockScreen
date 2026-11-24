using System.Reactive.Disposables;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData.Binding;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Shared.Controls.UI;

namespace OhMyLockScreen.ViewModels;

public sealed class StrokeSettingsViewModel : ObservableObject, IDisposable
{
    private readonly StrokeFeatureSettings _settings;
    private CompositeDisposable? _subscriptions;

    public StrokeSettingsViewModel(StrokeFeatureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        ResetCommand = new RelayCommand(Reset);
    }

    public IRelayCommand ResetCommand { get; }

    public double Thickness
    {
        get => _settings.Thickness;
        set
        {
            if (IsInRange(value, 0, StrokeFeatureSettings.MaxThickness))
            {
                _settings.Thickness = Round(value);
            }
        }
    }

    public double ThicknessStep
    {
        get => Math.Round(_settings.Thickness * 2.0, MidpointRounding.AwayFromZero) / 2.0;
        set
        {
            if (IsInRange(value, 0, StrokeFeatureSettings.MaxThickness))
            {
                var snapped = Math.Round(value * 2.0, MidpointRounding.AwayFromZero) / 2.0;
                _settings.Thickness = snapped == 0 ? 0 : snapped;
            }
        }
    }

    public StrokePosition Position
    {
        get => _settings.Position;
        set => _settings.Position = value;
    }

    public int PositionIndex
    {
        get => (int)_settings.Position;
        set
        {
            if (Enum.IsDefined((StrokePosition)value))
            {
                _settings.Position = (StrokePosition)value;
            }
        }
    }

    public bool IsHollow
    {
        get => _settings.IsHollow;
        set => _settings.IsHollow = value;
    }

    public void Reset()
    {
        _settings.StrokeBrush = StrokeFeatureSettings.CreateDefaultStrokeBrush();
        _settings.Thickness = StrokeFeatureSettings.DefaultThickness;
        _settings.Position = StrokeFeatureSettings.DefaultPosition;
        _settings.IsHollow = StrokeFeatureSettings.DefaultIsHollow;
    }

    public void Attach()
    {
        if (_subscriptions != null)
        {
            return;
        }

        _subscriptions = new CompositeDisposable
        {
            _settings.WhenValueChanged(source => source.Thickness).Subscribe(_ =>
            {
                OnPropertyChanged(nameof(Thickness));
                OnPropertyChanged(nameof(ThicknessStep));
            }),
            _settings.WhenValueChanged(source => source.Position).Subscribe(_ =>
            {
                OnPropertyChanged(nameof(Position));
                OnPropertyChanged(nameof(PositionIndex));
            }),
            _settings.WhenValueChanged(source => source.IsHollow).Subscribe(_ =>
            {
                OnPropertyChanged(nameof(IsHollow));
            })
        };
    }

    public void Dispose()
    {
        _subscriptions?.Dispose();
        _subscriptions = null;
    }

    private static bool IsInRange(double value, double min, double max)
        => double.IsFinite(value) && value >= min && value <= max;

    private static double Round(double value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return rounded == 0 ? 0 : rounded;
    }
}
