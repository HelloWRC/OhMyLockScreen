using System.Globalization;
using OhMyLockScreen;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.Devices.Input;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

void CheckDetached(Window window)
{
    Check(window.ActivationSubscribers == 0 && window.VisibilitySubscribers == 0 &&
        window.CoreWindow.KeySubscribers == 0 && window.CoreWindow.PointerSubscribers == 0 &&
        window.CoreWindow.Dispatcher.AcceleratorSubscribers == 0, "Page left window event handlers attached.");
}

void Run(string name, Action test)
{
    using var environment = new TestEnvironment();
    test();
    passed++;
    Console.WriteLine($"PASS: {name}");
}

async Task RunAsync(string name, Func<Task> test)
{
    using var environment = new TestEnvironment();
    await test();
    passed++;
    Console.WriteLine($"PASS: {name}");
}

Run("Hosted launch displays a transparent frame", () =>
{
    new App().Launch(new LaunchActivatedEventArgs());
    var frame = (Frame)Window.Current.Content!;
    Check(frame.Content is MainPage && frame.Background?.Color == Windows.UI.Colors.Transparent,
        "Hosted launch lost the page or transparent frame.");
});

Run("OnActivated displays its actual Kind for lock and other activation contracts", () =>
{
    var app = new App();
    foreach (var kind in new[] { ActivationKind.LockScreen, ActivationKind.LockScreenComponent, ActivationKind.Other, ActivationKind.Launch })
    {
        Window.Current = new Window();
        app.Activate(new ActivatedEventArgs(kind));
        Check(((Frame)Window.Current.Content!).Content is MainPage, "Lock activation lost its view.");
        var page = (MainPage)((Frame)Window.Current.Content!).Content!;
        Check(page.ActivationKindText.Text == $"App.OnActivated args.Kind：{kind}（{(int)kind}）",
            "OnActivated did not display the actual kind and numeric value.");
    }
});

Run("Reactivation updates the existing page and preserves its activation diagnostics", () =>
{
    var app = new App();
    app.Launch(new LaunchActivatedEventArgs());
    var page = (MainPage)((Frame)Window.Current.Content!).Content!;
    Check(page.ActivationKindText.Text.Contains("尚未触发"), "OnLaunched was incorrectly reported as OnActivated.");
    app.Activate(new ActivatedEventArgs(ActivationKind.LockScreen));
    app.Activate(new ActivatedEventArgs(ActivationKind.LockScreenComponent));
    Check(ReferenceEquals(page, ((Frame)Window.Current.Content!).Content) &&
        page.ActivationKindText.Text.Contains("LockScreenComponent"), "Reactivation did not update the existing page.");
    app.Launch(new LaunchActivatedEventArgs());
    Check(page.ActivationKindText.Text.Contains("LockScreenComponent"), "OnLaunched cleared the last actual OnActivated kind.");
    page.Unload();
});

Run("Clock immediately displays local 24-hour time and a Chinese date across midnight", () =>
{
    var clock = new ManualTimeProvider();
    var previousCulture = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var page = new MainPage(clock);
        page.Load();
        var timer = DispatcherTimer.LastCreated!;
        Check(page.TimeText.Text == "23:59" && page.DateText.Text == "10月22日，星期四", "Initial clock or Chinese date is wrong.");
        Check(timer.Interval == TimeSpan.FromSeconds(1) && timer.IsEnabled, "Clock is not scheduled every second.");
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        timer.Advance();
        Check(page.TimeText.Text == "00:00" && page.DateText.Text == "10月23日，星期五", "Midnight did not refresh both time and date.");
        page.Unload();
    }
    finally
    {
        CultureInfo.CurrentCulture = previousCulture;
    }
});

