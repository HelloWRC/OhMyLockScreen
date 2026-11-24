using OhMyLockScreen.Controls;
using OhMyLockScreen.Services.LockScreenProvider;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Controls.Scene;
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
    private static readonly TimeSpan UnlockSignalLeadTime = TimeSpan.FromMilliseconds(80);

    private readonly ISceneService? _scenes;
    private readonly ILockScreenUnlockService? _lockScreenUnlockService;
    private readonly LockScreenDismissalController _dismissal;
    private Func<Task<bool>>? requestUnlock;
    private DispatcherTimer? unlockSignalTimer;
    private Task? unlockRequestTask;
    private CoreDispatcher? viewDispatcher;
    private bool isPageLoaded;

    internal bool IsDismissalStarted => _dismissal.IsDismissalStarted;

    public MainPage() : this(IAppHost.TryGetService<ILockScreenUnlockService>())
    {
    }

    internal MainPage(ILockScreenUnlockService? lockScreenUnlockService)
    {
        InitializeComponent();
        RootScenePresenter.ParallaxInputSurface = this;
        _scenes = IAppHost.TryGetService<ISceneService>();
        _lockScreenUnlockService = lockScreenUnlockService;
        _dismissal = new LockScreenDismissalController(
            this,
            RootScenePresenter,
            OnPreparingDismissal,
            OnDismissalAnimationStarted,
            CompleteDismissalAsync);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (isPageLoaded || IsDismissalStarted)
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

        _dismissal.Attach(viewDispatcher);
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        isPageLoaded = false;
        DetachSceneSubscriptions();
        await _dismissal.HandleUnloadedAsync();
    }

    private void DetachSceneSubscriptions()
    {
        if (_scenes != null)
        {
            _scenes.SceneSaved -= OnSceneSaved;
            _scenes.ActiveSceneChanged -= OnSceneSaved;
        }

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
                if (isPageLoaded && !IsDismissalStarted)
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

    private void OnPreparingDismissal()
    {
        requestUnlock = _lockScreenUnlockService?.PrepareUnlock();
        DetachSceneSubscriptions();
    }

    private void OnDismissalAnimationStarted(Task animation)
    {
        if (requestUnlock != null && !animation.IsCompleted)
        {
            unlockSignalTimer = new DispatcherTimer
            {
                Interval = ScenePresenter.UnlockDuration - UnlockSignalLeadTime
            };
            unlockSignalTimer.Tick += OnUnlockSignalTimerTick;
            unlockSignalTimer.Start();
        }
    }

    private void OnUnlockSignalTimerTick(object? sender, object args)
    {
        StopUnlockSignalTimer();
        unlockRequestTask ??= SendUnlockRequestAsync();
    }

    private void StopUnlockSignalTimer()
    {
        if (unlockSignalTimer is { } timer)
        {
            unlockSignalTimer = null;
            timer.Stop();
            timer.Tick -= OnUnlockSignalTimerTick;
        }
    }

    private async Task SendUnlockRequestAsync()
    {
        if (requestUnlock is { } send)
        {
            requestUnlock = null;
            try
            {
                await send();
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"无法请求系统解锁，将回退退出应用：{exception}");
            }
        }
    }

    private async Task CompleteDismissalAsync()
    {
        StopUnlockSignalTimer();
        DetachSceneSubscriptions();
        await (unlockRequestTask ??= SendUnlockRequestAsync());

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
