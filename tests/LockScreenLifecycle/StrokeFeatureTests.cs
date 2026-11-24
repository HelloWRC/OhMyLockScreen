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
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using OhMyLockScreen.ViewModels;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

internal static class StrokeFeatureTests
{
    private const string FeatureId = StrokeFeature.Id;

    internal static void Run(Action<string, Action> run)
    {
        run("Stroke feature registers metadata and targets IDecoratorHost across scenes, layers, and components", () =>
        {
            using var provider = CreateProvider();
            var registry = provider.GetRequiredService<FeatureRegistryService>();
            var registration = registry.Find(FeatureId);
            Check(registration != null, "Stroke feature was not registered.");
            Check(registration!.Name == "描边"
                && registration.TargetType == typeof(IDecoratorHost)
                && registration.ClientSettingsType == typeof(StrokeFeatureSettings)
                && registration.SettingsType == typeof(StrokeSettingsControl),
                "Stroke feature metadata did not match IDecoratorHost, settings model, or control.");
            Check(registration.TargetType.IsAssignableFrom(typeof(ComponentPresenter))
                && registration.TargetType.IsAssignableFrom(typeof(LayerPresenter))
                && registration.TargetType.IsAssignableFrom(typeof(ScenePresenter))
                && !registration.TargetType.IsAssignableFrom(typeof(FrameworkElement)),
                "Stroke feature did not target IDecoratorHost implementations exclusively.");
        });

        run("StrokeFeatureSettings defaults copy theme foreground, sanitize bounds/enums, and round-trip JSON", () =>
        {
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(IAppHost).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(StrokeFeature).Assembly);

            var app = new Application();
            var themeBrush = new SolidColorBrush(Color.FromArgb(255, 22, 44, 66));
            app.Resources["SystemControlForegroundBaseHighBrush"] = themeBrush;

            var defaults = new StrokeFeatureSettings();
            Check(defaults.StrokeBrush is SolidColorBrush copied && copied.Color == themeBrush.Color
                && !ReferenceEquals(copied, themeBrush)
                && defaults.Thickness == 2
                && !defaults.IsHollow
                && defaults.Position == StrokePosition.Outside,
                "StrokeFeatureSettings defaults were unexpected.");

            themeBrush.Color = Color.FromArgb(255, 99, 88, 77);
            Check(((SolidColorBrush)defaults.StrokeBrush!).Color == Color.FromArgb(255, 22, 44, 66),
                "Mutating theme brush affected existing StrokeFeatureSettings.");

            defaults.Thickness = -5;
            Check(defaults.Thickness == 0, "Negative thickness was not clamped to 0.");
            defaults.Thickness = 200;
            Check(defaults.Thickness == StrokeFeatureSettings.MaxThickness, "Oversized thickness was not clamped to MaxThickness.");
            defaults.Thickness = double.NaN;
            Check(defaults.Thickness == StrokeFeatureSettings.DefaultThickness, "NaN thickness did not fall back.");
            defaults.Thickness = double.PositiveInfinity;
            Check(defaults.Thickness == StrokeFeatureSettings.DefaultThickness, "Infinite thickness did not fall back.");
            defaults.Thickness = -0.0;
            Check(BitConverter.DoubleToInt64Bits(defaults.Thickness) == 0, "Negative zero thickness was not normalized.");

            defaults.Position = (StrokePosition)999;
            Check(defaults.Position == StrokePosition.Outside, "Invalid StrokePosition enum value did not fall back to Outside.");

            var options = new JsonSerializerOptions
            {
                TypeInfoResolver = AutoJsonPolymorphicRegistrationHelper.CreateJsonTypeInfoResolver(),
                Converters = { new BrushJsonConverterFactory() }
            };

            var imageBrush = new ImageBrush { Stretch = Windows.UI.Xaml.Media.Stretch.Uniform };
            SceneImageBrush.SetBlobId(imageBrush, "blob-stroke-test");

            var samples = new Brush?[]
            {
                new SolidColorBrush(Color.FromArgb(200, 10, 20, 30)),
                new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0),
                    GradientStops =
                    {
                        new GradientStop { Color = Color.FromArgb(255, 255, 0, 0), Offset = 0 },
                        new GradientStop { Color = Color.FromArgb(255, 0, 0, 255), Offset = 1 }
                    }
                },
                imageBrush,
                new AcrylicBrush
                {
                    BackgroundSource = AcrylicBackgroundSource.Backdrop,
                    TintColor = Color.FromArgb(255, 40, 80, 120),
                    TintOpacity = 0.65,
                    FallbackColor = Color.FromArgb(255, 40, 80, 120)
                },
                null
            };

