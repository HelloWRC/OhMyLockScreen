using System.Reactive.Disposables;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.UI;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Services.Features.Appearance;

[FeatureInfo("omls.features.appearance.border", "边框")]
public sealed class BorderFeature : FeatureBase<ComponentPresenter, BorderFeatureSettings>
{
    private readonly CompositeDisposable _bindings = new();
    private ISharedDecoratorRegistration<Border>? _borderLease;

    public override void Attach(ComponentPresenter target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Detach();
        var settings = Settings;
        Target = target;

        try
        {
            var lease = _borderLease = target.Decorators.AcquireShared(
                DecoratorKeys.Border,
                static () => new Border(),
                DecoratorPriorities.Border);
            var border = lease.Element;

            _bindings.Add(DependencyPropertyBindingHelper.Bind(
                settings, source => source.BorderBrush, border, Border.BorderBrushProperty,
                brush => SceneImageBrush.Resolve(brush, (Target as ISceneBlobContext)?.BlobUriConverter)));
            _bindings.Add(DependencyPropertyBindingHelper.Bind(
                settings, source => source.BorderThickness, border, Border.BorderThicknessProperty));
            _bindings.Add(DependencyPropertyBindingHelper.Bind(
                settings, source => source.CornerRadius, border, Border.CornerRadiusProperty));
            _bindings.Add(DependencyPropertyBindingHelper.Bind(
                settings, source => source.Padding, border, Border.PaddingProperty));
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
        _borderLease?.Dispose();
        _borderLease = null;
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
