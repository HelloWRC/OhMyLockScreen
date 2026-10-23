using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.UI.Xaml;

namespace OhMyLockScreen;

internal static class WindowTransparency
{
    internal static unsafe void Enable(Window window)
    {
        try
        {
            // IXamlSourceTransparency removes XAML's default opaque window
            // background. Supported since Windows 10 1809, including 19044.
            // https://learn.microsoft.com/windows/apps/api-reference/interface-members/ixamlsourcetransparency-isbackgroundtransparent
            using var transparency = ((WinRT.IWinRTObject)window).NativeObject
                .As<WinRT.Interop.IUnknownVftbl>(new Guid("06636C29-5A17-458D-8EA2-2422D997A922"));
            var pointer = transparency.ThisPtr;
            // IInspectable slots 0-5, property getter 6, setter 7 (WinRT Boolean).
            var setTransparent = (delegate* unmanaged[Stdcall]<nint, byte, int>)(*(nint**)pointer)[7];
            Marshal.ThrowExceptionForHR(setTransparent(pointer, 1));
        }
        catch (Exception exception)
        {
            // A host without this optional interface should still show the page.
            Debug.WriteLine($"Could not enable transparent window background: {exception}");
        }
    }
}