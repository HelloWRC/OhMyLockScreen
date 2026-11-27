namespace OhMyLockScreen.Controls.Editor;

public sealed partial class ThemeSettingsControl : ThemeSettingsControlBase
{
    public ThemeSettingsControl() => InitializeComponent();

    protected override void OnSettingsChanged() => Bindings.Update();
}
