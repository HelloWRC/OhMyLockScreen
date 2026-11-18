using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using OhMyLockScreen.Shared.Models.LockScreenProvider;

namespace OhMyLockScreen.NativeHost;

internal static partial class LockScreenProviderHost
{
    private const int ErrorInsufficientBuffer = 122;
    private const string LockScreenKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Lock Screen";
    private const string LockAppAumIdValueName = "LockAppAumId";
    private static readonly TimeSpan RequestWaitTimeout = TimeSpan.FromSeconds(2);

    internal static async Task<int> RunAsync()
    {
        var directory = GetBridgeDirectory();
        var requestPath = Path.Combine(directory, LockScreenProviderProtocol.RequestFileName);
        var responsePath = Path.Combine(directory, LockScreenProviderProtocol.ResponseFileName);

        LockScreenProviderRequest? request = null;
        string? processingPath = null;
        try
        {
            Directory.CreateDirectory(directory);
            var requestDeadline = DateTime.UtcNow + RequestWaitTimeout;
            while (!File.Exists(requestPath) && DateTime.UtcNow < requestDeadline)
            {
                await Task.Delay(50);
            }

            if (!File.Exists(requestPath))
            {
                return 2;
            }

            var requestJson = await File.ReadAllTextAsync(requestPath);
            request = JsonSerializer.Deserialize(
                requestJson,
                LockScreenProviderJsonContext.Default.LockScreenProviderRequest);
            var receivedTimestamp = Stopwatch.GetTimestamp();
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
            {
                return 3;
            }

            processingPath = Path.Combine(
                directory,
                $"{Path.GetFileNameWithoutExtension(LockScreenProviderProtocol.RequestFileName)}-{request.RequestId}.processing");
            File.Move(requestPath, processingPath, true);

            LockScreenProviderResponse response;
            if (request.Operation == LockScreenProviderOperation.Unlock)
            {
                response = LockScreenUnlockCommand.Execute(request, new LockAppHostGlobalUnlockSignal());
                await LockScreenUnlockCommand.TryAppendReceiveLatencyAsync(directory, request, receivedTimestamp, "File");
            }
            else if (request.Operation == LockScreenProviderOperation.Query)
            {
                response = QueryProvider(request.RequestId);
            }
            else
            {
                throw new InvalidOperationException("未知的锁屏提供程序操作。");
            }

            await TryWriteDiagnosticAsync(directory, request, response);
            await WriteAtomicAsync(
                responsePath,
                JsonSerializer.Serialize(response, LockScreenProviderJsonContext.Default.LockScreenProviderResponse));
            return response.Success ? 0 : 1;
        }
        catch (Exception exception)
        {
            if (request != null)
            {
                var response = new LockScreenProviderResponse(
                    request.RequestId,
                    false,
                    false,
                    null,
                    exception.Message);
                await TryWriteDiagnosticAsync(directory, request, response);
                await TryWriteResponseAsync(responsePath, response);
            }

            return 1;
        }
        finally
        {
            TryDelete(processingPath);
            TryDelete(requestPath);
        }
    }

    private static LockScreenProviderResponse QueryProvider(string requestId)
    {
        var expectedAumId = GetPackageFamilyName() + "!App";
        var state = CreateRegistryAccess().Read(expectedAumId);
        return new LockScreenProviderResponse(
            requestId,
            true,
            state.IsEnabled,
            state.Value,
            null);
    }

    private static LockScreenRegistryAccess CreateRegistryAccess()
    {
        // Older deployments may have selected the provider from the other package architecture.
        var views = Environment.Is64BitOperatingSystem
            ? new ILockScreenRegistryView[]
            {
                new LockScreenRegistryView(RegistryView.Registry64),
                new LockScreenRegistryView(RegistryView.Registry32)
            }
            : [new LockScreenRegistryView(RegistryView.Registry32)];
        return new LockScreenRegistryAccess(views, LockScreenKeyPath, LockAppAumIdValueName);
    }

    private static async Task TryWriteDiagnosticAsync(
        string directory,
        LockScreenProviderRequest request,
        LockScreenProviderResponse response)
    {
        try
        {
            var diagnostic = string.Join(
                Environment.NewLine,
                $"TimeUtc={DateTimeOffset.UtcNow:O}",
                $"PackageFamilyName={GetPackageFamilyName()}",
                $"RequestId={request.RequestId}",
                $"Operation={request.Operation}",
                $"Success={response.Success}",
                $"IsEnabled={response.IsEnabled}",
                $"Value={response.Value ?? "<null>"}",
                $"Error={response.Error ?? "<null>"}");
            await WriteAtomicAsync(
                Path.Combine(directory, LockScreenProviderProtocol.DiagnosticFileName),
                diagnostic);
        }
        catch
        {
        }
    }

    private static async Task TryWriteResponseAsync(
        string responsePath,
        LockScreenProviderResponse response)
    {
        try
        {
            await WriteAtomicAsync(
                responsePath,
                JsonSerializer.Serialize(response, LockScreenProviderJsonContext.Default.LockScreenProviderResponse));
        }
        catch
        {
        }
    }

    private static async Task WriteAtomicAsync(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Helper 响应路径没有父目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, Path.GetRandomFileName());
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static string GetBridgeDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            localAppData,
            "Packages",
            GetPackageFamilyName(),
            "LocalState",
            LockScreenProviderProtocol.RequestDirectoryName);
    }

    private static unsafe string GetPackageFamilyName()
    {
        uint length = 0;
        var result = GetCurrentPackageFamilyName(ref length, null);
        if (result != ErrorInsufficientBuffer)
        {
            throw new Win32Exception(result, "无法读取当前包的 Package Family Name。");
        }

        var packageFamilyName = new char[checked((int)length)];
        fixed (char* buffer = packageFamilyName)
        {
            result = GetCurrentPackageFamilyName(ref length, buffer);
        }

        if (result != 0)
        {
            throw new Win32Exception(result, "无法读取当前包的 Package Family Name。");
        }

        return new string(packageFamilyName, 0, checked((int)length - 1));
    }

    private static void TryDelete(string? path)
    {
        if (path == null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetCurrentPackageFamilyName(
        ref uint packageFamilyNameLength,
        char* packageFamilyName);
}
