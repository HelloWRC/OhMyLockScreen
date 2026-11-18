using OhMyLockScreen.Shared.Models.LockScreenProvider;

namespace OhMyLockScreen.Services.LockScreenProvider;

public sealed class LockScreenReplacementService(ILockScreenHelperClient client)
{
    public async Task<bool> IsEnabledAsync()
    {
        var response = await client.ExecuteAsync(LockScreenProviderOperation.Query).ConfigureAwait(false);
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error ?? "锁屏辅助进程未能完成请求。");
        }

        return response.IsEnabled;
    }
}
