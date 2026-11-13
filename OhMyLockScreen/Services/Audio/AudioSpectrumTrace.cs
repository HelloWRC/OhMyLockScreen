using System.IO;

namespace OhMyLockScreen.Services.Audio;

/// <summary>调试构建记录最近一轮的采集状态。</summary>
internal static class AudioSpectrumTrace
{
#if DEBUG && WINDOWS
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int MaximumLogSize = 1024 * 1024;
    private static readonly object Sync = new();
#if !AUDIO_HOST
    private static bool _hasWritten;
#endif
#endif

    [System.Diagnostics.Conditional("DEBUG")]
    internal static void Write(string message)
    {
#if DEBUG && WINDOWS
        try
        {
            lock (Sync)
            {
                var directory = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                var path = Path.Combine(directory, "AudioSpectrum.log");
#if AUDIO_HOST
                const bool startNewLog = false;
#else
                var startNewLog = !_hasWritten;
#endif
                using (var stream = OpenLog(path))
                {
                    if (startNewLog || stream.Length > MaximumLogSize)
                    {
                        stream.SetLength(0);
                    }

                    stream.Seek(0, SeekOrigin.End);
                    using var writer = new StreamWriter(stream);
                    writer.WriteLine($"{DateTimeOffset.Now:O} [{Environment.ProcessId}] {message}");
                }

#if !AUDIO_HOST
                if (startNewLog)
                {
                    _hasWritten = true;
                    foreach (var legacyPath in Directory.EnumerateFiles(directory, "AudioSpectrum.*.log"))
                    {
                        var processId = Path.GetFileNameWithoutExtension(legacyPath).AsSpan("AudioSpectrum.".Length);
                        if (int.TryParse(processId, out _))
                        {
                            File.Delete(legacyPath);
                        }
                    }
                }
#endif
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }
#endif
    }

#if DEBUG && WINDOWS
    private static FileStream OpenLog(string path)
    {
        // UWP 和 NativeHost 共用日志，短暂等待另一进程完成写入。
        for (var attempt = 0; ; ++attempt)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            }
            catch (IOException exception) when (exception.HResult == SharingViolation && attempt < 4)
            {
                Thread.Sleep(10);
            }
        }
    }
#endif
}
