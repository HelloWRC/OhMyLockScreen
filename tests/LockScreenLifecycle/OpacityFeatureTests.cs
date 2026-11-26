using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLockScreen;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Features.Appearance;
using OhMyLockScreen.Services.Features.Layout;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.FeatureHost;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using OhMyLockScreen.ViewModels;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

internal static class OpacityFeatureTests
{
    private const string FeatureId = OpacityFeature.Id;

    internal static void Run(Action<string, Action> run)
    {
        run("Opacity feature registers metadata and targets IDecoratorHost across scenes, layers, and components", () =>
        {
            using var provider = CreateProvider();
            var registry = provider.GetRequiredService<FeatureRegistryService>();
            var registration = registry.Find(FeatureId);
            Check(registration != null, "Opacity feature was not registered.");
            Check(registration!.Name == "不透明度"
                && registration.TargetType == typeof(IDecoratorHost)
                && registration.ClientSettingsType == typeof(OpacityFeatureSettings)
                && registration.SettingsType == typeof(OpacitySettingsControl),
                "Opacity feature metadata did not match IDecoratorHost, settings model, or control.");
            Check(registration.TargetType.IsAssignableFrom(typeof(ComponentPresenter))
                && registration.TargetType.IsAssignableFrom(typeof(LayerPresenter))
                && registration.TargetType.IsAssignableFrom(typeof(ScenePresenter))
                && !registration.TargetType.IsAssignableFrom(typeof(FrameworkElement)),
                "Opacity feature did not target IDecoratorHost implementations exclusively.");
        });

        run("OpacityFeatureSettings and OpacityEffectHelper sanitize values and round-trip JSON", () =>
        {
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(IAppHost).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(OpacityFeature).Assembly);

            var defaults = new OpacityFeatureSettings();
            Check(defaults.Opacity == 1.0 && defaults.BlendMode == OpacityBlendMode.Normal,
                "OpacityFeatureSettings defaults were unexpected.");

            defaults.Opacity = -0.5;
            Check(defaults.Opacity == 0, "Negative opacity was not clamped to 0.");
            defaults.Opacity = -0.0;
            Check(BitConverter.DoubleToInt64Bits(defaults.Opacity) == 0, "Negative zero opacity was not normalized.");
            defaults.Opacity = 1.75;
            Check(defaults.Opacity == 1, "Oversized opacity was not clamped to 1.");
            defaults.Opacity = double.NaN;
            Check(defaults.Opacity == OpacityFeatureSettings.DefaultOpacity, "NaN opacity did not fall back.");
            defaults.Opacity = double.PositiveInfinity;
            Check(defaults.Opacity == OpacityFeatureSettings.DefaultOpacity, "Infinite opacity did not fall back.");

            defaults.BlendMode = (OpacityBlendMode)999;
            Check(defaults.BlendMode == OpacityBlendMode.Normal, "Invalid blend mode did not fall back to Normal.");

            var normalizedOutset = OpacityEffectHelper.NormalizeOutset(
                new Thickness(double.NaN, -3.2, 4.1, 2048));
            Check(normalizedOutset == new Thickness(0, 0, 5, OpacityEffectHelper.MaxOutset)
                && OpacityEffectHelper.GetMaxOutset(normalizedOutset) == OpacityEffectHelper.MaxOutset,
                "OpacityEffectHelper.NormalizeOutset did not sanitize bounds as expected.");

            Check(OpacitySettingsViewModel.OrderedBlendModes.Count == 22
                && OpacitySettingsViewModel.OrderedBlendModes.Distinct().Count() == 22
                && Enum.GetValues<OpacityBlendMode>().Length == 22,
                "OrderedBlendModes did not expose all 22 unique blend modes.");

            var options = new JsonSerializerOptions
            {
                TypeInfoResolver = AutoJsonPolymorphicRegistrationHelper.CreateJsonTypeInfoResolver()
            };

            foreach (var mode in Enum.GetValues<OpacityBlendMode>())
            {
                var original = new FeatureInfo
                {
                    Id = FeatureId,
                    Settings = new OpacityFeatureSettings
                    {
                        Opacity = 0.425,
                        BlendMode = mode
                    }
                };
                var json = JsonSerializer.Serialize(original, options);
                var restored = JsonSerializer.Deserialize<FeatureInfo>(json, options);
                Check(restored?.Settings is OpacityFeatureSettings roundTripped
                    && roundTripped.Opacity == 0.425
                    && roundTripped.BlendMode == mode,
                    $"OpacityFeatureSettings did not round-trip {mode} accurately through FeatureInfo JSON.");
            }
        });

