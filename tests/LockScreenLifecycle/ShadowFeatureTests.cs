using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLockScreen;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Models.Features.Layout;
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
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;

internal static class ShadowFeatureTests
{
    private const string FeatureId = ShadowFeature.Id;

    internal static void Run(Action<string, Action> run)
    {
        run("Shadow feature registers metadata and targets components and layers without targeting scenes", () =>
        {
            using var provider = CreateProvider();
            var registry = provider.GetRequiredService<FeatureRegistryService>();
            var registration = registry.Find(FeatureId);
            Check(registration != null, "Shadow feature was not registered.");
            Check(registration!.Name == "阴影"
                && registration.TargetType == typeof(IShadowTarget)
                && registration.ClientSettingsType == typeof(ShadowFeatureSettings)
                && registration.SettingsType == typeof(ShadowSettingsControl),
                "Shadow feature metadata did not match its target interface, settings model, or control.");
            Check(registration.TargetType.IsAssignableFrom(typeof(ComponentPresenter))
                && registration.TargetType.IsAssignableFrom(typeof(LayerPresenter))
                && !registration.TargetType.IsAssignableFrom(typeof(ScenePresenter)),
                "Shadow feature did not restrict its target to ComponentPresenter and LayerPresenter.");
        });

        run("ShadowFeatureSettings sanitizes bounds, ColorJsonConverter validates colors, and JSON round-trips", () =>
        {
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(IAppHost).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(ShadowFeature).Assembly);

            var defaults = new ShadowFeatureSettings();
            Check(defaults.Color == Colors.Black
                && defaults.Opacity == 0.5
                && defaults.BlurRadius == 16
                && defaults.OffsetX == 0
                && defaults.OffsetY == 4,
                "ShadowFeatureSettings defaults were unexpected.");

            defaults.Opacity = -0.25;
            Check(defaults.Opacity == 0, "Negative opacity was not clamped to 0.");
            defaults.Opacity = 1.5;
            Check(defaults.Opacity == 1, "Oversized opacity was not clamped to 1.");
            defaults.Opacity = double.NaN;
            Check(defaults.Opacity == ShadowFeatureSettings.DefaultOpacity, "NaN opacity did not fall back.");

            defaults.BlurRadius = -5;
            Check(defaults.BlurRadius == 0, "Negative blur radius was not clamped to 0.");
            defaults.BlurRadius = 250;
            Check(defaults.BlurRadius == 100, "Oversized blur radius was not clamped to 100.");
            defaults.BlurRadius = double.PositiveInfinity;
            Check(defaults.BlurRadius == ShadowFeatureSettings.DefaultBlurRadius, "Infinite blur radius did not fall back.");

            defaults.OffsetX = -999;
            Check(defaults.OffsetX == -500, "Underflow OffsetX was not clamped to -500.");
            defaults.OffsetX = 999;
            Check(defaults.OffsetX == 500, "Overflow OffsetX was not clamped to 500.");
            defaults.OffsetX = double.NaN;
            Check(defaults.OffsetX == 0, "NaN OffsetX did not fall back to 0.");

            defaults.OffsetY = -0.0;
            Check(BitConverter.DoubleToInt64Bits(defaults.OffsetY) == 0, "Negative zero OffsetY was not normalized.");
            defaults.OffsetY = 999;
            Check(defaults.OffsetY == 500, "Overflow OffsetY was not clamped to 500.");
            defaults.OffsetY = double.NegativeInfinity;
            Check(defaults.OffsetY == ShadowFeatureSettings.DefaultOffsetY, "Infinite OffsetY did not fall back.");

            var options = new JsonSerializerOptions
            {
                TypeInfoResolver = AutoJsonPolymorphicRegistrationHelper.CreateJsonTypeInfoResolver()
            };

            var colorJson = JsonSerializer.Serialize(Color.FromArgb(128, 17, 34, 51), new JsonSerializerOptions
            {
                Converters = { new ColorJsonConverter() }
            });
            Check(colorJson == "\"#80112233\"", $"ColorJsonConverter wrote unexpected JSON: {colorJson}");

            var rgbColor = JsonSerializer.Deserialize<Color>("\"#123456\"", new JsonSerializerOptions
            {
                Converters = { new ColorJsonConverter() }
            });
            Check(rgbColor == Color.FromArgb(255, 0x12, 0x34, 0x56), "ColorJsonConverter did not parse #RRGGBB with opaque alpha.");

            foreach (var invalid in new[] { "null", "123", "\"123456\"", "\"#12345\"", "\"# 12345\"", "\"#GGGGGG\"", "\"#123456789\"" })
            {
                Throws<JsonException>(() => JsonSerializer.Deserialize<Color>(invalid, new JsonSerializerOptions
                {
                    Converters = { new ColorJsonConverter() }
                }));
            }

            var original = new FeatureInfo
            {
                Id = FeatureId,
                Settings = new ShadowFeatureSettings
                {
                    Color = Color.FromArgb(255, 255, 96, 32),
                    Opacity = 0,
                    BlurRadius = 48.5,
                    OffsetX = -18.25,
                    OffsetY = 0
                }
            };
            var json = JsonSerializer.Serialize(original, options);
            var restored = JsonSerializer.Deserialize<FeatureInfo>(json, options);
            Check(restored?.Settings is ShadowFeatureSettings roundTripped
                && roundTripped.Color == Color.FromArgb(255, 255, 96, 32)
                && roundTripped.Opacity == 0
                && roundTripped.BlurRadius == 48.5
                && roundTripped.OffsetX == -18.25
                && roundTripped.OffsetY == 0,
                "ShadowFeatureSettings did not round-trip accurately through FeatureInfo JSON.");
        });

