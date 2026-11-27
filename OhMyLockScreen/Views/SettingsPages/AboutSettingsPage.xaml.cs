using FluentIcons.Common;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Attributes;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Enums.SettingsPages;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Views.SettingsPages;

[SettingsPageInfo("about", "关于", Icon.Info, SettingsPageCategory.About)]
public sealed partial class AboutSettingsPage : SettingsPageBase
{
    private int _copyrightTapCount;

    public string DisplayVersion => App.AppVersionLong;

    public AboutSettingsPage()
    {
        InitializeComponent();
    }

    private void OnCopyrightTapped(object sender, RoutedEventArgs args)
    {
        for (var element = args.OriginalSource as DependencyObject;
             element != null && !ReferenceEquals(element, sender);
             element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase)
            {
                return;
            }
        }

        if (++_copyrightTapCount == 10)
        {
            _copyrightTapCount = 0;
            if (Window.Current.Content is WindowToastHost { Frame.Content: ApplicationSettingsPage } host)
            {
                host.Frame.Navigate(typeof(EasterPage));
            }
        }
    }
}
