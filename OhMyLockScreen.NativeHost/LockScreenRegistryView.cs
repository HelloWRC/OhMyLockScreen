using Microsoft.Win32;

namespace OhMyLockScreen.NativeHost;

internal sealed class LockScreenRegistryView(LockScreenRegistryViewKind kind) : ILockScreenRegistryView
{
    public LockScreenRegistryViewKind Kind { get; } = kind;

    private RegistryView RegistryView => Kind == LockScreenRegistryViewKind.Registry64
        ? RegistryView.Registry64
        : RegistryView.Registry32;

    public string? ReadValue(string keyPath, string valueName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView);
        using var key = baseKey.OpenSubKey(keyPath);
        return key?.GetValue(valueName) as string;
    }

    public void WriteValue(string keyPath, string valueName, string value)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView);
        using var key = baseKey.CreateSubKey(keyPath, true)
            ?? throw new InvalidOperationException("无法打开当前用户的锁屏注册表项。");
        key.SetValue(valueName, value, RegistryValueKind.String);
    }
}
