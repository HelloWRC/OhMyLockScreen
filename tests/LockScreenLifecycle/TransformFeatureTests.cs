using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Services;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Features.Layout;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.FeatureHost;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using OhMyLockScreen.Shared.Services.Scene;
using OhMyLockScreen.ViewModels;
using Windows.Foundation;
using Windows.Storage;
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

        run("SceneTransformEditing self matrix matches UWP CompositeTransform matrix across compound transforms", () =>
        {
            var settings = new TransformFeatureSettings
            {
                TranslateX = 35.5,
                TranslateY = -42.25,
                Rotation = 37.5,
                ScaleX = 1.6,
                ScaleY = 0.75,
                FlipHorizontal = true,
                FlipVertical = false,
                SkewX = 14,
                SkewY = -9,
                OriginX = 0.25,
                OriginY = 0.8
            };
            const double width = 240;
            const double height = 120;

            var self = SceneTransformEditing.BuildSelfMatrix(settings, width, height);
            var fake = new CompositeTransform
            {
                CenterX = settings.OriginX * width,
                CenterY = settings.OriginY * height,
                ScaleX = -settings.ScaleX,
                ScaleY = settings.ScaleY,
                SkewX = settings.SkewX,
                SkewY = settings.SkewY,
                Rotation = settings.Rotation,
                TranslateX = settings.TranslateX,
                TranslateY = settings.TranslateY
            };
            var expected = BrushFakeTransforms.GetMatrix(fake);

            Check(Near(self.M11, expected.M11) && Near(self.M12, expected.M12)
                && Near(self.M21, expected.M21) && Near(self.M22, expected.M22)
                && Near(self.X, expected.OffsetX) && Near(self.Y, expected.OffsetY),
                $"BuildSelfMatrix {self} did not match CompositeTransform matrix {expected}.");
        });

        run("SceneTransformEditing selection geometry aligns with transformed corners, origin and outward rotation stem", () =>
        {
            const double width = 200;
            const double height = 100;
            var panelToViewport = new BrushEditorMatrix(1, 0, 0, 1, 300, 200);

            // Untransformed identity matches rectangle bounds
            var identityGeo = SceneTransformEditing.ComputeSelectionGeometry(width, height, panelToViewport, panelToViewport, null);
            Check(Near(identityGeo.TopLeft, new Point(300, 200))
                && Near(identityGeo.TopRight, new Point(500, 200))
                && Near(identityGeo.BottomRight, new Point(500, 300))
                && Near(identityGeo.BottomLeft, new Point(300, 300))
                && Near(identityGeo.Origin, new Point(400, 250))
                && Near(identityGeo.RotationStemStart, new Point(400, 200))
                && Near(identityGeo.RotationHandle, new Point(400, 200 - SceneSelectionGeometry.RotationHandleOffset)),
                $"Identity selection geometry mismatch: {identityGeo}");

            // 90 deg rotation rotates top edge to the right edge -> outward normal (+1, 0)
            var rot90 = new TransformFeatureSettings { Rotation = 90 };
            var targetToViewport90 = SceneTransformEditing.BuildSelfMatrix(rot90, width, height).Then(panelToViewport);
            var geo90 = SceneTransformEditing.ComputeSelectionGeometry(width, height, targetToViewport90, panelToViewport, rot90);
            Check(Near(geo90.RotationStemStart, new Point(450, 250))
                && Near(geo90.RotationHandle, new Point(450 + SceneSelectionGeometry.RotationHandleOffset, 250)),
                $"90-degree rotation stem did not extend right outward: {geo90.RotationHandle}");

            // Vertical flip turns top edge downward -> outward normal (0, +1)
            var flipV = new TransformFeatureSettings { FlipVertical = true };
            var targetToViewportFlip = SceneTransformEditing.BuildSelfMatrix(flipV, width, height).Then(panelToViewport);
            var geoFlip = SceneTransformEditing.ComputeSelectionGeometry(width, height, targetToViewportFlip, panelToViewport, flipV);
            Check(Near(geoFlip.RotationStemStart, new Point(400, 300))
                && Near(geoFlip.RotationHandle, new Point(400, 300 + SceneSelectionGeometry.RotationHandleOffset)),
                $"Vertically flipped top edge stem did not point outward downward: {geoFlip.RotationHandle}");

            // Skew + 45 deg rotation + parent transform keeps stem perpendicular to top edge and outside quad center
            var compound = new TransformFeatureSettings { Rotation = 45, SkewX = 20, ScaleX = 1.5, ScaleY = 0.8, OriginX = 0.25, OriginY = 0.75 };
            var parentSkew = new BrushEditorMatrix(1.2, 0.1, -0.15, 0.9, 100, 80);
            var targetToViewportCompound = SceneTransformEditing.BuildSelfMatrix(compound, width, height).Then(parentSkew);
            var geoCompound = SceneTransformEditing.ComputeSelectionGeometry(width, height, targetToViewportCompound, parentSkew, compound);
            var edgeX = geoCompound.TopRight.X - geoCompound.TopLeft.X;
            var edgeY = geoCompound.TopRight.Y - geoCompound.TopLeft.Y;
            var stemX = geoCompound.RotationHandle.X - geoCompound.RotationStemStart.X;
            var stemY = geoCompound.RotationHandle.Y - geoCompound.RotationStemStart.Y;
            var centerX = (geoCompound.TopLeft.X + geoCompound.TopRight.X + geoCompound.BottomRight.X + geoCompound.BottomLeft.X) / 4;
            var centerY = (geoCompound.TopLeft.Y + geoCompound.TopRight.Y + geoCompound.BottomRight.Y + geoCompound.BottomLeft.Y) / 4;
            Check(Math.Abs(edgeX * stemX + edgeY * stemY) < 1e-3
                && Near(double.Hypot(stemX, stemY), SceneSelectionGeometry.RotationHandleOffset)
                && (geoCompound.RotationStemStart.X - centerX) * stemX + (geoCompound.RotationStemStart.Y - centerY) * stemY > 0,
                "Compound transformed rotation handle was not perpendicular and outward from top edge.");

            // Degenerate zero scale remains finite and flips vertical direction cleanly
            var zeroScale = new TransformFeatureSettings { ScaleX = 0, ScaleY = 0, FlipVertical = true, Rotation = 0 };
            var targetToViewportZero = SceneTransformEditing.BuildSelfMatrix(zeroScale, width, height).Then(panelToViewport);
            var geoZero = SceneTransformEditing.ComputeSelectionGeometry(width, height, targetToViewportZero, panelToViewport, zeroScale);
            Check(double.IsFinite(geoZero.RotationHandle.X) && double.IsFinite(geoZero.RotationHandle.Y)
                && geoZero.RotationHandle.Y > geoZero.RotationStemStart.Y,
                $"Zero-scale fallback rotation stem failed: {geoZero.RotationHandle}");
        });

        run("SceneTransformEditing origin drag compensates translation, snaps with Ctrl bypass, and preserves grab offset", () =>
        {
            const double width = 200;
            const double height = 100;
            var panelToViewport = new BrushEditorMatrix(1, 0, 0, 1, 100, 100);

            // Identity transform never modifies translation
            var identity = new TransformFeatureSettings();
            var identityTarget = SceneTransformEditing.BuildSelfMatrix(identity, width, height).Then(panelToViewport);
            var startPivot = identityTarget.Map(new Point(100, 50));
            Check(SceneTransformEditing.TryBeginOriginDrag(identity, width, height, identityTarget, panelToViewport, startPivot, out var idState),
                "TryBeginOriginDrag failed on identity.");
            // Drag near top-left (within 8px snap threshold -> snaps to (0, 0))
            SceneTransformEditing.UpdateOriginDrag(idState, identity, new Point(104, 105), bypassSnap: false);
            Check(identity.OriginX == 0 && identity.OriginY == 0 && identity.TranslateX == 0 && identity.TranslateY == 0,
                $"Identity origin drag did not snap or modified translate: ({identity.OriginX}, {identity.OriginY}), T=({identity.TranslateX}, {identity.TranslateY})");

            // Ctrl bypass disables 9-point snap and preserves grab offset
            SceneTransformEditing.UpdateOriginDrag(idState, identity, new Point(104, 105), bypassSnap: true);
            Check(Near(identity.OriginX, 0.02, 1e-3) && Near(identity.OriginY, 0.05, 1e-3)
                && identity.TranslateX == 0 && identity.TranslateY == 0,
                $"Bypassing snap still snapped or altered translate: ({identity.OriginX}, {identity.OriginY})");

            // Rotated + scaled + skewed component dragged to visual TopLeft keeps all 4 visual corners stationary
            var rotated = new TransformFeatureSettings
            {
                TranslateX = 20,
                TranslateY = -10,
                Rotation = 45,
                ScaleX = 1.5,
                ScaleY = 0.8,
                SkewX = 12,
                OriginX = 0.5,
                OriginY = 0.5
            };
            var targetStart = SceneTransformEditing.BuildSelfMatrix(rotated, width, height).Then(panelToViewport);
            var visualCornersBefore = new[]
            {
                targetStart.Map(new Point(0, 0)),
                targetStart.Map(new Point(width, 0)),
                targetStart.Map(new Point(width, height)),
                targetStart.Map(new Point(0, height))
            };
            var pivotStart = targetStart.Map(new Point(rotated.OriginX * width, rotated.OriginY * height));
            var grabStart = new Point(pivotStart.X + 4, pivotStart.Y - 3);
            Check(SceneTransformEditing.TryBeginOriginDrag(rotated, width, height, targetStart, panelToViewport, grabStart, out var rotState),
                "TryBeginOriginDrag failed on rotated component.");

            // Moving pointer with zero delta must produce zero initial jump because grabOffset is preserved
            SceneTransformEditing.UpdateOriginDrag(rotState, rotated, grabStart, bypassSnap: true);
            Check(Near(rotated.OriginX, 0.5, 1e-3) && Near(rotated.OriginY, 0.5, 1e-3)
                && Near(rotated.TranslateX, 20, 1e-2) && Near(rotated.TranslateY, -10, 1e-2),
                "Off-center handle click caused an initial jump.");

            // Drag handle so desired pivot reaches visual TopLeft corner (preserving grab offset from pivot)
            var targetTopLeftPointer = new Point(visualCornersBefore[0].X + (grabStart.X - pivotStart.X), visualCornersBefore[0].Y + (grabStart.Y - pivotStart.Y));
            SceneTransformEditing.UpdateOriginDrag(rotState, rotated, targetTopLeftPointer, bypassSnap: false);
            Check(rotated.OriginX == 0 && rotated.OriginY == 0,
                $"Dragging origin to rotated visual TopLeft did not yield (0, 0): ({rotated.OriginX}, {rotated.OriginY})");

            var targetAfter = SceneTransformEditing.BuildSelfMatrix(rotated, width, height).Then(panelToViewport);
            var visualCornersAfter = new[]
            {
                targetAfter.Map(new Point(0, 0)),
                targetAfter.Map(new Point(width, 0)),
                targetAfter.Map(new Point(width, height)),
                targetAfter.Map(new Point(0, height))
            };
            for (var i = 0; i < 4; i++)
            {
                Check(Near(visualCornersBefore[i], visualCornersAfter[i], 0.05),
                    $"Corner {i} drifted after origin drag compensation: {visualCornersBefore[i]} -> {visualCornersAfter[i]}");
            }

            // Clamping and degenerate zero-scale fallback remain finite and safe
            SceneTransformEditing.UpdateOriginDrag(rotState, rotated, new Point(50000, -50000), bypassSnap: true);
            Check(rotated.OriginX is >= -10 and <= 10 && rotated.OriginY is >= -10 and <= 10,
                $"Origin drag escaped [-10, 10]: ({rotated.OriginX}, {rotated.OriginY})");

            var zeroScale = new TransformFeatureSettings { ScaleX = 0, ScaleY = 0 };
            var zeroTarget = SceneTransformEditing.BuildSelfMatrix(zeroScale, width, height).Then(panelToViewport);
            Check(SceneTransformEditing.TryBeginOriginDrag(zeroScale, width, height, zeroTarget, panelToViewport, new Point(200, 150), out var zeroState),
                "TryBeginOriginDrag failed on zero scale.");
            SceneTransformEditing.UpdateOriginDrag(zeroState, zeroScale, new Point(250, 175), bypassSnap: true);
            Check(Near(zeroScale.OriginX, 0.75, 1e-3) && Near(zeroScale.OriginY, 0.75, 1e-3),
                $"Zero-scale fallback origin drag produced unexpected result: ({zeroScale.OriginX}, {zeroScale.OriginY})");
        });

        run("SceneTransformEditing rotate drag unwraps continuously, snaps 15 deg, handles flips and tiny radii", () =>
        {
            const double width = 200;
            const double height = 100;
            var panelToViewport = new BrushEditorMatrix(1, 0, 0, 1, 200, 200);
            var settings = new TransformFeatureSettings { Rotation = 170, OriginX = 0.25, OriginY = 0.5 };
            var targetToViewport = SceneTransformEditing.BuildSelfMatrix(settings, width, height).Then(panelToViewport);
            var pivot = targetToViewport.Map(new Point(settings.OriginX * width, settings.OriginY * height)); // (250, 250)

            var start = new Point(pivot.X, pivot.Y - 80); // -90 deg
            Check(SceneTransformEditing.TryBeginRotateDrag(settings, width, height, targetToViewport, panelToViewport, start, out var state),
                "TryBeginRotateDrag failed.");

            // Tiny pointer movement right on pivot is safely ignored
            SceneTransformEditing.UpdateRotateDrag(ref state, settings, new Point(pivot.X + 0.5, pivot.Y + 0.5), snapAngle: false);
            Check(settings.Rotation == 170, "Pointer inside deadzone altered rotation.");

            // Cross +180 boundary continuously: written value normalizes to [-180, 180] while drag state stays continuous
            SceneTransformEditing.UpdateRotateDrag(ref state, settings, new Point(pivot.X + 80, pivot.Y), snapAngle: false);
            Check(Near(settings.Rotation, -100, 0.1), $"Expected -100 deg (from 260), got {settings.Rotation}.");
            SceneTransformEditing.UpdateRotateDrag(ref state, settings, new Point(pivot.X, pivot.Y + 80), snapAngle: false);
            SceneTransformEditing.UpdateRotateDrag(ref state, settings, new Point(pivot.X - 80, pivot.Y), snapAngle: false);
            var radCross = -170 * Math.PI / 180;
            SceneTransformEditing.UpdateRotateDrag(ref state, settings, new Point(pivot.X + 80 * Math.Cos(radCross), pivot.Y + 80 * Math.Sin(radCross)), snapAngle: false);
            Check(Near(settings.Rotation, 90, 0.2), $"Continuous wrap across 360 failed: {settings.Rotation}");

            // Shift snap to 15-degree increments normalized in [-180, 180]
            SceneTransformEditing.UpdateRotateDrag(ref state, settings, new Point(pivot.X + 80 * Math.Cos(-168 * Math.PI / 180), pivot.Y + 80 * Math.Sin(-168 * Math.PI / 180)), snapAngle: true);
            Check(settings.Rotation is >= -180 and <= 180 && Math.Abs(settings.Rotation % 15) < 1e-6,
                $"Shift snap did not land on 15-deg multiple within [-180, 180]: {settings.Rotation}");

            // NormalizeRotation boundary & precision cases
            Check(SceneTransformEditing.NormalizeRotation(180) == 180
                && SceneTransformEditing.NormalizeRotation(-180) == -180
                && SceneTransformEditing.NormalizeRotation(360) == 0
                && SceneTransformEditing.NormalizeRotation(-360) == 0
                && SceneTransformEditing.NormalizeRotation(540) == 180
                && SceneTransformEditing.NormalizeRotation(-540) == -180
                && SceneTransformEditing.NormalizeRotation(-195) == 165
                && Math.Round(SceneTransformEditing.NormalizeRotation(180.1), 1, MidpointRounding.AwayFromZero) == -179.9,
                "NormalizeRotation failed boundary or precision checks.");

            // Flipped parent container maps screen CCW/CW back into panel clockwise direction
            var flippedParent = new BrushEditorMatrix(-1, 0, 0, 1, 400, 200);
            var flippedSettings = new TransformFeatureSettings { Rotation = 0 };
            var flippedTarget = SceneTransformEditing.BuildSelfMatrix(flippedSettings, width, height).Then(flippedParent);
            var flippedPivot = flippedTarget.Map(new Point(100, 50)); // (300, 250)
            Check(SceneTransformEditing.TryBeginRotateDrag(flippedSettings, width, height, flippedTarget, flippedParent, new Point(flippedPivot.X, flippedPivot.Y - 60), out var flippedState),
                "TryBeginRotateDrag failed under flipped parent.");
            // In flipped parent (-X), moving right (+X in viewport) corresponds to -X in panel -> -90 deg panel rotation
            SceneTransformEditing.UpdateRotateDrag(ref flippedState, flippedSettings, new Point(flippedPivot.X + 60, flippedPivot.Y), snapAngle: false);
            Check(Near(flippedSettings.Rotation, -90, 0.1), $"Flipped parent rotation did not invert properly: {flippedSettings.Rotation}");
        });

        run("SceneTransformEditing scale drag scales around origin with lock, Shift toggle, skew, flips and zero recovery", () =>
        {
            const double width = 200;
            const double height = 100;
            var panelToViewport = new BrushEditorMatrix(1, 0, 0, 1, 100, 100);

            // Free scaling on rotated + skewed + flipped component tracks BottomRight corner 1:1 without changing Translate
            var free = new TransformFeatureSettings
            {
                TranslateX = 15,
                TranslateY = -25,
                Rotation = 35,
                SkewX = 15,
                ScaleX = 1,
                ScaleY = 1,
                FlipHorizontal = true,
                IsScaleLocked = false,
                OriginX = 0,
                OriginY = 0
            };
            var targetFree = SceneTransformEditing.BuildSelfMatrix(free, width, height).Then(panelToViewport);
            var startBr = targetFree.Map(new Point(width, height));
            Check(SceneTransformEditing.TryBeginScaleDrag(free, width, height, ResizeCorner.BottomRight, targetFree, panelToViewport, startBr, out var freeState),
                "TryBeginScaleDrag failed on compound transform.");

            // Target corner position corresponding to ScaleX = 2.0, ScaleY = 0.5
            var desiredSettings = new TransformFeatureSettings
            {
                TranslateX = 15,
                TranslateY = -25,
                Rotation = 35,
                SkewX = 15,
                ScaleX = 2.0,
                ScaleY = 0.5,
                FlipHorizontal = true,
                OriginX = 0,
                OriginY = 0
            };
            var desiredCorner = SceneTransformEditing.BuildSelfMatrix(desiredSettings, width, height).Then(panelToViewport).Map(new Point(width, height));
            SceneTransformEditing.UpdateScaleDrag(freeState, free, desiredCorner, toggleLock: false);
            Check(Near(free.ScaleX, 2.0, 1e-3) && Near(free.ScaleY, 0.5, 1e-3)
                && free.TranslateX == 15 && free.TranslateY == -25,
                $"Free scale tracking failed: S=({free.ScaleX}, {free.ScaleY}), T=({free.TranslateX}, {free.TranslateY})");

            // Shift toggles proportional lock ON from unlocked state (with 2:1 starting ratio)
            var ratio21 = new TransformFeatureSettings { ScaleX = 2.0, ScaleY = 1.0, IsScaleLocked = false };
            var target21 = SceneTransformEditing.BuildSelfMatrix(ratio21, width, height).Then(panelToViewport);
            var br21 = target21.Map(new Point(width, height));
            Check(SceneTransformEditing.TryBeginScaleDrag(ratio21, width, height, ResizeCorner.BottomRight, target21, panelToViewport, br21, out var ratioState),
                "TryBeginScaleDrag failed for 2:1 ratio.");
            var pivot21 = target21.Map(new Point(100, 50));
            // Double vector from pivot
            SceneTransformEditing.UpdateScaleDrag(ratioState, ratio21, new Point(pivot21.X + (br21.X - pivot21.X) * 2, pivot21.Y + (br21.Y - pivot21.Y) * 2), toggleLock: true);
            Check(Near(ratio21.ScaleX, 4.0, 1e-3) && Near(ratio21.ScaleY, 2.0, 1e-3),
                $"Shift-toggled proportional lock did not preserve 2:1 ratio: ({ratio21.ScaleX}, {ratio21.ScaleY})");

            // Clamping to MaxScale preserves ratio under lock
            SceneTransformEditing.UpdateScaleDrag(ratioState, ratio21, new Point(pivot21.X + (br21.X - pivot21.X) * 100, pivot21.Y + (br21.Y - pivot21.Y) * 100), toggleLock: true);
            Check(Near(ratio21.ScaleX, 100, 1e-3) && Near(ratio21.ScaleY, 50, 1e-3),
                $"Clamping proportional scale did not preserve 2:1 ratio: ({ratio21.ScaleX}, {ratio21.ScaleY})");

            // Dragging past pivot yields negative scale
            SceneTransformEditing.UpdateScaleDrag(ratioState, ratio21, new Point(pivot21.X - (br21.X - pivot21.X) * 0.5, pivot21.Y - (br21.Y - pivot21.Y) * 0.5), toggleLock: true);
            Check(Near(ratio21.ScaleX, -1.0, 1e-3) && Near(ratio21.ScaleY, -0.5, 1e-3),
                $"Dragging across pivot did not produce negative scale: ({ratio21.ScaleX}, {ratio21.ScaleY})");

            // Zero scale recovers cleanly and corner on pivot does not produce NaN
            var zero = new TransformFeatureSettings { ScaleX = 0, ScaleY = 0, IsScaleLocked = true };
            var zeroTarget = SceneTransformEditing.BuildSelfMatrix(zero, width, height).Then(panelToViewport);
            var zeroPivot = zeroTarget.Map(new Point(100, 50));
            Check(SceneTransformEditing.TryBeginScaleDrag(zero, width, height, ResizeCorner.BottomRight, zeroTarget, panelToViewport, zeroPivot, out var zeroScaleState),
                "TryBeginScaleDrag failed from zero scale.");
            SceneTransformEditing.UpdateScaleDrag(zeroScaleState, zero, new Point(zeroPivot.X + 150, zeroPivot.Y + 75), toggleLock: false);
            Check(Near(zero.ScaleX, 1.5, 1e-3) && Near(zero.ScaleY, 1.5, 1e-3),
                $"Scaling from 0 did not recover cleanly: ({zero.ScaleX}, {zero.ScaleY})");

            var cornerOrigin = new TransformFeatureSettings { OriginX = 0, OriginY = 0, ScaleX = 1, ScaleY = 1, IsScaleLocked = false };
            var coTarget = SceneTransformEditing.BuildSelfMatrix(cornerOrigin, width, height).Then(panelToViewport);
            var coTopLeft = coTarget.Map(new Point(0, 0));
            Check(SceneTransformEditing.TryBeginScaleDrag(cornerOrigin, width, height, ResizeCorner.TopLeft, coTarget, panelToViewport, coTopLeft, out var coState),
                "TryBeginScaleDrag failed when origin is on dragged corner.");
            SceneTransformEditing.UpdateScaleDrag(coState, cornerOrigin, new Point(coTopLeft.X + 40, coTopLeft.Y + 20), toggleLock: false);
            Check(cornerOrigin.ScaleX == 1 && cornerOrigin.ScaleY == 1,
                "Dragging corner on pivot caused division by zero or unexpected scaling.");
        });

        run("SceneTransformEditing maps resize deltas through invertible self linear transforms and falls back safely", () =>
        {
            var identity = SceneTransformEditing.MapResizeDelta(null, 12, -8);
            Check(identity == (12, -8), "Null inverse should preserve panel delta.");

            var rotated = new TransformFeatureSettings { Rotation = 90, ScaleX = 2, ScaleY = 2, FlipHorizontal = true };
            Check(SceneTransformEditing.TryGetLinearInverse(rotated, out var inv), "Invertible transform should invert.");
            // Linear maps local (10, 0) -> scale(-20, 0) -> rot90(0, -20). Inverse maps (0, -20) back to (10, 0).
            var mapped = SceneTransformEditing.MapResizeDelta(inv, 0, -20);
            Check(Near(mapped.DeltaX, 10) && Near(mapped.DeltaY, 0),
                $"Mapped resize delta expected (10, 0), got {mapped}.");

            var singular = new TransformFeatureSettings { ScaleX = 0, ScaleY = 1 };
            Check(!SceneTransformEditing.TryGetLinearInverse(singular, out _), "Singular transform unexpectedly inverted.");
            var fallback = SceneTransformEditing.MapResizeDelta(null, 15, 25);
            Check(fallback == (15, 25), "Singular fallback did not preserve panel delta.");
        });

        run("SceneEditorViewModel gates transform handles, scale focus mode, lock/hide, and TransformVisualChanged", () =>
        {
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(SceneSettings).Assembly);
            var path = ApplicationData.Current.LocalFolder.Path;
            using var appSettings = new SettingsService(NullLogger<SettingsService>.Instance, Path.Combine(path, "Settings.json"));
            appSettings.LoadSettingsAsync().GetAwaiter().GetResult();
            using var sceneService = new SceneService(appSettings, NullLogger<SceneService>.Instance, path);
            sceneService.Initialize();
            var blobs = new SceneBlobStorageService(sceneService);
            var componentServices = new ServiceCollection();
            componentServices.AddComponent<TextComponent>();
            componentServices.AddComponent<ClockComponent>();
            componentServices.AddComponent<DateComponent>();
            componentServices.AddComponent<ContainerComponent>();
            using var provider = componentServices.BuildServiceProvider();
            var registry = provider.GetRequiredService<ComponentRegistryService>();
            using var editor = new SceneEditorViewModel(sceneService, blobs, appSettings, registry);

            editor.Open(sceneService.ActiveSceneId);
            editor.ActiveTab = SceneEditorTab.Components;
            var layer = editor.Roots[0].Children[0];
            var group = (ComponentSettings)layer.Children[0].Model;
            var child = (ComponentSettings)layer.Children[0].Children[0].Model;
            var layoutInfo = child.Features.First(f => f.Id == "omls.features.layout.layout");

            editor.Select(child);
            editor.HasSelectionBounds = true;
            Check(!editor.CanEditTransform && !editor.ShowTransformHandles && !editor.IsScaleHandleMode && editor.ShowCornerHandles,
                "Component without Transform feature should not show transform handles.");

            var transformSettings = new TransformFeatureSettings();
            var transformInfo = new FeatureInfo { Id = SceneTransformEditing.TransformFeatureId, Settings = transformSettings };
            child.Features.Add(transformInfo);
            Check(editor.CanEditTransform && editor.ShowTransformHandles && !editor.IsScaleHandleMode && editor.GetTransform() == transformSettings,
                "Adding enabled Transform feature did not enable transform handles.");

            var visualNotifications = 0;
            editor.TransformVisualChanged += (_, _) => visualNotifications++;
            transformSettings.Rotation = 30;
            Check(visualNotifications == 1, "Mutating TransformFeatureSettings did not raise TransformVisualChanged.");

            editor.IsScaleFieldFocused = true;
            Check(editor.IsScaleHandleMode && editor.ShowCornerHandles,
                "Focusing scale field did not activate scale handle mode.");

            // Disabling layout still keeps corner handles visible while scale mode is active
            layoutInfo.IsEnabled = false;
            Check(!editor.CanResizeSelection && editor.ShowCornerHandles && editor.IsScaleHandleMode,
                "Corner handles disappeared during scale mode when layout feature was disabled.");

            // Disabling Transform turns off transform handles and scale handle mode
            transformInfo.IsEnabled = false;
            Check(!editor.CanEditTransform && !editor.ShowTransformHandles && !editor.IsScaleHandleMode && !editor.ShowCornerHandles,
                "Disabling Transform feature kept transform or scale handles visible.");

            transformInfo.IsEnabled = true;
            layoutInfo.IsEnabled = true;
            Check(editor.ShowTransformHandles && editor.IsScaleHandleMode,
                "Re-enabling Transform feature did not restore handles.");

            // Locking component or ancestor layer hides all transform & corner handles
            child.IsLocked = true;
            Check(!editor.CanEditTransform && !editor.ShowTransformHandles && !editor.IsScaleHandleMode && !editor.ShowCornerHandles,
                "Locking component did not hide handles.");
            child.IsLocked = false;
            ((LayerSettings)layer.Model).IsLocked = true;
            Check(!editor.CanEditTransform && !editor.ShowTransformHandles && !editor.ShowCornerHandles,
                "Locking ancestor layer did not hide handles.");
            ((LayerSettings)layer.Model).IsLocked = false;

            // Layer with Transform does not expose component canvas handles
            ((LayerSettings)layer.Model).Features.Add(new FeatureInfo
            {
                Id = SceneTransformEditing.TransformFeatureId,
                Settings = new TransformFeatureSettings()
            });
            editor.Select(layer.Model);
            editor.HasSelectionBounds = true;
            Check(!editor.CanEditTransform && !editor.ShowTransformHandles && !editor.IsScaleFieldFocused,
                "Selecting layer retained scale focus or exposed transform handles.");

            // Switching selection back resets scale focus
            editor.Select(child);
            editor.IsScaleFieldFocused = true;
            editor.Select(group);
            Check(!editor.IsScaleFieldFocused, "Switching component selection did not reset IsScaleFieldFocused.");
        });
    }

    private static bool Near(double a, double b, double tolerance = 1e-4)
        => Math.Abs(a - b) <= tolerance;

    private static bool Near(Point a, Point b, double tolerance = 1e-4)
        => Near(a.X, b.X, tolerance) && Near(a.Y, b.Y, tolerance);

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
