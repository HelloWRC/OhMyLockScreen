using Microsoft.Extensions.Logging.Abstractions;
using OhMyLockScreen;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Models.UI;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Services.Scene;
using OhMyLockScreen.ViewModels;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using OhMyLockScreen.Shared.Helpers.UI;

internal static class SceneTests
{
    internal static async Task RunAsync(Action<string, Action> run, Func<string, Func<Task>, Task> runAsync)
    {
        await runAsync("Dropped images retain pixel dimensions, persistent brushes and their original parent", async () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var parent = editor.Roots[0].Children[0].Children[0].Model;
            var original = Path.Combine(ApplicationData.Current.LocalFolder.Path, "original.PNG");
            File.WriteAllBytes(original, [1, 2, 3]);
            var sceneId = editor.Scene!.SceneId;
            var blobs = new SceneBlobStorageService(fixture.Service);
            string blobId;
            using (var stream = File.OpenRead(original))
            {
                blobId = await blobs.ImportAsync(sceneId, stream);
            }
            File.Delete(original);
            var first = editor.AddImageComponent("First", blobId, 1920, 1080, parent, 0);
            SceneLayoutEditing.ApplyDropPosition(first!, new Canvas(), new Point(123.6, 45.2));
            var second = editor.AddImageComponent("Second", blobId, 640, 480, parent, 1)!;
            SceneLayoutEditing.ApplyDropPosition(second, new Canvas(), new Point(123.6, 45.2));
            Check(first is { Settings: ContainerComponentSettings { Children.Count: 0 } },
                "The image did not create an empty layout container.");
            var children = ((ContainerComponentSettings)((ComponentSettings)parent).Settings!).Children;
            Check(ReferenceEquals(children[0], first) && ReferenceEquals(children[1], second)
                && ReferenceEquals(editor.SelectedModel, second), "Images were reordered, nested or not selected.");
            var layout = first!.Features.Single(feature => feature.Settings is LayoutFeatureSettings).GetSettings<LayoutFeatureSettings>();
            Check(layout is { Width: 1920, Height: 1080, IsAutoWidth: false, IsAutoHeight: false,
                HorizontalAlignment: HorizontalAlignment.Left, VerticalAlignment: VerticalAlignment.Top }
                && layout.Margin == new Thickness(124, 45, 0, 0)
                && SceneLayoutEditing.GetLayout(second, create: false)!.Margin == layout.Margin,
                "Image dimensions or alignment did not preserve the original pixel size.");
            var brush = (ImageBrush)first.Features.Single(feature => feature.Settings is BackgroundFeatureSettings)
                .GetSettings<BackgroundFeatureSettings>().Background!;
            var blobPath = Path.Combine(fixture.Service.GetSceneDirectory(sceneId), "Blobs", blobId[..2], blobId);
            Check(brush.Stretch == Windows.UI.Xaml.Media.Stretch.Uniform && brush.ImageSource == null
                && SceneImageBrush.GetBlobId(brush) == blobId
                && File.ReadAllBytes(blobPath).SequenceEqual(new byte[] { 1, 2, 3 }),
                "The brush was cropped or still depended on the original file.");
            fixture.Service.FlushPending();
            var restored = (ContainerComponentSettings)fixture.Service.Load(editor.Scene!.SceneId).Layers[0].Children[0].Settings!;
            var restoredImage = restored.Children[0];
            Check(restoredImage.Name == "First"
                && restoredImage.Features.Single(feature => feature.Settings is LayoutFeatureSettings).GetSettings<LayoutFeatureSettings>().Width == 1920
                && SceneLayoutEditing.GetLayout(restoredImage, create: false)!.Margin == layout.Margin
                && SceneImageBrush.GetBlobId((ImageBrush)restoredImage.Features.Single(feature => feature.Settings is BackgroundFeatureSettings)
                    .GetSettings<BackgroundFeatureSettings>().Background!) == blobId,
                "Image layout or persistent brush was lost after reloading the scene.");
        });
        run("Removed image targets reject insertion and empty scenes create a layer only on insertion", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var scene = editor.Scene!;
            var parent = scene.Layers[0];
            scene.Layers.Remove(parent);
            var blobId = new string('a', 64);
            Check(editor.AddImageComponent("Removed", blobId, 10, 20, parent, 0) == null, "A stale parent accepted an image.");
            editor.IsPreview = true;
            Check(editor.AddImageComponent("Preview", blobId, 10, 20, scene, 0) == null && scene.Layers.Count == 0,
                "Preview inserted an image or created a layer.");
            editor.IsPreview = false;
            var image = editor.AddImageComponent("Image", blobId, 10, 20, scene, 0);
            Check(scene.Layers.Count == 1 && ReferenceEquals(scene.Layers[0].Children.Single(), image),
                "An empty scene did not receive an image layer.");
            editor.Open(scene.SceneId);
            Check(editor.AddImageComponent("Stale scene", blobId, 10, 20, scene, 0) == null,
                "An image was inserted into a superseded scene instance.");
        });
        run("Free-layout drops replace alignment and margins without changing size or automatic switches", () =>
        {
            foreach (Panel panel in new Panel[] { new Grid(), new Canvas() })
            {
                var component = new ComponentSettings();
                var layout = SceneLayoutEditing.GetLayout(component, create: true)!;
                layout.Width = 123.45;
                layout.Height = 67.89;
                layout.IsAutoWidth = false;
                layout.IsAutoHeight = true;
                layout.IsAutoAlignment = false;
                layout.HorizontalAlignment = HorizontalAlignment.Stretch;
                layout.VerticalAlignment = VerticalAlignment.Bottom;
                layout.Margin = new Thickness(4, 5, 6, 7);

                SceneLayoutEditing.ApplyDropPosition(component, panel, new Point(234.5, -12.5));

                Check(layout.Margin == new Thickness(235, -13, 0, 0)
                    && layout.HorizontalAlignment == HorizontalAlignment.Left && layout.VerticalAlignment == VerticalAlignment.Top,
                    "The drop retained old margins, clamped its position or failed to align its top-left corner.");
                Check(layout is { Width: 123.45, Height: 67.89, IsAutoWidth: false, IsAutoHeight: true, IsAutoAlignment: false }
                    && component.Features.Count == 1 && ReferenceEquals(layout, SceneLayoutEditing.GetLayout(component, create: false)),
                    "A drop replaced existing settings or changed size and automatic switches.");
            }

            var withoutLayout = new ComponentSettings();
            Check(SceneLayoutEditing.GetLayout(withoutLayout, create: false) == null && withoutLayout.Features.Count == 0,
                "Reading a missing layout created a feature.");
            SceneLayoutEditing.ApplyDropPosition(withoutLayout, new Canvas(), new Point(-0.1, 8.6));
            var created = SceneLayoutEditing.GetLayout(withoutLayout, create: false)!;
            Check(created.Margin == new Thickness(0, 9, 0, 0)
                && BitConverter.DoubleToInt64Bits(created.Margin.Left) == 0
                && created.IsAutoWidth && created.IsAutoHeight && created.IsAutoAlignment,
                "A drop without a layout failed to create positioning settings with automatic sizing.");
        });
        run("Measured Grid drops use movement alignment and convert margins without resizing", () =>
        {
            var panel = new Grid { ActualWidth = 1000, ActualHeight = 800 };
            var component = new ComponentSettings();
            var layout = SceneLayoutEditing.GetLayout(component, create: true)!;
            layout.Width = 123.45;
            layout.Height = 67.89;
            layout.IsAutoWidth = true;
            layout.IsAutoHeight = false;
            (Point Point, HorizontalAlignment Horizontal, VerticalAlignment Vertical, Thickness Margin)[] cases =
            [
                (new Point(24, 32), HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(24, 32, 0, 0)),
                (new Point(400, 340), HorizontalAlignment.Center, VerticalAlignment.Center, new Thickness()),
                (new Point(760, 660), HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, 40, 20))
            ];
            foreach (var test in cases)
            {
                SceneLayoutEditing.ApplyDropPosition(component, panel, test.Point);
                SceneLayoutEditing.AlignDropPosition(component, panel, new Rect(test.Point.X, test.Point.Y, 200, 120));
                Check(layout.HorizontalAlignment == test.Horizontal && layout.VerticalAlignment == test.Vertical
                    && layout.Margin == test.Margin, "Drop alignment or converted margins differ from movement alignment.");
                Check(layout is { Width: 123.45, Height: 67.89, IsAutoWidth: true, IsAutoHeight: false, IsAutoAlignment: true },
                    "Drop alignment changed stored dimensions or automatic switches.");
            }

            layout.IsAutoAlignment = false;
            SceneLayoutEditing.ApplyDropPosition(component, panel, new Point(760, 660));
            SceneLayoutEditing.AlignDropPosition(component, panel, new Rect(760, 660, 200, 120));
            Check(layout.HorizontalAlignment == HorizontalAlignment.Left && layout.VerticalAlignment == VerticalAlignment.Top
                && layout.Margin == new Thickness(760, 660, 0, 0), "Drop alignment ignored the automatic alignment switch.");
            layout.IsAutoAlignment = true;
            SceneLayoutEditing.AlignDropPosition(component, new Canvas(), new Rect(760, 660, 200, 120));
            Check(layout.Margin == new Thickness(760, 660, 0, 0) && layout.HorizontalAlignment == HorizontalAlignment.Left,
                "Canvas acquired Grid alignment behavior.");
        });
        run("Dropped images save converted right and bottom margins with original dimensions", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var parent = editor.Scene!.Layers[0];
            var panel = new Grid { ActualWidth = 1000, ActualHeight = 800 };
            var image = editor.AddImageComponent("Positioned", new string('a', 64),
                200, 120, parent, 0)!;
            SceneLayoutEditing.ApplyDropPosition(image, panel, new Point(760, 660));
            SceneLayoutEditing.AlignDropPosition(image, panel, new Rect(760, 660, 200, 120));
            fixture.Service.FlushPending();
            var restored = fixture.Service.Load(editor.Scene.SceneId).Layers[0].Children[0];
            var layout = SceneLayoutEditing.GetLayout(restored, create: false)!;
            Check(layout is { Width: 200, Height: 120, IsAutoWidth: false, IsAutoHeight: false,
                HorizontalAlignment: HorizontalAlignment.Right, VerticalAlignment: VerticalAlignment.Bottom }
                && layout.Margin == new Thickness(0, 0, 40, 20),
                "An inserted image lost its dimensions or converted margins after saving.");
        });
        run("Grid drops preserve cells and spans while using the cell origin and spacing", () =>
        {
            var panel = new Grid { ActualWidth = 700, ActualHeight = 500, ColumnSpacing = 12, RowSpacing = 8 };
            panel.ColumnDefinitions.AddRange([
                new ColumnDefinition { ActualWidth = 100 },
                new ColumnDefinition { ActualWidth = 200 },
                new ColumnDefinition { ActualWidth = 376 }
            ]);
            panel.RowDefinitions.AddRange([
                new RowDefinition { ActualHeight = 60 },
                new RowDefinition { ActualHeight = 180 },
                new RowDefinition { ActualHeight = 244 }
            ]);
            var grid = new GridLayoutFeatureSettings { Row = 1, Column = 1, RowSpan = 2, ColumnSpan = 2 };
            var component = new ComponentSettings
            {
                Features = [new FeatureInfo { Id = "omls.features.layout.grid", Settings = grid }]
            };
            var area = SceneLayoutEditing.GetLayoutArea(panel, grid.Row, grid.Column, grid.RowSpan, grid.ColumnSpan);
            Check(area == new Rect(112, 68, 588, 432), "The grid cell area ignored definitions, spans or spacing.");
            SceneLayoutEditing.ApplyDropPosition(component, panel, new Point(150.6, 30.4));
            Check(SceneLayoutEditing.GetLayout(component, create: false)!.Margin == new Thickness(39, -38, 0, 0)
                && grid is { Row: 1, Column: 1, RowSpan: 2, ColumnSpan: 2 } && component.Features.Count == 2,
                "The grid drop used panel coordinates as cell margins or changed the assigned cells.");
            SceneLayoutEditing.ApplyDropPosition(component, panel, new Point(650, 440));
            SceneLayoutEditing.AlignDropPosition(component, panel, new Rect(650, 440, 40, 30));
            var aligned = SceneLayoutEditing.GetLayout(component, create: false)!;
            Check(aligned.HorizontalAlignment == HorizontalAlignment.Right && aligned.VerticalAlignment == VerticalAlignment.Bottom
                && aligned.Margin == new Thickness(0, 0, 10, 30)
                && grid is { Row: 1, Column: 1, RowSpan: 2, ColumnSpan: 2 },
                "Grid drop alignment ignored the cell extent or rewrote its row and column span.");
            Check(SceneLayoutEditing.GetLayoutArea(panel, 99, 99, 99, 99) == new Rect(324, 256, 376, 244)
                && SceneLayoutEditing.GetLayoutArea(new Grid { ActualWidth = 700, ActualHeight = 500 }, 99, 99, 2, 2)
                    == new Rect(0, 0, 700, 500), "Grid area fallback no longer matches native default and clamped cells.");
        });
        run("Flow-layout drops leave positioning features untouched", () =>
        {
            foreach (Panel panel in new Panel[] { new StackPanel(), new CommunityToolkit.WinUI.Controls.WrapPanel() })
            {
                var component = new ComponentSettings();
                SceneLayoutEditing.ApplyDropPosition(component, panel, new Point(300, 200));
                Check(component.Features.Count == 0, "A flow-layout drop added a positioning feature.");
                var layout = SceneLayoutEditing.GetLayout(component, create: true)!;
                layout.HorizontalAlignment = HorizontalAlignment.Right;
                layout.VerticalAlignment = VerticalAlignment.Bottom;
                layout.Margin = new Thickness(1, 2, 3, 4);
                SceneLayoutEditing.ApplyDropPosition(component, panel, new Point(400, 250));
                Check(layout.Margin == new Thickness(1, 2, 3, 4)
                    && layout.HorizontalAlignment == HorizontalAlignment.Right && layout.VerticalAlignment == VerticalAlignment.Bottom,
                    "A flow-layout drop overwrote positioning settings.");
            }
        });
        run("Tree-to-canvas placement updates unchanged insertion slots and survives reparenting and reload", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var layer = editor.Roots[0].Children[0];
            var component = editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!, layer, 0)!;
            var item = editor.FindItem(component)!;
            editor.Select(layer.Model);
            Check(!editor.MoveItem(item, layer, 0), "An unchanged insertion slot unexpectedly reordered the tree.");
            SceneLayoutEditing.ApplyDropPosition(component, new Grid(), new Point(75.6, 39.4));
            Check(SceneLayoutEditing.GetLayout(component, create: false)!.Margin == new Thickness(76, 39, 0, 0)
                && ReferenceEquals(editor.SelectedModel, layer.Model), "Placement relied on selection or ignored the unchanged insertion slot.");
            editor.AddLayer();
            var target = editor.SelectedItem!;
            Check(editor.MoveItem(item, target, 0), "A valid cross-parent drop was rejected.");
            SceneLayoutEditing.ApplyDropPosition(component, new Canvas(), new Point(101.2, 202.8));
            Check(ReferenceEquals(editor.FindItem(component)!.Parent, target), "Placement changed the target parent.");
            fixture.Service.FlushPending();
            var restored = fixture.Service.Load(editor.Scene!.SceneId).Layers[1].Children.Single();
            Check(SceneLayoutEditing.GetLayout(restored, create: false)!.Margin == new Thickness(101, 203, 0, 0),
                "Reparented drop coordinates were not saved and restored.");
        });
        run("Editor additions preserve declared defaults and use centered layout only as a fallback", () =>
        {
            using var fixture = new Fixture(services =>
            {
                OhMyLockScreen.Shared.Extensions.Scene.ComponentRegistryExtensions
                    .AddComponent<ComponentDefaultFeaturesTests.DefaultFeaturesComponent>(services);
                OhMyLockScreen.Shared.Extensions.Scene.ComponentRegistryExtensions
                    .AddComponent<ComponentDefaultFeaturesTests.AppearanceDefaultsComponent>(services);
            });
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var layer = editor.Roots[0].Children[0];
            var group = layer.Children[0];
            editor.Select(layer.Model);
            editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!);
            var text = (ComponentSettings)editor.SelectedModel!;
            var centered = text.Features.Single(feature => feature.Settings is LayoutFeatureSettings).GetSettings<LayoutFeatureSettings>();
            Check(centered.HorizontalAlignment == HorizontalAlignment.Center
                && centered.VerticalAlignment == VerticalAlignment.Center,
                "An existing component lost the editor's centered layout.");

            var info = fixture.Registry.Find(ComponentDefaultFeaturesTests.DefaultFeaturesComponent.Id)!;
            editor.Select(layer.Model);
            editor.AddComponent(info);
            var clicked = (ComponentSettings)editor.SelectedModel!;
            var nested = editor.AddComponent(info, group, 0)!;
            foreach (var added in new[] { clicked, nested })
            {
                Check(added.Features.Select(feature => feature.Id).SequenceEqual(
                    ["omls.features.appearance.border", "omls.features.layout.layout"]),
                    "An editor addition lost declared order or duplicated the layout.");
                var layout = added.Features[1].GetSettings<LayoutFeatureSettings>();
                Check(layout.HorizontalAlignment == HorizontalAlignment.Right
                    && layout.VerticalAlignment == VerticalAlignment.Bottom
                    && layout.Margin == new Thickness(1, 2, 3, 4),
                    "The editor replaced a component's declared layout values.");
            }

            var appearance = editor.AddComponent(
                fixture.Registry.Find(ComponentDefaultFeaturesTests.AppearanceDefaultsComponent.Id)!, layer, 0)!;
            Check(appearance.Features.Select(feature => feature.Id).SequenceEqual(
                ["omls.features.appearance.foreground", "omls.features.layout.layout"])
                && appearance.Features[1].GetSettings<LayoutFeatureSettings>() is
                { HorizontalAlignment: HorizontalAlignment.Center, VerticalAlignment: VerticalAlignment.Center },
                "The editor lost appearance defaults or failed to supply the centered layout.");
        });
        run("Failed default feature creation does not insert a partial component or change selection", () =>
        {
            using var fixture = new Fixture(services =>
                OhMyLockScreen.Shared.Extensions.Scene.ComponentRegistryExtensions
                    .AddComponent<ComponentDefaultFeaturesTests.FailingDefaultsComponent>(services));
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var layer = editor.Roots[0].Children[0];
            editor.Select(layer.Model);
            var children = editor.Scene!.Layers[0].Children.ToArray();
            var structures = 0;
            editor.StructureChanged += (_, _) => structures++;
            Throws<InvalidOperationException>(() => editor.AddComponent(
                fixture.Registry.Find(ComponentDefaultFeaturesTests.FailingDefaultsComponent.Id)!));
            Check(editor.Scene.Layers[0].Children.SequenceEqual(children)
                && ReferenceEquals(editor.SelectedItem, layer) && structures == 0,
                "A provider failure inserted a partial component or changed the editor state.");
        });
        run("Edited and removed default features persist without being restored by reload or multiple views", () =>
        {
            using var fixture = new Fixture(services =>
                OhMyLockScreen.Shared.Extensions.Scene.ComponentRegistryExtensions
                    .AddComponent<ComponentDefaultFeaturesTests.DefaultFeaturesComponent>(services));
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var layer = editor.Roots[0].Children[0];
            var info = fixture.Registry.Find(ComponentDefaultFeaturesTests.DefaultFeaturesComponent.Id)!;
            var added = editor.AddComponent(info, layer, 0)!;
            added.Features[0].GetSettings<BorderFeatureSettings>().BorderThickness = new Thickness(7);
            added.Features.RemoveAt(1);
            fixture.Service.FlushPending();
            var sceneId = editor.Scene!.SceneId;
            editor.Open(sceneId);
            var restored = editor.Scene!.Layers[0].Children[0];
            Check(restored.Features.Count == 1
                && restored.Features[0].GetSettings<BorderFeatureSettings>().BorderThickness == new Thickness(7),
                "Reload lost edited defaults or restored a removed layout.");

            var services = new ServiceCollection();
            services.AddTransient<ComponentDefaultFeaturesTests.DefaultFeaturesComponent>();
            using var provider = services.BuildServiceProvider();
            var factory = new ComponentsService(fixture.Registry, provider, NullLogger<ComponentsService>.Instance);
            var providersCreated = ComponentDefaultFeaturesTests.DefaultFeaturesProvider.Created;
            var first = factory.CreateComponent(restored)!;
            var second = factory.CreateComponent(restored)!;
            Check(!ReferenceEquals(first, second)
                && ComponentDefaultFeaturesTests.DefaultFeaturesProvider.Created == providersCreated
                && restored.Features.Count == 1
                && restored.Features[0].GetSettings<BorderFeatureSettings>().BorderThickness == new Thickness(7),
                "Creating multiple component views regenerated defaults or changed saved features.");

            restored.Features.Clear();
            fixture.Service.FlushPending();
            var empty = fixture.Service.Load(sceneId).Layers[0].Children[0];
            _ = factory.CreateComponent(empty);
            Check(empty.Features.Count == 0
                && ComponentDefaultFeaturesTests.DefaultFeaturesProvider.Created == providersCreated,
                "An intentionally empty feature list was repopulated.");
        });
        run("New scenes use independent directories and default to the system background without video resources", () =>
        {
            using var fixture = new Fixture();
            var scene = fixture.Service.Load(fixture.Service.ActiveSceneId);
            var directory = fixture.Service.GetSceneDirectory(scene.SceneId);
            Check(File.Exists(Path.Combine(directory, "scene.json")), "Missing scene.json in the scene folder.");
            Check(scene.Background.Mode == BackgroundMode.None && scene.Background.BlobId == null && !Directory.Exists(Path.Combine(directory, "Blobs")), "The default scene configured or copied a video background.");
            var another = fixture.Service.Create("新的系统背景场景");
            Check(another.Background.Mode == BackgroundMode.None && another.Background.BlobId == null, "Creating a scene selected a default video.");
            Check(Directory.GetFiles(fixture.Service.ScenesPath, "*.json").Length == 0, "Scene configs escaped their individual folders.");
            Check(fixture.Settings.Settings.LastEditedSceneId == scene.SceneId, "The initial edited scene was not persisted.");
        });
        run("Nested components, features, layout margins and background settings round-trip", () =>
        {
            using var fixture = new Fixture();
            var scene = fixture.Service.Load(fixture.Service.ActiveSceneId);
            var layout = scene.Layers[0].Children[0].Features[0].GetSettings<LayoutFeatureSettings>();
            layout.Margin = new Thickness(11, 22, 33, 44);
            layout.IsAutoWidth = false;
            layout.Width = 250;
            var clock = ((ContainerComponentSettings)scene.Layers[0].Children[0].Settings!).Children[0].GetSettings<ClockComponentSettings>();
            clock.ShowSeconds = true;
            scene.Background.EnableSound = true;
            fixture.Service.QueueSave(scene);
            fixture.Service.FlushPending();
            var loaded = fixture.Service.Load(scene.SceneId);
            var restoredLayout = loaded.Layers[0].Children[0].Features[0].GetSettings<LayoutFeatureSettings>();
            Check(restoredLayout.Margin == layout.Margin && restoredLayout.Width == 250 && !restoredLayout.IsAutoWidth, "Layout changed after reload.");
            var restoredClock = ((ContainerComponentSettings)loaded.Layers[0].Children[0].Settings!).Children[0].GetSettings<ClockComponentSettings>();
            var restoredDate = ((ContainerComponentSettings)loaded.Layers[0].Children[0].Settings!).Children[1];
            Check(restoredClock.ShowSeconds && loaded.Background.EnableSound && restoredDate.Id == DateComponent.Id && restoredDate.Settings is DateComponentSettings, "Nested component/background settings were lost.");
            Check(File.ReadAllText(Path.Combine(fixture.Service.GetSceneDirectory(scene.SceneId), "scene.json")).Contains('\n'), "Scene JSON is not formatted.");
        });
        run("Background feature brush replacements and null automatically persist through the editor", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var scene = editor.Scene!;
            var background = new BackgroundFeatureSettings();
            scene.Features.Add(new FeatureInfo { Id = "omls.features.appearance.background", Settings = background });
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0.2, 0.3),
                EndPoint = new Windows.Foundation.Point(0.8, 0.9),
                GradientStops =
                {
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 12, 34, 56), Offset = 0 },
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 65, 43, 21), Offset = 1 }
                }
            };
            background.Background = gradient;
            fixture.Service.FlushPending();
            var restored = fixture.Service.Load(scene.SceneId).Features.Single().GetSettings<BackgroundFeatureSettings>();
            Check(restored.Background is LinearGradientBrush loaded && loaded.StartPoint == gradient.StartPoint
                && loaded.EndPoint == gradient.EndPoint && loaded.GradientStops.Count == 2
                && loaded.GradientStops[1].Color == gradient.GradientStops[1].Color,
                "Automatic saving lost the background feature or gradient values.");
            background.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 23, 45, 67));
            fixture.Service.FlushPending();
            Check(fixture.Service.Load(scene.SceneId).Features.Single().GetSettings<BackgroundFeatureSettings>().Background
                is SolidColorBrush { Color.A: 128, Color.R: 23 }, "Replacing a gradient did not persist the solid brush.");
            background.Background = null;
            fixture.Service.FlushPending();
            Check(fixture.Service.Load(scene.SceneId).Features.Single().GetSettings<BackgroundFeatureSettings>().Background == null,
                "Clearing a background did not persist null.");
        });
        run("Border feature geometry and brush replacements automatically persist through the editor", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var scene = editor.Scene!;
            var component = scene.Layers[0].Children[0];
            var border = new BorderFeatureSettings();
            component.Features.Add(new FeatureInfo { Id = "omls.features.appearance.border", Settings = border });
            using var values = new BorderSettingsViewModel(border);
            values.Attach();
            border.BorderThickness = new Thickness(1.25, 2.5, 3.75, 4);
            border.CornerRadius = new CornerRadius(5.25, 6.5, 7.75, 8);
            border.Padding = new Thickness(9.25, 10.5, 11.75, 12);
            values.RadiusBottomLeft = 13.5;
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0.2, 0.3),
                EndPoint = new Windows.Foundation.Point(0.8, 0.9),
                GradientStops =
                {
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 12, 34, 56), Offset = 0 },
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 65, 43, 21), Offset = 1 }
                }
            };
            border.BorderBrush = gradient;
            fixture.Service.FlushPending();
            BorderFeatureSettings Reload() => fixture.Service.Load(scene.SceneId).Layers[0].Children[0].Features
                .Single(feature => feature.Id == "omls.features.appearance.border").GetSettings<BorderFeatureSettings>();
            var restored = Reload();
            Check(restored.BorderThickness == border.BorderThickness && restored.CornerRadius == new CornerRadius(5.25, 6.5, 7.75, 13.5)
                && restored.Padding == border.Padding, "Automatic saving lost asymmetric border geometry or an individual corner edit.");
            Check(restored.BorderBrush is LinearGradientBrush loaded && loaded.StartPoint == gradient.StartPoint
                && loaded.EndPoint == gradient.EndPoint && loaded.GradientStops.Count == 2
                && loaded.GradientStops[1].Color == gradient.GradientStops[1].Color,
                "Automatic saving lost the border gradient.");
            border.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 23, 45, 67));
            fixture.Service.FlushPending();
            Check(Reload().BorderBrush is SolidColorBrush { Color.A: 128, Color.R: 23 },
                "Replacing the border gradient did not persist the solid brush.");
            border.BorderBrush = null;
            fixture.Service.FlushPending();
            Check(Reload().BorderBrush == null, "Clearing the border brush did not persist null.");
        });
        run("Foreground feature brush replacements and null automatically persist through the editor", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var scene = editor.Scene!;
            var foreground = new ForegroundFeatureSettings();
            scene.Features.Add(new FeatureInfo { Id = "omls.features.appearance.foreground", Settings = foreground });
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0.2, 0.3),
                EndPoint = new Windows.Foundation.Point(0.8, 0.9),
                GradientStops =
                {
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 12, 34, 56), Offset = 0 },
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 65, 43, 21), Offset = 1 }
                }
            };
            foreground.Foreground = gradient;
            fixture.Service.FlushPending();
            var restored = fixture.Service.Load(scene.SceneId).Features.Single().GetSettings<ForegroundFeatureSettings>();
            Check(restored.Foreground is LinearGradientBrush loaded && loaded.StartPoint == gradient.StartPoint
                && loaded.EndPoint == gradient.EndPoint && loaded.GradientStops.Count == 2
                && loaded.GradientStops[1].Color == gradient.GradientStops[1].Color,
                "Automatic saving lost the foreground feature or gradient values.");
            foreground.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 23, 45, 67));
            fixture.Service.FlushPending();
            Check(fixture.Service.Load(scene.SceneId).Features.Single().GetSettings<ForegroundFeatureSettings>().Foreground
                is SolidColorBrush { Color.A: 128, Color.R: 23 }, "Replacing a foreground gradient did not persist the solid brush.");
            foreground.Foreground = null;
            fixture.Service.FlushPending();
            Check(fixture.Service.Load(scene.SceneId).Features.Single().GetSettings<ForegroundFeatureSettings>().Foreground == null,
                "Clearing a foreground did not persist null.");
        });
        run("Reload discovers scene folders and skips corrupt folders without overwriting them", () =>
        {
            using var fixture = new Fixture();
            var imported = DefaultSceneFactory.Create("新加入的场景");
            var folder = Path.Combine(fixture.Service.ScenesPath, imported.SceneId.ToString("N"));
            Directory.CreateDirectory(folder);
            ConfigureFileHelper.SaveConfig(Path.Combine(folder, "scene.json"), imported, true);
            var invalid = Path.Combine(fixture.Service.ScenesPath, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(invalid);
            File.WriteAllText(Path.Combine(invalid, "scene.json"), "bad json");
            fixture.Service.Reload();
            Check(fixture.Service.GetScenes().Any(scene => scene.Id == imported.SceneId) && fixture.Service.LoadErrors.Count == 1, "Reload did not discover new scene folders or report the corrupt one.");
            Check(File.ReadAllText(Path.Combine(invalid, "scene.json")) == "bad json", "Reload overwrote a corrupt scene.");
        });
        run("Editing and switching scenes preserve the separately selected lock screen scene", () =>
        {
            using var fixture = new Fixture();
            var active = fixture.Service.ActiveSceneId;
            var other = fixture.Service.Create("另一个场景");
            using var editor = fixture.Editor();
            editor.Open(other.SceneId);
            editor.Scene!.Name = "已重命名";
            fixture.Service.FlushPending();
            Check(fixture.Service.ActiveSceneId == active && fixture.Settings.Settings.LastEditedSceneId == other.SceneId, "Editing changed the active lock screen scene.");
            fixture.Service.SetActive(other.SceneId);
            Check(fixture.Settings.Settings.ActiveSceneId == other.SceneId && fixture.Service.Load(other.SceneId).Name == "已重命名", "Explicit activation did not persist the selection.");
        });
        await runAsync("Duplicating a scene copies blobs and retains independent references", async () =>
        {
            using var fixture = new Fixture();
            var original = fixture.Service.Load(fixture.Service.ActiveSceneId);
            var blobs = new SceneBlobStorageService(fixture.Service);
            using var stream = new MemoryStream("example video"u8.ToArray());
            var blobId = await blobs.ImportAsync(original.SceneId, stream);
            original.Background.Mode = BackgroundMode.Video;
            original.Background.BlobId = blobId;
            original.Name = "原始场景";
            fixture.Service.QueueSave(original);
            var clone = fixture.Service.Duplicate(original.SceneId);
            var originalPath = Path.Combine(fixture.Service.GetSceneDirectory(original.SceneId), "Blobs", blobId[..2], blobId);
            var clonePath = Path.Combine(fixture.Service.GetSceneDirectory(clone.SceneId), "Blobs", blobId[..2], blobId);
            Check(clone.SceneId != original.SceneId && clone.Name == "原始场景 副本" && clone.Background.BlobId == blobId
                && File.ReadAllBytes(clonePath).SequenceEqual(File.ReadAllBytes(originalPath)), "Copy did not use the latest model and copy resources.");
            File.WriteAllText(clonePath, "changed clone resource");
            Check(File.ReadAllText(originalPath) == "example video", "Copies share a resource file.");
            fixture.Service.SetActive(clone.SceneId);
            fixture.Service.Delete(original.SceneId);
            Check(File.Exists(clonePath) && fixture.Service.ActiveSceneId == clone.SceneId, "Deleting the source damaged the copy.");
            Throws<InvalidOperationException>(() => fixture.Service.Delete(clone.SceneId));
        });
        run("The settings graph follows nested collections and detaches replaced and removed models", () =>
        {
            var root = DefaultSceneFactory.Create("测试");
            var notifications = 0;
            using var graph = new ObservableSettingsGraph(root, (_, _, _) => notifications++);
            var container = root.Layers[0].Children[0].GetSettings<ContainerComponentSettings>();
            var oldClock = container.Children[0].GetSettings<ClockComponentSettings>();
            oldClock.ShowSeconds = true;
            Check(notifications == 1, "Deep model edits were not observed once.");
            var replacement = new ClockComponentSettings();
            container.Children[0].Settings = replacement;
            var previous = notifications;
            oldClock.ShowSeconds = false;
            Check(notifications == previous, "Replaced settings remained subscribed.");
            replacement.FlashSeparator = true;
            Check(notifications == previous + 1, "Replacement settings were not subscribed.");
            var removed = container.Children[0];
            container.Children.Remove(removed);
            previous = notifications;
            replacement.ShowSeconds = true;
            Check(notifications == previous, "Removed child settings remained subscribed.");
            graph.Dispose();
            root.Name = "after disposal";
            Check(notifications == previous, "Disposed settings graph still observes edits.");
        });
        run("Feature mutations preserve scene tree nodes expansion and the selected component", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var root = editor.Roots[0];
            var layer = root.Children[0];
            var group = layer.Children[0];
            var clock = group.Children[0];
            group.IsExpanded = false;
            editor.Select(clock.Model);
            var treeChanges = 0;
            var selections = 0;
            var structures = 0;
            var inspectorChanges = 0;
            editor.Roots.CollectionChanged += (_, _) => treeChanges++;
            root.Children.CollectionChanged += (_, _) => treeChanges++;
            layer.Children.CollectionChanged += (_, _) => treeChanges++;
            group.Children.CollectionChanged += (_, _) => treeChanges++;
            editor.SelectionChanged += (_, _) => selections++;
            editor.StructureChanged += (_, _) => structures++;
            editor.InspectorChanged += (_, _) => inspectorChanges++;
            var component = (ComponentSettings)clock.Model;
            var feature = new OhMyLockScreen.Shared.Models.FeatureHost.FeatureInfo
            {
                Id = "omls.features.layout.grid",
                Settings = new GridLayoutFeatureSettings()
            };
            component.Features.Add(feature);
            feature.GetSettings<GridLayoutFeatureSettings>().Row = 2;
            feature.Settings = new GridLayoutFeatureSettings
            {
                Row = 3
            };
            feature.Id = "unknown.feature";
            component.Features.Remove(feature);
            Check(treeChanges == 0 && structures == 0 && selections == 0 && inspectorChanges == 4, "Feature changes rebuilt the tree or refreshed the inspector for a scalar value change.");
            Check(ReferenceEquals(editor.Roots[0], root) && ReferenceEquals(root.Children[0], layer) && ReferenceEquals(layer.Children[0], group) && ReferenceEquals(group.Children[0], clock) && ReferenceEquals(editor.SelectedItem, clock) && !group.IsExpanded, "Feature changes replaced nodes, lost selection or reset expansion state.");
        });
        run("Resuming a cached editor restores scene observation without replacing its state", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            editor.ActiveTab = SceneEditorTab.Components;
            var scene = editor.Scene!;
            var group = editor.Roots[0].Children[0].Children[0];
            var clock = group.Children[0];
            group.IsExpanded = false;
            editor.Select(clock.Model);

            editor.Dispose();
            editor.ResumeSceneObservation();
            Check(ReferenceEquals(editor.Scene, scene) && ReferenceEquals(editor.SelectedItem, clock)
                && !group.IsExpanded && editor.ActiveTab == SceneEditorTab.Components,
                "Resuming replaced the scene, selection, expansion state, or active tab.");

            scene.Name = "返回后编辑";
            fixture.Service.FlushPending();
            Check(fixture.Service.Load(scene.SceneId).Name == scene.Name, "Resuming did not reconnect autosave.");
        });
        run("Hierarchy updates only reconcile the affected branch and retain existing node identities", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var root = editor.Roots[0];
            var layer = root.Children[0];
            var group = layer.Children[0];
            var rootChanges = 0;
            var resets = 0;
            editor.Roots.CollectionChanged += (_, _) => rootChanges++;
            layer.Children.CollectionChanged += (_, args) =>
            {
                if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                {
                    resets++;
                }
            };
            editor.Select(layer.Model);
            editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!);
            var added = editor.SelectedItem!;
            editor.MoveSelection(-1);
            Check(ReferenceEquals(layer.Children[0], added) && ReferenceEquals(editor.SelectedItem, added), "Reordering replaced the selected node.");
            editor.DeleteSelection();
            Check(rootChanges == 0 && resets == 0 && ReferenceEquals(editor.Roots[0], root) && ReferenceEquals(root.Children[0], layer) && ReferenceEquals(layer.Children[0], group) && ReferenceEquals(editor.SelectedItem, layer), "A local hierarchy change rebuilt unrelated branches.");
        });
        run("Tree reordering keeps add/remove-only native nodes synchronized through subsequent deletions", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var layer = editor.Roots[0].Children[0];
            var group = layer.Children[0];
            var added = editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!, layer, 1)!;
            var nativeNodes = layer.Children.ToList();
            layer.Children.CollectionChanged += (_, args) =>
            {
                // WinUI 2 TreeView handles Add/Remove/Replace/Reset, but ignores Move.
                if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
                {
                    nativeNodes.RemoveAt(args.OldStartingIndex);
                }
                else if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    nativeNodes.Insert(args.NewStartingIndex, (SceneTreeItem)args.NewItems![0]!);
                }
            };

            editor.MoveSelection(-1);
            Check(nativeNodes.SequenceEqual(layer.Children), "Native nodes retained their old order after moving up.");
            editor.MoveSelection(1);
            Check(nativeNodes.SequenceEqual(layer.Children), "Native nodes retained their old order after moving down.");
            editor.DeleteSelection();
            Check(nativeNodes.SequenceEqual(layer.Children) && nativeNodes.Count == 1
                && ReferenceEquals(nativeNodes[0], group) && editor.FindItem(added) == null
                && ReferenceEquals(editor.SelectedItem, group), "Deleting after reordering removed the wrong native node.");
            editor.DeleteSelection();
            Check(nativeNodes.Count == 0 && editor.FindItem(group.Model) == null
                && ReferenceEquals(editor.SelectedItem, layer), "Deleting the remaining container left stale native descendants.");
        });
        run("Tree reconciliation ignores transient selection callbacks and reapplies unchanged selections", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var group = editor.Roots[0].Children[0].Children[0];
            var selected = group.Children[1];
            var other = group.Children[0];
            editor.Select(selected.Model);
            SceneTreeItem? nativeSelection = selected;
            editor.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SceneEditorViewModel.SelectedItem))
                {
                    nativeSelection = editor.SelectedItem;
                }
            };
            group.Children.CollectionChanged += (_, args) =>
            {
                if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove
                    && args.OldItems!.Contains(nativeSelection))
                {
                    nativeSelection = null;
                    editor.Select(other.Model);
                }
            };

            editor.MoveSelection(-1);
            Check(ReferenceEquals(editor.SelectedItem, selected) && ReferenceEquals(nativeSelection, selected),
                "A transient native selection callback replaced the intended selection or the binding was not refreshed.");
            editor.DeleteSelection();
            Check(ReferenceEquals(editor.SelectedItem, group) && ReferenceEquals(nativeSelection, group)
                && group.Children.Count == 1 && ReferenceEquals(group.Children[0], other)
                && editor.FindItem(selected.Model) == null,
                "Deletion used the transient selection or left the deleted component in the tree.");
        });
        run("Tree keyboard reordering commits models, preserves focus and saves the displayed order", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var page = new SceneEditorPage();
            page.LoadTree(editor);
            var group = editor.Roots[0].Children[0].Children[0];
            var item = group.Children[1];
            var children = ((ContainerComponentSettings)((ComponentSettings)group.Model).Settings!).Children;
            editor.Select(group.Model);
            Check(page.PressTreeKey(item, Windows.System.VirtualKey.Up)
                && ReferenceEquals(group.Children[0], item) && ReferenceEquals(children[0], item.Model)
                && ReferenceEquals(editor.SelectedItem, item)
                && ((Windows.UI.Xaml.Controls.Control)page.SceneTree.ContainerFromItem(item)).HasKeyboardFocus,
                "Keyboard reordering bypassed the model, used selection instead of focus or lost keyboard focus.");
            Check(page.PressTreeKey(item, Windows.System.VirtualKey.Down)
                && ReferenceEquals(group.Children[1], item) && ReferenceEquals(children[1], item.Model),
                "The down shortcut did not commit the displayed order.");
            Check(page.PressTreeKey(item, Windows.System.VirtualKey.Left)
                && ReferenceEquals(group.Children[0], item), "The left shortcut did not move up.");
            Check(page.PressTreeKey(item, Windows.System.VirtualKey.Right)
                && ReferenceEquals(group.Children[1], item), "The right shortcut did not move down.");
            Check(page.PressTreeKey(item, Windows.System.VirtualKey.Down)
                && ReferenceEquals(group.Children[1], item), "A boundary shortcut reached native reordering.");
            Check(!page.PressTreeKey(item, Windows.System.VirtualKey.Up, alt: false)
                && !page.PressTreeKey(item, Windows.System.VirtualKey.Up, shift: false)
                && !page.PressTreeKey(item, Windows.System.VirtualKey.Up, control: true)
                && ReferenceEquals(group.Children[1], item), "Ordinary navigation was intercepted as reordering.");
            editor.IsPreview = true;
            Check(page.PressTreeKey(item, Windows.System.VirtualKey.Up)
                && ReferenceEquals(group.Children[1], item), "Preview allowed keyboard editing.");
            editor.IsPreview = false;
            page.PressTreeKey(item, Windows.System.VirtualKey.Up);
            fixture.Service.FlushPending();
            var saved = fixture.Service.Load(editor.Scene!.SceneId).Layers[0].Children[0].GetSettings<ContainerComponentSettings>();
            Check(saved.Children[0].Id == ((ComponentSettings)item.Model).Id, "Keyboard order was not saved.");
            editor.DeleteSelection();
            Check(editor.FindItem(item.Model) == null && group.Children.Count == 1 && children.Count == 1,
                "Deletion after keyboard reordering left a stale node or removed the wrong model.");
        });
        run("Delayed tree actions reject superseded scenes and preview transitions", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var page = new SceneEditorPage();
            page.LoadTree(editor);
            var calls = 0;
            page.QueueTreeAction(() => calls++);
            editor.Open(fixture.Service.ActiveSceneId);
            Window.Current.CoreWindow.Dispatcher.Drain();
            Check(calls == 0, "An action crossed a scene reload.");
            page.QueueTreeAction(() => calls++);
            editor.IsPreview = true;
            Window.Current.CoreWindow.Dispatcher.Drain();
            Check(calls == 0, "An action crossed a preview transition.");
            editor.IsPreview = false;
            page.QueueTreeAction(() => calls++);
            Window.Current.CoreWindow.Dispatcher.Drain();
            Check(calls == 1, "A valid deferred tree action was discarded.");
        });
        run("Removed tree targets and sources are rejected before any component leaves its scene", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var layer = editor.Roots[0].Children[0];
            var source = layer.Children[0];
            var targetModel = editor.AddComponent(fixture.Registry.Find(ContainerComponent.Id)!, layer, 1)!;
            var target = editor.SelectedItem!;
            var page = new SceneEditorPage();
            page.LoadTree(editor);
            var moveAccepted = true;
            ComponentSettings? added = targetModel;
            page.QueueTreeAction(() =>
            {
                moveAccepted = editor.MoveItem(source, target, 0);
                added = editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!, target, 0);
            });
            editor.DeleteSelection();
            editor.Select(source.Model);
            Window.Current.CoreWindow.Dispatcher.Drain();
            Check(!moveAccepted && added == null
                && ReferenceEquals(editor.FindItem(source.Model), source)
                && targetModel.GetSettings<ContainerComponentSettings>().Children.Count == 0
                && ReferenceEquals(editor.SelectedItem, source), "A removed destination accepted or swallowed a component.");
            var child = source.Children[0];
            editor.DeleteSelection();
            Check(!editor.MoveItem(child, layer, 0) && layer.Children.Count == 0,
                "A child of a removed source container was resurrected.");
            editor.Select(layer.Model);
            editor.DeleteSelection();
            Check(editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!, layer, 0) == null,
                "A removed layer accepted a component.");
            editor.Open(fixture.Service.ActiveSceneId);
            Check(!editor.MoveItem(source, editor.Roots[0], 0)
                && editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!, target, 0) == null,
                "Models from a previous scene instance were accepted.");
        });
        run("Tree editing validates the model hierarchy while native drag nodes and rebuilt wrappers change", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var layer = editor.Roots[0].Children[0];
            var group = layer.Children[0];
            var source = group.Children[0];
            editor.AddLayer();
            var target = editor.SelectedItem!;
            // Native drag moves the UI nodes before DragItemsCompleted commits the models.
            group.Children.Remove(source);
            target.Children.Add(source);
            Check(editor.MoveItem(source, target, 0), "Optimistically moved native nodes invalidated a live model move.");
            editor.RebuildTree();
            Check(ReferenceEquals(editor.FindItem(source.Model)!.Parent!.Model, target.Model),
                "Rebuilding did not reflect the committed parent.");
            Check(editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!, layer, 0) != null,
                "A rebuilt wrapper invalidated a still-live parent model.");
            var current = editor.FindItem(source.Model)!;
            editor.IsPreview = true;
            Check(!editor.MoveItem(current, layer, 0)
                && editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!, layer, 0) == null
                && ReferenceEquals(editor.FindItem(source.Model), current), "Preview accepted tree editing.");
            editor.IsPreview = false;
            var oldParent = current.Parent!;
            editor.Open(fixture.Service.ActiveSceneId);
            Check(!editor.MoveItem(current, editor.Roots[0].Children[0], 0)
                && editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!, oldParent, 0) == null
                && editor.FindItem(current.Model) == null,
                "A live source or parent from the previous scene instance was accepted after reloading.");
        });
        run("Cross-parent moves preserve mixed subtree expansion and descendant selection through rebuilds", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var layer = editor.Roots[0].Children[0];
            var group = layer.Children[0];
            var nestedModel = editor.AddComponent(fixture.Registry.Find(ContainerComponent.Id)!, group, 2)!;
            var nested = editor.FindItem(nestedModel)!;
            var child = editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!, nested, 0)!;
            group.IsExpanded = false;
            group.Children[0].IsExpanded = false;
            group.Children[1].IsExpanded = true;
            nested.IsExpanded = false;
            editor.AddLayer();
            var target = editor.SelectedItem!;
            var states = editor.GetAllItems().ToDictionary(item => item.Model, item => item.IsExpanded, ReferenceEqualityComparer.Instance);
            editor.Select(child);
            Check(editor.MoveItem(group, target, 0) && ReferenceEquals(editor.SelectedModel, child),
                "Moving a subtree lost the selected descendant.");
            foreach (var state in states)
            {
                Check(editor.FindItem(state.Key)!.IsExpanded == state.Value, "Moving reset a subtree expansion state.");
            }

            editor.RebuildTree();
            Check(ReferenceEquals(editor.SelectedModel, child), "Rebuilding lost the moved descendant selection.");
            foreach (var state in states)
            {
                Check(editor.FindItem(state.Key)!.IsExpanded == state.Value, "Rebuilding lost a preserved expansion state.");
            }
        });
        run("Tree dragging reorders in place and moves components across layers without replacing their settings", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var root = editor.Roots[0];
            var layer = root.Children[0];
            var group = layer.Children[0];
            var clock = group.Children[0];
            editor.Select(clock.Model);
            Check(editor.MoveItem(clock, group, 1), "Same-parent dragging did not reorder the component.");
            Check(ReferenceEquals(group.Children[1], clock) && ReferenceEquals(editor.SelectedItem, clock)
                && ReferenceEquals(editor.FindItem(clock.Model), clock), "Same-parent dragging replaced the tree node or lost selection.");
            Check(editor.MoveItem(clock, group, 0) && ReferenceEquals(editor.FindItem(clock.Model), clock),
                "A second same-parent drag replaced the node or rejected the move.");

            var component = (ComponentSettings)clock.Model;
            var settings = component.Settings;
            editor.AddLayer();
            var targetLayer = editor.SelectedItem!;
            editor.Select(component);
            Check(editor.MoveItem(clock, targetLayer, 0), "Cross-layer dragging did not move the component.");
            var moved = editor.FindItem(component);
            Check(ReferenceEquals(targetLayer.Children[0].Model, component)
                && ReferenceEquals(moved, editor.SelectedItem)
                && ReferenceEquals(component.Settings, settings), "Cross-layer dragging replaced the component, its settings or selection.");
            editor.Select(targetLayer.Model);
            Check(editor.MoveItem(targetLayer, root, 0)
                && ReferenceEquals(editor.Scene!.Layers[0], targetLayer.Model)
                && ReferenceEquals(root.Children[0], targetLayer), "Dragging a layer did not reorder it within the scene.");

            fixture.Service.FlushPending();
            var saved = fixture.Service.Load(editor.Scene!.SceneId);
            Check(saved.Layers[0].Children[0].Id == component.Id, "The moved component was not persisted in its new layer.");
        });
        run("Tree container edges reorder siblings while the center accepts children", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var layer = editor.Roots[0].Children[0];
            var group = layer.Children[0];
            var component = editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!, layer, layer.Children.Count)!;
            var source = editor.FindItem(component)!;
            var page = new SceneEditorPage();
            page.LoadTree(editor);

            var before = page.ResolveTreeDrop(source, group, 0.1);
            Check(before is { } first && ReferenceEquals(first.Parent, layer) && first.Index == 0,
                "The top edge of a container was resolved as a child drop.");
            var inside = page.ResolveTreeDrop(source, group, 0.5);
            Check(inside is { } middle && ReferenceEquals(middle.Parent, group) && middle.Index == group.Children.Count,
                "The middle of a container did not accept a child drop.");

            Check(editor.MoveItem(source, before!.Value.Parent, before.Value.Index), "The sibling drop was rejected.");
            source = editor.FindItem(component)!;
            var after = page.ResolveTreeDrop(source, group, 0.9);
            Check(after is { } last && ReferenceEquals(last.Parent, layer) && last.Index == 1,
                "The bottom edge of a container was resolved as a child drop or used the wrong adjusted index.");
            var child = group.Children[0];
            var outOfContainer = page.ResolveTreeDrop(child, group, 0.1);
            Check(outOfContainer is { } outside && ReferenceEquals(outside.Parent, layer) && outside.Index == 1,
                "Dropping a child above its container did not move it to the container's sibling level.");
            Check(page.ResolveTreeDrop(source, group, 0.1) == null,
                "An unchanged sibling insertion slot unexpectedly became a child drop.");
        });
        run("Tree leaf halves reorder siblings and preserve container foreground", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var layer = editor.Roots[0].Children[0];
            var group = layer.Children[0];
            var groupModel = (ComponentSettings)group.Model;
            var children = ((IComponentContainerSettings)groupModel.Settings!).Children;
            var source = group.Children[0];
            var leaf = group.Children[1];
            var foregroundColor = Windows.UI.Color.FromArgb(255, 20, 40, 60);
            var foreground = new SolidColorBrush(foregroundColor);
            groupModel.Features.Add(new FeatureInfo
            {
                Id = "omls.features.appearance.foreground",
                Settings = new ForegroundFeatureSettings { Foreground = foreground }
            });
            var page = new SceneEditorPage();
            page.LoadTree(editor);

            Check(page.ResolveTreeDrop(source, leaf, 0.4) == null, "The leaf's upper half changed an unchanged sibling slot.");
            Check(page.ResolveTreeDrop(group, leaf, 0.1) == null, "A container could be moved into its own subtree.");

            var after = page.ResolveTreeDrop(source, leaf, 0.6);
            Check(after is { } destination && ReferenceEquals(destination.Parent, group)
                && editor.MoveItem(source, destination.Parent, destination.Index),
                "The leaf's lower half did not move its sibling.");
            Check(ReferenceEquals(children[1], source.Model) && ReferenceEquals(group.Children[1], source),
                "The drop did not update both the model and the tree.");
            Check(ReferenceEquals(groupModel.Features.Last().GetSettings<ForegroundFeatureSettings>().Foreground, foreground),
                "The reorder replaced the container's foreground brush.");
            Check(page.ResolveTreeDrop(source, leaf, 0.4) is { } before && ReferenceEquals(before.Parent, group)
                && before.Index == 0, "The leaf's upper half did not resolve to a sibling slot.");

            fixture.Service.FlushPending();
            var saved = fixture.Service.Load(editor.Scene!.SceneId).Layers[0].Children[0];
            var savedChildren = ((IComponentContainerSettings)saved.Settings!).Children;
            Check(savedChildren[1].Id == ((ComponentSettings)source.Model).Id
                && saved.Features.Last().GetSettings<ForegroundFeatureSettings>().Foreground is SolidColorBrush brush
                && brush.Color == foregroundColor,
                "The saved scene lost the sibling order or the foreground brush.");
        });
        run("Moving into and within a container keeps the component under that container", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var layer = editor.Roots[0].Children[0];
            var group = layer.Children[0];
            editor.AddLayer();
            var sourceLayer = editor.SelectedItem!;
            var component = editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!, sourceLayer, 0)!;
            var item = editor.FindItem(component)!;

            Check(editor.MoveItem(item, group, group.Children.Count), "Dragging into a container was rejected.");
            var moved = editor.FindItem(component)!;
            Check(ReferenceEquals(moved.Parent, group) && ReferenceEquals(group.Children[^1].Model, component), "Dragging into a container did not attach the component to it.");

            Check(editor.MoveItem(moved, group, 0), "Reordering inside a container was rejected.");
            var reordered = editor.FindItem(component)!;
            Check(ReferenceEquals(reordered.Parent, group) && ReferenceEquals(group.Children[0].Model, component), "Reordering inside a container moved the component out.");
        });
        run("Hierarchy editing rejects scene roots, cycles and incompatible parents", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var root = editor.Roots[0];
            var layer = root.Children[0];
            var group = layer.Children[0];
            var child = group.Children[0];
            editor.AddLayer();
            var secondLayer = editor.SelectedItem!;

            Check(!editor.MoveItem(root, secondLayer, 0), "The scene root was movable.");
            Check(!editor.MoveItem(secondLayer, group, 0), "A layer was accepted by a component container.");
            Check(!editor.MoveItem(child, root, 0), "A component was accepted directly by the scene.");
            Check(!editor.MoveItem(group, child, 0), "A container was allowed to become its descendant's child.");
            Check(ReferenceEquals(editor.Roots[0], root) && ReferenceEquals(root.Children[0], layer) && ReferenceEquals(group.Children[0], child), "Rejected moves changed the hierarchy.");
        });
        run("Canvas drag scope follows the moving component and falls back to the selected layer", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var root = editor.Roots[0];
            var firstLayer = root.Children[0];
            var nested = firstLayer.Children[0].Children[0];
            editor.AddLayer();
            var secondLayer = editor.SelectedItem!;
            var secondComponent = editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!, secondLayer, 0)!;

            Check(ReferenceEquals(editor.GetCanvasLayer(nested.Model), firstLayer), "Canvas scope did not follow the moving nested component.");
            Check(ReferenceEquals(editor.GetCanvasLayer(secondComponent), secondLayer), "Canvas scope did not follow the moving component's layer.");
            Check(ReferenceEquals(editor.GetCanvasLayer(new object()), secondLayer), "Canvas scope did not fall back to the selected layer.");
            Check(SceneEditorViewModel.IsInCanvasLayer(nested, firstLayer) && !SceneEditorViewModel.IsInCanvasLayer(nested, secondLayer), "Canvas scope included a component from another layer.");
            editor.Select(root.Model);
            Check(ReferenceEquals(editor.GetCanvasLayer(null), firstLayer), "Canvas scope did not fall back to the first layer for a scene selection.");
        });
        run("Deleting selects the previous sibling and falls back to the parent for the first item", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var group = editor.Roots[0].Children[0].Children[0];
            var first = group.Children[0];
            var second = group.Children[1];

            editor.Select(second.Model);
            editor.DeleteSelection();
            Check(ReferenceEquals(editor.SelectedItem, first) && group.Children.Count == 1, "Deleting did not select the previous sibling.");

            editor.Select(group.Children[0].Model);
            editor.DeleteSelection();
            Check(ReferenceEquals(editor.SelectedItem, group) && group.Children.Count == 0, "Deleting the first sibling did not fall back to its parent.");
        });
        run("Explicit component insertion uses the requested index and deletion protects the scene root", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var root = editor.Roots[0];
            var layer = root.Children[0];
            var added = editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!, layer, 0);
            Check(added != null && ReferenceEquals(layer.Children[0].Model, added) && ReferenceEquals(editor.SelectedModel, added), "Dragging a library item did not insert at the requested index.");
            var group = layer.Children[1];
            var nested = editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!, group, 0);
            Check(nested != null && ReferenceEquals(group.Children[0].Model, nested), "A library item could not be inserted into a component container.");
            Check(editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!, root, 0) == null, "A component was added directly to the scene.");
            editor.Select(root.Model);
            editor.DeleteSelection();
            Check(ReferenceEquals(editor.Roots[0], root) && editor.Scene!.Layers.Count == 1, "Deleting the scene root changed the scene.");
        });
        run("Drag axis and insertion helpers cover ordered, two-dimensional and empty targets", () =>
        {
            Check(SceneLayoutEditing.ReordersChildrenOnCanvasDrop(new StackPanel())
                && SceneLayoutEditing.ReordersChildrenOnCanvasDrop(new CommunityToolkit.WinUI.Controls.WrapPanel())
                && !SceneLayoutEditing.ReordersChildrenOnCanvasDrop(new Canvas())
                && !SceneLayoutEditing.ReordersChildrenOnCanvasDrop(new Grid()),
                "Canvas drop ordering did not distinguish flow layouts from z-ordered layouts.");

            var horizontal = SceneLayoutEditing.ConstrainToDominantAxis(10, 10, 30, 16);
            var vertical = SceneLayoutEditing.ConstrainToDominantAxis(10, 10, 14, 40);
            Check(horizontal == (30, 10) && vertical == (10, 40), "Shift did not lock movement to its dominant axis.");

            (double X, double Y, double Width, double Height)[] bounds =
            [
                (0, 0, 20, 20),
                (20, 0, 20, 20),
                (40, 0, 20, 20)
            ];
            Check(SceneLayoutEditing.GetInsertionIndex(bounds, 5, 10, true) == 0, "The first ordered slot was incorrect.");
            Check(SceneLayoutEditing.GetInsertionIndex(bounds, 31, 10, true) == 2, "A middle ordered slot was incorrect.");
            Check(SceneLayoutEditing.GetInsertionIndex(bounds, 70, 10, true) == 3, "The trailing ordered slot was incorrect.");
            Check(SceneLayoutEditing.GetInsertionIndex(bounds, 19, 10, null) == 1, "Two-dimensional insertion ignored the nearest item's leading edge.");
            Check(SceneLayoutEditing.GetInsertionIndex(bounds, 31, 10, null) == 2, "Two-dimensional insertion ignored the nearest item's trailing edge.");
            Check(SceneLayoutEditing.GetInsertionIndex([], 10, 10, null) == 0, "An empty target did not append at index zero.");
        });
        run("Wrapping insertion distinguishes rows and columns while preserving the flow direction", () =>
        {
            (double X, double Y, double Width, double Height)[] bounds =
            [
                (0, 0, 40, 30),
                (50, 0, 60, 30),
                (0, 40, 40, 30),
                (50, 40, 60, 30)
            ];
            foreach (var horizontal in new[] { true, false })
            {
                var orientedBounds = horizontal
                    ? bounds
                    : bounds.Select(bound => (bound.Y, bound.X, bound.Height, bound.Width)).ToArray();
                int Insert(double x, double y) => SceneLayoutEditing.GetInsertionIndex(
                    orientedBounds, horizontal ? x : y, horizontal ? y : x,
                    horizontal, wrap: true);

                Check(Insert(10, 15) == 0 && Insert(10, 55) == 2,
                    "Wrapping insertion ignored the row or column under the pointer.");
                Check(Insert(100, 15) == 2 && Insert(100, 55) == 4,
                    "Wrapping insertion could not append to the selected row or column.");
                Check(Insert(18, 68) == 2 && Insert(22, 42) == 3,
                    "Wrapping insertion used the cross-axis offset to choose an item's leading or trailing edge.");
                Check(Insert(45, 55) == 3, "Insertion in a gap between wrapped items chose another line.");
            }

            Check(SceneLayoutEditing.GetInsertionIndex([], 10, 10, true, wrap: true) == 0,
                "An empty wrapping target did not insert at zero.");
        });
        run("Selection changes expose the current model and invalidate the previous selection bounds", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            editor.ActiveTab = SceneEditorTab.Components;
            var layer = editor.Roots[0].Children[0];
            var group = layer.Children[0];
            editor.Select(layer.Model);
            editor.SelectionGeometry = new SceneSelectionGeometry(0, 0, 1200, 800);
            editor.HasSelectionBounds = true;
            object expected = group.Model;
            var notifications = 0;
            editor.SelectionChanged += (_, _) =>
            {
                Check(ReferenceEquals(editor.SelectedModel, expected) && !editor.HasSelectionBounds && !editor.ShowSelection, "Selection observers saw the previous object or its full-scene bounds.");
                notifications++;
            };
            editor.Select(group.Model);
            expected = group.Children[0].Model;
            editor.Select(expected);
            Check(notifications == 2 && editor.SelectedModel is ComponentSettings { Settings: ClockComponentSettings }, "Consecutive selections did not reach the current component.");
        });
        run("Nested selections expose their component depth for same-level canvas selection", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var root = editor.Roots[0];
            var layer = root.Children[0];
            var group = layer.Children[0];
            var child = group.Children[0];

            editor.Select(child.Model);
            Check(editor.PreferredNestedComponentDepth == 3, "A nested component did not expose its tree depth.");
            editor.AddComponent(fixture.Registry.Find(ContainerComponent.Id)!, group, 0);
            Check(editor.PreferredNestedComponentDepth == 3, "A nested container did not expose its tree depth.");
            editor.Select(group.Model);
            Check(editor.PreferredNestedComponentDepth == null, "An outermost container was treated as a nested component.");
            editor.Select(layer.Model);
            Check(editor.PreferredNestedComponentDepth == null, "A layer was treated as a nested component.");
        });
        run("Tab switches and selection refreshes never create default components or write the scene", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var root = editor.Roots[0];
            var layer = root.Children[0];
            var group = layer.Children[0];
            var originalChildren = group.Children.Select(child => child.Model).ToArray();
            var info = fixture.Registry.Find(TextComponent.Id)!;
            for (var index = 0; index < 30; index++)
            {
                editor.ActiveTab = (SceneEditorTab)(index % 3);
                editor.Select(layer.Model);
                editor.Select(group.Model);
                editor.Select(group.Children[0].Model);
                editor.RefreshScenes();
                Check(editor.AddComponentCommand.CanExecute(info) == editor.ShowComponents, "The component add action was enabled on a different tab.");
            }

            editor.IsPreview = true;
            Check(!editor.AddComponentCommand.CanExecute(info), "Preview could add a component.");
            editor.IsPreview = false;
            Check(editor.Scene!.Layers.Count == 1 && layer.Children.Count == 1 && group.Children.Select(child => child.Model).SequenceEqual(originalChildren) && !fixture.Service.HasPendingChanges(editor.Scene.SceneId), "Navigation or selection changed scene content.");
            editor.ActiveTab = SceneEditorTab.Components;
            editor.Select(layer.Model);
            editor.AddComponentCommand.Execute(info);
            Check(layer.Children.Count == 2 && layer.Children[1].Model is ComponentSettings { Id: TextComponent.Id }, "An explicit add action did not add exactly one component.");
        });
        run("XAML margin editing preserves the other sides and releases obsolete settings subscriptions", () =>
        {
            var model = new LayoutFeatureSettings
            {
                Margin = new Thickness(1, 2, 3, 4)
            };
            using var margins = new LayoutMarginsViewModel(model);
            margins.Attach();
            margins.Left = 12;
            Check(model.Margin == new Thickness(12, 2, 3, 4), "Editing one margin overwrote other sides.");
            var notifications = 0;
            margins.PropertyChanged += (_, _) => notifications++;
            model.Margin = new Thickness(5, 6, 7, 8);
            Check(margins.Left == 5 && margins.Bottom == 8 && notifications == 4, "Canvas changes did not update the declaratively bound margin fields.");
            margins.Dispose();
            model.Margin = default;
            Check(notifications == 4, "An unloaded property editor retained its previous model subscription.");
        });
        run("Layout panel exposes only the options supported by the selected layout kind", () =>
        {
            var model = new LayerSettings
            {
                Kind = LayoutKind.Canvas
            };
            using var panel = new LayoutPanelSettingsViewModel
            {
                Model = model
            };
            panel.Attach();
            Check(!panel.ShowOrientation && !panel.ShowSpacing && !panel.ShowGridDefinitions, "Canvas exposed unsupported layout options.");
            model.Kind = LayoutKind.StackPanel;
            Check(panel.ShowOrientation && panel.ShowSpacing && !panel.ShowGridDefinitions, "Stack layout exposed grid options or hid supported options.");
            panel.Orientation = Orientation.Horizontal;
            panel.Spacing = 18;
            Check(model.Orientation == Orientation.Horizontal && model.Spacing == 18, "Layout options were not written to the rendered settings model.");
            panel.KindIndex = (int)LayoutKind.WrapPanel;
            panel.OrientationIndex = (int)Orientation.Vertical;
            panel.Spacing = double.NaN;
            Check(model.Kind == LayoutKind.WrapPanel && model.Orientation == Orientation.Vertical && model.Spacing == 18,
                "Layout selectors did not validate or write their values.");
            model.Kind = LayoutKind.WrapPanel;
            Check(panel.ShowOrientation && panel.ShowSpacing && !panel.ShowGridDefinitions, "Wrap layout exposed grid options or hid supported options.");
            var notifications = 0;
            panel.PropertyChanged += (_, _) => notifications++;
            model.Kind = LayoutKind.Grid;
            Check(!panel.ShowOrientation && panel.ShowSpacing && panel.ShowGridDefinitions, "Grid layout exposed orientation or hid grid options.");
            Check(notifications == 5, "A layout-kind change did not refresh the selected value and every option visibility.");
            panel.Kind = LayoutKind.Canvas;
            Check(model.Kind == LayoutKind.Canvas, "Changing the layout selector did not update the rendered settings model.");
            var notificationsBeforeDispose = notifications;
            panel.Dispose();
            model.Kind = LayoutKind.StackPanel;
            Check(notifications == notificationsBeforeDispose, "An unloaded layout panel retained its model subscription.");
        });
        run("The editor auto-saves edits and initializes component settings without creating UI controls", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            editor.AddLayer();
            editor.AddComponent(fixture.Registry.Find(TextComponent.Id)!);
            var text = (ComponentSettings)editor.SelectedModel!;
            text.GetSettings<TextComponentSettings>().Text = "实时文本";
            var layout = editor.GetLayout(create: true)!;
            SceneLayoutEditing.ApplyBounds(layout, 75.2, 40.4, 200, 120, 1000, 800, resize: false);
            layout.Height = 120;
            layout.IsAutoHeight = false;
            fixture.Service.FlushPending();
            var loaded = fixture.Service.Load(editor.Scene!.SceneId);
            Check(loaded.Layers[1].Children[0].GetSettings<TextComponentSettings>().Text == "实时文本", "Text editing was not saved.");
            Check(loaded.Layers[1].Children[0].Features.Single(feature => feature.Settings is LayoutFeatureSettings)
                .GetSettings<LayoutFeatureSettings>().Margin == new Thickness(75, 40, 0, 0), "Canvas editing was not saved.");
            editor.DeleteSelection();
            fixture.Service.FlushPending();
            Check(fixture.Service.Load(editor.Scene.SceneId).Layers[1].Children.Count == 0, "Deleting a component was not saved.");
        });
        run("Corrupt scene configs recover from backups and retain unrecoverable original bytes", () =>
        {
            using var fixture = new Fixture();
            var scene = fixture.Service.Load(fixture.Service.ActiveSceneId);
            var path = Path.Combine(fixture.Service.GetSceneDirectory(scene.SceneId), "scene.json");
            Check(File.Exists(path + ".bak"), "Loading did not create the standard config backup.");
            File.WriteAllText(path, "broken");
            Check(fixture.Service.Load(scene.SceneId).SceneId == scene.SceneId, "Valid backup was not recovered.");
            File.WriteAllText(path, "broken primary");
            File.WriteAllText(path + ".bak", "broken backup");
            Throws<System.Text.Json.JsonException>(() => fixture.Service.Load(scene.SceneId));
            Check(File.ReadAllText(path) == "broken primary", "Corrupt input was silently overwritten.");
        });
        run("Scene save failures preserve pending data and recover on a later flush", () =>
        {
            using var fixture = new Fixture();
            var scene = fixture.Service.Load(fixture.Service.ActiveSceneId);
            var temporary = Path.Combine(fixture.Service.GetSceneDirectory(scene.SceneId), "scene.json.tmp");
            Directory.CreateDirectory(temporary);
            var failures = 0;
            fixture.Service.SaveFailed += (_, _) => failures++;
            scene.Name = "恢复后的场景";
            fixture.Service.QueueSave(scene);
            fixture.Service.FlushPending();
            Check(failures == 1 && fixture.Service.Load(scene.SceneId).Name == scene.Name &&
                fixture.Service.GetSaveError(scene.SceneId) != null,
                "Save failure discarded pending edits or did not retain its error.");
            Directory.Delete(temporary);
            fixture.Service.FlushPending();
            Check(fixture.Service.Load(scene.SceneId).Name == "恢复后的场景" &&
                fixture.Service.GetSaveError(scene.SceneId) == null,
                "Pending edits did not recover or retained an obsolete error.");
        });
        run("Reopening a scene retains its pending and failed save status until a successful flush", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            var sceneId = fixture.Service.ActiveSceneId;
            var other = fixture.Service.Create("另一个场景");
            var temporary = Path.Combine(fixture.Service.GetSceneDirectory(sceneId), "scene.json.tmp");
            Directory.CreateDirectory(temporary);

            editor.Open(sceneId);
            editor.Scene!.Name = "尚未写入的修改";
            editor.Open(other.SceneId);
            editor.Open(sceneId);
            Check(editor.SaveStatus == "正在保存…" && editor.Scene!.Name == "尚未写入的修改",
                "Reopening a queued scene incorrectly reported a successful save.");

            fixture.Service.FlushPending();
            editor.Open(other.SceneId);
            editor.Open(sceneId);
            Check(editor.SaveStatus == "保存失败" && editor.Scene!.Name == "尚未写入的修改",
                "Reopening a failed scene hid its unsaved status or discarded its edits.");

            Directory.Delete(temporary);
            fixture.Service.FlushPending();
            editor.RefreshSaveStatus();
            Check(editor.SaveStatus == "已保存" && !fixture.Service.HasPendingChanges(sceneId),
                "A successful retry did not clear the failed save status.");
            editor.Open(sceneId);
            Check(editor.SaveStatus == "已保存" && editor.Scene!.Name == "尚未写入的修改",
                "The recovered scene did not retain the saved edits after reopening.");
        });
        run("Grid movement selects the nearest edge or center alignment and rounds persisted geometry", () =>
        {
            var model = new LayoutFeatureSettings
            {
                Width = 80.25,
                Height = 30.75,
                IsAutoWidth = false,
                IsAutoHeight = false
            };
            SceneLayoutEditing.ApplyBounds(model, 12.6, 20.2, 80.25, 30.75, 500, 300, resize: false);
            Check(model.HorizontalAlignment == HorizontalAlignment.Left && model.VerticalAlignment == VerticalAlignment.Top && model.Margin == new Thickness(13, 20, 0, 0) && model.Width == 80 && model.Height == 31, "Left/top movement did not use rounded native layout values.");
            SceneLayoutEditing.ApplyBounds(model, 210, 134.5, 80, 31, 500, 300, resize: false);
            Check(model.HorizontalAlignment == HorizontalAlignment.Center && model.VerticalAlignment == VerticalAlignment.Center && model.Margin == new Thickness(0, 0, 0, 0), "Movement through the layout center did not select center alignment.");
            SceneLayoutEditing.ApplyBounds(model, 400.4, 250.1, 80, 31, 500, 300, resize: false);
            Check(model.HorizontalAlignment == HorizontalAlignment.Right && model.VerticalAlignment == VerticalAlignment.Bottom && model.Margin == new Thickness(0, 0, 20, 19), "Right/bottom movement did not snap to the nearest edges.");
            var fixedAlignment = new LayoutFeatureSettings
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsAutoAlignment = false
            };
            SceneLayoutEditing.ApplyBounds(fixedAlignment, 180, 20, 80, 31, 500, 300, resize: false, autoAlignment: false);
            Check(fixedAlignment.HorizontalAlignment == HorizontalAlignment.Center && fixedAlignment.VerticalAlignment == VerticalAlignment.Bottom && fixedAlignment.Margin == new Thickness(0, 0, 60, 249), "Disabling automatic alignment did not preserve the selected alignment and position.");
            var original = new TranslateTransform();
            var target = new Control
            {
                RenderTransform = original
            };
            using var feature = new OhMyLockScreen.Services.Features.Layout.LayoutFeature();
            feature.SetSettings(model);
            feature.Attach(target);
            Check(target.Margin == model.Margin && ReferenceEquals(target.RenderTransform, original), "Movement introduced a render transform instead of native margins.");
            Check(BitConverter.DoubleToInt64Bits(SceneLayoutEditing.Round(-0.1)) == 0, "Rounding retained negative zero.");
        });
        run("Tree icons use the design fonts and semantic component icons", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var root = editor.Roots[0];
            var layer = root.Children[0];
            var container = layer.Children[0];
            var clock = container.Children[0];
            var date = container.Children[1];
            Check(root.IconSource is { Glyph: "\uE8B9", FontSize: 16 } && root.IconSource.FontFamily?.Source == "Segoe Fluent Icons" && layer.IconSource is { Glyph: "\uE81E", FontSize: 16 }, "Scene/layer icons used the component icon font.");
            Check(container.IconSource is FluentIcons.Uwp.FluentIconSource { Icon: FluentIcons.Common.Icon.Group } && clock.IconSource is FluentIcons.Uwp.FluentIconSource { Icon: FluentIcons.Common.Icon.Clock } && date.IconSource is FluentIcons.Uwp.FluentIconSource { Icon: FluentIcons.Common.Icon.Calendar }, "Component icons were encoded as unrelated glyphs.");
            Check(clock.Title == "[时钟]" && date.Title == "[日期]", "Independent clock and date components did not retain their titles.");
        });
        run("Stack dragging only changes order and preserves component layout values", () =>
        {
            using var fixture = new Fixture();
            using var editor = fixture.Editor();
            editor.Open(fixture.Service.ActiveSceneId);
            var container = editor.Scene!.Layers[0].Children[0].GetSettings<ContainerComponentSettings>();
            var component = container.Children[0];
            var layout = component.Features.Single(feature => feature.Settings is LayoutFeatureSettings).GetSettings<LayoutFeatureSettings>();
            layout.Margin = new Thickness(1.2, 3.4, 5.6, 7.8);
            layout.Width = 90.4;
            var before = ConfigureFileHelper.CopyObject(component);
            editor.Select(component);
            editor.MoveSelectionTo(1);
            Check(ReferenceEquals(container.Children[1], component)
                && layout.Margin == before.Features.Single(feature => feature.Settings is LayoutFeatureSettings).GetSettings<LayoutFeatureSettings>().Margin
                && layout.Width == 90.4, "Stack movement modified geometry rather than only the sibling order.");
        });
        run("Resizing writes integer sizes and keeps grid cell-relative margins", () =>
        {
            var model = new LayoutFeatureSettings();
            SceneLayoutEditing.ApplyBounds(model, 241.3, 101.7, 121.4, 88.6, 400, 240, resize: true);
            Check(model.Width == 121 && model.Height == 89 && !model.IsAutoWidth && !model.IsAutoHeight, "Resizing did not produce explicit integer sizes.");
            Check(model.Margin == new Thickness(0, 0, 37, 50), "Grid cell margins included the cell origin.");
        });
        run("Component settings controls are independent and edit the renderer's existing settings model", () =>
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IComponentsService, ComponentsService>();
            OhMyLockScreen.Shared.Extensions.Scene.ComponentRegistryExtensions.AddComponent<TextComponent, OhMyLockScreen.Controls.Components.TextSettingsControl>(services);
            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IComponentsService>();
            var component = new ComponentSettings
            {
                Id = TextComponent.Id
            };
            var first = (OhMyLockScreen.Controls.Components.TextSettingsControl)factory.CreateSettingsControl(component)!;
            var second = (OhMyLockScreen.Controls.Components.TextSettingsControl)factory.CreateSettingsControl(component)!;
            Check(!ReferenceEquals(first, second) && ReferenceEquals(first.Settings, component.Settings) && ReferenceEquals(first.Settings, second.Settings), "Settings controls forked the editable model or were shared across views.");
            first.Settings.Text = "共享模型，独立控件";
            Check(((TextComponent)factory.CreateComponent(component)!).Settings.Text == first.Settings.Text, "The renderer did not receive the edited settings instance.");
        });
        await runAsync("Manual launches open the editor; hosted launches and lock activations open the lock view", async () =>
        {
            var app = new App();
            var view = new InfrastructureTests.ViewContext();
            var window = view.Window;
            LockScreenHosting.IsHosted = () => false;
            view.Invoke(() => app.Launch(new LaunchActivatedEventArgs()));
            var initialize = (Lazy<Task>)typeof(App).GetField("_initializeHost", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
            var startupDeadline = DateTime.UtcNow.AddSeconds(5);
            while (!initialize.Value.IsCompleted)
            {
                Check(DateTime.UtcNow < startupDeadline, "Production media startup did not finish.");
                view.Drain();
                await Task.Delay(5);
            }
            await initialize.Value;
            view.Drain();
            Window.Current = window;
            Check(window.Content is WindowToastHost { Frame.Content: SceneEditorPage }, "Manual launch did not open the editor.");
            var toastHost = (WindowToastHost)window.Content!;
            var notification = new ToastMessage("正在编辑");
            toastHost.Show(notification);
            var host = IAppHost.Host;
            app.Launch(new LaunchActivatedEventArgs());
            Check(ReferenceEquals(host, IAppHost.Host), "Repeated manual activation replaced the app host.");
            Check(ReferenceEquals(toastHost, window.Content) && toastHost.Toasts.Messages.Count == 1, "Repeated activation replaced the window toast host.");
            app.Activate(new ActivatedEventArgs(ActivationKind.LockScreenComponent));
            Check(Window.Current.Content is WindowToastHost { Frame.Content: MainPage }, "Lock activation did not open the lock view.");
            Check(ReferenceEquals(toastHost, window.Content) && !notification.IsOpen && toastHost.Toasts.Messages.Count == 0, "Lock activation retained an editor toast or replaced its host.");
            app.Launch(new LaunchActivatedEventArgs());
            Check(Window.Current.Content is WindowToastHost { Frame.Content: SceneEditorPage }, "Manual reactivation retained stale lock activation state.");
            LockScreenHosting.IsHosted = () => true;
            app.Launch(new LaunchActivatedEventArgs());
            Check(Window.Current.Content is WindowToastHost { Frame.Content: MainPage }, "Hosted launch was mistaken for a manual launch.");
            var page = (MainPage)((WindowToastHost)window.Content!).Frame.Content!;
            page.Load();
            var scenes = IAppHost.GetRequiredService<ISceneService>();
            var firstId = scenes.ActiveSceneId;
            Check(page.RootScenePresenter.Scene?.SceneId == firstId, "The lock view did not load the active scene.");
            var other = scenes.Create("另一个锁屏场景");
            scenes.SetActive(other.SceneId);
            window.CoreWindow.Dispatcher.Drain();
            Check(page.RootScenePresenter.Scene?.SceneId == other.SceneId, "The lock view did not refresh after an active-scene change.");

            var external = DefaultSceneFactory.Create("外部进程创建的场景");
            var externalDirectory = Path.Combine(scenes.ScenesPath, external.SceneId.ToString("N"));
            Directory.CreateDirectory(externalDirectory);
            ConfigureFileHelper.SaveConfig(Path.Combine(externalDirectory, "scene.json"), external);
            ConfigureFileHelper.SaveConfig(Path.Combine(ApplicationData.Current.LocalFolder.Path, "Settings.json"), new OhMyLockScreen.Models.Settings
            {
                ActiveSceneId = external.SceneId,
                LastEditedSceneId = firstId
            });
            app.Activate(new ActivatedEventArgs(ActivationKind.LockScreenComponent));
            Check(scenes.ActiveSceneId == external.SceneId && page.RootScenePresenter.Scene?.SceneId == external.SceneId, "The lock view ignored an active scene written by another app process.");

            page.Unload();
            scenes.SetActive(firstId);
            window.CoreWindow.Dispatcher.Drain();
            Check(page.RootScenePresenter.Scene?.SceneId == external.SceneId, "Unloaded lock view retained scene subscriptions.");
        });
        await runAsync("Automatic save persists edits without an explicit save command", async () =>
        {
            using var fixture = new Fixture();
            var scene = fixture.Service.Load(fixture.Service.ActiveSceneId);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Service.SceneSaved += (_, args) =>
            {
                if (args.SceneId == scene.SceneId)
                {
                    completion.TrySetResult();
                }
            };
            scene.Name = "自动保存";
            fixture.Service.QueueSave(scene);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(File.ReadAllText(Path.Combine(fixture.Service.GetSceneDirectory(scene.SceneId), "scene.json")).Contains("自动保存") || fixture.Service.Load(scene.SceneId).Name == "自动保存", "Automatic save never reached disk.");
        });
        await runAsync("Repeated equal snapshots cannot postpone a pending write or leave phantom saves", async () =>
        {
            using var fixture = new Fixture();
            var scene = fixture.Service.Load(fixture.Service.ActiveSceneId);
            var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Service.SceneSaved += (_, args) =>
            {
                if (args.SceneId == scene.SceneId)
                {
                    saved.TrySetResult();
                }
            };
            scene.Name = "连续通知";
            fixture.Service.QueueSave(scene);
            using var cancellation = new CancellationTokenSource();
            var repeated = Task.Run(async () =>
            {
                while (!cancellation.IsCancellationRequested)
                {
                    fixture.Service.QueueSave(scene);
                    await Task.Delay(20);
                }
            });
            try
            {
                await saved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
            finally
            {
                cancellation.Cancel();
                await repeated;
            }

            fixture.Service.QueueSave(scene);
            Check(!fixture.Service.HasPendingChanges(scene.SceneId), "An unchanged saved snapshot created another pending write.");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void Throws<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new Exception($"Expected {typeof(T).Name}.");
    }

    private sealed class Fixture : IDisposable
    {
        internal SettingsService Settings
        {
            get;
        }
        internal SceneService Service
        {
            get;
        }
        internal ComponentRegistryService Registry
        {
            get;
        }

        private readonly ServiceProvider _provider;
        internal Fixture(Action<IServiceCollection>? configureServices = null)
        {
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(SceneSettings).Assembly);
            var path = ApplicationData.Current.LocalFolder.Path;
            Settings = new SettingsService(NullLogger<SettingsService>.Instance, Path.Combine(path, "Settings.json"));
            Settings.LoadSettingsAsync().GetAwaiter().GetResult();
            Service = new SceneService(Settings, NullLogger<SceneService>.Instance, path);
            Service.Initialize();
            var services = new ServiceCollection();
            OhMyLockScreen.Shared.Extensions.Scene.ComponentRegistryExtensions.AddComponent<TextComponent>(services);
            OhMyLockScreen.Shared.Extensions.Scene.ComponentRegistryExtensions.AddComponent<ClockComponent>(services);
            OhMyLockScreen.Shared.Extensions.Scene.ComponentRegistryExtensions.AddComponent<DateComponent>(services);
            OhMyLockScreen.Shared.Extensions.Scene.ComponentRegistryExtensions.AddComponent<ContainerComponent>(services);
            configureServices?.Invoke(services);
            _provider = services.BuildServiceProvider();
            Registry = _provider.GetRequiredService<ComponentRegistryService>();
        }

        internal SceneEditorViewModel Editor() => new(Service, Settings, Registry);
        public void Dispose()
        {
            Service.Dispose();
            Settings.Dispose();
            _provider.Dispose();
        }
    }
}
