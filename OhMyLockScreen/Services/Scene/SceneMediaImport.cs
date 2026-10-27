using OhMyLockScreen.Shared.Helpers.UI;

namespace OhMyLockScreen.Services.Scene;

internal static class SceneMediaImport
{
    internal static IReadOnlyList<string> VideoExtensions { get; } = Array.AsReadOnly(
        new[] { ".mp4", ".wmv", ".mov", ".m4v", ".avi", ".mkv", ".webm", ".ts", ".m2ts", ".mpg", ".mpeg", ".flv", ".ogv" });

    internal static async Task<SceneMediaImportResult> RunAsync<T>(
        IEnumerable<T> files,
        Func<T, string> getName,
        Func<T, Task<bool>> importImage,
        Func<T, Task<bool>> importVideo,
        Func<bool> isCurrent)
    {
        var result = new SceneMediaImportResult();
        var videoSeen = false;
        foreach (var file in files)
        {
            if (!isCurrent())
            {
                break;
            }

            var name = getName(file);
            var extension = Path.GetExtension(name);
            var image = BrushImageImport.IsSupported(extension);
            if (!image)
            {
                if (!VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    result.UnsupportedFiles.Add(name);
                    continue;
                }

                if (videoSeen)
                {
                    result.SkippedVideos++;
                    continue;
                }

                videoSeen = true;
            }

            try
            {
                var imported = await (image ? importImage(file) : importVideo(file));
                if (!isCurrent())
                {
                    break;
                }

                if (!imported)
                {
                    result.FailedFiles.Add($"{name}：导入目标已失效。");
                }
                else if (!image)
                {
                    result.VideoName = name;
                }
            }
            catch (Exception exception)
            {
                if (!isCurrent())
                {
                    break;
                }

                result.FailedFiles.Add($"{name}：{exception.Message}");
            }
        }

        return result;
    }

    internal static async Task<bool> ImportVideoAsync(
        Func<Task<Stream>> openSource,
        string extension,
        string sceneDirectory,
        Func<bool> isCurrent,
        Action<string> publish)
    {
        if (!isCurrent())
        {
            return false;
        }

        if (!VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("不支持的视频格式。", nameof(extension));
        }

        var directory = Path.Combine(sceneDirectory, "Assets");
        Directory.CreateDirectory(directory);
        var name = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var path = Path.Combine(directory, name);
        var committed = false;
        try
        {
            using (var source = await openSource())
            using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await source.CopyToAsync(destination);
            }

            if (!isCurrent())
            {
                return false;
            }

            publish($"Assets/{name}");
            committed = true;
            return true;
        }
        finally
        {
            if (!committed && File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

internal sealed class SceneMediaImportResult
{
    internal string? VideoName { get; set; }
    internal int SkippedVideos { get; set; }
    internal List<string> UnsupportedFiles { get; } = [];
    internal List<string> FailedFiles { get; } = [];
}