            foreach (var brush in samples)
            {
                var info = new FeatureInfo
                {
                    Id = FeatureId,
                    Settings = new StrokeFeatureSettings
                    {
                        StrokeBrush = brush,
                        Thickness = 12.5,
                        IsHollow = true,
                        Position = StrokePosition.Center
                    }
                };
                var json = JsonSerializer.Serialize(info, options);
                var restored = JsonSerializer.Deserialize<FeatureInfo>(json, options);
                Check(restored?.Settings is StrokeFeatureSettings roundTripped
                    && roundTripped.Thickness == 12.5
                    && roundTripped.IsHollow
                    && roundTripped.Position == StrokePosition.Center
                    && ((brush == null && roundTripped.StrokeBrush == null)
                        || (brush != null && roundTripped.StrokeBrush?.GetType() == brush.GetType())),
                    $"StrokeFeatureSettings did not round-trip brush type {brush?.GetType().Name ?? "null"}.");
            }
        });

        run("StrokeMorphologyPlanner plans compact Gaussian-threshold stages, zero-fog bounds, buckets, and outsets", () =>
        {
            Check(StrokeMorphologyPlanner.GetEffectiveRadii(10, StrokePosition.Outside) == (10, 0)
                && StrokeMorphologyPlanner.GetEffectiveRadii(10, StrokePosition.Center) == (5, 5)
                && StrokeMorphologyPlanner.GetEffectiveRadii(10, StrokePosition.Inside) == (0, 10)
                && StrokeMorphologyPlanner.GetEffectiveRadii(0, StrokePosition.Outside) == (0, 0)
                && StrokeMorphologyPlanner.GetEffectiveRadii(-3, StrokePosition.Outside) == (0, 0)
                && StrokeMorphologyPlanner.GetEffectiveRadii(double.NaN, StrokePosition.Outside) == (0, 0),
                "Effective radii calculation failed.");

            var solidBrush = new SolidColorBrush(Colors.White);
            Check(StrokeMorphologyPlanner.GetVisualOutset(null) == 0
                && StrokeMorphologyPlanner.GetVisualOutset(new StrokeEffectSpec(null, 10, false, StrokePosition.Outside)) == 0
                && StrokeMorphologyPlanner.GetVisualOutset(new StrokeEffectSpec(solidBrush, 0, false, StrokePosition.Outside)) == 0
                && StrokeMorphologyPlanner.GetVisualOutset(new StrokeEffectSpec(solidBrush, 10, false, StrokePosition.Inside)) == 0
                && StrokeMorphologyPlanner.GetVisualOutset(new StrokeEffectSpec(solidBrush, 10, false, StrokePosition.Center)) == 7
                && StrokeMorphologyPlanner.GetVisualOutset(new StrokeEffectSpec(solidBrush, 10.2, false, StrokePosition.Outside)) == 13,
                "Visual outset calculation did not match expected padded radii.");

            var prevPad = 0f;
            foreach (var radius in new[] { 0.0, 0.5, 4.0, 6.0, 8.0, 16.0, 24.0, 32.0, 48.0, 64.0 })
            {
                var pad = StrokeMorphologyPlanner.GetSurfacePad(radius);
                var minExpected = radius > 0 ? (float)(Math.Ceiling(radius) + 2) : 2f;
                Check(pad >= minExpected && pad >= prevPad,
                    $"Surface pad bucket ({pad}) violated monotonicity or margin ({minExpected}) for radius {radius}.");
                prevPad = pad;
            }

            Check(StrokeMorphologyPlanner.GetSurfacePad(-2) == 2f && StrokeMorphologyPlanner.GetSurfacePad(double.NaN) == 2f,
                "Surface pad did not sanitize non-positive/NaN radii.");

            foreach (var invalid in new[] { 0.0, -1.0, double.NaN })
            {
                var empty = StrokeMorphologyPlanner.BuildPlan(invalid, 1.5);
                Check(!empty.UsesHalfResolution && empty.Passes.Count == 0,
                    $"Expected empty morphology plan for radius {invalid}.");
            }

            foreach (var scale in new[] { 1.0, 1.5, 2.0, 3.0 })
            {
                foreach (var radius in new[] { 0.25, 0.5, 1.0, 2.0, 8.0, 16.0, 24.0, 32.0, 48.0, 64.0 })
                {
                    var plan = StrokeMorphologyPlanner.BuildPlan(radius, scale);
                    var expectHalfRes = radius * scale > 24.0;
                    Check(plan.UsesHalfResolution == expectHalfRes
                        && plan.Passes.Count >= (expectHalfRes ? 3 : 1),
                        $"Unexpected half-res flag or pass count for r={radius}, scale={scale} (half={plan.UsesHalfResolution}, passes={plan.Passes.Count}).");

                    foreach (var pass in plan.Passes)
                    {
                        Check(pass.BlurAmount is >= 0.75f and <= 12.01f
                            && pass.Gain > 0f
                            && pass.DilateOffset <= 1e-4f
                            && pass.Gain + pass.DilateOffset >= 1f - 1e-4f
                            && pass.ErodeOffset <= 1e-4f
                            && pass.Gain + pass.ErodeOffset >= 1f - 1e-4f
                            && Math.Abs(pass.DilateOffset + pass.ErodeOffset + pass.Gain - 1f) < 1e-4f,
                            $"Pass invariants failed for r={radius}, scale={scale}: {pass}.");
                    }
                }
            }

            var plan15 = StrokeMorphologyPlanner.BuildPlan(64, 1.5);
            var plan20 = StrokeMorphologyPlanner.BuildPlan(64, 2.0);
            Check(plan15.Passes.Count <= 7 && plan20.Passes.Count <= 8,
                $"Thick-stroke pass counts exceeded budget (1.5x={plan15.Passes.Count}, 2.0x={plan20.Passes.Count}).");
        });

        run("Stroke feature binds StrokeDecorator across hosts, resolves blobs, isolates composition brushes, and cleans up", () =>
        {
            using var provider = CreateProvider();
            var registry = provider.GetRequiredService<FeatureRegistryService>();
            var features = provider.GetRequiredService<IFeatureService>();

            var blobStorage = new FakeBlobStorageService();
            var converter = new SceneBlobUriConverter(blobStorage, Guid.NewGuid());
            var imageBrush = new ImageBrush();
            SceneImageBrush.SetBlobId(imageBrush, "stroke-blob-1");
            var acrylicBrush = new AcrylicBrush { TintColor = Color.FromArgb(255, 20, 40, 60), TintOpacity = 0.7 };

            var componentSettings = new StrokeFeatureSettings
            {
                StrokeBrush = imageBrush,
                Thickness = 6,
                IsHollow = true,
                Position = StrokePosition.Center
            };
            var componentModel = new ComponentSettings
            {
                Features = [new FeatureInfo { Id = FeatureId, Settings = componentSettings }]
            };

            var first = new ComponentPresenter { BlobUriConverter = converter };
            var second = new ComponentPresenter { BlobUriConverter = converter };

            using (var firstAttacher = new FeatureAttacher(first, features, registry) { FeatureContainer = componentModel })
            using (var secondAttacher = new FeatureAttacher(second, features, registry) { FeatureContainer = componentModel })
            {
                firstAttacher.Attach();
                secondAttacher.Attach();

                var firstDecorator = first.Decorators.Elements.OfType<StrokeDecorator>().SingleOrDefault();
                var secondDecorator = second.Decorators.Elements.OfType<StrokeDecorator>().SingleOrDefault();
                Check(firstDecorator != null && secondDecorator != null && !ReferenceEquals(firstDecorator, secondDecorator),
                    "Independent presenters did not create distinct StrokeDecorator instances.");
                Check(firstDecorator!.StrokeSpec is { Thickness: 6, IsHollow: true, Position: StrokePosition.Center }
                    && firstDecorator.StrokeSpec.Brush is ImageBrush { ImageSource: Windows.UI.Xaml.Media.Imaging.BitmapImage bmp }
                    && bmp.UriSource?.OriginalString == "https://example.invalid/stroke-blob-1",
                    "StrokeFeature did not resolve SceneImageBrush blob URI.");

                componentSettings.StrokeBrush = acrylicBrush;
                var firstAcrylic = firstDecorator.StrokeSpec!.Brush as AcrylicBrush;
                var secondAcrylic = secondDecorator!.StrokeSpec!.Brush as AcrylicBrush;
                Check(firstAcrylic != null
                    && secondAcrylic != null
                    && !ReferenceEquals(firstAcrylic, acrylicBrush)
                    && !ReferenceEquals(secondAcrylic, acrylicBrush)
                    && !ReferenceEquals(firstAcrylic, secondAcrylic),
                    "StrokeFeature did not isolate XamlCompositionBrushBase copies per presenter.");

                componentSettings.Thickness = 14;
                componentSettings.Position = StrokePosition.Inside;
                componentSettings.IsHollow = false;
                Check(firstDecorator.StrokeSpec is { Thickness: 14, Position: StrokePosition.Inside, IsHollow: false }
                    && ReferenceEquals(firstDecorator.StrokeSpec.Brush, firstAcrylic),
                    "Updating non-brush stroke properties unnecessarily recreated resolved composition brush.");

                componentModel.Features.Clear();
                Check(first.Decorators.Elements.Count == 0 && firstDecorator.StrokeSpec == null
                    && second.Decorators.Elements.Count == 0 && secondDecorator.StrokeSpec == null,
                    "Clearing features did not unregister StrokeDecorator and clear bindings.");
            }

            var layer = new LayerPresenter();
            var layerModel = new LayerSettings
            {
                Features = [new FeatureInfo { Id = FeatureId, Settings = new StrokeFeatureSettings { Thickness = 4 } }]
            };
            using (var attacher = new FeatureAttacher(layer, features, registry) { FeatureContainer = layerModel })
            {
                attacher.Attach();
                var decorator = layer.Decorators.Elements.OfType<StrokeDecorator>().SingleOrDefault();
                Check(decorator?.StrokeSpec is { Thickness: 4, Position: StrokePosition.Outside },
                    "StrokeFeature did not register StrokeDecorator on LayerPresenter.");
                attacher.Detach();
                Check(layer.Decorators.Elements.Count == 0 && decorator!.StrokeSpec == null,
                    "Detaching FeatureAttacher did not unregister StrokeDecorator from LayerPresenter.");
            }

            var scene = new ScenePresenter();
            var sceneModel = new SceneSettings
            {
                Features = [new FeatureInfo { Id = FeatureId, Settings = new StrokeFeatureSettings { Thickness = 8, IsHollow = true } }]
            };
            using (var attacher = new FeatureAttacher(scene, features, registry) { FeatureContainer = sceneModel })
            {
                attacher.Attach();
                var decorator = scene.Decorators.Elements.OfType<StrokeDecorator>().SingleOrDefault();
                Check(decorator?.StrokeSpec is { Thickness: 8, IsHollow: true },
                    "StrokeFeature did not register StrokeDecorator on ScenePresenter.");
                attacher.Detach();
                Check(scene.Decorators.Elements.Count == 0 && decorator!.StrokeSpec == null,
                    "Detaching FeatureAttacher did not unregister StrokeDecorator from ScenePresenter.");
            }
        });

        run("Stroke decorator coexists in priority order with Shadow, StripeMask, Border, and Parallax and survives stale unload", () =>
        {
            IAppHost.Host = new HostBuilder().ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<IFeatureService, FeatureService>();
                services.AddSingleton<IComponentsService, ComponentsService>();
                services.AddSingleton<TimeProvider>(TimeProvider.System);
                services.AddComponent<TextComponent>();
                services.AddFeature<StrokeFeature, StrokeSettingsControl>();
                services.AddFeature<ShadowFeature, ShadowSettingsControl>();
                services.AddFeature<BorderFeature, BorderSettingsControl>();
                services.AddFeature<BackgroundFeature, BackgroundSettingsControl>();
                services.AddFeature<TransformFeature, TransformSettingsControl>();
                services.AddFeature<ParallaxFeature, ParallaxSettingsControl>();
            }).Build();

            var component = new ComponentPresenter
            {
                IsEditing = true,
                Settings = new ComponentSettings
                {
                    Id = TextComponent.Id,
                    Settings = new TextComponentSettings { Text = "Stroke" },
                    Features =
                    [
                        new FeatureInfo { Id = "omls.features.appearance.border", Settings = new BorderFeatureSettings() },
                        new FeatureInfo { Id = FeatureId, Settings = new StrokeFeatureSettings { Thickness = 6 } },
                        new FeatureInfo { Id = ShadowFeature.Id, Settings = new ShadowFeatureSettings() }
                    ]
                }
            };
            component.Load();
            Check(component.Decorators.Elements.Count == 4
                && component.Decorators.Elements[0] is ShadowDecorator
                && component.Decorators.Elements[1] is StripeMaskDecorator
                && component.Decorators.Elements[2] is StrokeDecorator
                && component.Decorators.Elements[3] is Border,
                $"Component decorators order was unexpected: {string.Join(", ", component.Decorators.Elements.Select(e => e.GetType().Name))}");

            component.RaiseStaleUnloadForTest();
            Check(component.Decorators.Elements.Count == 4,
                "Stale Unloaded tore down decorators including StrokeDecorator while still loaded.");

            component.Unload();
            Check(component.Decorators.Elements.Count == 0,
                "Unloading ComponentPresenter left decorators registered.");

            var scene = new ScenePresenter { Scene = new SceneSettings() };
            var layer = new LayerPresenter
            {
                ParallaxHost = scene.ParallaxHost,
                Layer = new LayerSettings
                {
                    Features =
                    [
                        new FeatureInfo { Id = FeatureId, Settings = new StrokeFeatureSettings { Thickness = 4 } },
                        new FeatureInfo { Id = ShadowFeature.Id, Settings = new ShadowFeatureSettings() },
                        new FeatureInfo { Id = "omls.features.layout.parallax" }
                    ]
                }
            };
            layer.Load();
            Check(layer.Decorators.Elements.Count == 3
                && layer.Decorators.Elements[0] is Border
                && layer.Decorators.Elements[1] is ShadowDecorator
                && layer.Decorators.Elements[2] is StrokeDecorator,
                $"Layer decorators order was unexpected: {string.Join(", ", layer.Decorators.Elements.Select(e => e.GetType().Name))}");
            layer.Unload();
            Check(layer.Decorators.Elements.Count == 0,
                "Unloading LayerPresenter did not clean up StrokeDecorator.");
            scene.Unload();
        });

        run("StrokeSettingsViewModel syncs properties, snaps slider steps, rejects invalid values, resets, and disposes", () =>
        {
            var settings = new StrokeFeatureSettings();
            using var vm = new StrokeSettingsViewModel(settings);
            vm.Attach();
            vm.Attach();

            var changed = new List<string?>();
            vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

            Check(vm.Thickness == 2
                && vm.ThicknessStep == 2
                && vm.Position == StrokePosition.Outside
                && vm.PositionIndex == (int)StrokePosition.Outside
                && !vm.IsHollow,
                "Initial StrokeSettingsViewModel state was unexpected.");

            vm.Thickness = 12.345;
            Check(settings.Thickness == 12.35 && vm.ThicknessStep == 12.5,
                "Thickness rounding or slider step synchronization failed.");

            vm.ThicknessStep = 18.25;
            Check(settings.Thickness == 18.5 && vm.Thickness == 18.5,
                "ThicknessStep snapping to 0.5 failed.");

            vm.Thickness = -1;
            vm.Thickness = 65;
            vm.Thickness = double.NaN;
            vm.ThicknessStep = 100;
            Check(settings.Thickness == 18.5,
                "Out-of-range ViewModel thickness edits modified settings.");

            vm.Position = StrokePosition.Inside;
            vm.IsHollow = true;
            Check(settings.Position == StrokePosition.Inside
                && vm.PositionIndex == (int)StrokePosition.Inside
                && changed.Contains(nameof(StrokeSettingsViewModel.PositionIndex))
                && settings.IsHollow,
                "Position or IsHollow did not propagate to settings.");

            changed.Clear();
            vm.PositionIndex = (int)StrokePosition.Center;
            Check(settings.Position == StrokePosition.Center
                && vm.Position == StrokePosition.Center
                && changed.Contains(nameof(StrokeSettingsViewModel.PositionIndex)),
                "PositionIndex did not synchronize back to settings.");

            vm.PositionIndex = -1;
            vm.PositionIndex = 99;
            Check(settings.Position == StrokePosition.Center,
                "Out-of-range PositionIndex modified settings.");

            settings.StrokeBrush = null;
            changed.Clear();
            vm.ResetCommand.Execute(null);
            Check(settings.StrokeBrush is SolidColorBrush
                && settings.Thickness == StrokeFeatureSettings.DefaultThickness
                && settings.Position == StrokePosition.Outside
                && vm.PositionIndex == (int)StrokePosition.Outside
                && changed.Contains(nameof(StrokeSettingsViewModel.PositionIndex))
                && !settings.IsHollow,
                "ResetCommand did not restore default stroke values.");

            vm.Dispose();
            vm.Dispose();
            changed.Clear();
            settings.Thickness = 40;
            settings.Position = StrokePosition.Center;
            settings.IsHollow = true;
            Check(changed.Count == 0, "Disposing StrokeSettingsViewModel left subscriptions attached.");
        });

        run("Adding and editing stroke features triggers scene auto-save and survives reload", () =>
        {
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(IAppHost).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(StrokeFeature).Assembly);

            var dataFolder = ApplicationData.Current.LocalFolder.Path;
            var settingsService = new SettingsService(LoggerFactory.Create(_ => { }).CreateLogger<SettingsService>(),
                Path.Combine(dataFolder, "settings.json"));
            var sceneService = new SceneService(settingsService,
                LoggerFactory.Create(_ => { }).CreateLogger<SceneService>(),
                dataFolder);
            sceneService.Initialize();

            var scene = sceneService.Create("StrokeTest");
            var changes = 0;
            using var graph = new ObservableSettingsGraph(scene, (_, _, _) => changes++);

            var sceneStroke = new StrokeFeatureSettings
            {
                StrokeBrush = new SolidColorBrush(Color.FromArgb(255, 255, 0, 128)),
                Thickness = 3,
                IsHollow = false,
                Position = StrokePosition.Inside
            };
            scene.Features.Add(new FeatureInfo { Id = FeatureId, Settings = sceneStroke });

            var layer = new LayerSettings { Name = "StrokeLayer" };
            scene.Layers.Add(layer);
            var layerStroke = new StrokeFeatureSettings
            {
                StrokeBrush = new SolidColorBrush(Color.FromArgb(255, 20, 40, 60)),
                Thickness = 10,
                IsHollow = true,
                Position = StrokePosition.Center
            };
            layer.Features.Add(new FeatureInfo { Id = FeatureId, Settings = layerStroke });

            var component = new ComponentSettings
            {
                Id = "omls.components.text",
                Features =
                [
                    new FeatureInfo
                    {
                        Id = FeatureId,
                        Settings = new StrokeFeatureSettings
                        {
                            StrokeBrush = new SolidColorBrush(Color.FromArgb(255, 255, 128, 0)),
                            Thickness = 5.5,
                            IsHollow = true,
                            Position = StrokePosition.Outside
                        }
                    }
                ]
            };
            layer.Children.Add(component);

            var countBeforeEdit = changes;
            layerStroke.Thickness = 22.5;
            Check(changes > countBeforeEdit, "Editing stroke settings did not notify ObservableSettingsGraph.");

            sceneService.QueueSave(scene);
            sceneService.FlushPending();
            var reloaded = sceneService.Load(scene.SceneId);

            var restoredSceneStroke = reloaded.Features
                .Single(feature => feature.Id == FeatureId)
                .GetSettings<StrokeFeatureSettings>();
            var restoredLayer = reloaded.Layers[^1];
            var restoredLayerStroke = restoredLayer.Features
                .Single(feature => feature.Id == FeatureId)
                .GetSettings<StrokeFeatureSettings>();
            var restoredComponentStroke = restoredLayer.Children[0].Features
                .Single(feature => feature.Id == FeatureId)
                .GetSettings<StrokeFeatureSettings>();

            Check(restoredSceneStroke.StrokeBrush is SolidColorBrush { Color: { R: 255, G: 0, B: 128 } }
                && restoredSceneStroke.Thickness == 3
                && !restoredSceneStroke.IsHollow
                && restoredSceneStroke.Position == StrokePosition.Inside,
                "Scene stroke settings did not survive scene save/reload.");
            Check(restoredLayerStroke.StrokeBrush is SolidColorBrush { Color: { R: 20, G: 40, B: 60 } }
                && restoredLayerStroke.Thickness == 22.5
                && restoredLayerStroke.IsHollow
                && restoredLayerStroke.Position == StrokePosition.Center,
                "Layer stroke settings did not survive scene save/reload.");
            Check(restoredComponentStroke.StrokeBrush is SolidColorBrush { Color: { R: 255, G: 128, B: 0 } }
                && restoredComponentStroke.Thickness == 5.5
                && restoredComponentStroke.IsHollow
                && restoredComponentStroke.Position == StrokePosition.Outside,
                "Component stroke settings did not survive scene save/reload.");
        });
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<FeatureRegistryService>();
        services.AddSingleton<IFeatureService, FeatureService>();
        services.AddFeature<StrokeFeature, StrokeSettingsControl>();
        return services.BuildServiceProvider();
    }

    private sealed class FakeBlobStorageService : ISceneBlobStorageService
    {
        public Uri? GetBlobUri(Guid sceneId, string? blobId)
            => string.IsNullOrWhiteSpace(blobId) ? null : new($"https://example.invalid/{blobId}");

        public Task<string> ImportAsync(Guid sceneId, Stream source, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
