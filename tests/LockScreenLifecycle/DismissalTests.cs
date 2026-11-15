using OhMyLockScreen;
using OhMyLockScreen.Services.LockScreenProvider;
using OhMyLockScreen.Shared.Controls.Scene;
using Windows.ApplicationModel.Core;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Animation;

internal static class DismissalTests
{
    internal static async Task RunAsync(
        Action<string, Action> run,
        Func<string, Func<Task>, Task> runAsync)
    {
        run("ScenePresenter lifts the layers and fades the entire background within 300 ms", () =>
        {
            var presenter = new ScenePresenter { ActualHeight = 720 };
            presenter.BackgroundLayerElement.Visibility = Visibility.Collapsed;

            var animationTask = presenter.PlayUnlockAnimationAsync();
            Check(ReferenceEquals(animationTask, presenter.PlayUnlockAnimationAsync()), "Repeated animation requests created another task.");
            var storyboard = Storyboard.LastCreated!;
            Check(storyboard.IsRunning && storyboard.BeginCalls == 1, "The scene animation did not start exactly once.");
            Check(storyboard.Children.Count == 3, "The storyboard did not animate foreground motion and both opacities.");

            var layerOpacity = FindAnimation(storyboard, presenter.LayersLayerElement);
            var layerTranslation = FindAnimation(storyboard, presenter.LayersTranslation);
            var backgroundOpacity = FindAnimation(storyboard, presenter.BackgroundViewportElement);
            CheckHasCurve(layerOpacity);
            CheckHasCurve(layerTranslation);
            CheckHasCurve(backgroundOpacity);

            storyboard.Advance(TimeSpan.FromMilliseconds(71.25));
            CheckVisuals(presenter, -150.75, 0.1625);

            storyboard.Advance(TimeSpan.FromMilliseconds(228.75));
            animationTask.GetAwaiter().GetResult();
            CheckVisuals(presenter, -180, 0);
            Check(!storyboard.IsRunning && storyboard.CompletedSubscribers == 0,
                "The completed layer animation leaked its callback.");
            presenter.Unload();
        });

        run("Unloading ScenePresenter releases an interrupted unlock animation", () =>
        {
            var presenter = new ScenePresenter { ActualHeight = 1080 };
            var animationTask = presenter.PlayUnlockAnimationAsync();
            var storyboard = Storyboard.LastCreated!;
            storyboard.Advance(TimeSpan.FromMilliseconds(80));

            presenter.Unload();
            animationTask.GetAwaiter().GetResult();
            Check(!storyboard.IsRunning && storyboard.CompletedSubscribers == 0,
                "Scene unload did not stop and release the animation.");
            storyboard.Advance(TimeSpan.FromMilliseconds(300));
            CheckVisuals(presenter, 0, 1);
        });

        run("Animation startup failure faults the scene task without leaking callbacks", () =>
        {
            Storyboard.FailOnBegin = true;
            var presenter = new ScenePresenter { ActualHeight = 1080 };

            var animationTask = presenter.PlayUnlockAnimationAsync();
            CheckThrows(animationTask);
            Check(Storyboard.LastCreated!.CompletedSubscribers == 0,
                "Failed animation startup left a completed callback attached.");
            presenter.Unload();
        });

        foreach (var deviceType in new[] { PointerDeviceType.Mouse, PointerDeviceType.Touch, PointerDeviceType.Pen })
        {
            await runAsync($"{deviceType} input plays the scene unlock animation and exits once", async () =>
            {
                var page = new MainPage();
                page.RootScenePresenter.ActualHeight = 720;
                page.Load();

                var pointer = page.PressPointer(deviceType).Pointer;
                Check(Storyboard.LastCreated == null && page.CapturedPointer == pointer,
                    "Pointer press dismissed the page before release.");
                page.ReleasePointer(pointer);
                var storyboard = Storyboard.LastCreated!;
                Check(storyboard.IsRunning && storyboard.BeginCalls == 1 && CoreApplication.ExitCalls == 0,
                    "Pointer input exited before the scene animation.");
                Check(page.PointerSubscribers == 0 && page.KeySubscribers == 0,
                    "Starting the unlock animation left lock-screen input attached.");

                storyboard.Advance(TimeSpan.FromMilliseconds(299));
                Check(CoreApplication.ExitCalls == 0, "The application exited before 300 ms.");
                storyboard.Advance(TimeSpan.FromMilliseconds(1));
                await WaitUntilAsync(() => CoreApplication.ExitCalls == 1);
                Check(page.RootScenePresenter.LayersTranslation.Y == -180,
                    "The lock view did not use the scene presenter's unlock animation.");
                page.Unload();
                Check(CoreApplication.ExitCalls == 1, "Unloading the page exited more than once.");
            });

            await runAsync($"{deviceType} upward drag follows the pointer and commits by distance on release", async () =>
            {
                var unlock = new FakeUnlockService(true);
                var page = LoadPage(unlock);
                var pointer = page.PressPointer(deviceType, position: new(100, 300)).Pointer;

                page.MovePointer(pointer, new(100, 240), 150_000);
                CheckVisuals(page.RootScenePresenter, -60, 2d / 3);
                page.MovePointer(pointer, new(100, 200), 300_000);
                CheckVisuals(page.RootScenePresenter, -100, 4d / 9);
                Check(Storyboard.LastCreated == null && unlock.Calls == 0, "Reaching the threshold dismissed before release.");

                page.ReleasePointer(pointer, new(100, 200), 550_000);
                var storyboard = Storyboard.LastCreated!;
                CheckVisuals(page.RootScenePresenter, -100, 4d / 9);
                Check(page.CapturedPointer == null && page.IsDismissalStarted && unlock.Calls == 0,
                    "The release did not transfer from drag to the unlock animation.");
                storyboard.Advance(TimeSpan.FromMilliseconds(300));
                await WaitUntilAsync(() => unlock.Calls == 1 && CoreApplication.ExitCalls == 1);
                CheckVisuals(page.RootScenePresenter, -180, 0);
                page.Unload();
            });
        }

        await RunGestureChecksAsync(run, runAsync);

        foreach (var key in new[] { VirtualKey.A, VirtualKey.Space, VirtualKey.Enter, VirtualKey.Escape })
        {
            await runAsync($"{key} starts the scene unlock animation", async () =>
            {
                var page = new MainPage();
                page.Load();
                var capturedKeyHandler = page.CaptureKeyDown(VirtualKey.A);
                page.PressKey(key);
                Window.Current.CoreWindow.Dispatcher.Drain();
                var storyboard = Storyboard.LastCreated!;
                Check(storyboard.IsRunning, "Key input did not start the scene animation.");

                capturedKeyHandler();
                storyboard.Advance(TimeSpan.FromMilliseconds(300));
                await WaitUntilAsync(() => CoreApplication.ExitCalls == 1);
                Check(storyboard.BeginCalls == 1, "Repeated input restarted the scene animation.");
                page.Unload();
            });
        }

        await runAsync("System unlock is requested 80 ms before the animation ends and exit waits for the animation", async () =>
        {
            var unlock = new FakeUnlockService(true);
            var page = new MainPage(unlock);
            page.RootScenePresenter.ActualHeight = 720;
            page.Load();

            page.ReleasePointer(page.PressPointer(PointerDeviceType.Mouse).Pointer);
            var storyboard = Storyboard.LastCreated!;
            var signalTimer = DispatcherTimer.LastCreated!;
            Check(storyboard.IsRunning && unlock.Preparations == 1 && unlock.Calls == 0,
                "The helper was not prepared during the animation or unlock was sent too early.");
            Check(signalTimer.IsEnabled && signalTimer.Interval == TimeSpan.FromMilliseconds(220),
                "The unlock signal was not scheduled 80 ms before the end.");

            storyboard.Advance(signalTimer.Interval);
            signalTimer.Advance();
            Check(unlock.Calls == 1 && CoreApplication.ExitCalls == 0 && storyboard.IsRunning,
                "The signal was not sent while the animation was still running.");
            Check(!signalTimer.IsEnabled && signalTimer.TickSubscribers == 0,
                "Sending unlock did not stop and detach the timer.");
            storyboard.Advance(TimeSpan.FromMilliseconds(79));
            Check(CoreApplication.ExitCalls == 0, "The application exited before the end of the animation.");
            storyboard.Advance(TimeSpan.FromMilliseconds(1));

            await WaitUntilAsync(() => unlock.Calls == 1 && CoreApplication.ExitCalls == 1);
            page.Unload();
            Check(unlock.Calls == 1 && CoreApplication.ExitCalls == 1,
                "System unlock or application exit ran more than once.");
        });

        await runAsync("Failed system unlock falls back to exiting exactly once", async () =>
        {
            var unlock = new FakeUnlockService(false);
            var page = new MainPage(unlock);
            page.RootScenePresenter.ActualHeight = 720;
            page.Load();

            page.ReleasePointer(page.PressPointer(PointerDeviceType.Mouse).Pointer);
            var storyboard = Storyboard.LastCreated!;
            var signalTimer = DispatcherTimer.LastCreated!;
            storyboard.Advance(signalTimer.Interval);
            signalTimer.Advance();
            Check(unlock.Calls == 1 && CoreApplication.ExitCalls == 0, "A failed early request cut short the animation.");
            storyboard.Advance(TimeSpan.FromMilliseconds(80));

            await WaitUntilAsync(() => CoreApplication.ExitCalls == 1);
            Check(unlock.Calls == 1, "Fallback exit did not attempt system unlock exactly once.");
            page.Unload();
            Check(CoreApplication.ExitCalls == 1, "Fallback exit ran more than once.");
        });

        await runAsync("Unloading during dismissal requests system unlock once before exit", async () =>
        {
            var unlock = new FakeUnlockService(true);
            var page = new MainPage(unlock);
            page.Load();
            page.PressKey(VirtualKey.A);
            Window.Current.CoreWindow.Dispatcher.Drain();
            var storyboard = Storyboard.LastCreated!;
            var signalTimer = DispatcherTimer.LastCreated!;
            storyboard.Advance(TimeSpan.FromMilliseconds(80));

            page.Unload();
            await WaitUntilAsync(() => unlock.Calls == 1);
            signalTimer.Advance();
            page.Unload();
            Check(unlock.Calls == 1 && CoreApplication.ExitCalls == 1 && !signalTimer.IsEnabled && signalTimer.TickSubscribers == 0,
                "Interrupted dismissal duplicated unlock or application exit.");
        });
        run("Keys consumed later in the route or by app accelerators preserve the lock view", () =>
        {
            var page = new MainPage();
            page.Load();
            var args = page.PressKey(VirtualKey.A);
            Check(Storyboard.LastCreated == null, "The page dismissed before input routing finished.");
            args.Handled = true;
            Window.Current.CoreWindow.Dispatcher.Drain();
            Check(Storyboard.LastCreated == null && CoreApplication.ExitCalls == 0,
                "The fallback ignored a key consumed by a later handler.");
            page.Unload();
        });

        run("Queued keyboard fallback cannot dismiss an unloaded and reloaded page", () =>
        {
            var page = new MainPage();
            page.Load();
            page.PressKey(VirtualKey.A);
            page.Unload();
            page.Load();
            Window.Current.CoreWindow.Dispatcher.Drain();
            Check(Storyboard.LastCreated == null && CoreApplication.ExitCalls == 0,
                "An old input callback dismissed a new page lifetime.");
            page.Unload();
        });

        foreach (var key in new[]
        {
            VirtualKey.Tab,
            VirtualKey.Shift, VirtualKey.LeftShift, VirtualKey.RightShift,
            VirtualKey.Control, VirtualKey.LeftControl, VirtualKey.RightControl,
            VirtualKey.Menu, VirtualKey.LeftMenu, VirtualKey.RightMenu,
            VirtualKey.LeftWindows, VirtualKey.RightWindows
        })
        {
            run($"{key} leaves focus navigation and component shortcuts available", () =>
            {
                var page = new MainPage();
                page.Load();
                var args = page.PressKey(key);
                Check(!args.Handled && Storyboard.LastCreated == null && CoreApplication.ExitCalls == 0,
                    "Focus navigation or a modifier key was intercepted before a component shortcut.");
                page.Unload();
            });
        }

        foreach (var eventType in new[]
        {
            CoreAcceleratorKeyEventType.KeyDown,
            CoreAcceleratorKeyEventType.SystemKeyDown
        })
        {
            run($"Raw {eventType} cannot bypass component keyboard handling", () =>
            {
                var page = new MainPage();
                page.Load();
                Window.Current.CoreWindow.Dispatcher.PressAccelerator(eventType, VirtualKey.Menu);
                Window.Current.CoreWindow.PressKey(VirtualKey.Space);
                Check(Storyboard.LastCreated == null && CoreApplication.ExitCalls == 0,
                    "A global keyboard event bypassed routed component handling.");
                page.Unload();
            });
        }

        foreach (var deviceType in new[] { PointerDeviceType.Mouse, PointerDeviceType.Touch, PointerDeviceType.Pen })
        {
            run($"Handled {deviceType} component input preserves the lock view", () =>
            {
                var page = new MainPage();
                page.Load();
                var pointer = page.PressPointer(deviceType, handled: true).Pointer;
                page.MovePointer(pointer, new(0, -100), 50_000);
                page.ReleasePointer(pointer, new(0, -100), 60_000);
                Window.Current.CoreWindow.PressPointer(deviceType);
                Check(Storyboard.LastCreated == null && CoreApplication.ExitCalls == 0,
                    "A component pointer interaction started dismissal.");
                Check(page.PointerSubscribers == 1 && page.KeySubscribers == 1,
                    "A component interaction detached the fallback handlers.");
                page.Unload();
            });
        }

        foreach (var key in new[] { VirtualKey.Space, VirtualKey.Enter, VirtualKey.Left, VirtualKey.Right })
        {
            run($"Handled {key} input in a component preserves the lock view", () =>
            {
                var page = new MainPage();
                page.Load();
                page.PressKey(key, handled: true);
                Window.Current.CoreWindow.Dispatcher.PressAccelerator(CoreAcceleratorKeyEventType.KeyDown, key);
                Check(Storyboard.LastCreated == null && CoreApplication.ExitCalls == 0,
                    "Handled component keyboard input started dismissal.");
                page.Unload();
            });
        }

        run("Pointer movement and key release keep the lock view visible", () =>
        {
            var page = new MainPage();
            page.Load();
            Window.Current.CoreWindow.MovePointer();
            Window.Current.CoreWindow.Dispatcher.PressAccelerator(CoreAcceleratorKeyEventType.KeyUp, VirtualKey.A);
            Window.Current.CoreWindow.Dispatcher.PressAccelerator(CoreAcceleratorKeyEventType.SystemKeyUp, VirtualKey.Menu);
            Check(Storyboard.LastCreated == null && CoreApplication.ExitCalls == 0,
                "Movement or key release started the unlock animation.");
            page.Unload();
        });

        run("Repeated loading and unloading keep input handlers on their owning window", () =>
        {
            var firstWindow = Window.Current;
            var firstPage = new MainPage();
            firstPage.Load();
            firstPage.Load();
            Check(firstPage.KeySubscribers == 1 && firstPage.PointerSubscribers == 1 &&
                firstWindow.CoreWindow.KeySubscribers == 0 &&
                firstWindow.CoreWindow.PointerSubscribers == 0 &&
                firstWindow.CoreWindow.Dispatcher.AcceleratorSubscribers == 0,
                "Repeated page loading duplicated input handlers.");

            var secondWindow = new Window();
            Window.Current = secondWindow;
            var secondPage = new MainPage();
            secondPage.Load();
            secondPage.Unload();
            Check(secondPage.KeySubscribers == 0 && secondPage.PointerSubscribers == 0 &&
                secondWindow.CoreWindow.KeySubscribers == 0 &&
                secondWindow.CoreWindow.PointerSubscribers == 0 &&
                secondWindow.CoreWindow.Dispatcher.AcceleratorSubscribers == 0 &&
                firstPage.KeySubscribers == 1,
                "Unloading one page leaked handlers or detached another view.");

            Window.Current = firstWindow;
            firstPage.Unload();
            firstPage.PressKey(VirtualKey.A);
            Check(Storyboard.LastCreated == null && CoreApplication.ExitCalls == 0,
                "An unloaded page still handled input.");
            firstPage.Load();
            Check(firstPage.KeySubscribers == 1 && firstPage.PointerSubscribers == 1,
                "Reloading a page did not restore exactly one input subscription.");
            firstPage.Unload();
        });

        await runAsync("Animation startup failure still exits the lock view once", async () =>
        {
            Storyboard.FailOnBegin = true;
            var page = new MainPage();
            page.Load();
            page.PressKey(VirtualKey.A);
            Window.Current.CoreWindow.Dispatcher.Drain();

            await WaitUntilAsync(() => CoreApplication.ExitCalls == 1);
            Check(Storyboard.LastCreated!.CompletedSubscribers == 0,
                "Failed animation startup leaked a completed callback.");
            page.Unload();
            Check(CoreApplication.ExitCalls == 1, "Failed animation startup exited more than once.");
        });

        await runAsync("Unloading the lock view during dismissal exits once", async () =>
        {
            var page = new MainPage();
            page.Load();
            page.PressKey(VirtualKey.A);
            Window.Current.CoreWindow.Dispatcher.Drain();
            var storyboard = Storyboard.LastCreated!;
            storyboard.Advance(TimeSpan.FromMilliseconds(80));

            page.Unload();
            await WaitUntilAsync(() => CoreApplication.ExitCalls == 1);
            page.Unload();
            Check(CoreApplication.ExitCalls == 1,
                "Interrupted unlock animation exited more than once.");
        });
    }

