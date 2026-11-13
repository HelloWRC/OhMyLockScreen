using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Services.LockScreenProvider;
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
using OhMyLockScreen.Shared.Models.LockScreenProvider;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using OhMyLockScreen.ViewModels;
using OhMyLockScreen.Views;
using Windows.ApplicationModel.DataTransfer;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.Shared.Models.UI;
using OhMyLockScreen.Helpers.UI;
using TreeView = Microsoft.UI.Xaml.Controls.TreeView;
using TreeViewItemInvokedEventArgs = Microsoft.UI.Xaml.Controls.TreeViewItemInvokedEventArgs;
using TreeViewSelectionChangedEventArgs = Microsoft.UI.Xaml.Controls.TreeViewSelectionChangedEventArgs;
using TreeViewItem = Microsoft.UI.Xaml.Controls.TreeViewItem;

namespace OhMyLockScreen;

public sealed partial class SceneEditorPage : Page
{
    private readonly ISceneService _scenes;
    private readonly ISceneBlobStorageService _blobs;
    private readonly SettingsService _settings;
    private readonly IComponentsService _components;
    private readonly IFeatureService _features;
    private readonly FeatureRegistryService _featureRegistry;
    private readonly LockScreenReplacementService _lockScreenReplacement;
    private Window? _window;
    private bool _loaded;
    private bool _restoringEditor;
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
    private SceneTreeItem? _pressedTreeItem;
    private Pointer? _treePointer;
    private Point _treePressPoint;
    private ComponentRegisterInfo? _draggedLibraryItem;
    private readonly Dictionary<ComponentSettings, object> _pendingDropAlignments = [];
    private static readonly ComponentSettings LibraryDragPlaceholder = new();
    private const string SceneDragDataFormat = "OhMyLockScreen.SceneEditor.Drag";
    public SceneEditorViewModel ViewModel
    {
        get;
    }