Run("Visibility and activation refresh the clock without duplicate subscriptions", () =>
{
    var clock = new ManualTimeProvider();
    var page = new MainPage(clock);
    page.Load();
    page.Load();
    var timer = DispatcherTimer.LastCreated!;
    Check(timer.TickSubscribers == 1 && Window.Current.ActivationSubscribers == 1 &&
        Window.Current.CoreWindow.KeySubscribers == 1 && Window.Current.CoreWindow.Dispatcher.AcceleratorSubscribers == 1,
        "Repeated loading duplicated subscriptions.");
    Window.Current.SetVisible(false);
    Check(!timer.IsEnabled, "Invisible page kept its clock running.");
    clock.UtcNow = clock.UtcNow.AddHours(2);
    Window.Current.SetVisible(true);
    Check(page.TimeText.Text == "01:59" && timer.IsEnabled, "Becoming visible did not immediately refresh the clock.");
    clock.UtcNow = clock.UtcNow.AddMinutes(3);
    Window.Current.Activate();
    Check(page.TimeText.Text == "02:02", "Activation did not refresh the clock.");
    page.Unload();
});

foreach (var deviceType in new[] { PointerDeviceType.Mouse, PointerDeviceType.Touch, PointerDeviceType.Pen })
{
    Run($"{deviceType} press animates dismissal and exits", () =>
    {
        var page = new MainPage();
        page.Load();
        var timer = DispatcherTimer.LastCreated!;
        Window.Current.CoreWindow.PressPointer(deviceType);
        var animation = Storyboard.LastCreated!;
        Check(animation.IsRunning && CoreApplication.ExitCalls == 0, "Input exited before the animation.");
        Check(!timer.IsEnabled && timer.TickSubscribers == 0, "Animation left the clock active.");
        CheckDetached(Window.Current);
        // cubic-bezier(0,0,0,1) reaches half its distance at one eighth of its duration.
        animation.Advance(TimeSpan.FromMilliseconds(41.625));
        Check(Math.Abs(page.ForegroundTranslation.Y + 540) < 0.000001 && page.BackgroundPlaceholder.Opacity == 0.875 && CoreApplication.ExitCalls == 0,
            "Slide easing or simultaneous background fade is wrong.");
        animation.Advance(TimeSpan.FromMilliseconds(290.375));
        Check(CoreApplication.ExitCalls == 0, "Application exited before 333 ms.");
        animation.Advance(TimeSpan.FromMilliseconds(1));
        Check(CoreApplication.ExitCalls == 1, "Completion did not exit exactly once.");
        Check(page.ForegroundTranslation.Y == -1080 && page.BackgroundPlaceholder.Opacity == 0,
            "Final frame is not fully slid away and transparent.");
        Check(animation.CompletedSubscribers == 0 && !animation.IsRunning, "Completed animation was not released.");
        animation.Advance(TimeSpan.FromSeconds(1));
        page.Unload();
        Check(CoreApplication.ExitCalls == 1, "Repeated completion or unload exited twice.");
    });
}

foreach (var key in new[] { VirtualKey.A, VirtualKey.Space, VirtualKey.Enter, VirtualKey.Escape, VirtualKey.Shift, VirtualKey.Control })
{
    Run($"{key} key starts dismissal", () =>
    {
        var page = new MainPage();
        page.Load();
        Window.Current.CoreWindow.PressKey(key);
        Check(Storyboard.LastCreated is { IsRunning: true } && CoreApplication.ExitCalls == 0, "Key did not start an animated dismissal.");
        Storyboard.LastCreated!.Advance(TimeSpan.FromMilliseconds(333));
        Check(CoreApplication.ExitCalls == 1, "Key dismissal did not exit.");
    });
}

foreach (var eventType in new[] { CoreAcceleratorKeyEventType.KeyDown, CoreAcceleratorKeyEventType.SystemKeyDown })
{
    Run($"{eventType} accelerator including Alt starts dismissal", () =>
    {
        var page = new MainPage();
        page.Load();
        Window.Current.CoreWindow.Dispatcher.PressAccelerator(eventType, VirtualKey.Menu);
        Check(Storyboard.LastCreated is { IsRunning: true }, "Accelerator did not start dismissal.");
        Storyboard.LastCreated!.Advance(TimeSpan.FromMilliseconds(333));
        Check(CoreApplication.ExitCalls == 1, "Accelerator dismissal did not exit.");
    });
}

