using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using FluentIcons.Uwp;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.Scene;
using OhMyLockScreen.Shared.Helpers.Scene;
using Windows.UI.Xaml.Controls;
using OhMyLockScreen.Services;
using OhMyLockScreen.Services.Scene;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Shared.Helpers.UI;
using Stretch = Windows.UI.Xaml.Media.Stretch;

namespace OhMyLockScreen.ViewModels;

public sealed partial class SceneEditorViewModel : ObservableObject, IDisposable
{
    private readonly ISceneService _scenes;
    private readonly ISceneBlobStorageService _blobs;
    private readonly SettingsService _settings;
    private readonly ComponentRegistryService _components;
    private ObservableSettingsGraph? _graph;
    private object? _pendingSelectionModel;
    private IReadOnlyDictionary<object, bool>? _pendingExpansionStates;
    private bool _updatingTree;
    private bool _syncingMetadata;
    [ObservableProperty]
    private SceneSettings? _scene;
    [ObservableProperty]
    private SceneTreeItem? _selectedItem;
    [ObservableProperty]
    private string _saveStatus = "已保存";
    [ObservableProperty]
    private SceneEditorTab _activeTab;
    [ObservableProperty]
    private double _sidePanelWidth = 341;
    [ObservableProperty]
    private string _metadataName = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetadataNameError))]
    private string _metadataNameError = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewImage))]
    private Uri? _previewImageUri;
    [ObservableProperty]
    private bool _isCapturingPreview;
    [ObservableProperty]
    private string _dialogTitle = "";
    [ObservableProperty]
    private string _dialogName = "";
    [ObservableProperty]
    private string _dialogNameError = "";
    [ObservableProperty]
    private SceneSelectionGeometry _selectionGeometry;
    [ObservableProperty]
    private bool _hasSelectionBounds;
    [ObservableProperty]
    private bool _canResizeSelection;
    [ObservableProperty]
    private SceneSelectionGeometry _hoverGeometry;
    [ObservableProperty]
    private bool _showHover;
    public ObservableCollection<SceneTreeItem> Roots { get; } = [];
    public ObservableCollection<InspectorSection> InspectorSections { get; } = [];
    public IReadOnlyList<ComponentRegisterInfo> Library => _components.Registered;
    public IReadOnlyList<string> LicensePresets { get; } = (string[])
    [
        "CC BY 4.0",
        "CC BY-NC 4.0",
        "CC BY-SA 4.0",
        "CC BY-NC-SA 4.0",
        "CC0 1.0",
        "MIT",
        "保留所有权利"
    ];
    public bool HasPreviewImage => PreviewImageUri != null;
    public bool HasMetadataNameError => !string.IsNullOrEmpty(MetadataNameError);
    public object? SelectedModel => SelectedItem?.Model;
    public bool CanEditSelection => SelectedModel is ComponentSettings or LayerSettings;
    internal int? PreferredNestedComponentDepth =>
        SelectedItem is { Model: ComponentSettings, Parent.Model: ComponentSettings } selected
            ? selected.Depth
            : null;
    public bool ShowHome => ActiveTab == SceneEditorTab.Home;
    public bool ShowComponents => ActiveTab == SceneEditorTab.Components;
    public bool ShowBackground => ActiveTab == SceneEditorTab.Background;
    public GridLength PanelWidth => new(SidePanelWidth, GridUnitType.Pixel);
    public Thickness ViewportMargin => ShowComponents ? new Thickness(SidePanelWidth, 0, SidePanelWidth, 0) : default;
    public bool ShowSelection => ShowComponents && HasSelectionBounds;
    public bool ShowResizeHandles => ShowSelection && CanResizeSelection;

    public event EventHandler? SelectionChanged;
    public event EventHandler? StructureChanged;
    public event EventHandler? InspectorChanged;
    public event EventHandler? NodeStateChanged;
    public event EventHandler<Exception>? Error;
    public SceneEditorViewModel(
        ISceneService scenes,
        ISceneBlobStorageService blobs,
        SettingsService settings,
        ComponentRegistryService components)
    {
        _scenes = scenes;
        _blobs = blobs;
        _settings = settings;
        _components = components;
    }

    public void Open(Guid id)
    {
        // Load first so that a corrupt file cannot discard the current editor state.
        var scene = _scenes.Load(id);
        _graph?.Dispose();
        Scene = scene;
        _settings.Settings.LastEditedSceneId = id;
        _graph = new ObservableSettingsGraph(scene, OnSceneChanged);
        SyncMetadataFromScene(scene);
        Roots.Clear();
        var root = CreateTreeItem(scene, null);
        Roots.Add(root);
        SelectedItem = root;
        RefreshSaveStatus();
    }

    internal void ResumeSceneObservation()
    {
        if (Scene is { } scene)
        {
            _graph ??= new ObservableSettingsGraph(scene, OnSceneChanged);
            SyncMetadataFromScene(scene);
            RefreshSaveStatus();
        }
    }

    public void RefreshSaveStatus()
    {
        SaveStatus = Scene is not { } scene || !_scenes.HasPendingChanges(scene.SceneId)
            ? "已保存"
            : _scenes.GetSaveError(scene.SceneId) != null ? "保存失败" : "正在保存…";
    }

    public void RefreshPreviewImageUri() =>
        PreviewImageUri = Scene is { } scene
            ? _blobs.GetBlobUri(scene.SceneId, scene.PreviewImageBlobId)
            : null;

    internal void CommitMetadataName()
    {
        if (Scene == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(MetadataName))
        {
            _syncingMetadata = true;
            MetadataName = Scene.Name;
            MetadataNameError = "";
            _syncingMetadata = false;
            return;
        }

        var trimmed = MetadataName.Trim();
        if (!string.Equals(MetadataName, trimmed, StringComparison.Ordinal))
        {
            _syncingMetadata = true;
            MetadataName = trimmed;
            _syncingMetadata = false;
        }

        if (!string.Equals(Scene.Name, trimmed, StringComparison.Ordinal))
        {
            Scene.Name = trimmed;
        }
    }

    partial void OnMetadataNameChanged(string value)
    {
        if (_syncingMetadata || Scene == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            MetadataNameError = "名称不能为空";
            return;
        }

        MetadataNameError = "";
        if (!string.Equals(Scene.Name, value, StringComparison.Ordinal))
        {
            Scene.Name = value;
        }
    }

    private void SyncMetadataFromScene(SceneSettings scene)
    {
        _syncingMetadata = true;
        MetadataName = scene.Name;
        MetadataNameError = "";
        _syncingMetadata = false;
        RefreshPreviewImageUri();
    }

    partial void OnSelectedItemChanged(SceneTreeItem? value)
    {
        HasSelectionBounds = false;
        CanResizeSelection = value is { Model: ComponentSettings component, IsEffectivelyVisible: true, IsEffectivelyLocked: false }
            && SceneLayoutEditing.CanEditLayout(component);
        OnPropertyChanged(nameof(SelectedModel));
        OnPropertyChanged(nameof(CanEditSelection));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    internal bool CanSelectOnCanvas(object model) =>
        FindItem(model) is { IsEffectivelyVisible: true, IsEffectivelyLocked: false };

    public void Select(object model)
    {
        // TreeView can raise selection events while its nodes are being reconciled.
        if (_updatingTree)
        {
            return;
        }

        var item = FindItem(model);
        if (item != null)
        {
            SelectedItem = item;
        }
    }

    internal SceneTreeItem? FindItem(object model) => Flatten(Roots).FirstOrDefault(item => ReferenceEquals(item.Model, model));

    internal IReadOnlyList<SceneTreeItem> GetAllItems() => Flatten(Roots).ToArray();

    internal void RebuildTree()
    {
        if (Scene == null)
        {
            return;
        }

        var expanded = Flatten(Roots)
            .Where(item => item.IsExpanded)
            .Select(item => item.Model)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        var selected = SelectedModel;
        Roots.Clear();
        var root = CreateTreeItem(Scene, null, expanded);
        Roots.Add(root);
        SelectedItem = selected == null
            ? root
            : Flatten(Roots).FirstOrDefault(item => ReferenceEquals(item.Model, selected)) ?? root;
        StructureChanged?.Invoke(this, EventArgs.Empty);
    }

    internal SceneTreeItem? GetCanvasLayer(object? movingModel)
    {
        var anchor = (movingModel == null ? null : FindItem(movingModel)) ?? SelectedItem;
        for (var item = anchor; item != null; item = item.Parent)
        {
            if (item.Model is LayerSettings)
            {
                return item;
            }
        }

        return Flatten(Roots).FirstOrDefault(item => item.Model is LayerSettings && item.IsEffectivelyVisible && !item.IsEffectivelyLocked)
            ?? Flatten(Roots).FirstOrDefault(item => item.Model is LayerSettings);
    }

    internal static bool IsInCanvasLayer(SceneTreeItem item, SceneTreeItem layer)
    {
        for (var current = item; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, layer))
            {
                return true;
            }
        }

        return false;
    }

    private void OnSceneChanged(object sender, string? property, bool collectionChanged)
    {
        if (Scene == null)
        {
            return;
        }

        var previousSelection = SelectedItem;
        var structureChanged = false;
        var nodeStateChanged = false;
        var wasUpdatingTree = _updatingTree;
        _updatingTree = true;
        try
        {
            foreach (var node in Flatten(Roots).ToArray())
            {
                var children = SceneHierarchyEditing.GetChildren(node.Model);
                if (ReferenceEquals(sender, children) || ReferenceEquals(sender, node.Model) && property is "Layers" or "Children" or "Settings" || node.Model is ComponentSettings component && ReferenceEquals(sender, component.Settings) && property == "Children")
                {
                    structureChanged |= ReconcileChildren(node, children?.Cast<object>() ?? []);
                }

                if (ReferenceEquals(sender, node.Model))
                {
                    if (property == "Name")
                    {
                        node.RefreshTitle();
                    }

                    if (string.IsNullOrEmpty(property)
                        || property is nameof(LayerSettings.IsVisible) or nameof(LayerSettings.IsLocked))
                    {
                        node.RefreshNodeState();
                        nodeStateChanged = true;
                    }
                }
            }
        }
        finally
        {
            _updatingTree = wasUpdatingTree;
        }

        if (structureChanged)
        {
            var selection = _pendingSelectionModel is { } selectedModel
                ? Flatten(Roots).FirstOrDefault(item => ReferenceEquals(item.Model, selectedModel))
                : previousSelection;
            if (_pendingSelectionModel == null)
            {
                var nodes = Flatten(Roots).ToHashSet();
                while (selection != null && !nodes.Contains(selection))
                {
                    selection = selection.Parent;
                }
            }

            if (selection != null)
            {
                SelectedItem = selection;
            }
            else if (_pendingSelectionModel == null)
            {
                SelectedItem = Roots.FirstOrDefault();
            }

            // Removing and reinserting a node clears TreeView's selection even when
            // the selected model and SceneTreeItem instance have not changed.
            if (ReferenceEquals(SelectedItem, previousSelection))
            {
                OnPropertyChanged(nameof(SelectedItem));
            }

            StructureChanged?.Invoke(this, EventArgs.Empty);
        }

        if (nodeStateChanged || structureChanged)
        {
            CanResizeSelection = SelectedItem is { Model: ComponentSettings selectedComponent, IsEffectivelyVisible: true, IsEffectivelyLocked: false }
                && SceneLayoutEditing.CanEditLayout(selectedComponent);
        }

        if (nodeStateChanged)
        {
            NodeStateChanged?.Invoke(this, EventArgs.Empty);
        }

        var selectedFeatures = (SelectedModel as IFeatureContainer)?.Features;
        if (ReferenceEquals(sender, selectedFeatures)
            || ReferenceEquals(sender, SelectedModel) && property is "Features" or "Settings"
            || sender is FeatureInfo info && selectedFeatures?.Contains(info) == true && property is "Id" or "Settings" or nameof(FeatureInfo.IsEnabled))
        {
            InspectorChanged?.Invoke(this, EventArgs.Empty);
        }

        if (ReferenceEquals(sender, Scene))
        {
            if (property is null or nameof(SceneSettings.Name)
                && !string.Equals(MetadataName, Scene.Name, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(Scene.Name))
            {
                _syncingMetadata = true;
                MetadataName = Scene.Name;
                MetadataNameError = "";
                _syncingMetadata = false;
            }

            if (property is null or nameof(SceneSettings.PreviewImageBlobId))
            {
                RefreshPreviewImageUri();
            }
        }

        try
        {
            _scenes.QueueSave(Scene);
            RefreshSaveStatus();
        }
        catch (Exception exception)
        {
            SaveStatus = "保存失败";
            Error?.Invoke(this, exception);
        }
    }

    private SceneTreeItem CreateTreeItem(
        object model,
        SceneTreeItem? parent,
        IReadOnlySet<object>? expanded = null)
    {
        var item = new SceneTreeItem(model, parent, _components);
        if (expanded != null)
        {
            item.IsExpanded = expanded.Contains(model);
        }
        else if (_pendingExpansionStates?.TryGetValue(model, out var isExpanded) == true)
        {
            item.IsExpanded = isExpanded;
        }

        ReconcileChildren(item, SceneHierarchyEditing.GetChildren(model)?.Cast<object>() ?? [], expanded);
        return item;
    }

    private bool ReconcileChildren(
        SceneTreeItem parent,
        IEnumerable<object> models,
        IReadOnlySet<object>? expanded = null)
    {
        var existing = parent.Children.ToList();
        var desired = new List<SceneTreeItem>();
        foreach (var model in models)
        {
            var item = existing.FirstOrDefault(node => ReferenceEquals(node.Model, model));
            if (item != null)
            {
                existing.Remove(item);
            }

            desired.Add(item ?? CreateTreeItem(model, parent, expanded));
        }

        var changed = existing.Count != 0;
        foreach (var removed in existing)
        {
            parent.Children.Remove(removed);
        }

        for (var index = 0; index < desired.Count; index++)
        {
            var current = parent.Children.IndexOf(desired[index]);
            if (current < 0)
            {
                parent.Children.Insert(index, desired[index]);
                changed = true;
            }
            else if (current != index)
            {
                // WinUI 2 TreeView ignores Move notifications. Keep its native nodes
                // in the same order so later removals target the correct component.
                parent.Children.RemoveAt(current);
                parent.Children.Insert(index, desired[index]);
                changed = true;
            }
        }

        return changed;
    }

    partial void OnActiveTabChanged(SceneEditorTab value)
    {
        AddComponentCommand.NotifyCanExecuteChanged();
        RefreshViewProperties();
    }

    partial void OnSidePanelWidthChanged(double value)
    {
        OnPropertyChanged(nameof(PanelWidth));
        OnPropertyChanged(nameof(ViewportMargin));
    }

    private void RefreshViewProperties()
    {
        OnPropertyChanged(nameof(ShowHome));
        OnPropertyChanged(nameof(ShowComponents));
        OnPropertyChanged(nameof(ShowBackground));
        OnPropertyChanged(nameof(ViewportMargin));
        RefreshSelectionVisibility();
    }

    partial void OnHasSelectionBoundsChanged(bool value) => RefreshSelectionVisibility();
    partial void OnCanResizeSelectionChanged(bool value) => RefreshSelectionVisibility();
    private void RefreshSelectionVisibility()
    {
        OnPropertyChanged(nameof(ShowSelection));
        OnPropertyChanged(nameof(ShowResizeHandles));
    }

    private bool CanAddComponent(ComponentRegisterInfo? info) => ShowComponents && info != null;
    [RelayCommand(CanExecute = nameof(CanAddComponent))]
    public void AddComponent(ComponentRegisterInfo info)
    {
        if (Scene == null)
        {
            return;
        }

        var parent = SelectedItem;
        if (parent?.Model is ComponentSettings { Settings: not IComponentContainerSettings })
        {
            parent = parent.Parent;
        }

        if (parent?.Model is not LayerSettings and not ComponentSettings { Settings: IComponentContainerSettings })
        {
            if (Scene.Layers.Count == 0)
            {
                AddLayer();
            }

            parent = Flatten(Roots).FirstOrDefault(item => ReferenceEquals(item.Model, Scene.Layers[0]));
        }

        if (parent != null)
        {
            AddComponent(info, parent, SceneHierarchyEditing.GetChildren(parent.Model)?.Count ?? 0);
        }
    }

    internal ComponentSettings? AddComponent(ComponentRegisterInfo info, SceneTreeItem parent, int index)
    {
        if (Scene == null || !SceneHierarchyEditing.Contains(Scene, parent.Model)
            || SceneHierarchyEditing.GetChildren(parent.Model) is not { } children)
        {
            return null;
        }

        var component = CreateComponent(info);
        if (!SceneHierarchyEditing.CanAccept(parent.Model, component))
        {
            return null;
        }

        children.Insert(Math.Clamp(index, 0, children.Count), component);
        Select(component);
        return component;
    }

    private static ComponentSettings CreateComponent(ComponentRegisterInfo info)
    {
        var component = info.CreateSettings();
        if (!component.Features.Any(feature => feature.Id == "omls.features.layout.layout"))
        {
            component.Features.Add(new FeatureInfo
            {
                Id = "omls.features.layout.layout",
                Settings = new LayoutFeatureSettings
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
        }
        return component;
    }

    internal ComponentSettings? AddImageComponent(string name, string blobId, int width, int height, object parentModel, int index)
    {
        if (Scene == null || FindItem(parentModel) == null)
        {
            return null;
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var info = _components.Find(ContainerComponent.Id)
            ?? throw new InvalidOperationException("布局容器未注册。");
        var component = CreateComponent(info);
        component.Name = name;
        var layout = SceneLayoutEditing.GetLayout(component, create: true)!;
        layout.Width = width;
        layout.Height = height;
        layout.IsAutoWidth = false;
        layout.IsAutoHeight = false;
        var background = new ImageBrush { Stretch = Stretch.Uniform };
        SceneImageBrush.SetBlobId(background, blobId);
        component.Features.Add(new FeatureInfo
        {
            Id = "omls.features.appearance.background",
            Settings = new BackgroundFeatureSettings
            {
                Background = background
            }
        });

        if (ReferenceEquals(parentModel, Scene))
        {
            if (Scene.Layers.Count == 0)
            {
                AddLayer();
            }

            parentModel = Scene.Layers[0];
        }

        if (!SceneHierarchyEditing.CanAccept(parentModel, component)
            || SceneHierarchyEditing.GetChildren(parentModel) is not { } children)
        {
            return null;
        }

        children.Insert(Math.Clamp(index, 0, children.Count), component);
        Select(component);
        return component;
    }

    internal bool MoveItem(SceneTreeItem item, SceneTreeItem parent, int index)
    {
        if (Scene == null || item.Parent is not { } sourceParent
            || !SceneHierarchyEditing.Contains(Scene, sourceParent.Model)
            || !SceneHierarchyEditing.Contains(Scene, parent.Model)
            || SceneHierarchyEditing.GetChildren(sourceParent.Model) is not { } source
            || !source.Contains(item.Model))
        {
            return false;
        }

        var subtree = Flatten([item]).ToArray();
        var previousSelectionModel = _pendingSelectionModel;
        var previousExpansionStates = _pendingExpansionStates;
        _pendingSelectionModel = subtree.Contains(SelectedItem) ? SelectedModel : null;
        _pendingExpansionStates = subtree.ToDictionary(node => node.Model, node => node.IsExpanded, ReferenceEqualityComparer.Instance);
        try
        {
            var moved = ReferenceEquals(sourceParent.Model, parent.Model)
                ? MoveInPlace(item.Model, parent.Model, index)
                : SceneHierarchyEditing.TryMove(item.Model, sourceParent.Model, parent.Model, index);
            if (moved && _pendingSelectionModel != null)
            {
                Select(_pendingSelectionModel);
            }

            return moved;
        }
        finally
        {
            _pendingSelectionModel = previousSelectionModel;
            _pendingExpansionStates = previousExpansionStates;
        }
    }

    private static bool MoveInPlace(object model, object parent, int index) => (model, parent) switch
    {
        (LayerSettings layer, SceneSettings scene) => SceneHierarchyEditing.TryMove(scene.Layers, layer, index),
        (ComponentSettings component, LayerSettings layer) => SceneHierarchyEditing.TryMove(layer.Children, component, index),
        (ComponentSettings component, ComponentSettings { Settings: IComponentContainerSettings container }) => SceneHierarchyEditing.TryMove(container.Children, component, index),
        _ => false
    };

    public void AddLayer()
    {
        if (Scene == null)
        {
            return;
        }

        var layer = new LayerSettings
        {
            Name = $"图层 {Scene.Layers.Count + 1}",
            Kind = LayoutKind.Grid
        };
        Scene.Layers.Add(layer);
        Select(layer);
    }

    public void DeleteSelection()
    {
        if (SelectedItem is not { Parent: { } parent } selected)
        {
            return;
        }

        var index = parent.Children.IndexOf(selected);
        SelectedItem = index > 0 ? parent.Children[index - 1] : parent;
        switch (selected.Model)
        {
            case LayerSettings layer:
                Scene?.Layers.Remove(layer);
                break;
            case ComponentSettings component:
                SceneHierarchyEditing.GetChildren(parent.Model)?.Remove(component);
                break;
        }
    }

    public void MoveSelection(int direction)
    {
        if (SelectedItem is not { Parent: { } parent } item)
        {
            return;
        }

        var children = SceneHierarchyEditing.GetChildren(parent.Model);
        var current = children?.IndexOf(item.Model) ?? -1;
        if (children != null && current >= 0)
        {
            var target = current + direction;
            if (target >= 0 && target < children.Count)
            {
                MoveItem(item, parent, target);
            }
        }
    }

    public void MoveSelectionTo(int index)
    {
        if (SelectedItem is not { Parent: { } parent } item || SceneHierarchyEditing.GetChildren(parent.Model) is not { } children)
        {
            return;
        }

        var current = children.IndexOf(item.Model);
        index = Math.Clamp(index, 0, children.Count - 1);
        if (current >= 0 && current != index)
        {
            MoveItem(item, parent, index);
        }
    }

    public LayoutFeatureSettings? GetLayout(bool create)
        => SceneLayoutEditing.GetLayout(SelectedModel as IFeatureContainer, create);

    public void Dispose()
    {
        _graph?.Dispose();
        _graph = null;
        foreach (var section in InspectorSections)
        {
            section.Dispose();
        }
        InspectorSections.Clear();
        _scenes.FlushPending();
    }

    private static IEnumerable<SceneTreeItem> Flatten(IEnumerable<SceneTreeItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Flatten(item.Children))
            {
                yield return child;
            }
        }
    }

}

public sealed partial class SceneTreeItem(object model, SceneTreeItem? parent, ComponentRegistryService components) : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActionButtons))]
    [NotifyPropertyChangedFor(nameof(ShowLockButton))]
    [NotifyPropertyChangedFor(nameof(IsVisibilityButtonInteractive))]
    [NotifyPropertyChangedFor(nameof(VisibilityButtonOpacity))]
    private bool _isPointerOver;

    public object Model { get; } = model;
    public SceneTreeItem? Parent { get; } = parent;
    public int Depth => Parent == null ? 0 : Parent.Depth + 1;
    public ObservableCollection<SceneTreeItem> Children { get; } = [];
    public bool CanToggleState => Model is LayerSettings or ComponentSettings;

    public bool IsLocked
    {
        get => Model switch
        {
            LayerSettings layer => layer.IsLocked,
            ComponentSettings component => component.IsLocked,
            _ => false
        };
        set
        {
            switch (Model)
            {
                case LayerSettings layer when layer.IsLocked != value:
                    layer.IsLocked = value;
                    break;
                case ComponentSettings component when component.IsLocked != value:
                    component.IsLocked = value;
                    break;
            }
        }
    }

    public bool IsVisible
    {
        get => Model switch
        {
            LayerSettings layer => layer.IsVisible,
            ComponentSettings component => component.IsVisible,
            _ => true
        };
        set
        {
            switch (Model)
            {
                case LayerSettings layer when layer.IsVisible != value:
                    layer.IsVisible = value;
                    break;
                case ComponentSettings component when component.IsVisible != value:
                    component.IsVisible = value;
                    break;
            }
        }
    }

    public bool IsHidden
    {
        get => !IsVisible;
        set => IsVisible = !value;
    }

    public bool IsEffectivelyLocked => IsLocked || (Parent?.IsEffectivelyLocked ?? false);
    public bool IsEffectivelyHidden => IsHidden || (Parent?.IsEffectivelyHidden ?? false);
    public bool IsEffectivelyVisible => !IsEffectivelyHidden;
    public double ContentOpacity => IsEffectivelyHidden ? 0.45 : 1.0;
    public bool ShowActionButtons => CanToggleState && (IsPointerOver || IsLocked || IsHidden);
    public bool ShowLockButton => CanToggleState && (IsPointerOver || IsLocked);
    public bool IsVisibilityButtonInteractive => CanToggleState && (IsPointerOver || IsHidden);
    public double VisibilityButtonOpacity => IsVisibilityButtonInteractive ? 1.0 : 0.0;
    public Icon LockIcon => IsLocked ? Icon.LockClosed : Icon.LockOpen;
    public Icon VisibilityIcon => IsHidden ? Icon.EyeOff : Icon.Eye;
    public string LockToolTip => IsLocked ? "解锁" : "锁定";
    public string VisibilityToolTip => IsHidden ? "显示" : "隐藏";

    public string Title => Model switch
    {
        SceneSettings scene => scene.Name,
        LayerSettings layer => string.IsNullOrWhiteSpace(layer.Name) ? "图层" : layer.Name,
        ComponentSettings component => string.IsNullOrWhiteSpace(component.Name) ? $"[{components.Find(component.Id)?.Name ?? component.Id}]" : component.Name,
        _ => ""
    };
    public FontIconSource? IconSource => Model switch
    {
        ComponentSettings component => components.Find(component.Id)?.IconSource,
        _ => new FluentIconSource
        {
            Icon = Model is LayerSettings ? Icon.Layer : Icon.Image,
            IconSize = IconSize.Resizable,
            FontSize = 16
        }
    };

    internal void RefreshTitle()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IconSource));
    }

    internal void RefreshNodeState()
    {
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsHidden));
        OnPropertyChanged(nameof(ShowActionButtons));
        OnPropertyChanged(nameof(ShowLockButton));
        OnPropertyChanged(nameof(IsVisibilityButtonInteractive));
        OnPropertyChanged(nameof(VisibilityButtonOpacity));
        OnPropertyChanged(nameof(LockIcon));
        OnPropertyChanged(nameof(VisibilityIcon));
        OnPropertyChanged(nameof(LockToolTip));
        OnPropertyChanged(nameof(VisibilityToolTip));
        RefreshInheritedState();
    }

    internal void RefreshInheritedState()
    {
        OnPropertyChanged(nameof(IsEffectivelyLocked));
        OnPropertyChanged(nameof(IsEffectivelyHidden));
        OnPropertyChanged(nameof(IsEffectivelyVisible));
        OnPropertyChanged(nameof(ContentOpacity));
        foreach (var child in Children)
        {
            child.RefreshInheritedState();
        }
    }
}

