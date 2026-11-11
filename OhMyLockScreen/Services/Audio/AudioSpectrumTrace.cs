namespace OhMyLockScreen.Services.Audio;

/// <summary>调试构建仅记录采集状态，按进程隔离；不保存音频采样。</summary>
internal static class AudioSpectrumTrace
{
#if DEBUG && WINDOWS
    private static readonly object Sync = new();
#endif

    [System.Diagnostics.Conditional("DEBUG")]
    internal static void Write(string message)
    {
#if DEBUG && WINDOWS
        try
        {
            lock (Sync)
            {
                var path = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path,
                    $"AudioSpectrum.{Environment.ProcessId}.log");
                if (System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length > 1024 * 1024)
                {
                    System.IO.File.WriteAllText(path, "");
                }

                System.IO.File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }
#endif
    }
}