    private static async Task RunGestureChecksAsync(
        Action<string, Action> run, Func<string, Func<Task>, Task> runAsync)
    {
        await runAsync("A short fast flick commits at the distance and velocity minima", async () =>
        {
            var page = LoadPage();
            var pointer = page.PressPointer(PointerDeviceType.Touch).Pointer;
            page.ReleasePointer(pointer, new(0, -12), 20_000);

            Check(page.IsDismissalStarted, "A 12 DIP flick at 600 DIP/s did not commit.");
            CheckVisuals(page.RootScenePresenter, -12, 1 - 12d / 180);
            Storyboard.LastCreated!.Advance(TimeSpan.FromMilliseconds(300));
            await WaitUntilAsync(() => CoreApplication.ExitCalls == 1);
            page.Unload();
        });

        run("A pause clears flick velocity and the view returns in 180 ms", () =>
        {
            var unlock = new FakeUnlockService(true);
            var page = LoadPage(unlock);
            var pointer = page.PressPointer(PointerDeviceType.Touch).Pointer;
            page.MovePointer(pointer, new(0, -40), 20_000);
            page.ReleasePointer(pointer, new(0, -40), 130_000);

            var rebound = Storyboard.LastCreated!;
            Check(rebound.IsRunning && !page.IsDismissalStarted, "Stale velocity triggered dismissal.");
            CheckVisuals(page.RootScenePresenter, -40, 7d / 9);
            var other = page.PressPointer(PointerDeviceType.Touch, id: 2).Pointer;
            page.ReleasePointer(other);
            Check(page.CapturedPointer == null && ReferenceEquals(rebound, Storyboard.LastCreated),
                "A new press interrupted the rebound.");
            rebound.Advance(TimeSpan.FromMilliseconds(179));
            Check(rebound.IsRunning, "The rebound finished before 180 ms.");
            rebound.Advance(TimeSpan.FromMilliseconds(1));
            CheckVisuals(page.RootScenePresenter, 0, 1);
            Check(unlock.Calls == 0 && CoreApplication.ExitCalls == 0, "Rebound requested unlock.");
            page.Unload();
        });

        foreach (bool loseCapture in new[] { false, true })
        {
            run(loseCapture ? "Losing capture rebounds" : "Canceling the pointer rebounds", () =>
            {
                var page = LoadPage();
                var pointer = page.PressPointer(PointerDeviceType.Pen).Pointer;
                page.MovePointer(pointer, new(0, -220), 50_000);
                CheckVisuals(page.RootScenePresenter, -180, 0);
                var other = page.PressPointer(PointerDeviceType.Touch, id: 2).Pointer;
                page.MovePointer(other, new(0, 50), 60_000);
                page.ReleasePointer(other);
                CheckVisuals(page.RootScenePresenter, -180, 0);
                Check(page.CapturedPointer == pointer && Storyboard.LastCreated == null,
                    "A second pointer changed the active drag.");

                if (loseCapture)
                {
                    page.ReleasePointerCapture(pointer);
                }
                else
                {
                    page.CancelPointer(pointer);
                }

                var rebound = Storyboard.LastCreated!;
                page.ReleasePointer(pointer, new(0, -220), 60_000);
                Check(page.CapturedPointer == null && !page.IsDismissalStarted, "A canceled drag unlocked the view.");
                rebound.Advance(TimeSpan.FromMilliseconds(180));
                CheckVisuals(page.RootScenePresenter, 0, 1);
                page.Unload();
            });
        }

        run("Horizontal and downward drags, a reversed drag and non-left mouse input stay locked", () =>
        {
            var page = LoadPage();
            foreach (var position in new[] { new Point(120, -100), new Point(0, 120) })
            {
                var pointer = page.PressPointer(PointerDeviceType.Mouse).Pointer;
                page.MovePointer(pointer, position, 50_000);
                CheckVisuals(page.RootScenePresenter, 0, 1);
                page.ReleasePointer(pointer, position, 60_000);
            }

            var reversed = page.PressPointer(PointerDeviceType.Mouse).Pointer;
            page.MovePointer(reversed, new(0, -50), 40_000);
            page.ReleasePointer(reversed, new(0, 0), 50_000);
            var rightButton = page.PressPointer(PointerDeviceType.Mouse, leftButton: false).Pointer;
            page.ReleasePointer(rightButton);
            CheckVisuals(page.RootScenePresenter, 0, 1);
            Check(Storyboard.LastCreated == null && !page.IsDismissalStarted, "A non-dismissal gesture became a tap.");
            page.Unload();
        });

        run("Failed capture leaves input available and unloading a drag resets it", () =>
        {
            var page = LoadPage();
            page.FailPointerCapture = true;
            var ignored = page.PressPointer(PointerDeviceType.Touch);
            Check(!ignored.Handled && page.CapturedPointer == null, "An uncaptured press consumed the input.");
            page.ReleasePointer(ignored.Pointer);
            page.FailPointerCapture = false;
            var pointer = page.PressPointer(PointerDeviceType.Touch).Pointer;
            page.MovePointer(pointer, new(0, -125), 20_000);
            page.Unload();
            page.Load();
            page.ReleasePointer(pointer, new(0, -125), 30_000);
            CheckVisuals(page.RootScenePresenter, 0, 1);
            Check(page.CapturedPointer == null && Storyboard.LastCreated == null, "Unloading left an active drag.");
            page.Unload();
        });

        await runAsync("Keyboard dismissal starts from the current rebound frame", async () =>
        {
            var page = LoadPage();
            var presenter = page.RootScenePresenter;
            var pointer = page.PressPointer(PointerDeviceType.Touch).Pointer;
            page.MovePointer(pointer, new(0, -60), 150_000);
            page.ReleasePointer(pointer, new(0, -60), 260_000);
            var rebound = Storyboard.LastCreated!;
            rebound.Advance(TimeSpan.FromMilliseconds(40));
            double y = presenter.LayersTranslation.Y;
            double opacity = presenter.LayersLayerElement.Opacity;
            Check(y > -60 && y < 0, "The rebound did not move toward rest.");

            page.PressKey(VirtualKey.Enter);
            Window.Current.CoreWindow.Dispatcher.Drain();
            Check(!rebound.IsRunning && rebound.CompletedSubscribers == 0 && page.IsDismissalStarted,
                "Keyboard dismissal did not replace rebound.");
            CheckVisuals(presenter, y, opacity);
            Storyboard.LastCreated!.Advance(TimeSpan.FromMilliseconds(300));
            await WaitUntilAsync(() => CoreApplication.ExitCalls == 1);
            page.Unload();
        });
    }