Run("Pointer movement and key release do not dismiss the page", () =>
{
    var page = new MainPage();
    page.Load();
    Window.Current.CoreWindow.MovePointer();
    Window.Current.CoreWindow.Dispatcher.PressAccelerator(CoreAcceleratorKeyEventType.KeyUp, VirtualKey.A);
    Window.Current.CoreWindow.Dispatcher.PressAccelerator(CoreAcceleratorKeyEventType.SystemKeyUp, VirtualKey.Menu);
    Check(Storyboard.LastCreated == null && CoreApplication.ExitCalls == 0, "Movement or release started dismissal.");
    page.Unload();
});

Run("Rapid mixed input and reactivation do not restart a pending dismissal", () =>
{
    var page = new MainPage();
    page.Load();
    var pendingKey = Window.Current.CoreWindow.CaptureKeyDown(VirtualKey.Enter);
    Window.Current.CoreWindow.PressPointer(PointerDeviceType.Touch);
    var animation = Storyboard.LastCreated!;
    for (int i = 0; i < 20; i++)
    {
        pendingKey();
        Window.Current.CoreWindow.PressKey(VirtualKey.Space);
        Window.Current.CoreWindow.PressPointer(PointerDeviceType.Mouse);
        Window.Current.CoreWindow.Dispatcher.PressAccelerator(CoreAcceleratorKeyEventType.SystemKeyDown, VirtualKey.Menu);
    }
    Window.Current.Activate();
    Window.Current.SetVisible(true);
    page.Load();
    Check(ReferenceEquals(animation, Storyboard.LastCreated) && animation.BeginCalls == 1, "Repeated input restarted the animation.");
    CheckDetached(Window.Current);
    animation.Advance(TimeSpan.FromMilliseconds(333));
    Check(CoreApplication.ExitCalls == 1, "Repeated input caused duplicate exit.");
});

Run("Animation startup failure cleans up and exits directly", () =>
{
    Storyboard.FailOnBegin = true;
    var page = new MainPage();
    page.Load();
    Window.Current.CoreWindow.PressKey(VirtualKey.A);
    Check(CoreApplication.ExitCalls == 1 && page.BackgroundPlaceholder.Opacity == 0,
        "Animation failure prevented exit or left an opaque frame.");
    CheckDetached(Window.Current);
    Check(!DispatcherTimer.LastCreated!.IsEnabled && Storyboard.LastCreated!.CompletedSubscribers == 0,
        "Animation failure leaked resources.");
});

Run("Unload during animation stops it and completes exit once", () =>
{
    var page = new MainPage();
    page.Load();
    Window.Current.CoreWindow.PressKey(VirtualKey.A);
    var animation = Storyboard.LastCreated!;
    animation.Advance(TimeSpan.FromMilliseconds(100));
    page.Unload();
    Check(CoreApplication.ExitCalls == 1 && !animation.IsRunning && animation.CompletedSubscribers == 0,
        "Interrupted animation did not complete and release its exit.");
    animation.Advance(TimeSpan.FromMilliseconds(333));
    Check(CoreApplication.ExitCalls == 1, "Interrupted animation exited twice.");
    CheckDetached(Window.Current);
});

Run("Unload releases the clock and window handlers and permits a clean reload", () =>
{
    var clock = new ManualTimeProvider();
    var page = new MainPage(clock);
    page.Load();
    var timer = DispatcherTimer.LastCreated!;
    page.Unload();
    int reads = clock.Reads;
    Window.Current.Activate();
    Window.Current.SetVisible(true);
    Window.Current.CoreWindow.PressKey(VirtualKey.A);
    Window.Current.CoreWindow.PressPointer(PointerDeviceType.Mouse);
    timer.Advance();
    Check(clock.Reads == reads && !timer.IsEnabled && timer.TickSubscribers == 0 && CoreApplication.ExitCalls == 0,
        "Unloaded page kept handling events or exited unexpectedly.");
    CheckDetached(Window.Current);
    clock.UtcNow = clock.UtcNow.AddMinutes(2);
    page.Load();
    Check(page.TimeText.Text == "00:01" && timer.IsEnabled && timer.TickSubscribers == 1, "Reload did not resume a single clock.");
    page.Unload();
});

