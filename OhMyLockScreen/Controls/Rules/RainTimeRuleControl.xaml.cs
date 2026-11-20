namespace OhMyLockScreen.Controls.Rules;

public sealed partial class RainTimeRuleControl : RainTimeRuleControlBase
{
    public RainTimeRuleControl() => InitializeComponent();
    protected override void OnSettingsChanged() => Bindings.Update();
}
