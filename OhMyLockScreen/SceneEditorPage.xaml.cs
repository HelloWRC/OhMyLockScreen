using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Services.Features.Appearance;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Services;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using OhMyLockScreen.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.Shared.Models.UI;
using OhMyLockScreen.Helpers.UI;
using TreeViewSelectionChangedEventArgs = Microsoft.UI.Xaml.Controls.TreeViewSelectionChangedEventArgs;
using TreeViewItem = Microsoft.UI.Xaml.Controls.TreeViewItem;

namespace OhMyLockScreen;

public sealed partial class SceneEditorPage : Page
{
    private readonly ISceneService _scenes;
    private readonly SettingsService _settings;
    private readonly IComponentsService _components;
    private readonly IFeatureService _features;
    private readonly FeatureRegistryService _featureRegistry;
    private Window? _window;
    private bool _loaded;
    private readonly HashSet<ToastMessage> _pageToasts = [];
    private readonly SceneSaveToastTracker _saveToasts = new();
    private bool _syncingLists;
    private bool _enteredFullScreen;
    private bool _updatingInspector;
    private bool _inspectorAgain;
    private bool _selectionPending;
    private SceneInfo? _contextScene;
    private IFeatureContainer? _featureTarget;
    private IBorderable? _featureBorderHost;
    private uint? _dragPointer;
    private bool _resizing;
    private bool _dragChanged;
    private Point _dragStart;
    private LayoutFeatureSettings? _dragLayout;
    private Panel? _dragPanel;
    private ComponentPresenter? _dragPresenter;
    private bool _dragPresenterHitTestVisible;
    private LayoutKind _dragKind;
    private Rect _startBounds;
    private Rect _layoutArea;
    private ResizeCorner _resizeCorner;
    private SceneTreeItem? _draggedTreeItem;
    private ComponentRegisterInfo? _draggedLibraryItem;
    private (SceneTreeItem Parent, int Index)? _treeDropTarget;
    private readonly Dictionary<ComponentSettings, object> _pendingDropAlignments = [];
    private readonly HashSet<TreeViewItem> _observedTreeItems = [];
    private readonly DragEventHandler _observedTreeItemDragOverHandler;
    private static readonly ComponentSettings LibraryDragPlaceholder = new();
    private const string SceneDragDataFormat = "OhMyLockScreen.SceneEditor.Drag";
    public SceneEditorViewModel ViewModel
    {
        get;
    }

    public SceneEditorPage()
    {
        _scenes = IAppHost.GetRequiredService<ISceneService>();
        _settings = IAppHost.GetRequiredService<SettingsService>();
        _components = IAppHost.GetRequiredService<IComponentsService>();
        _features = IAppHost.GetRequiredService<IFeatureService>();
        _featureRegistry = IAppHost.GetRequiredService<FeatureRegistryService>();
        ViewModel = IAppHost.GetRequiredService<SceneEditorViewModel>();
        _observedTreeItemDragOverHandler = OnObservedTreeItemDragOver;
        InitializeComponent();
        InitializePanelAnimations();
        SceneTree.AddHandler(UIElement.DragOverEvent, new DragEventHandler(OnTreeDragOver), true);
        SceneTree.AddHandler(UIElement.DropEvent, new DragEventHandler(OnTreeDrop), true);
        AddHandler(UIElement.DropEvent, new DragEventHandler(OnPageDrop), true);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _window = Window.Current;
        AttachSystemBackground();
        ConfigureTitleBar();
        ViewModel.SelectionChanged += OnSelectionChanged;
        ViewModel.StructureChanged += OnStructureChanged;
        ViewModel.InspectorChanged += OnInspectorChanged;
        ViewModel.Error += OnEditorError;
        ViewModel.PropertyChanged += OnPanelViewModelPropertyChanged;
        ViewModel.PropertyChanged += OnMediaImportStateChanged;
        _scenes.SceneSaved += OnSceneSaved;
        _scenes.ActiveSceneChanged += OnSceneSaved;
        _scenes.SaveFailed += OnSaveFailed;
        EditorPresenter.BackgroundFailed += OnBackgroundFailed;
        SceneViewport.LayoutUpdated += OnViewportLayoutUpdated;
        _window.CoreWindow.KeyDown += OnEditorKeyDown;
        try
        {
            var id = _settings.Settings.LastEditedSceneId;
            ViewModel.Open(id.HasValue && _scenes.GetScenes().Any(scene => scene.Id == id) ? id.Value : _scenes.ActiveSceneId);
            RefreshSceneLists();
            ShowTab(SceneEditorTab.Home);
            if (_scenes.LoadErrors.Count > 0)
            {
                ShowMessage("部分场景未能加载", string.Join(Environment.NewLine, _scenes.LoadErrors), InfoBarSeverity.Warning);
            }
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }

        UpdatePanelAnimations();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (!_loaded)
        {
            return;
        }

        EndDrag();
        ViewModel.PropertyChanged -= OnPanelViewModelPropertyChanged;
        StopPanelAnimations();
        ExitPreview();
        ClearPageToasts();
        _loaded = false;
        _pendingDropAlignments.Clear();
        _mediaImportRevision++;
        ViewModel.PropertyChanged -= OnMediaImportStateChanged;
        DetachSystemBackground();
        RefreshFeatureEditorTargets();
        ViewModel.SelectionChanged -= OnSelectionChanged;
        ViewModel.StructureChanged -= OnStructureChanged;
        ViewModel.InspectorChanged -= OnInspectorChanged;
        ViewModel.Error -= OnEditorError;
        _scenes.SceneSaved -= OnSceneSaved;
        _scenes.ActiveSceneChanged -= OnSceneSaved;
        _scenes.SaveFailed -= OnSaveFailed;
        EditorPresenter.BackgroundFailed -= OnBackgroundFailed;
        SceneViewport.LayoutUpdated -= OnViewportLayoutUpdated;
        if (_window != null)
        {
            _window.CoreWindow.KeyDown -= OnEditorKeyDown;
        }

        CompositionTarget.Rendering -= OnSelectionRendering;
        ViewModel.Dispose();
        CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = false;
        _window = null;
    }

    private void ConfigureTitleBar()
    {
        CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
        _window!.SetTitleBar(EditorTitleBar);
        var title = ApplicationView.GetForCurrentView().TitleBar;
        title.ButtonBackgroundColor = Colors.Transparent;
        title.ButtonInactiveBackgroundColor = Colors.Transparent;
        title.ButtonForegroundColor = Colors.White;
    }

    private void OnSelectionChanged(object? sender, EventArgs args)
    {
        EndDrag();
        ClearHover();
        InvalidateSelectionGeometry();
        RefreshInspector();
    }