        run("Opacity feature registers OpacityDecorator, binds OpacitySpec across hosts, and handles enable toggle", () =>
        {
            using var provider = CreateProvider();
            var registry = provider.GetRequiredService<FeatureRegistryService>();
            var features = provider.GetRequiredService<IFeatureService>();

            var component = new ComponentPresenter();
            var componentModel = new ComponentSettings();

            using (var attacher = new FeatureAttacher(component, features, registry) { FeatureContainer = componentModel })
            {
                attacher.Attach();
                var settings = new OpacityFeatureSettings();
                var info = new FeatureInfo { Id = FeatureId, Settings = settings };
                componentModel.Features.Add(info);

                var decorator = component.Decorators.Elements.OfType<OpacityDecorator>().SingleOrDefault();
                Check(decorator != null
                    && decorator.OpacitySpec == new OpacityEffectSpec(1.0, OpacityBlendMode.Normal),
                    "OpacityDecorator was not registered or bound with initial spec on ComponentPresenter.");

                settings.Opacity = 0.35;
                settings.BlendMode = OpacityBlendMode.Multiply;
                Check(decorator!.OpacitySpec == new OpacityEffectSpec(0.35, OpacityBlendMode.Multiply),
                    "Updated opacity spec was not synchronized to OpacityDecorator.");

                info.IsEnabled = false;
                Check(component.Decorators.Elements.Count == 0 && decorator.OpacitySpec == null,
                    "Disabling OpacityFeature did not unregister OpacityDecorator.");

                info.IsEnabled = true;
                var reattached = component.Decorators.Elements.OfType<OpacityDecorator>().SingleOrDefault();
                Check(reattached != null
                    && reattached.OpacitySpec == new OpacityEffectSpec(0.35, OpacityBlendMode.Multiply),
                    "Re-enabling OpacityFeature did not restore OpacityDecorator.");

                componentModel.Features.Remove(info);
                Check(component.Decorators.Elements.Count == 0 && reattached!.OpacitySpec == null,
                    "Removing OpacityFeature did not unregister OpacityDecorator and clear its binding.");
            }

            var layer = new LayerPresenter();
            var layerModel = new LayerSettings();

            using (var attacher = new FeatureAttacher(layer, features, registry) { FeatureContainer = layerModel })
            {
                attacher.Attach();
                layerModel.Features.Add(new FeatureInfo
                {
                    Id = FeatureId,
                    Settings = new OpacityFeatureSettings { Opacity = 0.65, BlendMode = OpacityBlendMode.Screen }
                });

                var decorator = layer.Decorators.Elements.OfType<OpacityDecorator>().SingleOrDefault();
                Check(decorator != null
                    && decorator.OpacitySpec == new OpacityEffectSpec(0.65, OpacityBlendMode.Screen),
                    "OpacityFeature did not register OpacityDecorator on LayerPresenter.");

                attacher.Detach();
                Check(layer.Decorators.Elements.Count == 0 && decorator!.OpacitySpec == null,
                    "Detaching FeatureAttacher did not unregister OpacityDecorator from LayerPresenter.");
            }

            var scene = new ScenePresenter();
            var sceneModel = new SceneSettings();

            using (var attacher = new FeatureAttacher(scene, features, registry) { FeatureContainer = sceneModel })
            {
                attacher.Attach();
                sceneModel.Features.Add(new FeatureInfo
                {
                    Id = FeatureId,
                    Settings = new OpacityFeatureSettings { Opacity = 0.5, BlendMode = OpacityBlendMode.Difference }
                });

                var decorator = scene.Decorators.Elements.OfType<OpacityDecorator>().SingleOrDefault();
                Check(decorator != null
                    && decorator.OpacitySpec == new OpacityEffectSpec(0.5, OpacityBlendMode.Difference),
                    "OpacityFeature did not register OpacityDecorator on ScenePresenter.");

                attacher.Detach();
                Check(scene.Decorators.Elements.Count == 0 && decorator!.OpacitySpec == null,
                    "Detaching FeatureAttacher did not unregister OpacityDecorator from ScenePresenter.");
            }
        });

        run("Opacity feature orders decorator chain correctly and survives stale Unloaded", () =>
        {
            IAppHost.Host = new HostBuilder().ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<FeatureRegistryService>();
                services.AddSingleton<IFeatureService, FeatureService>();
                services.AddSingleton<IComponentsService, ComponentsService>();
                services.AddSingleton<TimeProvider>(TimeProvider.System);
                services.AddComponent<TextComponent>();
                services.AddFeature<OpacityFeature, OpacitySettingsControl>();
                services.AddFeature<ShadowFeature, ShadowSettingsControl>();
                services.AddFeature<StrokeFeature, StrokeSettingsControl>();
                services.AddFeature<BorderFeature, BorderSettingsControl>();
                services.AddFeature<ParallaxFeature, ParallaxSettingsControl>();
            }).Build();

