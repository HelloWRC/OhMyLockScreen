using Windows.Graphics.Imaging;
using Windows.Storage;

namespace OhMyLockScreen.Shared.Helpers.UI;

internal static class SceneImageDecoder
{
    internal static async Task<(int Width, int Height)> ReadSizeAsync(Uri uri)
    {
        var file = await StorageFile.GetFileFromApplicationUriAsync(uri);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var decoded = await decoder.GetSoftwareBitmapAsync();
        var width = checked((int)decoder.OrientedPixelWidth);
        var height = checked((int)decoder.OrientedPixelHeight);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("无法读取图片尺寸。");
        }

        return (width, height);
    }
}
