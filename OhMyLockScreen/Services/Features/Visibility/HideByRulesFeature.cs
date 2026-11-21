using System.ComponentModel;
using OhMyLockScreen.Models.Features.Visibility;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Services;

namespace OhMyLockScreen.Services.Features.Visibility;

[FeatureInfo("omls.features.visibility.hideByRules", "按规则隐藏")]
public sealed class HideByRulesFeature(IRulesetService service) : FeatureBase<ComponentPresenter, HideByRulesSettings>
{
    private RulesetObservation? _observation;

    public override void Attach(ComponentPresenter target)
    {
        Detach();
        Target = target;
        Settings.PropertyChanged += OnSettingsReplaced;
        Observe();
    }

    private void OnSettingsReplaced(object? sender, PropertyChangedEventArgs args) => Observe();

    private void Observe()
    {
        _observation?.Dispose();
        _observation = null;
        if (Target is not { } target) return;
        _observation = new(service, Settings.Ruleset, target.Dispatcher, result => target.SetVisibilitySuppressed(this, result));
    }

    public override void Detach()
    {
        if (Target is not { } target) return;
        Settings.PropertyChanged -= OnSettingsReplaced;
        _observation?.Dispose();
        _observation = null;
        target.SetVisibilitySuppressed(this, false);
        Target = null;
    }
}
