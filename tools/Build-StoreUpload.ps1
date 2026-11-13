$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'OhMyLockScreen\OhMyLockScreen.csproj'
$packageDirectory = Join-Path $repository 'artifacts\store'

# Generate the Store signing certificate before MSIX tooling prepares the manifest.
& $msbuild $project -restore '-target:_ValidateSigningCertificate;_GenerateAppxPackage' -nologo -verbosity:minimal `
    -p:Configuration=Release `
    -p:Platform=x64 `
    -p:UapAppxPackageBuildMode=StoreOnly `
    "-p:AppxPackageDir=$packageDirectory/"

exit $LASTEXITCODE
