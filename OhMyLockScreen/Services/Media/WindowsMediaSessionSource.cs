using System.IO;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Shared.Models.Media;
using Windows.Graphics.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Windows.Storage;

namespace OhMyLockScreen.Services.Media;

/// <summary>只访问系统当前会话，不创建播放器或发布自己的 SMTC 会话。</summary>
public sealed class WindowsMediaSessionSource(ILogger<WindowsMediaSessionSource> logger) : IMediaSessionSource
{
    private readonly object _sync = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private Task<MediaState>? _metadata;
    private bool _disposed;

    public event EventHandler? Changed;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _manager = manager;
            manager.CurrentSessionChanged += OnCurrentSessionChanged;
            manager.SessionsChanged += OnSessionsChanged;
            SelectSession();
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => UpdateSession();
    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => UpdateSession();

    private void UpdateSession()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            SelectSession();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SelectSession()
    {
        var session = _manager?.GetCurrentSession();
        if (ReferenceEquals(session, _session))
        {
            return;
        }

        DetachSession();
        _session = session;
        _metadata = null;
        if (session != null)
        {
            session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        lock (_sync)
        {
            if (_disposed || !ReferenceEquals(sender, _session))
            {
                return;
            }

            _metadata = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => NotifyIfCurrent(sender);
    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => NotifyIfCurrent(sender);

    private void NotifyIfCurrent(GlobalSystemMediaTransportControlsSession sender)
    {
        lock (_sync)
        {
            if (_disposed || !ReferenceEquals(sender, _session))
            {
                return;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<MediaState> ReadAsync(CancellationToken cancellationToken)
    {
        GlobalSystemMediaTransportControlsSession session;
        Task<MediaState> metadata;
        lock (_sync)
        {
            if (_disposed || _session == null)
            {
                return MediaState.Empty;
            }

            session = _session;
            metadata = _metadata ??= ReadMetadataAsync(session, cancellationToken);
        }

        MediaState state;
        try
        {
            state = await metadata.ConfigureAwait(false);
        }
        catch
        {
            lock (_sync)
            {
                if (ReferenceEquals(_metadata, metadata))
                {
                    _metadata = null;
                }
            }

            throw;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var controls = playback.Controls;
        var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        return state with
        {
            HasSession = true,
            SourceAppId = session.SourceAppUserModelId,
            IsPlaying = playing,
            PlaybackStatus = (MediaPlaybackStatus)(int)playback.PlaybackStatus,
            CanPrevious = controls.IsPreviousEnabled,
            CanNext = controls.IsNextEnabled,
            CanPlayPause = controls.IsPlayPauseToggleEnabled || (playing ? controls.IsPauseEnabled : controls.IsPlayEnabled),
            CanSeek = controls.IsPlaybackPositionEnabled && timeline.MaxSeekTime > timeline.MinSeekTime,
            PlaybackRate = playback.PlaybackRate ?? 1,
            Position = timeline.Position,
            StartTime = timeline.StartTime,
            EndTime = timeline.EndTime,
            MinSeekTime = timeline.MinSeekTime,
            MaxSeekTime = timeline.MaxSeekTime,
            LastUpdatedTime = timeline.LastUpdatedTime
        };
    }

    private async Task<MediaState> ReadMetadataAsync(GlobalSystemMediaTransportControlsSession session, CancellationToken cancellationToken)
    {
        var properties = await session.TryGetMediaPropertiesAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var state = new MediaState
        {
            Title = properties.Title ?? "",
            Artist = properties.Artist ?? "",
            Subtitle = properties.Subtitle ?? "",
            AlbumTitle = properties.AlbumTitle ?? "",
            AlbumArtist = properties.AlbumArtist ?? "",
            Genres = string.Join("、", properties.Genres),
            TrackNumber = properties.TrackNumber,
            AlbumTrackCount = properties.AlbumTrackCount,
            PlaybackType = properties.PlaybackType?.ToString() ?? ""
        };

        if (properties.Thumbnail == null)
        {
            return state;
        }

        try
        {
            using var stream = await properties.Thumbnail.OpenReadAsync();
            var artwork = await ReadArtworkAsync(stream, cancellationToken);
            return state with
            {
                Cover = artwork.Cover,
                CoverWidth = artwork.CoverWidth,
                CoverHeight = artwork.CoverHeight,
                ThemeColor = artwork.ThemeColor
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "无法解码媒体封面");
            return state;
        }
    }

    public async Task<MediaState> ReadPlaceholderAsync(CancellationToken cancellationToken)
    {
        var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///OhMyLockScreen.Shared/Assets/Media/PreviewCover.png"));
        using var stream = await file.OpenReadAsync();
        return await ReadArtworkAsync(stream, cancellationToken);
    }

    internal static async Task<MediaState> ReadArtworkAsync(IRandomAccessStream stream, CancellationToken cancellationToken)
    {
        // Bound the shared encoded image; malformed sources must not allocate unbounded buffers.
        if (stream.Size == 0 || stream.Size > 32 * 1024 * 1024)
        {
            return MediaState.Empty;
        }

        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[(int)stream.Size];
        reader.ReadBytes(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            new BitmapTransform { ScaledWidth = 48, ScaledHeight = 48 },
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        cancellationToken.ThrowIfCancellationRequested();
        return new MediaState
        {
            Cover = bytes,
            CoverWidth = decoder.OrientedPixelWidth,
            CoverHeight = decoder.OrientedPixelHeight,
            ThemeColor = MediaThemeColor.Extract(pixels.DetachPixelData())
        };
    }

    public async Task<bool> ExecuteAsync(MediaAction action, CancellationToken cancellationToken)
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_sync)
        {
            session = _disposed ? null : _session;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (session == null)
        {
            return false;
        }

        var info = session.GetPlaybackInfo();
        return action switch
        {
            MediaAction.Previous when info.Controls.IsPreviousEnabled => await session.TrySkipPreviousAsync(),
            MediaAction.Next when info.Controls.IsNextEnabled => await session.TrySkipNextAsync(),
            MediaAction.PlayPause when info.Controls.IsPlayPauseToggleEnabled => await session.TryTogglePlayPauseAsync(),
            MediaAction.PlayPause when info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                && info.Controls.IsPauseEnabled => await session.TryPauseAsync(),
            MediaAction.PlayPause when info.Controls.IsPlayEnabled => await session.TryPlayAsync(),
            _ => false
        };
    }

    public async Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_sync)
        {
            session = _disposed ? null : _session;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (session == null || !session.GetPlaybackInfo().Controls.IsPlaybackPositionEnabled)
        {
            return false;
        }

        var timeline = session.GetTimelineProperties();
        return timeline.MaxSeekTime > timeline.MinSeekTime
            && await session.TryChangePlaybackPositionAsync(Math.Clamp(position.Ticks, timeline.MinSeekTime.Ticks, timeline.MaxSeekTime.Ticks));
    }

    private void DetachSession()
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
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
            if (_manager != null)
            {
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _manager.SessionsChanged -= OnSessionsChanged;
            }

            DetachSession();
            _session = null;
            _metadata = null;
            _manager = null;
        }
    }
}