    private void OnStructureChanged(object? sender, EventArgs args) => InvalidateSelectionGeometry();
    private void OnInspectorChanged(object? sender, EventArgs args) => RefreshInspector();
    private void OnEditorError(object? sender, Exception error) => ShowError(error);
    private void OnBackgroundFailed(object? sender, string message) => ShowMessage("背景播放失败", message);
    private async void OnSceneSaved(object? sender, SceneChangedEventArgs args)
    {
        await OnViewAsync(() =>
        {
            if (ViewModel.Scene?.SceneId == args.SceneId)
            {
                ViewModel.RefreshSaveStatus();
            }

            if (!_scenes.HasPendingChanges(args.SceneId))
            {
                _saveToasts.Close(args.SceneId);
            }

            if (ViewModel.Scene is { } current && _scenes.GetScenes().All(scene => scene.Id != current.SceneId))
            {
                ViewModel.Open(_scenes.ActiveSceneId);
            }

            RefreshSceneLists();
        });
    }

    private async void OnSaveFailed(object? sender, SceneChangedEventArgs args)
    {
        await OnViewAsync(() =>
        {
            if (ViewModel.Scene?.SceneId == args.SceneId)
            {
                ViewModel.RefreshSaveStatus();
            }

            _saveToasts.Replace(args.SceneId, ShowMessage("场景尚未保存", args.Error?.Message ?? "请检查数据目录是否可写。"));
        });
    }

