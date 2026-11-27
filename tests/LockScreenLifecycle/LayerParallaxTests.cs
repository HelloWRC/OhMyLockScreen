using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Features.Layout;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

internal static class LayerParallaxTests
{
    private const string FeatureId = "omls.features.layout.parallax";
    private static readonly TimeSpan OldParallaxTickInterval = TimeSpan.FromMilliseconds(1000d / 60);

    private static int ParallaxTimersCreated => DispatcherTimer.Created.Count(timer =>
        timer.Interval == OldParallaxTickInterval);

    internal static void Run(Action<string, Action> run)
    {
        run("Parallax feature targets only LayerPresenter and defaults to following background", () =>
        {
            StartParallaxHost();
            var registry = IAppHost.GetRequiredService<FeatureRegistryService>();
            var registration = registry.Find(FeatureId);
            Check(registration != null, "Parallax feature was not registered.");
            Check(registration!.TargetType == typeof(LayerPresenter)
                && registration.TargetType.IsAssignableFrom(typeof(LayerPresenter))
                && !registration.TargetType.IsAssignableFrom(typeof(ComponentPresenter))
                && !registration.TargetType.IsAssignableFrom(typeof(ScenePresenter)),
                "Parallax feature did not restrict its target to LayerPresenter.");
            Check(registration.ClientSettingsType == typeof(ParallaxFeatureSettings)
                && registration.SettingsType == typeof(ParallaxSettingsControl),
                "Parallax feature metadata did not match its settings model or control.");

            var defaults = new ParallaxFeatureSettings();
            Check(!defaults.OverrideStrength && defaults.Strength == 100,
                "ParallaxFeatureSettings did not default to following background with 100% override strength.");
        });

        run("ParallaxFeatureSettings clamps values and round-trips through polymorphic FeatureInfo JSON", () =>
        {
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(IAppHost).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(ParallaxFeature).Assembly);

            var settings = new ParallaxFeatureSettings();
            settings.Strength = -25;
            Check(settings.Strength == 0, "Negative strength was not clamped to 0.");
            settings.Strength = 250;
            Check(settings.Strength == 200, "Oversized strength was not clamped to 200.");
            settings.Strength = double.NaN;
            Check(settings.Strength == 100, "NaN strength did not fall back to 100.");
            settings.Strength = double.PositiveInfinity;
            Check(settings.Strength == 100, "Infinite strength did not fall back to 100.");
            settings.OverrideStrength = true;
            settings.Strength = 0;

            var options = new JsonSerializerOptions
            {
                TypeInfoResolver = AutoJsonPolymorphicRegistrationHelper.CreateJsonTypeInfoResolver()
            };
            var original = new FeatureInfo
            {
                Id = FeatureId,
                Settings = settings
            };

            var json = JsonSerializer.Serialize(original, options);
            var restored = JsonSerializer.Deserialize<FeatureInfo>(json, options);
            Check(restored?.Settings is ParallaxFeatureSettings roundTripped
                && roundTripped.OverrideStrength
                && roundTripped.Strength == 0,
                "ParallaxFeatureSettings did not round-trip 0% override strength accurately.");
        });

