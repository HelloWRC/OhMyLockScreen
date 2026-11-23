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
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using Windows.Devices.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

internal static class LayerParallaxTests
{
    private const string FeatureId = "omls.features.layout.parallax";
    private static readonly TimeSpan ParallaxTickInterval = TimeSpan.FromMilliseconds(1000d / 60);

    private static int ActiveParallaxTimers => DispatcherTimer.Created.Count(timer =>
        timer.IsEnabled && timer.Interval == ParallaxTickInterval);

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

        run("Layer parallax matches wallpaper parallax timing, aspect scaling, saturation and background changes", () =>
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
                Check(ActiveParallaxTimers == 1, "Layer and background did not share a single parallax timer.");

                AdvanceParallaxClockTo(16);
                CheckNear(layer.ParallaxTransform.ScaleX, presenter.ParallaxTransform.ScaleX,
                    "Initial layer zoom did not match background parallax.");

                input.MovePointer(mouse, new(1024, 0), 0, handled: true);
                AdvanceParallaxClockTo(32);
                CheckNear(layer.ParallaxTransform.TranslateX, presenter.ParallaxTransform.TranslateX,
                    "Layer easing did not match background at 32ms.");
                SettleAt(48);
                CheckNear(layer.ParallaxTransform.TranslateX, 4.104783, "Layer right-edge offset did not match osu!lazer.");
                CheckNear(layer.ParallaxTransform.TranslateY, -2.449916, "Layer top-edge offset did not match osu!lazer.");
                Check(layer.ParallaxTransform.ScaleX == 1.02, "Layer default zoom was not 102%.");

                scene.Background.ParallaxStrength = 200;
                SettleAt(1300);
                CheckNear(layer.ParallaxTransform.TranslateX, 8.209566, "Layer did not follow updated background strength.");
                Check(layer.ParallaxTransform.ScaleY == 1.04, "Layer zoom did not follow updated background strength.");

                scene.Background = new BackgroundSettings { ParallaxStrength = 50 };
                SettleAt(2600);
                CheckNear(layer.ParallaxTransform.TranslateX, 2.0523915, "Layer did not follow replaced BackgroundSettings.");

                input.MovePointer(mouse, new(99999, 384), 0);
                SettleAt(3900);
                CheckNear(layer.ParallaxTransform.TranslateX, 5.12, "Distant input exceeded 50% overscan.");

                input.ExitPointer(mouse, new(1500, 384));
                CheckNear(layer.ParallaxTransform.TranslateX, 5.12, "Leaving input surface reset layer parallax.");
                Check(ActiveParallaxTimers == 0, "Leaving input surface started a timer.");

                input.MovePointer(mouse, new(512, 384), 0);
                SettleAt(5200);
                Check(layer.ParallaxTransform.TranslateX == 0 && layer.ParallaxTransform.TranslateY == 0,
                    "Returning pointer to center did not zero layer offset.");

                presenter.BackgroundViewportElement.Resize(1920, 1080);
                input.MovePointer(mouse, new(1920, 1080), 0);
                SettleAt(6500);
                CheckNear(layer.ParallaxTransform.TranslateX, presenter.ParallaxTransform.TranslateX,
                    "Widescreen layer horizontal offset diverged from background.");
                CheckNear(layer.ParallaxTransform.TranslateY, presenter.ParallaxTransform.TranslateY,
                    "Widescreen layer vertical offset diverged from background.");

                input.MovePointer(new Pointer(2, PointerDeviceType.Touch), new(0, 0), 0);
                input.MovePointer(new Pointer(3, PointerDeviceType.Pen), new(0, 0), 0);
                Check(ActiveParallaxTimers == 0, "Non-mouse pointer changed layer parallax.");
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
                CheckNear(presenter.ParallaxTransform.TranslateX, 4.104783, "Background strength was unexpected.");
                CheckNear(first.ParallaxTransform.TranslateX, 8.209566, "200% override layer did not double the offset.");
                CheckNear(second.ParallaxTransform.TranslateX, 2.0523915, "50% override layer did not halve the offset.");
                Check(third.ParallaxTransform.TranslateX == 0 && third.ParallaxTransform.ScaleX == 1,
                    "0% override layer did not stay stationary.");

                scene.Background.ParallaxStrength = 150;
                SettleAt(1300);
                CheckNear(presenter.ParallaxTransform.TranslateX, 6.1571745, "Background did not update to 150%.");
                CheckNear(first.ParallaxTransform.TranslateX, 8.209566, "Overridden 200% layer unexpectedly followed background.");
                CheckNear(second.ParallaxTransform.TranslateX, 2.0523915, "Overridden 50% layer unexpectedly followed background.");

                secondSettings.Strength = 100;
                SettleAt(2600);
                CheckNear(second.ParallaxTransform.TranslateX, 4.104783, "Updating override strength did not move the layer.");

                firstSettings.OverrideStrength = false;
                SettleAt(3900);
                CheckNear(first.ParallaxTransform.TranslateX, 6.1571745, "Disabling override did not resume following background.");

                thirdSettings.OverrideStrength = false;
                SettleAt(5200);
                CheckNear(third.ParallaxTransform.TranslateX, 6.1571745, "Disabling 0% override did not resume following background.");
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
                Check(presenter.ParallaxTransform.TranslateX == 0 && presenter.ParallaxTransform.ScaleX == 1,
                    "Background was not suppressed in editor None mode.");
                CheckNear(following.ParallaxTransform.TranslateX, 6.1571745,
                    "Layer following background strength was suppressed in None mode.");
                CheckNear(overridden.ParallaxTransform.TranslateX, 2.0523915,
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

                Check(layer.RenderTransform is CompositeTransform userTransform
                    && userTransform.TranslateX == 32 && userTransform.Rotation == 15 && userTransform.ScaleX == 1.25,
                    "TransformFeature on LayerPresenter was disturbed by layer parallax.");
                Check(!ReferenceEquals(layer.RenderTransform, layer.ParallaxTransform)
                    && layer.ParallaxTransform.ScaleX == 1.02
                    && Math.Abs(layer.ParallaxTransform.TranslateX - 4.104783) < 0.00001,
                    "Layer ParallaxTransform did not apply independently of RenderTransform.");

                layerModel.Features.Remove(parallaxInfo);
                Check(layer.ParallaxTransform.ScaleX == 1 && layer.ParallaxTransform.TranslateX == 0,
                    "Removing ParallaxFeature did not reset ParallaxTransform.");
                Check(layer.RenderTransform is CompositeTransform stillUser && stillUser.TranslateX == 32 && stillUser.Rotation == 15,
                    "Removing ParallaxFeature affected TransformFeature.");

                layerModel.Features.Add(parallaxInfo);
                SettleAt(1300);
                layerModel.Features.Remove(transformInfo);
                Check(layer.RenderTransform == null, "Removing TransformFeature did not restore RenderTransform.");
                SettleAt(2600);
                CheckNear(layer.ParallaxTransform.TranslateX, 4.104783, "Removing TransformFeature disturbed ParallaxTransform.");
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
                var x = layer.ParallaxTransform.TranslateX;
                var y = layer.ParallaxTransform.TranslateY;
                var zoom = layer.ParallaxTransform.ScaleX - 1;
                CheckNear(x, 8.209566, "Layer did not settle before unlock drag.");

                presenter.SetUnlockDragOffset(90);
                CheckNear(layer.ParallaxTransform.TranslateX, x * 0.5, "Layer horizontal parallax did not follow drag.");
                CheckNear(layer.ParallaxTransform.TranslateY, y * 0.5, "Layer vertical parallax did not follow drag.");
                CheckNear(layer.ParallaxTransform.ScaleX, 1 + zoom * 0.5, "Layer zoom did not follow drag.");

                presenter.PlayUnlockReturnAnimation();
                var rebound = Storyboard.LastCreated!;
                rebound.Advance(TimeSpan.FromMilliseconds(45));
                var opacity = presenter.BackgroundViewportElement.Opacity;
                CheckNear(layer.ParallaxTransform.TranslateX, x * opacity, "Layer did not follow rebound opacity.");
                rebound.Advance(TimeSpan.FromMilliseconds(135));
                CheckNear(layer.ParallaxTransform.TranslateX, x, "Rebound did not restore layer parallax.");

                app.Suspend();
                Check(layer.ParallaxTransform.TranslateX == 0 && layer.ParallaxTransform.TranslateY == 0 && ActiveParallaxTimers == 0,
                    "Suspending did not reset layer parallax offset or stop timer.");

                app.Resume();
                presenter.Dispatcher.Drain();
                SettleAt(1300);
                CheckNear(layer.ParallaxTransform.TranslateX, x, "Resuming did not restore layer parallax.");

                var dismissal = presenter.PlayUnlockAnimationAsync();
                var unlock = Storyboard.LastCreated!;
                unlock.Advance(TimeSpan.FromMilliseconds(75));
                Check(layer.ParallaxTransform.TranslateX > 0 && layer.ParallaxTransform.TranslateX < x,
                    "Unlock animation did not smoothly reset layer parallax.");
                unlock.Advance(TimeSpan.FromMilliseconds(225));
                Check(dismissal.IsCompletedSuccessfully
                    && layer.ParallaxTransform.TranslateX == 0
                    && layer.ParallaxTransform.ScaleX == 1,
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
                presenter.SceneRootElement.MovePointer(new Pointer(1, PointerDeviceType.Mouse), new(1024, 0), 0);
                Check(ActiveParallaxTimers == 1, "Pending hover animation did not start.");

                var dismissal = presenter.PlayUnlockAnimationAsync();
                var storyboard = Storyboard.LastCreated!;
                AdvanceParallaxClockTo(75);
                storyboard.Advance(TimeSpan.FromMilliseconds(75));
                Check(ActiveParallaxTimers == 0 && layer.ParallaxTransform.ScaleX == 1 && layer.ParallaxTransform.TranslateX == 0,
                    "Unlock made a pending layer parallax target visible.");
                storyboard.Advance(TimeSpan.FromMilliseconds(225));
                Check(dismissal.IsCompletedSuccessfully, "Unlock animation did not finish.");
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

            // Load layer before host is assigned and before scenes are loaded.
            layer.Load();
            Check(layer.ParallaxTransform.ScaleX == 1 && layer.ParallaxTransform.TranslateX == 0 && updateEvents == 0,
                "Unbound layer moved without a host.");

            firstScene.Load();
            firstScene.SceneRootElement.MovePointer(new Pointer(1, PointerDeviceType.Mouse), new(1024, 0), 0);
            SettleAt(0);
            var eventsWithoutLayers = updateEvents;
            Check(eventsWithoutLayers == 0, "LayerParallaxUpdated fired when no layer was registered.");

            layer.ParallaxHost = firstScene.ParallaxHost;
            SettleAt(1300);
            var eventsWithLayer = updateEvents;
            CheckNear(layer.ParallaxTransform.TranslateX, 4.104783, "Late ParallaxHost binding did not move the layer.");
            Check(eventsWithLayer > 0, "LayerParallaxUpdated did not fire while layer parallax eased.");

            // Idle ticks or scene ticks after layer settled must not emit extra LayerParallaxUpdated events.
            SettleAt(2600);
            Check(updateEvents == eventsWithLayer, "LayerParallaxUpdated fired when layer transform did not change.");

            secondScene.Load();
            secondScene.SceneRootElement.MovePointer(new Pointer(1, PointerDeviceType.Mouse), new(1024, 0), 0);
            SettleAt(3900);

            layer.ParallaxHost = secondScene.ParallaxHost;
            Check(updateEvents == eventsWithLayer + 1, "Switching ParallaxHost did not reset old registration and notify.");
            SettleAt(5200);
            CheckNear(layer.ParallaxTransform.TranslateX, 8.209566, "Switching ParallaxHost did not follow the new scene.");

            layer.Unload();
            Check(layer.ParallaxTransform.ScaleX == 1 && layer.ParallaxTransform.TranslateX == 0 && ActiveParallaxTimers == 0,
                "Unloading LayerPresenter did not reset transform or clear timers.");

            firstScene.Unload();
            secondScene.Unload();
            Check(DispatcherTimer.Created.All(timer => timer.TickSubscribers == 0),
                "Unloading scenes left parallax timer handlers attached.");
        });
    }

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
