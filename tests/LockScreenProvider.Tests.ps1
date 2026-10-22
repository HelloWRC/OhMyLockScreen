#requires -Version 5.1
# Standalone checks: package queries and registry access are replaced with in-memory fakes.
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '..\scripts\LockScreenProvider.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref] $tokens, [ref] $parseErrors)
if ($parseErrors.Count -ne 0) { throw ($parseErrors | Out-String) }
$functions = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)
. ([scriptblock]::Create(($functions.Extent.Text -join "`n")))

function Get-AppxPackage {
    [CmdletBinding()]
    param([string] $Name)
    if ($Name -ne 'dev.hellowrc.OhMyLockScreen') { throw 'Unexpected package query.' }
    return $script:packages
}
function Get-AppxPackageManifest {
    [CmdletBinding()]
    param([string] $Package)
    if ($Package -ne 'TestPackageFullName') { throw 'Unexpected manifest query.' }
    $category = if ($script:missingExtension) { 'windows.lockScreenCall' } else { 'windows.lockScreen' }
    return [xml] ('<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"><Applications><Application Id="App"><Extensions><uap:Extension Category="' + $category + '" /></Extensions></Application></Applications></Package>')
}
function Get-LockProviderUserSid { return 'S-1-5-21-test-user' }
function Get-LockProviderSelection { return $script:selection }
function Set-LockProviderSelection {
    param($Selection)
    $script:writes++
    $script:selection = [pscustomobject]@{ Exists = $Selection.Exists; Kind = $Selection.Kind; Value = $Selection.Value }
}
function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}
function Assert-Throws {
    param([scriptblock] $Body, [string] $MessagePattern)
    try { & $Body }
    catch {
        if ($_.Exception.Message -notlike $MessagePattern) { throw }
        return
    }
    throw "Expected failure: $MessagePattern"
}

$testDirectory = Join-Path $PSScriptRoot ('.tmp.' + [guid]::NewGuid().ToString('N'))
[void] [System.IO.Directory]::CreateDirectory($testDirectory)
$backupPath = Join-Path $testDirectory 'backup.clixml'
$script:passed = 0
function Invoke-Case {
    param([string] $Name, [scriptblock] $Body)
    if (Test-Path -LiteralPath $backupPath) { Remove-Item -LiteralPath $backupPath }
    $script:selection = [pscustomobject]@{ Exists = $true; Kind = 'String'; Value = 'Original!App' }
    $script:packages = @([pscustomobject]@{ PackageFullName = 'TestPackageFullName'; PackageFamilyName = 'TestFamily' })
    $script:missingExtension = $false
    $script:writes = 0
    & $Body
    $script:passed++
    Write-Output "PASS: $Name"
}

