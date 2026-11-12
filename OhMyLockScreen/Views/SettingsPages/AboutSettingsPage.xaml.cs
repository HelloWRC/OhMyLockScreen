using FluentIcons.Common;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Attributes;
using OhMyLockScreen.Shared.Enums.SettingsPages;

namespace OhMyLockScreen.Views.SettingsPages;

[SettingsPageInfo("about", "关于", Icon.Info, SettingsPageCategory.About)]
public sealed partial class AboutSettingsPage : SettingsPageBase
{
    public AboutSettingsPage()
    {
        InitializeComponent();
    }
}
