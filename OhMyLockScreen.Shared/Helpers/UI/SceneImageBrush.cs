using OhMyLockScreen.Shared.Controls.Scene;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace OhMyLockScreen.Shared.Helpers.UI;

internal static class SceneImageBrush
{
    public static readonly DependencyProperty BlobIdProperty = DependencyProperty.RegisterAttached(
        "BlobId", typeof(string), typeof(SceneImageBrush), new PropertyMetadata(null));

    public static string? GetBlobId(ImageBrush brush) => (string?)brush.GetValue(BlobIdProperty);

    public static void SetBlobId(ImageBrush brush, string? blobId) => brush.SetValue(BlobIdProperty, blobId);

    internal static Brush? Resolve(Brush? brush, SceneBlobUriConverter? converter)
    {
        if (brush is not ImageBrush image || GetBlobId(image) is not { } blobId)
        {
            return brush;
        }

        var uri = converter?.Resolve(blobId);
        var resolved = (ImageBrush)BrushEditorOperations.Copy(image);
        resolved.ImageSource = uri == null ? null : new BitmapImage(uri);
        return resolved;
    }
}
