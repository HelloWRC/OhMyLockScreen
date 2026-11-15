using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Shared.Models.LockScreenProvider;
using Windows.ApplicationModel;
using Windows.Storage;

namespace OhMyLockScreen.Services.LockScreenProvider;

public sealed class LockScreenUnlockService(
    SettingsService settings,
    ILockScreenHelperClient client,
    ILogger<LockScreenUnlockService> logger) : ILockScreenUnlockService
{
    public Func<Task<bool>> PrepareUnlock()
    {
        if (settings.Settings.CompatibilityUnlockMode)
        {
            return static () => Task.FromResult(false);
        }

        var dismissal = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = Task.Run(() => RunPreparedUnlockAsync(dismissal.Task));
        return () =>
        {
            dismissal.TrySetResult(Stopwatch.GetTimestamp());
            return operation;
        };
    }

    private async Task<bool> RunPreparedUnlockAsync(Task<long> dismissal)
    {
        var id = Guid.NewGuid().ToString("N");
        var sending = false;
        try
        {
            using var preparation = new CancellationTokenSource(LockScreenProviderProtocol.UnlockPipeTimeout);
            await using var pipe = new NamedPipeServerStream(
                @"LOCAL\" + LockScreenProviderProtocol.UnlockPipeNamePrefix + id,
                PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await ConnectPipeAsync(pipe, id, preparation.Token).ConfigureAwait(false);
            var startTimestamp = await dismissal.WaitAsync(preparation.Token).ConfigureAwait(false);
            if (settings.Settings.CompatibilityUnlockMode)
            {
                return false;
            }

            var request = new LockScreenProviderRequest(id, LockScreenProviderOperation.Unlock)
            {
                UnlockStartTimestamp = startTimestamp
            };
            var json = JsonSerializer.Serialize(request, LockScreenProviderJsonContext.Default.LockScreenProviderRequest);
            using var exchange = new CancellationTokenSource(LockScreenProviderProtocol.UnlockPipeTimeout);
            using var writer = new StreamWriter(pipe, leaveOpen: true);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            sending = true;
            await writer.WriteLineAsync(json.AsMemory(), exchange.Token).ConfigureAwait(false);
            await writer.FlushAsync(exchange.Token).ConfigureAwait(false);
            var reply = await reader.ReadLineAsync(exchange.Token).ConfigureAwait(false);
            var response = reply == null ? null : JsonSerializer.Deserialize(
                reply, LockScreenProviderJsonContext.Default.LockScreenProviderResponse);
            if (response?.RequestId != id)
            {
                throw new InvalidDataException("解锁管道未返回匹配的响应。");
            }

            return ReportResponse(response);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "锁屏解锁管道通信失败");
        }

        // A missing reply can follow a successful signal, so only retry before sending.
        if (sending)
        {
            return false;
        }

        var unlockStartTimestamp = await dismissal.ConfigureAwait(false);
        return await TryFileUnlockAsync(unlockStartTimestamp).ConfigureAwait(false);
    }

    private static async Task ConnectPipeAsync(NamedPipeServerStream pipe, string id, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(
            ApplicationData.Current.LocalFolder.Path,
            LockScreenProviderProtocol.RequestDirectoryName,
            LockScreenProviderProtocol.UnlockPipeDirectoryName);
        Directory.CreateDirectory(directory);
        var marker = Path.Combine(directory, id + LockScreenProviderProtocol.UnlockPipeRequestExtension);
        try
        {
            // The marker only identifies an existing pipe. The unlock command travels over the pipe after dismissal.
            await File.WriteAllTextAsync(marker, string.Empty, cancellationToken).ConfigureAwait(false);
            await LaunchPipeHostAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(marker);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task LaunchPipeHostAsync() =>
        await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync(
            LockScreenProviderProtocol.UnlockPipeParameterGroupId);

    public Task<bool> TryUnlockAsync(CancellationToken cancellationToken = default) =>
        TryFileUnlockAsync(null, cancellationToken);

    private async Task<bool> TryFileUnlockAsync(long? startTimestamp, CancellationToken cancellationToken = default)
    {
        if (settings.Settings.CompatibilityUnlockMode)
        {
            return false;
        }

        try
        {
            var response = await client.ExecuteAsync(
                LockScreenProviderOperation.Unlock,
                cancellationToken,
                startTimestamp).ConfigureAwait(false);
            return ReportResponse(response);
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

    private bool ReportResponse(LockScreenProviderResponse response)
    {
        if (!response.Success)
        {
            logger.LogWarning(
                "锁屏辅助进程未能发送系统解锁事件：{Error}",
                response.Error ?? "未返回错误详情");
        }

        return response.Success;
    }
}
