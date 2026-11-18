using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OhMyLockScreen.NativeHost;

internal static partial class LockScreenProcessRefresher
{
    private const string ShellProcessName = "explorer";
    private const string LockScreenHostModuleName = "AboveLockAppHost.dll";
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ShellRestartTimeout = TimeSpan.FromSeconds(3);

    internal static bool RestartExplorer()
    {
        using var currentProcess = Process.GetCurrentProcess();
        return RestartShell(currentProcess.SessionId);
    }

    private static bool RestartShell(int currentSessionId)
    {
        using var shell = FindShellProcess(currentSessionId);
        var shellProcessId = shell?.Id ?? 0;
        if (shell is not null)
        {
            try
            {
                shell.Kill();
                if (!shell.WaitForExit(ProcessExitTimeout))
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        if (WaitForShellRestart(currentSessionId, shellProcessId, ShellRestartTimeout))
        {
            return true;
        }

        try
        {
            var explorerPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "explorer.exe");
            using var replacement = Process.Start(new ProcessStartInfo(explorerPath)
            {
                UseShellExecute = false
            });
            return replacement is not null &&
                WaitForShellRestart(currentSessionId, shellProcessId, ShellRestartTimeout);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Process? FindShellProcess(int currentSessionId)
    {
        var shellWindow = GetShellWindow();
        if (shellWindow != 0)
        {
            GetWindowThreadProcessId(shellWindow, out var shellProcessId);
            if (shellProcessId != 0)
            {
                try
                {
                    var process = Process.GetProcessById((int)shellProcessId);
                    if (process.SessionId == currentSessionId &&
                        string.Equals(process.ProcessName, ShellProcessName, StringComparison.OrdinalIgnoreCase))
                    {
                        return process;
                    }

                    process.Dispose();
                }
                catch (Exception)
                {
                }
            }
        }

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(ShellProcessName);
        }
        catch (Exception)
        {
            return null;
        }

        foreach (var process in processes)
        {
            try
            {
                if (process.SessionId == currentSessionId && HasLockScreenHostModule(process))
                {
                    return process;
                }
            }
            catch (Exception)
            {
            }

            process.Dispose();
        }

        return null;
    }

    private static bool HasLockScreenHostModule(Process process)
    {
        try
        {
            return process.Modules.Cast<ProcessModule>().Any(module =>
                string.Equals(module.ModuleName, LockScreenHostModuleName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool WaitForShellRestart(int currentSessionId, int previousProcessId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var shellWindow = GetShellWindow();
            if (shellWindow != 0)
            {
                GetWindowThreadProcessId(shellWindow, out var shellProcessId);
                if (shellProcessId != 0 &&
                    shellProcessId != previousProcessId &&
                    IsProcessInSession((int)shellProcessId, currentSessionId))
                {
                    return true;
                }
            }

            Thread.Sleep(100);
        }

        return false;
    }

    private static bool IsProcessInSession(int processId, int currentSessionId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.SessionId == currentSessionId;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetShellWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processId);
}
