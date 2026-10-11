<#
.SYNOPSIS
  Builds PharmaBill in Release, optionally runs tests, publishes the app, and compiles the installer.
.PARAMETER SkipTests
  Skip unit tests and proceed directly to publish + Inno Setup packaging.
.PARAMETER SkipInstaller
  Stop after publishing (do not compile Inno Setup).
.PARAMETER InnoSetupPath
  Full path to ISCC.exe if Inno Setup is not in a standard location.
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$SkipInstaller,
    [string]$InnoSetupPath
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$solution = Join-Path $root 'PharmaBill.slnx'
$appProject = Join-Path $root 'PharmaBill.App\PharmaBill.App.csproj'
$testProject = Join-Path $root 'PharmaBill.Tests\PharmaBill.Tests.csproj'
$publishDir = Join-Path $root 'artifacts\publish\win-x64'
$installerDir = Join-Path $root 'artifacts\installer'
$issScript = Join-Path $root 'installer\PharmaBill.iss'
$buildDate = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "$Name failed (exit code $LASTEXITCODE)."
    }
}

[xml]$csproj = Get-Content $appProject
$version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw 'No <Version> found in PharmaBill.App.csproj.' }
Write-Host "PharmaBill $version (build date $buildDate)"

Invoke-Step 'Restore' { dotnet restore $solution }
Invoke-Step 'Build (Release)' {
    # Do not pass RuntimeIdentifier at solution scope (NETSDK1134). RID is set on publish below.
    dotnet build $solution -c Release --no-restore "-p:BuildDateUtc=$buildDate"
}

if ($SkipTests) {
    Write-Warning 'Tests skipped (-SkipTests). Proceeding to publish and installer packaging.'
} else {
    Invoke-Step 'Unit tests (Release)' {
        dotnet test $testProject -c Release --no-build
    }
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force $publishDir | Out-Null

Invoke-Step 'Publish (self-contained, single-file, win-x64)' {
    dotnet publish $appProject `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        "-p:BuildDateUtc=$buildDate" `
        -o $publishDir
}

$exePath = Join-Path $publishDir 'PharmaBill.App.exe'
if (-not (Test-Path $exePath)) {
    throw "Publish output not found: $exePath"
}
Write-Host "Published to $publishDir"

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
    Write-Warning 'Inno Setup 6 (ISCC.exe) was not found — publish succeeded, installer skipped. Install via `winget install JRSoftware.InnoSetup` or pass -InnoSetupPath.'
    return
}

if (-not (Test-Path $issScript)) {
    throw "Inno script not found: $issScript"
}

New-Item -ItemType Directory -Force $installerDir | Out-Null
Invoke-Step 'Compile installer' {
    & $iscc $issScript `
        "/DAppVersion=$version" `
        "/DPublishDir=$publishDir" `
        "/DOutputDir=$installerDir"
}

$setupPath = Join-Path $installerDir 'PharmaBill_Setup.exe'
if (-not (Test-Path $setupPath)) {
    # Fallback: older naming PharmaBill-Setup-{version}.exe
    $legacy = Join-Path $installerDir "PharmaBill-Setup-$version.exe"
    if (Test-Path $legacy) {
        Copy-Item $legacy $setupPath -Force
    }
}

if (-not (Test-Path $setupPath)) {
    throw "Installer EXE was not produced under $installerDir"
}

Write-Host "Installer: $setupPath" -ForegroundColor Green
Get-Item $setupPath | Format-List FullName, Length, LastWriteTime
