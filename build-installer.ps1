<#
.SYNOPSIS
  Builds the Pharma Remit Local Market edition (USB / direct download) and compiles its Inno Setup installer.
.DESCRIPTION
  One source, two editions. This script publishes PharmaBill.App with -p:Edition=LocalMarket; the Microsoft Store
  edition is packaged by build-msstore-package.ps1. Product name and version come from
  PharmaBill.App\Editions\LocalMarket.props.

  Tests are not run here. They run once, against the shared code, with:
    dotnet test PharmaBill.slnx -c Release
.PARAMETER SkipInstaller
  Stop after publishing (do not compile Inno Setup).
.PARAMETER InnoSetupPath
  Full path to ISCC.exe if Inno Setup is not in a standard location.
#>
[CmdletBinding()]
param(
    [switch]$SkipInstaller,
    [string]$InnoSetupPath
)

$ErrorActionPreference = 'Stop'
$edition = 'LocalMarket'
$root = $PSScriptRoot
$appProject = Join-Path $root 'PharmaBill.App\PharmaBill.App.csproj'
$publishDir = Join-Path $root 'artifacts\publish\local-market'
$installerDir = Join-Path $root 'artifacts\installer'
$issScript = Join-Path $root 'installer\PharmaRemitLocalMarket.iss'
$buildDate = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "$Name failed (exit code $LASTEXITCODE)."
    }
}

# Name, version and constants exactly as the Release build of this edition will see them.
$props = dotnet msbuild $appProject -p:Configuration=Release "-p:Edition=$edition" -p:MasterAdminBuild=false `
    -getProperty:Product -getProperty:Version -getProperty:DefineConstants | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "Could not evaluate the $edition edition properties." }
$product = $props.Properties.Product
$version = $props.Properties.Version
$constants = $props.Properties.DefineConstants
if ($constants -notmatch '(^|;)LOCAL_MARKET_EDITION(;|$)' -or $constants -match 'STORE_EDITION') {
    throw "Release constants are not the Local Market edition: $constants"
}
if ($constants -match 'MASTER_ADMIN_BUILD') { throw "Release constants contain MASTER_ADMIN_BUILD: $constants" }
Write-Host "$product $version (edition $edition, build date $buildDate)"

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force $publishDir | Out-Null

Invoke-Step "Publish $edition (Release, win-x64, self-contained)" {
    # Not single-file: single-file leaves Assembly.Location empty and WPF content-file pack URIs crash at startup.
    dotnet publish $appProject `
        -c Release `
        -r win-x64 `
        --self-contained true `
        "-p:Edition=$edition" `
        -p:MasterAdminBuild=false `
        -p:PublishSingleFile=false `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        "-p:BuildDateUtc=$buildDate" `
        -o $publishDir
}

$exePath = Join-Path $publishDir 'PharmaBill.App.exe'
if (-not (Test-Path $exePath)) { throw "Publish output not found: $exePath" }
$exeInfo = (Get-Item $exePath).VersionInfo
if ($exeInfo.ProductName -ne $product) { throw "Published exe reports product '$($exeInfo.ProductName)', expected '$product'." }
# One version everywhere: the file version, the product (About screen) version and the installer's.
if ($exeInfo.FileVersion -ne $version -or $exeInfo.ProductVersion -ne $version) {
    throw "Published exe reports file $($exeInfo.FileVersion) / product $($exeInfo.ProductVersion), expected $version for both."
}
$appDll = Join-Path $publishDir 'PharmaBill.App.dll'
$assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($appDll).Version.ToString()
if ($assemblyVersion -ne $version) { throw "PharmaBill.App.dll assembly version is $assemblyVersion, expected $version." }

# Self-contained: the .NET runtime ships inside, so a pharmacy PC needs nothing installed first.
foreach ($runtimeFile in 'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'System.Private.CoreLib.dll', 'PresentationFramework.dll') {
    if (-not (Test-Path (Join-Path $publishDir $runtimeFile))) { throw "Self-contained publish is missing $runtimeFile." }
}
$runtimeConfig = Get-Content (Join-Path $publishDir 'PharmaBill.App.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $runtimeConfig.runtimeOptions.includedFrameworks) { throw 'runtimeconfig.json does not list included frameworks: the publish is not self-contained.' }

# Nothing secret ships: no database, key, certificate or keystore files, and no key or credential text in any file.
$forbiddenFiles = Get-ChildItem $publishDir -Recurse -File | Where-Object {
    $_.Extension -in '.db', '.sqlite', '.db-wal', '.db-shm', '.pfx', '.p12', '.pem', '.key', '.snk', '.jks', '.keystore', '.pbbak', '.dpapi' -or
    ($_.Extension -ne '.dll' -and ($_.Name -like '*secret*' -or $_.Name -like '*credential*')) -or $_.Name -eq 'database.key'
}
if ($forbiddenFiles) { throw "Publish output contains files that must not ship: $($forbiddenFiles.FullName -join ', ')" }
$textPatterns = @(
    'AIza[0-9A-Za-z_\-]{35}',                      # Google / Gemini API key
    'AKIA[0-9A-Z]{16}',                             # AWS access key
    '-----BEGIN [A-Z ]*PRIVATE KEY-----',
    'GOCSPX-[0-9A-Za-z_\-]{20,}',                  # Google OAuth client secret
    '(?i)(password|pwd)\s*=\s*[^;"''\s{]{4,}',    # a connection string carrying a password
    'pharmabill-license-private',
    'PHARMABILL_LICENSE_PRIVATE_KEY_PEM'
)
$leaks = foreach ($file in Get-ChildItem $publishDir -Recurse -File | Where-Object { $_.Name -like 'PharmaBill*' -or $_.Extension -in '.json', '.config', '.xml', '.txt', '.md' }) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    $text = [Text.Encoding]::UTF8.GetString($bytes) + [Text.Encoding]::Unicode.GetString($bytes)
    foreach ($pattern in $textPatterns) {
        $match = [regex]::Match($text, $pattern)
        if ($match.Success) { "$($file.Name): /$pattern/ -> $($match.Value.Substring(0, [Math]::Min(24, $match.Value.Length)))..." }
    }
}
if ($leaks) { throw "Publish output contains key or credential text:`n$($leaks -join "`n")" }