public enum SceneEditorTab
{
    Home,
    Components,
    Background
}

public readonly record struct SceneSelectionGeometry(double X, double Y, double Width, double Height)
{
    public double HandleLeft => X - 12;
    public double HandleTop => Y - 12;
    public double HandleRight => X + Width - 12;
    public double HandleBottom => Y + Height - 12;
}

public sealed class InspectorSection : ObservableObject, IDisposable
{
    private readonly IFeatureContainer? _owner;
    private bool _disposed;

    public InspectorSection(string title, UserControl? editor, object model, object? settings, IFeatureContainer? owner = null, FeatureInfo? feature = null)
    {
        Title = title;
        Editor = editor;
        Model = model;
        Settings = settings;
        Feature = feature;
        _owner = owner;
        RemoveCommand = new RelayCommand(RemoveFeature);
        ToggleEnabledCommand = new RelayCommand(ToggleEnabled);
        if (Feature != null)
        {
            Feature.PropertyChanged += OnFeaturePropertyChanged;
        }
    }

    public string Title
    {
        get;
    }
    public UserControl? Editor
    {
        get;
    }
    public object Model
    {
        get;
    }
    public object? Settings
    {
        get;
    }
    public FeatureInfo? Feature
    {
        get;
    }
    public bool CanRemove => _owner != null && Feature != null;
    public bool IsFeatureEnabled => Feature?.IsEnabled ?? true;
    public double EditorOpacity => IsFeatureEnabled ? 1.0 : 0.5;
    public Icon ToggleEnabledIcon => IsFeatureEnabled ? Icon.Eye : Icon.EyeOff;
    public string ToggleEnabledToolTip => IsFeatureEnabled ? "禁用特性" : "启用特性";
    public IRelayCommand RemoveCommand
    {
        get;
    }
    public IRelayCommand ToggleEnabledCommand
    {
        get;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Feature != null)
        {
            Feature.PropertyChanged -= OnFeaturePropertyChanged;
        }
    }

    private void ToggleEnabled()
    {
        if (Feature != null)
        {
            Feature.IsEnabled = !Feature.IsEnabled;
        }
    }

    private void OnFeaturePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(FeatureInfo.IsEnabled))
        {
            OnPropertyChanged(nameof(IsFeatureEnabled));
            OnPropertyChanged(nameof(EditorOpacity));
            OnPropertyChanged(nameof(ToggleEnabledIcon));
            OnPropertyChanged(nameof(ToggleEnabledToolTip));
        }
    }

    private void RemoveFeature()
    {
        if (_owner != null && Feature != null)
        {
            _owner.Features.Remove(Feature);
        }
    }
}