    public SceneEditorPage()
    {
        _scenes = IAppHost.GetRequiredService<ISceneService>();
        _blobs = IAppHost.GetRequiredService<ISceneBlobStorageService>();
        _settings = IAppHost.GetRequiredService<SettingsService>();
        _components = IAppHost.GetRequiredService<IComponentsService>();
        _features = IAppHost.GetRequiredService<IFeatureService>();
        _featureRegistry = IAppHost.GetRequiredService<FeatureRegistryService>();
        _lockScreenReplacement = IAppHost.GetRequiredService<LockScreenReplacementService>();
        ViewModel = IAppHost.GetRequiredService<SceneEditorViewModel>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
        VideoPicker.FileTypes = SceneMediaImport.VideoExtensions;
        VideoPicker.PickFailed += (_, exception) => ShowError(exception);
        VideoPicker.BlobPicked += OnVideoBlobPicked;
        InitializePanelAnimations();
        SceneTree.AddHandler(UIElement.DragOverEvent, new DragEventHandler(OnTreeDragOver), true);
        SceneTree.AddHandler(UIElement.DropEvent, new DragEventHandler(OnTreeDrop), true);
        SceneTree.AddHandler(UIElement.DragLeaveEvent, new DragEventHandler(OnTreeDragLeave), true);
        SceneTree.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnTreePointerPressed), true);
        AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnTreePointerMoved), true);
        AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnTreePointerReleased), true);
        AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnTreePointerCanceled), true);
        AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnTreePointerCaptureLost), true);
        PointerExited += OnEditorPointerExited;
        AddHandler(UIElement.DropEvent, new DragEventHandler(OnPageDrop), true);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        _restoringEditor = args.NavigationMode == NavigationMode.Back;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _window = Window.Current;
        _lockScreenReplacement.StateChanged += OnLockScreenReplacementStateChanged;
        _lockScreenReplacement.OperationFailed += OnLockScreenReplacementFailed;
        AttachSystemBackground();
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
            var scenes = _scenes.GetScenes();
            if (_restoringEditor && ViewModel.Scene is { } previous && scenes.Any(scene => scene.Id == previous.SceneId))
            {
                ViewModel.ResumeSceneObservation();
                RefreshInspector();
                InvalidateSelectionGeometry();
            }
            else
            {
                var id = _settings.Settings.LastEditedSceneId;
                ViewModel.Open(id.HasValue && scenes.Any(scene => scene.Id == id) ? id.Value : _scenes.ActiveSceneId);
                ShowTab(SceneEditorTab.Home);
            }

            RefreshSceneLists();
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
        if (_restoringEditor)
        {
            _restoringEditor = false;
            SettingsButton.Focus(FocusState.Programmatic);
        }

        await _lockScreenReplacement.EnsureInitializedAsync();
        await OnViewAsync(UpdateLockScreenReplacementInfoBar);
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (!_loaded)
        {
            return;
        }

        EndDrag();
        ClearTreePointer();
        _lockScreenReplacement.StateChanged -= OnLockScreenReplacementStateChanged;
        _lockScreenReplacement.OperationFailed -= OnLockScreenReplacementFailed;
        ViewModel.PropertyChanged -= OnPanelViewModelPropertyChanged;
        StopPanelAnimations();
        ExitPreview();
        ClearPageToasts();
        _loaded = false;
        _pendingDropAlignments.Clear();
        _mediaImportRevision++;
        VideoPicker.InvalidateSelection();
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
        _window = null;
    }

    private void OnSettingsClick(object sender, RoutedEventArgs args)
    {
        TryAction(() => Frame.Navigate(typeof(ApplicationSettingsPage)));
    }

    private async void OnLockScreenReplacementStateChanged(
        object? sender,
        LockScreenReplacementStateChangedEventArgs args) =>
        await OnViewAsync(UpdateLockScreenReplacementInfoBar);

    private async void OnLockScreenReplacementFailed(
        object? sender,
        LockScreenReplacementFailedEventArgs args)
    {
        var title = args.Operation == LockScreenProviderOperation.Query
            ? "无法读取系统锁屏设置"
            : "无法更改系统锁屏设置";
        await OnViewAsync(() => ShowMessage(title, args.Exception.Message));
    }

    private void UpdateLockScreenReplacementInfoBar() =>
        LockScreenReplacementInfoBar.IsOpen =
            _lockScreenReplacement.IsInitialized && !_lockScreenReplacement.CurrentValue;

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
        ClearTreePointer();
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

    private void OnTreeSelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        var item = args.AddedItems.OfType<SceneTreeItem>().LastOrDefault();
        if (_loaded && item != null)
        {
            ViewModel.Select(item.Model);
        }
    }

    private void OnTreeItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (_loaded && args.InvokedItem is SceneTreeItem item)
        {
            QueueEditorAction(() => ViewModel.Select(item.Model));
        }
    }

    private void OnTreeDragOver(object sender, DragEventArgs args)
    {
        if (HandleFileDragOver(args))
        {
            return;
        }

        var location = TryGetDraggedModel(out var model) && _loaded && !ViewModel.IsPreview
            ? ResolveTreeDropLocation(args.GetPosition(SceneTree), model)
            : null;
        args.AcceptedOperation = DataPackageOperation.None;
        args.Handled = true;
        TreeDropIndicator.Visibility = Visibility.Collapsed;
        if (location is { } target)
        {
            args.AcceptedOperation = DataPackageOperation.Copy;
            ShowTreeDropIndicator(target);
            args.DragUIOverride.Caption = GetTreeDropCaption(target);
        }
    }

    private void OnTreeDragLeave(object sender, DragEventArgs args) =>
        TreeDropIndicator.Visibility = Visibility.Collapsed;

    private void OnTreeDrop(object sender, DragEventArgs args)
    {
        if (IsFileDrag(args))
        {
            OnMediaFileDrop(SceneTree, args);
            return;
        }

        var target = ResolveTreeDropTarget(args);
        args.Handled = true;
        if (target is not { } destination)
        {
            args.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        args.AcceptedOperation = DataPackageOperation.Copy;
        CommitTreeDrop(destination);
    }

    private void OnPageDrop(object sender, DragEventArgs args)
    {
        if (args.Handled || ViewModel.ActiveTab != SceneEditorTab.Components
            || (!TryGetDraggedModel(out _) && !IsFileDrag(args)))
        {
            return;
        }

        var bounds = new Rect(0, 0, SceneTree.ActualWidth, SceneTree.ActualHeight);
        if (bounds.Contains(args.GetPosition(SceneTree)))
        {
            OnTreeDrop(SceneTree, args);
        }
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
            }
        });
    }

    private (SceneTreeItem Parent, int Index)? ResolveTreeDropTarget(DragEventArgs args)
    {
        if (!_loaded || ViewModel.IsPreview || !TryGetDraggedModel(out var model))
        {
            return null;
        }

        return ResolveTreeDropTarget(args.GetPosition(SceneTree), model);
    }

    private (SceneTreeItem Parent, int Index)? ResolveTreeDropTarget(Point point, object model) =>
        ResolveTreeDropLocation(point, model)?.Destination;

    private TreeDropLocation? ResolveTreeDropLocation(Point point, object model)
    {
        if (!new Rect(0, 0, SceneTree.ActualWidth, SceneTree.ActualHeight).Contains(point))
        {
            return null;
        }

        foreach (var row in FindVisuals(SceneTree).OfType<TreeViewItem>())
        {
            if (row.ActualWidth <= 0 || row.ActualHeight <= 0
                || SceneTree.ItemFromContainer(row) is not SceneTreeItem target)
            {
                continue;
            }

            var bounds = row.TransformToVisual(SceneTree)
                .TransformBounds(new Rect(0, 0, row.ActualWidth, row.ActualHeight));
            if (bounds.Contains(point))
            {
                var ratio = (point.Y - bounds.Y) / bounds.Height;
                var region = target.Model is SceneSettings ? TreeDropRegion.Inside : GetTreeDropRegion(target, ratio);
                return ResolveTreeDropTarget(target, model, region) is { } destination
                    ? new TreeDropLocation(destination, target, region, bounds)
                    : null;
            }
        }

        return null;
    }

    private readonly record struct TreeDropLocation(
        (SceneTreeItem Parent, int Index) Destination,
        SceneTreeItem Target,
        TreeDropRegion Region,
        Rect Bounds);

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

    private void OnTreePointerPressed(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(SceneTree);
        if (!_loaded || ViewModel.IsPreview
            || args.Pointer.PointerDeviceType == PointerDeviceType.Touch
            || !point.Properties.IsLeftButtonPressed
            || FindParent<ButtonBase>(args.OriginalSource as DependencyObject) != null
            || FindParent<TreeViewItem>(args.OriginalSource as DependencyObject) is not { } row
            || SceneTree.ItemFromContainer(row) is not SceneTreeItem { Model: ComponentSettings or LayerSettings } item)
        {
            return;
        }

        _pressedTreeItem = item;
        _treePointer = args.Pointer;
        _treePressPoint = point.Position;
    }

    private void OnTreePointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_treePointer?.PointerId != args.Pointer.PointerId || _pressedTreeItem is not { } item)
        {
            return;
        }

        var point = args.GetCurrentPoint(SceneTree);
        if (!point.Properties.IsLeftButtonPressed)
        {
            ClearTreePointer();
            return;
        }

        const double dragThreshold = 4;
        if (_draggedTreeItem == null)
        {
            if (Math.Abs(point.Position.X - _treePressPoint.X) < dragThreshold
                && Math.Abs(point.Position.Y - _treePressPoint.Y) < dragThreshold)
            {
                return;
            }

            _draggedTreeItem = item;
            ViewModel.Select(item.Model);
            CapturePointer(args.Pointer);
        }

        var target = ResolveTreeDropLocation(point.Position, item.Model);
        TreeDropIndicator.Visibility = Visibility.Collapsed;
        var caption = "无法放置";
        if (target is { } location)
        {
            ShowTreeDropIndicator(location);
            caption = GetTreeDropCaption(location);
        }
        else if (!new Rect(0, 0, SceneTree.ActualWidth, SceneTree.ActualHeight).Contains(point.Position)
            && ResolveCanvasDrop(args.GetCurrentPoint(SceneViewport).Position, item.Model) is { } canvasTarget)
        {
            caption = $"放入 {canvasTarget.Parent.Title}";
        }

        var pointer = args.GetCurrentPoint(PageRoot).Position;
        const double offset = 14;
        TreeDragHint.Content = caption;
        TreeDragHint.Measure(new Size(PageRoot.ActualWidth, PageRoot.ActualHeight));
        var size = TreeDragHint.DesiredSize;
        var left = pointer.X + offset + size.Width <= PageRoot.ActualWidth
            ? pointer.X + offset : pointer.X - offset - size.Width;
        var top = pointer.Y + offset + size.Height <= PageRoot.ActualHeight
            ? pointer.Y + offset : pointer.Y - offset - size.Height;
        TreeDragHintPopup.HorizontalOffset = Math.Max(0, left);
        TreeDragHintPopup.VerticalOffset = Math.Max(0, top);
        TreeDragHintPopup.IsOpen = true;
    }

    private static string GetTreeDropCaption(TreeDropLocation location) => location.Region switch
    {
        TreeDropRegion.Before => $"移到 {location.Target.Title} 前面",
        TreeDropRegion.After => $"移到 {location.Target.Title} 后面",
        _ => $"放入 {location.Destination.Parent.Title}"
    };

    private void ShowTreeDropIndicator(TreeDropLocation location)
    {
        var transform = SceneTree.TransformToVisual(PageRoot);
        var row = transform.TransformBounds(location.Bounds);
        var viewport = transform.TransformBounds(new Rect(0, 0, SceneTree.ActualWidth, SceneTree.ActualHeight));
        var top = Math.Max(row.Top, viewport.Top);
        var bottom = Math.Min(row.Bottom, viewport.Bottom);
        var inside = location.Region == TreeDropRegion.Inside;
        TreeDropIndicator.BorderThickness = inside ? new Thickness(2) : new Thickness(0, 2, 0, 0);
        TreeDropIndicator.Width = row.Width;
        TreeDropIndicator.Height = inside ? bottom - top : 2;
        Canvas.SetLeft(TreeDropIndicator, row.X);
        Canvas.SetTop(TreeDropIndicator, location.Region == TreeDropRegion.After ? bottom - 2 : top);
        TreeDropIndicator.Visibility = Visibility.Visible;
    }

    private void OnTreePointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_treePointer?.PointerId != args.Pointer.PointerId)
        {
            return;
        }

        try
        {
            if (_draggedTreeItem is not { } item || ViewModel.IsPreview)
            {
                return;
            }

            args.Handled = true;
            var point = args.GetCurrentPoint(SceneTree).Position;
            if (new Rect(0, 0, SceneTree.ActualWidth, SceneTree.ActualHeight).Contains(point))
            {
                if (ResolveTreeDropTarget(point, item.Model) is { } target)
                {
                    CommitTreeDrop(target);
                }
            }
            else
            {
                DropOnCanvas(args.GetCurrentPoint(SceneViewport).Position, item.Model);
            }
        }
        finally
        {
            ClearTreePointer();
        }
    }

    private void OnTreePointerCanceled(object sender, PointerRoutedEventArgs args)
    {
        if (_treePointer?.PointerId == args.Pointer.PointerId)
        {
            ClearTreePointer();
        }
    }

    private void OnEditorPointerExited(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(this).Position;
        if (!new Rect(0, 0, ActualWidth, ActualHeight).Contains(point))
        {
            OnTreePointerCanceled(sender, args);
        }
    }

    private void OnTreePointerCaptureLost(object sender, PointerRoutedEventArgs args)
    {
        if (ReferenceEquals(args.OriginalSource, this))
        {
            OnTreePointerCanceled(sender, args);
        }
    }

    private void ClearTreePointer()
    {
        var pointer = _treePointer;
        _treePointer = null;
        _pressedTreeItem = null;
        _draggedTreeItem = null;
        TreeDropIndicator.Visibility = Visibility.Collapsed;
        TreeDragHintPopup.IsOpen = false;
        if (pointer != null)
        {
            ReleasePointerCapture(pointer);
        }
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => ClearDragPayload();

    private void ClearDragPayload()
    {
        _draggedTreeItem = null;
        TreeDropIndicator.Visibility = Visibility.Collapsed;
        _draggedLibraryItem = null;
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
                        featureEditor.BlobUriConverter = null;
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
                    featureEditor.BlobUriConverter = null;
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

            var blobContext = presenter != null && ReferenceEquals(section.Model, ViewModel.SelectedModel)
                ? EditorPresenter.BlobUriConverter
                : null;
            if (!ReferenceEquals(editor.BlobUriConverter, blobContext))
            {
                editor.BlobUriConverter = blobContext;
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

    private void OnVideoBlobPicked(object? sender, string fileName)
    {
        if (_loaded && !ViewModel.IsPreview && ViewModel.Scene is { } scene)
        {
            _mediaImportRevision++;
            scene.Background.Mode = BackgroundMode.Video;
            TrackToast(this.ShowSuccessToastRef($"已设置视频背景：{fileName}"));
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

        var presenter = ResolveComponentPresenterAtPoint(
            args.GetCurrentPoint(SceneViewport).Position,
            IsControlPressed(args));
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
        var stackMove = !resizing && SceneLayoutEditing.ReordersChildrenOnCanvasDrop(panel);
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

        var presenter = ResolveComponentPresenterAtPoint(
            args.GetCurrentPoint(SceneViewport).Position,
            IsControlPressed(args));
        if (presenter?.Settings == null || ReferenceEquals(presenter.Settings, ViewModel.SelectedModel))
        {
            ClearHover();
            return;
        }

        var bounds = GetSceneViewportBounds(presenter);
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

    private ComponentPresenter? ResolveComponentPresenterAtPoint(Point point, bool deep)
    {
        var source = FindVisuals(EditorPresenter)
            .OfType<ComponentPresenter>()
            .Where(presenter => presenter.Settings is not null
                && presenter.Visibility == Visibility.Visible
                && presenter.ActualWidth > 0
                && presenter.ActualHeight > 0
                && GetSceneViewportBounds(presenter).Contains(point))
            .LastOrDefault();
        return source == null ? null : ResolveComponentPresenter(source, deep);
    }

    private ComponentPresenter? ResolveComponentPresenter(ComponentPresenter source, bool deep)
    {
        List<ComponentPresenter> presenters = [];
        for (DependencyObject? current = source; current != null && current is not LayerPresenter; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ComponentPresenter { Settings: not null } presenter)
            {
                presenters.Add(presenter);
            }
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
        var presenter = ResolveComponentPresenterAtPoint(args.GetPosition(SceneViewport), control);
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

        args.AcceptedOperation = DataPackageOperation.Copy;
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

        if (DropOnCanvas(args.GetPosition(SceneViewport), model))
        {
            ClearDragPayload();
            args.Handled = true;
        }
    }

    private bool DropOnCanvas(Point point, object model)
    {
        if (ResolveCanvasDrop(point, model) is not { } target)
        {
            return false;
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
                if (MoveItemToCanvas(item, target, index))
                {
                    component = item.Model as ComponentSettings;
                }
            }

            if (component != null)
            {
                ApplyCanvasDropPosition(component, target.Panel, panelPoint);
            }
        });
        return true;
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
        MoveItemToCanvas(item, target, GetInsertionIndex(target.Panel, point, excluded));
    }

    private bool MoveItemToCanvas(
        SceneTreeItem item,
        (SceneTreeItem Parent, Panel Panel) target,
        int index)
    {
        if (ViewModel.IsPreview || !ReferenceEquals(ViewModel.FindItem(item.Model), item))
        {
            return false;
        }

        if (ReferenceEquals(item.Parent, target.Parent)
            && !SceneLayoutEditing.ReordersChildrenOnCanvasDrop(target.Panel))
        {
            return true;
        }

        return ViewModel.MoveItem(item, target.Parent, index);
    }

    private (SceneTreeItem Parent, Panel Panel)? ResolveCanvasDrop(Point point, object movingModel)
    {
        if (!new Rect(0, 0, SceneViewport.ActualWidth, SceneViewport.ActualHeight).Contains(point))
        {
            return null;
        }

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

        var bounds = GetSceneViewportBounds(target);
        ViewModel.SelectionGeometry = new SceneSelectionGeometry(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        ViewModel.CanResizeSelection = ViewModel.SelectedModel is ComponentSettings;
        ViewModel.HasSelectionBounds = true;
    }

    private Rect GetSceneViewportBounds(FrameworkElement element)
        => element.TransformToVisual(SceneViewport).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

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

        if (args.VirtualKey == VirtualKey.Escape && _treePointer != null)
        {
            ClearTreePointer();
            args.Handled = true;
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
}
