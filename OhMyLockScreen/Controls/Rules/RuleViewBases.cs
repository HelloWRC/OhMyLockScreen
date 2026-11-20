using OhMyLockScreen.Models.Features.Visibility;
using OhMyLockScreen.Models.Rules;
using OhMyLockScreen.Shared.Abstractions.Controls;

namespace OhMyLockScreen.Controls.Rules;

public abstract class CurrentWeatherRuleControlBase : RuleSettingsControlBase<CurrentWeatherRuleSettings>;
public abstract class RainTimeRuleControlBase : RuleSettingsControlBase<RainTimeRuleSettings>;
public abstract class MediaFieldRuleControlBase : RuleSettingsControlBase<MediaFieldRuleSettings>;
public abstract class HideByRulesSettingsControlBase : FeatureSettingsControlBase<HideByRulesSettings>;
