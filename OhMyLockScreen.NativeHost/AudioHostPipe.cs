using System.Diagnostics;

namespace OhMyLockScreen.NativeHost;

internal static class AudioHostPipe
{
    internal static string GetName(string requestId)
    {
        using var process = Process.GetCurrentProcess();
        const string pipeRoot = @"\\.\pipe\";
        var sessionScope = $@"{pipeRoot}Sessions\{process.SessionId}\AppContainerNamedObjects\";
        var suffix = @"\OhMyLockScreen.Spectrum." + requestId;
        // UWP expands LOCAL to its session/package scope. Resolve only the unique,
        // unpredictable request ID published in this package's private request folder.
        var matches = Directory.EnumerateFiles(pipeRoot).Where(path =>
            path.StartsWith(sessionScope, StringComparison.OrdinalIgnoreCase)
            && path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
        {
            throw new IOException("音频频谱请求的包内管道已关闭或不唯一。");
        }

        return matches[0][pipeRoot.Length..];
    }
}
