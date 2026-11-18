using System.IO.Pipes;
using System.Text.Json;
using OhMyLockScreen.Shared.Models.LockScreenProvider;

namespace OhMyLockScreen.NativeHost;

internal static partial class LockScreenProviderHost
{
    internal static async Task<int> RunUnlockPipeAsync()
    {
        try
        {
            var directory = GetBridgeDirectory();
            var rendezvous = Path.Combine(directory, LockScreenProviderProtocol.UnlockPipeDirectoryName);
            foreach (var marker in Directory.EnumerateFiles(rendezvous, "*" + LockScreenProviderProtocol.UnlockPipeRequestExtension))
            {
                var id = Path.GetFileNameWithoutExtension(marker);
                if (!Guid.TryParseExact(id, "N", out _))
                {
                    continue;
                }

                var pipeName = AppContainerPipe.FindName(LockScreenProviderProtocol.UnlockPipeNamePrefix + id);
                if (pipeName == null)
                {
                    continue;
                }

                using var claim = TryClaimUnlockPipe(marker);
                if (claim == null)
                {
                    continue;
                }

                using var timeout = new CancellationTokenSource(LockScreenProviderProtocol.UnlockPipeTimeout);
                await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(timeout.Token);
                var response = await LockScreenUnlockCommand.ServePipeAsync(
                    pipe, directory, id, new LockAppHostGlobalUnlockSignal(), timeout.Token);
                if (response == null)
                {
                    return 2;
                }

                await TryWriteDiagnosticAsync(directory, new(id, LockScreenProviderOperation.Unlock), response);
                return response.Success ? 0 : 1;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException or JsonException)
        {
            return 1;
        }

        return 2;
    }

    private static FileStream? TryClaimUnlockPipe(string marker)
    {
        try
        {
            return new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
