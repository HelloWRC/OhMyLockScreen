using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace OhMyLockScreen.Services.Logging;

internal sealed partial class FileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly object _sync = new();
    private volatile StreamWriter? _writer;
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    internal Task ArchiveTask { get; } = Task.CompletedTask;

    internal FileLoggerProvider(string directory, TimeProvider clock)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var timestamp = clock.GetLocalNow().ToString("yyyy-M-d-HH-mm-ss", CultureInfo.InvariantCulture);
            for (var number = 1; ; number++)
            {
                var path = Path.Combine(directory, $"log-{timestamp}-{number}.log");
                if (File.Exists(path + ".gz")) continue;

                FileStream stream;
                try
                {
                    stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                }
                catch (IOException) when (File.Exists(path))
                {
                    // Another host may have created the same name in this second.
                    continue;
                }

                try
                {
                    _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }

                ArchiveTask = Task.Run(() => ArchivePreviousLogs(directory, path, clock.GetUtcNow().UtcDateTime.AddDays(-30)));
                break;
            }
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            Debug.WriteLine(ex);
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    [GeneratedRegex(@"\Alog-[0-9]{4}-[0-9]{1,2}-[0-9]{1,2}-[0-9]{2}-[0-9]{2}-[0-9]{2}-[1-9][0-9]*\.log(?:\.gz)?\z")]
    private static partial Regex LogFileName();

    private static void ArchivePreviousLogs(string directory, string currentPath, DateTime cutoff)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, "log-*"))
            {
                if (path == currentPath || !LogFileName().IsMatch(Path.GetFileName(path))) continue;
                try
                {
                    ProcessPreviousLog(path, cutoff);
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    Debug.WriteLine(ex);
                }
            }
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            Debug.WriteLine(ex);
        }
    }

    private static void ProcessPreviousLog(string path, DateTime cutoff)
    {
        // FileShare.Delete lets us delete the source without first releasing the lock.
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Delete);
        var modified = File.GetLastWriteTimeUtc(path);
        if (modified < cutoff)
        {
            File.Delete(path);
            return;
        }

        var archive = path + ".gz";
        if (path.EndsWith(".gz", StringComparison.Ordinal) || File.Exists(archive)) return;

        var temporary = archive + ".tmp";
        try
        {
            using (var output = File.Create(temporary))
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            {
                source.CopyTo(gzip);
            }

            File.SetLastWriteTimeUtc(temporary, modified);
            File.Move(temporary, archive);
        }
        finally
        {
            File.Delete(temporary);
        }
        File.Delete(path);
    }

    private void WriteLine(string entry)
    {
        lock (_sync)
        {
            try
            {
                _writer?.WriteLine(entry);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                Debug.WriteLine(ex);
                CloseWriter();
            }
        }
    }

    private void CloseWriter()
    {
        var writer = _writer;
        _writer = null;
        try
        {
            writer?.Dispose();
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            Debug.WriteLine(ex);
        }
    }

    public void Dispose()
    {
        lock (_sync) CloseWriter();
    }

    [GeneratedRegex(@"(\b(?:latitude|longitude)=)[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Coordinates();

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public bool IsEnabled(LogLevel level) => level is >= LogLevel.Trace and < LogLevel.None && provider._writer != null;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider._scopes.Push(state);

        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            var scopes = new Stack<string>();
            provider._scopes.ForEachScope(static (scope, stack) => stack.Push($"{scope}=>"), scopes);
            var message = string.Concat(scopes) + formatter(state, exception);
            if (exception != null) message += Environment.NewLine + exception;
            provider.WriteLine($"{DateTime.Now}|{level}|{category}|{Coordinates().Replace(message, "$1***")}");
        }
    }
}
