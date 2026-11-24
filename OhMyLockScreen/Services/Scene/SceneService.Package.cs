using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Models.Scene;

namespace OhMyLockScreen.Services.Scene;

public sealed partial class SceneService
{
    private const int MaxPackageSceneJsonBytes = 16 * 1024 * 1024;
    private const string PackageStreamTemporaryPrefix = ".omls-stream-";

    public async Task ExportAsync(Guid id, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("目标流不可写。", nameof(destination));
        }

        cancellationToken.ThrowIfCancellationRequested();
        FlushPending();

        byte[] sceneBytes;
        List<(string EntryPath, string FilePath)> files;
        lock (_sync)
        {
            RequireScene(id);
            var directory = GetDirectory(id);
            sceneBytes = _pending.TryGetValue(id, out var pending)
                ? pending
                : File.ReadAllBytes(GetPath(id));
            files = CollectExportFiles(directory);
        }

        await using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var sceneEntry = archive.CreateEntry("scene.json", CompressionLevel.Optimal);
        await using (var entryStream = await sceneEntry.OpenAsync(cancellationToken).ConfigureAwait(false))
        {
            await entryStream.WriteAsync(sceneBytes, cancellationToken).ConfigureAwait(false);
        }

        foreach (var (entryPath, filePath) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.CreateEntry(entryPath, CompressionLevel.NoCompression);
            await using var sourceStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var entryStream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
            await sourceStream.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<SceneSettings> ImportAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("源流不可读。", nameof(source));
        }

        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            EnsureReady();
        }

        Directory.CreateDirectory(ScenesPath);
        string? bufferedPackagePath = null;
        string? stagingDirectory = null;
        try
        {
            Stream archiveStream = source;
            if (!source.CanSeek)
            {
                bufferedPackagePath = Path.Combine(ScenesPath, $"{PackageStreamTemporaryPrefix}{Guid.NewGuid():N}.tmp");
                archiveStream = await BufferPackageStreamAsync(source, bufferedPackagePath, cancellationToken);
            }

            try
            {
                await using var archive = await ZipArchive.CreateAsync(
                    archiveStream,
                    ZipArchiveMode.Read,
                    leaveOpen: true,
                    entryNameEncoding: null,
                    cancellationToken);

                var (sceneEntry, resourceEntries) = CollectImportEntries(archive);
                var sceneBytes = await ReadSceneBytesFromEntryAsync(sceneEntry, cancellationToken);

                // SceneSettings contains UI-thread-affine XAML brushes (SolidColorBrush, AcrylicBrush, ImageBrush);
                // deserialize and snapshot on the caller's synchronization context.
                var scene = DeserializeImportedScene(sceneBytes);
                var newId = Guid.NewGuid();
                scene.SceneId = newId;
                scene.Name = string.IsNullOrWhiteSpace(scene.Name) ? "导入的场景" : scene.Name.Trim();
                var snapshot = Serialize(scene);
                var info = CreateInfo(scene);

                stagingDirectory = Path.Combine(ScenesPath, $"{SceneImportTemporaryPrefix}{Guid.NewGuid():N}");
                await StageImportedSceneAsync(stagingDirectory, resourceEntries, snapshot, cancellationToken);

                lock (_sync)
                {
                    EnsureReady();
                    var targetDirectory = GetDirectory(newId);
                    Directory.Move(stagingDirectory, targetDirectory);
                    stagingDirectory = null;
                    _savedSnapshots[newId] = snapshot;
                    _scenes.Add(newId, info);
                }

                SceneSaved?.Invoke(this, new SceneChangedEventArgs(newId));
                return scene;
            }
            finally
            {
                if (!ReferenceEquals(archiveStream, source))
                {
                    await archiveStream.DisposeAsync();
                }
            }
        }
        finally
        {
            if (stagingDirectory != null && Directory.Exists(stagingDirectory))
            {
                try
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            if (bufferedPackagePath != null && File.Exists(bufferedPackagePath))
            {
                try
                {
                    File.Delete(bufferedPackagePath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static async Task<Stream> BufferPackageStreamAsync(
        Stream source,
        string bufferedPackagePath,
        CancellationToken cancellationToken)
    {
        await using (var bufferFile = new FileStream(
            bufferedPackagePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous))
        {
            await source.CopyToAsync(bufferFile, cancellationToken).ConfigureAwait(false);
        }

        return new FileStream(
            bufferedPackagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    internal static (ZipArchiveEntry SceneEntry, List<(ZipArchiveEntry Entry, string[] Segments, bool IsBlob)> ResourceEntries) CollectImportEntries(
        ZipArchive archive)
    {
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ZipArchiveEntry? sceneEntry = null;
        var resourceEntries = new List<(ZipArchiveEntry Entry, string[] Segments, bool IsBlob)>();

        foreach (var entry in archive.Entries)
        {
            var normalized = NormalizeArchivePath(entry.FullName, out var isDirectory);
            if (isDirectory)
            {
                continue;
            }

            if (!seenPaths.Add(normalized))
            {
                throw new InvalidDataException($"场景包中包含重复文件：{entry.FullName}");
            }

            var segments = normalized.Split('/');
            if (IsIgnoredPackageEntry(segments))
            {
                continue;
            }

            if (segments.Length == 1 && segments[0].Equals("scene.json", StringComparison.OrdinalIgnoreCase))
            {
                sceneEntry = entry;
                continue;
            }

            var isBlob = segments[0].Equals("Blobs", StringComparison.OrdinalIgnoreCase);
            if (isBlob)
            {
                ValidateBlobEntrySegments(segments, entry.FullName);
            }

            resourceEntries.Add((entry, segments, isBlob));
        }

        if (sceneEntry == null)
        {
            throw new InvalidDataException("场景包缺少 scene.json 文件。");
        }

        return (sceneEntry, resourceEntries);
    }

    private static List<(string EntryPath, string FilePath)> CollectExportFiles(string sceneDirectory)
    {
        var files = new List<(string EntryPath, string FilePath)>();
        CollectFilesRecursive(sceneDirectory, sceneDirectory, files);
        files.Sort((left, right) => StringComparer.Ordinal.Compare(left.EntryPath, right.EntryPath));
        return files;
    }

    private static void CollectFilesRecursive(
        string sceneDirectory,
        string currentDirectory,
        List<(string EntryPath, string FilePath)> files)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(currentDirectory).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(entry);
            if (ShouldSkipExportEntry(sceneDirectory, currentDirectory, name))
            {
                continue;
            }

            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("无法导出场景中的链接资源。");
            }

            if (Directory.Exists(entry))
            {
                CollectFilesRecursive(sceneDirectory, entry, files);
            }
            else
            {
                var relativePath = Path.GetRelativePath(sceneDirectory, entry).Replace('\\', '/');
                files.Add((relativePath, entry));
            }
        }
    }

    private static bool ShouldSkipExportEntry(string sceneDirectory, string currentDirectory, string name)
    {
        if (string.Equals(sceneDirectory, currentDirectory, StringComparison.OrdinalIgnoreCase)
            && name is "scene.json" or "scene.json.bak" or "scene.json.tmp")
        {
            return true;
        }

        return name.StartsWith(SceneBlobStorageService.TemporaryFilePrefix, StringComparison.Ordinal);
    }

    private static string NormalizeArchivePath(string fullName, out bool isDirectory)
    {
        var normalized = fullName.Replace('\\', '/');
        isDirectory = normalized.EndsWith('/');
        if (isDirectory)
        {
            normalized = normalized.TrimEnd('/');
            if (normalized.Length == 0)
            {
                return string.Empty;
            }
        }

        if (normalized.Length == 0 || normalized.StartsWith('/') || normalized.Contains('\0') || Path.IsPathRooted(normalized))
        {
            throw new InvalidDataException($"场景包路径无效：{fullName}");
        }

        var segments = normalized.Split('/');
        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment)
                || segment is "." or ".."
                || segment.IndexOf(':') >= 0
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidDataException($"场景包路径包含非法片段：{fullName}");
            }
        }

        return normalized;
    }

    private static bool IsIgnoredPackageEntry(string[] segments)
    {
        if (segments[0].Equals("__MACOSX", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (segments.Length == 1 && segments[0].ToLowerInvariant() is "scene.json.bak" or "scene.json.tmp")
        {
            return true;
        }

        return segments[^1].StartsWith(SceneBlobStorageService.TemporaryFilePrefix, StringComparison.Ordinal);
    }

    private static void ValidateBlobEntrySegments(string[] segments, string originalName)
    {
        if (segments.Length != 3
            || segments[1].Length != 2
            || segments[2].Length != 64
            || !segments[1].All(char.IsAsciiHexDigit)
            || !segments[2].All(char.IsAsciiHexDigit)
            || !segments[1].Equals(segments[2][..2], StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"场景包中的资源路径无效：{originalName}");
        }
    }

    internal static async Task<byte[]> ReadSceneBytesFromEntryAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using var entryStream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        var totalBytes = 0;
        int read;
        while ((read = await entryStream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            totalBytes += read;
            if (totalBytes > MaxPackageSceneJsonBytes)
            {
                throw new InvalidDataException("场景包中的 scene.json 体积过大。");
            }

            buffer.Write(chunk, 0, read);
        }

        var sceneBytes = buffer.ToArray();
        using var document = JsonDocument.Parse(sceneBytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("场景包中的 scene.json 格式无效。");
        }

        return sceneBytes;
    }

    private static SceneSettings DeserializeImportedScene(byte[] sceneBytes)
    {
        using var buffer = new MemoryStream(sceneBytes, writable: false);
        return ConfigureFileHelper.LoadConfigUnWrapped<SceneSettings>(buffer);
    }

    private static async Task StageImportedSceneAsync(
        string stagingDirectory,
        List<(ZipArchiveEntry Entry, string[] Segments, bool IsBlob)> resourceEntries,
        byte[] snapshot,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stagingDirectory);
        var stagingRoot = Path.GetFullPath(stagingDirectory) + Path.DirectorySeparatorChar;

        foreach (var (entry, segments, isBlob) in resourceEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isBlob)
            {
                await ExtractVerifiedBlobAsync(entry, segments, stagingDirectory, stagingRoot, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await ExtractRegularFileAsync(entry, segments, stagingDirectory, stagingRoot, cancellationToken).ConfigureAwait(false);
            }
        }

        await File.WriteAllBytesAsync(Path.Combine(stagingDirectory, "scene.json"), snapshot, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExtractVerifiedBlobAsync(
        ZipArchiveEntry entry,
        string[] segments,
        string stagingDirectory,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        var shard = segments[1].ToLowerInvariant();
        var hash = segments[2].ToLowerInvariant();
        var destinationPath = ResolveStagingPath(stagingDirectory, stagingRoot, "Blobs", shard, hash);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var entryStream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var destinationStream = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous);
        var buffer = new byte[81920];
        int read;
        while ((read = await entryStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hasher.AppendData(buffer, 0, read);
            await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        var actualHash = Convert.ToHexStringLower(hasher.GetHashAndReset());
        if (!string.Equals(actualHash, hash, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"场景包中的资源校验失败：{entry.FullName}");
        }
    }

    private static async Task ExtractRegularFileAsync(
        ZipArchiveEntry entry,
        string[] segments,
        string stagingDirectory,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        var destinationPath = ResolveStagingPath(stagingDirectory, stagingRoot, segments);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        await using var entryStream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var destinationStream = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous);
        await entryStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveStagingPath(string stagingDirectory, string stagingRoot, params string[] segments)
    {
        var combined = Path.GetFullPath(Path.Combine([stagingDirectory, .. segments]));
        if (!combined.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("场景包文件路径超出临时目录。");
        }

        return combined;
    }
}
