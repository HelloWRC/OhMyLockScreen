using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Shared.Abstractions.Services.SystemStatus;
using OhMyLockScreen.Shared.Models.SystemStatus;

namespace OhMyLockScreen.Services.SystemStatus;

/// <summary>Shares native observers across loaded components and windows, independently for each kind.</summary>
public sealed class SystemStatusService(ISystemStatusSource source, ILogger<SystemStatusService> logger)
    : ISystemStatusService, IHostedService, IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<SystemStatusKind, Entry> _entries = [];
    private bool _disposed;
    public event EventHandler<SystemStatusChangedEventArgs>? Changed;

    public SystemStatusSnapshot GetState(SystemStatusKind kind)
    {
        lock (_sync)
        {
            return _entries.TryGetValue(kind, out var entry) ? entry.State : SystemStatusSnapshot.Unavailable;
        }
    }

    public IDisposable Acquire(SystemStatusKind kind)
    {
        Entry entry;
        bool start;
        lock (_sync)
        {
            if (_disposed)
            {
                return System.Reactive.Disposables.Disposable.Empty;
            }

            start = !_entries.TryGetValue(kind, out entry!);
            if (start)
            {
                _entries.Add(kind, entry = new Entry());
            }

            ++entry.Users;
        }

        if (start)
        {
            IDisposable? observer = null;
            try
            {
                observer = source.Watch(kind, state => Publish(kind, entry, state));
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "无法监听系统状态 {Kind}", kind);
                Publish(kind, entry, SystemStatusSnapshot.Unavailable);
            }

            lock (_sync)
            {
                if (_entries.TryGetValue(kind, out var current) && ReferenceEquals(current, entry))
                {
                    entry.Observer = observer;
                    observer = null;
                }
            }

            DisposeObserver(observer);
        }

        return System.Reactive.Disposables.Disposable.Create(() => Release(kind, entry));
    }

    private void Publish(SystemStatusKind kind, Entry entry, SystemStatusSnapshot state)
    {
        lock (_sync)
        {
            if (_disposed || !_entries.TryGetValue(kind, out var current)
                || !ReferenceEquals(current, entry) || entry.State == state)
            {
                return;
            }

            entry.State = state;
        }

        Changed?.Invoke(this, new SystemStatusChangedEventArgs(kind));
    }

    private void Release(SystemStatusKind kind, Entry entry)
    {
        IDisposable? observer;
        lock (_sync)
        {
            if (!_entries.TryGetValue(kind, out var current) || !ReferenceEquals(current, entry)
                || --entry.Users > 0)
            {
                return;
            }

            _entries.Remove(kind);
            observer = entry.Observer;
        }

        DisposeObserver(observer);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Entry[] entries;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            entries = [.. _entries.Values];
            _entries.Clear();
        }

        foreach (var entry in entries)
        {
            DisposeObserver(entry.Observer);
        }
    }

    private void DisposeObserver(IDisposable? observer)
    {
        try
        {
            observer?.Dispose();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "释放系统状态监听失败");
        }
    }

    private sealed class Entry
    {
        public int Users;
        public IDisposable? Observer;
        public SystemStatusSnapshot State = SystemStatusSnapshot.Unavailable;
    }
}
