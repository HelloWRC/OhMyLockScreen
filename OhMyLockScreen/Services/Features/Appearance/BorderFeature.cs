using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData.Binding;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.UI;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Services.Features.Appearance;

[FeatureInfo("omls.features.appearance.border", "边框")]
public sealed class BorderFeature : FeatureBase<IBorderable, BorderFeatureSettings>
{
    private readonly CompositeDisposable _bindings = new();
    private IDisposable? _borderSubscription;

    public override void Attach(IBorderable target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Detach();
        var settings = Settings;
        Target = target;

        try
        {
            _borderSubscription = target.WhenValueChanged(source => source.Border)
                .Subscribe(border => BindBorder(settings, border));
        }
        catch
        {
            Detach();
            throw;
        }
    }

    public override void Detach()
    {
        _borderSubscription?.Dispose();
        _borderSubscription = null;
        _bindings.Clear();
        Target = null;
    }

    private void BindBorder(BorderFeatureSettings settings, Border? border)
    {
        _bindings.Clear();
        if (border == null)
        {
            return;
        }

        try
        {
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
            _bindings.Clear();
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
}
