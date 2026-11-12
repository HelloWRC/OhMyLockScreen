using FluentIcons.Common;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Attributes;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Views
{
    public sealed class ApplicationSettingsPage : Page;
}

namespace OhMyLockScreen.Views.SettingsPages
{
    public sealed partial class GeneralSettingsPage
    {
        private void InitializeComponent() { }
    }

    public sealed partial class AboutSettingsPage
    {
        private void InitializeComponent() { }
    }

    [SettingsPageInfo("wide-title", "Wide title", Icon.Settings)]
    [FullWidthPage]
    public sealed class WideTitleSettingsPage : SettingsPageBase;

    [SettingsPageInfo("hidden-title", "Hidden title", Icon.Settings)]
    [HidePageTitle]
    public sealed class HiddenTitleSettingsPage : SettingsPageBase;
}
