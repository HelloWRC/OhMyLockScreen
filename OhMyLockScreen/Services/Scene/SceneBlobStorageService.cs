using System.Buffers;
using System.Security.Cryptography;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;

namespace OhMyLockScreen.Services.Scene;

internal sealed class SceneBlobStorageService(SceneService scenes) : ISceneBlobStorageService
{
    internal const string TemporaryFilePrefix = ".blob-import-";

    public Uri? GetBlobUri(Guid sceneId, string? blobId)
    {
        if (sceneId == Guid.Empty || blobId is not { Length: 64 } || !blobId.All(char.IsAsciiHexDigit))
        {
            return null;
        }

        var hash = blobId.ToLowerInvariant();
        try
        {
            return scenes.WithSceneDirectory<Uri?>(sceneId, directory =>
                File.Exists(GetBlobPath(directory, hash))
                    ? new Uri($"ms-appdata:///local/Scenes/{sceneId:N}/Blobs/{hash[..2]}/{hash}")
                    : null);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<string> ImportAsync(Guid sceneId, Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        string? temporaryPath = null;
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            var destination = scenes.WithSceneDirectory(sceneId, directory =>
            {
                EnsureNotLink(Path.GetDirectoryName(directory)!);
                var path = Path.Combine(directory, $"{TemporaryFilePrefix}{Guid.NewGuid():N}.tmp");
                var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
                temporaryPath = path;
                return stream;
            });

            string hash;
            using (destination)
            using (var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                int count;
                while ((count = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
                {
                    sha256.AppendData(buffer, 0, count);
                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                hash = Convert.ToHexStringLower(sha256.GetHashAndReset());
            }

            return scenes.WithSceneDirectory(sceneId, directory =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(temporaryPath))
                {
                    throw new IOException("场景资产的临时文件已被移除。");
                }

                var path = GetBlobPath(directory, hash);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                try
                {
                    File.Move(temporaryPath!, path);
                    temporaryPath = null;
                }
                catch (IOException) when (File.Exists(path))
                {
                    // A separate process can publish the same content while this import is running.
                    EnsureNotLink(path);
                }

                return hash;
            });
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (temporaryPath != null && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string GetBlobPath(string directory, string hash)
    {
        EnsureNotLink(Path.GetDirectoryName(directory)!);
        var blobs = Path.Combine(directory, "Blobs");
        EnsureNotLink(blobs);
        var shard = Path.Combine(blobs, hash[..2]);
        EnsureNotLink(shard);
        var path = Path.Combine(shard, hash);
        EnsureNotLink(path);
        return path;
    }

    private static void EnsureNotLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("场景资产路径不能包含链接。");
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