        run("Layer parallax matches wallpaper parallax Composition timing, aspect scaling, saturation and background changes", () =>
        {
            StartParallaxHost();
            var input = new Grid();
            var scene = new SceneSettings();
            var presenter = new ScenePresenter { Scene = scene, IsEditing = true, ParallaxInputSurface = input };
            var layerModel = new LayerSettings
            {
                Features = [new FeatureInfo { Id = FeatureId }]
            };
            var layer = new LayerPresenter { Layer = layerModel, ParallaxHost = presenter.ParallaxHost };
            var mouse = new Pointer(1, PointerDeviceType.Mouse);
            presenter.BackgroundViewportElement.Resize(1024, 768);
            presenter.Load();
            layer.Load();
            try
            {
                Check(ParallaxTimersCreated == 0, "Layer parallax created a DispatcherTimer.");

                var parallaxBorder = GetParallaxBorder(layer);
                parallaxBorder.Resize(1024, 768);
                var layerVisual = ElementCompositionPreview.GetElementVisual(parallaxBorder);
                var bgVisual = GetParallaxVisual(presenter);

                Check(ElementCompositionPreview.GetIsTranslationEnabled(parallaxBorder),
                    "Layer parallax did not enable Translation on the wrapper visual.");
                Check(layerVisual.EvaluateCenterPoint() == new Vector3(512, 384, 0),
                    "Layer parallax wrapper did not center its Composition CenterPoint.");

                AdvanceParallaxClockTo(16);
                CheckNear(layerVisual.EvaluateScale().X, bgVisual.EvaluateScale().X,
                    "Initial layer zoom did not match background parallax.");

                input.MovePointer(mouse, new(1024, 0), 0, handled: true);
                AdvanceParallaxClockTo(32);
                CheckNear(layerVisual.EvaluateTranslation().X, bgVisual.EvaluateTranslation().X,
                    "Layer easing did not match background at 32ms.");
                Check(presenter.HasActiveLayerParallaxAnimation,
                    "HasActiveLayerParallaxAnimation was false mid-easing.");

                SettleAt(32);
                Check(!presenter.HasActiveLayerParallaxAnimation,
                    "HasActiveLayerParallaxAnimation stayed true after settling.");
                var settled = layerVisual.EvaluateTranslation();
                CheckNear(settled.X, 4.104783, "Layer right-edge offset did not match osu!lazer.");
                CheckNear(settled.Y, -2.449916, "Layer top-edge offset did not match osu!lazer.");
                CheckNear(layerVisual.EvaluateScale().X, 1.02, "Layer default zoom was not 102%.");

                Check(presenter.TryGetLayerParallaxMatrix(parallaxBorder, out var matrix),
                    "TryGetLayerParallaxMatrix failed to resolve registered layer.");
                var expectedMatrix = ParallaxMotionMath.CreateCenteredTransformMatrix(1024, 768, new Vector2(settled.X, settled.Y), 1.02);
                CheckNear(matrix.M11, expectedMatrix.M11, "Analytical matrix scale did not match Composition visual.");
                CheckNear(matrix.X, expectedMatrix.X, "Analytical matrix X translation did not match Composition visual.");
                CheckNear(matrix.Y, expectedMatrix.Y, "Analytical matrix Y translation did not match Composition visual.");

                scene.Background.ParallaxStrength = 200;
                SettleAt(1300);
                CheckNear(layerVisual.EvaluateTranslation().X, 8.209566,
                    "Layer did not follow updated background strength.");
                CheckNear(layerVisual.EvaluateScale().Y, 1.04,
                    "Layer zoom did not follow updated background strength.");

                scene.Background = new BackgroundSettings { ParallaxStrength = 50 };
                SettleAt(2600);
                CheckNear(layerVisual.EvaluateTranslation().X, 2.0523915,
                    "Layer did not follow replaced BackgroundSettings.");

                input.MovePointer(mouse, new(99999, 384), 0);
                SettleAt(3900);
                CheckNear(layerVisual.EvaluateTranslation().X, 5.12,
                    "Distant input exceeded 50% overscan.");

                input.ExitPointer(mouse, new(1500, 384));
                CheckNear(layerVisual.EvaluateTranslation().X, 5.12,
                    "Leaving input surface reset layer parallax.");
                Check(Compositor.ActiveKeyFrameAnimationCount == 0,
                    "Leaving input surface started an animation.");

                input.MovePointer(mouse, new(512, 384), 0);
                SettleAt(5200);
                var zeroed = layerVisual.EvaluateTranslation();
                Check(zeroed.X == 0 && zeroed.Y == 0,
                    "Returning pointer to center did not zero layer offset.");

                presenter.BackgroundViewportElement.Resize(1920, 1080);
                parallaxBorder.Resize(1920, 1080);
                input.MovePointer(mouse, new(1920, 1080), 0);
                SettleAt(6500);
                CheckNear(layerVisual.EvaluateTranslation().X, bgVisual.EvaluateTranslation().X,
                    "Widescreen layer horizontal offset diverged from background.");
                CheckNear(layerVisual.EvaluateTranslation().Y, bgVisual.EvaluateTranslation().Y,
                    "Widescreen layer vertical offset diverged from background.");

                input.MovePointer(new Pointer(2, PointerDeviceType.Touch), new(0, 0), 0);
                input.MovePointer(new Pointer(3, PointerDeviceType.Pen), new(0, 0), 0);
                Check(Compositor.ActiveKeyFrameAnimationCount == 0,
                    "Non-mouse pointer changed layer parallax.");
            }
            finally
            {
                layer.Unload();
                presenter.Unload();
            }
        });

