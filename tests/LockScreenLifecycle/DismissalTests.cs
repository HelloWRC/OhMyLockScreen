using OhMyLockScreen;
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
        run("ScenePresenter fades and lifts all layers within 333 ms", () =>
        {
            var presenter = new ScenePresenter { ActualHeight = 720 };
            presenter.BackgroundLayerElement.Visibility = Visibility.Collapsed;

            var animationTask = presenter.PlayUnlockAnimationAsync();
            Check(ReferenceEquals(animationTask, presenter.PlayUnlockAnimationAsync()), "Repeated animation requests created another task.");
            var storyboard = Storyboard.LastCreated!;
            Check(storyboard.IsRunning && storyboard.BeginCalls == 1, "The scene animation did not start exactly once.");
            Check(storyboard.Children.Count == 2, "A scene without a background animated an extra visual.");

            var layerOpacity = FindAnimation(storyboard, presenter.LayersLayerElement);
            var layerTranslation = FindAnimation(storyboard, presenter.LayersTranslation);
            Check(layerOpacity.Duration.TimeSpan == TimeSpan.FromMilliseconds(333) &&
                layerTranslation.Duration.TimeSpan == TimeSpan.FromMilliseconds(333),
                "Layer animations did not use the 333 ms duration.");
            CheckHasCurve(layerOpacity);
            CheckHasCurve(layerTranslation);

            storyboard.Advance(TimeSpan.FromMilliseconds(41.625));
            Check(Math.Abs(presenter.LayersLayerElement.Opacity - 0.5) < 0.000001 &&
                Math.Abs(presenter.LayersTranslation.Y + 360) < 0.000001,
                "The layer opacity or upward motion did not use the configured curve.");

            storyboard.Advance(TimeSpan.FromMilliseconds(291.375));
            animationTask.GetAwaiter().GetResult();
            Check(presenter.LayersLayerElement.Opacity == 0 && presenter.LayersTranslation.Y == -720,
                "The layers were not fully transparent and above the scene.");
            Check(!storyboard.IsRunning && storyboard.CompletedSubscribers == 0,
                "The completed layer animation leaked its callback.");
            presenter.Unload();
        });

        run("ScenePresenter fades a visible background with the layers", () =>
        {
            var presenter = new ScenePresenter { ActualHeight = 480 };
            presenter.BackgroundLayerElement.Visibility = Visibility.Visible;

            var animationTask = presenter.PlayUnlockAnimationAsync();
            var storyboard = Storyboard.LastCreated!;
            Check(storyboard.Children.Count == 3, "A visible background was not animated.");
            var backgroundOpacity = FindAnimation(storyboard, presenter.BackgroundLayerElement);
            CheckHasCurve(backgroundOpacity);

            storyboard.Advance(TimeSpan.FromMilliseconds(41.625));
            Check(Math.Abs(presenter.BackgroundLayerElement.Opacity - 0.5) < 0.000001,
                "The background did not fade in sync with the layers.");
            storyboard.Advance(TimeSpan.FromMilliseconds(291.375));
            animationTask.GetAwaiter().GetResult();
            Check(presenter.BackgroundLayerElement.Opacity == 0,
                "The background remained visible after the animation.");
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
            storyboard.Advance(TimeSpan.FromMilliseconds(333));
            Check(presenter.LayersTranslation.Y > -1080,
                "An unloaded scene continued to animate.");
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

                page.PressPointer(deviceType);
                var storyboard = Storyboard.LastCreated!;
                Check(storyboard.IsRunning && storyboard.BeginCalls == 1 && CoreApplication.ExitCalls == 0,
                    "Pointer input exited before the scene animation.");
                Check(page.PointerSubscribers == 0 && page.KeySubscribers == 0,
                    "Starting the unlock animation left lock-screen input attached.");

                storyboard.Advance(TimeSpan.FromMilliseconds(332));
                Check(CoreApplication.ExitCalls == 0, "The application exited before 333 ms.");
                storyboard.Advance(TimeSpan.FromMilliseconds(1));
                await WaitUntilAsync(() => CoreApplication.ExitCalls == 1);
                Check(page.RootScenePresenter.LayersTranslation.Y == -720,
                    "The lock view did not use the scene presenter's unlock animation.");
                page.Unload();
                Check(CoreApplication.ExitCalls == 1, "Unloading the page exited more than once.");
            });
        }

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
                storyboard.Advance(TimeSpan.FromMilliseconds(333));
                await WaitUntilAsync(() => CoreApplication.ExitCalls == 1);
                Check(storyboard.BeginCalls == 1, "Repeated input restarted the scene animation.");
                page.Unload();
            });
        }

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
                page.PressPointer(deviceType, handled: true);
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

    private static DoubleAnimationUsingKeyFrames FindAnimation(Storyboard storyboard, object target)
    {
        return storyboard.Children
            .OfType<DoubleAnimationUsingKeyFrames>()
            .Single(animation => ReferenceEquals(animation.Target, target));
    }

    private static void CheckHasCurve(DoubleAnimationUsingKeyFrames animation)
    {
        var frame = animation.KeyFrames.OfType<SplineDoubleKeyFrame>().Single();
        Check(frame.KeyTime.TimeSpan == TimeSpan.FromMilliseconds(333) &&
            frame.KeySpline.ControlPoint1 == new Point(0, 0) &&
            frame.KeySpline.ControlPoint2 == new Point(0, 1),
            "The animation did not end at 333 ms with the (0,0,0,1) curve.");
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
