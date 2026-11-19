using System.Runtime.CompilerServices;
using System.Windows.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace OhMyLockScreen.Shared.Abstractions.Controls;

public abstract class SettingsPageBase : Page
{
    private static readonly ConditionalWeakTable<Window, DrawerRoute> DrawerRoutes = new();

    public static readonly ICommand OpenDrawerCommand = new DrawerCommand(true);
    public static readonly ICommand CloseDrawerCommand = new DrawerCommand(false);

    protected SettingsPageBase()
    {
        NavigationCacheMode = NavigationCacheMode.Disabled;
    }

    /// <summary>按注册 ID 导航。显式保留历史时，系统返回按钮可返回当前设置页。</summary>
    protected bool NavigateToSettingsPage(string pageId, bool keepHistory = false) =>
        Frame?.Tag is Func<string, bool, bool> navigate && navigate(pageId, keepHistory);

    protected void OpenDrawer(string key, bool useGlobalDataContext = false, object? dataContext = null)
    {
        var resource = Resources.ContainsKey(key) ? Resources[key] : Application.Current?.Resources[key];
        OpenDrawer(resource is DataTemplate template ? template.LoadContent() : resource, useGlobalDataContext, dataContext);
    }

    protected void OpenDrawer(object? content, bool useGlobalDataContext = false, object? dataContext = null)
    {
        if (!DrawerRoutes.TryGetValue(Window.Current, out var route) || !ReferenceEquals(route.Frame.Content, this))
        {
            return;
        }

        if (content is FrameworkElement element && !useGlobalDataContext)
        {
            element.DataContext = dataContext ?? this;
        }

        route.Open(content);
    }

    protected void CloseDrawer()
    {
        if (DrawerRoutes.TryGetValue(Window.Current, out var route) && ReferenceEquals(route.Frame.Content, this))
        {
            route.Close();
        }
    }

    public static IDisposable AttachDrawerRoute(Window window, Frame frame, Action<object?> open, Action close)
    {
        var route = new DrawerRoute(window, frame, open, close);
        DrawerRoutes.Remove(window);
        DrawerRoutes.Add(window, route);
        return route;
    }

    private sealed class DrawerRoute(Window window, Frame frame, Action<object?> open, Action close) : IDisposable
    {
        public Frame Frame { get; } = frame;
        public Action<object?> Open { get; } = open;
        public Action Close { get; } = close;

        public void Dispose()
        {
            if (DrawerRoutes.TryGetValue(window, out var route) && ReferenceEquals(route, this))
            {
                DrawerRoutes.Remove(window);
            }
        }
    }

    private sealed class DrawerCommand(bool open) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            if (DrawerRoutes.TryGetValue(Window.Current, out var route))
            {
                if (open)
                {
                    route.Open(parameter);
                }
                else
                {
                    route.Close();
                }
            }
        }
    }
}
