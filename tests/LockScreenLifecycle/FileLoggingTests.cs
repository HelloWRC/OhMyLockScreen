using System.IO.Compression;
using Microsoft.Extensions.Logging;
using OhMyLockScreen;
using OhMyLockScreen.Services.Logging;
using OhMyLockScreen.Shared;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

internal static class FileLoggingTests
{
    private static string DirectoryPath => Path.Combine(ApplicationData.Current.LocalFolder.Path, "Logs");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static async Task RunAsync(Func<string, Func<Task>, Task> run)
    {
        await run("Desktop and lock-screen activations share one flushed, filtered and masked log", async () =>
        {
            var app = await LaunchEditorAsync(app => ReportUnhandled(app, "before-host"));
            var logger = IAppHost.GetRequiredService<ILoggerFactory>().CreateLogger("Verification");
            logger.LogInformation("editor-started");
            var lockView = new InfrastructureTests.ViewContext();
            lockView.Invoke(() => app.Activate(new ActivatedEventArgs(ActivationKind.LockScreen)));
            Check(lockView.Window.Content != null, "No lock-screen view.");
            lockView.Invoke(() => app.Activate(new ActivatedEventArgs(ActivationKind.LockScreenComponent)));
            lockView.Invoke(() => ReportUnhandled(app, "after-start"));
            using (logger.BeginScope("outer latitude=-1.5"))
            using (logger.BeginScope("inner"))
            {
                await Task.Yield();
                try { throw new InvalidOperationException("longitude=-2.5"); }
                catch (InvalidOperationException ex) { logger.LogError(ex, "lock-screen latitude={Value}", "+3.5"); }
            }
            if (logger.IsEnabled(LogLevel.Trace)) logger.LogTrace("trace-marker");
            Parallel.For(0, 250, n => logger.LogInformation("parallel:{Index}", n));

            var path = Directory.GetFiles(DirectoryPath, "*.log").Single();
            using (var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
            {
                var text = reader.ReadToEnd();
                var startup = text.Split(Environment.NewLine).Where(line => line.Contains("|Information|OhMyLockScreen.App|OhMyLockScreen 1.0.0.0 启动，生命周期："));
                Check(startup.Select(line => line.Split('：')[^1]).SequenceEqual(new[] { "编辑器", "锁屏" }), "Incorrect version, severity, lifecycle or repeated startup message.");
                foreach (var markerText in new[] { "before-host", "after-start" })
                {
                    var expected = $"|Critical|OhMyLockScreen.App|应用发生未处理异常{Environment.NewLine}System.InvalidOperationException: {markerText}{Environment.NewLine}";
                    var parts = text.Split(expected);
                    Check(parts.Length == 2 && parts[1].Split(Environment.NewLine)[0].Contains(nameof(ReportUnhandled)),
                        "Expected one Critical entry with the original exception and its throw site.");
                }
                Check(text.Contains("|Information|Verification|editor-started") &&
                    text.Contains("|Error|Verification|inner=>outer latitude=***=>lock-screen latitude=***") &&
                    text.Contains("System.InvalidOperationException: longitude=***") && text.Contains(nameof(FileLoggingTests)),
                    "Missing formatted message, scopes or exception stack.");
                Check(!new[] { "-1.5", "-2.5", "+3.5" }.Any(text.Contains), "Leaked a coordinate.");
#if DEBUG
                Check(text.Contains("|Trace|Verification|trace-marker"), "Debug file did not receive Trace.");
#else
                Check(!text.Contains("trace-marker"), "Release file received Trace.");
#endif
                const string marker = "|Information|Verification|parallel:";
                var entries = text.Split(Environment.NewLine).Where(line => line.Contains(marker))
                    .Select(line => int.Parse(line.Split(marker)[1])).Order();
                Check(entries.SequenceEqual(Enumerable.Range(0, 250)), "Lost or interleaved concurrent entries.");
            }
            await app.ExitAsync();
            using var reopened = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        });

        await run("Log maintenance skips active and unrelated files, preserves age and never overwrites", async () =>
        {
            var clock = new FixedClock();
            var directory = Directory.CreateDirectory(DirectoryPath).FullName;
            string Seed(string name, int ageDays)
            {
                var path = Path.Combine(directory, name);
                File.WriteAllText(path, name);
                File.SetLastWriteTimeUtc(path, clock.GetUtcNow().UtcDateTime.AddDays(-ageDays));
                return path;
            }
            var recent = Seed("log-2026-1-2-00-00-00-1.log", 30);
            var modified = File.GetLastWriteTimeUtc(recent);
            var expired = Seed("log-2026-1-1-00-00-00-1.log", 31);
            var expiredArchive = Seed("log-2026-1-1-00-00-00-2.log.gz", 31);
            var unrelated = Seed("log-other.log", 90);
            var collision = Seed("log-2026-1-3-00-00-00-1.log", 2);
            Seed(Path.GetFileName(collision) + ".gz", 2);
            var uncompressible = Seed("log-2026-1-4-00-00-00-1.log", 2);
            Directory.CreateDirectory(uncompressible + ".gz.tmp");
            var prior = Seed("log-2026-2-1-12-00-00-1.log.gz", 1);
            using var first = new FileLoggerProvider(directory, clock);
            await first.ArchiveTask;
            using var second = new FileLoggerProvider(directory, clock);
            await second.ArchiveTask;
            Check(File.Exists(Path.Combine(directory, "log-2026-2-1-12-00-00-2.log")) &&
                File.Exists(Path.Combine(directory, "log-2026-2-1-12-00-00-3.log")) && File.Exists(prior), "Reused an active or archived name.");
            Check(!File.Exists(expired) && !File.Exists(expiredArchive) && File.Exists(unrelated), "Incorrect retention or filename filter.");
            Check(File.Exists(collision) && File.ReadAllText(collision + ".gz") == Path.GetFileName(collision) + ".gz", "Archive collision destroyed data.");
            Check(File.Exists(uncompressible) && !File.Exists(uncompressible + ".gz"), "Failed compression lost the original file.");
            Check(!File.Exists(recent) && modified == File.GetLastWriteTimeUtc(recent + ".gz"), "Lost original archive age.");
            using var gzip = new GZipStream(File.OpenRead(recent + ".gz"), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            Check(reader.ReadToEnd() == Path.GetFileName(recent), "Archive contents changed.");
        });

        await run("An unusable log directory does not prevent activation or shutdown", async () =>
        {
            File.WriteAllText(DirectoryPath, "not a directory");
            var app = await LaunchEditorAsync();
            var log = IAppHost.GetRequiredService<ILoggerFactory>().CreateLogger("Failed");

            log.LogError("failed writer");
            Check(File.ReadAllText(DirectoryPath) == "not a directory", "Replaced an unrelated file.");
            await app.ExitAsync();
        });
    }

    private static void ReportUnhandled(App app, string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException exception)
        {
            Check(app.RaiseUnhandledException(exception).Handled, "Unhandled exception bypassed the Toast handler.");
        }
    }

    private static async Task<App> LaunchEditorAsync(Action<App>? beforeLaunch = null)
    {
        var app = new App();
        var editor = new InfrastructureTests.ViewContext();
        LockScreenHosting.IsHosted = () => false;
        if (beforeLaunch != null) editor.Invoke(() => beforeLaunch(app));
        editor.Invoke(() => app.Launch(new LaunchActivatedEventArgs()));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (editor.Window.Content == null)
        {
            Check(DateTime.UtcNow < deadline, "Desktop activation timed out.");
            editor.Drain();
            await Task.Delay(5);
        }
        return app;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
