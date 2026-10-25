using OhMyLockScreen.Shared.Models.Scene;

namespace OhMyLockScreen.Shared.Abstractions.Services.Scene;

public interface ISceneService
{
    string ScenesPath
    {
        get;
    }

    Guid ActiveSceneId
    {
        get;
    }

    IReadOnlyList<string> LoadErrors
    {
        get;
    }

    event EventHandler<SceneChangedEventArgs>? SceneSaved;
    event EventHandler<SceneChangedEventArgs>? ActiveSceneChanged;
    event EventHandler<SceneChangedEventArgs>? SaveFailed;
    void Initialize();
    void Reload();
    IReadOnlyList<SceneInfo> GetScenes();
    string GetSceneDirectory(Guid id);
    bool HasPendingChanges(Guid id);
    Exception? GetSaveError(Guid id);
    SceneSettings Load(Guid id);
    SceneSettings Create(string name);
    SceneSettings Duplicate(Guid id);
    void Delete(Guid id);
    void SetActive(Guid id);
    void QueueSave(SceneSettings scene);
    void FlushPending();
}
