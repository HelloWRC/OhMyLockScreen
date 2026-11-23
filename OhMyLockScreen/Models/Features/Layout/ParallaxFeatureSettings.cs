using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Attributes.Serialization;

namespace OhMyLockScreen.Models.Features.Layout;

[AutoJsonTypeName("omls.features.layout.parallax")]
public partial class ParallaxFeatureSettings : ObservableObject, IFeatureSettings
{
    private const double DefaultStrength = 100;

    [ObservableProperty]
    private bool _overrideStrength;

    private double _strength = DefaultStrength;

    public double Strength
    {
        get => _strength;
        set => SetProperty(ref _strength, double.IsFinite(value)
            ? Math.Clamp(value, 0, 200)
            : DefaultStrength);
    }
}
