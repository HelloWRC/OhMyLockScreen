using OhMyLockScreen.Controls.Rules;
using OhMyLockScreen.Shared.Controls.Ruleset;
using OhMyLockScreen.Shared.Models.Ruleset;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace ComponentBackgroundPreview;

public sealed partial class RulesetPreviewEditor : UserControl, IRulesetDrawerHost
{
    public RulesetPreviewEditor() => InitializeComponent();

    public void OpenRulesetDrawer(Ruleset ruleset)
    {
        var editor = (RulesetControl)((DataTemplate)Resources["RuleEditor"]).LoadContent();
        editor.Ruleset = ruleset;
        Drawer.DrawerContent = editor;
        Drawer.IsDrawerOpen = true;
    }
}