        run("Override strength controls multiple layers independently and toggling override restores background tracking", () =>
        {
            StartParallaxHost();
            var scene = new SceneSettings
            {
                Background = new BackgroundSettings { ParallaxStrength = 100 }
            };
            var presenter = new ScenePresenter { Scene = scene };
            var firstSettings = new ParallaxFeatureSettings { OverrideStrength = true, Strength = 200 };
            var secondSettings = new ParallaxFeatureSettings { OverrideStrength = true, Strength = 50 };
            var thirdSettings = new ParallaxFeatureSettings { OverrideStrength = true, Strength = 0 };
            var first = CreateLoadedLayer(presenter, firstSettings);
            var second = CreateLoadedLayer(presenter, secondSettings);
            var third = CreateLoadedLayer(presenter, thirdSettings);
            var mouse = new Pointer(1, PointerDeviceType.Mouse);
            presenter.BackgroundViewportElement.Resize(1024, 768);
            presenter.Load();
            try
            {
                presenter.SceneRootElement.MovePointer(mouse, new(1024, 0), 0);
                SettleAt(0);
                var firstVisual = GetParallaxVisual(first);
                var secondVisual = GetParallaxVisual(second);
                var thirdVisual = GetParallaxVisual(third);
                CheckNear(GetParallaxVisual(presenter).EvaluateTranslation().X, 4.104783, "Background strength was unexpected.");
                CheckNear(firstVisual.EvaluateTranslation().X, 8.209566, "200% override layer did not double the offset.");
                CheckNear(secondVisual.EvaluateTranslation().X, 2.0523915, "50% override layer did not halve the offset.");
                Check(thirdVisual.EvaluateTranslation().X == 0 && thirdVisual.EvaluateScale().X == 1,
                    "0% override layer did not stay stationary.");

                scene.Background.ParallaxStrength = 150;
                SettleAt(1300);
                CheckNear(GetParallaxVisual(presenter).EvaluateTranslation().X, 6.1571745, "Background did not update to 150%.");
                CheckNear(firstVisual.EvaluateTranslation().X, 8.209566, "Overridden 200% layer unexpectedly followed background.");
                CheckNear(secondVisual.EvaluateTranslation().X, 2.0523915, "Overridden 50% layer unexpectedly followed background.");

                secondSettings.Strength = 100;
                SettleAt(2600);
                CheckNear(secondVisual.EvaluateTranslation().X, 4.104783, "Updating override strength did not move the layer.");

                firstSettings.OverrideStrength = false;
                SettleAt(3900);
                CheckNear(firstVisual.EvaluateTranslation().X, 6.1571745, "Disabling override did not resume following background.");

                thirdSettings.OverrideStrength = false;
                SettleAt(5200);
                CheckNear(thirdVisual.EvaluateTranslation().X, 6.1571745, "Disabling 0% override did not resume following background.");
            }
            finally
            {
                first.Unload();
                second.Unload();
                third.Unload();
                presenter.Unload();
            }
        });

        run("SuppressParallaxWithoutBackground leaves layer parallax active in None background mode", () =>
        {
            StartParallaxHost();
            var scene = new SceneSettings
            {
                Background = new BackgroundSettings { Mode = BackgroundMode.None, ParallaxStrength = 150 }
            };
            var presenter = new ScenePresenter { Scene = scene, IsEditing = true, SuppressParallaxWithoutBackground = true };
            var following = CreateLoadedLayer(presenter, new ParallaxFeatureSettings());
            var overridden = CreateLoadedLayer(presenter, new ParallaxFeatureSettings { OverrideStrength = true, Strength = 50 });
            presenter.BackgroundViewportElement.Resize(1024, 768);
            presenter.Load();
            try
            {
                presenter.SceneRootElement.MovePointer(new Pointer(1, PointerDeviceType.Mouse), new(1024, 0), 0);
                SettleAt(0);
                var bgVisual = GetParallaxVisual(presenter);
                Check(bgVisual.EvaluateTranslation().X == 0 && bgVisual.EvaluateScale().X == 1,
                    "Background was not suppressed in editor None mode.");
                CheckNear(GetParallaxVisual(following).EvaluateTranslation().X, 6.1571745,
                    "Layer following background strength was suppressed in None mode.");
                CheckNear(GetParallaxVisual(overridden).EvaluateTranslation().X, 2.0523915,
                    "Overridden layer was suppressed in None mode.");
            }
            finally
            {
                following.Unload();
                overridden.Unload();
                presenter.Unload();
            }
        });

