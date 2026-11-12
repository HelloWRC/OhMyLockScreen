using System.Reactive.Linq;
using DynamicData.Binding;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Services.Features.Appearance;

[FeatureInfo("omls.features.appearance.background", "背景")]
public sealed class BackgroundFeature : FeatureBase<FrameworkElement, BackgroundFeatureSettings>
{
    private IDisposable? _binding;
    private IDisposable? _borderSubscription;

    internal static FrameworkElement? ResolveBackgroundTarget(FrameworkElement? target) => target switch
    {
        IBorderable borderable => borderable.Border,
        Control control => control,
        _ => null
    };

    public override void Attach(FrameworkElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Detach();
        var settings = Settings;
        Target = target;

        try
        {
            if (target is IBorderable borderable)
            {
                _borderSubscription = borderable.WhenValueChanged(source => source.Border)
                    .Subscribe(border => BindBackground(settings, border));
            }
            else
            {
                BindBackground(settings, ResolveBackgroundTarget(target));
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
        _borderSubscription?.Dispose();
        _borderSubscription = null;
        _binding?.Dispose();
        _binding = null;
        Target = null;
    }

    private void BindBackground(BackgroundFeatureSettings settings, FrameworkElement? target)
    {
        _binding?.Dispose();
        _binding = null;

        var property = target switch
        {
            Border => Windows.UI.Xaml.Controls.Border.BackgroundProperty,
            Control => Control.BackgroundProperty,
            _ => null
        };
        if (property != null)
        {
            _binding = DependencyPropertyBindingHelper.Bind(settings, source => source.Background, target!, property,
                brush => SceneImageBrush.Resolve(brush, (Target as ISceneBlobContext)?.BlobUriConverter));
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
