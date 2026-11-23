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
using OhMyLockScreen.Shared.FeatureHost;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using OhMyLockScreen.ViewModels;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

internal static class TransformFeatureTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Transform feature attaches to components, layers and scenes and restores original values", () =>
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IFeatureService, FeatureService>();
            services.AddFeature<TransformFeature, TransformSettingsControl>();
            using var provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<FeatureRegistryService>();
            var registration = registry.Find("omls.features.layout.transform");
            Check(registration != null, "Transform feature was not registered.");
            Check(registration!.TargetType.IsAssignableFrom(typeof(ComponentPresenter))
                && registration.TargetType.IsAssignableFrom(typeof(LayerPresenter))
                && registration.TargetType.IsAssignableFrom(typeof(ScenePresenter)),
                "Transform feature does not target components, layers and scenes.");

            var targets = new (UIElement Target, OhMyLockScreen.Shared.Abstractions.Models.FeatureHost.IFeatureContainer Container)[]
            {
                (new ComponentPresenter(), new ComponentSettings()),
                (new LayerPresenter(), new LayerSettings()),
                (new ScenePresenter(), new SceneSettings())
            };

            foreach (var (target, container) in targets)
            {
                var originalTransform = new TranslateTransform { X = 12, Y = 34 };
                var originalOrigin = new Point(0.25, 0.75);
                target.RenderTransform = originalTransform;
                target.RenderTransformOrigin = originalOrigin;

                using var attacher = new FeatureAttacher(target,
                    provider.GetRequiredService<IFeatureService>(),
                    registry)
                {
                    FeatureContainer = container
                };
                attacher.Attach();

                var info = new FeatureInfo
                {
                    Id = "omls.features.layout.transform",
                    Settings = new TransformFeatureSettings
                    {
                        TranslateX = 40,
                        TranslateY = -20,
                        Rotation = 45,
                        ScaleX = 1.5,
                        ScaleY = 0.75,
                        FlipHorizontal = true,
                        FlipVertical = false,
                        SkewX = 10,
                        SkewY = -15,
                        OriginX = 0,
                        OriginY = 1
                    }
                };
                container.Features.Add(info);

                Check(target.RenderTransform is CompositeTransform applied
                    && applied.TranslateX == 40 && applied.TranslateY == -20
                    && applied.Rotation == 45
                    && applied.ScaleX == -1.5 && applied.ScaleY == 0.75
                    && applied.SkewX == 10 && applied.SkewY == -15
                    && target.RenderTransformOrigin == new Point(0, 1),
                    $"Transform feature did not apply properly to {target.GetType().Name}.");

                container.Features.Remove(info);
                Check(ReferenceEquals(target.RenderTransform, originalTransform)
                    && target.RenderTransformOrigin == originalOrigin,
                    $"Removing transform feature did not restore original transform on {target.GetType().Name}.");
            }
        });

        run("Transform feature synchronizes edits, clamps skew, sanitizes non-finite values, and handles reattach/replace", () =>
        {
            var settings = new TransformFeatureSettings();
            using var feature = new TransformFeature();
            feature.SetSettings(settings);
            var first = new Control();
            var second = new Control();

            feature.Attach(first);
            var t1 = first.RenderTransform as CompositeTransform
                ?? throw new Exception("Default RenderTransform was not a CompositeTransform.");
            Check(t1.TranslateX == 0 && t1.TranslateY == 0 && t1.Rotation == 0
                && t1.ScaleX == 1 && t1.ScaleY == 1 && t1.SkewX == 0 && t1.SkewY == 0
                && first.RenderTransformOrigin == new Point(0.5, 0.5),
                "Default transform values were not applied.");

            settings.TranslateX = 24;
            settings.TranslateY = -18;
            settings.Rotation = 135;
            settings.ScaleX = 2;
            settings.ScaleY = 0.5;
            settings.FlipVertical = true;
            settings.SkewX = 120;
            settings.SkewY = -95;
            settings.OriginX = 1;
            settings.OriginY = 0;
            Check(t1.TranslateX == 24 && t1.TranslateY == -18 && t1.Rotation == 135
                && t1.ScaleX == 2 && t1.ScaleY == -0.5
                && t1.SkewX == 89 && t1.SkewY == -89
                && first.RenderTransformOrigin == new Point(1, 0),
                "Updated transform values were not synchronized or clamped.");

            settings.TranslateX = double.NaN;
            settings.ScaleX = double.PositiveInfinity;
            settings.OriginX = double.NaN;
            Check(t1.TranslateX == 0 && t1.ScaleX == 1 && first.RenderTransformOrigin == new Point(0.5, 0),
                "Non-finite values were not sanitized to safe fallbacks.");

            feature.Attach(second);
            Check(first.RenderTransform == null && first.RenderTransformOrigin == default
                && second.RenderTransform is CompositeTransform,
                "Reattaching to a second target did not restore the first target.");

            var replacement = new TransformFeatureSettings { Rotation = 90 };
            feature.SetSettings(replacement);
            settings.Rotation = 15;
            var t2 = second.RenderTransform as CompositeTransform
                ?? throw new Exception("Replacing settings did not attach a CompositeTransform.");
            Check(t2.Rotation == 90,
                "Replacing settings retained the old source.");

            replacement.Rotation = 180;
            Check(t2.Rotation == 180, "Replacement settings were not observed.");

            feature.Detach();
            feature.Detach();
            replacement.Rotation = 270;
            Check(feature.Target == null && second.RenderTransform == null && second.RenderTransformOrigin == default,
                "Detach did not restore target transform or unsubscribe.");
        });

        run("TransformSettingsViewModel supports scale lock, rotate 90, flips, nine-point origin and reset", () =>
        {
            var settings = new TransformFeatureSettings();
            using var vm = new TransformSettingsViewModel(settings);
            vm.Attach();

            var changed = new List<string>();
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != null)
                {
                    changed.Add(args.PropertyName);
                }
            };

            Check(vm.IsScaleLocked && vm.IsCenter && !vm.IsTopLeft, "Initial view model state was unexpected.");

            // Scale lock preserves aspect ratio
            vm.ScaleX = 2;
            Check(settings.ScaleX == 2 && settings.ScaleY == 2, "Locked ScaleX did not scale ScaleY proportionally.");

            vm.IsScaleLocked = false;
            vm.ScaleY = 1;
            Check(settings.ScaleX == 2 && settings.ScaleY == 1, "Unlocked ScaleY affected ScaleX.");

            vm.IsScaleLocked = true;
            vm.ScaleX = 4;
            Check(settings.ScaleX == 4 && settings.ScaleY == 2, "Locked ScaleX did not preserve 2:1 ratio.");

            // Zero scale edge case under lock
            vm.ScaleX = 0;
            Check(settings.ScaleX == 0 && settings.ScaleY == 0, "Scaling to 0 did not zero both axes.");
            vm.ScaleX = 1.5;
            Check(settings.ScaleX == 1.5 && settings.ScaleY == 1.5, "Scaling from 0 under lock did not restore uniform scale.");

            // Rotate 90 wraps cleanly in [0, 360)
            vm.Rotate90Command.Execute(null);
            Check(settings.Rotation == 90, "First Rotate90 did not produce 90.");
            vm.Rotate90Command.Execute(null);
            vm.Rotate90Command.Execute(null);
            vm.Rotate90Command.Execute(null);
            Check(settings.Rotation == 0, "Four Rotate90 clicks did not wrap to 0.");
            settings.Rotation = -45;
            vm.Rotate90Command.Execute(null);
            Check(settings.Rotation == 45, "Rotate90 from -45 did not produce 45.");

            // Flips & anchors
            vm.FlipHorizontal = true;
            vm.FlipVertical = true;
            Check(settings.FlipHorizontal && settings.FlipVertical, "Flip toggles did not update settings.");

            vm.IsTopLeft = true;
            Check(settings.OriginX == 0 && settings.OriginY == 0 && vm.IsTopLeft && !vm.IsCenter,
                "Selecting TopLeft anchor did not update origin.");

            // Clicking the already active anchor keeps it selected
            changed.Clear();
            vm.IsTopLeft = false;
            Check(settings.OriginX == 0 && settings.OriginY == 0 && vm.IsTopLeft && changed.Contains(nameof(vm.IsTopLeft)),
                "Unchecking active anchor cleared origin or did not notify view.");

            // Non-preset origin deselects all nine preset anchors
            vm.OriginX = 0.25;
            Check(!vm.IsTopLeft && !vm.IsCenter && !vm.IsBottomRight,
                "Custom origin still matched a preset anchor.");

            // Reset restores identity transform
            vm.TranslateX = 100;
            vm.TranslateY = -50;
            vm.SkewX = 15;
            vm.SkewY = -10;
            vm.ResetCommand.Execute(null);
            Check(settings.TranslateX == 0 && settings.TranslateY == 0 && settings.Rotation == 0
                && settings.ScaleX == 1 && settings.ScaleY == 1
                && !settings.FlipHorizontal && !settings.FlipVertical
                && settings.SkewX == 0 && settings.SkewY == 0
                && settings.OriginX == 0.5 && settings.OriginY == 0.5
                && vm.IsCenter,
                "Reset did not restore identity transform values.");
        });

        run("TransformFeatureSettings round-trips through polymorphic FeatureInfo JSON serialization", () =>
        {
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(IAppHost).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(TransformFeature).Assembly);

            var options = new JsonSerializerOptions
            {
                TypeInfoResolver = AutoJsonPolymorphicRegistrationHelper.CreateJsonTypeInfoResolver()
            };
            var original = new FeatureInfo
            {
                Id = "omls.features.layout.transform",
                Settings = new TransformFeatureSettings
                {
                    TranslateX = 12.5,
                    TranslateY = -30,
                    Rotation = 30,
                    ScaleX = 0,
                    ScaleY = 1.25,
                    FlipHorizontal = true,
                    FlipVertical = false,
                    IsScaleLocked = false,
                    SkewX = 5,
                    SkewY = -8,
                    OriginX = 0,
                    OriginY = 1
                }
            };

            var json = JsonSerializer.Serialize(original, options);
            var restored = JsonSerializer.Deserialize<FeatureInfo>(json, options);
            Check(restored?.Settings is TransformFeatureSettings s
                && s.TranslateX == 12.5 && s.TranslateY == -30 && s.Rotation == 30
                && s.ScaleX == 0 && s.ScaleY == 1.25
                && s.FlipHorizontal && !s.FlipVertical && !s.IsScaleLocked
                && s.SkewX == 5 && s.SkewY == -8
                && s.OriginX == 0 && s.OriginY == 1,
                "TransformFeatureSettings did not round-trip accurately (including 0 / false non-default values).");
        });

        run("Untransformed layout bounds remain exact under RenderTransform across Grid alignments and Canvas", () =>
        {
            var grid = new Grid { ActualWidth = 1000, ActualHeight = 600 };
            var area = new Rect(0, 0, 1000, 600);

            foreach (var (x, y, hAlign, vAlign, autoAlign) in new[]
            {
                (40d, 30d, HorizontalAlignment.Left, VerticalAlignment.Top, true),
                (420d, 260d, HorizontalAlignment.Center, VerticalAlignment.Center, true),
                (760d, 490d, HorizontalAlignment.Right, VerticalAlignment.Bottom, true),
                (150d, 90d, HorizontalAlignment.Stretch, VerticalAlignment.Stretch, false)
            })
            {
                var layout = new LayoutFeatureSettings
                {
                    HorizontalAlignment = hAlign,
                    VerticalAlignment = vAlign,
                    IsAutoAlignment = autoAlign
                };
                OhMyLockScreen.Services.Scene.SceneLayoutEditing.ApplyBounds(
                    layout, x, y, 200, 80, area.Width, area.Height, resize: false, canvas: false, autoAlignment: autoAlign);

                var target = new ComponentPresenter
                {
                    ActualWidth = 200,
                    ActualHeight = 80,
                    HorizontalAlignment = layout.HorizontalAlignment,
                    VerticalAlignment = layout.VerticalAlignment,
                    Margin = layout.Margin,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new CompositeTransform
                    {
                        TranslateX = 180,
                        TranslateY = -95,
                        Rotation = 45,
                        ScaleX = -2.2,
                        ScaleY = 1.6,
                        SkewX = 25,
                        SkewY = -15
                    }
                };

                var startBounds = OhMyLockScreen.Services.Scene.SceneLayoutEditing.GetLayoutBounds(target, grid, area);
                Check(Math.Abs(startBounds.X - x) < 0.01 && Math.Abs(startBounds.Y - y) < 0.01
                    && startBounds.Width == 200 && startBounds.Height == 80,
                    $"Grid layout bounds for ({hAlign}, {vAlign}) were affected by RenderTransform: {startBounds}");

                // Dragging by (+10, -5) should shift layout by exactly (+10, -5) without jumping.
                OhMyLockScreen.Services.Scene.SceneLayoutEditing.ApplyBounds(
                    layout, startBounds.X + 10, startBounds.Y - 5, startBounds.Width, startBounds.Height,
                    area.Width, area.Height, resize: false, canvas: false, autoAlignment: autoAlign);
                target.HorizontalAlignment = layout.HorizontalAlignment;
                target.VerticalAlignment = layout.VerticalAlignment;
                target.Margin = layout.Margin;
                var movedBounds = OhMyLockScreen.Services.Scene.SceneLayoutEditing.GetLayoutBounds(target, grid, area);
                Check(Math.Abs(movedBounds.X - (x + 10)) < 0.01 && Math.Abs(movedBounds.Y - (y - 5)) < 0.01,
                    $"Moving transformed element in Grid produced abnormal displacement: expected ({x + 10}, {y - 5}), got ({movedBounds.X}, {movedBounds.Y})");
            }

            // Canvas layout bounds ignore RenderTransform too
            var canvas = new Canvas { ActualWidth = 1000, ActualHeight = 600 };
            var canvasTarget = new ComponentPresenter
            {
                ActualWidth = 120,
                ActualHeight = 60,
                Margin = new Thickness(75, 45, 0, 0),
                RenderTransform = new CompositeTransform { TranslateX = 300, Rotation = 90, ScaleX = 3 }
            };
            var canvasBounds = OhMyLockScreen.Services.Scene.SceneLayoutEditing.GetLayoutBounds(canvasTarget, canvas, area);
            Check(canvasBounds == new Rect(75, 45, 120, 60),
                $"Canvas layout bounds were affected by RenderTransform: {canvasBounds}");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
