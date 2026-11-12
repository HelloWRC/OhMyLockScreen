using OhMyLockScreen.Shared.Models.LockScreenProvider;

namespace OhMyLockScreen.Services.LockScreenProvider;

public interface ILockScreenHelperClient
{
    Task<LockScreenProviderResponse> ExecuteAsync(
        LockScreenProviderOperation operation,
        CancellationToken cancellationToken = default);
}
