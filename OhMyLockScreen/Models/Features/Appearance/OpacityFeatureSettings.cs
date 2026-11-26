using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Attributes.Serialization;
using OhMyLockScreen.Shared.Controls.UI;

namespace OhMyLockScreen.Models.Features.Appearance;

[AutoJsonTypeName("omls.features.appearance.opacity")]
public partial class OpacityFeatureSettings : ObservableObject, IFeatureSettings
{
    public const double DefaultOpacity = 1.0;
    public const OpacityBlendMode DefaultBlendMode = OpacityBlendMode.Normal;

    private double _opacity = DefaultOpacity;
    private OpacityBlendMode _blendMode = DefaultBlendMode;

    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, OpacityEffectHelper.SanitizeOpacity(value, DefaultOpacity));
    }

    public OpacityBlendMode BlendMode
    {
        get => _blendMode;
        set => SetProperty(ref _blendMode, OpacityEffectHelper.SanitizeBlendMode(value));
    }
}
