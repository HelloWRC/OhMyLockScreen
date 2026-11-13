using System.Runtime.InteropServices;

namespace OhMyLockScreen.NativeHost;

internal sealed partial class LockAppHostGlobalUnlockSignal
{
    internal const string DefaultEventName = @"Local\LockAppHostGlobalUnlockEvent";
    private const uint EventModifyState = 0x0002;
    private readonly string _eventName;

    internal LockAppHostGlobalUnlockSignal() : this(DefaultEventName)
    {
    }

    internal LockAppHostGlobalUnlockSignal(string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        _eventName = eventName;
    }

    internal bool TrySignal(out int errorCode)
    {
        // LockAppHost publishes this session-local event and treats a signal as RequestUnlock.
        var handle = OpenEvent(EventModifyState, 0, _eventName);
        if (handle == nint.Zero)
        {
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        try
        {
            if (SetEvent(handle) == 0)
            {
                errorCode = Marshal.GetLastWin32Error();
                return false;
            }

            errorCode = 0;
            return true;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "OpenEventW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint OpenEvent(
        uint desiredAccess,
        int inheritHandle,
        string name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int SetEvent(nint handle);

    [LibraryImport("kernel32.dll")]
    private static partial int CloseHandle(nint handle);
}
