using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Shared.Models;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.UI.Xaml;

namespace OhMyLockScreen;

public sealed partial class App
{
    private const string EditorWindowRecoveryParameterGroupId = "EditorWindowRecovery";
    private static readonly TimeSpan EditorWindowProbeTimeout = TimeSpan.FromSeconds(2);
    private int _editorWindowRecoveryRequested;

    private void StartEditorWindowProbe(Window window, ViewActivation activation)
    {
        if (activation.EditorWindowProbe is not null || Volatile.Read(ref _editorWindowRecoveryRequested) != 0)
        {
            return;
        }

        var recoveryDirectory = Path.Combine(ApplicationData.Current.LocalFolder.Path, EditorWindowRecoveryProtocol.DirectoryName);
        var launchRequest = Path.Combine(recoveryDirectory, EditorWindowRecoveryProtocol.LaunchRequestName);
        var isRecoveryLaunch = File.Exists(launchRequest) &&
            DateTime.UtcNow - File.GetLastWriteTimeUtc(launchRequest) <= EditorWindowRecoveryProtocol.LaunchRequestLifetime;
        var elapsed = Stopwatch.StartNew();
        (string RootClass, int Cloaked)? lastShape = null;
        // Sample the CoreWindow on its owning UI thread while the desktop frame is being attached.
        var probe = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        activation.EditorWindowProbe = probe;
        probe.Tick += (_, _) =>
        {
            if (activation.EditorWindowProbe != probe || !probe.IsEnabled)
            {
                return;
            }

            if (_isExiting || activation.IsClosed)
            {
                StopEditorWindowProbe(activation);
                return;
            }

            if (WindowShapeProbe.Describe(window.CoreWindow) is { } shape)
            {
                lastShape = shape;
                if (shape.RootClass is WindowShapeProbe.ApplicationFrameClass or WindowShapeProbe.HostBridgeClass)
                {
                    if (isRecoveryLaunch && shape.RootClass == WindowShapeProbe.ApplicationFrameClass && File.Exists(launchRequest))
                    {
                        if (!window.Visible || shape.Cloaked != 0)
                        {
                            return;
                        }

                        try
                        {
                            File.WriteAllText(Path.Combine(recoveryDirectory, EditorWindowRecoveryProtocol.LaunchReadyName), string.Empty);
                        }
                        catch (IOException exception)
                        {
                            LogLifecycleError(exception, "无法报告场景编辑器窗口已就绪");
                        }
                    }

                    StopEditorWindowProbe(activation);
                    return;
                }
            }

            if (isRecoveryLaunch)
            {
                if (!File.Exists(launchRequest) || elapsed.Elapsed >= EditorWindowRecoveryProtocol.LaunchRequestLifetime)
                {
                    StopEditorWindowProbe(activation);
                }

                return;
            }

            if (elapsed.Elapsed < EditorWindowProbeTimeout)
            {
                return;
            }

            StopEditorWindowProbe(activation);
            _ = LaunchEditorWindowRecoveryAsync(lastShape);
        };
        probe.Start();
    }

    private static void StopEditorWindowProbe(ViewActivation activation)
    {
        activation.EditorWindowProbe?.Stop();
        activation.EditorWindowProbe = null;
    }

    private async Task LaunchEditorWindowRecoveryAsync((string RootClass, int Cloaked)? shape)
    {
        if (Interlocked.Exchange(ref _editorWindowRecoveryRequested, 1) != 0)
        {
            return;
        }

        string? request = null;
        try
        {
            _host?.Services.GetService<ILogger<App>>()?.LogWarning(
                "场景编辑器窗口状态异常，rootClass={RootClass} cloaked={Cloaked}", shape?.RootClass, shape?.Cloaked);
            var directory = Path.Combine(ApplicationData.Current.LocalFolder.Path, EditorWindowRecoveryProtocol.DirectoryName);
            Directory.CreateDirectory(directory);
            request = Path.Combine(directory, $"{Environment.ProcessId}-{Guid.NewGuid():N}{EditorWindowRecoveryProtocol.RequestExtension}");
            File.WriteAllText(request, string.Empty);
            await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync(
                EditorWindowRecoveryParameterGroupId);
        }
        catch (Exception exception)
        {
            if (request is not null)
            {
                try
                {
                    File.Delete(request);
                }
                catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine(cleanupException);
                }
            }

            Volatile.Write(ref _editorWindowRecoveryRequested, 0);
            LogLifecycleError(exception, "无法启动场景编辑器恢复程序");
            return;
        }

        await ExitAsync();
    }
}
