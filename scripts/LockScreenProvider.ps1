#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Enable', 'Restore')]
    [string] $Action,

    [string] $BackupPath = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'OhMyLockScreen\LockAppAumId.clixml')
)

function Get-LockProviderAumid {
    $packages = @(Get-AppxPackage -Name 'dev.hellowrc.OhMyLockScreen' -ErrorAction Stop)
    if ($packages.Count -ne 1) {
        throw 'Expected one installed dev.hellowrc.OhMyLockScreen package for the current user. Deploy the package first.'
    }

    $manifest = Get-AppxPackageManifest -Package $packages[0].PackageFullName -ErrorAction Stop
    $namespaces = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
    $namespaces.AddNamespace('p', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $namespaces.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')
    $extension = $manifest.SelectSingleNode(
        '/p:Package/p:Applications/p:Application[@Id="App"]/p:Extensions/uap:Extension[@Category="windows.lockScreen"]',
        $namespaces)
    if ($null -eq $extension) {
        throw 'The installed App does not declare windows.lockScreen. Rebuild and deploy the updated package first.'
    }

    return "$($packages[0].PackageFamilyName)!App"
}

function Get-LockProviderUserSid {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    try { return $identity.User.Value }
    finally { $identity.Dispose() }
}

function Get-LockProviderSelection {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Lock Screen')
    try {
        if ($null -eq $key -or $key.GetValueNames() -notcontains 'LockAppAumId') {
            return [pscustomobject]@{ Exists = $false; Kind = $null; Value = $null }
        }
        return [pscustomobject]@{
            Exists = $true
            Kind = $key.GetValueKind('LockAppAumId').ToString()
            Value = $key.GetValue('LockAppAumId', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        }
    }
    finally { if ($null -ne $key) { $key.Dispose() } }
}

function Set-LockProviderSelection {
    param([Parameter(Mandatory)] $Selection)

    $subKey = 'Software\Microsoft\Windows\CurrentVersion\Lock Screen'
    $key = $null
    try {
        if ($Selection.Exists) {
            $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($subKey)
            $kind = [Microsoft.Win32.RegistryValueKind] [Enum]::Parse([Microsoft.Win32.RegistryValueKind], $Selection.Kind)
            $key.SetValue('LockAppAumId', $Selection.Value, $kind)
        }
        else {
            $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subKey, $true)
            if ($null -ne $key) { $key.DeleteValue('LockAppAumId', $false) }
        }
    }
    finally { if ($null -ne $key) { $key.Dispose() } }
}

function Read-LockProviderBackup {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Backup not found: $Path"
    }
    $backup = Import-Clixml -LiteralPath $Path -ErrorAction Stop
    if ($backup.SchemaVersion -ne 1 -or $backup.UserSid -ne (Get-LockProviderUserSid) -or
        $backup.Exists -isnot [bool]) {
        throw 'Invalid backup or backup belongs to another user. No settings were changed.'
    }

    if ($backup.Exists) {
        $valid = switch ($backup.Kind) {
            'String'       { $backup.Value -is [string] }
            'ExpandString' { $backup.Value -is [string] }
            'DWord'        { $backup.Value -is [int] }
            'QWord'        { $backup.Value -is [long] }
            'Binary'      { $backup.Value -is [byte[]] }
            'None'        { $backup.Value -is [byte[]] }
            'MultiString' {
                ($backup.Value -is [System.Collections.IList]) -and
                    (@($backup.Value | Where-Object { $_ -isnot [string] }).Count -eq 0)
            }
            default { $false }
        }
        if (-not $valid) { throw 'Invalid registry type or value in backup. No settings were changed.' }
        # CLIXML can deserialize arrays as ArrayList; RegistryKey requires string[].
        if ($backup.Kind -eq 'MultiString') { $backup.Value = [string[]] $backup.Value }
    }
    elseif ($null -ne $backup.Kind -or $null -ne $backup.Value) {
        throw 'Invalid backup of an absent value. No settings were changed.'
    }
    return $backup
}

function Save-LockProviderBackup {
    param([Parameter(Mandatory)] $Backup, [Parameter(Mandatory)][string] $Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $directory = [System.IO.Path]::GetDirectoryName($fullPath)
    [void] [System.IO.Directory]::CreateDirectory($directory)
    $temporaryPath = Join-Path $directory ([System.IO.Path]::GetRandomFileName())
    try {
        $Backup | Export-Clixml -LiteralPath $temporaryPath -Encoding UTF8 -ErrorAction Stop
        # The two-argument Move fails if the original backup already exists.
        [System.IO.File]::Move($temporaryPath, $fullPath)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -ErrorAction Stop
        }
    }
}

function Invoke-LockScreenProvider {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)][ValidateSet('Enable', 'Restore')][string] $Action,
        [Parameter(Mandatory)][string] $BackupPath
    )

    $ErrorActionPreference = 'Stop'
    if ($Action -eq 'Enable') {
        $aumid = Get-LockProviderAumid
        $hasBackup = Test-Path -LiteralPath $BackupPath
        if ($hasBackup) {
            $backup = Read-LockProviderBackup -Path $BackupPath
        }
        else {
            $selection = Get-LockProviderSelection
            # Without the original backup, backing up our own AUMID would make Restore ineffective.
            if ($selection.Exists -and $selection.Value -eq $aumid) {
                throw 'This provider is already enabled but its original backup is missing. No settings were changed.'
            }
            $backup = [pscustomobject]@{
                SchemaVersion = 1
                UserSid = Get-LockProviderUserSid
                Exists = $selection.Exists
                Kind = $selection.Kind
                Value = $selection.Value
            }
        }

        if ($PSCmdlet.ShouldProcess('HKCU\Software\Microsoft\Windows\CurrentVersion\Lock Screen\LockAppAumId', "Back up and select $aumid")) {
            if (-not $hasBackup) { Save-LockProviderBackup -Backup $backup -Path $BackupPath }
            Set-LockProviderSelection -Selection ([pscustomobject]@{ Exists = $true; Kind = 'String'; Value = $aumid })
            Write-Output "Enabled $aumid. Original setting saved at $BackupPath. Lock manually with Win+L to test."
        }
    }
    else {
        $backup = Read-LockProviderBackup -Path $BackupPath
        if ($PSCmdlet.ShouldProcess('HKCU\Software\Microsoft\Windows\CurrentVersion\Lock Screen\LockAppAumId', 'Restore original setting and remove completed backup')) {
            Set-LockProviderSelection -Selection $backup
            Remove-Item -LiteralPath $BackupPath -ErrorAction Stop
            Write-Output 'Restored the original lock screen setting. The completed backup has been removed.'
        }
    }
}

$PSBoundParameters['BackupPath'] = $BackupPath
Invoke-LockScreenProvider @PSBoundParameters