    private static MainPage LoadPage(FakeUnlockService? unlock = null)
    {
        var page = new MainPage(unlock);
        page.RootScenePresenter.ActualHeight = 720;
        page.Load();
        return page;
    }

    private static void CheckVisuals(ScenePresenter presenter, double y, double opacity)
    {
        const double tolerance = 0.000001;
        Check(Math.Abs(presenter.LayersTranslation.Y - y) < tolerance &&
            Math.Abs(presenter.LayersLayerElement.Opacity - opacity) < tolerance &&
            Math.Abs(presenter.BackgroundViewportElement.Opacity - opacity) < tolerance,
            $"Expected offset {y} and opacity {opacity}, got offset {presenter.LayersTranslation.Y} " +
            $"and opacities {presenter.LayersLayerElement.Opacity}/{presenter.BackgroundViewportElement.Opacity}.");
    }

    private sealed class FakeUnlockService(bool result) : ILockScreenUnlockService
    {
        internal int Preparations { get; private set; }
        internal int Calls { get; private set; }

        public Func<Task<bool>> PrepareUnlock()
        {
            Preparations++;
            return () => TryUnlockAsync();
        }

        public Task<bool> TryUnlockAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private static DoubleAnimationUsingKeyFrames FindAnimation(Storyboard storyboard, object target)
    {
        return storyboard.Children
            .OfType<DoubleAnimationUsingKeyFrames>()
            .Single(animation => ReferenceEquals(animation.Target, target));
    }

    private static void CheckHasCurve(DoubleAnimationUsingKeyFrames animation)
    {
        var frame = animation.KeyFrames.OfType<SplineDoubleKeyFrame>().Single();
        Check(animation.Duration.TimeSpan == TimeSpan.FromMilliseconds(300) &&
            frame.KeyTime.TimeSpan == TimeSpan.FromMilliseconds(300) &&
            frame.KeySpline.ControlPoint1 == new Point(0.1, 0.9) &&
            frame.KeySpline.ControlPoint2 == new Point(0.2, 1),
            "The animation did not end at 300 ms with the (0.1,0.9,0.2,1) curve.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new Exception("Timed out waiting for the asynchronous animation completion.");
    }

    private static void CheckThrows(Task task)
    {
        try
        {
            task.GetAwaiter().GetResult();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new Exception("Animation startup failure completed successfully.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
