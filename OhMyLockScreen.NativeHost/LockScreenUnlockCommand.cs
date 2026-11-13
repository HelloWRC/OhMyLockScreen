using System.ComponentModel;
using OhMyLockScreen.Shared.Models.LockScreenProvider;

namespace OhMyLockScreen.NativeHost;

internal static class LockScreenUnlockCommand
{
    internal static LockScreenProviderResponse Execute(
        LockScreenProviderRequest request,
        LockAppHostGlobalUnlockSignal signal)
    {
        if (request.Operation != LockScreenProviderOperation.Unlock)
        {
            throw new InvalidOperationException("请求不是系统解锁操作。");
        }

        if (signal.TrySignal(out var errorCode))
        {
            return new LockScreenProviderResponse(request.RequestId, true, false, null, null);
        }

        var error = new Win32Exception(errorCode);
        return new LockScreenProviderResponse(
            request.RequestId,
            false,
            false,
            null,
            $"发送 LockAppHost 解锁事件失败：{error.Message}（Win32 {errorCode}）");
    }
}