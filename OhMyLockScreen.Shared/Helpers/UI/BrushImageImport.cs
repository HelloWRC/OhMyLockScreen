namespace OhMyLockScreen.Shared.Helpers.UI;

/// <summary>File extensions accepted by the scene image picker and drop handler.</summary>
internal static class BrushImageImport
{
    internal static IReadOnlyList<string> Extensions { get; } = Array.AsReadOnly(
        new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" });

    internal static bool IsSupported(string extension) => Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
}
