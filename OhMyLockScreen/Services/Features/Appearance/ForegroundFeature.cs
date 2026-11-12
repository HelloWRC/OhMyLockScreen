using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Services.Features.Appearance;

[FeatureInfo("omls.features.appearance.foreground", "前景")]
public sealed class ForegroundFeature : FeatureBase<FrameworkElement, ForegroundFeatureSettings>
{
    private IDisposable? _binding;

    public override void Attach(FrameworkElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Detach();
        var settings = Settings;
        Target = target;

        try
        {
            // UWP foreground properties are not attached properties and only accept their owning types.
            var property = target switch
            {
                Control => Control.ForegroundProperty,
                TextBlock => TextBlock.ForegroundProperty,
                RichTextBlock => RichTextBlock.ForegroundProperty,
                ContentPresenter => ContentPresenter.ForegroundProperty,
                _ => null
            };
            if (property != null)
            {
                _binding = DependencyPropertyBindingHelper.Bind(
                    settings, source => source.Foreground, target, property, brush =>
                    {
                        // Refresh inherited content foregrounds even when only painting changes.
                        target.InvalidateArrange();
                        return SceneImageBrush.Resolve(brush, (target as ISceneBlobContext)?.BlobUriConverter);
                    });
            }
        }
        catch
        {
            Detach();
            throw;
        }
    }

    public override void Detach()
    {
        var target = _binding != null ? Target : null;
        _binding?.Dispose();
        _binding = null;
        Target = null;
        target?.InvalidateArrange();
    }

    protected override void OnSettingsChanged()
    {
        if (Target is { } target)
        {
            Attach(target);
        }
    }
}
