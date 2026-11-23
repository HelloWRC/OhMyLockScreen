using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Services.Features.Appearance;

[FeatureInfo("omls.features.appearance.background", "背景")]
public sealed class BackgroundFeature : FeatureBase<FrameworkElement, BackgroundFeatureSettings>
{
    private IDisposable? _binding;
    private ISharedDecoratorRegistration<Border>? _borderLease;

    internal static FrameworkElement? ResolveBackgroundTarget(FrameworkElement? target) => target switch
    {
        Control control => control,
        IDecoratorHost host => host.Decorators.TryGetShared<Border>(DecoratorKeys.Border, out var border) ? border : target,
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
            if (target is Control control)
            {
                BindBackground(settings, control, Control.BackgroundProperty);
            }
            else if (target is IDecoratorHost host)
            {
                var lease = _borderLease = host.Decorators.AcquireShared(
                    DecoratorKeys.Border,
                    static () => new Border(),
                    DecoratorPriorities.Border);
                BindBackground(settings, lease.Element, Border.BackgroundProperty);
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
        _binding?.Dispose();
        _binding = null;
        _borderLease?.Dispose();
        _borderLease = null;
        Target = null;
    }

    private void BindBackground(BackgroundFeatureSettings settings, FrameworkElement target, DependencyProperty property)
    {
        _binding = DependencyPropertyBindingHelper.Bind(settings, source => source.Background, target, property,
            brush => SceneImageBrush.Resolve(brush, (Target as ISceneBlobContext)?.BlobUriConverter));
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
