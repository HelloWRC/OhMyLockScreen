using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace OhMyLockScreen.Shared.Abstractions.Controls;

public abstract class SettingsPageBase : Page
{
    protected SettingsPageBase()
    {
        NavigationCacheMode = NavigationCacheMode.Disabled;
    }

    /// <summary>按注册 ID 导航。显式保留历史时，系统返回按钮可返回当前设置页。</summary>
    protected bool NavigateToSettingsPage(string pageId, bool keepHistory = false) =>
        Frame?.Tag is Func<string, bool, bool> navigate && navigate(pageId, keepHistory);
}
