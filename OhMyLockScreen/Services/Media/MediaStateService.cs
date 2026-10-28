using System.ComponentModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Shared.Models.Media;

namespace OhMyLockScreen.Services.Media;

public sealed class MediaStateService : IMediaStateService, IHostedService, IDisposable
{
    private readonly IMediaSessionSource _source;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MediaStateService> _logger;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _stopping;
    private ITimer? _timer;
    private MediaState _state = MediaState.Empty;
    private long _version;
    private volatile bool _stopped;

    public MediaStateService(IMediaSessionSource source, TimeProvider timeProvider, ILogger<MediaStateService> logger)
    {
        _source = source;
        _timeProvider = timeProvider;
        _logger = logger;
        _stopping = _lifetime.Token;
    }

    public MediaState State => Volatile.Read(ref _state);
    public MediaState Placeholder { get; private set; } = new()
    {
        HasSession = true,
        Title = "再会",
        Artist = "はるまきごはん",
        AlbumTitle = "ふたりの",
        AlbumArtist = "はるまきごはん",
        PlaybackType = "Music",
        SourceAppId = "媒体预览",
        Position = TimeSpan.FromSeconds(14),
        EndTime = TimeSpan.FromSeconds(239),
        MaxSeekTime = TimeSpan.FromSeconds(239),
        ThemeColor = 0xFF606060
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _source.Changed += OnSourceChanged;
        try
        {
            try
            {
                var artwork = await Task.Run(() => _source.ReadPlaceholderAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
                Placeholder = Placeholder with
                {
                    Cover = artwork.Cover,
                    CoverWidth = artwork.CoverWidth,
                    CoverHeight = artwork.CoverHeight,
                    ThemeColor = artwork.ThemeColor
                };
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogDebug(exception, "无法加载媒体占位封面");
            }

            await _source.StartAsync(cancellationToken).ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
            lock (_sync)
            {
                if (_stopped)
                {
                    return;
                }

                _timer = _timeProvider.CreateTimer(_ => AdvancePosition(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "无法初始化系统媒体会话");
        }
    }

    private void OnSourceChanged(object? sender, EventArgs args) => _ = RefreshAsync();

    private void AdvancePosition()
    {
        lock (_sync)
        {
            var state = State;
            if (_stopped || !state.IsPlaying)
            {
                return;
            }

            var now = _timeProvider.GetUtcNow();
            Volatile.Write(ref _state, state with { Position = state.GetPosition(now), LastUpdatedTime = now });
        }

        NotifyStateChanged();
    }

    private async Task RefreshAsync()
    {
        long version;
        lock (_sync)
        {
            if (_stopped)
            {
                return;
            }

            version = ++_version;
        }

        try
        {
            // WinRT callbacks may arrive on a UI thread; image decoding stays off that thread.
            var state = await Task.Run(() => _source.ReadAsync(_stopping), _stopping).ConfigureAwait(false);
            lock (_sync)
            {
                if (_stopped || version != _version)
                {
                    return;
                }

                Volatile.Write(ref _state, state);
            }

            NotifyStateChanged();
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "无法读取系统媒体状态");
            lock (_sync)
            {
                if (_stopped || version != _version)
                {
                    return;
                }

                Volatile.Write(ref _state, MediaState.Empty);
            }

            NotifyStateChanged();
        }
    }

    public Task<bool> ExecuteAsync(MediaAction action, CancellationToken cancellationToken = default)
    {
        var state = State;
        var enabled = action switch
        {
            MediaAction.Previous => state.CanPrevious,
            MediaAction.Next => state.CanNext,
            _ => state.CanPlayPause
        };
        return state.HasSession && enabled
            ? RunOperationAsync(token => _source.ExecuteAsync(action, token), cancellationToken)
            : Task.FromResult(false);
    }

    public Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        var state = State;
        if (!state.HasSession || !state.CanSeek || state.MaxSeekTime < state.MinSeekTime)
        {
            return Task.FromResult(false);
        }

        var target = TimeSpan.FromTicks(Math.Clamp(position.Ticks, state.MinSeekTime.Ticks, state.MaxSeekTime.Ticks));
        return RunOperationAsync(token => _source.SeekAsync(target, token), cancellationToken);
    }

    private async Task<bool> RunOperationAsync(Func<CancellationToken, Task<bool>> operation, CancellationToken cancellationToken)
    {
        if (_stopped)
        {
            return false;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping);
        try
        {
            return await operation(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "媒体控制请求失败");
            return false;
        }
        finally
        {
            await RefreshAsync().ConfigureAwait(false);
        }
    }

    private void NotifyStateChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            ++_version;
        }

        _source.Changed -= OnSourceChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _timer?.Dispose();
        _source.Dispose();
    }
}
