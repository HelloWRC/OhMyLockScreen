using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Models.Scene;

namespace OhMyLockScreen.ViewModels;

public sealed partial class SceneManagerViewModel : ObservableObject
{
    private readonly ISceneService _scenes;
    private readonly ISceneBlobStorageService _blobs;
    private readonly Dictionary<Guid, SceneCardItem> _itemsById = [];
    private IReadOnlyList<SceneCardItem> _allItems = [];

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _sceneCountText = "0 个场景";

    [ObservableProperty]
    private string _dialogTitle = "";

    [ObservableProperty]
    private string _dialogName = "";

    [ObservableProperty]
    private string _dialogNameError = "";

    [ObservableProperty]
    private string _dialogMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDialogDetail))]
    private string _dialogDetail = "";

    public bool HasDialogDetail => !string.IsNullOrWhiteSpace(DialogDetail);

    public ObservableCollection<SceneCardItem> FilteredScenes { get; } = [];

    public SceneManagerViewModel(ISceneService scenes, ISceneBlobStorageService blobs)
    {
        _scenes = scenes;
        _blobs = blobs;
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    public void RefreshScenes()
    {
        var activeId = _scenes.ActiveSceneId;
        var sceneInfos = _scenes.GetScenes();
        var seen = new HashSet<Guid>(sceneInfos.Count);
        var nextAll = new List<SceneCardItem>(sceneInfos.Count);

        foreach (var info in sceneInfos)
        {
            seen.Add(info.Id);
            if (!_itemsById.TryGetValue(info.Id, out var item))
            {
                item = new SceneCardItem(info.Id);
                _itemsById.Add(info.Id, item);
            }

            item.Name = info.Name;
            item.Description = info.Description;
            item.Author = info.Author;
            item.License = info.License;
            item.PreviewImageBlobId = info.PreviewImageBlobId;
            item.PreviewImageUri = _blobs.GetBlobUri(info.Id, info.PreviewImageBlobId);
            item.IsActive = info.Id == activeId;
            nextAll.Add(item);
        }

        foreach (var staleId in _itemsById.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            _itemsById.Remove(staleId);
        }

        _allItems = nextAll;
        ApplyFilter();
    }

    public void SetActive(Guid id)
    {
        _scenes.SetActive(id);
        RefreshScenes();
    }

    public SceneSettings CreateScene(string name)
    {
        var created = _scenes.Create(name);
        RefreshScenes();
        return created;
    }

    public SceneSettings DuplicateScene(Guid id)
    {
        var copy = _scenes.Duplicate(id);
        RefreshScenes();
        return copy;
    }

    public void RenameScene(Guid id, string name)
    {
        var scene = _scenes.Load(id);
        scene.Name = name;
        _scenes.QueueSave(scene);
        _scenes.FlushPending();
        RefreshScenes();
    }

    public void DeleteScene(Guid id)
    {
        _scenes.Delete(id);
        RefreshScenes();
    }

    public Task ExportSceneAsync(Guid id, Stream destination, CancellationToken cancellationToken = default) =>
        _scenes.ExportAsync(id, destination, cancellationToken);

    public async Task<SceneSettings> ImportSceneAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var imported = await _scenes.ImportAsync(source, cancellationToken);
        RefreshScenes();
        return imported;
    }

    private void ApplyFilter()
    {
        var query = FilterText.Trim();
        var desired = string.IsNullOrEmpty(query)
            ? _allItems
            : _allItems.Where(item => MatchesFilter(item, query)).ToArray();

        var desiredSet = desired.ToHashSet();
        for (var index = FilteredScenes.Count - 1; index >= 0; index--)
        {
            if (!desiredSet.Contains(FilteredScenes[index]))
            {
                FilteredScenes.RemoveAt(index);
            }
        }

        for (var index = 0; index < desired.Count; index++)
        {
            var currentIndex = FilteredScenes.IndexOf(desired[index]);
            if (currentIndex < 0)
            {
                FilteredScenes.Insert(index, desired[index]);
            }
            else if (currentIndex != index)
            {
                FilteredScenes.Move(currentIndex, index);
            }
        }

        IsEmpty = FilteredScenes.Count == 0;
        SceneCountText = string.IsNullOrEmpty(query)
            ? $"{_allItems.Count} 个场景"
            : $"{FilteredScenes.Count} / {_allItems.Count} 个场景";
    }

    private static bool MatchesFilter(SceneCardItem item, string query) =>
        item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || item.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || item.Author.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || item.License.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
