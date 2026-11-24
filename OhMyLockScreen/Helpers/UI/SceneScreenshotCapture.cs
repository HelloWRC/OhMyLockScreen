using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;

namespace OhMyLockScreen.Helpers.UI;

/// <summary>以 16:9 标准比例截取场景真实渲染画面并存入场景 Blob 存储。</summary>
internal static class SceneScreenshotCapture
{
    private const double CaptureLayoutWidth = 1920;
    private const double CaptureLayoutHeight = 1080;
    private const int OutputWidth = 1280;
    private const int OutputHeight = 720;

    internal static async Task<string> Capture16By9Async(
        SceneSettings scene,
        ScenePresenter livePresenter,
        Panel captureHost,
        ISceneBlobStorageService blobs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(livePresenter);
        ArgumentNullException.ThrowIfNull(captureHost);
        ArgumentNullException.ThrowIfNull(blobs);

        var snapshot = ConfigureFileHelper.CopyObject(scene);
        SoftwareBitmap? videoFrame = null;
        if (snapshot.Background.Mode == BackgroundMode.Video)
        {
            videoFrame = await livePresenter.TryCaptureVideoFrameAsync(cancellationToken);
        }

        var capturePresenter = new ScenePresenter
        {
            Width = CaptureLayoutWidth,
            Height = CaptureLayoutHeight,
            IsEditing = false,
            SkipVideoPlayback = true,
            ShowSystemBackgroundPreview = snapshot.Background.Mode == BackgroundMode.None
                || (snapshot.Background.Mode == BackgroundMode.Video && videoFrame == null),
            IsHitTestVisible = false
        };

        if (capturePresenter.ShowSystemBackgroundPreview
            || (snapshot.Background.Mode == BackgroundMode.Image && string.IsNullOrWhiteSpace(snapshot.Background.ImageBlobId)))
        {
            capturePresenter.SetPreloadedSystemWallpaper(livePresenter.CurrentSystemWallpaper);
        }

        capturePresenter.Scene = snapshot;
        Canvas.SetLeft(capturePresenter, 0);
        Canvas.SetTop(capturePresenter, 0);
        captureHost.Children.Add(capturePresenter);

        try
        {
            capturePresenter.UpdateLayout();
            await WaitRenderFramesAsync(2, cancellationToken);
            await WaitPendingBitmapsAsync(capturePresenter, cancellationToken);
            capturePresenter.UpdateLayout();
            await WaitRenderFramesAsync(2, cancellationToken);

            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(capturePresenter, OutputWidth, OutputHeight);
            if (rtb.PixelWidth <= 0 || rtb.PixelHeight <= 0)
            {
                throw new InvalidOperationException("场景渲染输出尺寸为空，无法生成预览图。");
            }

            var xamlBuffer = await rtb.GetPixelsAsync();
            var xamlBytes = xamlBuffer.ToArray();

            using var pngStream = new MemoryStream();
            if (videoFrame != null)
            {
                await EncodeVideoCompositePngAsync(videoFrame, xamlBytes, rtb.PixelWidth, rtb.PixelHeight, pngStream);
            }
            else
            {
                await EncodeXamlPngAsync(xamlBytes, rtb.PixelWidth, rtb.PixelHeight, pngStream);
            }

            pngStream.Position = 0;
            return await blobs.ImportAsync(scene.SceneId, pngStream, cancellationToken);
        }
        finally
        {
            captureHost.Children.Remove(capturePresenter);
            videoFrame?.Dispose();
        }
    }

    private static async Task EncodeVideoCompositePngAsync(
        SoftwareBitmap videoFrame,
        byte[] xamlBytes,
        int xamlWidth,
        int xamlHeight,
        Stream destination)
    {
        var device = CanvasDevice.GetSharedDevice();
        using var target = new CanvasRenderTarget(device, OutputWidth, OutputHeight, 96);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Colors.Black);
            using var videoBitmap = CanvasBitmap.CreateFromSoftwareBitmap(device, videoFrame);
            DrawUniformToFill(session, videoBitmap, OutputWidth, OutputHeight);

