using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using OhMyLockScreen.Services.Audio;

namespace OhMyLockScreen.NativeHost;

internal static partial class ApplicationActivation
{
    private static readonly Guid ActivationManagerClass = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");
    private static readonly Guid ActivationManagerInterface = new("2E941141-7F97-4756-BA1D-9DECDE894A3D");
    private const uint LocalServer = 4;

    internal static void Launch(string appUserModelId)
    {
        Marshal.ThrowExceptionForHR(AudioInterop.CoInitializeEx(0, 0));
        try
        {
            // The surrogate keeps the activation arguments alive after NativeHost exits.
            Marshal.ThrowExceptionForHR(CoCreateInstance(ActivationManagerClass, 0, LocalServer, ActivationManagerInterface, out var pointer));
            try
            {
                CoAllowSetForegroundWindow(pointer, 0);
                var manager = AudioInterop.WrapUnique<IApplicationActivationManager>(pointer);
                try
                {
                    Marshal.ThrowExceptionForHR(manager.ActivateApplication(appUserModelId, null, 0, out _));
                }
                finally
                {
                    AudioInterop.Release(manager);
                }
            }
            finally
            {
                Marshal.Release(pointer);
            }
        }
        finally
        {
            AudioInterop.CoUninitialize();
        }
    }

    internal static void LaunchFromShell(string appUserModelId)
    {
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var start = new ProcessStartInfo(explorer) { UseShellExecute = false };
        start.ArgumentList.Add($@"shell:AppsFolder\{appUserModelId}");
        using var process = Process.Start(start);
        if (process is null)
        {
            throw new InvalidOperationException("无法通过 Windows 资源管理器打开场景编辑器。");
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [LibraryImport("ole32.dll")]
    private static partial int CoAllowSetForegroundWindow(nint instance, nint reserved);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
internal partial interface IApplicationActivationManager
{
    [PreserveSig]
    int ActivateApplication(string appUserModelId, string? arguments, uint options, out uint processId);
}
