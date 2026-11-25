using System.Reflection;
using Microsoft.Extensions.Hosting;

namespace OhMyLockScreen.Shared;

/// <summary>应用主机和服务访问入口。</summary>
public interface IAppHost
{
    public static IHost? Host;

    public static string AppVersion { get; } = typeof(IAppHost).Assembly.GetName().Version!.ToString();

    public static string AppCodeName => "Amiya";

    public static string AppGitHash { get; } = GetBuildMetadata("GitCommitHash");

    public static string AppGitBranch { get; } = GetBuildMetadata("GitBranch");

    public static string AppVersionLong => $"{AppVersion}-{AppCodeName}-{AppGitHash[..7]}({AppGitBranch})";

    private static string GetBuildMetadata(string key) =>
        typeof(IAppHost).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)?.Value ?? "Unknown";

    public static T GetRequiredService<T>() where T : class
    {
        return TryGetService<T>()
               ?? throw new InvalidOperationException($"应用主机尚未初始化或未注册服务 {typeof(T).FullName}。");
    }

    public static T? TryGetService<T>() where T : class
    {
        return Host?.Services.GetService(typeof(T)) as T;
    }
}
