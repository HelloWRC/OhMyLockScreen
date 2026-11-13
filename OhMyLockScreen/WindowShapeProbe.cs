using System.Runtime.InteropServices;
using Windows.UI.Core;

namespace OhMyLockScreen;

internal static unsafe class WindowShapeProbe
{
    internal const string ApplicationFrameClass = "ApplicationFrameWindow";
    internal const string HostBridgeClass = "ApplicationHostBridgeWindow";
    private const int CloakedAttribute = 14;

    internal static bool IsParentedByLockScreenHost(CoreWindow? coreWindow)
    {
        var handle = GetWindowHandle(coreWindow);
        var parent = handle == 0 ? 0 : GetParent(handle);
        return parent != 0 && ReadClassName(parent) == HostBridgeClass;
    }

    internal static (string RootClass, int Cloaked)? Describe(CoreWindow? coreWindow)
    {
        var handle = GetWindowHandle(coreWindow);
        if (handle == 0)
        {
            return null;
        }

        var root = handle;
        var parent = GetParent(root);
        while (parent != 0)
        {
            root = parent;
            parent = GetParent(root);
        }

        var rootClass = ReadClassName(root);
        var value = 0;
        var cloaked = DwmGetWindowAttribute(handle, CloakedAttribute, &value, sizeof(int)) >= 0 ? value : 0;
        return (rootClass, cloaked);
    }

    private static nint GetWindowHandle(CoreWindow? coreWindow)
    {
        if (coreWindow is null)
        {
            return 0;
        }

        try
        {
            // ICoreWindowInterop::get_WindowHandle is slot 3 in the COM vtable.
            using var interop = ((WinRT.IWinRTObject)coreWindow).NativeObject.As<WinRT.Interop.IUnknownVftbl>(
                new Guid("45D64A29-A63E-4CB6-B498-5781D298CB4F"));
            var pointer = interop.ThisPtr;
            var getWindowHandle = (delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)pointer)[3];
            nint handle = 0;
            return getWindowHandle(pointer, &handle) >= 0 ? handle : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string ReadClassName(nint windowHandle)
    {
        const int capacity = 256;
        char* className = stackalloc char[capacity];
        var length = GetClassNameW(windowHandle, className, capacity);
        return new string(className, 0, length);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetParent(nint windowHandle);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetClassNameW(nint windowHandle, char* className, int maximumCount);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(nint windowHandle, int attribute, int* value, int size);
}
