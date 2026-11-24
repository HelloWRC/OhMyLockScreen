using System.IO.Compression;
using System.Text.Json;
using OhMyLockScreen.Shared.Models.Scene;

namespace OhMyLockScreen.Services.Scene;

internal sealed record ScenePackageMetadata(string Name, string Description, string Author);

internal sealed record ScenePackageImportCandidate<T>(T File, string FileName, ScenePackageMetadata? Metadata);

internal static class ScenePackageImport
{
    internal const string PackageExtension = ".omls";

    internal static bool IsPackageFileName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && Path.GetExtension(name).Equals(PackageExtension, StringComparison.OrdinalIgnoreCase);

    internal static (List<T> Supported, List<string> UnsupportedNames) PartitionItems<T>(
        IEnumerable<T> items,
        Func<T, bool> isFile,
        Func<T, string> getName)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(isFile);
        ArgumentNullException.ThrowIfNull(getName);

        var supported = new List<T>();
        var unsupportedNames = new List<string>();
        foreach (var item in items)
        {
            var name = getName(item);
            if (isFile(item) && IsPackageFileName(name))
            {
                supported.Add(item);
            }
            else
            {
                unsupportedNames.Add(name);
            }
        }

        return (supported, unsupportedNames);
    }

    internal static async Task<ScenePackageMetadata?> TryReadMetadataAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || !source.CanSeek)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var archive = await ZipArchive.CreateAsync(
                source,
                ZipArchiveMode.Read,
                leaveOpen: true,
                entryNameEncoding: null,
                cancellationToken).ConfigureAwait(false);
            var (sceneEntry, _) = SceneService.CollectImportEntries(archive);
            var sceneBytes = await SceneService.ReadSceneBytesFromEntryAsync(sceneEntry, cancellationToken).ConfigureAwait(false);

            using var document = JsonDocument.Parse(sceneBytes);
            var root = document.RootElement;
            return new ScenePackageMetadata(
                ReadTrimmedString(root, nameof(SceneSettings.Name)),
                ReadTrimmedString(root, nameof(SceneSettings.Description)),
                ReadTrimmedString(root, nameof(SceneSettings.Author)));
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static (string Message, string Detail) FormatConfirmationPrompt<T>(
        IReadOnlyList<ScenePackageImportCandidate<T>> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException("至少需要一个待导入的场景包。", nameof(candidates));
        }

        if (candidates.Count == 1)
        {
            var candidate = candidates[0];
            if (!string.IsNullOrEmpty(candidate.Metadata?.Name))
            {
                var detailLines = new List<string> { $"文件：{candidate.FileName}" };
                if (!string.IsNullOrEmpty(candidate.Metadata.Author))
                {
                    detailLines.Add($"作者：{candidate.Metadata.Author}");
                }

                if (!string.IsNullOrEmpty(candidate.Metadata.Description))
                {
                    detailLines.Add(candidate.Metadata.Description);
                }

                return ($"确定要导入场景“{candidate.Metadata.Name}”吗？", string.Join(Environment.NewLine, detailLines));
            }

            return ($"确定要导入场景文件“{candidate.FileName}”吗？", string.Empty);
        }

        const int maxListed = 5;
        var lines = new List<string>(Math.Min(candidates.Count, maxListed) + 1);
        foreach (var candidate in candidates.Take(maxListed))
        {
            lines.Add(!string.IsNullOrEmpty(candidate.Metadata?.Name)
                ? $"• {candidate.Metadata.Name}（{candidate.FileName}）"
                : $"• {candidate.FileName}");
        }

        if (candidates.Count > maxListed)
        {
            lines.Add($"…以及其余 {candidates.Count - maxListed} 个文件");
        }

        return ($"确定要导入这 {candidates.Count} 个场景包吗？", string.Join(Environment.NewLine, lines));
    }

    private static string ReadTrimmedString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;
}
