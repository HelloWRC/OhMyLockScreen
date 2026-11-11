using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.Extensions.Logging.Abstractions;
using OhMyLockScreen.Services.Audio;
using Windows.Storage;

// A full-trust packaged desktop process owns WASAPI; the hosted UWP views only receive spectra.
var directory = Path.Combine(ApplicationData.Current.LocalFolder.Path, AudioSpectrumProtocol.RequestDirectory);
Directory.CreateDirectory(directory);
FileStream singleton;
try
{
    singleton = new FileStream(Path.Combine(directory, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
}
catch (IOException)
{
    return;
}

using var singletonLease = singleton;
try
{
    using var source = new WindowsAudioSpectrumSource();
    using var spectrum = new AudioSpectrumService(source, NullLogger<AudioSpectrumService>.Instance, TimeProvider.System);
    var clients = new Dictionary<string, Task>();
    var idle = Stopwatch.StartNew();
    AudioSpectrumTrace.Write("Desktop audio host started");
    while (idle.Elapsed < TimeSpan.FromSeconds(3))
    {
        foreach (var request in Directory.EnumerateFiles(directory, "*.request"))
        {
            var id = Path.GetFileNameWithoutExtension(request);
            if (clients.ContainsKey(id) || !Guid.TryParseExact(id, "N", out _))
            {
                continue;
            }

            clients.Add(id, ServeAsync(id, request, spectrum));
        }

        foreach (var id in clients.Where(entry => entry.Value.IsCompleted).Select(entry => entry.Key).ToArray())
        {
            await clients[id];
            clients.Remove(id);
        }

        if (clients.Count > 0)
        {
            idle.Restart();
        }

        await Task.Delay(100);
    }

    await spectrum.StopAsync(default);
    AudioSpectrumTrace.Write("Desktop audio host stopped after its last client disconnected");
}
catch (Exception exception)
{
    AudioSpectrumTrace.Write($"Desktop audio host failed: {exception}");
}
static async Task ServeAsync(string id, string request, AudioSpectrumService spectrum)
{
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var pipe = new NamedPipeClientStream(".", AudioHostPipe.GetName(id),
            PipeDirection.Out, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        File.Delete(request);
        using var lease = spectrum.Acquire();
        AudioSpectrumTrace.Write($"Serving spectrum client {id}");
        while (pipe.IsConnected)
        {
            using var write = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await AudioSpectrumProtocol.WriteAsync(pipe, spectrum.State, write.Token);
            await Task.Delay(TimeSpan.FromSeconds(1d / 30));
        }
    }
    catch (Exception exception) when (exception is IOException or OperationCanceledException or UnauthorizedAccessException)
    {
        AudioSpectrumTrace.Write($"Spectrum client {id} disconnected: {exception.Message}");
    }
    finally
    {
        File.Delete(request);
    }
}
