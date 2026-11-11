using OhMyLockScreen.Shared.Models.Audio;

namespace OhMyLockScreen.Shared.Abstractions.Services.Audio;

public interface IAudioSpectrumService
{
    AudioSpectrumSnapshot State
    {
        get;
    }
    event EventHandler? Changed;
    IDisposable Acquire();
}

/// <summary>隔离平台采集；返回或取消时必须已释放本轮订阅及持有的资源。</summary>
public interface IAudioSpectrumSource
{
    bool IsSuspended
    {
        get;
    }
    event EventHandler? Changed;
    Task CaptureAsync(Action<AudioSpectrumSnapshot> publish, CancellationToken cancellationToken);
}
