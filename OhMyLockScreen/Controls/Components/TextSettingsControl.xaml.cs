namespace OhMyLockScreen.Controls.Components;

public sealed partial class TextSettingsControl : TextSettingsControlBase
{
    public TextSettingsControl() => InitializeComponent();
    protected override void OnSettingsChanged() => Bindings.Update();
}