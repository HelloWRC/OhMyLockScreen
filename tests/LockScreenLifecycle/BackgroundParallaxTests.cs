using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OhMyLockScreen;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using Windows.Devices.Input;
using Windows.System.UserProfile;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;

internal static class BackgroundParallaxTests
{
    private static readonly TimeSpan ParallaxTickInterval = TimeSpan.FromMilliseconds(1000d / 60);

    private static int ActiveParallaxTimers => DispatcherTimer.Created.Count(timer =>
        timer.IsEnabled && timer.Interval == ParallaxTickInterval);

    internal static void Run(Action<string, Action> run)
    {
        run("None background mode suppresses only editor and preview parallax without changing scene strength", () =>
        {
            StartPresenterHost();
            var scene = new SceneSettings { Background = new BackgroundSettings { ParallaxStrength = 200 } };
            var editor = new ScenePresenter { Scene = scene, IsEditing = true, SuppressParallaxWithoutBackground = true };
            var lockView = new ScenePresenter { Scene = scene };
            var mouse = new Pointer(1, PointerDeviceType.Mouse);
            editor.BackgroundViewportElement.Resize(1024, 768);
            lockView.BackgroundViewportElement.Resize(1024, 768);
            editor.Load();
            lockView.Load();
            try
            {
                void CheckEditorIsStationary() => Check(editor.ParallaxTransform.ScaleX == 1
                    && editor.ParallaxTransform.ScaleY == 1 && editor.ParallaxTransform.TranslateX == 0
                    && editor.ParallaxTransform.TranslateY == 0 && ActiveParallaxTimers == 0,
                    "None mode left a transform or animation active in the editor presenter.");

                editor.SceneRootElement.MovePointer(mouse, new(1024, 0), 0);
                lockView.SceneRootElement.MovePointer(mouse, new(1024, 0), 0);
                SettleAt(0);
                CheckEditorIsStationary();
                CheckNear(lockView.ParallaxTransform.TranslateX, 8.209566, "The editor policy also disabled parallax on the lock view.");

                editor.IsEditing = false;
                editor.SceneRootElement.MovePointer(mouse, new(0, 768), 0);
                CheckEditorIsStationary();

                scene.Background.Mode = BackgroundMode.Video;
                SettleAt(1300);
                CheckNear(editor.ParallaxTransform.TranslateX, -8.209566, "Video mode did not restore preview parallax.");

                editor.SceneRootElement.MovePointer(mouse, new(1024, 0), 0);
                scene.Background.Mode = BackgroundMode.None;
                CheckEditorIsStationary();
                Check(scene.Background.ParallaxStrength == 200, "Mode switching changed the saved strength.");
            }
            finally
            {
                lockView.Unload();
                editor.Unload();
            }
        });

        run("Mouse parallax matches osu!lazer easing, direction, saturation and strength", () =>
        {
            StartPresenterHost();
            var input = new Grid();
            var scene = new SceneSettings();
            var presenter = new ScenePresenter { Scene = scene, IsEditing = true, ParallaxInputSurface = input };
            var transform = presenter.ParallaxTransform;
            var mouse = new Pointer(1, PointerDeviceType.Mouse);
            presenter.BackgroundViewportElement.Resize(1024, 768);
            presenter.Load();
            try
            {
                AdvanceParallaxClockTo(16);
                CheckNear(transform.ScaleX, 1.001550, "The initial zoom did not use the slower OutQuint easing.");

                var moved = input.MovePointer(mouse, new(1024, 0), 0, handled: true);
                Check(moved.Handled && transform.TranslateX == 0, "The handler changed routing or jumped to its target.");
                AdvanceParallaxClockTo(32);
                CheckNear(transform.TranslateX, 2.388114, "The background did not ease in the direction of the mouse.");
                Check(transform.ScaleX >= 1 + transform.TranslateX / 512, "The background did not cover the viewport while zooming.");
                SettleAt(48);
                CheckNear(transform.TranslateX, 4.104783, "The right edge did not reach the osu!lazer offset.");
                CheckNear(transform.TranslateY, -2.449916, "The top edge did not reach the osu!lazer offset.");
                Check(transform.ScaleX == 1.02, "The default zoom was not 102%.");

                scene.Background.ParallaxStrength = 200;
                SettleAt(1300);
                CheckNear(transform.TranslateX, 8.209566, "200% parallax did not double the offset.");
                Check(transform.ScaleY == 1.04, "200% parallax did not double the zoom increment.");

                input.MovePointer(mouse, new(1400, 384), 0);
                SettleAt(2600);
                CheckNear(transform.TranslateX, 12.056676, "Movement outside the viewport did not progressively saturate.");
                Check(transform.TranslateY == 0, "The centered axis retained an offset.");

                input.ExitPointer(mouse, new(1500, 384));
                CheckNear(transform.TranslateX, 12.056676, "Leaving the input surface unexpectedly reset the last mouse position.");
                Check(ActiveParallaxTimers == 0, "Leaving the input surface started an animation.");

                input.MovePointer(mouse, new(99999, 384), 0);
                SettleAt(3900);
                CheckNear(transform.TranslateX, 20.48, "Distant input exceeded the maximum overscan.");

                scene.Background.ParallaxStrength = 100;
                SettleAt(5200);
                CheckNear(transform.TranslateX, 10.24, "Reducing zoom at the edge did not settle at the new strength.");

                input.MovePointer(mouse, new(512, 384), 0);
                SettleAt(6500);
                Check(transform.TranslateX == 0 && transform.TranslateY == 0, "Returning the mouse to center did not zero the offset.");

                presenter.BackgroundViewportElement.Resize(1920, 1080);
                input.MovePointer(mouse, new(1920, 1080), 0);
                SettleAt(7800);
                CheckNear(transform.TranslateX, 9.502171, "The widescreen horizontal offset did not use osu!lazer's aspect scaling.");
                CheckNear(transform.TranslateY, 3.445194, "The widescreen vertical offset did not use osu!lazer's aspect scaling.");

                input.MovePointer(new Pointer(2, PointerDeviceType.Touch), new(0, 0), 0);
                input.MovePointer(new Pointer(3, PointerDeviceType.Pen), new(0, 0), 0);
                Check(ActiveParallaxTimers == 0, "A non-mouse pointer changed the background target.");
            }
            finally
            {
                presenter.Unload();
            }
        });

        run("Disabling, replacing or unloading the scene clears mouse parallax and frame handlers", () =>
        {
            StartPresenterHost();
            var scene = new SceneSettings();
            var presenter = new ScenePresenter { Scene = scene };
            var transform = presenter.ParallaxTransform;
            var mouse = new Pointer(1, PointerDeviceType.Mouse);
            presenter.BackgroundViewportElement.Resize(1024, 768);
            presenter.Load();
            try
            {
                presenter.SceneRootElement.MovePointer(mouse, new(0, 0), 0);
                SettleAt(0);
                CheckNear(transform.TranslateX, -4.104783, "The default input surface did not move the background.");

                scene.Background.ParallaxStrength = 0;
                presenter.SceneRootElement.MovePointer(mouse, new(0, 0), 0);
                Check(transform.ScaleX == 1 && transform.TranslateX == 0 && ActiveParallaxTimers == 0,
                    "0% did not immediately disable movement and scaling.");

                scene.Background = new BackgroundSettings { ParallaxStrength = 200 };
                presenter.SceneRootElement.MovePointer(mouse, new(0, 0), 0);
                SettleAt(1300);
                CheckNear(transform.TranslateX, -8.209566, "Replacing the background settings did not re-enable tracking.");

                presenter.BackgroundViewportElement.Resize(512, 384);
                SettleAt(2600);
                CheckNear(transform.TranslateX, -4.104783, "Resizing did not preserve the desktop UI's logical horizontal scale.");
                CheckNear(transform.TranslateY, -2.449916, "Resizing did not preserve the desktop UI's logical vertical scale.");

                presenter.Scene = new SceneSettings();
                Check(transform.TranslateX == 0 && transform.TranslateY == 0, "The replacement scene inherited the previous offset.");
                SettleAt(3900);
                Check(transform.ScaleX == 1.02, "The replacement scene inherited the previous strength.");

                presenter.SceneRootElement.MovePointer(mouse, new(512, 384), 0);
                Check(ActiveParallaxTimers == 1, "The new scene did not schedule animation.");
            }
            finally
            {
                presenter.Unload();
            }

            presenter.SceneRootElement.MovePointer(mouse, new(0, 0), 0);
            Check(transform.ScaleX == 1 && transform.TranslateX == 0 && ActiveParallaxTimers == 0
                && DispatcherTimer.Created.All(timer => timer.TickSubscribers == 0),
                "Unloading retained transforms or active input and rendering handlers.");
        });

        run("Static wallpaper follows parallax through drag, rebound and dismissal", () =>
        {
            StartPresenterHost();
            var scene = new SceneSettings { Background = new BackgroundSettings { Mode = BackgroundMode.Image, ParallaxStrength = 200 } };
            LockScreen.OpenImage = () => new WallpaperStream();
            var presenter = new ScenePresenter { Scene = scene, ActualHeight = 720 };
            presenter.BackgroundViewportElement.Resize(1024, 768);
            presenter.Load();
            try
            {
                var background = (Grid)presenter.BackgroundViewportElement.Children.Single();
                Check(background.Children.Contains(presenter.WallpaperElement) && presenter.WallpaperElement.Source != null &&
                    ReferenceEquals(background.RenderTransform, presenter.ParallaxTransform),
                    "Static wallpaper is not rendered inside the animated background viewport.");
                var mouse = new Pointer(1, PointerDeviceType.Mouse);
                presenter.SceneRootElement.MovePointer(mouse, new(1024, 0), 0);
                SettleAt(0);
                var transform = presenter.ParallaxTransform;
                var x = transform.TranslateX;
                var y = transform.TranslateY;
                var zoom = transform.ScaleX - 1;

                void CheckStrength(double expected)
                {
                    CheckNear(transform.TranslateX, x * expected, "Horizontal parallax did not follow swipe progress.");
                    CheckNear(transform.TranslateY, y * expected, "Vertical parallax did not follow swipe progress.");
                    CheckNear(transform.ScaleX, 1 + zoom * expected, "Parallax zoom did not follow swipe progress.");
                    CheckNear(transform.ScaleY, transform.ScaleX, "Parallax zoom lost its aspect ratio.");
                }

                presenter.SetUnlockDragOffset(90);
                CheckStrength(0.5);
                presenter.SetUnlockDragOffset(270);
                CheckStrength(0);
                presenter.SceneRootElement.MovePointer(mouse, new(768, 384), 0);
                CheckStrength(0);
                Check(ActiveParallaxTimers == 0, "Mouse movement restarted parallax easing during the swipe.");
                presenter.SceneRootElement.MovePointer(mouse, new(1024, 0), 0);
                CheckStrength(0);
                presenter.SetUnlockDragOffset(45);
                CheckStrength(0.75);

                presenter.PlayUnlockReturnAnimation();
                var rebound = Storyboard.LastCreated!;
                rebound.Advance(TimeSpan.FromMilliseconds(45));
                var opacity = presenter.BackgroundViewportElement.Opacity;
                Check(opacity is > 0.75 and < 1, "The rebound did not restore background opacity gradually.");
                CheckStrength(opacity);
                rebound.Advance(TimeSpan.FromMilliseconds(135));
                CheckStrength(1);

                presenter.SetUnlockDragOffset(90);
                presenter.PlayUnlockReturnAnimation();
                Storyboard.LastCreated!.Advance(TimeSpan.FromMilliseconds(20));
                opacity = presenter.BackgroundViewportElement.Opacity;
                var dismissal = presenter.PlayUnlockAnimationAsync();
                CheckStrength(opacity);
                var unlock = Storyboard.LastCreated!;
                unlock.Advance(TimeSpan.FromMilliseconds(75));
                var nextOpacity = presenter.BackgroundViewportElement.Opacity;
                Check(nextOpacity > 0 && nextOpacity < opacity, "The dismissal did not continue fading from the rebound frame.");
                CheckStrength(nextOpacity);
                unlock.Advance(TimeSpan.FromMilliseconds(225));
                Check(dismissal.IsCompletedSuccessfully, "Parallax held the dismissal storyboard open.");
                CheckStrength(0);
                Check(scene.Background.ParallaxStrength == 200, "The swipe changed the saved parallax setting.");
            }
            finally
            {
                presenter.Unload();
            }
        });

        run("The first lock view follows the mouse before unlock, without a compositor frame", () =>
        {
            StartPresenterHost();
            var scene = new SceneSettings();
            var editorWindow = Window.Current;
            var editor = new ScenePresenter { Scene = scene, IsEditing = true };
            editor.BackgroundViewportElement.Resize(1024, 768);
            editor.Load();
            var mouse = new Pointer(1, PointerDeviceType.Mouse);
            editor.SceneRootElement.MovePointer(mouse, new(0, 0), 0);

            var lockWindow = new Window();
            Window.Current = lockWindow;
            var page = new MainPage();
            var presenter = page.RootScenePresenter;
            presenter.BackgroundViewportElement.Resize(1024, 768);
            page.Load();
            presenter.Load();
            try
            {
                page.EnterPointer(mouse, new(1024, 384));
                presenter.Scene = scene;
                Check(DispatcherTimer.Created.Any(timer => timer.IsEnabled && timer.Owner == lockWindow),
                    "The lock view did not start its own timer before dismissal.");
                AdvanceParallaxClockTo(16);
                Check(presenter.ParallaxTransform.TranslateX > 0 && Storyboard.LastCreated == null,
                    "The initial pointer made no visible progress before an unlock animation.");
                SettleAt(32);
                CheckNear(editor.ParallaxTransform.TranslateX, -4.104783, "The editor used the lock view's pointer.");
                CheckNear(presenter.ParallaxTransform.TranslateX, 4.104783, "The lock view lost the pointer received before scene load.");

                presenter.Scene = new SceneSettings { SceneId = scene.SceneId };
                CheckNear(presenter.ParallaxTransform.TranslateX, 4.104783, "Reloading the same scene reset the current parallax.");
                Check(ActiveParallaxTimers == 0, "Reloading an unchanged scene restarted parallax easing.");
            }
            finally
            {
                presenter.Unload();
                page.Unload();
                Window.Current = editorWindow;
                editor.Unload();
            }
        });

        run("A captured swipe cannot start pending parallax or change its displayed starting transform", () =>
        {
            StartPresenterHost();
            var page = new MainPage();
            var presenter = page.RootScenePresenter;
            presenter.Scene = new SceneSettings();
            presenter.ActualHeight = 720;
            presenter.BackgroundViewportElement.Resize(1024, 768);
            page.Load();
            presenter.Load();
            try
            {
                page.EnterPointer(new Pointer(1, PointerDeviceType.Mouse), new(1024, 600));
                var press = page.PressPointer(PointerDeviceType.Mouse, position: new(1024, 600));
                Check(page.CapturedPointer == press.Pointer, "The first press did not capture the mouse on the page.");
                var transform = presenter.ParallaxTransform;
                Check(transform.TranslateX == 0 && transform.ScaleX == 1, "Pressing before the first tick forced parallax to appear.");

                var move = page.MovePointer(press.Pointer, new(975, 510), 150_000);
                Check(move.Handled, "The page did not recognize the captured upward drag.");
                AdvanceParallaxClockTo(100);
                Check(transform.TranslateX == 0 && transform.ScaleX == 1 && ActiveParallaxTimers == 0,
                    "The swipe restarted the pending hover animation.");

                page.CancelPointer(press.Pointer);
                Storyboard.LastCreated!.Advance(TimeSpan.FromMilliseconds(180));
                page.MovePointer(press.Pointer, new(1024, 600), 0);
                SettleAt(200);
                var initialY = transform.TranslateY;
                CheckNear(transform.TranslateX, 4.104783, "The view did not resume normal mouse parallax after the rebound.");

                press = page.PressPointer(PointerDeviceType.Mouse, position: new(1024, 600));
                page.MovePointer(press.Pointer, new(975, 510), 150_000);
                CheckNear(transform.TranslateX, 4.104783 / 2, "The captured move did not attenuate the horizontal position.");
                CheckNear(transform.TranslateY, initialY / 2, "The captured move changed the original vertical position during attenuation.");
                CheckNear(transform.ScaleX, 1.01, "The captured move did not attenuate zoom.");

                page.CancelPointer(press.Pointer);
                Storyboard.LastCreated!.Advance(TimeSpan.FromMilliseconds(180));
                CheckNear(transform.TranslateY, initialY, "Canceling the swipe did not restore the original offset on rebound.");
                SettleAt(1450);
                Check(transform.TranslateY < initialY, "Mouse tracking did not resume from the latest captured position.");
            }
            finally
            {
                presenter.Unload();
                page.Unload();
            }
        });

        run("Starting dismissal before the first hover tick does not bring pending parallax into view", () =>
        {
            StartPresenterHost();
            var presenter = new ScenePresenter { Scene = new SceneSettings(), ActualHeight = 720 };
            presenter.BackgroundViewportElement.Resize(1024, 768);
            presenter.Load();
            try
            {
                presenter.SceneRootElement.MovePointer(new Pointer(1, PointerDeviceType.Mouse), new(1024, 0), 0);
                Check(ActiveParallaxTimers == 1, "The hover update was not pending before unlock.");

                var dismissal = presenter.PlayUnlockAnimationAsync();
                var storyboard = Storyboard.LastCreated!;
                AdvanceParallaxClockTo(75);
                storyboard.Advance(TimeSpan.FromMilliseconds(75));
                Check(ActiveParallaxTimers == 0 && presenter.ParallaxTransform.ScaleX == 1 && presenter.ParallaxTransform.TranslateX == 0,
                    "Unlock made a pending parallax target visible.");
                storyboard.Advance(TimeSpan.FromMilliseconds(225));
                Check(dismissal.IsCompletedSuccessfully, "The unlock animation did not complete.");
            }
            finally
            {
                presenter.Unload();
            }
        });
    }

