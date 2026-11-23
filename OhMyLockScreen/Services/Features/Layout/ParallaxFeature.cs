using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData.Binding;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;

namespace OhMyLockScreen.Services.Features.Layout;

[FeatureInfo("omls.features.layout.parallax", "视差")]
public sealed class ParallaxFeature : FeatureBase<LayerPresenter, ParallaxFeatureSettings>
{
    private readonly CompositeDisposable _bindings = new();

    public override void Attach(LayerPresenter target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Detach();
        var settings = Settings;
        Target = target;

        try
        {
            _bindings.Add(settings.WhenValueChanged(source => source.OverrideStrength)
                .CombineLatest(
                    settings.WhenValueChanged(source => source.Strength),
                    (overrideStrength, strength) => overrideStrength ? (double?)strength : null)
                .DistinctUntilChanged()
                .Subscribe(strengthOverride => target.SetParallax(this, strengthOverride)));
        }
        catch
        {
            Detach();
            throw;
        }
    }

    public override void Detach()
    {
        _bindings.Clear();
        if (Target is { } target)
        {
            Target = null;
            target.ClearParallax(this);
        }
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
