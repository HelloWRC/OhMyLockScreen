namespace OhMyLockScreen.LockScreenHelper;

internal enum LockScreenRegistryViewKind
{
    Registry32,
    Registry64
}

internal sealed record LockScreenRegistryState(bool IsEnabled, string? Value);

internal sealed record LockScreenRegistryValue(LockScreenRegistryViewKind Kind, string? Value);

internal interface ILockScreenRegistryView
{
    LockScreenRegistryViewKind Kind { get; }
    string? ReadValue(string keyPath, string valueName);
    void WriteValue(string keyPath, string valueName, string value);
}

internal sealed class LockScreenRegistryAccess(
    IReadOnlyList<ILockScreenRegistryView> views,
    string keyPath,
    string valueName)
{
    internal IReadOnlyList<LockScreenRegistryValue> ReadValues() =>
        views.Select(view => new LockScreenRegistryValue(
            view.Kind,
            view.ReadValue(keyPath, valueName)))
        .ToArray();

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

    internal LockScreenRegistryState Enable(string expectedValue)
    {
        foreach (var view in views)
        {
            view.WriteValue(keyPath, valueName, expectedValue);
        }

        var state = Read(expectedValue);
        if (!state.IsEnabled)
        {
            throw new InvalidOperationException("未能将当前应用写入锁屏提供程序注册表值。");
        }

        return state;
    }

    internal LockScreenRegistryState Disable(string expectedValue, string defaultValue)
    {
        foreach (var view in views)
        {
            view.WriteValue(keyPath, valueName, defaultValue);
        }

        var state = Read(expectedValue);
        if (state.IsEnabled)
        {
            throw new InvalidOperationException("未能恢复默认锁屏提供程序注册表值。");
        }

        return state;
    }
}
