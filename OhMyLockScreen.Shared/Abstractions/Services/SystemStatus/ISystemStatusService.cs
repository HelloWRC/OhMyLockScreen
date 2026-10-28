using OhMyLockScreen.Shared.Models.SystemStatus;

namespace OhMyLockScreen.Shared.Abstractions.Services.SystemStatus;

public interface ISystemStatusService
{
    SystemStatusSnapshot GetState(SystemStatusKind kind);
    event EventHandler<SystemStatusChangedEventArgs>? Changed;
    IDisposable Acquire(SystemStatusKind kind);
}

/// <summary>Starts one native observer, including its initial read. Disposal prevents further updates.</summary>
public interface ISystemStatusSource
{
    IDisposable Watch(SystemStatusKind kind, Action<SystemStatusSnapshot> publish);
}
