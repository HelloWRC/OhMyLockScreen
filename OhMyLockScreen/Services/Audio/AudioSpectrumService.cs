using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Shared.Abstractions.Services.Audio;
using OhMyLockScreen.Shared.Models.Audio;
using System.Reactive.Disposables;

namespace OhMyLockScreen.Services.Audio;

/// <summary>按租约共享采集；串行结束旧采集后才启动下一轮。</summary>
public sealed class AudioSpectrumService(IAudioSpectrumSource source, ILogger<AudioSpectrumService> logger, TimeProvider clock)
    : IAudioSpectrumService, IHostedService, IDisposable
{
    private readonly object _sync = new();
    private Entry? _entry;
    private Task _runner = Task.CompletedTask;
    private AudioSpectrumSnapshot _state = AudioSpectrumSnapshot.Unavailable;
    private bool _disposed;
    public event EventHandler? Changed;

    public AudioSpectrumSnapshot State => Volatile.Read(ref _state);

    public IDisposable Acquire()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return Disposable.Empty;
            }

            if (_entry == null)
            {
                var previous = _runner;
                var entry = _entry = new Entry();
                source.Changed += OnSourceChanged;
                _runner = Task.Run(async () =>
                {
                    await previous.ConfigureAwait(false);
                    try
                    {
                        await RunAsync(entry).ConfigureAwait(false);
                    }
                    finally
                    {
                        entry.Stop.Dispose();
                    }
                });
            }

            var current = _entry;
            ++current.Users;
            return Disposable.Create(() => Release(current));
        }
    }

    private void OnSourceChanged(object? sender, EventArgs args)
    {
        AudioSpectrumTrace.Write($"Source changed; suspended={source.IsSuspended}");
        lock (_sync)
        {
            if (_entry is { } entry)
            {
                ++entry.Version;
                entry.Attempt?.Cancel();
            }
        }
    }

    private async Task RunAsync(Entry entry)
    {
        var failures = 0;
        while (!entry.Stop.IsCancellationRequested)
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(entry.Stop.Token);
            int version;
            lock (_sync)
            {
                if (!ReferenceEquals(_entry, entry))
                {
                    return;
                }

                entry.Attempt = attempt;
                version = ++entry.Version;
            }

            try
            {
                Publish(entry, version, AudioSpectrumSnapshot.Unavailable);
                if (source.IsSuspended)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, clock, attempt.Token).ConfigureAwait(false);
                }
                else
                {
                    await source.CaptureAsync(state =>
                    {
                        if (Publish(entry, version, state))
                        {
                            failures = 0;
                        }
                    }, attempt.Token).ConfigureAwait(false);
                    attempt.Token.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("音频采集意外结束。");
                }
            }
            catch (OperationCanceledException) when (attempt.IsCancellationRequested)
            {
                failures = 0;
            }
            catch (Exception exception)
            {
                AudioSpectrumTrace.Write($"Capture failed: {exception}");
                logger.LogWarning(exception, "无法采集系统音频频谱");
                Publish(entry, version, AudioSpectrumSnapshot.Unavailable);
                var seconds = ++failures switch
                {
                    1 => 1,
                    2 => 2,
                    _ => 5
                };
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(seconds), clock, attempt.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (attempt.IsCancellationRequested)
                {
                }
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(entry.Attempt, attempt))
                    {
                        entry.Attempt = null;
                    }
                }
            }
        }
    }

    private bool Publish(Entry entry, int version, AudioSpectrumSnapshot state)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_entry, entry) || version != entry.Version || entry.Stop.IsCancellationRequested)
            {
                return false;
            }

            Volatile.Write(ref _state, state);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void Release(Entry entry)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_entry, entry) || --entry.Users > 0)
            {
                return;
            }

            ClearEntry();
        }
    }

    private void ClearEntry()
    {
        source.Changed -= OnSourceChanged;
        var entry = _entry;
        _entry = null;
        Volatile.Write(ref _state, AudioSpectrumSnapshot.Unavailable);
        entry?.Stop.Cancel();
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        await _runner.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _disposed = true;
                ClearEntry();
            }
        }
    }

    private sealed class Entry
    {
        internal int Users;
        internal int Version;
        internal CancellationTokenSource Stop { get; } = new();
        internal CancellationTokenSource? Attempt;
    }
}
