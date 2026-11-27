using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Helpers;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Services.Features.Appearance;

[FeatureInfo("omls.features.appearance.theme", "颜色主题")]
public sealed class ThemeFeature : FeatureBase<FrameworkElement, ThemeFeatureSettings>
{
    private IDisposable? _binding;

    public override void Attach(FrameworkElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Detach();
        _binding = DependencyPropertyBindingHelper.Bind(
            Settings, source => source.RequestedTheme, target, FrameworkElement.RequestedThemeProperty);
        Target = target;
    }

    public override void Detach()
    {
        _binding?.Dispose();
        _binding = null;
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
