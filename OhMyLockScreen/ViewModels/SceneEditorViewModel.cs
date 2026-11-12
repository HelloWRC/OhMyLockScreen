using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly SettingsService _settings;
    private readonly ComponentRegistryService _components;
    private ObservableSettingsGraph? _graph;
    private object? _pendingSelectionModel;
    private IReadOnlyDictionary<object, bool>? _pendingExpansionStates;
    private bool _updatingTree;
    [ObservableProperty]
    private SceneSettings? _scene;
    [ObservableProperty]
    private SceneTreeItem? _selectedItem;
    [ObservableProperty]
    private string _saveStatus = "已保存";
    [ObservableProperty]
    private SceneEditorTab _activeTab;
    [ObservableProperty]
    private bool _isPreview;
    [ObservableProperty]
    private double _sidePanelWidth = 341;
    [ObservableProperty]
    private SceneInfo? _selectedScene;
    [ObservableProperty]
    private string _dialogTitle = "";
    [ObservableProperty]
    private string _dialogName = "";
    [ObservableProperty]
    private string _dialogMessage = "";
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
    public ObservableCollection<SceneInfo> Scenes { get; } = [];
    public ObservableCollection<SceneTreeItem> Roots { get; } = [];
    public ObservableCollection<InspectorSection> InspectorSections { get; } = [];
    public IReadOnlyList<ComponentRegisterInfo> Library => _components.Registered;
    public object? SelectedModel => SelectedItem?.Model;
    public bool CanEditSelection => SelectedModel is ComponentSettings or LayerSettings;
    internal int? PreferredNestedComponentDepth =>
        SelectedItem is { Model: ComponentSettings, Parent.Model: ComponentSettings } selected
            ? selected.Depth
            : null;
    public bool ShowHome => !IsPreview && ActiveTab == SceneEditorTab.Home;
    public bool ShowComponents => !IsPreview && ActiveTab == SceneEditorTab.Components;
    public bool ShowBackground => !IsPreview && ActiveTab == SceneEditorTab.Background;
    public int ContentRow => IsPreview ? 0 : 2;
    public int ContentRowSpan => IsPreview ? 3 : 1;
    public GridLength PanelWidth => new(SidePanelWidth, GridUnitType.Pixel);
    public Thickness ViewportMargin => ShowComponents ? new Thickness(SidePanelWidth, 0, SidePanelWidth, 0) : default;
    public bool ShowSelection => ShowComponents && HasSelectionBounds;
    public bool ShowResizeHandles => ShowSelection && CanResizeSelection;

    public event EventHandler? SelectionChanged;
    public event EventHandler? StructureChanged;
    public event EventHandler? InspectorChanged;
    public event EventHandler<Exception>? Error;
    public SceneEditorViewModel(ISceneService scenes, SettingsService settings, ComponentRegistryService components)
    {
        _scenes = scenes;
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
        RefreshScenes();
        Roots.Clear();
        var root = CreateTreeItem(scene, null);
        Roots.Add(root);
        SelectedItem = root;
        RefreshSaveStatus();
    }

    public void RefreshSaveStatus()
    {
        SaveStatus = Scene is not { } scene || !_scenes.HasPendingChanges(scene.SceneId)
            ? "已保存"
            : _scenes.GetSaveError(scene.SceneId) != null ? "保存失败" : "正在保存…";
    }

    public void RefreshScenes()
    {
        var desired = _scenes.GetScenes().Select(info => Scenes.FirstOrDefault(existing => existing == info) ?? info).ToArray();
        foreach (var removed in Scenes.Where(info => !desired.Contains(info)).ToArray())
        {
            Scenes.Remove(removed);
        }

        for (var index = 0; index < desired.Length; index++)
        {
            var current = Scenes.IndexOf(desired[index]);
            if (current < 0)
            {
                Scenes.Insert(index, desired[index]);
            }
            else if (current != index)
            {
                Scenes.Move(current, index);
            }
        }

        SelectedScene = Scenes.FirstOrDefault(info => info.Id == Scene?.SceneId);
    }

    partial void OnSelectedItemChanged(SceneTreeItem? value)
    {
        HasSelectionBounds = false;
        CanResizeSelection = value?.Model is ComponentSettings;
        OnPropertyChanged(nameof(SelectedModel));
        OnPropertyChanged(nameof(CanEditSelection));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

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

        return Flatten(Roots).FirstOrDefault(item => item.Model is LayerSettings);
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

                if (ReferenceEquals(sender, node.Model) && property == "Name")
                {
                    node.RefreshTitle();
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

        var selectedFeatures = (SelectedModel as IFeatureContainer)?.Features;
        if (ReferenceEquals(sender, selectedFeatures) || ReferenceEquals(sender, SelectedModel) && property is "Features" or "Settings" || sender is FeatureInfo info && selectedFeatures?.Contains(info) == true && property is "Id" or "Settings")
        {
            InspectorChanged?.Invoke(this, EventArgs.Empty);
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

    partial void OnIsPreviewChanged(bool value)
    {
        AddComponentCommand.NotifyCanExecuteChanged();
        RefreshViewProperties();
        OnPropertyChanged(nameof(ContentRow));
        OnPropertyChanged(nameof(ContentRowSpan));
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
        if (Scene == null || IsPreview || !SceneHierarchyEditing.Contains(Scene, parent.Model)
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
        if (Scene == null || IsPreview || FindItem(parentModel) == null)
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
        if (Scene == null || IsPreview || item.Parent is not { } sourceParent
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
    public object Model { get; } = model;
    public SceneTreeItem? Parent { get; } = parent;
    public int Depth => Parent == null ? 0 : Parent.Depth + 1;
    public ObservableCollection<SceneTreeItem> Children { get; } = [];
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
        _ => new FontIconSource
        {
            Glyph = Model is LayerSettings ? "\uE81E" : "\uE8B9",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16
        }
    };

    internal void RefreshTitle()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IconSource));
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

public sealed class InspectorSection
{
    private readonly IFeatureContainer? _owner;
    public InspectorSection(string title, UserControl? editor, object model, object? settings, IFeatureContainer? owner = null, FeatureInfo? feature = null)
    {
        Title = title;
        Editor = editor;
        Model = model;
        Settings = settings;
        Feature = feature;
        _owner = owner;
        RemoveCommand = new RelayCommand(RemoveFeature);
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
    public IRelayCommand RemoveCommand
    {
        get;
    }

    private void RemoveFeature()
    {
        if (_owner != null && Feature != null)
        {
            _owner.Features.Remove(Feature);
        }
    }
}
