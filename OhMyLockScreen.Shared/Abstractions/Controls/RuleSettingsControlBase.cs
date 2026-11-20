namespace OhMyLockScreen.Shared.Abstractions.Controls;

public abstract class RuleSettingsControlBase : ControlWithSettingsBase;

public abstract class RuleSettingsControlBase<TSettings> : RuleSettingsControlBase where TSettings : class
{
    public TSettings Settings => (TSettings)SettingsInternal!;
}
