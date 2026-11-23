using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData.Binding;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Helpers;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Services.Features.Layout;

[FeatureInfo("omls.features.layout.transform", "变换")]
public sealed class TransformFeature : FeatureBase<UIElement, TransformFeatureSettings>
{
    private readonly CompositeDisposable _bindings = new();

    public override void Detach()
    {
        _bindings.Clear();
        Target = null;
    }

    public override void Attach(UIElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Detach();
        var settings = Settings;
        Target = target;

        try
        {
            var transform = new CompositeTransform();

            _bindings.Add(settings.WhenValueChanged(source => source.TranslateX)
                .Subscribe(value => transform.TranslateX = Sanitize(value, 0)));
            _bindings.Add(settings.WhenValueChanged(source => source.TranslateY)
                .Subscribe(value => transform.TranslateY = Sanitize(value, 0)));
            _bindings.Add(settings.WhenValueChanged(source => source.Rotation)
                .Subscribe(value => transform.Rotation = Sanitize(value, 0)));
            _bindings.Add(settings.WhenValueChanged(source => source.ScaleX)
                .CombineLatest(settings.WhenValueChanged(source => source.FlipHorizontal),
                    (scale, flip) => ApplyFlip(Sanitize(scale, 1), flip))
                .Subscribe(value => transform.ScaleX = value));
            _bindings.Add(settings.WhenValueChanged(source => source.ScaleY)
                .CombineLatest(settings.WhenValueChanged(source => source.FlipVertical),
                    (scale, flip) => ApplyFlip(Sanitize(scale, 1), flip))
                .Subscribe(value => transform.ScaleY = value));
            _bindings.Add(settings.WhenValueChanged(source => source.SkewX)
                .Subscribe(value => transform.SkewX = Math.Clamp(Sanitize(value, 0), -89, 89)));
            _bindings.Add(settings.WhenValueChanged(source => source.SkewY)
                .Subscribe(value => transform.SkewY = Math.Clamp(Sanitize(value, 0), -89, 89)));

            _bindings.Add(DependencyPropertyBindingHelper.Bind(
                Observable.Return<Transform>(transform), target, UIElement.RenderTransformProperty));
            _bindings.Add(DependencyPropertyBindingHelper.Bind(
                settings.WhenValueChanged(source => source.OriginX)
                    .CombineLatest(settings.WhenValueChanged(source => source.OriginY),
                        (x, y) => new Point(Sanitize(x, 0.5), Sanitize(y, 0.5))),
                target, UIElement.RenderTransformOriginProperty));
        }
        catch
        {
            Detach();
            throw;
        }
    }

    protected override void OnSettingsChanged()
    {
        if (Target is { } target)
        {
            Attach(target);
        }
    }

    private static double Sanitize(double value, double fallback)
        => double.IsFinite(value) ? NormalizeZero(value) : fallback;

    private static double ApplyFlip(double scale, bool flip)
        => NormalizeZero(flip ? -scale : scale);

    private static double NormalizeZero(double value)
        => value == 0 ? 0 : value;
}
