using Microsoft.Extensions.Hosting;
using OhMyLockScreen;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Helpers;
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
        LockScreenHosting.IsHosted = () => true;
        CoreApplication.ExitCalls = 0;
        Storyboard.LastCreated = null;
        Storyboard.FailOnBegin = false;
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