        run("Shadow feature binds ShadowEffect.Shadow on components and layers and restores original values", () =>
        {
            using var provider = CreateProvider();
            var registry = provider.GetRequiredService<FeatureRegistryService>();
            var features = provider.GetRequiredService<IFeatureService>();

            var component = new ComponentPresenter();
            var componentModel = new ComponentSettings();
            var originalSpec = new ShadowEffectSpec(Colors.White, 0.2, 8, 1, 2);
            ShadowEffect.SetShadow(component, originalSpec);

            using (var attacher = new FeatureAttacher(component, features, registry) { FeatureContainer = componentModel })
            {
                attacher.Attach();
                var settings = new ShadowFeatureSettings();
                var info = new FeatureInfo { Id = FeatureId, Settings = settings };
                componentModel.Features.Add(info);

                var applied = ShadowEffect.GetShadow(component);
                Check(applied == new ShadowEffectSpec(Colors.Black, 0.5, 16, 0, 4),
                    "Initial shadow spec was not synchronized to ComponentPresenter.");

                settings.Color = Color.FromArgb(255, 10, 20, 30);
                settings.Opacity = 0.75;
                settings.BlurRadius = 30;
                settings.OffsetX = -12;
                settings.OffsetY = 24;
                Check(ShadowEffect.GetShadow(component) == new ShadowEffectSpec(Color.FromArgb(255, 10, 20, 30), 0.75, 30, -12, 24),
                    "Updated shadow spec was not synchronized to ComponentPresenter.");

                componentModel.Features.Remove(info);
                Check(ReferenceEquals(ShadowEffect.GetShadow(component), originalSpec),
                    "Removing ShadowFeature did not restore the original ShadowEffect.Shadow value.");
            }

            var layer = new LayerPresenter();
            var layerModel = new LayerSettings();
            var originalBinding = new Binding { Source = layerModel, Path = new PropertyPath("Name") };
            layer.SetBinding(ShadowEffect.ShadowProperty, originalBinding);

            using (var attacher = new FeatureAttacher(layer, features, registry) { FeatureContainer = layerModel })
            {
                attacher.Attach();
                var info = new FeatureInfo
                {
                    Id = FeatureId,
                    Settings = new ShadowFeatureSettings { Opacity = 0.8, BlurRadius = 24, OffsetX = 4, OffsetY = -6 }
                };
                layerModel.Features.Add(info);

                Check(ShadowEffect.GetShadow(layer) == new ShadowEffectSpec(Colors.Black, 0.8, 24, 4, -6),
                    "ShadowFeature did not bind ShadowEffect.Shadow on LayerPresenter.");

                attacher.Detach();
                Check(ReferenceEquals(layer.GetBindingExpression(ShadowEffect.ShadowProperty)?.ParentBinding, originalBinding),
                    "Detaching FeatureAttacher did not restore the original ShadowEffect.Shadow binding.");
            }
        });