$publishBytes = (Get-ChildItem $publishDir -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Published to $publishDir ({0:N1} MB, {1} files, .NET runtime included; no keys, credentials or databases)" -f ($publishBytes / 1MB), (Get-ChildItem $publishDir -Recurse -File).Count)

if ($SkipInstaller) {
    Write-Host 'Installer compilation skipped (-SkipInstaller).'
    return
}

$candidates = @(
    $InnoSetupPath,
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path $_) }
$iscc = $candidates | Select-Object -First 1
if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) {
    throw 'Inno Setup 6 (ISCC.exe) was not found. Install it with `winget install JRSoftware.InnoSetup` or pass -InnoSetupPath.'
}
if (-not (Test-Path $issScript)) { throw "Inno script not found: $issScript" }

New-Item -ItemType Directory -Force $installerDir | Out-Null
$setupPath = Join-Path $installerDir "PharmaRemit_Setup_$version.exe"
if (Test-Path $setupPath) { Remove-Item $setupPath -Force }
Invoke-Step 'Compile installer' {
    & $iscc $issScript `
        "/DAppName=$product" `
        "/DAppVersion=$version" `
        "/DPublishDir=$publishDir" `
        "/DOutputDir=$installerDir"
}

if (-not (Test-Path $setupPath)) { throw "Installer EXE was not produced: $setupPath" }
$setupInfo = (Get-Item $setupPath).VersionInfo
if ($setupInfo.FileVersion.Trim() -ne $version -or $setupInfo.ProductVersion.Trim() -ne $version) { throw "Installer reports version $($setupInfo.FileVersion), expected $version." }

Write-Host "Installer: $setupPath" -ForegroundColor Green
Get-Item $setupPath | Format-List FullName, Length, LastWriteTime
