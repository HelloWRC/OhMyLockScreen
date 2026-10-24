using System.Reactive.Disposables;
using Windows.UI.Xaml;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Helpers;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Services.Features.Layout;

[FeatureInfo("omls.features.layout.grid", "网格布局")]
public class GridLayoutFeature : FeatureBase<FrameworkElement, GridLayoutFeatureSettings>
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
            _bindings.Add(DependencyPropertyBindingHelper.Bind(
                settings, source => source.Row, target, Grid.RowProperty));
            _bindings.Add(DependencyPropertyBindingHelper.Bind(
                settings, source => source.Column, target, Grid.ColumnProperty));
            _bindings.Add(DependencyPropertyBindingHelper.Bind(
                settings, source => source.RowSpan, target, Grid.RowSpanProperty));
            _bindings.Add(DependencyPropertyBindingHelper.Bind(
                settings, source => source.ColumnSpan, target, Grid.ColumnSpanProperty));
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