            var component = new ComponentPresenter
            {
                IsEditing = true,
                Settings = new ComponentSettings
                {
                    Id = TextComponent.Id,
                    Settings = new TextComponentSettings { Text = "Opacity" },
                    Features =
                    [
                        new FeatureInfo { Id = "omls.features.appearance.border", Settings = new BorderFeatureSettings() },
                        new FeatureInfo { Id = StrokeFeature.Id, Settings = new StrokeFeatureSettings() },
                        new FeatureInfo { Id = ShadowFeature.Id, Settings = new ShadowFeatureSettings() },
                        new FeatureInfo { Id = FeatureId, Settings = new OpacityFeatureSettings { Opacity = 0.8, BlendMode = OpacityBlendMode.Overlay } }
                    ]
                }
            };
            component.Load();
            component.SetVisibilitySuppressed(new object(), true);

            Check(component.Decorators.Elements.Count == 5
                && component.Decorators.Elements[0] is OpacityDecorator
                && component.Decorators.Elements[1] is ShadowDecorator
                && component.Decorators.Elements[2] is StripeMaskDecorator
                && component.Decorators.Elements[3] is StrokeDecorator
                && component.Decorators.Elements[4] is Border,
                "Component decorators did not maintain [Opacity, Shadow, StripeMask, Stroke, Border] priority order.");

            // Stale same-frame Unloaded while IsLoaded stays true must preserve active decorators.
            component.RaiseStaleUnloadForTest();
            Check(component.Decorators.Elements.Count == 5
                && component.Decorators.Elements[0] is OpacityDecorator,
                "Stale Unloaded tore down OpacityDecorator while still loaded.");

            component.Unload();
            Check(component.Decorators.Elements.Count == 0,
                "Real Unload did not clean up OpacityDecorator.");

