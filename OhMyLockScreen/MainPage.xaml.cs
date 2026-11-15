using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Services.LockScreenProvider;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Models.Scene;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace OhMyLockScreen;

/// <summary>Lock screen scene and animated dismissal to the system sign-in screen.</summary>
public sealed partial class MainPage : Page
{
    private const double DragSlop = 8;
    private const double MinimumFlickDistance = 12;
    private const double MinimumFlickVelocity = 600;
    private const ulong VelocityWindowMicroseconds = 100_000;
    private static readonly TimeSpan UnlockSignalLeadTime = TimeSpan.FromMilliseconds(80);

    private readonly ISceneService? _scenes;
    private readonly ILockScreenUnlockService? _lockScreenUnlockService;
    private readonly Queue<(ulong Timestamp, double Y)> pointerSamples = new();
    private Func<Task<bool>>? requestUnlock;
    private DispatcherTimer? unlockSignalTimer;
    private Task? unlockRequestTask;
    private Pointer? dismissalPointer;
    private Point pressPosition;
    private bool exceededDragSlop;
    private bool isDraggingUp;
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
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCanceled += OnPointerCanceled;
        PointerCaptureLost += OnPointerCanceled;
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
        else
        {
            RootScenePresenter.ResetUnlockDrag();
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
        PointerMoved -= OnPointerMoved;
        PointerReleased -= OnPointerReleased;
        PointerCanceled -= OnPointerCanceled;
        PointerCaptureLost -= OnPointerCanceled;
        KeyDown -= OnKeyDown;
        ReleaseDismissalPointer();

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
        if (e.Handled || dismissalPointer != null || RootScenePresenter.IsReturningFromUnlockDrag)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (!CapturePointer(e.Pointer))
        {
            return;
        }

        dismissalPointer = e.Pointer;
        pressPosition = point.Position;
        pointerSamples.Enqueue((point.Timestamp, point.Position.Y));
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (dismissalPointer?.PointerId != e.Pointer.PointerId)
        {
            return;
        }

        UpdateUnlockDrag(e.GetCurrentPoint(this));
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (dismissalPointer?.PointerId != e.Pointer.PointerId)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        double distance = UpdateUnlockDrag(point);
        var (timestamp, y) = pointerSamples.Peek();
        ulong elapsed = point.Timestamp - timestamp;
        double velocity = elapsed > 0 ? (y - point.Position.Y) * 1_000_000 / elapsed : 0;
        double height = RootScenePresenter.ActualHeight;
        bool shouldDismiss = !exceededDragSlop ||
            (isDraggingUp && height > 0 && (distance >= height / 8 ||
                (distance >= MinimumFlickDistance && velocity >= MinimumFlickVelocity)));

        ReleaseDismissalPointer();
        e.Handled = true;
        if (shouldDismiss)
        {
            BeginDismissal();
        }
        else
        {
            RootScenePresenter.PlayUnlockReturnAnimation();
        }
    }

    private double UpdateUnlockDrag(PointerPoint point)
    {
        double x = point.Position.X - pressPosition.X;
        double upwardDistance = pressPosition.Y - point.Position.Y;
        exceededDragSlop |= x * x + upwardDistance * upwardDistance > DragSlop * DragSlop;
        isDraggingUp |= upwardDistance > DragSlop && upwardDistance > Math.Abs(x);
        if (isDraggingUp)
        {
            RootScenePresenter.SetUnlockDragOffset(upwardDistance);
        }

        while (pointerSamples.TryPeek(out var sample) && point.Timestamp - sample.Timestamp > VelocityWindowMicroseconds)
        {
            pointerSamples.Dequeue();
        }

        pointerSamples.Enqueue((point.Timestamp, point.Position.Y));
        return upwardDistance;
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (dismissalPointer?.PointerId != e.Pointer.PointerId)
        {
            return;
        }

        ReleaseDismissalPointer();
        RootScenePresenter.PlayUnlockReturnAnimation();
    }

    private void ReleaseDismissalPointer()
    {
        var pointer = dismissalPointer;
        if (pointer == null)
        {
            return;
        }

        dismissalPointer = null;
        exceededDragSlop = false;
        isDraggingUp = false;
        pointerSamples.Clear();
        ReleasePointerCapture(pointer);
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
                    BeginDismissal();
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

    private async void BeginDismissal()
    {
        if (!isPageLoaded || isDismissing)
        {
            return;
        }

        isDismissing = true;
        requestUnlock = _lockScreenUnlockService?.PrepareUnlock();
        DetachViewEvents();
        try
        {
            var animation = RootScenePresenter.PlayUnlockAnimationAsync();
            if (requestUnlock != null && !animation.IsCompleted)
            {
                unlockSignalTimer = new DispatcherTimer { Interval = ScenePresenter.UnlockDuration - UnlockSignalLeadTime };
                unlockSignalTimer.Tick += OnUnlockSignalTimerTick;
                unlockSignalTimer.Start();
            }

            await animation;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法启动锁屏退出动画: {exception}");
        }

        await CompleteDismissalAsync();
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
        if (hasCompletedDismissal)
        {
            return;
        }

        hasCompletedDismissal = true;
        StopUnlockSignalTimer();
        DetachViewEvents();
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
