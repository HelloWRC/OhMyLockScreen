namespace OhMyLockScreen.Controls.Editor;

public sealed partial class BackgroundSettingsControl : BackgroundSettingsControlBase
{
    public BackgroundSettingsControl() => InitializeComponent();

    protected override void OnSettingsChanged() => Bindings.Update();
}
