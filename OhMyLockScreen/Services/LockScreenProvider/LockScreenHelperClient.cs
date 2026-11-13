using System.Text;
using System.Text.Json;
using OhMyLockScreen.Shared.Models.LockScreenProvider;
using Windows.ApplicationModel;
using Windows.Storage;

namespace OhMyLockScreen.Services.LockScreenProvider;

public sealed class LockScreenHelperClient : ILockScreenHelperClient, IDisposable
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private bool _disposed;

    public async Task<LockScreenProviderResponse> ExecuteAsync(
        LockScreenProviderOperation operation,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.Combine(
                ApplicationData.Current.LocalFolder.Path,
                LockScreenProviderProtocol.RequestDirectoryName);
            var requestPath = Path.Combine(directory, LockScreenProviderProtocol.RequestFileName);
            var responsePath = Path.Combine(directory, LockScreenProviderProtocol.ResponseFileName);
            var request = new LockScreenProviderRequest(Guid.NewGuid().ToString("N"), operation);
            var processingPath = Path.Combine(
                directory,
                $"request-{request.RequestId}.processing");
            try
            {
                Directory.CreateDirectory(directory);
                File.Delete(requestPath);
                File.Delete(responsePath);
                File.Delete(processingPath);

                await WriteAtomicAsync(
                    requestPath,
                    JsonSerializer.Serialize(
                        request,
                        LockScreenProviderJsonContext.Default.LockScreenProviderRequest),
                    cancellationToken).ConfigureAwait(false);

                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync(
                    LockScreenProviderProtocol.HelperParameterGroupId);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ResponseTimeout);
                while (!File.Exists(responsePath))
                {
                    await Task.Delay(50, timeout.Token).ConfigureAwait(false);
                }

                var responseJson = await File.ReadAllTextAsync(responsePath, timeout.Token).ConfigureAwait(false);
                var response = JsonSerializer.Deserialize(
                    responseJson,
                    LockScreenProviderJsonContext.Default.LockScreenProviderResponse)
                    ?? throw new InvalidDataException("锁屏辅助进程返回了空响应。");
                if (!string.Equals(response.RequestId, request.RequestId, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("锁屏辅助进程返回了不匹配的响应。");
                }

                return response;
            }
            catch (Exception exception)
            {
                await TryWriteClientDiagnosticAsync(directory, request, exception);
                if (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("等待锁屏辅助进程响应超时。", exception);
                }
                throw;
            }
            finally
            {
                TryDelete(processingPath);
                TryDelete(requestPath);
                TryDelete(responsePath);
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static async Task WriteAtomicAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("锁屏辅助进程请求路径没有父目录。");
        var temporaryPath = Path.Combine(directory, Path.GetRandomFileName());
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static async Task TryWriteClientDiagnosticAsync(
        string directory,
        LockScreenProviderRequest request,
        Exception exception)
    {
        try
        {
            var diagnostic = string.Join(
                Environment.NewLine,
                $"TimeUtc={DateTimeOffset.UtcNow:O}",
                $"RequestId={request.RequestId}",
                $"Operation={request.Operation}",
                $"Exception={exception.GetType().FullName}",
                $"Message={exception.Message}");
            await WriteAtomicAsync(
                Path.Combine(directory, LockScreenProviderProtocol.ClientDiagnosticFileName),
                diagnostic,
                CancellationToken.None);
        }
        catch
        {
        }
    }
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
