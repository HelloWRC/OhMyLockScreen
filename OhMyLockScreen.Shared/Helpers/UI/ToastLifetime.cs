using OhMyLockScreen.Shared.Models.UI;

namespace OhMyLockScreen.Shared.Helpers.UI;

/// <summary>使用单调时钟计算剩余时间；所有状态变化经所属窗口的调度器执行。</summary>
internal sealed class ToastLifetime : IDisposable
{
    private readonly ToastMessage _message;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private TimeSpan _remaining;
    private long _startedAt;
    private bool _running;
    private bool _disposed;

    internal ToastLifetime(ToastMessage message, TimeProvider time, Action<Action> dispatch)
    {
        _message = message;
        _time = time;
        _remaining = message.Duration > TimeSpan.Zero ? message.Duration : TimeSpan.Zero;
        _timer = time.CreateTimer(_ => dispatch(OnElapsed), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    internal TimeSpan Remaining => _running
        ? MaxZero(_remaining - _time.GetElapsedTime(_startedAt))
        : _remaining;

    internal void Pause()
    {
        if (_disposed || !_running)
        {
            return;
        }

        _remaining = Remaining;
        _running = false;
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    internal void Resume()
    {
        if (_disposed || _running || !_message.AutoClose || !_message.IsOpen)
        {
            return;
        }

        _startedAt = _time.GetTimestamp();
        _running = true;
        // Long durations are re-armed when the platform timer's maximum is reached.
        _timer.Change(TimeSpan.FromMilliseconds(Math.Min(Remaining.TotalMilliseconds, uint.MaxValue - 1)), Timeout.InfiniteTimeSpan);
    }

    private void OnElapsed()
    {
        if (_disposed || !_running || !_message.IsOpen)
        {
            return;
        }

        if (Remaining > TimeSpan.Zero)
        {
            Pause();
            Resume();
            return;
        }

        _remaining = TimeSpan.Zero;
        _running = false;
        _message.Close();
    }

    public void Dispose()
    {
        _disposed = true;
        _running = false;
        _timer.Dispose();
    }

    private static TimeSpan MaxZero(TimeSpan value) => value > TimeSpan.Zero ? value : TimeSpan.Zero;
}
