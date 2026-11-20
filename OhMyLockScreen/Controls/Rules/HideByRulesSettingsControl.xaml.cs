using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Controls.Rules;

internal interface IRulesetDrawerHost
{
    void OpenRulesetDrawer(Shared.Models.Ruleset.Ruleset ruleset);
}

public sealed partial class HideByRulesSettingsControl : HideByRulesSettingsControlBase
{
    public HideByRulesSettingsControl() => InitializeComponent();

    private void OnEditClick(object sender, RoutedEventArgs args)
    {
        for (DependencyObject? node = this; node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is not IRulesetDrawerHost editor) continue;
            editor.OpenRulesetDrawer(Settings.Ruleset);
            break;
        }
    }
}
