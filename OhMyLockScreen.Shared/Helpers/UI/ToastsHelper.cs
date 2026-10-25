using Microsoft.UI.Xaml.Controls;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Models.UI;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Shared.Helpers.UI;

/// <summary>在调用控件所属窗口显示 Toast；请在该窗口的 UI 线程调用。</summary>
public static class ToastsHelper
{
    public static void ShowToast(this FrameworkElement control, ToastMessage message) => control.ShowToastRef(message);
    public static void ShowToast(this FrameworkElement control, string message) => control.ShowToastRef(message);
    public static ToastMessage ShowToastRef(this FrameworkElement control, string message) => control.ShowToastRef(new ToastMessage(message));

    public static ToastMessage ShowToastRef(this FrameworkElement control, ToastMessage message)
    {
        WindowToastHost.Find(control).Show(message);
        return message;
    }

    public static void ShowWarningToast(this FrameworkElement control, string message) => control.ShowWarningToastRef(message);
    public static ToastMessage ShowWarningToastRef(this FrameworkElement control, string message) => control.ShowToastRef(Create(message, InfoBarSeverity.Warning));
    public static void ShowSuccessToast(this FrameworkElement control, string message) => control.ShowSuccessToastRef(message);
    public static ToastMessage ShowSuccessToastRef(this FrameworkElement control, string message) => control.ShowToastRef(Create(message, InfoBarSeverity.Success));
    public static void ShowErrorToast(this FrameworkElement control, string message) => control.ShowErrorToastRef(message);
    public static ToastMessage ShowErrorToastRef(this FrameworkElement control, string message) => control.ShowToastRef(Create(message, InfoBarSeverity.Error));
    public static void ShowErrorToast(this FrameworkElement control, string title, Exception exception) => control.ShowErrorToastRef(title, exception);
    public static ToastMessage ShowErrorToastRef(this FrameworkElement control, string title, Exception exception) => control.ShowToastRef(CreateError(title, exception));

    public static void ShowToast(this Window window, ToastMessage message) => window.ShowToastRef(message);
    public static void ShowToast(this Window window, string message) => window.ShowToastRef(message);
    public static ToastMessage ShowToastRef(this Window window, string message) => window.ShowToastRef(new ToastMessage(message));

    public static ToastMessage ShowToastRef(this Window window, ToastMessage message)
    {
        WindowToastHost.Find(window).Show(message);
        return message;
    }

    public static void ShowWarningToast(this Window window, string message) => window.ShowWarningToastRef(message);
    public static ToastMessage ShowWarningToastRef(this Window window, string message) => window.ShowToastRef(Create(message, InfoBarSeverity.Warning));
    public static void ShowSuccessToast(this Window window, string message) => window.ShowSuccessToastRef(message);
    public static ToastMessage ShowSuccessToastRef(this Window window, string message) => window.ShowToastRef(Create(message, InfoBarSeverity.Success));
    public static void ShowErrorToast(this Window window, string message) => window.ShowErrorToastRef(message);
    public static ToastMessage ShowErrorToastRef(this Window window, string message) => window.ShowToastRef(Create(message, InfoBarSeverity.Error));
    public static void ShowErrorToast(this Window window, string title, Exception exception) => window.ShowErrorToastRef(title, exception);
    public static ToastMessage ShowErrorToastRef(this Window window, string title, Exception exception) => window.ShowToastRef(CreateError(title, exception));

    private static ToastMessage Create(string message, InfoBarSeverity severity) => new(message)
    {
        Severity = severity,
        Duration = TimeSpan.FromSeconds(severity == InfoBarSeverity.Error ? 10 : 5)
    };

    private static ToastMessage CreateError(string title, Exception exception) => new(title, exception.Message)
    {
        Severity = InfoBarSeverity.Error,
        AutoClose = false
    };
}
