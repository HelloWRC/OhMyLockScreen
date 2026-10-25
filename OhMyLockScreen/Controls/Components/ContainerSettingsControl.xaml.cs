namespace OhMyLockScreen.Controls.Components;

public sealed partial class ContainerSettingsControl : ContainerSettingsControlBase
{
    public ContainerSettingsControl() => InitializeComponent();
    protected override void OnSettingsChanged() => Bindings.Update();
}