namespace OhMyLockScreen.NativeHost;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        switch (GetMode(args))
        {
            case "--audio-spectrum":
                await AudioSpectrumHost.RunAsync();
                return 0;
            case "--lock-screen-provider":
                return await LockScreenProviderHost.RunAsync();
            default:
                return 2;
        }
    }

    private static string? GetMode(string[] args)
    {
        // FullTrustProcessLauncher can prepend the invoker's package-relative application ID.
        ReadOnlySpan<string> parameters = args;
        if (parameters is ["/InvokerPRAID:", "App", ..])
        {
            parameters = parameters[2..];
        }

        return parameters is ["--audio-spectrum"] or ["--lock-screen-provider"] ? parameters[0] : null;
    }
}
