using System.Text.Json.Serialization;

namespace OhMyLockScreen.Shared.Models.LockScreenProvider;

public static class LockScreenProviderProtocol
{
    public const string RequestDirectoryName = "LockScreenProvider";
    public const string RequestFileName = "request.json";
    public const string ResponseFileName = "response.json";
    public const string DiagnosticFileName = "last-operation.txt";
    public const string ClientDiagnosticFileName = "last-client-operation.txt";
    public const string UnlockLatencyFileName = "unlock-latency.log";
    public const string UnlockPipeDirectoryName = "UnlockPipe";
    public const string UnlockPipeParameterGroupId = "LockScreenUnlockPipe";
    public const string UnlockPipeNamePrefix = "OhMyLockScreen.Unlock.";
    public const string UnlockPipeRequestExtension = ".request";
    public static readonly TimeSpan UnlockPipeTimeout = TimeSpan.FromSeconds(2);
    public const string DefaultLockAppAumId = "Microsoft.LockApp_cw5n1h2txyewy!WindowsDefaultLockScreen";
    public const string HelperParameterGroupId = "LockScreenProvider";
}

public enum LockScreenProviderOperation
{
    Query,
    Enable,
    Disable,
    Unlock
}

public sealed record LockScreenProviderRequest(string RequestId, LockScreenProviderOperation Operation)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? UnlockStartTimestamp { get; init; }
}

public sealed record LockScreenProviderResponse(
    string RequestId,
    bool Success,
    bool IsEnabled,
    string? Value,
    string? Error);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LockScreenProviderRequest))]
[JsonSerializable(typeof(LockScreenProviderResponse))]
public partial class LockScreenProviderJsonContext : JsonSerializerContext;
