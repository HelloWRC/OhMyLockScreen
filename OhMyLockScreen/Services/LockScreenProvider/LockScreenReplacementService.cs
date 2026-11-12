using Microsoft.Extensions.Logging;
using OhMyLockScreen.Shared.Models.LockScreenProvider;

namespace OhMyLockScreen.Services.LockScreenProvider;

public sealed class LockScreenReplacementService : IDisposable
{
    private readonly ILockScreenHelperClient _client;
    private readonly ILogger<LockScreenReplacementService> _logger;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private Task? _initialization;
    private long _revision;
    private bool _isInitialized;
    private bool _currentValue;
    private bool _confirmedValue;
    private bool _disposed;

    public LockScreenReplacementService(
        ILockScreenHelperClient client,
        ILogger<LockScreenReplacementService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public bool CurrentValue
    {
        get
        {
            lock (_sync)
            {
                return _currentValue;
            }
        }
    }

    public bool IsInitialized
    {
        get
        {
            lock (_sync)
            {
                return _isInitialized;
            }
        }
    }

    public event EventHandler<LockScreenReplacementStateChangedEventArgs>? StateChanged;
    public event EventHandler<LockScreenReplacementFailedEventArgs>? OperationFailed;

    public void EnsureInitialized()
    {
        _ = EnsureInitializedAsync();
    }

    public Task EnsureInitializedAsync()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            if (_initialization is { IsCompleted: false })
            {
                return _initialization;
            }

            if (_isInitialized)
            {
                return _initialization ?? Task.CompletedTask;
            }

            return _initialization = RefreshAsync(_disposeCancellation.Token);
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        long revision;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            revision = _revision;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        var token = linkedCancellation.Token;
        try
        {
            await _operationGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var response = await _client.ExecuteAsync(LockScreenProviderOperation.Query, token)
                    .ConfigureAwait(false);
                ThrowIfUnsuccessful(response);
                ApplyQueryResult(revision, response);
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (OperationCanceledException) when (_disposed || token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportFailure(LockScreenProviderOperation.Query, revision, exception, rolledBack: false);
        }
    }

    public void SetEnabled(bool enabled)
    {
        _ = SetEnabledAsync(enabled);
    }

    public Task SetEnabledAsync(bool enabled)
    {
        long revision;
        bool notify;
        lock (_sync)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            revision = ++_revision;
            notify = _currentValue != enabled;
            _currentValue = enabled;
        }

        if (notify)
        {
            RaiseStateChanged(enabled);
        }

        return ExecuteSetAsync(revision, enabled);
    }

    private async Task ExecuteSetAsync(long revision, bool enabled)
    {
        try
        {
            await _operationGate.WaitAsync(_disposeCancellation.Token).ConfigureAwait(false);
            try
            {
                var operation = enabled
                    ? LockScreenProviderOperation.Enable
                    : LockScreenProviderOperation.Disable;
                var response = await _client.ExecuteAsync(operation, _disposeCancellation.Token)
                    .ConfigureAwait(false);
                ThrowIfUnsuccessful(response);
                ApplySetResult(revision, response.IsEnabled);
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        catch (Exception exception)
        {
            ReportFailure(
                enabled ? LockScreenProviderOperation.Enable : LockScreenProviderOperation.Disable,
                revision,
                exception,
                rolledBack: RollBackIfCurrent(revision));
        }
    }

    private void ApplyQueryResult(long revision, LockScreenProviderResponse response)
    {
        bool changed;
        lock (_sync)
        {
            if (_disposed || revision != _revision)
            {
                return;
            }

            _isInitialized = true;
            _confirmedValue = response.IsEnabled;
            changed = _currentValue != response.IsEnabled;
            _currentValue = response.IsEnabled;
        }

        if (changed)
        {
            RaiseStateChanged(response.IsEnabled);
        }
    }

    private void ApplySetResult(long revision, bool enabled)
    {
        bool changed;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _isInitialized = true;
            _confirmedValue = enabled;
            if (revision != _revision)
            {
                return;
            }

            changed = _currentValue != enabled;
            _currentValue = enabled;
        }

        if (changed)
        {
            RaiseStateChanged(enabled);
        }
    }

    private bool RollBackIfCurrent(long revision)
    {
        bool changed;
        lock (_sync)
        {
            if (_disposed || revision != _revision)
            {
                return false;
            }

            changed = _currentValue != _confirmedValue;
            _currentValue = _confirmedValue;
        }

        if (changed)
        {
            RaiseStateChanged(_confirmedValue);
        }

        return changed;
    }

    private void ReportFailure(
        LockScreenProviderOperation operation,
        long revision,
        Exception exception,
        bool rolledBack)
    {
        lock (_sync)
        {
            if (_disposed || revision != _revision)
            {
                return;
            }
        }

        _logger.LogError(exception, "锁屏替换操作失败：{Operation}", operation);
        OperationFailed?.Invoke(
            this,
            new LockScreenReplacementFailedEventArgs(operation, exception, rolledBack));
    }

    private void RaiseStateChanged(bool isEnabled) =>
        StateChanged?.Invoke(this, new LockScreenReplacementStateChangedEventArgs(isEnabled));

    private static void ThrowIfUnsuccessful(LockScreenProviderResponse response)
    {
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error ?? "锁屏辅助进程未能完成请求。");
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _disposeCancellation.Cancel();
        _disposeCancellation.Dispose();
    }
}

public sealed class LockScreenReplacementStateChangedEventArgs(bool isEnabled) : EventArgs
{
    public bool IsEnabled { get; } = isEnabled;
}

public sealed class LockScreenReplacementFailedEventArgs(
    LockScreenProviderOperation operation,
    Exception exception,
    bool rolledBack) : EventArgs
{
    public LockScreenProviderOperation Operation { get; } = operation;
    public Exception Exception { get; } = exception;
    public bool RolledBack { get; } = rolledBack;
}
