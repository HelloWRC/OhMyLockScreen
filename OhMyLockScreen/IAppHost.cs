using System;
using Microsoft.Extensions.Hosting;

namespace OhMyLockScreen;

/// <summary>应用主机和服务访问入口。</summary>
public interface IAppHost
{
    public static IHost? Host;

    public static T GetService<T>() where T : class
    {
        return TryGetService<T>()
               ?? throw new InvalidOperationException($"应用主机尚未初始化或未注册服务 {typeof(T).FullName}。");
    }

    public static T? TryGetService<T>() where T : class
    {
        return Host?.Services.GetService(typeof(T)) as T;
    }
}