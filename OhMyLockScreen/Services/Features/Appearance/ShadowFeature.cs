using System.Reactive.Linq;
using DynamicData.Binding;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Helpers;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Services.Features.Appearance;

[FeatureInfo(Id, "阴影")]
public sealed class ShadowFeature : FeatureBase<IShadowTarget, ShadowFeatureSettings>
{
    public const string Id = "omls.features.appearance.shadow";
    private IDisposable? _binding;

    public override void Attach(IShadowTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Detach();
        var settings = Settings;
        Target = target;

        try
        {
            if (target is DependencyObject dependencyTarget)
            {
                var spec = Observable.CombineLatest(
                    settings.WhenValueChanged(source => source.Color),
                    settings.WhenValueChanged(source => source.Opacity),
                    settings.WhenValueChanged(source => source.BlurRadius),
                    settings.WhenValueChanged(source => source.OffsetX),
                    settings.WhenValueChanged(source => source.OffsetY),
                    (color, opacity, blurRadius, offsetX, offsetY)
                        => new ShadowEffectSpec(color, opacity, blurRadius, offsetX, offsetY))
                    .DistinctUntilChanged();
                _binding = DependencyPropertyBindingHelper.Bind(spec, dependencyTarget, ShadowEffect.ShadowProperty);
            }
        }
        catch
        {
            Detach();
            throw;
        }
    }

    public override void Detach()
    {
        _binding?.Dispose();
        _binding = null;
        Target = null;
    }

    protected override void OnSettingsChanged()
    {
        if (Target is { } target)
        {
            if (SettingsInternal == null)
            {
                Detach();
            }
            else
            {
                Attach(target);
            }
        }
    }
}
