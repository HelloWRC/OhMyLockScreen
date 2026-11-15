using System.Diagnostics;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.System.UserProfile;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace OhMyLockScreen.Shared.Controls.Scene;

public sealed partial class ScenePresenter
{
    internal bool ShowSystemBackgroundPreview { get; set; }

    private Window? _systemBackgroundWindow;
    private bool _systemBackgroundWindowActive;
    private long _systemBackgroundRequest;
    private BitmapImage? _systemWallpaper;
    private BitmapImage? _customWallpaper;

    private bool UsesSystemWallpaper => Scene?.Background is { Mode: BackgroundMode.Image } background
        && string.IsNullOrWhiteSpace(background.ImageBlobId);

    private void UpdateSystemBackgroundSubscription()
    {
        if (!ShowSystemBackgroundPreview && !UsesSystemWallpaper)
        {
            DetachSystemBackground();
            return;
        }

        if (_systemBackgroundWindow == null)
        {
            _systemBackgroundWindow = Window.Current;
            _systemBackgroundWindow.Activated += OnSystemBackgroundWindowActivated;
            _systemBackgroundWindowActive = true;
            _ = RefreshSystemBackgroundAsync();
        }

        UpdateSystemBackgroundPreview();
    }

    private void DetachSystemBackground()
    {
        if (_systemBackgroundWindow == null)
        {
            return;
        }

        _systemBackgroundWindow.Activated -= OnSystemBackgroundWindowActivated;
        _systemBackgroundWindow = null;
        _systemBackgroundRequest++;
        _systemWallpaper = null;
        SystemBackgroundImageElement.Source = null;
    }

    private void OnSystemBackgroundWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        var active = args.WindowActivationState != CoreWindowActivationState.Deactivated;
        var reactivated = active && !_systemBackgroundWindowActive;
        _systemBackgroundWindowActive = active;
        if (reactivated)
        {
            _ = RefreshSystemBackgroundAsync();
        }
    }

    private async Task RefreshSystemBackgroundAsync()
    {
        var request = ++_systemBackgroundRequest;
        BitmapImage? wallpaper = null;
        try
        {
            using var stream = LockScreen.GetImageStream();
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            wallpaper = image;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法读取系统锁屏图像：{exception}");
        }

        await ApplyBackgroundUpdateAsync(() =>
        {
            if (request != _systemBackgroundRequest)
            {
                return;
            }

            _systemWallpaper = wallpaper;
            UpdateSystemBackgroundPreview();
            if (UsesSystemWallpaper)
            {
                WallpaperImage.Source = wallpaper;
            }
        });
    }

    private void UpdateSystemBackgroundPreview() =>
        SystemBackgroundImageElement.Source = ShowSystemBackgroundPreview && !UsesSystemWallpaper ? _systemWallpaper : null;

    private void UpdateWallpaperImage(string? blobId)
    {
        if (string.IsNullOrWhiteSpace(blobId))
        {
            if (_videoView != null || _customWallpaper != null)
            {
                ReleaseBackground();
            }

            WallpaperImage.Source = _systemWallpaper;
            return;
        }

        try
        {
            var uri = BlobUriConverter?.Resolve(blobId) ?? throw new FileNotFoundException("背景图片资产不存在。");
            if (_customWallpaper?.UriSource == uri)
            {
                return;
            }

            ReleaseBackground();
            var image = new BitmapImage(uri);
            _customWallpaper = image;
            image.ImageFailed += OnWallpaperImageFailed;
            WallpaperImage.Source = image;
        }
        catch (Exception exception)
        {
            FailBackground($"无法加载背景图片：{exception.Message}");
        }
    }

    private void OnWallpaperImageFailed(object sender, ExceptionRoutedEventArgs args)
    {
        if (ReferenceEquals(sender, _customWallpaper))
        {
            FailBackground($"无法加载背景图片：{args.ErrorMessage}");
        }
    }

    private void ClearWallpaperImage()
    {
        if (_customWallpaper is { } image)
        {
            image.ImageFailed -= OnWallpaperImageFailed;
            _customWallpaper = null;
        }

        WallpaperImage.Source = null;
    }
}
