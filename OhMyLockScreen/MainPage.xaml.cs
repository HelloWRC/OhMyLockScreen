using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Models.Scene;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen;

/// <summary>Lock screen scene and animated dismissal to the system sign-in screen.</summary>
public sealed partial class MainPage : Page
{
    private readonly ISceneService? _scenes;
    private Window? viewWindow;
    private bool isPageLoaded;
    private bool isDismissing;
    private bool hasExited;
    public MainPage()
    {
        InitializeComponent();
        _scenes = IAppHost.TryGetService<ISceneService>();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (isPageLoaded || isDismissing)
        {
            return;
        }

        isPageLoaded = true;
        viewWindow = Window.Current;
        if (_scenes != null)
        {
            RefreshActiveScene();
            _scenes.SceneSaved += OnSceneSaved;
            _scenes.ActiveSceneChanged += OnSceneSaved;
        }

        viewWindow.CoreWindow.PointerPressed += OnPointerPressed;
        viewWindow.CoreWindow.KeyDown += OnKeyDown;
        // Alt and other system keys may be delivered as accelerator events.
        viewWindow.CoreWindow.Dispatcher.AcceleratorKeyActivated += OnAcceleratorKeyActivated;
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        isPageLoaded = false;
        DetachViewEvents();
        if (isDismissing)
        {
            // An unloaded view cannot be relied on to finish its storyboard.
            await ExitAsync();
        }
    }

    private void DetachViewEvents()
    {
        if (_scenes != null)
        {
            _scenes.SceneSaved -= OnSceneSaved;
            _scenes.ActiveSceneChanged -= OnSceneSaved;
        }

        if (viewWindow != null)
        {
            viewWindow.CoreWindow.PointerPressed -= OnPointerPressed;
            viewWindow.CoreWindow.KeyDown -= OnKeyDown;
            viewWindow.CoreWindow.Dispatcher.AcceleratorKeyActivated -= OnAcceleratorKeyActivated;
            viewWindow = null;
        }
    }

    internal void RefreshActiveScene()
    {
        try
        {
            _scenes?.Reload();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法重新加载锁屏场景设置：{exception}");
        }

        LoadActiveScene();
    }

    private void LoadActiveScene()
    {
        try
        {
            if (_scenes != null)
            {
                RootScenePresenter.Scene = _scenes.Load(_scenes.ActiveSceneId);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法加载当前锁屏场景：{exception}");
        }
    }

    private async void OnSceneSaved(object? sender, SceneChangedEventArgs args)
    {
        if (_scenes == null || args.SceneId != _scenes.ActiveSceneId || viewWindow == null)
        {
            return;
        }

        try
        {
            await viewWindow.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (isPageLoaded && !isDismissing)
                {
                    LoadActiveScene();
                }
            });
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法刷新锁屏场景：{exception}");
        }
    }

    private void OnPointerPressed(CoreWindow sender, PointerEventArgs e)
    {
        e.Handled = true;
        RequestDismissal();
    }

    private void OnKeyDown(CoreWindow sender, KeyEventArgs e)
    {
        e.Handled = true;
        RequestDismissal();
    }

    private void OnAcceleratorKeyActivated(CoreDispatcher sender, AcceleratorKeyEventArgs e)
    {
        if (e.EventType is CoreAcceleratorKeyEventType.KeyDown or CoreAcceleratorKeyEventType.SystemKeyDown)
        {
            e.Handled = true;
            RequestDismissal();
        }
    }

    private void RequestDismissal()
    {
        BeginDismissal();
    }

    private async void BeginDismissal()
    {
        if (!isPageLoaded || isDismissing)
        {
            return;
        }

        isDismissing = true;
        DetachViewEvents();
        try
        {
            await RootScenePresenter.PlayUnlockAnimationAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法启动锁屏退出动画: {exception}");
        }

        await ExitAsync();
    }

    private async Task ExitAsync()
    {
        if (hasExited)
        {
            return;
        }

        hasExited = true;
        DetachViewEvents();
        if (Application.Current is App app)
        {
            await app.ExitAsync();
        }
        else
        {
            CoreApplication.Exit();
        }
    }
}
