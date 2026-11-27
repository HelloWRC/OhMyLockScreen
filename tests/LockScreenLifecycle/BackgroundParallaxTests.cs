using System.Numerics;
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
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;

internal static class BackgroundParallaxTests
{
    private static readonly TimeSpan OldParallaxTickInterval = TimeSpan.FromMilliseconds(1000d / 60);

    private static int ParallaxTimersCreated => DispatcherTimer.Created.Count(timer =>
        timer.Interval == OldParallaxTickInterval);

    private static Visual GetParallaxVisual(ScenePresenter presenter) =>
        ElementCompositionPreview.GetElementVisual(presenter.BackgroundParallaxHostElement);

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
                void CheckEditorIsStationary()
                {
                    var visual = GetParallaxVisual(editor);
                    var scale = visual.EvaluateScale();
                    var translation = visual.EvaluateTranslation();
                    Check(Math.Abs(scale.X - 1f) < 0.00001
                        && Math.Abs(scale.Y - 1f) < 0.00001
                        && translation == Vector3.Zero
                        && ParallaxTimersCreated == 0,
                        "None mode left a transform or timer active in the editor presenter.");
                }

                editor.SceneRootElement.MovePointer(mouse, new(1024, 0), 0);
                lockView.SceneRootElement.MovePointer(mouse, new(1024, 0), 0);
                SettleAt(0);
                CheckEditorIsStationary();
                CheckNear(GetParallaxVisual(lockView).EvaluateTranslation().X, 8.209566,
                    "The editor policy also disabled parallax on the lock view.");

                editor.IsEditing = false;
                editor.SceneRootElement.MovePointer(mouse, new(0, 768), 0);
                CheckEditorIsStationary();

                scene.Background.Mode = BackgroundMode.Video;
                SettleAt(1300);
                CheckNear(GetParallaxVisual(editor).EvaluateTranslation().X, -8.209566,
                    "Video mode did not restore preview parallax.");

