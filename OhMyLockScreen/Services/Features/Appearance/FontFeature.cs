using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData.Binding;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Helpers;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Services.Features.Appearance;

[FeatureInfo(Id, "字体")]
public sealed class FontFeature : FeatureBase<FrameworkElement, FontFeatureSettings>
{
    public const string Id = "omls.features.appearance.font";
    private CompositeDisposable? _bindings;

    public override void Attach(FrameworkElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Detach();
        Target = target;
        var bindings = _bindings = new CompositeDisposable();

        try
        {
            if (TextStyleTargetHelper.GetFontProperties(target) is not { } properties)
            {
                return;
            }

            bindings.Add(DependencyPropertyBindingHelper.Bind(Settings, source => source.FontWeight,
                target, properties.Weight, value => new FontWeight { Weight = value }));

            // Empty family names restore the original value/binding, allowing native inheritance.
            var familyBinding = new SerialDisposable();
            bindings.Add(familyBinding);
            bindings.Add(Settings.WhenValueChanged(source => source.FontFamilyName).Subscribe(name =>
            {
                familyBinding.Disposable = null;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    familyBinding.Disposable = DependencyPropertyBindingHelper.Bind(
                        Observable.Return(new FontFamily(name)), target, properties.Family);
                }
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
        _bindings?.Dispose();
        _bindings = null;
        Target = null;
    }

    protected override void OnSettingsChanged()
    {
        if (Target is { } target)
        {
            Attach(target);
        }
    }
}
