using System.Runtime.InteropServices;

namespace OhMyLockScreen.LockScreenHelper;

internal sealed class LockAppHostGlobalUnlockSignal
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
        var handle = OpenEvent(EventModifyState, false, _eventName);
        if (handle == nint.Zero)
        {
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        try
        {
            if (!SetEvent(handle))
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

    [DllImport("kernel32.dll", EntryPoint = "OpenEventW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint OpenEvent(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEvent(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}