    private sealed class WallpaperStream : Windows.Storage.Streams.IRandomAccessStream
    {
        public Task DecodeAsync() => Task.CompletedTask;

        public void Dispose() { }
    }

    private static void CheckNear(double actual, double expected, string message) =>
        Check(Math.Abs(actual - expected) < 0.00001, $"{message} Expected {expected}, got {actual}.");

    private static void StartPresenterHost()
    {
        IAppHost.Host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<FeatureRegistryService>();
            services.AddSingleton<IFeatureService, FeatureService>();
            services.AddSingleton<TimeProvider, ManualParallaxClock>();
        }).Build();
    }

    private static void SettleAt(double start)
    {
        for (var elapsed = 0; elapsed <= 1200; elapsed += 16)
        {
            AdvanceParallaxClockTo(start + elapsed);
        }

        Check(ActiveParallaxTimers == 0, "Parallax kept ticking after reaching its target.");
    }

    private static void AdvanceParallaxClockTo(double milliseconds)
    {
        var clock = (ManualParallaxClock)IAppHost.GetRequiredService<TimeProvider>();
        clock.Timestamp = TimeSpan.FromMilliseconds(milliseconds).Ticks;
        var previous = Window.Current;
        try
        {
            foreach (var timer in DispatcherTimer.Created.Where(timer => timer.IsEnabled && timer.Interval == ParallaxTickInterval))
            {
                Window.Current = timer.Owner;
                timer.Advance();
            }
        }
        finally
        {
            Window.Current = previous;
        }
    }

    private sealed class ManualParallaxClock : TimeProvider
    {
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        internal long Timestamp { get; set; }
        public override long GetTimestamp() => Timestamp;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
