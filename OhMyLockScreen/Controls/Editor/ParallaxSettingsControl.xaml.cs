namespace OhMyLockScreen.Controls.Editor;

public sealed partial class ParallaxSettingsControl : ParallaxSettingsControlBase
{
    public ParallaxSettingsControl()
    {
        InitializeComponent();
    }

    protected override void OnSettingsChanged()
    {
        if (SettingsInternal != null)
        {
            Bindings.Update();
        }
    }

    private string FormatStrength(double strength)
    {
        return $"视差强度 {strength:0}%";
    }
}
