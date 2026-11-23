using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData.Binding;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Controls.UI;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Services.Features.Layout;

[FeatureInfo("omls.features.layout.parallax", "视差")]
public sealed class ParallaxFeature : FeatureBase<LayerPresenter, ParallaxFeatureSettings>
{
    private readonly CompositeDisposable _bindings = new();
    private CompositeTransform? _transform;
    private IDecoratorRegistration? _decoratorRegistration;
    private ISceneParallaxRegistration? _parallaxRegistration;
    private ISceneParallaxHost? _registeredHost;
    private double? _strengthOverride;
    private long? _hostCallbackToken;

    public override void Attach(LayerPresenter target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Detach();
        var settings = Settings;
        Target = target;

        try
        {
            var transform = new CompositeTransform();
            var wrapper = new Border
            {
                RenderTransform = transform,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            _transform = transform;
            _decoratorRegistration = target.Decorators.Register(wrapper, DecoratorPriorities.Parallax);
            _hostCallbackToken = target.RegisterPropertyChangedCallback(
                LayerPresenter.ParallaxHostProperty,
                OnParallaxHostChanged);

            _bindings.Add(settings.WhenValueChanged(source => source.OverrideStrength)
                .CombineLatest(
                    settings.WhenValueChanged(source => source.Strength),
                    (overrideStrength, strength) => overrideStrength ? (double?)strength : null)
                .DistinctUntilChanged()
                .Subscribe(strengthOverride =>
                {
                    _strengthOverride = strengthOverride;
                    SyncParallaxRegistration();
                }));
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

        var target = Target;
        Target = null;

        if (target != null && _hostCallbackToken is { } token)
        {
            _hostCallbackToken = null;
            target.UnregisterPropertyChangedCallback(LayerPresenter.ParallaxHostProperty, token);
        }

        var parallax = _parallaxRegistration;
        _parallaxRegistration = null;
        _registeredHost = null;
        parallax?.Dispose();

        var decorator = _decoratorRegistration;
        _decoratorRegistration = null;
        _transform = null;
        _strengthOverride = null;
        decorator?.Dispose();
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

    private void OnParallaxHostChanged(DependencyObject sender, DependencyProperty property)
        => SyncParallaxRegistration();

    private void SyncParallaxRegistration()
    {
        var host = Target?.ParallaxHost;
        if (!ReferenceEquals(host, _registeredHost))
        {
            var previous = _parallaxRegistration;
            _parallaxRegistration = null;
            _registeredHost = null;
            previous?.Dispose();

            if (host != null && _transform != null)
            {
                var registration = host.Register(_transform, _strengthOverride);
                _registeredHost = host;
                _parallaxRegistration = registration;
            }
        }
        else if (_parallaxRegistration != null)
        {
            _parallaxRegistration.SetStrengthOverride(_strengthOverride);
        }
    }
}
