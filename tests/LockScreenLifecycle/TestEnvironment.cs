using Microsoft.Extensions.Hosting;
using OhMyLockScreen;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Helpers;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Animation;

internal sealed class TestEnvironment : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("OhMyLockScreen.Tests.");

    internal TestEnvironment()
    {
        Application.Current = null;
        IAppHost.Host = null;
        ApplicationData.Current.LocalFolder.Path = directory.FullName;
        Window.Current = new Window();
        Windows.System.UserProfile.LockScreen.OpenImage =
            () => throw new InvalidOperationException("No simulated lock screen image was configured.");
        LockScreenHosting.IsHosted = () => true;
        CoreApplication.ExitCalls = 0;
        Storyboard.LastCreated = null;
        Storyboard.FailOnBegin = false;
        LibVLCSharp.Shared.MediaPlayer.FailOnPlay = false;
        OhMyLockScreen.Shared.Controls.Scene.VideoSwapChain.Created.Clear();
        OhMyLockScreen.Shared.Controls.Scene.VideoSwapChain.FailOnCreate = false;
        DispatcherTimer.Created.Clear();
        ConfigureFileHelper.Errors.Clear();
        ConfigureFileHelper.IsBackupEnabled = true;
    }

    public void Dispose()
    {
        var host = IAppHost.Host;
        IAppHost.Host = null;
        Application.Current = null;
        if (host != null)
        {
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            host.Dispose();
        }

        if (!directory.FullName.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to clean up a directory outside the test temp root.");
        directory.Delete(recursive: true);
    }
}