        run("Layer parallax composes with TransformFeature without overwriting RenderTransform", () =>
        {
            StartParallaxHost();
            var scene = new SceneSettings();
            var presenter = new ScenePresenter { Scene = scene };
            var parallaxInfo = new FeatureInfo
            {
                Id = FeatureId,
                Settings = new ParallaxFeatureSettings { OverrideStrength = true, Strength = 100 }
            };
            var transformInfo = new FeatureInfo
            {
                Id = "omls.features.layout.transform",
                Settings = new TransformFeatureSettings { TranslateX = 32, Rotation = 15, ScaleX = 1.25 }
            };
            var layerModel = new LayerSettings
            {
                Features = [transformInfo, parallaxInfo]
            };
            var layer = new LayerPresenter { Layer = layerModel, ParallaxHost = presenter.ParallaxHost };
            presenter.BackgroundViewportElement.Resize(1024, 768);
            presenter.Load();
            layer.Load();
            try
            {
                presenter.SceneRootElement.MovePointer(new Pointer(1, PointerDeviceType.Mouse), new(1024, 0), 0);
                SettleAt(0);

                var parallaxBorder = GetParallaxBorder(layer);
                var parallaxVisual = ElementCompositionPreview.GetElementVisual(parallaxBorder);
                Check(layer.RenderTransform is CompositeTransform userTransform
                    && userTransform.TranslateX == 32 && userTransform.Rotation == 15 && userTransform.ScaleX == 1.25,
                    "TransformFeature on LayerPresenter was disturbed by layer parallax.");
                Check(parallaxBorder.RenderTransform == null
                    && Math.Abs(parallaxVisual.EvaluateScale().X - 1.02) < 0.00001
                    && Math.Abs(parallaxVisual.EvaluateTranslation().X - 4.104783) < 0.00001,
                    "Layer Composition parallax did not apply independently of RenderTransform.");

                layerModel.Features.Remove(parallaxInfo);
                Check(layer.Decorators.Elements.Count == 0
                    && parallaxVisual.EvaluateScale() == Vector3.One
                    && parallaxVisual.EvaluateTranslation() == Vector3.Zero,
                    "Removing ParallaxFeature did not unregister decorator wrapper or reset visual.");
                Check(layer.RenderTransform is CompositeTransform stillUser && stillUser.TranslateX == 32 && stillUser.Rotation == 15,
                    "Removing ParallaxFeature affected TransformFeature.");

                layerModel.Features.Add(parallaxInfo);
                SettleAt(1300);
                layerModel.Features.Remove(transformInfo);
                Check(layer.RenderTransform == null, "Removing TransformFeature did not restore RenderTransform.");
                SettleAt(2600);
                CheckNear(GetParallaxVisual(layer).EvaluateTranslation().X, 4.104783,
                    "Removing TransformFeature disturbed layer parallax visual.");
            }
            finally
            {
                layer.Unload();
                presenter.Unload();
            }
        });

