namespace OhMyLockScreen.Controls.Components;

public sealed partial class ClockSettingsControl : ClockSettingsControlBase
{
    public ClockSettingsControl() => InitializeComponent();
    protected override void OnSettingsChanged() => Bindings.Update();
}