            // LayerPresenter ordering: Parallax (-200) -> Opacity (-150) -> Shadow (-100) -> Stroke (50)
            var scene = new ScenePresenter { Scene = new SceneSettings() };
            var layer = new LayerPresenter
            {
                ParallaxHost = scene.ParallaxHost,
                Layer = new LayerSettings
                {
                    Features =
                    [
                        new FeatureInfo { Id = StrokeFeature.Id, Settings = new StrokeFeatureSettings() },
                        new FeatureInfo { Id = ShadowFeature.Id, Settings = new ShadowFeatureSettings() },
                        new FeatureInfo { Id = FeatureId, Settings = new OpacityFeatureSettings { Opacity = 0.75, BlendMode = OpacityBlendMode.SoftLight } },
                        new FeatureInfo { Id = "omls.features.layout.parallax" }
                    ]
                }
            };
            layer.Load();
            Check(layer.Decorators.Elements.Count == 4
                && layer.Decorators.Elements[0] is Border
                && layer.Decorators.Elements[1] is OpacityDecorator
                && layer.Decorators.Elements[2] is ShadowDecorator
                && layer.Decorators.Elements[3] is StrokeDecorator,
                "Layer decorators did not maintain [Parallax Border, Opacity, Shadow, Stroke] priority order.");
            layer.Unload();
            Check(layer.Decorators.Elements.Count == 0, "Unloading LayerPresenter did not clean up OpacityDecorator.");
            scene.Unload();
        });

        run("OpacitySettingsViewModel syncs Opacity, BlendMode, ignores invalid writes, resets, and disposes", () =>
        {
            var settings = new OpacityFeatureSettings();
            using var vm = new OpacitySettingsViewModel(settings);
            vm.Attach();

            var changed = new List<string>();
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != null)
                {
                    changed.Add(args.PropertyName);
                }
            };

            Check(vm.Opacity == 1.0
                && vm.OpacityPercent == 100
                && vm.BlendMode == OpacityBlendMode.Normal
                && vm.BlendModeIndex == 0,
                "Initial OpacitySettingsViewModel state was unexpected.");

            vm.OpacityPercent = 45;
            Check(settings.Opacity == 0.45 && vm.Opacity == 0.45,
                "Updating OpacityPercent did not synchronize settings.Opacity.");

            vm.Opacity = 0.674;
            Check(settings.Opacity == 0.674 && vm.OpacityPercent == 67,
                "Updating fractional Opacity did not keep precision and round OpacityPercent.");

            vm.BlendModeIndex = 7;
            Check(settings.BlendMode == OpacityBlendMode.Screen && vm.BlendMode == OpacityBlendMode.Screen,
                "Updating BlendModeIndex did not synchronize settings.BlendMode.");

            settings.BlendMode = OpacityBlendMode.Difference;
            var expectedDifferenceIndex = -1;
            for (var index = 0; index < OpacitySettingsViewModel.OrderedBlendModes.Count; index++)
            {
                if (OpacitySettingsViewModel.OrderedBlendModes[index] == OpacityBlendMode.Difference)
                {
                    expectedDifferenceIndex = index;
                    break;
                }
            }

            Check(vm.BlendMode == OpacityBlendMode.Difference && vm.BlendModeIndex == expectedDifferenceIndex,
                "External settings.BlendMode update did not refresh ViewModel BlendModeIndex.");

            // Out-of-range inputs are ignored
            vm.Opacity = -0.1;
            vm.Opacity = 1.2;
            vm.Opacity = double.NaN;
            vm.OpacityPercent = 120;
            vm.BlendModeIndex = -1;
            vm.BlendModeIndex = 999;
            Check(settings.Opacity == 0.674 && settings.BlendMode == OpacityBlendMode.Difference,
                "Invalid ViewModel writes modified settings.");

            vm.ResetCommand.Execute(null);
            Check(settings.Opacity == 1.0 && vm.OpacityPercent == 100
                && settings.BlendMode == OpacityBlendMode.Normal && vm.BlendModeIndex == 0,
                "ResetCommand did not restore default opacity and blend mode.");

            vm.Dispose();
            changed.Clear();
            settings.Opacity = 0.25;
            settings.BlendMode = OpacityBlendMode.Multiply;
            Check(changed.Count == 0, "Disposing OpacitySettingsViewModel left subscriptions attached.");
        });

        run("Adding and editing opacity features triggers scene auto-save and survives reload", () =>
        {
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(IAppHost).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(OpacityFeature).Assembly);

            var dataFolder = ApplicationData.Current.LocalFolder.Path;
            var settingsService = new SettingsService(LoggerFactory.Create(_ => { }).CreateLogger<SettingsService>(),
                Path.Combine(dataFolder, "settings.json"));
            var sceneService = new SceneService(settingsService,
                LoggerFactory.Create(_ => { }).CreateLogger<SceneService>(),
                dataFolder);
            sceneService.Initialize();

            var scene = sceneService.Create("OpacityTest");
            var changes = 0;
            using var graph = new ObservableSettingsGraph(scene, (_, _, _) => changes++);

            var sceneOpacity = new OpacityFeatureSettings { Opacity = 0.9, BlendMode = OpacityBlendMode.SoftLight };
            scene.Features.Add(new FeatureInfo { Id = FeatureId, Settings = sceneOpacity });

            var layer = new LayerSettings { Name = "OpacityLayer" };
            scene.Layers.Add(layer);
            var layerOpacity = new OpacityFeatureSettings { Opacity = 0.6, BlendMode = OpacityBlendMode.Screen };
            layer.Features.Add(new FeatureInfo { Id = FeatureId, Settings = layerOpacity });

            var component = new ComponentSettings
            {
                Id = "omls.components.text",
                Features =
                [
                    new FeatureInfo
                    {
                        Id = FeatureId,
                        Settings = new OpacityFeatureSettings
                        {
                            Opacity = 0.45,
                            BlendMode = OpacityBlendMode.Difference
                        }
                    }
                ]
            };
            layer.Children.Add(component);

            var countBeforeEdit = changes;
            layerOpacity.BlendMode = OpacityBlendMode.ColorDodge;
            Check(changes > countBeforeEdit, "Editing opacity settings did not notify ObservableSettingsGraph.");

            sceneService.QueueSave(scene);
            sceneService.FlushPending();
            var reloaded = sceneService.Load(scene.SceneId);

            var restoredSceneOpacity = reloaded.Features
                .Single(feature => feature.Id == FeatureId)
                .GetSettings<OpacityFeatureSettings>();
            var restoredLayerOpacity = reloaded.Layers[^1].Features
                .Single(feature => feature.Id == FeatureId)
                .GetSettings<OpacityFeatureSettings>();
            var restoredComponentOpacity = reloaded.Layers[^1].Children[0].Features
                .Single(feature => feature.Id == FeatureId)
                .GetSettings<OpacityFeatureSettings>();

            Check(restoredSceneOpacity.Opacity == 0.9 && restoredSceneOpacity.BlendMode == OpacityBlendMode.SoftLight,
                "Scene opacity settings did not survive scene save/reload.");
            Check(restoredLayerOpacity.Opacity == 0.6 && restoredLayerOpacity.BlendMode == OpacityBlendMode.ColorDodge,
                "Layer opacity settings did not survive scene save/reload.");
            Check(restoredComponentOpacity.Opacity == 0.45 && restoredComponentOpacity.BlendMode == OpacityBlendMode.Difference,
                "Component opacity settings did not survive scene save/reload.");
        });
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<FeatureRegistryService>();
        services.AddSingleton<IFeatureService, FeatureService>();
        services.AddFeature<OpacityFeature, OpacitySettingsControl>();
        return services.BuildServiceProvider();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
