using System.Diagnostics;

namespace OhMyLockScreen.NativeHost;

internal static class AppContainerPipe
{
    internal static string? FindName(string pipeName)
    {
        using var process = Process.GetCurrentProcess();
        const string pipeRoot = @"\\.\pipe\";
        var sessionScope = $@"{pipeRoot}Sessions\{process.SessionId}\AppContainerNamedObjects\";
        var suffix = "\\" + pipeName;
        // UWP expands LOCAL to its session/package scope. Resolve only the unique,
        // unpredictable request ID published in this package's private request folder.
        var matches = Directory.EnumerateFiles(pipeRoot).Where(path =>
            path.StartsWith(sessionScope, StringComparison.OrdinalIgnoreCase)
            && path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0][pipeRoot.Length..] : null;
    }
}
