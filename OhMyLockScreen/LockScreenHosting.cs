using System;
using System.Diagnostics;
using Windows.ApplicationModel.Core;
using Windows.UI.Xaml;

namespace OhMyLockScreen;

internal static class LockScreenHosting
{
    internal static bool IsWindowHostedByLockScreen()
    {
        try
        {
            if (CoreApplication.GetCurrentView().IsHosted)
            {
                return true;
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not query the current application view host: {exception}");
        }

        return WindowShapeProbe.IsParentedByLockScreenHost(Window.Current?.CoreWindow);
    }
}