        run("Layer parallax follows unlock drag, rebound, dismissal and application suspend/resume", () =>
        {
            StartParallaxHost();
            var app = new Application();
            var scene = new SceneSettings { Background = new BackgroundSettings { ParallaxStrength = 200 } };
            var presenter = new ScenePresenter { Scene = scene, ActualHeight = 720 };
            var layer = CreateLoadedLayer(presenter, new ParallaxFeatureSettings());
            presenter.BackgroundViewportElement.Resize(1024, 768);
            presenter.Load();
            try
            {
                presenter.SceneRootElement.MovePointer(new Pointer(1, PointerDeviceType.Mouse), new(1024, 0), 0);
                SettleAt(0);
                var parallaxVisual = GetParallaxVisual(layer);
                var settledT = parallaxVisual.EvaluateTranslation();
                var settledS = parallaxVisual.EvaluateScale();
                double x = settledT.X;
                double y = settledT.Y;
                double zoom = settledS.X - 1;
                CheckNear(x, 8.209566, "Layer did not settle before unlock drag.");

                presenter.SetUnlockDragOffset(90);
                CheckNear(parallaxVisual.EvaluateTranslation().X, x * 0.5, "Layer horizontal parallax did not follow drag.");
                CheckNear(parallaxVisual.EvaluateTranslation().Y, y * 0.5, "Layer vertical parallax did not follow drag.");
                CheckNear(parallaxVisual.EvaluateScale().X, 1 + zoom * 0.5, "Layer zoom did not follow drag.");

                presenter.PlayUnlockReturnAnimation();
                var rebound = Storyboard.LastCreated!;
                rebound.Advance(TimeSpan.FromMilliseconds(45));
                var opacity = presenter.BackgroundViewportElement.Opacity;
                CheckNear(parallaxVisual.EvaluateTranslation().X, x * opacity, "Layer did not follow rebound opacity.");
                rebound.Advance(TimeSpan.FromMilliseconds(135));
                CheckNear(parallaxVisual.EvaluateTranslation().X, x, "Rebound did not restore layer parallax.");

                app.Suspend();
                var suspendedT = parallaxVisual.EvaluateTranslation();
                Check(suspendedT.X == 0 && suspendedT.Y == 0 && ParallaxTimersCreated == 0,
                    "Suspending did not reset layer parallax offset.");

                app.Resume();
                presenter.Dispatcher.Drain();
                SettleAt(1300);
                CheckNear(parallaxVisual.EvaluateTranslation().X, x, "Resuming did not restore layer parallax.");

                var dismissal = presenter.PlayUnlockAnimationAsync();
                var unlock = Storyboard.LastCreated!;
                AdvanceClockBy(75);
                unlock.Advance(TimeSpan.FromMilliseconds(75));
                Check(parallaxVisual.EvaluateTranslation().X > 0 && parallaxVisual.EvaluateTranslation().X < x,
                    "Unlock animation did not smoothly reset layer parallax.");
                AdvanceClockBy(225);
                unlock.Advance(TimeSpan.FromMilliseconds(225));
                Check(dismissal.IsCompletedSuccessfully
                    && parallaxVisual.EvaluateTranslation().X == 0
                    && parallaxVisual.EvaluateScale().X == 1,
                    "Completed dismissal did not reset layer parallax to identity.");
            }
            finally
            {
                layer.Unload();
                presenter.Unload();
            }
        });

        run("Starting dismissal before first hover tick keeps pending layer parallax stationary", () =>
        {
            StartParallaxHost();
            var presenter = new ScenePresenter { Scene = new SceneSettings(), ActualHeight = 720 };
            var layer = CreateLoadedLayer(presenter, new ParallaxFeatureSettings());
            presenter.BackgroundViewportElement.Resize(1024, 768);
            presenter.Load();
            try
            {
                var visual = GetParallaxVisual(layer);
                presenter.SceneRootElement.MovePointer(new Pointer(1, PointerDeviceType.Mouse), new(1024, 0), 0);
                Check(Compositor.ActiveKeyFrameAnimationCount > 0, "Pending hover animation did not start.");

                var dismissal = presenter.PlayUnlockAnimationAsync();
                var storyboard = Storyboard.LastCreated!;
                AdvanceParallaxClockTo(75);
                storyboard.Advance(TimeSpan.FromMilliseconds(75));
                Check(visual.EvaluateScale().X == 1 && visual.EvaluateTranslation().X == 0,
                    "Unlock made a pending layer parallax target visible.");
                AdvanceParallaxClockTo(300);
                storyboard.Advance(TimeSpan.FromMilliseconds(225));
                Check(dismissal.IsCompletedSuccessfully && Compositor.ActiveKeyFrameAnimationCount == 0,
                    "Unlock animation did not finish cleanly.");
            }
            finally
            {
                layer.Unload();
                presenter.Unload();
            }
        });

