using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace OhMyLockScreen.Shared.Controls.Scene;

public interface ISceneBlobContext
{
    SceneBlobUriConverter? BlobUriConverter { get; }
}

public sealed class SceneBlobUriConverter(ISceneBlobStorageService storage, Guid sceneId) : IValueConverter
{
    public Guid SceneId { get; } = sceneId;

    public Uri? Resolve(string? blobId) => storage.GetBlobUri(SceneId, blobId);

    internal Task<string> ImportAsync(Stream source, CancellationToken cancellationToken) =>
        storage.ImportAsync(SceneId, source, cancellationToken);

    public object Convert(object value, Type targetType, object parameter, string language) =>
        Resolve(value as string)!;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        DependencyProperty.UnsetValue;
}
