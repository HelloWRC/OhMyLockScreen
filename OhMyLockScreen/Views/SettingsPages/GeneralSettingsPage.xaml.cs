using FluentIcons.Common;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Attributes;
using OhMyLockScreen.Shared.Enums.SettingsPages;

namespace OhMyLockScreen.Views.SettingsPages;

[SettingsPageInfo("general", "通用", Icon.Settings, SettingsPageCategory.Internal)]
public sealed partial class GeneralSettingsPage : SettingsPageBase
{
    public GeneralSettingsPage()
    {
        InitializeComponent();
    }
}
