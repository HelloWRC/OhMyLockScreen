using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Models;

namespace OhMyLockScreen.Helpers;

/// <summary>JSON 配置读写，沿用 ClassIsland 的加载备份与错误记录约定。</summary>
public class ConfigureFileHelper
{
    private static readonly JsonSerializerOptions SerializerOptions = new();
    private static readonly JsonSerializerOptions IndentedSerializerOptions = new() { WriteIndented = true };

    // Do not cache a logger from a host that may have been disposed.
    private static ILogger? Logger => IAppHost.TryGetService<ILogger<ConfigureFileHelper>>();

    public static bool IsBackupEnabled { get; set; } = true;
    public static ObservableCollection<ConfigError> Errors { get; } = new();

    /// <summary>读取失败时抛出异常；启用备份时，先尝试恢复有效的备份。</summary>
    public static T LoadConfigUnWrapped<T>(string path, bool? backupEnabled = null, bool isLoadingBackup = false)
    {
        Logger?.LogInformation("读取 JSON 文件：{Path}", path);
        if (!File.Exists(path))
        {
            if (isLoadingBackup)
            {
                throw new FileNotFoundException("配置备份不存在。", path);
            }

            var config = Activator.CreateInstance<T>();
            SaveConfig(path, config);
            return config;
        }

        var backupPath = path + ".bak";
        var useBackup = !isLoadingBackup && (backupEnabled ?? IsBackupEnabled);
        T result;
        try
        {
            using var stream = File.OpenRead(path);
            result = LoadConfigUnWrapped<T>(stream);
        }
        catch (Exception exception) when (useBackup && File.Exists(backupPath))
        {
            Logger?.LogWarning(exception, "读取 JSON 文件 {Path} 失败，尝试加载备份", path);
            // Validate the backup without the wrapped API: never restore corrupt bytes
            // after a failed backup load has silently returned a default object.
            var backup = LoadConfigUnWrapped<T>(backupPath, false, true);
            File.Copy(backupPath, path, true);
            RecordError(new ConfigError(path, 0, false, exception));
            return backup;
        }

        // A backup-write failure must not trigger restoration of stale settings.
        if (useBackup)
        {
            File.Copy(path, backupPath, true);
        }
        return result;
    }

    /// <summary>从流的当前位置读取；调用方负责流的生命周期。</summary>
    public static T LoadConfigUnWrapped<T>(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true);
        return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), SerializerOptions) ?? Activator.CreateInstance<T>();
    }

    /// <summary>读取及备份恢复均失败时，记录错误并返回默认配置。</summary>
    public static T LoadConfig<T>(string path, bool? backupEnabled = null, bool isLoadingBackup = false)
    {
        try
        {
            return LoadConfigUnWrapped<T>(path, backupEnabled, isLoadingBackup);
        }
        catch (Exception exception)
        {
            Logger?.LogError(exception, "无法读取 JSON 文件 {Path}，已返回默认值", path);
            RecordError(new ConfigError(path, 1, true, exception));
            return Activator.CreateInstance<T>();
        }
    }

    public static T LoadConfig<T>(Stream stream)
    {
        try
        {
            return LoadConfigUnWrapped<T>(stream);
        }
        catch (Exception exception)
        {
            Logger?.LogError(exception, "无法从流中读取 JSON 文件，已返回默认值");
            RecordError(new ConfigError("<stream>", 1, true, exception));
            return Activator.CreateInstance<T>();
        }
    }

    /// <summary>写入配置；保留上次成功加载时的备份，写入失败时抛出异常。</summary>
    public static void SaveConfig<T>(string path, T o) => SaveConfig(path, o, false);

    public static void SaveConfig<T>(string path, T o, bool writeIndented)
    {
        Logger?.LogInformation("写入 JSON 文件：{Path}", path);
        var json = JsonSerializer.Serialize(o, GetOptions(writeIndented));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
        writer.Write(json);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    /// <summary>从当前位置写入并刷新；不关闭或截断调用方的流。</summary>
    public static void SaveConfig<T>(Stream stream, T o, bool writeIndented = false)
    {
        var json = JsonSerializer.Serialize(o, GetOptions(writeIndented));
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
        writer.Write(json);
        writer.Flush();
        stream.Flush();
    }

    public static T CopyObject<T>(T o)
    {
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(o, SerializerOptions), SerializerOptions)
               ?? Activator.CreateInstance<T>();
    }

    private static JsonSerializerOptions GetOptions(bool writeIndented)
        => writeIndented ? IndentedSerializerOptions : SerializerOptions;

    private static void RecordError(ConfigError error)
    {
        lock (Errors)
        {
            Errors.Add(error);
        }
    }
}