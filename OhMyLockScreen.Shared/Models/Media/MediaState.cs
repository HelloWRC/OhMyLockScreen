using System.Globalization;

namespace OhMyLockScreen.Shared.Models.Media;

public enum MediaTextField
{
    Title,
    Artist,
    Subtitle,
    AlbumTitle,
    AlbumArtist,
    Genres,
    TrackNumber,
    AlbumTrackCount,
    PlaybackType,
    SourceAppId,
    Position,
    Duration,
    Remaining
}

public enum MediaAction
{
    PlayPause,
    Previous,
    Next
}

public enum MediaVariant
{
    IconOnly,
    Mini,
    Lite,
    Immersive,
    Full,
    ImmersiveFull
}

public enum MediaPlaybackStatus
{
    Closed,
    Opened,
    Changing,
    Stopped,
    Playing,
    Paused
}

/// <summary>不包含任何 UI 对象的媒体快照，可以安全地在视图之间共享。</summary>
public sealed record MediaState
{
    public static MediaState Empty { get; } = new();
    public bool HasSession { get; init; }
    public bool IsPlaying { get; init; }
    public MediaPlaybackStatus PlaybackStatus { get; init; }
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string AlbumTitle { get; init; } = "";
    public string AlbumArtist { get; init; } = "";
    public string Genres { get; init; } = "";
    public int TrackNumber { get; init; }
    public int AlbumTrackCount { get; init; }
    public string PlaybackType { get; init; } = "";
    public string SourceAppId { get; init; } = "";
    public ReadOnlyMemory<byte> Cover { get; init; }
    public uint CoverWidth { get; init; }
    public uint CoverHeight { get; init; }
    /// <summary>未经暗化的封面主题色，采用 ARGB 格式。</summary>
    public uint ThemeColor { get; init; } = 0xFF606060;
    public TimeSpan StartTime { get; init; }
    public TimeSpan EndTime { get; init; }
    public TimeSpan Position { get; init; }
    public TimeSpan MinSeekTime { get; init; }
    public TimeSpan MaxSeekTime { get; init; }
    public DateTimeOffset LastUpdatedTime { get; init; }
    public double PlaybackRate { get; init; } = 1;
    public bool CanPlayPause { get; init; }
    public bool CanPrevious { get; init; }
    public bool CanNext { get; init; }
    public bool CanSeek { get; init; }
    public TimeSpan Duration => EndTime > StartTime ? EndTime - StartTime : TimeSpan.Zero;

    public TimeSpan GetPosition(DateTimeOffset now)
    {
        var seconds = Position.TotalSeconds;
        if (IsPlaying && LastUpdatedTime != default && now > LastUpdatedTime && double.IsFinite(PlaybackRate))
        {
            seconds += (now - LastUpdatedTime).TotalSeconds * PlaybackRate;
        }

        var ticks = seconds * TimeSpan.TicksPerSecond;
        var maximum = EndTime > StartTime ? EndTime.Ticks : long.MaxValue;
        if (ticks >= maximum)
        {
            return TimeSpan.FromTicks(maximum);
        }

        return ticks <= StartTime.Ticks ? StartTime : TimeSpan.FromTicks((long)ticks);
    }

    public string GetText(MediaTextField field, DateTimeOffset now)
    {
        if (!HasSession)
        {
            return "";
        }

        var elapsed = GetPosition(now) - StartTime;
        return field switch
        {
            MediaTextField.Title => Title,
            MediaTextField.Artist => Artist,
            MediaTextField.Subtitle => Subtitle,
            MediaTextField.AlbumTitle => AlbumTitle,
            MediaTextField.AlbumArtist => AlbumArtist,
            MediaTextField.Genres => Genres,
            MediaTextField.TrackNumber => TrackNumber <= 0 ? "" : TrackNumber.ToString(CultureInfo.InvariantCulture),
            MediaTextField.AlbumTrackCount => AlbumTrackCount <= 0 ? "" : AlbumTrackCount.ToString(CultureInfo.InvariantCulture),
            MediaTextField.PlaybackType => PlaybackType,
            MediaTextField.SourceAppId => SourceAppId,
            MediaTextField.Position => FormatTime(elapsed),
            MediaTextField.Duration => Duration > TimeSpan.Zero ? FormatTime(Duration) : "",
            MediaTextField.Remaining => Duration > TimeSpan.Zero ? FormatTime(Duration - elapsed) : "",
            _ => ""
        };
    }

    private static string FormatTime(TimeSpan time)
    {
        var seconds = Math.Max(0, (long)time.TotalSeconds);
        return seconds >= 3600
            ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
            : $"{seconds / 60}:{seconds % 60:00}";
    }
}
