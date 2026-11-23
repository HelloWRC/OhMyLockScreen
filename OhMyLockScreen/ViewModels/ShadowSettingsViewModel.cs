using System.Reactive.Disposables;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData.Binding;
using OhMyLockScreen.Models.Features.Appearance;
using Windows.UI;

namespace OhMyLockScreen.ViewModels;

public sealed class ShadowSettingsViewModel : ObservableObject, IDisposable
{
    private const double OpacityTolerance = 0.005;
    private const double MetricTolerance = 0.01;

    private static readonly ShadowPreset SoftPreset = new(Colors.Black, 0.5, 16, 0, 4);
    private static readonly ShadowPreset ElevatedPreset = new(Colors.Black, 0.42, 32, 0, 12);
    private static readonly ShadowPreset CrispPreset = new(Colors.Black, 0.65, 6, 0, 2);
    private static readonly ShadowPreset GlowPreset = new(Colors.White, 0.6, 24, 0, 0);

    private readonly ShadowFeatureSettings _settings;
    private CompositeDisposable? _subscriptions;

    public ShadowSettingsViewModel(ShadowFeatureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        ResetCommand = new RelayCommand(Reset);
    }

    public IRelayCommand ResetCommand { get; }

    public Color Color
    {
        get => _settings.Color;
        set => _settings.Color = value;
    }

    public string ColorHex => $"#{_settings.Color.R:X2}{_settings.Color.G:X2}{_settings.Color.B:X2}";

    public double Opacity
    {
        get => _settings.Opacity;
        set
        {
            if (IsInRange(value, 0, 1))
            {
                _settings.Opacity = RoundOpacity(value);
            }
        }
    }

    public double OpacityPercent
    {
        get => Math.Round(_settings.Opacity * 100);
        set
        {
            if (IsInRange(value, 0, 100))
            {
                _settings.Opacity = Math.Round(value, MidpointRounding.AwayFromZero) / 100.0;
            }
        }
    }

    public double BlurRadius
    {
        get => _settings.BlurRadius;
        set
        {
            if (IsInRange(value, 0, ShadowFeatureSettings.MaxBlurRadius))
            {
                _settings.BlurRadius = Round(value);
            }
        }
    }

    public double BlurRadiusStep
    {
        get => Math.Round(_settings.BlurRadius);
        set
        {
            if (IsInRange(value, 0, ShadowFeatureSettings.MaxBlurRadius))
            {
                _settings.BlurRadius = Math.Round(value, MidpointRounding.AwayFromZero);
            }
        }
    }

    public double OffsetX
    {
        get => _settings.OffsetX;
        set
        {
            if (IsInRange(value, -ShadowFeatureSettings.MaxOffset, ShadowFeatureSettings.MaxOffset))
            {
                _settings.OffsetX = Round(value);
            }
        }
    }

    public double OffsetY
    {
        get => _settings.OffsetY;
        set
        {
            if (IsInRange(value, -ShadowFeatureSettings.MaxOffset, ShadowFeatureSettings.MaxOffset))
            {
                _settings.OffsetY = Round(value);
            }
        }
    }

    public bool IsSoftPreset
    {
        get => MatchesPreset(SoftPreset);
        set => UpdatePreset(value, SoftPreset, nameof(IsSoftPreset));
    }

    public bool IsElevatedPreset
    {
        get => MatchesPreset(ElevatedPreset);
        set => UpdatePreset(value, ElevatedPreset, nameof(IsElevatedPreset));
    }

    public bool IsCrispPreset
    {
        get => MatchesPreset(CrispPreset);
        set => UpdatePreset(value, CrispPreset, nameof(IsCrispPreset));
    }

    public bool IsGlowPreset
    {
        get => MatchesPreset(GlowPreset);
        set => UpdatePreset(value, GlowPreset, nameof(IsGlowPreset));
    }

    public void ApplySoftPreset() => ApplyPreset(SoftPreset);
    public void ApplyElevatedPreset() => ApplyPreset(ElevatedPreset);
    public void ApplyCrispPreset() => ApplyPreset(CrispPreset);
    public void ApplyGlowPreset() => ApplyPreset(GlowPreset);

    public void Reset() => ApplyPreset(SoftPreset);

    public void Attach()
    {
        if (_subscriptions != null)
        {
            return;
        }

        _subscriptions = new CompositeDisposable
        {
            _settings.WhenValueChanged(source => source.Color).Subscribe(_ =>
            {
                OnPropertyChanged(nameof(Color));
                OnPropertyChanged(nameof(ColorHex));
                NotifyPresets();
            }),
            _settings.WhenValueChanged(source => source.Opacity).Subscribe(_ =>
            {
                OnPropertyChanged(nameof(Opacity));
                OnPropertyChanged(nameof(OpacityPercent));
                NotifyPresets();
            }),
            _settings.WhenValueChanged(source => source.BlurRadius).Subscribe(_ =>
            {
                OnPropertyChanged(nameof(BlurRadius));
                OnPropertyChanged(nameof(BlurRadiusStep));
                NotifyPresets();
            }),
            _settings.WhenValueChanged(source => source.OffsetX).Subscribe(_ =>
            {
                OnPropertyChanged(nameof(OffsetX));
                NotifyPresets();
            }),
            _settings.WhenValueChanged(source => source.OffsetY).Subscribe(_ =>
            {
                OnPropertyChanged(nameof(OffsetY));
                NotifyPresets();
            })
        };
    }

    public void Dispose()
    {
        _subscriptions?.Dispose();
        _subscriptions = null;
    }

    private void UpdatePreset(bool isChecked, ShadowPreset preset, string propertyName)
    {
        if (isChecked)
        {
            ApplyPreset(preset);
        }
        else
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void ApplyPreset(ShadowPreset preset)
    {
        _settings.Color = preset.Color;
        _settings.Opacity = preset.Opacity;
        _settings.BlurRadius = preset.BlurRadius;
        _settings.OffsetX = preset.OffsetX;
        _settings.OffsetY = preset.OffsetY;
        NotifyPresets();
    }

    private bool MatchesPreset(ShadowPreset preset)
        => _settings.Color == preset.Color
            && Math.Abs(_settings.Opacity - preset.Opacity) <= OpacityTolerance
            && Math.Abs(_settings.BlurRadius - preset.BlurRadius) <= MetricTolerance
            && Math.Abs(_settings.OffsetX - preset.OffsetX) <= MetricTolerance
            && Math.Abs(_settings.OffsetY - preset.OffsetY) <= MetricTolerance;

    private void NotifyPresets()
    {
        OnPropertyChanged(nameof(IsSoftPreset));
        OnPropertyChanged(nameof(IsElevatedPreset));
        OnPropertyChanged(nameof(IsCrispPreset));
        OnPropertyChanged(nameof(IsGlowPreset));
    }

    private static bool IsInRange(double value, double min, double max)
        => double.IsFinite(value) && value >= min && value <= max;

    private static double Round(double value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return rounded == 0 ? 0 : rounded;
    }

    private static double RoundOpacity(double value)
    {
        var rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);
        return rounded == 0 ? 0 : rounded;
    }

    private readonly record struct ShadowPreset(
        Color Color,
        double Opacity,
        double BlurRadius,
        double OffsetX,
        double OffsetY);
}