        run("LayerPresenter handles host switches, unload cleanup and LayerParallaxUpdated notifications", () =>
        {
            StartParallaxHost();
            var firstScene = new ScenePresenter { Scene = new SceneSettings() };
            var secondScene = new ScenePresenter { Scene = new SceneSettings { Background = new BackgroundSettings { ParallaxStrength = 200 } } };
            firstScene.BackgroundViewportElement.Resize(1024, 768);
            secondScene.BackgroundViewportElement.Resize(1024, 768);

            var updateEvents = 0;
            firstScene.LayerParallaxUpdated += (_, _) => updateEvents++;

            var layerModel = new LayerSettings
            {
                Features = [new FeatureInfo { Id = FeatureId }]
            };
            var layer = new LayerPresenter { Layer = layerModel };
            Check(layer.Decorators.Elements.Count == 0, "Unloaded layer exposed parallax decorator before Load.");

            // Load layer before host is assigned and before scenes are loaded.
            layer.Load();
            var parallaxBorder = GetParallaxBorder(layer);
            var parallaxVisual = ElementCompositionPreview.GetElementVisual(parallaxBorder);
            Check(parallaxVisual.EvaluateScale().X == 1 && parallaxVisual.EvaluateTranslation().X == 0 && updateEvents == 0,
                "Unbound layer moved without a host.");

            firstScene.Load();
            firstScene.SceneRootElement.MovePointer(new Pointer(1, PointerDeviceType.Mouse), new(1024, 0), 0);
            SettleAt(0);
            var eventsWithoutLayers = updateEvents;
            Check(eventsWithoutLayers == 0, "LayerParallaxUpdated fired when no layer was registered.");

            layer.ParallaxHost = firstScene.ParallaxHost;
            SettleAt(1300);
            var eventsWithLayer = updateEvents;
            CheckNear(parallaxVisual.EvaluateTranslation().X, 4.104783, "Late ParallaxHost binding did not move the layer.");
            Check(eventsWithLayer > 0, "LayerParallaxUpdated did not fire when layer parallax changed.");

            // Idle clock advance after layer settled must not emit extra LayerParallaxUpdated events.
            SettleAt(2600);
            Check(updateEvents == eventsWithLayer, "LayerParallaxUpdated fired when layer transform did not change.");

            secondScene.Load();
            secondScene.SceneRootElement.MovePointer(new Pointer(1, PointerDeviceType.Mouse), new(1024, 0), 0);
            SettleAt(3900);

            layer.ParallaxHost = secondScene.ParallaxHost;
            Check(updateEvents == eventsWithLayer + 1, "Switching ParallaxHost did not reset old registration and notify.");
            SettleAt(5200);
            CheckNear(parallaxVisual.EvaluateTranslation().X, 8.209566, "Switching ParallaxHost did not follow the new scene.");

            layer.Unload();
            Check(layer.Decorators.Elements.Count == 0
                && parallaxVisual.EvaluateScale() == Vector3.One
                && parallaxVisual.EvaluateTranslation() == Vector3.Zero
                && parallaxVisual.EvaluateCenterPoint() == Vector3.Zero,
                "Unloading LayerPresenter did not unregister decorator wrapper or reset Composition visual.");

                firstScene.Unload();
            secondScene.Unload();
            Check(Compositor.ActiveKeyFrameAnimationCount == 0
                && Compositor.CreatedObjects.All(obj => obj.IsDisposed)
                && ParallaxTimersCreated == 0,
                "Unloading scenes left active animations, undisposed Composition objects, or parallax timers.");
        });

