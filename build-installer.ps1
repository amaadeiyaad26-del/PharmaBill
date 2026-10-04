<#
.SYNOPSIS
  Builds PharmaBill in Release, runs all tests, publishes the app and compiles the installer.
.PARAMETER SkipTests
  Skip the unit tests (not recommended for a release).
.PARAMETER SkipInstaller
  Stop after publishing.
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
$buildDate = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit code $LASTEXITCODE)." }
}

[xml]$csproj = Get-Content $appProject
$version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw 'No <Version> found in PharmaBill.App.csproj.' }
Write-Host "PharmaBill $version (build date $buildDate)"

Invoke-Step 'Restore' { dotnet restore $solution }
Invoke-Step 'Build (Release)' { dotnet build $solution -c Release --no-restore "-p:BuildDateUtc=$buildDate" }

if ($SkipTests) {
    Write-Warning 'Tests skipped.'
} else {
    Invoke-Step 'Unit tests (Release)' { dotnet test $testProject -c Release --no-build }
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Invoke-Step 'Publish (self-contained, ReadyToRun, win-x64)' {
    dotnet publish $appProject -c Release -p:PublishProfile=Release-win-x64 "-p:BuildDateUtc=$buildDate"
}
if (-not (Test-Path (Join-Path $publishDir 'PharmaBill.App.exe'))) { throw "Publish output not found in $publishDir." }
Write-Host "Published to $publishDir"

if ($SkipInstaller) { return }

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
    throw 'Inno Setup 6 (ISCC.exe) was not found. Install it (winget install JRSoftware.InnoSetup) or pass -InnoSetupPath.'
}

New-Item -ItemType Directory -Force $installerDir | Out-Null
Invoke-Step 'Compile installer' {
    & $iscc (Join-Path $root 'installer\PharmaBill.iss') "/DAppVersion=$version" "/DPublishDir=$publishDir" "/DOutputDir=$installerDir"
}
Write-Host "Installer: $(Join-Path $installerDir "PharmaBill-Setup-$version.exe")" -ForegroundColor Green
