using System.Reactive.Linq;
using DynamicData.Binding;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.UI;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Services.Features.Appearance;

[FeatureInfo(Id, "描边")]
public sealed class StrokeFeature : FeatureBase<IDecoratorHost, StrokeFeatureSettings>
{
    public const string Id = "omls.features.appearance.stroke";
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
            var decorator = new StrokeDecorator();
            _registration = target.Decorators.Register(decorator, DecoratorPriorities.Stroke);
            var resolvedBrush = settings.WhenValueChanged(source => source.StrokeBrush)
                .Select(ResolveBrush);
            var spec = Observable.CombineLatest(
                resolvedBrush,
                settings.WhenValueChanged(source => source.Thickness),
                settings.WhenValueChanged(source => source.IsHollow),
                settings.WhenValueChanged(source => source.Position),
                (brush, thickness, isHollow, position)
                    => new StrokeEffectSpec(brush, thickness, isHollow, position))
                .DistinctUntilChanged();
            _binding = DependencyPropertyBindingHelper.Bind(spec, decorator, StrokeDecorator.StrokeSpecProperty);
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

    private Brush? ResolveBrush(Brush? brush)
    {
        if (brush == null)
        {
            return null;
        }

        var resolved = SceneImageBrush.Resolve(brush, (Target as ISceneBlobContext)?.BlobUriConverter);
        if (resolved is XamlCompositionBrushBase
            && BrushEditorOperations.Kind(resolved) != BrushEditorKind.Unsupported)
        {
            return BrushEditorOperations.Copy(resolved);
        }

        return resolved;
    }
}
