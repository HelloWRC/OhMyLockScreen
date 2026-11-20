using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Attributes.Serialization;
using OhMyLockScreen.Shared.Models.Ruleset;

namespace OhMyLockScreen.Models.Features.Visibility;

[AutoJsonTypeName("omls.features.visibility.hideByRules")]
public sealed partial class HideByRulesSettings : ObservableObject, IFeatureSettings
{
    [ObservableProperty] private Ruleset _ruleset = new();
}
