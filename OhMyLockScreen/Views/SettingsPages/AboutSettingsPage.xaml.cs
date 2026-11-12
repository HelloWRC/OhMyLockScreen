using FluentIcons.Common;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Attributes;
using OhMyLockScreen.Shared.Enums.SettingsPages;
using Windows.ApplicationModel;

namespace OhMyLockScreen.Views.SettingsPages;

[SettingsPageInfo("about", "关于", Icon.Info, SettingsPageCategory.About)]
public sealed partial class AboutSettingsPage : SettingsPageBase
{
    public string DisplayVersion { get; } = GetDisplayVersion();

    public AboutSettingsPage()
    {
        InitializeComponent();
    }

    private static string GetDisplayVersion()
    {
        var version = Package.Current.Id.Version;
        return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }
}
