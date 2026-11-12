using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Services.LockScreenProvider;
using OhMyLockScreen.Shared.Models.Scene;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace OhMyLockScreen;

/// <summary>Lock screen scene and animated dismissal to the system sign-in screen.</summary>
public sealed partial class MainPage : Page
{
    private readonly ISceneService? _scenes;
    private readonly ILockScreenUnlockService? _lockScreenUnlockService;
    private CoreDispatcher? viewDispatcher;
    private bool isPageLoaded;
    private bool isDismissing;
    private bool hasCompletedDismissal;
    private int inputGeneration;

    internal bool IsDismissalStarted => isDismissing || hasCompletedDismissal;

    public MainPage() : this(IAppHost.TryGetService<ILockScreenUnlockService>())
    {
    }

    internal MainPage(ILockScreenUnlockService? lockScreenUnlockService)
    {
        InitializeComponent();
        _scenes = IAppHost.TryGetService<ISceneService>();
        _lockScreenUnlockService = lockScreenUnlockService;
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
        viewDispatcher = Window.Current.CoreWindow.Dispatcher;
        if (_scenes != null)
        {
            RefreshActiveScene();
            _scenes.SceneSaved += OnSceneSaved;
            _scenes.ActiveSceneChanged += OnSceneSaved;
        }

        // Ordinary bubbling subscriptions let component class handlers consume input first.
        PointerPressed += OnPointerPressed;
        KeyDown += OnKeyDown;
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        isPageLoaded = false;
        DetachViewEvents();
        if (isDismissing)
        {
            // An unloaded view cannot be relied on to finish its storyboard.
            await CompleteDismissalAsync();
        }
    }

    private void DetachViewEvents()
    {
        ++inputGeneration;
        if (_scenes != null)
        {
            _scenes.SceneSaved -= OnSceneSaved;
            _scenes.ActiveSceneChanged -= OnSceneSaved;
        }

        PointerPressed -= OnPointerPressed;
        KeyDown -= OnKeyDown;

        viewDispatcher = null;
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
        var dispatcher = viewDispatcher;
        if (_scenes == null || args.SceneId != _scenes.ActiveSceneId || dispatcher == null)
        {
            return;
        }

        try
        {
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
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

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        e.Handled = true;
        RequestDismissal();
    }

    private async void QueueUnhandledKey(KeyRoutedEventArgs args)
    {
        var dispatcher = viewDispatcher;
        var generation = inputGeneration;
        if (dispatcher == null)
        {
            return;
        }

        try
        {
            // Let the remaining route and application accelerators consume the key first.
            await dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                if (isPageLoaded && !isDismissing && generation == inputGeneration && !args.Handled)
                {
                    args.Handled = true;
                    RequestDismissal();
                }
            });
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法调度锁屏按键处理：{exception}");
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Focus traversal and modifier chords finish after this event reaches the page.
        if (e.Handled || e.Key is VirtualKey.Tab
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            return;
        }

        QueueUnhandledKey(e);
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

        await CompleteDismissalAsync();
    }

    private async Task CompleteDismissalAsync()
    {
        if (hasCompletedDismissal)
        {
            return;
        }

        hasCompletedDismissal = true;
        DetachViewEvents();
        if (_lockScreenUnlockService != null)
        {
            try
            {
                await _lockScreenUnlockService.TryUnlockAsync();
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"无法请求系统解锁，将回退退出应用：{exception}");
            }
        }

        // End this UWP process after either unlock path so the next lock creates a fresh view.
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
