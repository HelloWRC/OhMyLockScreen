using FluentIcons.Common;
using OhMyLockScreen.Models;
using OhMyLockScreen.Services;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Attributes;
using OhMyLockScreen.Shared.Enums.SettingsPages;

namespace OhMyLockScreen.Views.SettingsPages;

[SettingsPageInfo("general", "通用", Icon.Settings, SettingsPageCategory.Internal)]
public sealed partial class GeneralSettingsPage : SettingsPageBase
{
    public Settings Settings { get; }

    public GeneralSettingsPage()
    {
        Settings = IAppHost.GetRequiredService<SettingsService>().Settings;
        InitializeComponent();
    }
}