try {
    Invoke-Case 'Uninstalled package leaves settings and backup untouched' {
        $script:packages = @()
        Assert-Throws { Invoke-LockScreenProvider Enable $backupPath } '*Deploy the package first*'
        Assert-True ($script:writes -eq 0 -and -not (Test-Path $backupPath)) 'Uninstalled package caused a mutation.'
    }
    Invoke-Case 'Missing extension leaves settings and backup untouched' {
        $script:missingExtension = $true
        Assert-Throws { Invoke-LockScreenProvider Enable $backupPath } '*does not declare windows.lockScreen*'
        Assert-True ($script:writes -eq 0 -and -not (Test-Path $backupPath)) 'Invalid manifest caused a mutation.'
    }
    Invoke-Case 'Script entry point forwards explicit BackupPath and WhatIf' {
        # Constants keep these fakes independent of the child script's script scope.
        function Get-AppxPackage {
            [CmdletBinding()]
            param([string] $Name)
            return [pscustomobject]@{ PackageFullName = 'TestPackageFullName'; PackageFamilyName = 'TestFamily' }
        }
        function Get-AppxPackageManifest {
            [CmdletBinding()]
            param([string] $Package)
            return [xml] '<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"><Applications><Application Id="App"><Extensions><uap:Extension Category="windows.lockScreenCall" /></Extensions></Application></Applications></Package>'
        }
        Assert-Throws { & $source -Action Enable -BackupPath $backupPath -WhatIf } '*does not declare windows.lockScreen*'
        Assert-Throws { & $source -Action Restore -BackupPath $backupPath -WhatIf } '*Backup not found*'
        Assert-True ($script:writes -eq 0 -and -not (Test-Path $backupPath)) 'Entry point caused a mutation.'
    }
    Invoke-Case 'Enable WhatIf writes neither registry nor backup' {
        Invoke-LockScreenProvider Enable $backupPath -WhatIf
        Assert-True ($script:writes -eq 0 -and -not (Test-Path $backupPath)) 'WhatIf caused a mutation.'
    }
    Invoke-Case 'Enable uses actual family and preserves backup across repeated enable' {
        Invoke-LockScreenProvider Enable $backupPath
        $firstBackup = [System.IO.File]::ReadAllBytes($backupPath)
        Assert-True ($script:selection.Value -eq 'TestFamily!App' -and $script:selection.Kind -eq 'String') 'Incorrect AUMID or type.'
        Invoke-LockScreenProvider Enable $backupPath
        Assert-True ([Convert]::ToBase64String($firstBackup) -eq [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($backupPath))) 'Original backup overwritten.'
        Invoke-LockScreenProvider Restore $backupPath
        Assert-True ($script:selection.Value -eq 'Original!App' -and -not (Test-Path $backupPath)) 'Original setting was not restored.'
    }
    Invoke-Case 'Absent original value is removed on restore' {
        $script:selection = [pscustomobject]@{ Exists = $false; Kind = $null; Value = $null }
        Invoke-LockScreenProvider Enable $backupPath
        Invoke-LockScreenProvider Restore $backupPath
        Assert-True (-not $script:selection.Exists) 'Absent original value was not restored.'
    }
    Invoke-Case 'Restore WhatIf keeps current setting and backup' {
        Invoke-LockScreenProvider Enable $backupPath
        $script:writes = 0
        Invoke-LockScreenProvider Restore $backupPath -WhatIf
        Assert-True ($script:writes -eq 0 -and (Test-Path $backupPath) -and $script:selection.Value -eq 'TestFamily!App') 'Restore WhatIf caused a mutation.'
    }
    Invoke-Case 'Missing backup prevents restore' {
        Assert-Throws { Invoke-LockScreenProvider Restore $backupPath } '*Backup not found*'
        Assert-True ($script:writes -eq 0) 'Missing backup caused a registry write.'
    }
    Invoke-Case 'Invalid backup prevents enable and restore' {
        [pscustomobject]@{ SchemaVersion = 0 } | Export-Clixml -LiteralPath $backupPath
        Assert-Throws { Invoke-LockScreenProvider Enable $backupPath } '*Invalid backup*'
        Assert-Throws { Invoke-LockScreenProvider Restore $backupPath } '*Invalid backup*'
        Assert-True ($script:writes -eq 0) 'Invalid backup caused a registry write.'
    }
    Invoke-Case 'Backup from another user prevents restore' {
        Invoke-LockScreenProvider Enable $backupPath
        $backup = Import-Clixml -LiteralPath $backupPath
        $backup.UserSid = 'AnotherUser'
        $backup | Export-Clixml -LiteralPath $backupPath
        $script:writes = 0
        Assert-Throws { Invoke-LockScreenProvider Restore $backupPath } '*another user*'
        Assert-True ($script:writes -eq 0) 'Foreign backup caused a registry write.'
    }
    Invoke-Case 'Invalid backup value type prevents restore' {
        Invoke-LockScreenProvider Enable $backupPath
        $backup = Import-Clixml -LiteralPath $backupPath
        $backup.Kind = 'DWord'
        $backup | Export-Clixml -LiteralPath $backupPath
        $script:writes = 0
        Assert-Throws { Invoke-LockScreenProvider Restore $backupPath } '*Invalid registry type*'
        Assert-True ($script:writes -eq 0) 'Invalid value type caused a registry write.'
    }
    Invoke-Case 'Existing backup cannot be overwritten by backup writer' {
        Invoke-LockScreenProvider Enable $backupPath
        $before = Get-Content -LiteralPath $backupPath -Raw
        Assert-Throws { Save-LockProviderBackup -Backup $script:selection -Path $backupPath } '*'
        Assert-True ((Get-Content -LiteralPath $backupPath -Raw) -eq $before) 'Backup writer overwrote original.'
    }
    Invoke-Case 'Backup failure prevents registry write' {
        # A file in place of the parent directory makes saving fail deterministically.
        'blocked' | Set-Content -LiteralPath $backupPath
        Assert-Throws { Invoke-LockScreenProvider Enable (Join-Path $backupPath 'bad.clixml') } '*'
        Assert-True ($script:writes -eq 0) 'Backup failure caused a registry write.'
    }
    Invoke-Case 'Enabled provider with missing original backup is rejected' {
        $script:selection.Value = 'TestFamily!App'
        Assert-Throws { Invoke-LockScreenProvider Enable $backupPath } '*original backup is missing*'
        Assert-True ($script:writes -eq 0 -and -not (Test-Path $backupPath)) 'Backed up our own selection.'
    }
    foreach ($entry in @(
        @{ Kind = 'ExpandString'; Value = '%SystemRoot%\Original!App' },
        @{ Kind = 'DWord'; Value = [int] 42 },
        @{ Kind = 'QWord'; Value = [long] 4294967296 },
        @{ Kind = 'Binary'; Value = [byte[]] @(0, 1, 255) },
        @{ Kind = 'None'; Value = [byte[]] @(2, 3) },
        @{ Kind = 'MultiString'; Value = [string[]] @('first', 'second') }
    )) {
        Invoke-Case "Restores original $($entry.Kind) type and value" {
            $script:selection = [pscustomobject]@{ Exists = $true; Kind = $entry.Kind; Value = $entry.Value }
            Invoke-LockScreenProvider Enable $backupPath
            Invoke-LockScreenProvider Restore $backupPath
            Assert-True ($script:selection.Kind -eq $entry.Kind) 'Registry type changed.'
            Assert-True (($script:selection.Value | ConvertTo-Json -Compress) -eq ($entry.Value | ConvertTo-Json -Compress)) 'Registry value changed.'
        }
    }
    Write-Output "All $script:passed checks passed. No real package or registry operations were performed."
}
finally {
    # The dedicated directory contains only test files; do not recursively delete a computed path.
    Get-ChildItem -LiteralPath $testDirectory -File | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
    Remove-Item -LiteralPath $testDirectory
}
