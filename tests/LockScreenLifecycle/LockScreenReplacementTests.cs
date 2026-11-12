using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using OhMyLockScreen.LockScreenHelper;
using OhMyLockScreen.Models;
using OhMyLockScreen.Services;
using OhMyLockScreen.Services.LockScreenProvider;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Models.LockScreenProvider;
using Windows.Storage;

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

        run("Compatibility unlock mode defaults off and persists", () =>
        {
            var path = Path.Combine(
                ApplicationData.Current.LocalFolder.Path,
                "CompatibilityUnlockMode.json");
            var settings = new Settings();
            Check(!settings.CompatibilityUnlockMode, "Compatibility unlock mode was enabled by default.");

            settings.CompatibilityUnlockMode = true;
            ConfigureFileHelper.SaveConfig(path, settings);

            var loaded = ConfigureFileHelper.LoadConfig<Settings>(path);
            Check(loaded.CompatibilityUnlockMode, "Compatibility unlock mode was not persisted.");
        });

        run("Lock-screen unlock signal sets a test event without touching the real host event", () =>
        {
            Check(
                LockAppHostGlobalUnlockSignal.DefaultEventName == @"Local\LockAppHostGlobalUnlockEvent",
                "The production unlock event name changed.");
            var eventName = $"Local\\OhMyLockScreen.Tests.{Guid.NewGuid():N}";
            using var expectedEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
            var signal = new LockAppHostGlobalUnlockSignal(eventName);

            Check(signal.TrySignal(out var errorCode), $"Test unlock event was not signaled: {errorCode}.");
            Check(errorCode == 0 && expectedEvent.WaitOne(0), "Test unlock event was not observed.");
        });

        run("Lock-screen helper maps unlock signals to protocol responses", () =>
        {
            var eventName = $"Local\\OhMyLockScreen.Tests.{Guid.NewGuid():N}";
            using var expectedEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
            var request = new LockScreenProviderRequest("unlock", LockScreenProviderOperation.Unlock);

            var success = LockScreenUnlockCommand.Execute(
                request,
                new LockAppHostGlobalUnlockSignal(eventName));
            Check(
                success is { Success: true, IsEnabled: false, Value: null, Error: null } &&
                expectedEvent.WaitOne(0),
                "Successful unlock response or event was incorrect.");

            var missingName = $"Local\\OhMyLockScreen.Tests.{Guid.NewGuid():N}";
            var failure = LockScreenUnlockCommand.Execute(
                request,
                new LockAppHostGlobalUnlockSignal(missingName));
            Check(
                failure is { Success: false, IsEnabled: false, Value: null } &&
                !string.IsNullOrWhiteSpace(failure.Error),
                "Missing unlock event did not produce a diagnostic failure.");
        });

        run("Lock-screen unlock protocol round trips", () =>
        {
            var request = new LockScreenProviderRequest("unlock", LockScreenProviderOperation.Unlock);
            var json = JsonSerializer.Serialize(
                request,
                LockScreenProviderJsonContext.Default.LockScreenProviderRequest);
            var parsed = JsonSerializer.Deserialize(
                json,
                LockScreenProviderJsonContext.Default.LockScreenProviderRequest);

            Check(parsed == request, "Unlock protocol request did not round trip.");
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

        await runAsync("System unlock uses the helper and reports success", async () =>
        {
            var client = new FakeClient(operation => Task.FromResult(
                Response(false, success: operation == LockScreenProviderOperation.Unlock)));
            using var settings = CreateSettingsService();
            var service = new LockScreenUnlockService(
                settings,
                client,
                NullLogger<LockScreenUnlockService>.Instance);

            Check(await service.TryUnlockAsync(), "Successful system unlock was not reported.");
            Check(
                client.Operations.SequenceEqual([LockScreenProviderOperation.Unlock]),
                "System unlock did not use the unlock helper operation.");
        });

        await runAsync("Compatibility unlock bypasses the helper and requests the legacy exit", async () =>
        {
            var client = new FakeClient(_ => throw new InvalidOperationException("Helper must not run."));
            using var settings = CreateSettingsService();
            settings.Settings.CompatibilityUnlockMode = true;
            var service = new LockScreenUnlockService(
                settings,
                client,
                NullLogger<LockScreenUnlockService>.Instance);

            Check(!await service.TryUnlockAsync(), "Compatibility mode did not request the legacy exit.");
            Check(client.Operations.IsEmpty, "Compatibility mode invoked the helper.");
        });

        await runAsync("Failed system unlock requests the legacy exit", async () =>
        {
            var client = new FakeClient(_ => Task.FromResult(Response(false, success: false)));
            using var settings = CreateSettingsService();
            var service = new LockScreenUnlockService(
                settings,
                client,
                NullLogger<LockScreenUnlockService>.Instance);

            Check(!await service.TryUnlockAsync(), "Failed system unlock did not request the legacy exit.");
            Check(
                client.Operations.SequenceEqual([LockScreenProviderOperation.Unlock]),
                "Failed system unlock did not attempt the helper operation.");
        });

        await runAsync("System unlock exceptions request the legacy exit", async () =>
        {
            var client = new FakeClient(_ => throw new TimeoutException("simulated timeout"));
            using var settings = CreateSettingsService();
            var service = new LockScreenUnlockService(
                settings,
                client,
                NullLogger<LockScreenUnlockService>.Instance);

            Check(!await service.TryUnlockAsync(), "System unlock exception did not request the legacy exit.");
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

    private static SettingsService CreateSettingsService()
    {
        var path = Path.Combine(
            ApplicationData.Current.LocalFolder.Path,
            $"Settings-{Guid.NewGuid():N}.json");
        var service = new SettingsService(NullLogger<SettingsService>.Instance, path);
        service.LoadSettingsAsync().GetAwaiter().GetResult();
        return service;
    }

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
