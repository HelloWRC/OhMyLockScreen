using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OhMyLockScreen.Shared.Models;
using Windows.ApplicationModel;
using Windows.Storage;

namespace OhMyLockScreen.NativeHost;

internal static partial class EditorWindowRecovery
{
    private const string DialogTitle = "OhMyLockScreen 场景编辑器";
    private const uint YesNo = 0x00000004;
    private const uint WarningIcon = 0x00000030;
    private const uint ErrorIcon = 0x00000010;
    private const uint DefaultSecondButton = 0x00000100;
    private const uint SetForeground = 0x00010000;
    private const uint Topmost = 0x00040000;
    private const int Yes = 6;
    private static readonly TimeSpan RequestLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(3);

    internal static async Task<int> RunAsync()
    {
        try
        {
            var package = Package.Current;
            using var dialogInstance = new Mutex(false, $@"Local\{package.Id.FamilyName}.EditorWindowRecovery", out var created);
            if (!created)
            {
                return 0;
            }

            var directory = Path.Combine(ApplicationData.Current.LocalFolder.Path, EditorWindowRecoveryProtocol.DirectoryName);
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            var requests = new DirectoryInfo(directory)
                .EnumerateFiles("*" + EditorWindowRecoveryProtocol.RequestExtension)
                .OrderByDescending(request => request.LastWriteTimeUtc);
            foreach (var request in requests)
            {
                var requestedAt = request.LastWriteTimeUtc;
                request.Delete();
                if (DateTime.UtcNow - requestedAt > RequestLifetime || GetRequestProcessId(request.Name) is not { } processId)
                {
                    continue;
                }

                return await RecoverAsync(directory, processId, requestedAt, package.Id.FamilyName + "!App");
            }

            return 0;
        }
        catch (Exception exception)
        {
            MessageBoxW(0, $"恢复场景编辑器失败。\n\n{exception.Message}", DialogTitle, ErrorIcon | SetForeground | Topmost);
            return 1;
        }
    }

    private static int? GetRequestProcessId(string request)
    {
        var name = Path.GetFileNameWithoutExtension(request.AsSpan());
        var separator = name.IndexOf('-');
        if (separator < 1 || !int.TryParse(name[..separator], out var id) || id <= 0 ||
            !Guid.TryParseExact(name[(separator + 1)..], "N", out _))
        {
            return null;
        }

        return id;
    }

    private static void TerminateRequestProcess(int processId, DateTime requestedAt)
    {
        Process app;
        try
        {
            app = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (app)
        {
            try
            {
                // Hold the process handle across identity checks and termination.
                _ = app.SafeHandle;
                using var helper = Process.GetCurrentProcess();
                if (app.HasExited || app.SessionId != helper.SessionId ||
                    app.StartTime.ToUniversalTime() > requestedAt ||
                    !string.Equals(app.MainModule?.FileName, Path.Combine(AppContext.BaseDirectory, "OhMyLockScreen.exe"), StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                app.Kill();
                if (!app.WaitForExit(ProcessExitTimeout))
                {
                    throw new TimeoutException("等待残留应用进程结束超时。");
                }
            }
            catch (Exception exception) when ((exception is InvalidOperationException or Win32Exception) && app.HasExited)
            {
            }
        }
    }

    private static async Task<int> RecoverAsync(string directory, int processId, DateTime requestedAt, string appUserModelId)
    {
        if (MessageBoxW(
                0,
                "受 Windows 系统特性影响，需要重启 Windows 资源管理器（Explorer）才能正常显示场景编辑器。重启可能导致当前文件的打开状态丢失。\n\n是否现在重启资源管理器并重新打开应用？",
                DialogTitle,
                YesNo | WarningIcon | DefaultSecondButton | SetForeground | Topmost) != Yes)
        {
            return 0;
        }

        TerminateRequestProcess(processId, requestedAt);
        if (!LockScreenProcessRefresher.RestartExplorer())
        {
            throw new InvalidOperationException("无法重启 Windows 资源管理器。");
        }

        await RelaunchAsync(directory, appUserModelId);
        return 0;
    }

    private static async Task RelaunchAsync(string directory, string appUserModelId)
    {
        var request = Path.Combine(directory, EditorWindowRecoveryProtocol.LaunchRequestName);
        var ready = Path.Combine(directory, EditorWindowRecoveryProtocol.LaunchReadyName);
        File.Delete(ready);
        File.WriteAllText(request, string.Empty);
        try
        {
            try
            {
                ApplicationActivation.Launch(appUserModelId);
                if (await WaitForEditorAsync(ready, TimeSpan.FromSeconds(4)))
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is COMException or UnauthorizedAccessException)
            {
                Debug.WriteLine(exception);
            }

            if (!File.Exists(ready))
            {
                ApplicationActivation.LaunchFromShell(appUserModelId);
            }

            if (!await WaitForEditorAsync(ready, TimeSpan.FromSeconds(8)))
            {
                throw new TimeoutException("已请求重新打开应用，但场景编辑器窗口未就绪。");
            }
        }
        finally
        {
            File.Delete(request);
            File.Delete(ready);
        }
    }

    private static async Task<bool> WaitForEditorAsync(string ready, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (!File.Exists(ready) && elapsed.Elapsed < timeout)
        {
            await Task.Delay(100);
        }

        return File.Exists(ready);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint windowHandle, string text, string caption, uint type);
}