                editor.SceneRootElement.MovePointer(mouse, new(1024, 0), 0);
                scene.Background.Mode = BackgroundMode.None;
                CheckEditorIsStationary();
                Check(scene.Background.ParallaxStrength == 200, "Mode switching changed the saved strength.");
                Check(ParallaxTimersCreated == 0, "Parallax created a DispatcherTimer.");
            }
            finally
            {
                lockView.Unload();
                editor.Unload();
            }
        });

        run("Mouse parallax matches Composition OutQuint easing, direction, saturation and strength", () =>
        {
            StartPresenterHost();
            var input = new Grid();
            var scene = new SceneSettings();
            var presenter = new ScenePresenter { Scene = scene, IsEditing = true, ParallaxInputSurface = input };
            var mouse = new Pointer(1, PointerDeviceType.Mouse);
            presenter.BackgroundViewportElement.Resize(1024, 768);
            presenter.Load();
            try
            {
                var visual = GetParallaxVisual(presenter);
                Check(ElementCompositionPreview.GetIsTranslationEnabled(presenter.BackgroundParallaxHostElement),
                    "Translation property was not enabled on the background parallax visual.");
                Check(visual.EvaluateCenterPoint() == new Vector3(512, 384, 0),
                    "Background parallax CenterPoint was not centered on the host visual.");

                AdvanceParallaxClockTo(16);
                var expectedInitialZoom = 1 + 0.02 * ParallaxMotionMath.EvaluateCubicBezier(
                    16.0 / 1000.0,
                    ParallaxMotionMath.OutQuintControlPoint1,
                    ParallaxMotionMath.OutQuintControlPoint2);
                CheckNear(visual.EvaluateScale().X, expectedInitialZoom,
                    "The initial zoom did not use the slower OutQuint cubic-bezier easing.");

                var moved = input.MovePointer(mouse, new(1024, 0), 0, handled: true);
                Check(moved.Handled && visual.EvaluateTranslation().X == 0,
                    "The handler changed routing or jumped to its target.");

                AdvanceParallaxClockTo(32);
                var translationAt32 = visual.EvaluateTranslation();
                var scaleAt32 = visual.EvaluateScale();
                var expectedXAt32 = 4.104783 * ParallaxMotionMath.EvaluateCubicBezier(
                    16.0 / 100.0,
                    ParallaxMotionMath.OutQuintControlPoint1,
                    ParallaxMotionMath.OutQuintControlPoint2);
                CheckNear(translationAt32.X, expectedXAt32, "The background did not ease in the direction of the mouse.");
                Check(scaleAt32.X >= 1 + translationAt32.X / 512,
                    "The background did not cover the viewport while zooming.");

                SettleAt(32);
                var settledTranslation = visual.EvaluateTranslation();
                var settledScale = visual.EvaluateScale();
                CheckNear(settledTranslation.X, 4.104783, "The right edge did not reach the osu!lazer offset.");
                CheckNear(settledTranslation.Y, -2.449916, "The top edge did not reach the osu!lazer offset.");
                CheckNear(settledScale.X, 1.02, "The default zoom was not 102%.");

                scene.Background.ParallaxStrength = 200;
                SettleAt(1300);
                CheckNear(visual.EvaluateTranslation().X, 8.209566, "200% parallax did not double the offset.");
                CheckNear(visual.EvaluateScale().Y, 1.04, "200% parallax did not double the zoom increment.");

                input.MovePointer(mouse, new(1400, 384), 0);
                SettleAt(2600);
                CheckNear(visual.EvaluateTranslation().X, 12.056676,
                    "Movement outside the viewport did not progressively saturate.");
                Check(visual.EvaluateTranslation().Y == 0, "The centered axis retained an offset.");

                input.ExitPointer(mouse, new(1500, 384));
                CheckNear(visual.EvaluateTranslation().X, 12.056676,
                    "Leaving the input surface unexpectedly reset the last mouse position.");
                Check(Compositor.ActiveKeyFrameAnimationCount == 0,
                    "Leaving the input surface started an animation.");

                input.MovePointer(mouse, new(99999, 384), 0);
                SettleAt(3900);
                CheckNear(visual.EvaluateTranslation().X, 20.48, "Distant input exceeded the maximum overscan.");

                scene.Background.ParallaxStrength = 100;
                SettleAt(5200);
                CheckNear(visual.EvaluateTranslation().X, 10.24,
                    "Reducing zoom at the edge did not settle at the new strength.");

                input.MovePointer(mouse, new(512, 384), 0);
                SettleAt(6500);
                var centered = visual.EvaluateTranslation();
                Check(centered.X == 0 && centered.Y == 0, "Returning the mouse to center did not zero the offset.");

                presenter.BackgroundViewportElement.Resize(1920, 1080);
                input.MovePointer(mouse, new(1920, 1080), 0);
                SettleAt(7800);
                var widescreen = visual.EvaluateTranslation();
                CheckNear(widescreen.X, 9.502171, "The widescreen horizontal offset did not use osu!lazer's aspect scaling.");
                CheckNear(widescreen.Y, 3.445194, "The widescreen vertical offset did not use osu!lazer's aspect scaling.");

                input.MovePointer(new Pointer(2, PointerDeviceType.Touch), new(0, 0), 0);
                input.MovePointer(new Pointer(3, PointerDeviceType.Pen), new(0, 0), 0);
                Check(Compositor.ActiveKeyFrameAnimationCount == 0,
                    "A non-mouse pointer changed the background target.");
                Check(ParallaxTimersCreated == 0, "Parallax created a DispatcherTimer.");
            }
            finally
            {
                presenter.Unload();
            }
        });

        run("Disabling, replacing or unloading the scene clears mouse parallax and disposes Composition resources", () =>
        {
            StartPresenterHost();
            var scene = new SceneSettings();
            var presenter = new ScenePresenter { Scene = scene };
            var mouse = new Pointer(1, PointerDeviceType.Mouse);
            presenter.BackgroundViewportElement.Resize(1024, 768);
            presenter.Load();
            var visual = GetParallaxVisual(presenter);
            try
            {
                presenter.SceneRootElement.MovePointer(mouse, new(0, 0), 0);
                SettleAt(0);
                CheckNear(visual.EvaluateTranslation().X, -4.104783, "The default input surface did not move the background.");

                scene.Background.ParallaxStrength = 0;
                presenter.SceneRootElement.MovePointer(mouse, new(0, 0), 0);
                Check(visual.EvaluateScale().X == 1 && visual.EvaluateTranslation().X == 0
                    && Compositor.ActiveKeyFrameAnimationCount == 0,
                    "0% did not immediately disable movement and scaling.");

                scene.Background = new BackgroundSettings { ParallaxStrength = 200 };
                presenter.SceneRootElement.MovePointer(mouse, new(0, 0), 0);
                SettleAt(1300);
                CheckNear(visual.EvaluateTranslation().X, -8.209566,
                    "Replacing the background settings did not re-enable tracking.");

                presenter.BackgroundViewportElement.Resize(512, 384);
                SettleAt(2600);
                var resized = visual.EvaluateTranslation();
                CheckNear(resized.X, -4.104783, "Resizing did not preserve the desktop UI's logical horizontal scale.");
                CheckNear(resized.Y, -2.449916, "Resizing did not preserve the desktop UI's logical vertical scale.");

                presenter.Scene = new SceneSettings();
                var reset = visual.EvaluateTranslation();
                Check(reset.X == 0 && reset.Y == 0, "The replacement scene inherited the previous offset.");
                SettleAt(3900);
                CheckNear(visual.EvaluateScale().X, 1.02, "The replacement scene inherited the previous strength.");

                presenter.SceneRootElement.MovePointer(mouse, new(512, 384), 0);
                Check(Compositor.ActiveKeyFrameAnimationCount > 0, "The new scene did not schedule Composition animation.");
            }
            finally
            {
                presenter.Unload();
            }

            presenter.SceneRootElement.MovePointer(mouse, new(0, 0), 0);
            Check(visual.EvaluateScale() == Vector3.One
                && visual.EvaluateTranslation() == Vector3.Zero
                && visual.EvaluateCenterPoint() == Vector3.Zero
                && Compositor.ActiveKeyFrameAnimationCount == 0
                && Compositor.CreatedObjects.All(obj => obj.IsDisposed)
                && ParallaxTimersCreated == 0,
                "Unloading retained Composition animations, non-identity visuals, undisposed objects, or timers.");
        });

        run("Static wallpaper follows Composition parallax through drag, rebound and dismissal", () =>
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
                Check(ReferenceEquals(background, presenter.BackgroundParallaxHostElement)
                    && background.Children.Contains(presenter.WallpaperElement)
                    && presenter.WallpaperElement.Source != null,
                    "Static wallpaper is not rendered inside the Composition parallax host.");
                var mouse = new Pointer(1, PointerDeviceType.Mouse);
                presenter.SceneRootElement.MovePointer(mouse, new(1024, 0), 0);
                SettleAt(0);
                var visual = GetParallaxVisual(presenter);
                var settledT = visual.EvaluateTranslation();
                var settledS = visual.EvaluateScale();
                double x = settledT.X;
                double y = settledT.Y;
                double zoom = settledS.X - 1;

                void CheckStrength(double expected)
                {
                    var t = visual.EvaluateTranslation();
                    var s = visual.EvaluateScale();
                    CheckNear(t.X, x * expected, "Horizontal parallax did not follow swipe progress.");
                    CheckNear(t.Y, y * expected, "Vertical parallax did not follow swipe progress.");
                    CheckNear(s.X, 1 + zoom * expected, "Parallax zoom did not follow swipe progress.");
                    CheckNear(s.Y, s.X, "Parallax zoom lost its aspect ratio.");
                }

                presenter.SetUnlockDragOffset(90);
                CheckStrength(0.5);
                presenter.SetUnlockDragOffset(270);
                CheckStrength(0);
                presenter.SceneRootElement.MovePointer(mouse, new(768, 384), 0);
                CheckStrength(0);
                Check(Compositor.ActiveKeyFrameAnimationCount == 0,
                    "Mouse movement restarted parallax easing during the swipe.");
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
                Check(unlock.Children.Count == 3,
                    "Unlock Storyboard should contain only core transform and opacity animations.");

                AdvanceClockBy(75);
                unlock.Advance(TimeSpan.FromMilliseconds(75));
                var nextOpacity = presenter.BackgroundViewportElement.Opacity;
                Check(nextOpacity > 0 && nextOpacity < opacity, "The dismissal did not continue fading from the rebound frame.");
                CheckStrength(nextOpacity);

                AdvanceClockBy(225);
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
                var lockVisual = GetParallaxVisual(presenter);
                var editorVisual = GetParallaxVisual(editor);
                Check(!ReferenceEquals(lockVisual.Compositor, editorVisual.Compositor),
                    "Lock view did not bind to its own window Compositor.");

                page.EnterPointer(mouse, new(1024, 384));
                presenter.Scene = scene;
                Check(Compositor.ActiveKeyFrameAnimationCount > 0 && ParallaxTimersCreated == 0,
                    "The lock view did not start Composition animations without a timer.");
                AdvanceParallaxClockTo(16);
                Check(lockVisual.EvaluateTranslation().X > 0 && Storyboard.LastCreated == null,
                    "The initial pointer made no visible progress before an unlock animation.");

                SettleAt(32);
                CheckNear(editorVisual.EvaluateTranslation().X, -4.104783, "The editor used the lock view's pointer.");
                CheckNear(lockVisual.EvaluateTranslation().X, 4.104783,
                    "The lock view lost the pointer received before scene load.");

                presenter.Scene = new SceneSettings { SceneId = scene.SceneId };
                CheckNear(lockVisual.EvaluateTranslation().X, 4.104783, "Reloading the same scene reset the current parallax.");
                Check(Compositor.ActiveKeyFrameAnimationCount == 0,
                    "Reloading an unchanged scene restarted parallax easing.");
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
                var visual = GetParallaxVisual(presenter);
                page.EnterPointer(new Pointer(1, PointerDeviceType.Mouse), new(1024, 600));
                var press = page.PressPointer(PointerDeviceType.Mouse, position: new(1024, 600));
                Check(page.CapturedPointer == press.Pointer, "The first press did not capture the mouse on the page.");
                Check(visual.EvaluateTranslation().X == 0 && visual.EvaluateScale().X == 1,
                    "Pressing before the first frame forced parallax to appear.");

                var move = page.MovePointer(press.Pointer, new(975, 510), 150_000);
                Check(move.Handled, "The page did not recognize the captured upward drag.");
                AdvanceParallaxClockTo(100);
                Check(visual.EvaluateTranslation().X == 0 && visual.EvaluateScale().X == 1
                    && Compositor.ActiveKeyFrameAnimationCount == 0,
                    "The swipe restarted the pending hover animation.");

                page.CancelPointer(press.Pointer);
                Storyboard.LastCreated!.Advance(TimeSpan.FromMilliseconds(180));
                page.MovePointer(press.Pointer, new(1024, 600), 0);
                SettleAt(200);
                double initialY = visual.EvaluateTranslation().Y;
                CheckNear(visual.EvaluateTranslation().X, 4.104783,
                    "The view did not resume normal mouse parallax after the rebound.");

                press = page.PressPointer(PointerDeviceType.Mouse, position: new(1024, 600));
                page.MovePointer(press.Pointer, new(975, 510), 150_000);
                CheckNear(visual.EvaluateTranslation().X, 4.104783 / 2,
                    "The captured move did not attenuate the horizontal position.");
                CheckNear(visual.EvaluateTranslation().Y, initialY / 2,
                    "The captured move changed the original vertical position during attenuation.");
                CheckNear(visual.EvaluateScale().X, 1.01, "The captured move did not attenuate zoom.");

                page.CancelPointer(press.Pointer);
                Storyboard.LastCreated!.Advance(TimeSpan.FromMilliseconds(180));
                CheckNear(visual.EvaluateTranslation().Y, initialY,
                    "Canceling the swipe did not restore the original offset on rebound.");
                SettleAt(1450);
                Check(visual.EvaluateTranslation().Y < initialY,
                    "Mouse tracking did not resume from the latest captured position.");
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
                var visual = GetParallaxVisual(presenter);
                presenter.SceneRootElement.MovePointer(new Pointer(1, PointerDeviceType.Mouse), new(1024, 0), 0);
                Check(Compositor.ActiveKeyFrameAnimationCount > 0, "The hover update was not pending before unlock.");

                var dismissal = presenter.PlayUnlockAnimationAsync();
                var storyboard = Storyboard.LastCreated!;
                AdvanceParallaxClockTo(75);
                storyboard.Advance(TimeSpan.FromMilliseconds(75));
                Check(visual.EvaluateScale().X == 1 && visual.EvaluateTranslation().X == 0,
                    "Unlock made a pending parallax target visible.");
                AdvanceParallaxClockTo(300);
                storyboard.Advance(TimeSpan.FromMilliseconds(225));
                Check(dismissal.IsCompletedSuccessfully && Compositor.ActiveKeyFrameAnimationCount == 0,
                    "The unlock animation did not complete cleanly.");
            }
            finally
            {
                presenter.Unload();
            }
        });

        run("High-frequency retargeting, rapid strength scrubbing and rapid viewport resizing emit explicit 0.0f keyframes and advance smoothly", () =>
        {
            StartPresenterHost();
            var input = new Grid();
            var scene = new SceneSettings { Background = new BackgroundSettings { ParallaxStrength = 200 } };
            var presenter = new ScenePresenter { Scene = scene, IsEditing = true, ParallaxInputSurface = input };
            var mouse = new Pointer(1, PointerDeviceType.Mouse);
            presenter.BackgroundViewportElement.Resize(1024, 768);
            presenter.Load();
            try
            {
                var visual = GetParallaxVisual(presenter);
                input.MovePointer(mouse, new(0, 384), 0);
                SettleAt(0);
                CheckNear(visual.EvaluateTranslation().X, -8.209566, "Initial edge position did not settle.");

                // Continuous 2ms (500Hz) and 6ms (166Hz) sweeps must continuously advance during motion.
                var clockMs = 1200.0;
                foreach (var (steps, stepMs) in new[] { (100, 2.0), (34, 6.0) })
                {
                    var fromLeft = steps == 100;
                    var startX = fromLeft ? 0.0 : 1024.0;
                    var endX = fromLeft ? 1024.0 : 0.0;
                    var recordStart = Compositor.ActiveKeyFrameAnimations.Count;
                    var previousX = visual.EvaluateTranslation().X;
                    var initialX = previousX;
                    var advancedSteps = 0;

                    for (var step = 1; step <= steps; step++)
                    {
                        clockMs += stepMs;
                        AdvanceParallaxClockTo(clockMs);
                        var currentX = visual.EvaluateTranslation().X;
                        if (step > 1 && (fromLeft ? currentX > previousX : currentX < previousX))
                        {
                            advancedSteps++;
                        }

                        previousX = currentX;
                        var pointerX = startX + (endX - startX) * step / steps;
                        input.MovePointer(mouse, new(pointerX, 384), 0);
                    }

                    var offsetRecords = Compositor.ActiveKeyFrameAnimations
                        .Skip(recordStart)
                        .Where(r => r.PropertyName == "Offset")
                        .ToList();
                    Check(offsetRecords.Count == steps && offsetRecords.All(r => r.HasExplicitStartKeyFrame),
                        $"Missing explicit 0.0f keyframe during {stepMs}ms sweep (count={offsetRecords.Count}).");
                    Check(advancedSteps >= steps - 2,
                        $"Sweep @ {stepMs}ms stalled during continuous input ({advancedSteps}/{steps}).");
                    Check(Math.Abs(previousX - initialX) > 8.0,
                        $"Sweep @ {stepMs}ms did not advance meaningfully during sweep (dx={previousX - initialX}).");

                    clockMs += 25.0;
                    AdvanceParallaxClockTo(clockMs);
                    var coastX = visual.EvaluateTranslation().X;
                    Check(fromLeft ? coastX > previousX : coastX < previousX,
                        $"Sweep @ {stepMs}ms failed to coast smoothly after pointer stopped.");

                    SettleAt(clockMs);
                    clockMs += 1200.0;
                    CheckNear(visual.EvaluateTranslation().X, fromLeft ? 8.209566 : -8.209566,
                        $"Sweep @ {stepMs}ms did not settle at target endpoint.");
                }

                // Rapid 6ms ParallaxStrength scrub (0 -> 200) must advance scale smoothly without resetting to StartingValue.
                input.MovePointer(mouse, new(1024, 384), 0);
                SettleAt(clockMs);
                clockMs += 1200.0;
                scene.Background.ParallaxStrength = 0;
                var strengthRecordStart = Compositor.ActiveKeyFrameAnimations.Count;
                var prevScale = visual.EvaluateScale().X;
                var scaleAdvanced = 0;
                for (var step = 1; step <= 30; step++)
                {
                    clockMs += 6.0;
                    AdvanceParallaxClockTo(clockMs);
                    var currentScale = visual.EvaluateScale().X;
                    if (step > 1 && currentScale > prevScale)
                    {
                        scaleAdvanced++;
                    }

                    prevScale = currentScale;
                    scene.Background.ParallaxStrength = step * 200.0 / 30.0;
                }

                var scaleRecords = Compositor.ActiveKeyFrameAnimations
                    .Skip(strengthRecordStart)
                    .Where(r => r.PropertyName == "Scale")
                    .ToList();
                Check(scaleRecords.Count == 30 && scaleRecords.All(r => r.HasExplicitStartKeyFrame),
                    $"Rapid strength scrub missed explicit 0.0f Scale keyframes (count={scaleRecords.Count}).");
                Check(scaleAdvanced >= 28 && prevScale > 1.003,
                    $"Rapid strength scrub stalled Scale animation (advanced={scaleAdvanced}, scale={prevScale}).");
                SettleAt(clockMs);
                clockMs += 1200.0;
                CheckNear(visual.EvaluateScale().X, 1.04, "Rapid strength scrub did not settle at 104% scale.");

                // Rapid viewport shrinks during active return-to-center motion must clamp start offsets and retain explicit 0.0f keyframes.
                input.MovePointer(mouse, new(99999, 384), 0);
                SettleAt(clockMs);
                clockMs += 1200.0;
                CheckNear(visual.EvaluateTranslation().X, 20.48, "Saturated edge offset did not settle.");

                var resizeRecordStart = Compositor.ActiveKeyFrameAnimations.Count;
                input.MovePointer(mouse, new(512, 384), 0);
                for (var step = 1; step <= 5; step++)
                {
                    clockMs += 2.0;
                    AdvanceParallaxClockTo(clockMs);
                    var width = 1024.0 - step * 102.4;
                    var height = 768.0 - step * 76.8;
                    presenter.BackgroundViewportElement.Resize(width, height);
                    input.MovePointer(mouse, new(width * 0.5, height * 0.5), 0);
                    Check(Math.Abs(visual.EvaluateTranslation().X) <= width * 0.02 + 1e-4,
                        $"Rapid viewport resize exceeded clamped margin (width={width}, x={visual.EvaluateTranslation().X}).");
                }

                var resizeRecords = Compositor.ActiveKeyFrameAnimations
                    .Skip(resizeRecordStart)
                    .Where(r => r.PropertyName == "Offset")
                    .ToList();
                Check(resizeRecords.Count > 0 && resizeRecords.All(r => r.HasExplicitStartKeyFrame),
                    "Viewport resize animation omitted explicit 0.0f keyframe.");
                SettleAt(clockMs);
                CheckNear(visual.EvaluateTranslation().X, 0, "Viewport resize return did not settle at center.");
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
            services.AddLogging();
            services.AddSingleton<FeatureRegistryService>();
            services.AddSingleton<IFeatureService, FeatureService>();
            services.AddSingleton<TimeProvider, ManualParallaxClock>();
        }).Build();
    }

    private static void SettleAt(double start)
    {
        AdvanceParallaxClockTo(start + 1200);
        Check(Compositor.ActiveKeyFrameAnimationCount == 0,
            "Composition keyframe animations stayed active after settling.");
    }

    private static void AdvanceParallaxClockTo(double milliseconds)
    {
        var clock = (ManualParallaxClock)IAppHost.GetRequiredService<TimeProvider>();
        clock.Timestamp = TimeSpan.FromMilliseconds(milliseconds).Ticks;
    }

    private static void AdvanceClockBy(double milliseconds)
    {
        var clock = (ManualParallaxClock)IAppHost.GetRequiredService<TimeProvider>();
        clock.Timestamp += TimeSpan.FromMilliseconds(milliseconds).Ticks;
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