        run("Shadow feature handles settings replacement, clearing, reattachment and coexistence with other features", () =>
        {
            using var provider = CreateProvider();
            IAppHost.Host = new HostBuilder().ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<FeatureRegistryService>();
                services.AddSingleton<IFeatureService, FeatureService>();
                services.AddSingleton<TimeProvider>(TimeProvider.System);
                services.AddFeature<ShadowFeature, ShadowSettingsControl>();
                services.AddFeature<TransformFeature, TransformSettingsControl>();
                services.AddFeature<ParallaxFeature, ParallaxSettingsControl>();
                services.AddFeature<BorderFeature, BorderSettingsControl>();
                services.AddFeature<BackgroundFeature, BackgroundSettingsControl>();
            }).Build();

            var settings = new ShadowFeatureSettings { BlurRadius = 10 };
            var replacement = new ShadowFeatureSettings { Color = Colors.White, Opacity = 0.9, BlurRadius = 40, OffsetX = 8, OffsetY = -8 };
            var first = new ComponentPresenter();
            var second = new LayerPresenter();

            using (var feature = new ShadowFeature())
            {
                feature.SetSettings(settings);
                feature.Attach(first);
                Check(ShadowEffect.GetShadow(first) == new ShadowEffectSpec(Colors.Black, 0.5, 10, 0, 4),
                    "Initial ShadowFeature attach failed.");

                feature.SetSettings(replacement);
                settings.BlurRadius = 88;
                Check(ShadowEffect.GetShadow(first) == new ShadowEffectSpec(Colors.White, 0.9, 40, 8, -8),
                    "Old settings continued updating ShadowEffect.Shadow after SetSettings.");

                feature.Attach(second);
                Check(ShadowEffect.GetShadow(first) == null
                    && ShadowEffect.GetShadow(second) == new ShadowEffectSpec(Colors.White, 0.9, 40, 8, -8),
                    "Reattaching ShadowFeature did not clear first target.");

                feature.SetSettings(null);
                Check(feature.Target == null && ShadowEffect.GetShadow(second) == null,
                    "Clearing settings did not detach ShadowFeature.");
            }

