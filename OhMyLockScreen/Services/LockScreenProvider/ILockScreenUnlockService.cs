namespace OhMyLockScreen.Services.LockScreenProvider;

public interface ILockScreenUnlockService
{
    Func<Task<bool>> PrepareUnlock();
    Task<bool> TryUnlockAsync(CancellationToken cancellationToken = default);
}
