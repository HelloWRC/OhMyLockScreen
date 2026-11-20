using OhMyLockScreen.Models.Features.Visibility;
using OhMyLockScreen.Models.Rules;
using OhMyLockScreen.Shared.Abstractions.Controls;

namespace OhMyLockScreen.Controls.Rules;

// XAML controls are exercised by the separate UWP preview package.
public sealed class CurrentWeatherRuleControl : RuleSettingsControlBase<CurrentWeatherRuleSettings>;
public sealed class RainTimeRuleControl : RuleSettingsControlBase<RainTimeRuleSettings>;
public sealed class MediaFieldRuleControl : RuleSettingsControlBase<MediaFieldRuleSettings>;
public sealed class HideByRulesSettingsControl : FeatureSettingsControlBase<HideByRulesSettings>;
