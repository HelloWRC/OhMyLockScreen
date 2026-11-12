using Microsoft.Extensions.Logging;
using OhMyLockScreen.Shared.Models.LockScreenProvider;

namespace OhMyLockScreen.Services.LockScreenProvider;

public sealed class LockScreenUnlockService(
    SettingsService settings,
    ILockScreenHelperClient client,
    ILogger<LockScreenUnlockService> logger) : ILockScreenUnlockService
{
    public async Task<bool> TryUnlockAsync(CancellationToken cancellationToken = default)
    {
        if (settings.Settings.CompatibilityUnlockMode)
        {
            return false;
        }

        try
        {
            var response = await client.ExecuteAsync(
                LockScreenProviderOperation.Unlock,
                cancellationToken).ConfigureAwait(false);
            if (response.Success)
            {
                return true;
            }

            logger.LogWarning(
                "锁屏辅助进程未能发送系统解锁事件：{Error}",
                response.Error ?? "未返回错误详情");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("发送系统解锁事件已取消");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "发送系统解锁事件失败");
        }

        return false;
    }
}