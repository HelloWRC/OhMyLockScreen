using System.Reactive.Linq;
using DynamicData.Binding;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Helpers;

namespace OhMyLockScreen.Services.Features.Appearance;

[FeatureInfo(Id, "阴影")]
public sealed class ShadowFeature : FeatureBase<IDecoratorHost, ShadowFeatureSettings>
{
    public const string Id = "omls.features.appearance.shadow";
    private IDisposable? _binding;
    private IDecoratorRegistration? _registration;

    public override void Attach(IDecoratorHost target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Detach();
        var settings = Settings;
        Target = target;

        try
        {
            var decorator = new ShadowDecorator();
            _registration = target.Decorators.Register(decorator, DecoratorPriorities.Shadow);
            var spec = Observable.CombineLatest(
                settings.WhenValueChanged(source => source.Color),
                settings.WhenValueChanged(source => source.Opacity),
                settings.WhenValueChanged(source => source.BlurRadius),
                settings.WhenValueChanged(source => source.OffsetX),
                settings.WhenValueChanged(source => source.OffsetY),
                (color, opacity, blurRadius, offsetX, offsetY)
                    => new ShadowEffectSpec(color, opacity, blurRadius, offsetX, offsetY))
                .DistinctUntilChanged();
            _binding = DependencyPropertyBindingHelper.Bind(spec, decorator, ShadowDecorator.ShadowSpecProperty);
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
        _registration?.Dispose();
        _registration = null;
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