            // Coexistence on ComponentPresenter and LayerPresenter through Load/Unload
            var scene = new ScenePresenter { Scene = new SceneSettings() };
            var layerModel = new LayerSettings
            {
                Features =
                [
                    new FeatureInfo { Id = FeatureId, Settings = new ShadowFeatureSettings { OffsetY = 12 } },
                    new FeatureInfo { Id = "omls.features.layout.transform", Settings = new TransformFeatureSettings { TranslateX = 15 } },
                    new FeatureInfo { Id = "omls.features.layout.parallax" }
                ]
            };
            var layer = new LayerPresenter { Layer = layerModel, ParallaxHost = scene.ParallaxHost };
            layer.Load();
            Check(ShadowEffect.GetShadow(layer) == new ShadowEffectSpec(Colors.Black, 0.5, 16, 0, 12)
                && layer.RenderTransform is CompositeTransform { TranslateX: 15 },
                "ShadowFeature and TransformFeature/ParallaxFeature did not coexist on LayerPresenter.");
            layer.Unload();
            Check(ShadowEffect.GetShadow(layer) == null && layer.RenderTransform == null,
                "Unloading LayerPresenter did not detach ShadowFeature and TransformFeature cleanly.");
            scene.Unload();
        });

        run("ShadowSettingsViewModel syncs properties, handles presets, active toggle preservation, and reset", () =>
        {
            var settings = new ShadowFeatureSettings();
            using var vm = new ShadowSettingsViewModel(settings);
            vm.Attach();

            var changed = new List<string>();
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != null)
                {
                    changed.Add(args.PropertyName);
                }
            };

            Check(vm.Color == Colors.Black
                && vm.ColorHex == "#000000"
                && vm.Opacity == 0.5
                && vm.OpacityPercent == 50
                && vm.BlurRadius == 16
                && vm.BlurRadiusStep == 16
                && vm.OffsetX == 0
                && vm.OffsetY == 4
                && vm.IsSoftPreset
                && !vm.IsElevatedPreset
                && !vm.IsCrispPreset
                && !vm.IsGlowPreset,
                "Initial ShadowSettingsViewModel state was unexpected.");

            vm.OpacityPercent = 42;
            Check(settings.Opacity == 0.42 && vm.Opacity == 0.42 && !vm.IsSoftPreset,
                "Updating OpacityPercent did not synchronize settings.Opacity.");

            vm.Opacity = 0.654;
            Check(settings.Opacity == 0.654 && vm.OpacityPercent == 65,
                "Updating fractional Opacity did not round OpacityPercent for slider without mutating settings.");

            vm.BlurRadius = 24.4;
            Check(settings.BlurRadius == 24.4 && vm.BlurRadiusStep == 24,
                "Updating fractional BlurRadius did not keep fractional value.");
            vm.BlurRadiusStep = 32;
            Check(settings.BlurRadius == 32 && vm.BlurRadius == 32,
                "Updating BlurRadiusStep did not synchronize BlurRadius.");

            vm.OffsetX = -15.345;
            vm.OffsetY = 22.678;
            Check(settings.OffsetX == -15.35 && settings.OffsetY == 22.68,
                "Offset rounding did not round to 2 decimal places.");

            // Out-of-range inputs are ignored
            vm.Opacity = 2;
            vm.OpacityPercent = 150;
            vm.BlurRadius = -5;
            vm.OffsetX = 999;
            vm.OffsetY = -999;
            Check(settings.Opacity == 0.654 && settings.BlurRadius == 32
                && settings.OffsetX == -15.35 && settings.OffsetY == 22.68,
                "Out-of-range ViewModel writes modified settings.");

            // Presets
            vm.IsElevatedPreset = true;
            Check(settings.Color == Colors.Black && settings.Opacity == 0.42
                && settings.BlurRadius == 32 && settings.OffsetX == 0 && settings.OffsetY == 12
                && vm.IsElevatedPreset && !vm.IsSoftPreset,
                "Elevated preset did not apply expected values.");

            vm.IsCrispPreset = true;
            Check(settings.Color == Colors.Black && settings.Opacity == 0.65
                && settings.BlurRadius == 6 && settings.OffsetX == 0 && settings.OffsetY == 2
                && vm.IsCrispPreset,
                "Crisp preset did not apply expected values.");

            vm.IsGlowPreset = true;
            Check(settings.Color == Colors.White && vm.ColorHex == "#FFFFFF"
                && settings.Opacity == 0.6 && settings.BlurRadius == 24
                && settings.OffsetX == 0 && settings.OffsetY == 0
                && vm.IsGlowPreset,
                "Glow preset did not apply expected values.");

            // Clicking active preset does not uncheck it
            changed.Clear();
            vm.IsGlowPreset = false;
            Check(vm.IsGlowPreset && settings.Color == Colors.White && changed.Contains(nameof(vm.IsGlowPreset)),
                "Unchecking active preset cleared settings or skipped property notification.");

            // Reset restores Soft preset
            vm.Color = Color.FromArgb(255, 255, 0, 128);
            vm.OffsetX = 40;
            vm.ResetCommand.Execute(null);
            Check(settings.Color == Colors.Black && vm.ColorHex == "#000000"
                && settings.Opacity == 0.5 && settings.BlurRadius == 16
                && settings.OffsetX == 0 && settings.OffsetY == 4
                && vm.IsSoftPreset,
                "ResetCommand did not restore default shadow values.");

            vm.Dispose();
            changed.Clear();
            settings.BlurRadius = 77;
            Check(changed.Count == 0, "Disposing ShadowSettingsViewModel left subscriptions attached.");
        });

        run("Adding and editing shadow features triggers scene auto-save and survives reload", () =>
        {
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(IAppHost).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(ShadowFeature).Assembly);

            var dataFolder = ApplicationData.Current.LocalFolder.Path;
            var settingsService = new SettingsService(LoggerFactory.Create(_ => { }).CreateLogger<SettingsService>(),
                Path.Combine(dataFolder, "settings.json"));
            var sceneService = new SceneService(settingsService,
                LoggerFactory.Create(_ => { }).CreateLogger<SceneService>(),
                dataFolder);
            sceneService.Initialize();

            var scene = sceneService.Create("ShadowTest");
            var changes = 0;
            using var graph = new ObservableSettingsGraph(scene, (_, _, _) => changes++);

            var layer = new LayerSettings { Name = "ShadowLayer" };
            scene.Layers.Add(layer);
            var layerShadow = new ShadowFeatureSettings
            {
                Color = Color.FromArgb(255, 20, 40, 60),
                Opacity = 0.7,
                BlurRadius = 28,
                OffsetX = -6,
                OffsetY = 10
            };
            layer.Features.Add(new FeatureInfo { Id = FeatureId, Settings = layerShadow });

            var component = new ComponentSettings
            {
                Id = "omls.components.text",
                Features =
                [
                    new FeatureInfo
                    {
                        Id = FeatureId,
                        Settings = new ShadowFeatureSettings
                        {
                            Color = Color.FromArgb(255, 255, 128, 0),
                            Opacity = 0.35,
                            BlurRadius = 12,
                            OffsetX = 3,
                            OffsetY = 5
                        }
                    }
                ]
            };
            layer.Children.Add(component);

            var countBeforeEdit = changes;
            layerShadow.BlurRadius = 42;
            Check(changes > countBeforeEdit, "Editing shadow settings did not notify ObservableSettingsGraph.");

            sceneService.QueueSave(scene);
            sceneService.FlushPending();
            var reloaded = sceneService.Load(scene.SceneId);
            var restoredLayer = reloaded.Layers[^1];
            var restoredLayerShadow = restoredLayer.Features
                .Single(feature => feature.Id == FeatureId)
                .GetSettings<ShadowFeatureSettings>();
            var restoredComponentShadow = restoredLayer.Children[0].Features
                .Single(feature => feature.Id == FeatureId)
                .GetSettings<ShadowFeatureSettings>();

            Check(restoredLayerShadow.Color == Color.FromArgb(255, 20, 40, 60)
                && restoredLayerShadow.Opacity == 0.7
                && restoredLayerShadow.BlurRadius == 42
                && restoredLayerShadow.OffsetX == -6
                && restoredLayerShadow.OffsetY == 10,
                "Layer shadow settings did not survive scene save/reload.");
            Check(restoredComponentShadow.Color == Color.FromArgb(255, 255, 128, 0)
                && restoredComponentShadow.Opacity == 0.35
                && restoredComponentShadow.BlurRadius == 12
                && restoredComponentShadow.OffsetX == 3
                && restoredComponentShadow.OffsetY == 5,
                "Component shadow settings did not survive scene save/reload.");
        });
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<FeatureRegistryService>();
        services.AddSingleton<IFeatureService, FeatureService>();
        services.AddFeature<ShadowFeature, ShadowSettingsControl>();
        return services.BuildServiceProvider();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new Exception($"Expected {typeof(TException).Name}.");
    }
}
