using System.ComponentModel;
using OhMyLockScreen.Shared.Models.Media;

namespace OhMyLockScreen.Shared.Abstractions.Services.Media;

public interface IMediaStateService : INotifyPropertyChanged
{
    MediaState State { get; }
    MediaState Placeholder { get; }
    Task<bool> ExecuteAsync(MediaAction action, CancellationToken cancellationToken = default);
    Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
}

/// <summary>隔离 Windows 会话访问，允许在模拟宿主中验证服务。</summary>
public interface IMediaSessionSource : IDisposable
{
    event EventHandler? Changed;
    Task StartAsync(CancellationToken cancellationToken);
    Task<MediaState> ReadAsync(CancellationToken cancellationToken);
    Task<MediaState> ReadPlaceholderAsync(CancellationToken cancellationToken);
    Task<bool> ExecuteAsync(MediaAction action, CancellationToken cancellationToken);
    Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken);
}