        run("High-frequency 2ms pointer retargeting continuously advances LayerPresenter visual and analytical matrix", () =>
        {
            StartParallaxHost();
            var input = new Grid();
            var scene = new SceneSettings { Background = new BackgroundSettings { ParallaxStrength = 200 } };
            var presenter = new ScenePresenter { Scene = scene, IsEditing = true, ParallaxInputSurface = input };
            var layer = CreateLoadedLayer(presenter, new ParallaxFeatureSettings());
            var mouse = new Pointer(1, PointerDeviceType.Mouse);
            presenter.BackgroundViewportElement.Resize(1024, 768);
            var parallaxBorder = GetParallaxBorder(layer);
            parallaxBorder.Resize(1024, 768);
            presenter.Load();
            try
            {
                var layerVisual = GetParallaxVisual(layer);
                input.MovePointer(mouse, new(0, 384), 0);
                SettleAt(0);
                CheckNear(layerVisual.EvaluateTranslation().X, -8.209566, "Initial layer edge offset did not settle.");

                var recordStart = Compositor.ActiveKeyFrameAnimations.Count;
                var clockMs = 1200.0;
                var previousX = layerVisual.EvaluateTranslation().X;
                var advancedSteps = 0;

                for (var step = 1; step <= 100; step++)
                {
                    clockMs += 2.0;
                    AdvanceParallaxClockTo(clockMs);
                    var currentTranslation = layerVisual.EvaluateTranslation();
                    Check(presenter.TryGetLayerParallaxMatrix(parallaxBorder, out var matrix),
                        "TryGetLayerParallaxMatrix failed mid-sweep.");
                    var expectedMatrix = ParallaxMotionMath.CreateCenteredTransformMatrix(
                        1024, 768, new Vector2(currentTranslation.X, currentTranslation.Y), layerVisual.EvaluateScale().X);
                    Check(Math.Abs(matrix.X - expectedMatrix.X) < 0.0001,
                        $"Analytical matrix X diverged from layer visual mid-sweep. Expected {expectedMatrix.X}, got {matrix.X}.");

                    if (step > 1 && currentTranslation.X > previousX)
                    {
                        advancedSteps++;
                    }

                    previousX = currentTranslation.X;
                    input.MovePointer(mouse, new(step * 1024.0 / 100.0, 384), 0);
                }

                var records = Compositor.ActiveKeyFrameAnimations
                    .Skip(recordStart)
                    .Where(r => r.PropertyName == "Offset" || r.PropertyName == "Scale")
                    .ToList();
                Check(records.Count >= 100 && records.All(r => r.HasExplicitStartKeyFrame),
                    $"Layer 2ms sweep missed explicit 0.0f keyframes (count={records.Count}).");
                Check(advancedSteps >= 98 && previousX - (-8.209566) > 8.0,
                    $"Layer 2ms sweep stalled during continuous pointer input (advanced={advancedSteps}, x={previousX}).");
                Check(presenter.HasActiveLayerParallaxAnimation,
                    "HasActiveLayerParallaxAnimation returned false immediately after 2ms sweep.");

                clockMs += 25.0;
                AdvanceParallaxClockTo(clockMs);
                Check(layerVisual.EvaluateTranslation().X > previousX,
                    "Layer parallax stopped advancing instead of coasting smoothly after pointer stop.");

                SettleAt(clockMs);
                Check(!presenter.HasActiveLayerParallaxAnimation,
                    "HasActiveLayerParallaxAnimation stayed true after settling.");
                CheckNear(layerVisual.EvaluateTranslation().X, 8.209566,
                    "Layer 2ms sweep did not settle at target endpoint.");
            }
            finally
            {
                layer.Unload();
                presenter.Unload();
            }
        });
    }

    private static Border GetParallaxBorder(LayerPresenter layer)
        => layer.Decorators.Elements.OfType<Border>().Single();

    private static Visual GetParallaxVisual(LayerPresenter layer)
        => ElementCompositionPreview.GetElementVisual(GetParallaxBorder(layer));

    private static Visual GetParallaxVisual(ScenePresenter presenter)
        => ElementCompositionPreview.GetElementVisual(presenter.BackgroundParallaxHostElement);

    private static LayerPresenter CreateLoadedLayer(ScenePresenter presenter, ParallaxFeatureSettings settings)
    {
        var layer = new LayerPresenter
        {
            Layer = new LayerSettings
            {
                Features = [new FeatureInfo { Id = FeatureId, Settings = settings }]
            },
            ParallaxHost = presenter.ParallaxHost
        };
        layer.Load();
        return layer;
    }

    private static void StartParallaxHost()
    {
        IAppHost.Host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddLogging();
            services.AddSingleton<FeatureRegistryService>();
            services.AddSingleton<IFeatureService, FeatureService>();
            services.AddSingleton<TimeProvider, ManualParallaxClock>();
            services.AddFeature<ParallaxFeature, ParallaxSettingsControl>();
            services.AddFeature<TransformFeature, TransformSettingsControl>();
        }).Build();
    }

    private static void SettleAt(double start)
    {
        AdvanceParallaxClockTo(start + 1200);
        Check(Compositor.ActiveKeyFrameAnimationCount == 0,
            "Composition keyframe animations kept running after settling.");
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

    private static void CheckNear(double actual, double expected, string message)
    {
        if (Math.Abs(actual - expected) >= 0.00001)
        {
            throw new Exception($"{message} Expected {expected}, got {actual}.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private sealed class ManualParallaxClock : TimeProvider
    {
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        internal long Timestamp { get; set; }

        public override long GetTimestamp() => Timestamp;
    }
}
