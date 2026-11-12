using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using OhMyLockScreen.LockScreenHelper;
using OhMyLockScreen.Models;
using OhMyLockScreen.Services.LockScreenProvider;
using OhMyLockScreen.Shared.Models.LockScreenProvider;

internal static class LockScreenReplacementTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    internal static void Run(Action<string, Action> run)
    {
        run("Lock-screen replacement property is excluded from settings JSON", () =>
        {
            var property = typeof(Settings).GetProperty(nameof(Settings.ReplaceSystemLockScreen));
            Check(property != null, "Replacement property was not found.");
            Check(
                property!.GetCustomAttributes(typeof(JsonIgnoreAttribute), true).Length == 1,
                "Replacement property is not marked JsonIgnore.");
        });

        run("Lock-screen helper protocol round trips with source generation", () =>
        {
            var request = new LockScreenProviderRequest("abc", LockScreenProviderOperation.Enable);
            var requestJson = JsonSerializer.Serialize(
                request,
                LockScreenProviderJsonContext.Default.LockScreenProviderRequest);
            var parsedRequest = JsonSerializer.Deserialize(
                requestJson,
                LockScreenProviderJsonContext.Default.LockScreenProviderRequest);
            Check(parsedRequest == request, "Protocol request did not round trip.");

            var response = new LockScreenProviderResponse("abc", true, true, "value", null);
            var responseJson = JsonSerializer.Serialize(
                response,
                LockScreenProviderJsonContext.Default.LockScreenProviderResponse);
            var parsedResponse = JsonSerializer.Deserialize(
                responseJson,
                LockScreenProviderJsonContext.Default.LockScreenProviderResponse);
            Check(parsedResponse == response, "Protocol response did not round trip.");
        });

        run("Lock-screen registry restores the default provider in both views", () =>
        {
            const string expectedAumId = "dev.test!App";
            var view64 = new FakeRegistryView(LockScreenRegistryViewKind.Registry64);
            var view32 = new FakeRegistryView(LockScreenRegistryViewKind.Registry32);
            view32.Values["LockAppAumId"] = expectedAumId;
            var registry = new LockScreenRegistryAccess(
                [view64, view32],
                @"Software\Test\Lock Screen",
                "LockAppAumId");

            Check(registry.Read(expectedAumId).IsEnabled, "Legacy 32-bit provider was not detected.");

            registry.Disable(expectedAumId, LockScreenProviderProtocol.DefaultLockAppAumId);

            Check(
                view64.Values["LockAppAumId"] == LockScreenProviderProtocol.DefaultLockAppAumId,
                "64-bit provider was not restored.");
            Check(
                view32.Values["LockAppAumId"] == LockScreenProviderProtocol.DefaultLockAppAumId,
                "32-bit provider was not restored.");

            registry.Enable(expectedAumId);

            Check(view64.Values["LockAppAumId"] == expectedAumId, "64-bit provider was not written.");
            Check(view32.Values["LockAppAumId"] == expectedAumId, "32-bit provider was not written.");
        });
    }

    internal static async Task RunAsync(Func<string, Func<Task>, Task> runAsync)
    {
        await runAsync("Lock-screen replacement refresh reads the registry-backed state", async () =>
        {
            var client = new FakeClient(operation => Task.FromResult(
                Response(operation == LockScreenProviderOperation.Query, success: true)));
            using var service = CreateService(client);

            await service.RefreshAsync();

            Check(service.IsInitialized && service.CurrentValue, "Query state was not applied.");
            Check(client.Operations.SequenceEqual([LockScreenProviderOperation.Query]), "Unexpected helper operation.");
        });

        await runAsync("Lock-screen replacement setter applies helper state", async () =>
        {
            var client = new FakeClient(operation => Task.FromResult(
                Response(operation == LockScreenProviderOperation.Enable, success: true)));
            using var service = CreateService(client);

            await service.RefreshAsync();
            await service.SetEnabledAsync(true);

            Check(service.CurrentValue, "Enable result was not applied.");
            Check(
                client.Operations.SequenceEqual([
                    LockScreenProviderOperation.Query,
                    LockScreenProviderOperation.Enable
                ]),
                "Enable operation was not sent.");
        });

        await runAsync("Failed replacement rolls back the optimistic value and reports an error", async () =>
        {
            var enableGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new FakeClient(async operation =>
            {
                if (operation == LockScreenProviderOperation.Enable)
                {
                    await enableGate.Task;
                    throw new InvalidOperationException("simulated helper failure");
                }

                return Response(false, success: true);
            });
            using var service = CreateService(client);
            LockScreenReplacementFailedEventArgs? failure = null;
            service.OperationFailed += (_, args) => failure = args;

            await service.RefreshAsync();
            var operation = service.SetEnabledAsync(true);
            Check(service.CurrentValue, "Setter was not optimistic.");

            enableGate.SetResult();
            await operation;

            Check(!service.CurrentValue, "Failed set did not roll back.");
            Check(
                failure is { Operation: LockScreenProviderOperation.Enable, RolledBack: true },
                "Failure was not reported with rollback.");
        });

        await runAsync("Rapid replacement requests do not publish stale success state", async () =>
        {
            var enableGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new FakeClient(async operation =>
            {
                if (operation == LockScreenProviderOperation.Enable)
                {
                    await enableGate.Task;
                }

                return Response(operation == LockScreenProviderOperation.Enable, success: true);
            });
            using var service = CreateService(client);

            await service.RefreshAsync();
            var enable = service.SetEnabledAsync(true);
            var disable = service.SetEnabledAsync(false);
            Check(service.CurrentValue == false, "Latest optimistic disable was not retained.");

            enableGate.SetResult();
            await enable;
            await disable;

            Check(!service.CurrentValue, "Stale enable result overwrote the latest disable.");
            Check(
                client.Operations.SequenceEqual([
                    LockScreenProviderOperation.Query,
                    LockScreenProviderOperation.Enable,
                    LockScreenProviderOperation.Disable
                ]),
                "Rapid requests were not serialized in order.");
        });

        await runAsync("Refresh failure is surfaced without changing the cached value", async () =>
        {
            var client = new FakeClient(_ => throw new TimeoutException("simulated timeout"));
            using var service = CreateService(client);
            LockScreenReplacementFailedEventArgs? failure = null;
            service.OperationFailed += (_, args) => failure = args;

            await service.RefreshAsync();

            Check(!service.CurrentValue, "Refresh failure changed the cached value.");
            Check(
                failure is { Operation: LockScreenProviderOperation.Query },
                "Refresh failure was not reported.");
        });
    }

    private sealed class FakeRegistryView(LockScreenRegistryViewKind kind) : ILockScreenRegistryView
    {
        public LockScreenRegistryViewKind Kind { get; } = kind;
        internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public string? ReadValue(string keyPath, string valueName) =>
            Values.TryGetValue(valueName, out var value) ? value : null;

        public void WriteValue(string keyPath, string valueName, string value) =>
            Values[valueName] = value;
    }

    private static LockScreenReplacementService CreateService(ILockScreenHelperClient client) =>
        new(client, NullLogger<LockScreenReplacementService>.Instance);

    private static LockScreenProviderResponse Response(bool enabled, bool success) =>
        new("request", success, enabled, enabled ? "test-aumid" : null, success ? null : "test failure");

    private sealed class FakeClient(
        Func<LockScreenProviderOperation, Task<LockScreenProviderResponse>> handler) : ILockScreenHelperClient
    {
        internal ConcurrentQueue<LockScreenProviderOperation> Operations { get; } = new();

        public Task<LockScreenProviderResponse> ExecuteAsync(
            LockScreenProviderOperation operation,
            CancellationToken cancellationToken = default)
        {
            Operations.Enqueue(operation);
            return handler(operation);
        }
    }
}
