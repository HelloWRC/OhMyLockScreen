using System.Diagnostics;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Storage;

namespace OhMyLockScreen.Shared.Controls.Scene;

public sealed partial class ScenePresenter
{
    /// <summary>尝试截取当前正在播放的背景视频帧用于生成场景预览图。</summary>
    internal async Task<SoftwareBitmap?> TryCaptureVideoFrameAsync(CancellationToken cancellationToken = default)
    {
        var player = _backgroundPlayer;
        var path = _backgroundPath;
        var hasVout = player is { VoutCount: > 0 };
        var timeMs = 0L;
        if (player != null)
        {
            try
            {
                timeMs = Math.Max(0, player.Time);
            }
            catch (ObjectDisposedException)
            {
                player = null;
                hasVout = false;
            }
        }

        if (player != null && hasVout)
        {
            var snapshot = await TryTakeVlcSnapshotAsync(player, cancellationToken).ConfigureAwait(false);
            if (snapshot != null)
            {
                return snapshot;
            }
        }

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            return await TryExtractMediaThumbnailAsync(path, TimeSpan.FromMilliseconds(timeMs), cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<SoftwareBitmap?> TryTakeVlcSnapshotAsync(
        LibVLCSharp.Shared.MediaPlayer player,
        CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(ApplicationData.Current.TemporaryFolder.Path, $"omls-snap-{Guid.NewGuid():N}.png");
        try
        {
            var requested = await Task.Run(() =>
            {
                try
                {
                    return player.TakeSnapshot(0, tempPath, 0, 0);
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"LibVLC 截图失败：{exception.Message}");
                    return false;
                }
            }, cancellationToken).ConfigureAwait(false);

            if (!requested)
            {
                return null;
            }

            for (var attempt = 0; attempt < 20; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(tempPath))
                {
                    try
                    {
                        await using var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        if (stream.Length > 0)
                        {
                            using var raStream = stream.AsRandomAccessStream();
                            var decoder = await BitmapDecoder.CreateAsync(raStream);
                            return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                        }
                    }
                    catch (IOException)
                    {
                    }
                }

                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"读取视频截图失败：{exception.Message}");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        return null;
    }

    private static async Task<SoftwareBitmap?> TryExtractMediaThumbnailAsync(
        string path,
        TimeSpan position,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(path);
            var clip = await MediaClip.CreateFromFileAsync(file);
            if (clip.OriginalDuration > TimeSpan.Zero && position >= clip.OriginalDuration)
            {
                position = TimeSpan.Zero;
            }

            var composition = new MediaComposition();
            composition.Clips.Add(clip);
            using var imageStream = await composition.GetThumbnailAsync(
                position,
                0,
                0,
                VideoFramePrecision.NearestFrame);
            var decoder = await BitmapDecoder.CreateAsync(imageStream);
            return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"提取视频缩略图失败：{exception.Message}");
            return null;
        }
    }
}
