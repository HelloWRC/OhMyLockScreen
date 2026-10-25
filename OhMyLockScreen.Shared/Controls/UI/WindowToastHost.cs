using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using OhMyLockScreen.Shared.Models.UI;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>每个 UWP 视图独立持有的导航内容与窗口 Toast 层。</summary>
public sealed class WindowToastHost : Grid
{
    private readonly Window _window;
    private readonly CoreApplicationViewTitleBar _titleBar;
    private FrameworkElement? _page;
    private bool _attached;

    public Frame Frame { get; }
    public AppToastAdorner Toasts { get; } = new();

    public WindowToastHost(Window window, Frame frame)
    {
        _window = window;
        _titleBar = CoreApplication.GetCurrentView().TitleBar;
        Frame = frame;
        Children.Add(frame);
        Children.Add(Toasts);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Attach();
    }

    public void Show(ToastMessage message)
    {
        VerifyAccess();
        if (!_attached)
        {
            throw new InvalidOperationException("Toast 宿主已卸载。");
        }

        Toasts.Show(message);
    }

    public void Clear()
    {
        VerifyAccess();
        Toasts.Clear();
    }

    internal static WindowToastHost Find(Window window)
    {
        if (!window.Dispatcher.HasThreadAccess)
        {
            throw new InvalidOperationException("请在目标窗口的 UI 线程显示 Toast。");
        }

        return window.Content as WindowToastHost
            ?? throw new InvalidOperationException("窗口尚未连接 Toast 宿主。");
    }

    internal static WindowToastHost Find(FrameworkElement source)
    {
        if (!source.Dispatcher.HasThreadAccess)
        {
            throw new InvalidOperationException("请在控件所属的 UI 线程显示 Toast。");
        }

        for (DependencyObject? current = source; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is WindowToastHost host)
            {
                return host;
            }
        }

        // Popups have a separate visual tree, but share their owner's XamlRoot.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362) && source.XamlRoot?.Content is WindowToastHost root)
        {
            return root;
        }

        throw new InvalidOperationException("控件尚未连接窗口 Toast 宿主。");
    }

    private void VerifyAccess()
    {
        if (!Dispatcher.HasThreadAccess)
        {
            throw new InvalidOperationException("请在 Toast 宿主所属的 UI 线程操作。");
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs args) => Attach();
    private void OnUnloaded(object sender, RoutedEventArgs args) => Detach();
    private void OnClosed(object sender, CoreWindowEventArgs args) => Detach();

    private void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        _window.Closed += OnClosed;
        Frame.Navigated += OnNavigated;
        LayoutUpdated += OnLayoutUpdated;
        UpdatePage();
    }

    private void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        _window.Closed -= OnClosed;
        Frame.Navigated -= OnNavigated;
        LayoutUpdated -= OnLayoutUpdated;
        if (_page != null)
        {
            _page.ActualThemeChanged -= OnThemeChanged;
            _page = null;
        }

        Toasts.Clear();
    }

    private void OnNavigated(object sender, NavigationEventArgs args) => UpdatePage();

    private void UpdatePage()
    {
        if (_page != null)
        {
            _page.ActualThemeChanged -= OnThemeChanged;
        }

        _page = Frame.Content as FrameworkElement;
        if (_page != null)
        {
            _page.ActualThemeChanged += OnThemeChanged;
        }

        UpdateTheme();
    }

    private void OnThemeChanged(FrameworkElement sender, object args) => UpdateTheme();
    private void UpdateTheme() => Toasts.RequestedTheme = _page?.ActualTheme ?? ActualTheme;

    private void OnLayoutUpdated(object? sender, object args)
    {
        var top = _titleBar.ExtendViewIntoTitleBar && _titleBar.IsVisible ? _titleBar.Height : 0;
        if (Toasts.TopInset != top)
        {
            Toasts.TopInset = top;
        }
    }
}
