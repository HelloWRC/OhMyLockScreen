namespace OhMyLockScreen.Shared.Models;

/// <summary>配置加载错误；Level 为 0 表示已从备份恢复，1 表示返回默认值。</summary>
public sealed class ConfigError
{
    public string Path { get; }
    public int Level { get; }
    public bool Critical { get; }
    public Exception Exception { get; }

    internal ConfigError(string path, int level, bool critical, Exception exception)
    {
        Path = path;
        Level = level;
        Critical = critical;
        Exception = exception;
    }
}