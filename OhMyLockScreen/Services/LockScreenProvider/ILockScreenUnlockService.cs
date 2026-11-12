namespace OhMyLockScreen.Services.LockScreenProvider;

public interface ILockScreenUnlockService
{
    Task<bool> TryUnlockAsync(CancellationToken cancellationToken = default);
}