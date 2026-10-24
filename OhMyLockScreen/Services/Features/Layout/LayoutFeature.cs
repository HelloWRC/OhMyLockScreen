using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData.Binding;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Helpers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Services.Features.Layout;

[FeatureInfo("omls.features.layout.layout", "布局")]
public class LayoutFeature : FeatureBase<FrameworkElement, LayoutFeatureSettings>
{
    private readonly CompositeDisposable _bindings = new();

    public override void Detach()
    {
        _bindings.Clear();
        Target = null;
    }

    public override void Attach(FrameworkElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Detach();
        var settings = Settings;
        Target = target;

        try
        {
            _bindings.Add(settings.WhenValueChanged(source => source.Width)
                .CombineLatest(settings.WhenValueChanged(source => source.IsAutoWidth),
                    (width, isAuto) => isAuto ? double.NaN : width)
                .Subscribe(width => target.Width = width));
            _bindings.Add(settings.WhenValueChanged(source => source.Height)
                .CombineLatest(settings.WhenValueChanged(source => source.IsAutoHeight),
                    (height, isAuto) => isAuto ? double.NaN : height)
                .Subscribe(height => target.Height = height));
            _bindings.Add(DependencyPropertyBindingHelper.Bind(
                settings, source => source.VerticalAlignment, target, FrameworkElement.VerticalAlignmentProperty));
            _bindings.Add(DependencyPropertyBindingHelper.Bind(
                settings, source => source.HorizontalAlignment, target, FrameworkElement.HorizontalAlignmentProperty));
            _bindings.Add(DependencyPropertyBindingHelper.Bind(
                settings, source => source.Margin, target, FrameworkElement.MarginProperty));
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
}
