using System.Reactive.Disposables;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData.Binding;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Shared.Controls.UI;

namespace OhMyLockScreen.ViewModels;

public sealed class OpacitySettingsViewModel : ObservableObject, IDisposable
{
    public static IReadOnlyList<OpacityBlendMode> OrderedBlendModes { get; } =
        (OpacityBlendMode[])
        [
        OpacityBlendMode.Normal,
        OpacityBlendMode.Darken,
        OpacityBlendMode.Multiply,
        OpacityBlendMode.ColorBurn,
        OpacityBlendMode.LinearBurn,
        OpacityBlendMode.DarkerColor,
        OpacityBlendMode.Lighten,
        OpacityBlendMode.Screen,
        OpacityBlendMode.ColorDodge,
        OpacityBlendMode.LinearDodge,
        OpacityBlendMode.LighterColor,
        OpacityBlendMode.Overlay,
        OpacityBlendMode.SoftLight,
        OpacityBlendMode.HardLight,
        OpacityBlendMode.VividLight,
        OpacityBlendMode.LinearLight,
        OpacityBlendMode.PinLight,
        OpacityBlendMode.HardMix,
        OpacityBlendMode.Difference,
        OpacityBlendMode.Exclusion,
        OpacityBlendMode.Subtract,
        OpacityBlendMode.Division
    ];

    private readonly OpacityFeatureSettings _settings;
    private CompositeDisposable? _subscriptions;

    public OpacitySettingsViewModel(OpacityFeatureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        ResetCommand = new RelayCommand(Reset);
    }

    public IRelayCommand ResetCommand { get; }

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

    public OpacityBlendMode BlendMode
    {
        get => _settings.BlendMode;
        set => _settings.BlendMode = value;
    }

    public int BlendModeIndex
    {
        get
        {
            var mode = _settings.BlendMode;
            for (var index = 0; index < OrderedBlendModes.Count; index++)
            {
                if (OrderedBlendModes[index] == mode)
                {
                    return index;
                }
            }

            return 0;
        }
        set
        {
            if ((uint)value < (uint)OrderedBlendModes.Count)
            {
                _settings.BlendMode = OrderedBlendModes[value];
            }
        }
    }

    public void Reset()
    {
        _settings.Opacity = OpacityFeatureSettings.DefaultOpacity;
        _settings.BlendMode = OpacityFeatureSettings.DefaultBlendMode;
    }

    public void Attach()
    {
        if (_subscriptions != null)
        {
            return;
        }

        _subscriptions = new CompositeDisposable
        {
            _settings.WhenValueChanged(source => source.Opacity).Subscribe(_ =>
            {
                OnPropertyChanged(nameof(Opacity));
                OnPropertyChanged(nameof(OpacityPercent));
            }),
            _settings.WhenValueChanged(source => source.BlendMode).Subscribe(_ =>
            {
                OnPropertyChanged(nameof(BlendMode));
                OnPropertyChanged(nameof(BlendModeIndex));
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

    private static double RoundOpacity(double value)
    {
        var rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);
        return rounded == 0 ? 0 : rounded;
    }
}
