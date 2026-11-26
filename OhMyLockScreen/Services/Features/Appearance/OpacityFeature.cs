using System.Reactive.Linq;
using DynamicData.Binding;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Helpers;

namespace OhMyLockScreen.Services.Features.Appearance;

[FeatureInfo(Id, "不透明度")]
public sealed class OpacityFeature : FeatureBase<IDecoratorHost, OpacityFeatureSettings>
{
    public const string Id = "omls.features.appearance.opacity";
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
            var decorator = new OpacityDecorator();
            _registration = target.Decorators.Register(decorator, DecoratorPriorities.Opacity);
            var spec = Observable.CombineLatest(
                    settings.WhenValueChanged(source => source.Opacity),
                    settings.WhenValueChanged(source => source.BlendMode),
                    (opacity, blendMode) => new OpacityEffectSpec(opacity, blendMode))
                .DistinctUntilChanged();
            _binding = DependencyPropertyBindingHelper.Bind(spec, decorator, OpacityDecorator.OpacitySpecProperty);
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
