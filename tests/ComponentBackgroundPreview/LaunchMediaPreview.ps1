param(
    [ValidateSet('media', 'system-status', 'spectrum', 'scene-blobs')]
    [string]$Scenario = 'media'
)

$ErrorActionPreference = 'Stop'

# Activate only the isolated test package. This package has no lock-screen extension.
$previewLayout = Join-Path $PSScriptRoot 'bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'
$previewManifest = Join-Path $previewLayout 'AppxManifest.xml'
if (-not (Test-Path -LiteralPath $previewManifest)) {
    throw 'Build ComponentBackgroundPreview for Debug/x64 before launching it.'
}

$previewPackage = Get-AppxPackage -Name 'dev.hellowrc.OhMyLockScreen.BackgroundPreview'
[xml]$layoutManifest = Get-Content -LiteralPath $previewManifest -Raw
$layoutVersion = [version]$layoutManifest.Package.Identity.Version
# Extension/capability changes use a higher package version; ordinary rebuilds reuse registration.
if (-not $previewPackage -or $previewPackage.InstallLocation -ne $previewLayout -or [version]$previewPackage.Version -lt $layoutVersion) {
    Add-AppxPackage -Register $previewManifest -ForceApplicationShutdown
    $previewPackage = Get-AppxPackage -Name 'dev.hellowrc.OhMyLockScreen.BackgroundPreview'
}

if (-not ('MediaPreviewActivation' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class MediaPreviewActivation
{
    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            uint options,
            out uint processId);
    }

    public static uint Launch(string appUserModelId, string scenario)
    {
        var type = Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"));
        var manager = (IApplicationActivationManager)Activator.CreateInstance(type);
        try
        {
            uint processId;
            Marshal.ThrowExceptionForHR(manager.ActivateApplication(appUserModelId, scenario, 0, out processId));
            return processId;
        }
        finally
        {
            Marshal.ReleaseComObject(manager);
        }
    }
}
'@
}

[MediaPreviewActivation]::Launch("$($previewPackage.PackageFamilyName)!App", $Scenario)