            using var xamlBitmap = CanvasBitmap.CreateFromBytes(
                device,
                xamlBytes,
                xamlWidth,
                xamlHeight,
                DirectXPixelFormat.B8G8R8A8UIntNormalized);
            session.DrawImage(
                xamlBitmap,
                new Rect(0, 0, OutputWidth, OutputHeight),
                new Rect(0, 0, xamlWidth, xamlHeight));
        }

        using var raStream = new InMemoryRandomAccessStream();
        await target.SaveAsync(raStream, CanvasBitmapFileFormat.Png);
        raStream.Seek(0);
        await using var readStream = raStream.AsStreamForRead();
        await readStream.CopyToAsync(destination);
    }

    private static void DrawUniformToFill(
        CanvasDrawingSession session,
        CanvasBitmap bitmap,
        double targetWidth,
        double targetHeight)
    {
        var sourceWidth = bitmap.Size.Width;
        var sourceHeight = bitmap.Size.Height;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return;
        }

        var scale = Math.Max(targetWidth / sourceWidth, targetHeight / sourceHeight);
        var cropWidth = targetWidth / scale;
        var cropHeight = targetHeight / scale;
        var cropX = (sourceWidth - cropWidth) / 2;
        var cropY = (sourceHeight - cropHeight) / 2;

        session.DrawImage(
            bitmap,
            new Rect(0, 0, targetWidth, targetHeight),
            new Rect(cropX, cropY, cropWidth, cropHeight));
    }

    private static async Task EncodeXamlPngAsync(byte[] xamlBytes, int width, int height, Stream destination)
    {
        using var raStream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, raStream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            (uint)width,
            (uint)height,
            96,
            96,
            xamlBytes);

        if (width != OutputWidth || height != OutputHeight)
        {
            encoder.BitmapTransform.ScaledWidth = (uint)OutputWidth;
            encoder.BitmapTransform.ScaledHeight = (uint)OutputHeight;
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        }

        await encoder.FlushAsync();
        raStream.Seek(0);
        await using var readStream = raStream.AsStreamForRead();
        await readStream.CopyToAsync(destination);
    }

    private static async Task WaitRenderFramesAsync(int count, CancellationToken cancellationToken)
    {
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tcs = new TaskCompletionSource();
            void OnRendering(object? sender, object args)
            {
                CompositionTarget.Rendering -= OnRendering;
                tcs.TrySetResult();
            }

            CompositionTarget.Rendering += OnRendering;
            try
            {
                using var registration = cancellationToken.Register(() =>
                {
                    CompositionTarget.Rendering -= OnRendering;
                    tcs.TrySetCanceled(cancellationToken);
                });
                await tcs.Task;
            }
            catch
            {
                CompositionTarget.Rendering -= OnRendering;
                throw;
            }
        }
    }

    private static async Task WaitPendingBitmapsAsync(DependencyObject root, CancellationToken cancellationToken)
    {
        var pendingBitmaps = CollectPendingBitmaps(root).ToArray();
        if (pendingBitmaps.Length == 0)
        {
            return;
        }

        var waiters = new List<Task>(pendingBitmaps.Length);
        var cleanups = new List<Action>(pendingBitmaps.Length);
        foreach (var bitmap in pendingBitmaps)
        {
            if (bitmap.PixelWidth > 0)
            {
                continue;
            }

            var tcs = new TaskCompletionSource();
            void OnOpened(object sender, RoutedEventArgs args) => tcs.TrySetResult();
            void OnFailed(object sender, ExceptionRoutedEventArgs args) => tcs.TrySetResult();

            bitmap.ImageOpened += OnOpened;
            bitmap.ImageFailed += OnFailed;
            cleanups.Add(() =>
            {
                bitmap.ImageOpened -= OnOpened;
                bitmap.ImageFailed -= OnFailed;
            });
            waiters.Add(tcs.Task);
        }

        if (waiters.Count == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAny(
                Task.WhenAll(waiters),
                Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            foreach (var cleanup in cleanups)
            {
                cleanup();
            }
        }
    }

    private static IEnumerable<BitmapImage> CollectPendingBitmaps(DependencyObject parent)
    {
        switch (parent)
        {
            case Image { Source: BitmapImage imageBitmap } when imageBitmap.UriSource != null:
                yield return imageBitmap;
                break;
            case Border { Background: ImageBrush { ImageSource: BitmapImage borderBitmap } } when borderBitmap.UriSource != null:
                yield return borderBitmap;
                break;
            case Panel { Background: ImageBrush { ImageSource: BitmapImage panelBitmap } } when panelBitmap.UriSource != null:
                yield return panelBitmap;
                break;
            case Shape { Fill: ImageBrush { ImageSource: BitmapImage shapeBitmap } } when shapeBitmap.UriSource != null:
                yield return shapeBitmap;
                break;
        }

        var childrenCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childrenCount; index++)
        {
            foreach (var nested in CollectPendingBitmaps(VisualTreeHelper.GetChild(parent, index)))
            {
                yield return nested;
            }
        }
    }
}
