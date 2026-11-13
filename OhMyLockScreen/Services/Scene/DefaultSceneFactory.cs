using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.ApplicationModel;

namespace OhMyLockScreen.Services.Scene;

internal static class DefaultSceneFactory
{
    internal static SceneSettings Create(string name)
    {
        var path = Path.Combine(Package.Current.InstalledLocation.Path, "Assets", "DefaultScene.json");
        using var template = File.OpenRead(path);
        var scene = ConfigureFileHelper.LoadConfigUnWrapped<SceneSettings>(template);
        scene.Name = name;
        return scene;
    }
}
