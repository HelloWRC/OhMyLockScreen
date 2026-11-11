using System.IO;
using System.IO.Pipes;
using OhMyLockScreen.Shared.Models.Audio;
using Windows.ApplicationModel;
using Windows.Storage;

namespace OhMyLockScreen.Services.Audio;

internal static class AudioHostSpectrumClient
{
    internal static async Task CaptureAsync(Action<AudioSpectrumSnapshot> publish, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(ApplicationData.Current.LocalFolder.Path, AudioSpectrumProtocol.RequestDirectory);
        Directory.CreateDirectory(directory);
        var request = Path.Combine(directory, requestId + ".request");
        await using var pipe = new NamedPipeServerStream(AudioSpectrumProtocol.PipePrefix + requestId,
            PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            // The pipe exists before the request becomes visible to the helper.
            await File.WriteAllTextAsync(request, requestId, cancellationToken).ConfigureAwait(false);
            AudioSpectrumTrace.Write($"Requesting audio host: {requestId}");
            await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync("AudioSpectrum");
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connection.CancelAfter(TimeSpan.FromSeconds(10));
            await pipe.WaitForConnectionAsync(connection.Token).ConfigureAwait(false);
            AudioSpectrumTrace.Write("Audio host connected");
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await AudioSpectrumProtocol.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
                publish(frame);
            }
        }
        finally
        {
            File.Delete(request);
            AudioSpectrumTrace.Write("Audio host disconnected");
        }
    }
}