Run("Resizing constrains the clock and slides by the current page height", () =>
{
    var page = new MainPage();
    page.Load();
    page.Resize(320, 240);
    Check(page.ClockViewbox.MaxWidth == 256 && page.ClockViewbox.MaxHeight == 112, "Small viewport did not constrain clock scaling.");
    page.Resize(1280, 720);
    Window.Current.CoreWindow.PressKey(VirtualKey.Space);
    Storyboard.LastCreated!.Advance(TimeSpan.FromMilliseconds(333));
    Check(page.ForegroundTranslation.Y == -720, "Slide distance ignored the current viewport height.");
});

Run("Multiple views keep clock updates and input handlers on their owning window", () =>
{
    var firstWindow = Window.Current;
    var firstClock = new ManualTimeProvider();
    var firstPage = new MainPage(firstClock);
    firstPage.Load();
    var firstTimer = DispatcherTimer.LastCreated!;
    var secondWindow = new Window();
    Window.Current = secondWindow;
    var secondPage = new MainPage(new ManualTimeProvider());
    secondPage.Load();
    var secondTimer = DispatcherTimer.LastCreated!;
    secondPage.Unload();
    CheckDetached(secondWindow);
    Check(firstTimer.IsEnabled && !secondTimer.IsEnabled && firstWindow.CoreWindow.KeySubscribers == 1,
        "Unloading another view changed the first view.");
    Window.Current = firstWindow;
    firstClock.UtcNow = firstClock.UtcNow.AddMinutes(2);
    firstTimer.Advance();
    Check(firstPage.TimeText.Text == "00:01", "Clock updated on the wrong view.");
    firstPage.Unload();
    CheckDetached(firstWindow);
});

Run("Preview input keeps the page visible until the window is hosted", () =>
{
    LockScreenHosting.IsHosted = () => false;
    var page = new MainPage();
    page.Load();
    Check(page.DismissStatusPanel.Visibility == Visibility.Visible && page.DismissStatusText.Text.Contains("预览模式"),
        "Preview did not show an explanation.");
    Window.Current.CoreWindow.PressKey(VirtualKey.Enter);
    Window.Current.CoreWindow.PressPointer(PointerDeviceType.Mouse);
    Window.Current.CoreWindow.Dispatcher.PressAccelerator(CoreAcceleratorKeyEventType.SystemKeyDown, VirtualKey.Menu);
    Check(Storyboard.LastCreated == null && CoreApplication.ExitCalls == 0 && page.BackgroundPlaceholder.Opacity == 1,
        "Preview input dismissed the page.");
    Check(DispatcherTimer.LastCreated!.IsEnabled && Window.Current.CoreWindow.KeySubscribers == 1,
        "Preview input stopped the clock or detached input.");
    LockScreenHosting.IsHosted = () => true;
    Window.Current.CoreWindow.PressKey(VirtualKey.Enter);
    Storyboard.LastCreated!.Advance(TimeSpan.FromMilliseconds(333));
    Check(CoreApplication.ExitCalls == 1 && page.DismissStatusPanel.Visibility == Visibility.Collapsed,
        "Hosted window did not dismiss or clear the preview status.");
});

await InfrastructureTests.RunAsync(Run, RunAsync);

Console.WriteLine($"All {passed} checks passed using temporary settings and simulated UWP views and animations.");

internal sealed class ManualTimeProvider : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 22, 15, 59, 0, TimeSpan.Zero);
    public int Reads { get; private set; }
    public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone(
        "Test/HongKong", TimeSpan.FromHours(8), "Test Hong Kong", "Test Hong Kong");

    public override DateTimeOffset GetUtcNow()
    {
        Reads++;
        return UtcNow;
    }
}
