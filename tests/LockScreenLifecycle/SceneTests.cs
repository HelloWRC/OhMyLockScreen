using Microsoft.Extensions.Logging.Abstractions;
using OhMyLockScreen;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.Scene;
using OhMyLockScreen.ViewModels;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

internal static class SceneTests
{
    internal static async Task RunAsync(Action<string, Action> run, Func<string, Func<Task>, Task> runAsync)
    {
        run("New scenes use independent directories and default to the system background without video resources", () =>
        {
            using var fixture = new Fixture();
            var scene = fixture.Service.Load(fixture.Service.ActiveSceneId);
            var directory = fixture.Service.GetSceneDirectory(scene.SceneId);
            Check(File.Exists(Path.Combine(directory, "scene.json")), "Missing scene.json in the scene folder.");
            Check(scene.Background.Mode == BackgroundMode.None && scene.Background.Source == "" && !Directory.Exists(Path.Combine(directory, "Assets")), "The default scene configured or copied a video background.");
            var another = fixture.Service.Create("新的系统背景场景");
            Check(another.Background.Mode == BackgroundMode.None && another.Background.Source == "", "Creating a scene selected a default video.");
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
        run("Duplicating a scene copies resources and retains independent relative references", () =>
        {
            using var fixture = new Fixture();
            var original = fixture.Service.Load(fixture.Service.ActiveSceneId);
            var assets = Path.Combine(fixture.Service.GetSceneDirectory(original.SceneId), "Assets");
            Directory.CreateDirectory(assets);
            File.WriteAllText(Path.Combine(assets, "background.mp4"), "example video");
            original.Background.Mode = BackgroundMode.Video;
            original.Background.Source = "Assets/background.mp4";
            original.Name = "原始场景";
            fixture.Service.QueueSave(original);
            var clone = fixture.Service.Duplicate(original.SceneId);
            var originalPath = Path.Combine(fixture.Service.GetSceneDirectory(original.SceneId), original.Background.Source);
            var clonePath = Path.Combine(fixture.Service.GetSceneDirectory(clone.SceneId), clone.Background.Source);
            Check(clone.SceneId != original.SceneId && clone.Name == "原始场景 副本" && File.ReadAllBytes(clonePath).SequenceEqual(File.ReadAllBytes(originalPath)), "Copy did not use the latest model and copy resources.");
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
            Check(ReferenceEquals(group.Children[1], clock) && ReferenceEquals(editor.SelectedItem, clock), "Same-parent dragging replaced the tree node or lost selection.");

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
            Check(loaded.Layers[1].Children[0].Features[0].GetSettings<LayoutFeatureSettings>().Margin == new Thickness(75, 40, 0, 0), "Canvas editing was not saved.");
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
            var layout = component.Features[0].GetSettings<LayoutFeatureSettings>();
            layout.Margin = new Thickness(1.2, 3.4, 5.6, 7.8);
            layout.Width = 90.4;
            var before = ConfigureFileHelper.CopyObject(component);
            editor.Select(component);
            editor.MoveSelectionTo(1);
            Check(ReferenceEquals(container.Children[1], component) && layout.Margin == before.Features[0].GetSettings<LayoutFeatureSettings>().Margin && layout.Width == 90.4, "Stack movement modified geometry rather than only the sibling order.");
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
            var window = Window.Current;
            LockScreenHosting.IsHosted = () => false;
            app.Launch(new LaunchActivatedEventArgs());
            var initialize = (Lazy<Task>)typeof(App).GetField("_initializeHost", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
            await initialize.Value;
            await Task.Delay(20);
            Check(window.Content is Frame { Content: SceneEditorPage }, "Manual launch did not open the editor.");
            Window.Current = window;
            var host = IAppHost.Host;
            app.Launch(new LaunchActivatedEventArgs());
            Check(ReferenceEquals(host, IAppHost.Host), "Repeated manual activation replaced the app host.");
            app.Activate(new ActivatedEventArgs(ActivationKind.LockScreenComponent));
            Check(Window.Current.Content is Frame { Content: MainPage }, "Lock activation did not open the lock view.");
            app.Launch(new LaunchActivatedEventArgs());
            Check(Window.Current.Content is Frame { Content: SceneEditorPage }, "Manual reactivation retained stale lock activation state.");
            LockScreenHosting.IsHosted = () => true;
            app.Launch(new LaunchActivatedEventArgs());
            Check(Window.Current.Content is Frame { Content: MainPage }, "Hosted launch was mistaken for a manual launch.");
            var page = (MainPage)((Frame)window.Content!).Content!;
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
        internal Fixture()
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
