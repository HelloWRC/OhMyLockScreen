using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;

namespace OhMyLockScreen;

/// <summary>Lock screen clock and animated dismissal to the system sign-in screen.</summary>
public sealed partial class MainPage : Page
{
    private static readonly CultureInfo DateCulture = CultureInfo.GetCultureInfo("zh-CN");
    private readonly TimeProvider timeProvider;
    private readonly DispatcherTimer clockTimer;
    private Window? viewWindow;
    private Storyboard? dismissStoryboard;
    private bool isPageLoaded;
    private bool isDismissing;
    private bool hasExited;

    internal void ShowActivationKind(ActivationKind kind)
    {
        ActivationKindText.Text = $"App.OnActivated args.Kind：{kind}（{(int)kind}）";
    }

    public MainPage() : this(TimeProvider.System)
    {
    }

    internal MainPage(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
        InitializeComponent();
        clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
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
        viewWindow.Activated += OnWindowActivated;
        viewWindow.VisibilityChanged += OnWindowVisibilityChanged;
        viewWindow.CoreWindow.PointerPressed += OnPointerPressed;
        viewWindow.CoreWindow.KeyDown += OnKeyDown;
        // Alt and other system keys may be delivered as accelerator events.
        viewWindow.CoreWindow.Dispatcher.AcceleratorKeyActivated += OnAcceleratorKeyActivated;
        clockTimer.Tick += OnClockTick;
        UpdateClock();
        ResizeClock();
        clockTimer.Start();
        ShowDismissStatus(LockScreenHosting.IsWindowHostedByLockScreen() ? null : "预览模式：请从系统锁屏启动应用以退出到登录界面。");
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
        clockTimer.Stop();
        clockTimer.Tick -= OnClockTick;
        if (viewWindow != null)
        {
            viewWindow.Activated -= OnWindowActivated;
            viewWindow.VisibilityChanged -= OnWindowVisibilityChanged;
            viewWindow.CoreWindow.PointerPressed -= OnPointerPressed;
            viewWindow.CoreWindow.KeyDown -= OnKeyDown;
            viewWindow.CoreWindow.Dispatcher.AcceleratorKeyActivated -= OnAcceleratorKeyActivated;
            viewWindow = null;
        }
    }

    private void UpdateClock()
    {
        var now = timeProvider.GetLocalNow();
        TimeText.Text = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        DateText.Text = now.ToString("M月d日，dddd", DateCulture);
    }

    private void OnClockTick(object? sender, object e) => UpdateClock();

    private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState != CoreWindowActivationState.Deactivated)
        {
            UpdateClock();
        }
    }

    private void OnWindowVisibilityChanged(object sender, VisibilityChangedEventArgs e)
    {
        if (e.Visible)
        {
            UpdateClock();
            clockTimer.Start();
        }
        else
        {
            clockTimer.Stop();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ResizeClock();

    private void ResizeClock()
    {
        ClockViewbox.MaxWidth = Math.Max(1, ActualWidth - 64);
        ClockViewbox.MaxHeight = Math.Max(1, ActualHeight - 128);
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
        if (!isPageLoaded || isDismissing || hasExited)
        {
            return;
        }

        if (!LockScreenHosting.IsWindowHostedByLockScreen())
        {
            ShowDismissStatus("预览模式：请从系统锁屏启动应用以退出到登录界面。");
            return;
        }

        ShowDismissStatus(null);
        BeginDismissal();
    }

    private void ShowDismissStatus(string? message)
    {
        DismissStatusText.Text = message ?? string.Empty;
        DismissStatusPanel.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
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
            var animationTime = TimeSpan.FromMilliseconds(333);
            var duration = new Duration(animationTime);
            var slide = new DoubleAnimationUsingKeyFrames { Duration = duration };
            slide.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
                Value = 0
            });
            slide.KeyFrames.Add(new SplineDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(animationTime),
                Value = -ActualHeight,
                KeySpline = new KeySpline
                {
                    ControlPoint1 = new Point(0, 0),
                    ControlPoint2 = new Point(0, 1)
                }
            });
            Storyboard.SetTarget(slide, ForegroundTranslation);
            Storyboard.SetTargetProperty(slide, "Y");

            var fade = new DoubleAnimation { From = 1, To = 0, Duration = duration };
            Storyboard.SetTarget(fade, BackgroundPlaceholder);
            Storyboard.SetTargetProperty(fade, "Opacity");

            dismissStoryboard = new Storyboard();
            dismissStoryboard.Children.Add(slide);
            dismissStoryboard.Children.Add(fade);
            dismissStoryboard.Completed += OnDismissCompleted;
            dismissStoryboard.Begin();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法启动锁屏退出动画: {exception}");
            await ExitAsync();
        }
    }

    private async void OnDismissCompleted(object? sender, object e) => await ExitAsync();

    private async Task ExitAsync()
    {
        if (hasExited)
        {
            return;
        }

        hasExited = true;
        DetachViewEvents();
        // Preserve the transparent final frame while hosted services shut down.
        ForegroundTranslation.Y = -ActualHeight;
        BackgroundPlaceholder.Opacity = 0;
        if (dismissStoryboard != null)
        {
            dismissStoryboard.Completed -= OnDismissCompleted;
            dismissStoryboard.Stop();
            dismissStoryboard = null;
        }

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
