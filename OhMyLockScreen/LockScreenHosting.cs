using System;
using System.Runtime.InteropServices;

namespace OhMyLockScreen;

internal static unsafe class LockScreenHosting
{
    /// <summary>
    /// True when the current CoreWindow has been reparented into the lock host's
    /// ApplicationHostBridgeWindow, i.e. this process really is the displayed lock app.
    /// </summary>
    internal static bool IsWindowHostedByLockScreen()
    {
        var coreWindow = Windows.UI.Xaml.Window.Current?.CoreWindow;
        if (coreWindow is null)
        {
            return false;
        }

        try
        {
            // ICoreWindowInterop {45D64A29-A63E-4CB6-B498-5781D298CB4F}, get_WindowHandle is slot 3.
            using var interop = ((WinRT.IWinRTObject)coreWindow).NativeObject.As<WinRT.Interop.IUnknownVftbl>(
                new Guid("45D64A29-A63E-4CB6-B498-5781D298CB4F"));
            var pointer = interop.ThisPtr;
            var getWindowHandle = (delegate* unmanaged[Stdcall]<nint, ulong*, int>)(*(nint**)pointer)[3];
            ulong windowHandle = 0;
            if (getWindowHandle(pointer, &windowHandle) < 0 || windowHandle == 0)
            {
                return false;
            }

            var parent = GetParent((nint)(long)windowHandle);
            if (parent == 0)
            {
                return false;
            }

            char* className = stackalloc char[128];
            var length = GetClassNameW(parent, className, 128);
            return length > 0 &&
                string.Equals(new string(className, 0, length), "ApplicationHostBridgeWindow", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetParent(nint windowHandle);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetClassNameW(nint windowHandle, char* className, int maximumCount);
}
