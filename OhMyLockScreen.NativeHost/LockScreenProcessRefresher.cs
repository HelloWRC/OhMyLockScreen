using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OhMyLockScreen.NativeHost;

internal sealed partial class LockScreenProcessRefresher
{
    private const string ShellProcessName = "explorer";
    private const string LockScreenHostModuleName = "AboveLockAppHost.dll";
    private static readonly string[] LockProcessNames = ["LockAppHost", "LockApp"];
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ShellRestartTimeout = TimeSpan.FromSeconds(3);

    public bool Refresh()
    {
        // The main Explorer holds AboveLockAppHost and caches the selected lock-screen extension.
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var succeeded = TerminateProcesses(LockProcessNames, currentSessionId);
        succeeded &= RestartShell(currentSessionId);
        return succeeded;
    }

    private static bool RestartShell(int currentSessionId)
    {
        using var shell = FindShellProcess(currentSessionId);
        if (shell is null)
        {
            return true;
        }

        var shellProcessId = shell.Id;
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

    private static bool TerminateProcesses(IReadOnlyList<string> processNames, int currentSessionId)
    {
        var succeeded = true;
        foreach (var processName in processNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch (Exception)
            {
                succeeded = false;
                continue;
            }

            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        if (process.HasExited || process.SessionId != currentSessionId)
                        {
                            continue;
                        }

                        process.Kill();
                        succeeded &= process.WaitForExit(ProcessExitTimeout);
                    }
                    catch (Exception)
                    {
                        succeeded = false;
                    }
                }
            }
        }

        return succeeded;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetShellWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processId);
}
