using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using OhMyLockScreen.Shared.Models.LockScreenProvider;

namespace OhMyLockScreen.NativeHost;

internal static class LockScreenUnlockCommand
{
    internal static async Task<LockScreenProviderResponse?> ServePipeAsync(
        Stream pipe,
        string directory,
        string requestId,
        LockAppHostGlobalUnlockSignal signal,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        var json = await reader.ReadLineAsync(cancellationToken);
        if (json == null)
        {
            return null;
        }

        var request = JsonSerializer.Deserialize(json, LockScreenProviderJsonContext.Default.LockScreenProviderRequest);
        var receivedTimestamp = Stopwatch.GetTimestamp();
        if (request is not { Operation: LockScreenProviderOperation.Unlock } || request.RequestId != requestId)
        {
            return null;
        }

        var response = Execute(request, signal);
        await TryAppendReceiveLatencyAsync(directory, request, receivedTimestamp, "Pipe");
        var reply = JsonSerializer.Serialize(response, LockScreenProviderJsonContext.Default.LockScreenProviderResponse);
        using var writer = new StreamWriter(pipe, leaveOpen: true);
        await writer.WriteLineAsync(reply.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
        return response;
    }

    internal static async Task TryAppendReceiveLatencyAsync(
        string directory,
        LockScreenProviderRequest request,
        long receivedTimestamp,
        string transport)
    {
        if (request.UnlockStartTimestamp is not long startTimestamp ||
            startTimestamp < 0 || startTimestamp > receivedTimestamp)
        {
            return;
        }

        // Stopwatch uses the same Windows performance counter in both processes.
        var elapsed = Stopwatch.GetElapsedTime(startTimestamp, receivedTimestamp);
        var line = FormattableString.Invariant(
            $"{DateTimeOffset.UtcNow:O} Transport={transport} RequestId={request.RequestId} LatencyMs={elapsed.TotalMilliseconds:F3}");
        try
        {
            await File.AppendAllTextAsync(
                Path.Combine(directory, LockScreenProviderProtocol.UnlockLatencyFileName),
                line + Environment.NewLine);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

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
