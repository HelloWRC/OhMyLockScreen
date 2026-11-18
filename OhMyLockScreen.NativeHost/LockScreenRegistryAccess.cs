namespace OhMyLockScreen.NativeHost;

internal sealed record LockScreenRegistryState(bool IsEnabled, string? Value);

internal interface ILockScreenRegistryView
{
    string? ReadValue(string keyPath, string valueName);
}

internal sealed class LockScreenRegistryAccess(
    IReadOnlyList<ILockScreenRegistryView> views,
    string keyPath,
    string valueName)
{
    internal LockScreenRegistryState Read(string expectedValue)
    {
        string? firstValue = null;
        foreach (var view in views)
        {
            var value = view.ReadValue(keyPath, valueName);
            firstValue ??= value;
            if (string.Equals(value, expectedValue, StringComparison.OrdinalIgnoreCase))
            {
                return new LockScreenRegistryState(true, value);
            }
        }

        return new LockScreenRegistryState(false, firstValue);
    }
}
