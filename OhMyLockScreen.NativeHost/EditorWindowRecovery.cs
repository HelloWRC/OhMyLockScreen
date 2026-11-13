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
    private const int No = 7;
    private const uint CommandMessage = 0x0111;
    [ThreadStatic]
    private static (string Path, Process App)? _dialogRequest;

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

            foreach (var request in Directory.EnumerateFiles(directory, "*" + EditorWindowRecoveryProtocol.RequestExtension))
            {
                using var app = GetRequestProcess(request);
                if (app is null)
                {
                    File.Delete(request);
                    continue;
                }

                return await RecoverAsync(request, app, package.Id.FamilyName + "!App");
            }

            return 0;
        }
        catch (Exception exception)
        {
            MessageBoxW(0, $"恢复场景编辑器失败。\n\n{exception.Message}", DialogTitle, ErrorIcon | SetForeground | Topmost);
            return 1;
        }
    }

    private static Process? GetRequestProcess(string request)
    {
        var name = Path.GetFileNameWithoutExtension(request).AsSpan();
        var separator = name.IndexOf('-');
        if (separator < 1 || !int.TryParse(name[..separator], out var id) ||
            !Guid.TryParseExact(name[(separator + 1)..], "N", out _))
        {
            return null;
        }

        try
        {
            var app = Process.GetProcessById(id);
            var matches = false;
            try
            {
                using var helper = Process.GetCurrentProcess();
                matches = app.SessionId == helper.SessionId &&
                    app.StartTime.ToUniversalTime() <= File.GetLastWriteTimeUtc(request) &&
                    string.Equals(app.MainModule?.FileName, Path.Combine(AppContext.BaseDirectory, "OhMyLockScreen.exe"), StringComparison.OrdinalIgnoreCase);
                return matches ? app : null;
            }
            finally
            {
                if (!matches)
                {
                    app.Dispose();
                }
            }
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<int> RecoverAsync(string request, Process app, string appUserModelId)
    {
        var confirmed = Path.ChangeExtension(request, EditorWindowRecoveryProtocol.ConfirmedExtension);
        var exiting = Path.ChangeExtension(request, EditorWindowRecoveryProtocol.ExitingExtension);
        try
        {
            if (!File.Exists(request) || !Confirm(request, app))
            {
                return 0;
            }

            try
            {
                File.Move(request, confirmed);
            }
            catch (FileNotFoundException)
            {
                return 0;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!File.Exists(exiting))
            {
                if (!File.Exists(confirmed) && !File.Exists(exiting))
                {
                    return 0;
                }

                await Task.Delay(50, timeout.Token);
            }

            await app.WaitForExitAsync(timeout.Token);
            if (!LockScreenProcessRefresher.RestartExplorer())
            {
                throw new InvalidOperationException("无法重启 Windows 资源管理器。");
            }

            await RelaunchAsync(Path.GetDirectoryName(request)!, appUserModelId);
            return 0;
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException("等待场景编辑器保存并退出超时。", exception);
        }
        finally
        {
            File.Delete(request);
            File.Delete(confirmed);
            File.Delete(exiting);
        }
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

    private static unsafe bool Confirm(string request, Process app)
    {
        // A normal frame can attach while the native confirmation is open.
        _dialogRequest = (request, app);
        var timer = SetTimer(0, 0, 120, &CloseCanceledDialog);
        try
        {
            if (timer == 0)
            {
                throw new InvalidOperationException("无法启动场景编辑器恢复提示。");
            }

            return MessageBoxW(
                0,
                "受 Windows 系统特性影响，需要重启 Windows 资源管理器（Explorer）才能正常显示场景编辑器。重启可能导致当前文件的打开状态丢失。\n\n是否现在重启资源管理器并重新打开应用？",
                DialogTitle,
                YesNo | WarningIcon | DefaultSecondButton | SetForeground | Topmost) == Yes;
        }
        finally
        {
            KillTimer(0, timer);
            _dialogRequest = null;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void CloseCanceledDialog(nint window, uint message, nuint timer, uint time)
    {
        try
        {
            if (_dialogRequest is { } request && (!File.Exists(request.Path) || request.App.HasExited))
            {
                var dialog = FindWindowW("#32770", DialogTitle);
                if (dialog != 0 && GetWindowThreadProcessId(dialog, 0) == GetCurrentThreadId())
                {
                    PostMessageW(dialog, CommandMessage, No, 0);
                }
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint windowHandle, string text, string caption, uint type);

    [LibraryImport("user32.dll")]
    private static unsafe partial nuint SetTimer(nint window, nuint timer, uint interval, delegate* unmanaged[Stdcall]<nint, uint, nuint, uint, void> callback);

    [LibraryImport("user32.dll")]
    private static partial int KillTimer(nint window, nuint timer);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowW(string className, string title);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, nint processId);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    private static partial int PostMessageW(nint window, uint message, nuint wParam, nint lParam);
}
