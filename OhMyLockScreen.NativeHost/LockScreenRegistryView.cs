using Microsoft.Win32;

namespace OhMyLockScreen.NativeHost;

internal sealed class LockScreenRegistryView(RegistryView view) : ILockScreenRegistryView
{
    public string? ReadValue(string keyPath, string valueName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
        using var key = baseKey.OpenSubKey(keyPath, writable: false);
        return key?.GetValue(valueName) as string;
    }
}