    private async Task OnViewAsync(Action action)
    {
        var window = _window;
        if (!_loaded || window == null)
        {
            return;
        }

        try
        {
            if (window.CoreWindow.Dispatcher.HasThreadAccess)
            {
                action();
            }
            else
            {
                await window.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (_loaded && ReferenceEquals(_window, window))
                    {
                        action();
                    }
                });
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"无法刷新场景编辑器：{exception}");
        }
    }

    private void RefreshSceneLists()
    {
        _syncingLists = true;
        try
        {
            ViewModel.RefreshScenes();
        }
        finally
        {
            _syncingLists = false;
        }
    }

    private void OnSceneSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        var info = args.AddedItems.OfType<SceneInfo>().LastOrDefault();
        if (_syncingLists || info == null || info.Id == ViewModel.Scene?.SceneId)
        {
            return;
        }

        TryAction(() =>
        {
            EndDrag();
            ViewModel.Open(info.Id);
            RefreshSceneLists();
        });
    }

    private void OnRefreshScenesClick(object sender, RoutedEventArgs args) => TryAction(() =>
    {
        var id = ViewModel.Scene?.SceneId;
        _scenes.Reload();
        ViewModel.Open(id.HasValue && _scenes.GetScenes().Any(scene => scene.Id == id) ? id.Value : _scenes.ActiveSceneId);
        RefreshSceneLists();
        if (_scenes.LoadErrors.Count > 0)
        {
            ShowMessage("部分场景未能加载", string.Join(Environment.NewLine, _scenes.LoadErrors), InfoBarSeverity.Warning);
        }
    });
    private async void OnOpenFolderClick(object sender, RoutedEventArgs args)
    {
        try
        {
            var path = ViewModel.Scene is { } scene ? _scenes.GetSceneDirectory(scene.SceneId) : _scenes.ScenesPath;
            await Launcher.LaunchFolderAsync(await StorageFolder.GetFolderFromPathAsync(path));
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async void OnCreateSceneClick(object sender, RoutedEventArgs args)
    {
        var name = await AskNameAsync("新建场景", "新场景");
        if (name == null)
        {
            return;
        }

        TryAction(() =>
        {
            var scene = _scenes.Create(name);
            ViewModel.Open(scene.SceneId);
            RefreshSceneLists();
        });
    }

    private void OnNavigationChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_loaded || !ReferenceEquals(sender, EditorNavigation))
        {
            return;
        }

        var item = args.AddedItems.OfType<PivotItem>().LastOrDefault();
        if (item == null)
        {
            return;
        }

        var index = EditorNavigation.Items.IndexOf(item);
        if (index >= 0)
        {
            ShowTab((SceneEditorTab)index);
        }
    }

    private void ShowTab(SceneEditorTab tab)
    {
        EndDrag();
        ClearHover();
        ViewModel.ActiveTab = tab;
        RefreshFeatureEditorTargets();
        InvalidateSelectionGeometry();
    }

    private void OnContentSizeChanged(object sender, SizeChangedEventArgs args)
    {
        EditorPresenter.BackgroundViewportSize = args.NewSize;
        ViewModel.SidePanelWidth = Math.Clamp((args.NewSize.Width - 320) / 2, 200, 341);
        UpdatePanelViewport();
    }

    private void OnTreeSelectionChanged(Microsoft.UI.Xaml.Controls.TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        var item = args.AddedItems.OfType<SceneTreeItem>().LastOrDefault();
        if (item != null)
        {
            ViewModel.Select(item.Model);
        }
    }

    private void OnTreeDragOver(object sender, DragEventArgs args)
    {
        if (HandleFileDragOver(args))
        {
            return;
        }

        var target = ResolveTreeDropTarget(args);
        if (target == null)
        {
            return;
        }

        _treeDropTarget = target;
        if (_draggedTreeItem != null)
        {
            return;
        }

        args.AcceptedOperation = _draggedLibraryItem == null ? DataPackageOperation.Move : DataPackageOperation.Copy;
        args.DragUIOverride.Caption = $"放入 {target.Value.Parent.Title}";
    }

    private void OnTreeItemLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is TreeViewItem item && _observedTreeItems.Add(item))
        {
            item.AddHandler(UIElement.DragOverEvent, _observedTreeItemDragOverHandler, true);
        }
    }

    private void OnTreeItemUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is TreeViewItem item && _observedTreeItems.Remove(item))
        {
            item.RemoveHandler(UIElement.DragOverEvent, _observedTreeItemDragOverHandler);
        }
    }

    private void OnObservedTreeItemDragOver(object sender, DragEventArgs args)
    {
        if (HandleFileDragOver(args))
        {
            return;
        }

        if (!TryGetDraggedModel(out var model))
        {
            return;
        }

        var point = args.GetPosition(SceneTree);
        if (FindTreeTargetAtPoint(point) is not { } hit
            || ResolveTreeDropTarget(hit.Target, model, GetTreeDropRegion(hit.Target, hit.Row, point)) is not { } drop)
        {
            return;
        }

        _treeDropTarget = drop;
        if (_draggedLibraryItem != null)
        {
            args.AcceptedOperation = DataPackageOperation.Copy;
            args.DragUIOverride.Caption = $"放入 {drop.Parent.Title}";
        }
    }

    private void OnTreeDrop(object sender, DragEventArgs args)
    {
        if (IsFileDrag(args))
        {
            OnMediaFileDrop(SceneTree, args);
            return;
        }

        if (_draggedTreeItem != null)
        {
            return;
        }

        var target = ResolveTreeDropTarget(args);
        if (target == null)
        {
            return;
        }

        CommitTreeDrop(target.Value);
        args.Handled = true;
    }

    private void OnPageDrop(object sender, DragEventArgs args)
    {
        if (IsFileDrag(args))
        {
            var treePoint = args.GetPosition(SceneTree);
            if (!args.Handled && treePoint.X >= 0 && treePoint.Y >= 0
                && treePoint.X <= SceneTree.ActualWidth && treePoint.Y <= SceneTree.ActualHeight)
            {
                OnMediaFileDrop(SceneTree, args);
            }

            return;
        }

        if (_draggedLibraryItem == null)
        {
            return;
        }

        var point = args.GetPosition(SceneTree);
        if (point.X < 0 || point.Y < 0 || point.X > SceneTree.ActualWidth || point.Y > SceneTree.ActualHeight)
        {
            return;
        }

        var target = ResolveTreeDropTarget(args);
        if (target != null)
        {
            CommitTreeDrop(target.Value);
        }

        args.Handled = true;
    }

    private void CommitTreeDrop((SceneTreeItem Parent, int Index) target)
    {
        var libraryItem = _draggedLibraryItem;
        var treeItem = _draggedTreeItem;
        ClearDragPayload();
        if (libraryItem == null && treeItem == null)
        {
            return;
        }

        QueueEditorAction(() =>
        {
            if (libraryItem != null)
            {
                ViewModel.AddComponent(libraryItem, target.Parent, target.Index);
            }
            else if (treeItem != null)
            {
                ViewModel.MoveItem(treeItem, target.Parent, target.Index);
                ViewModel.RebuildTree();
            }
        });
    }

    private void CommitNativeTreeDrop(SceneTreeItem source, (SceneTreeItem Parent, int Index)? target)
    {
        QueueEditorAction(() =>
        {
            if (target != null)
            {
                ViewModel.MoveItem(source, target.Value.Parent, target.Value.Index);
            }

            ViewModel.RebuildTree();
        });
    }

    private (SceneTreeItem Parent, int Index)? ResolveTreeDropTarget(DragEventArgs args)
    {
        if (!TryGetDraggedModel(out var model))
        {
            return null;
        }

        var point = args.GetPosition(SceneTree);
        if (FindTreeTargetAtPoint(point) is not { } hit)
        {
            return null;
        }

        return ResolveTreeDropTarget(hit.Target, model, GetTreeDropRegion(hit.Target, hit.Row, point));
    }

    private TreeDropRegion GetTreeDropRegion(SceneTreeItem target, FrameworkElement? row, Point point)
    {
        if (target.Model is SceneSettings || row is not { ActualHeight: > 0 })
        {
            return TreeDropRegion.Inside;
        }

        var ratio = (point.Y - row.TransformToVisual(SceneTree).TransformPoint(new Point(0, 0)).Y) / row.ActualHeight;
        return ratio < 0.2 ? TreeDropRegion.Before : ratio > 0.8 ? TreeDropRegion.After : TreeDropRegion.Inside;
    }

    private (SceneTreeItem Parent, int Index)? ResolveTreeDropTarget(
        SceneTreeItem target,
        object model,
        TreeDropRegion region)
    {
        SceneTreeItem? parent = null;
        var index = 0;
        if (target.Model is SceneSettings)
        {
            parent = target;
            index = SceneHierarchyEditing.GetChildren(parent.Model)?.Count ?? 0;
        }
        else if (SceneHierarchyEditing.CanAccept(target.Model, model))
        {
            parent = target;
            index = SceneHierarchyEditing.GetChildren(parent.Model)?.Count ?? 0;
        }
        else if (target.Parent != null)
        {
            parent = target.Parent;
            var targetIndex = SceneHierarchyEditing.GetChildren(parent.Model)?.IndexOf(target.Model) ?? -1;
            if (targetIndex < 0)
            {
                return null;
            }

            index = targetIndex + (region == TreeDropRegion.Before ? 0 : 1);
        }

        if (parent == null
            || !SceneHierarchyEditing.CanAccept(parent.Model, model)
            || SceneHierarchyEditing.GetChildren(parent.Model) is not { } children)
        {
            return null;
        }

        if (_draggedTreeItem is { } source && ReferenceEquals(source.Parent, parent))
        {
            var sourceIndex = children.IndexOf(source.Model);
            if (sourceIndex < 0)
            {
                return null;
            }

            if (sourceIndex < index)
            {
                index--;
            }

            index = Math.Clamp(index, 0, children.Count - 1);
            return sourceIndex == index ? null : (parent, index);
        }

        return (parent, Math.Clamp(index, 0, children.Count));
    }

    private (SceneTreeItem Target, FrameworkElement Row)? FindTreeTargetAtPoint(Point point)
    {
        var hit = FindVisuals(SceneTree)
            .Where(row => row.Name == "TreeRow"
                && row.DataContext is SceneTreeItem
                && row.ActualWidth > 0
                && row.ActualHeight > 0)
            .Select(row =>
            {
                var bounds = row.TransformToVisual(SceneTree)
                    .TransformBounds(new Rect(0, 0, row.ActualWidth, row.ActualHeight));
                return (Row: row, Item: (SceneTreeItem)row.DataContext, Bounds: bounds);
            })
            .Where(candidate => candidate.Bounds.Contains(point))
            .OrderByDescending(candidate => candidate.Item.Depth)
            .ThenBy(candidate => candidate.Bounds.Width * candidate.Bounds.Height)
            .FirstOrDefault();
        return hit.Row == null ? null : (hit.Item, hit.Row);
    }

    private static IEnumerable<FrameworkElement> FindVisuals(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement element)
            {
                yield return element;
            }

            foreach (var descendant in FindVisuals(child))
            {
                yield return descendant;
            }
        }
    }

    private void OnAddComponentDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        var info = FindData<ComponentRegisterInfo>(args.OriginalSource as DependencyObject);
        if (!_loaded || info == null || !ViewModel.AddComponentCommand.CanExecute(info))
        {
            return;
        }

        TryAction(() => ViewModel.AddComponentCommand.Execute(info));
        args.Handled = true;
    }

    private void OnLibraryDragItemsStarting(object sender, DragItemsStartingEventArgs args)
    {
        _treeDropTarget = null;
        var info = args.Items.OfType<ComponentRegisterInfo>().FirstOrDefault();
        if (info == null)
        {
            args.Cancel = true;
            return;
        }

        _draggedLibraryItem = info;
        _draggedTreeItem = null;
        args.Data.SetText(SceneDragDataFormat);
        args.Data.RequestedOperation = DataPackageOperation.Copy;
    }

    private void OnTreeDragItemsStarting(Microsoft.UI.Xaml.Controls.TreeView sender, Microsoft.UI.Xaml.Controls.TreeViewDragItemsStartingEventArgs args)
    {
        var item = args.Items.OfType<SceneTreeItem>().SingleOrDefault();
        if (item?.Model is not ComponentSettings and not LayerSettings)
        {
            args.Cancel = true;
            return;
        }

        _draggedTreeItem = item;
        _draggedLibraryItem = null;
        _treeDropTarget = null;
        ViewModel.Select(item.Model);
        args.Data.SetText(SceneDragDataFormat);
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void OnDragItemsCompleted(Microsoft.UI.Xaml.Controls.TreeView sender, Microsoft.UI.Xaml.Controls.TreeViewDragItemsCompletedEventArgs args)
    {
        var source = _draggedTreeItem;
        var target = _treeDropTarget;
        ClearDragPayload();
        if (source != null && args.DropResult == DataPackageOperation.Move)
        {
            CommitNativeTreeDrop(source, target);
        }
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult == DataPackageOperation.Copy
            && _draggedLibraryItem != null && _treeDropTarget is { } target)
        {
            CommitTreeDrop(target);
            return;
        }

        ClearDragPayload();
    }

    private void ClearDragPayload()
    {
        _draggedTreeItem = null;
        _draggedLibraryItem = null;
        _treeDropTarget = null;
    }

    private void OnAddLayerClick(object sender, RoutedEventArgs args) => TryAction(ViewModel.AddLayer);
    private void RefreshInspector()
    {
        if (_updatingInspector)
        {
            _inspectorAgain = true;
            return;
        }

        _updatingInspector = true;
        try
        {
            do
            {
                _inspectorAgain = false;
                var model = ViewModel.SelectedModel;
                var previous = ViewModel.InspectorSections.ToArray();
                List<InspectorSection> desired = [];
                if (model is ComponentSettings component)
                {
                    var info = ViewModel.Library.FirstOrDefault(entry => entry.Id == component.Id);
                    var title = info?.Name ?? component.Id;
                    var existing = previous.FirstOrDefault(section => section.Feature == null && ReferenceEquals(section.Model, component) && ReferenceEquals(section.Settings, component.Settings) && section.Title == title);
                    if (existing != null)
                    {
                        desired.Add(existing);
                    }
                    else
                    {
                        var editor = _components.CreateSettingsControl(component);
                        desired.Add(new InspectorSection(title, editor, component, component.Settings));
                    }
                }
                else if (model is LayerSettings layer)
                {
                    desired.Add(previous.FirstOrDefault(section => section.Feature == null && ReferenceEquals(section.Model, layer)) ?? new InspectorSection("图层布局", null, layer, layer));
                }

                if (model is IFeatureContainer container)
                {
                    foreach (var feature in container.Features.ToArray())
                    {
                        var title = _featureRegistry.Find(feature.Id)?.Name ?? feature.Id;
                        var existing = previous.FirstOrDefault(section => ReferenceEquals(section.Model, model) && ReferenceEquals(section.Feature, feature) && ReferenceEquals(section.Settings, feature.Settings) && section.Title == title);
                        if (existing != null)
                        {
                            desired.Add(existing);
                        }
                        else
                        {
                            var editor = _features.CreateSettingsControl(feature);
                            desired.Add(new InspectorSection(title, editor, model, feature.Settings, container, feature));
                        }
                    }
                }

                if (!ReferenceEquals(model, ViewModel.SelectedModel))
                {
                    _inspectorAgain = true;
                    continue;
                }

                foreach (var removed in ViewModel.InspectorSections.Where(section => !desired.Contains(section)).ToArray())
                {
                    if (removed.Editor is FeatureSettingsControlBase featureEditor)
                    {
                        featureEditor.TargetElement = null;
                    }
                    ViewModel.InspectorSections.Remove(removed);
                }

                for (var index = 0; index < desired.Count; index++)
                {
                    var current = ViewModel.InspectorSections.IndexOf(desired[index]);
                    if (current < 0)
                    {
                        ViewModel.InspectorSections.Insert(index, desired[index]);
                    }
                    else if (current != index)
                    {
                        ViewModel.InspectorSections.Move(current, index);
                    }
                }
            }
            while (_inspectorAgain);
        }
        catch (Exception exception)
        {
            foreach (var section in ViewModel.InspectorSections)
            {
                if (section.Editor is FeatureSettingsControlBase featureEditor)
                {
                    featureEditor.TargetElement = null;
                }
            }
            ViewModel.InspectorSections.Clear();
            ShowError(exception);
        }
        finally
        {
            _updatingInspector = false;
            RefreshFeatureEditorTargets();
        }
    }

    private void RefreshFeatureEditorTargets()
    {
        FrameworkElement? presenter = null;
        if (_loaded && ViewModel.ShowComponents && ViewModel.InspectorSections.Any(section =>
            section.Editor is FeatureSettingsControlBase && ReferenceEquals(section.Model, ViewModel.SelectedModel)))
        {
            presenter = ReferenceEquals(ViewModel.SelectedModel, ViewModel.Scene)
                ? EditorPresenter
                : FindModelPresenter(ViewModel.SelectedModel!);
        }

        var borderHost = presenter as IBorderable;
        if (!ReferenceEquals(_featureBorderHost, borderHost))
        {
            if (_featureBorderHost != null)
            {
                _featureBorderHost.PropertyChanged -= OnFeatureBorderChanged;
            }
            _featureBorderHost = borderHost;
            if (_featureBorderHost != null)
            {
                _featureBorderHost.PropertyChanged += OnFeatureBorderChanged;
            }
        }

        var featureTarget = BackgroundFeature.ResolveBackgroundTarget(presenter);
        foreach (var section in ViewModel.InspectorSections)
        {
            if (section.Editor is not FeatureSettingsControlBase editor)
            {
                continue;
            }

            var target = ReferenceEquals(section.Model, ViewModel.SelectedModel)
                ? editor is LayoutSettingsControl ? presenter : featureTarget
                : null;

            if (!ReferenceEquals(editor.TargetElement, target))
            {
                editor.TargetElement = target;
            }
        }
    }

    private void OnFeatureBorderChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(IBorderable.Border))
        {
            RefreshFeatureEditorTargets();
        }
    }

    private void OnAddFeatureMenuOpening(object sender, object args)
    {
        if (sender is not MenuFlyout menu)
        {
            return;
        }

        _featureTarget = ViewModel.SelectedModel as IFeatureContainer;
        menu.Items.Clear();
        if (_featureTarget == null)
        {
            menu.Items.Add((MenuFlyoutItem)Resources["NoAvailableFeaturesMenuItem"]);
            return;
        }

        var target = ViewModel.SelectedModel switch
        {
            ComponentSettings => typeof(ComponentPresenter),
            LayerSettings => typeof(LayerPresenter),
            _ => typeof(ScenePresenter)
        };
        var template = (DataTemplate)Resources["AddFeatureMenuItemTemplate"];
        foreach (var info in _featureRegistry.Registered.Where(info => info.TargetType.IsAssignableFrom(target) && _featureTarget.Features.All(feature => feature.Id != info.Id)))
        {
            var item = (MenuFlyoutItem)template.LoadContent();
            item.DataContext = info;
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
        {
            menu.Items.Add((MenuFlyoutItem)Resources["NoAvailableFeaturesMenuItem"]);
        }
    }

    private void OnAddFeatureClick(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: FeatureRegisterInfo info } || _featureTarget == null || !ReferenceEquals(_featureTarget, ViewModel.SelectedModel)
            || _featureTarget.Features.Any(feature => feature.Id == info.Id))
        {
            return;
        }

        TryAction(() =>
        {
            var feature = new FeatureInfo
            {
                Id = info.Id
            };
            if (info.ClientSettingsType != null)
            {
                feature.GetSettingsOrCreate(info.ClientSettingsType);
            }

            _featureTarget.Features.Add(feature);
        });
    }

    private async void OnBrowseBackgroundClick(object sender, RoutedEventArgs args)
    {
        if (ViewModel.IsPreview || ViewModel.Scene is not { } scene)
        {
            return;
        }

        var revision = ++_mediaImportRevision;
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary
            };
            foreach (var extension in SceneMediaImport.VideoExtensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            var file = await picker.PickSingleFileAsync();
            if (file == null || !IsCurrentMediaImport(scene, revision))
            {
                return;
            }

            if (await ImportVideoBackgroundAsync(file, scene, revision))
            {
                TrackToast(this.ShowSuccessToastRef($"已设置视频背景：{file.Name}"));
            }
        }
        catch (Exception exception)
        {
            if (IsCurrentMediaImport(scene, revision))
            {
                ShowError(exception);
            }
        }
    }

    private void OnSceneRightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        _contextScene = FindData<SceneInfo>(args.OriginalSource as DependencyObject) ?? ViewModel.SelectedScene;
        if (_contextScene == null)
        {
            return;
        }

        GetContextMenu("SceneContextMenu").ShowAt(SceneList, args.GetPosition(SceneList));
        args.Handled = true;
    }

    private void OnTreeRightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        var item = FindData<SceneTreeItem>(args.OriginalSource as DependencyObject);
        if (item != null)
        {
            ViewModel.Select(item.Model);
        }

        GetContextMenu("TreeContextMenu").ShowAt(SceneTree, args.GetPosition(SceneTree));
        args.Handled = true;
    }

    private void OnSetContextSceneActiveClick(object sender, RoutedEventArgs args)
    {
        if (_contextScene is { } info)
        {
            TryAction(() => _scenes.SetActive(info.Id));
        }
    }

    private async void OnRenameSceneClick(object sender, RoutedEventArgs args)
    {
        if (_contextScene is not { } info)
        {
            return;
        }

        try
        {
            var name = await AskNameAsync("重命名场景", info.Name);
            if (name == null)
            {
                return;
            }

            if (ViewModel.Scene?.SceneId == info.Id)
            {
                ViewModel.Scene.Name = name;
            }
            else
            {
                var scene = _scenes.Load(info.Id);
                scene.Name = name;
                _scenes.QueueSave(scene);
            }

            RefreshSceneLists();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void OnDuplicateSceneClick(object sender, RoutedEventArgs args)
    {
        if (_contextScene is not { } info)
        {
            return;
        }

        TryAction(() =>
        {
            var scene = _scenes.Duplicate(info.Id);
            ViewModel.Open(scene.SceneId);
            RefreshSceneLists();
        });
    }

    private async void OnDeleteSceneClick(object sender, RoutedEventArgs args)
    {
        if (_contextScene is not { } info)
        {
            return;
        }

        try
        {
            ViewModel.DialogMessage = $"将删除“{info.Name}”及其专有资源。";
            PrepareDialog(DeleteSceneDialog);
            if (await DeleteSceneDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            _scenes.Delete(info.Id);
            if (ViewModel.Scene?.SceneId == info.Id)
            {
                ViewModel.Open(_scenes.ActiveSceneId);
            }

            RefreshSceneLists();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async void OnRenameSelectionClick(object sender, RoutedEventArgs args)
    {
        var selected = ViewModel.SelectedItem;
        if (selected == null)
        {
            return;
        }

        try
        {
            var name = await AskNameAsync("重命名", selected.Title);
            if (name == null)
            {
                return;
            }

            switch (selected.Model)
            {
                case SceneSettings scene:
                    scene.Name = name;
                    break;
                case LayerSettings layer:
                    layer.Name = name;
                    break;
                case ComponentSettings component:
                    component.Name = name;
                    break;
            }
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void OnMoveSelectionUpClick(object sender, RoutedEventArgs args) => TryAction(() => ViewModel.MoveSelection(-1));
    private void OnMoveSelectionDownClick(object sender, RoutedEventArgs args) => TryAction(() => ViewModel.MoveSelection(1));
    private void OnDeleteSelectionClick(object sender, RoutedEventArgs args) => TryAction(ViewModel.DeleteSelection);
    private async Task<string?> AskNameAsync(string title, string initial)
    {
        ViewModel.DialogTitle = title;
        ViewModel.DialogName = initial;
        ViewModel.DialogNameError = "";
        PrepareDialog(NameDialog);
        return await NameDialog.ShowAsync() == ContentDialogResult.Primary ? ViewModel.DialogName.Trim() : null;
    }

    private void OnNameDialogPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(ViewModel.DialogName))
        {
            return;
        }

        args.Cancel = true;
        ViewModel.DialogNameError = "名称不能为空";
    }

    private MenuFlyout GetContextMenu(string resourceKey)
    {
        var menu = (MenuFlyout)Resources[resourceKey];
        foreach (var item in menu.Items)
        {
            item.DataContext = ViewModel;
        }

        return menu;
    }

    private void PrepareDialog(ContentDialog dialog)
    {
        dialog.DataContext = ViewModel;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
        {
            dialog.XamlRoot = PageRoot.XamlRoot;
        }
    }

    private void OnCanvasPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (ViewModel.IsPreview || ViewModel.ActiveTab != SceneEditorTab.Components)
        {
            return;
        }

        var presenter = ResolveComponentPresenter(args.OriginalSource as DependencyObject, IsControlPressed(args));
        if (presenter?.Settings is not { } settings)
        {
            return;
        }

        ViewModel.Select(settings);
        ClearHover();
        if (!args.GetCurrentPoint(SceneViewport).Properties.IsLeftButtonPressed)
        {
            return;
        }

        StartDrag(args, resizing: false);
    }

    private void OnResizePointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (ViewModel.IsPreview || ViewModel.ActiveTab != SceneEditorTab.Components || sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        _resizeCorner = Enum.Parse<ResizeCorner>(tag);
        StartDrag(args, resizing: true);
    }

    private void StartDrag(PointerRoutedEventArgs args, bool resizing)
    {
        if (ViewModel.SelectedModel is not ComponentSettings component || FindSelectedPresenter() is not ComponentPresenter target)
        {
            return;
        }

        var panel = FindParent<Panel>(VisualTreeHelper.GetParent(target));
        if (panel == null)
        {
            return;
        }

        EndDrag();
        _pendingDropAlignments.Remove(component);
        _dragPanel = panel;
        _dragKind = panel switch
        {
            StackPanel => LayoutKind.StackPanel,
            CommunityToolkit.WinUI.Controls.WrapPanel => LayoutKind.WrapPanel,
            Canvas => LayoutKind.Canvas,
            _ => LayoutKind.Grid
        };
        var stackMove = !resizing && _dragKind is LayoutKind.StackPanel or LayoutKind.WrapPanel;
        _dragLayout = stackMove ? null : ViewModel.GetLayout(create: true);
        _layoutArea = GetLayoutArea(panel, target);
        var bounds = target.TransformToVisual(panel).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
        _startBounds = new Rect(bounds.X - _layoutArea.X, bounds.Y - _layoutArea.Y, bounds.Width, bounds.Height);
        _dragStart = args.GetCurrentPoint(SceneViewport).Position;
        _dragPointer = args.Pointer.PointerId;
        _resizing = resizing;
        _dragChanged = false;
        _dragPresenter = target;
        _dragPresenterHitTestVisible = target.IsHitTestVisible;
        target.IsHitTestVisible = false;
        if (!SceneViewport.CapturePointer(args.Pointer))
        {
            EndDrag();
            return;
        }

        args.Handled = true;
    }

    private void OnCanvasPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_dragPointer != args.Pointer.PointerId)
        {
            UpdateHover(args);
            return;
        }

        ClearHover();
        if (_dragPanel == null)
        {
            return;
        }

        var point = args.GetCurrentPoint(SceneViewport).Position;
        if (!_resizing)
        {
            point = ConstrainMove(args, point);
            _dragChanged |= point.X != _dragStart.X || point.Y != _dragStart.Y;
        }

        if (!_resizing && _dragKind is LayoutKind.StackPanel or LayoutKind.WrapPanel)
        {
            ReorderStack(point);
        }
        else if (_dragLayout != null)
        {
            var deltaX = SceneLayoutEditing.Round(point.X - _dragStart.X);
            var deltaY = SceneLayoutEditing.Round(point.Y - _dragStart.Y);
            var left = _startBounds.Left;
            var top = _startBounds.Top;
            var right = _startBounds.Right;
            var bottom = _startBounds.Bottom;
            if (_resizing)
            {
                if (_resizeCorner is ResizeCorner.TopLeft or ResizeCorner.BottomLeft)
                {
                    left = Math.Min(right - 8, left + deltaX);
                }
                else
                {
                    right = Math.Max(left + 8, right + deltaX);
                }

                if (_resizeCorner is ResizeCorner.TopLeft or ResizeCorner.TopRight)
                {
                    top = Math.Min(bottom - 8, top + deltaY);
                }
                else
                {
                    bottom = Math.Max(top + 8, bottom + deltaY);
                }
            }
            else
            {
                left += deltaX;
                right += deltaX;
                top += deltaY;
                bottom += deltaY;
            }

            if (_dragKind is LayoutKind.StackPanel or LayoutKind.WrapPanel)
            {
                SceneLayoutEditing.ResizeInStack(_dragLayout, right - left, bottom - top);
            }
            else
            {
                SceneLayoutEditing.ApplyBounds(
                    _dragLayout,
                    left,
                    top,
                    right - left,
                    bottom - top,
                    _layoutArea.Width,
                    _layoutArea.Height,
                    _resizing,
                    _dragKind == LayoutKind.Canvas,
                    _dragLayout.IsAutoAlignment);
            }
        }

        UpdateSelectionGeometry();
        args.Handled = true;
    }

    private void UpdateHover(PointerRoutedEventArgs args)
    {
        if (ViewModel.IsPreview || ViewModel.ActiveTab != SceneEditorTab.Components)
        {
            ClearHover();
            return;
        }

        var presenter = ResolveComponentPresenter(args.OriginalSource as DependencyObject, IsControlPressed(args));
        if (presenter?.Settings == null || ReferenceEquals(presenter.Settings, ViewModel.SelectedModel))
        {
            ClearHover();
            return;
        }

        var bounds = presenter.TransformToVisual(SceneViewport).TransformBounds(new Rect(0, 0, presenter.ActualWidth, presenter.ActualHeight));
        ViewModel.HoverGeometry = new SceneSelectionGeometry(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        ViewModel.ShowHover = true;
    }

    private void OnCanvasPointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (_dragPointer == null)
        {
            ClearHover();
        }
    }

    private void ClearHover()
    {
        if (ViewModel.ShowHover)
        {
            ViewModel.ShowHover = false;
        }
    }

    private ComponentPresenter? ResolveComponentPresenter(DependencyObject? source, bool deep)
    {
        List<ComponentPresenter> presenters = [];
        while (source != null)
        {
            if (source is ComponentPresenter { Settings: not null } presenter)
            {
                presenters.Add(presenter);
            }

            if (source is LayerPresenter)
            {
                break;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        if (deep)
        {
            return presenters.FirstOrDefault();
        }

        if (ViewModel.PreferredNestedComponentDepth is { } preferredDepth)
        {
            var sameLevel = presenters.FirstOrDefault(presenter => presenter.Settings is { } settings
                && ViewModel.FindItem(settings) is { } item
                && item.Depth == preferredDepth);
            if (sameLevel != null)
            {
                return sameLevel;
            }
        }

        return presenters.LastOrDefault();
    }

    private static bool IsControlPressed(PointerRoutedEventArgs args) => (args.KeyModifiers & VirtualKeyModifiers.Control) != 0;

    private Point ConstrainMove(PointerRoutedEventArgs args, Point point)
    {
        if ((args.KeyModifiers & VirtualKeyModifiers.Shift) == 0)
        {
            return point;
        }

        var (x, y) = SceneLayoutEditing.ConstrainToDominantAxis(_dragStart.X, _dragStart.Y, point.X, point.Y);
        return new Point(x, y);
    }

    private void OnCanvasRightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (ViewModel.IsPreview || ViewModel.ActiveTab != SceneEditorTab.Components)
        {
            return;
        }

        var control = _window != null
            && (_window.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;
        var presenter = ResolveComponentPresenter(args.OriginalSource as DependencyObject, control);
        if (presenter?.Settings is not { } settings)
        {
            return;
        }

        ViewModel.Select(settings);
        GetContextMenu("TreeContextMenu").ShowAt(SceneViewport, args.GetPosition(SceneViewport));
        args.Handled = true;
    }

    private void OnCanvasDragOver(object sender, DragEventArgs args)
    {
        if (HandleFileDragOver(args))
        {
            return;
        }

        if (ViewModel.IsPreview || ViewModel.ActiveTab != SceneEditorTab.Components || !TryGetDraggedModel(out var model))
        {
            return;
        }

        var point = args.GetPosition(SceneViewport);
        if (ResolveCanvasDrop(point, model) == null)
        {
            return;
        }

        args.AcceptedOperation = _draggedLibraryItem == null ? DataPackageOperation.Move : DataPackageOperation.Copy;
        args.DragUIOverride.Caption = _draggedLibraryItem?.Name ?? _draggedTreeItem?.Title ?? "";
        args.Handled = true;
    }

    private void OnCanvasDrop(object sender, DragEventArgs args)
    {
        if (IsFileDrag(args))
        {
            OnMediaFileDrop(SceneViewport, args);
            return;
        }

        if (!TryGetDraggedModel(out var model))
        {
            return;
        }

        var point = args.GetPosition(SceneViewport);
        if (ResolveCanvasDrop(point, model) is not { } target)
        {
            return;
        }

        var excluded = ReferenceEquals(_draggedTreeItem?.Parent, target.Parent) ? _draggedTreeItem?.Model : null;
        var index = GetInsertionIndex(target.Panel, point, excluded);
        var panelPoint = SceneViewport.TransformToVisual(target.Panel).TransformPoint(point);
        TryAction(() =>
        {
            ComponentSettings? component = null;
            if (_draggedLibraryItem is { } info)
            {
                component = ViewModel.AddComponent(info, target.Parent, index);
            }
            else if (_draggedTreeItem is { } item)
            {
                var sameParent = ReferenceEquals(item.Parent, target.Parent);
                if (ViewModel.MoveItem(item, target.Parent, index) || sameParent)
                {
                    component = item.Model as ComponentSettings;
                }
            }

            if (component != null)
            {
                ApplyCanvasDropPosition(component, target.Panel, panelPoint);
            }
        });
        ClearDragPayload();
        args.Handled = true;
    }

    private void ApplyCanvasDropPosition(ComponentSettings component, Panel panel, Point point)
    {
        SceneLayoutEditing.ApplyDropPosition(component, panel, point);
        if (panel is Grid && SceneLayoutEditing.GetLayout(component, create: false) is { IsAutoAlignment: true }
            && ViewModel.FindItem(component)?.Parent is { } parent)
        {
            _pendingDropAlignments[component] = parent.Model;
        }
    }

    private void AlignPendingDrops()
    {
        if (!_loaded || ViewModel.IsPreview)
        {
            _pendingDropAlignments.Clear();
            return;
        }

        foreach (var (component, parent) in _pendingDropAlignments.ToArray())
        {
            if (!ReferenceEquals(ViewModel.FindItem(component)?.Parent?.Model, parent))
            {
                _pendingDropAlignments.Remove(component);
                continue;
            }

            if (FindModelPresenter(component) is not ComponentPresenter { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 } presenter)
            {
                continue;
            }

            _pendingDropAlignments.Remove(component);
            if (FindParent<Panel>(VisualTreeHelper.GetParent(presenter)) is { } panel)
            {
                var bounds = presenter.TransformToVisual(panel).TransformBounds(new Rect(0, 0, presenter.ActualWidth, presenter.ActualHeight));
                SceneLayoutEditing.AlignDropPosition(component, panel, bounds);
            }
        }
    }

    private bool TryGetDraggedModel(out object model)
    {
        if (_draggedTreeItem?.Model is { } treeModel)
        {
            model = treeModel;
            return true;
        }

        if (_draggedLibraryItem != null)
        {
            model = LibraryDragPlaceholder;
            return true;
        }

        model = LibraryDragPlaceholder;
        return false;
    }

    private void TryMoveOnCanvas(ComponentSettings settings, Point point)
    {
        var item = ViewModel.FindItem(settings);
        if (item == null || ResolveCanvasDrop(point, item.Model) is not { } target)
        {
            return;
        }

        var excluded = ReferenceEquals(item.Parent, target.Parent) ? item.Model : null;
        ViewModel.MoveItem(item, target.Parent, GetInsertionIndex(target.Panel, point, excluded));
    }

    private (SceneTreeItem Parent, Panel Panel)? ResolveCanvasDrop(Point point, object movingModel)
    {
        var layer = ViewModel.GetCanvasLayer(movingModel);
        if (layer == null)
        {
            return null;
        }

        SceneTreeItem? best = null;
        var bestDepth = -1;
        var bestArea = double.MaxValue;
        foreach (var item in ViewModel.GetAllItems())
        {
            if (!SceneEditorViewModel.IsInCanvasLayer(item, layer)
                || !SceneHierarchyEditing.CanAccept(item.Model, movingModel)
                || FindModelPresenter(item.Model) is not { ActualWidth: > 0, ActualHeight: > 0 } presenter)
            {
                continue;
            }

            var bounds = presenter.TransformToVisual(SceneViewport).TransformBounds(new Rect(0, 0, presenter.ActualWidth, presenter.ActualHeight));
            if (!bounds.Contains(point))
            {
                continue;
            }

            var depth = item.Depth;
            var area = bounds.Width * bounds.Height;
            if (depth > bestDepth || depth == bestDepth && area <= bestArea)
            {
                best = item;
                bestDepth = depth;
                bestArea = area;
            }
        }

        if (best != null)
        {
            return FindLayoutPanel(best) is { } bestPanel ? (best, bestPanel) : null;
        }

        if (SceneHierarchyEditing.CanAccept(layer.Model, movingModel)
            && FindLayoutPanel(layer) is { } fallbackPanel)
        {
            return (layer, fallbackPanel);
        }

        return null;
    }

    private Panel? FindLayoutPanel(SceneTreeItem item)
    {
        var presenter = FindModelPresenter(item.Model);
        var layout = presenter == null
            ? null
            : FindVisual(presenter, element => element is ItemsControl { ItemsPanelRoot: not null }) as ItemsControl;
        return layout?.ItemsPanelRoot;
    }

    private int GetInsertionIndex(Panel panel, Point viewportPoint, object? excluded)
    {
        var children = panel.Children
            .OfType<ComponentPresenter>()
            .Where(child => child.Settings != null && !ReferenceEquals(child.Settings, excluded))
            .ToArray();
        if (children.Length == 0)
        {
            return 0;
        }

        var point = SceneViewport.TransformToVisual(panel).TransformPoint(viewportPoint);
        var bounds = children
            .Select(child =>
            {
                var rect = child.TransformToVisual(panel).TransformBounds(new Rect(0, 0, child.ActualWidth, child.ActualHeight));
                return (rect.X, rect.Y, rect.Width, rect.Height);
            })
            .ToArray();
        bool? horizontal = panel switch
        {
            StackPanel stack => stack.Orientation == Orientation.Horizontal,
            CommunityToolkit.WinUI.Controls.WrapPanel wrap => wrap.Orientation == Orientation.Horizontal,
            _ => null
        };
        return SceneLayoutEditing.GetInsertionIndex(
            bounds, point.X, point.Y, horizontal,
            wrap: panel is CommunityToolkit.WinUI.Controls.WrapPanel);
    }

    private void ReorderStack(Point point)
    {
        if (_dragPanel == null)
        {
            return;
        }

        ViewModel.MoveSelectionTo(GetInsertionIndex(_dragPanel, point, ViewModel.SelectedModel));
    }

    private static Rect GetLayoutArea(Panel panel, FrameworkElement child)
        => SceneLayoutEditing.GetLayoutArea(panel, Grid.GetRow(child), Grid.GetColumn(child),
            Grid.GetRowSpan(child), Grid.GetColumnSpan(child));

    private void OnCanvasPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_dragPointer == args.Pointer.PointerId)
        {
            if (_dragChanged && !_resizing && _dragPresenter?.Settings is { } settings)
            {
                TryMoveOnCanvas(settings, ConstrainMove(args, args.GetCurrentPoint(SceneViewport).Position));
            }

            EndDrag();
            args.Handled = true;
        }
    }

    private void OnCanvasDragCanceled(object sender, PointerRoutedEventArgs args)
    {
        if (_dragPointer == args.Pointer.PointerId)
        {
            EndDrag();
            args.Handled = true;
        }
    }

    private void EndDrag()
    {
        _dragPointer = null;
        _dragLayout = null;
        _dragPanel = null;
        _dragChanged = false;
        if (_dragPresenter != null)
        {
            _dragPresenter.IsHitTestVisible = _dragPresenterHitTestVisible;
            _dragPresenter = null;
        }

        SceneViewport.ReleasePointerCaptures();
    }

    private void OnViewportLayoutUpdated(object? sender, object args)
    {
        RefreshFeatureEditorTargets();
        AlignPendingDrops();
        if (!_selectionPending)
        {
            UpdateSelectionGeometry();
        }
    }

    private void InvalidateSelectionGeometry()
    {
        ViewModel.HasSelectionBounds = false;
        _selectionPending = true;
        CompositionTarget.Rendering -= OnSelectionRendering;
        CompositionTarget.Rendering += OnSelectionRendering;
    }

    private void OnSelectionRendering(object? sender, object args)
    {
        CompositionTarget.Rendering -= OnSelectionRendering;
        _selectionPending = false;
        if (_loaded)
        {
            UpdateSelectionGeometry();
        }
    }

    private void UpdateSelectionGeometry()
    {
        var target = !ViewModel.IsPreview && ViewModel.ActiveTab == SceneEditorTab.Components ? FindSelectedPresenter() : null;
        if (target == null || target.ActualWidth <= 0 || target.ActualHeight <= 0)
        {
            ViewModel.HasSelectionBounds = false;
            return;
        }

        var bounds = target.TransformToVisual(SceneViewport).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
        ViewModel.SelectionGeometry = new SceneSelectionGeometry(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        ViewModel.CanResizeSelection = ViewModel.SelectedModel is ComponentSettings;
        ViewModel.HasSelectionBounds = true;
    }

    private FrameworkElement? FindSelectedPresenter() => FindVisual(
        EditorPresenter,
        element => element switch
        {
            ComponentPresenter component => ReferenceEquals(component.Settings, ViewModel.SelectedModel),
            LayerPresenter layer => ReferenceEquals(layer.Layer, ViewModel.SelectedModel),
            _ => false
        });

    private FrameworkElement? FindModelPresenter(object model) => FindVisual(EditorPresenter, element => element switch
    {
        ComponentPresenter component => ReferenceEquals(component.Settings, model),
        LayerPresenter layer => ReferenceEquals(layer.Layer, model),
        _ => false
    });

    private static FrameworkElement? FindVisual(DependencyObject parent, Func<FrameworkElement, bool> predicate)
    {
        if (parent is FrameworkElement element && predicate(element))
        {
            return element;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var found = FindVisual(VisualTreeHelper.GetChild(parent, i), predicate);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static T? FindParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private static T? FindData<T>(DependencyObject? child)
        where T : class
    {
        while (child != null)
        {
            if (child is FrameworkElement { DataContext: T data })
            {
                return data;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private void OnEditorKeyDown(CoreWindow sender, KeyEventArgs args)
    {
        if (!_loaded || ViewModel.IsPreview)
        {
            return;
        }

        var control = (sender.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;
        if (control && args.VirtualKey == VirtualKey.S)
        {
            args.Handled = true;
            TryAction(SaveNow);
            return;
        }

        if (args.VirtualKey == VirtualKey.Delete && !IsTextEditing() && !HasOpenPopup())
        {
            EndDrag();
            TryAction(ViewModel.DeleteSelection);
            args.Handled = true;
        }
    }

    private void SaveNow()
    {
        _scenes.FlushPending();
        ViewModel.RefreshSaveStatus();
    }

    private static bool IsTextEditing() => FocusManager.GetFocusedElement() is TextBox or RichEditBox or PasswordBox or AutoSuggestBox or Microsoft.UI.Xaml.Controls.NumberBox;

    private bool HasOpenPopup() => _window != null && VisualTreeHelper.GetOpenPopups(_window).Count > 0;

    private void OnPreviewClick(object sender, RoutedEventArgs args)
    {
        if (ViewModel.IsPreview || _window == null)
        {
            return;
        }

        EndDrag();
        ClearHover();
        ViewModel.IsPreview = true;
        RefreshFeatureEditorTargets();
        ClearPageToasts();
        InvalidateSelectionGeometry();
        var view = ApplicationView.GetForCurrentView();
        _enteredFullScreen = !view.IsFullScreenMode && view.TryEnterFullScreenMode();
        _window.CoreWindow.KeyDown += OnPreviewKeyDown;
        _window.CoreWindow.PointerPressed += OnPreviewPointerPressed;
        _window.CoreWindow.Dispatcher.AcceleratorKeyActivated += OnPreviewAccelerator;
    }

    private void OnPreviewKeyDown(CoreWindow sender, KeyEventArgs args)
    {
        args.Handled = true;
        ExitPreview();
    }

    private void OnPreviewPointerPressed(CoreWindow sender, PointerEventArgs args)
    {
        args.Handled = true;
        ExitPreview();
    }

    private void OnPreviewAccelerator(CoreDispatcher sender, AcceleratorKeyEventArgs args)
    {
        if (args.EventType is CoreAcceleratorKeyEventType.KeyDown or CoreAcceleratorKeyEventType.SystemKeyDown)
        {
            args.Handled = true;
            ExitPreview();
        }
    }

    private void ExitPreview()
    {
        if (!ViewModel.IsPreview)
        {
            return;
        }

        ViewModel.IsPreview = false;
        RefreshFeatureEditorTargets();
        if (_window != null)
        {
            _window.CoreWindow.KeyDown -= OnPreviewKeyDown;
            _window.CoreWindow.PointerPressed -= OnPreviewPointerPressed;
            _window.CoreWindow.Dispatcher.AcceleratorKeyActivated -= OnPreviewAccelerator;
        }

        if (_enteredFullScreen)
        {
            ApplicationView.GetForCurrentView().ExitFullScreenMode();
        }

        _enteredFullScreen = false;
        InvalidateSelectionGeometry();
    }

    private void QueueEditorAction(Action action)
    {
        var window = _window;
        if (window == null)
        {
            return;
        }

        _ = window.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            if (_loaded && ReferenceEquals(_window, window))
            {
                TryAction(action);
            }
        });
    }

    private void TryAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void ShowError(Exception exception) => TrackToast(this.ShowErrorToastRef("操作未完成", exception));

    private ToastMessage ShowMessage(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Error)
    {
        return TrackToast(this.ShowToastRef(new ToastMessage(title, message)
        {
            Severity = severity,
            AutoClose = false
        }));
    }

    private ToastMessage TrackToast(ToastMessage message)
    {
        _pageToasts.RemoveWhere(toast => !toast.IsOpen);
        _pageToasts.Add(message);
        return message;
    }

    private void ClearPageToasts()
    {
        foreach (var message in _pageToasts)
        {
            message.Close();
        }

        _pageToasts.Clear();
        _saveToasts.Clear();
        if (_window?.Content is WindowToastHost host)
        {
            host.Clear();
        }
    }

    private enum ResizeCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    private enum TreeDropRegion
    {
        Before,
        Inside,
        After
    }
}
