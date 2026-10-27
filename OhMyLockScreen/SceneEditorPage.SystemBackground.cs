using System.Diagnostics;
using Windows.System.UserProfile;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;

namespace OhMyLockScreen;

public sealed partial class SceneEditorPage
{
    private int _systemBackgroundRequestVersion;
    private bool _systemBackgroundWindowActive;

    private void AttachSystemBackground()
    {
        _window!.Activated += OnSystemBackgroundWindowActivated;
        _systemBackgroundWindowActive = true;
        _ = RefreshSystemBackgroundAsync();
    }

    private void DetachSystemBackground()
    {
        if (_window != null)
        {
            _window.Activated -= OnSystemBackgroundWindowActivated;
        }

        _systemBackgroundRequestVersion++;
        _systemBackgroundWindowActive = false;
        EditorPresenter.SystemBackgroundImage = null;
    }

    private void OnSystemBackgroundWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        var active = args.WindowActivationState != CoreWindowActivationState.Deactivated;
        if (active && !_systemBackgroundWindowActive)
        {
            _ = RefreshSystemBackgroundAsync();
        }

        _systemBackgroundWindowActive = active;
    }

    private async Task RefreshSystemBackgroundAsync()
    {
        var version = ++_systemBackgroundRequestVersion;
        try
        {
            using var stream = LockScreen.GetImageStream();
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            if (_loaded && version == _systemBackgroundRequestVersion)
            {
                EditorPresenter.SystemBackgroundImage = image;
            }
        }
        catch (Exception exception)
        {
            if (_loaded && version == _systemBackgroundRequestVersion)
            {
                EditorPresenter.SystemBackgroundImage = null;
            }

            Debug.WriteLine($"无法读取系统锁屏图像：{exception}");
        }
    }
}
