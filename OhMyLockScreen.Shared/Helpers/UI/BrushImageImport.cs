namespace OhMyLockScreen.Shared.Helpers.UI;

/// <summary>Copies and validates an image before publishing its persistent URI.</summary>
internal static class BrushImageImport
{
    internal static IReadOnlyList<string> Extensions { get; } = Array.AsReadOnly(
        new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" });

    internal static bool IsSupported(string extension) => Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    internal static async Task<bool> RunAsync(
        Func<Task<Stream>> openSource,
        string extension,
        string directory,
        Func<string, Task> validate,
        Func<bool> isCurrent,
        Action<Uri> publish)
    {
        if (!isCurrent())
        {
            return false;
        }
        if (!IsSupported(extension))
        {
            throw new ArgumentException("不支持的图片格式。", nameof(extension));
        }
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
            await validate(path);
            if (!isCurrent())
            {
                return false;
            }
            publish(new Uri($"ms-appdata:///local/BrushAssets/{name}"));
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
