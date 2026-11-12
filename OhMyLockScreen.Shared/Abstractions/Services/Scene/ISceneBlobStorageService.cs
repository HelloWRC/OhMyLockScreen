namespace OhMyLockScreen.Shared.Abstractions.Services.Scene;

public interface ISceneBlobStorageService
{
    Uri? GetBlobUri(Guid sceneId, string? blobId);

    Task<string> ImportAsync(Guid sceneId, Stream source, CancellationToken cancellationToken = default);
}
