namespace OhMyLockScreen.Controls.Editor;

public sealed partial class ForegroundSettingsControl : ForegroundSettingsControlBase
{
    public ForegroundSettingsControl() => InitializeComponent();

    protected override void OnSettingsChanged() => Bindings.Update();
}
