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
$exeProduct = (Get-Item $exePath).VersionInfo.ProductName
if ($exeProduct -ne $product) { throw "Published exe reports product '$exeProduct', expected '$product'." }
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
    throw 'Inno Setup 6 (ISCC.exe) was not found. Install it with `winget install JRSoftware.InnoSetup` or pass -InnoSetupPath.'
}
if (-not (Test-Path $issScript)) { throw "Inno script not found: $issScript" }

New-Item -ItemType Directory -Force $installerDir | Out-Null
$setupPath = Join-Path $installerDir 'PharmaRemitLocalMarket_Setup.exe'
if (Test-Path $setupPath) { Remove-Item $setupPath -Force }
Invoke-Step 'Compile installer' {
    & $iscc $issScript `
        "/DAppName=$product" `
        "/DAppVersion=$version" `
        "/DPublishDir=$publishDir" `
        "/DOutputDir=$installerDir"
}

if (-not (Test-Path $setupPath)) { throw "Installer EXE was not produced: $setupPath" }

Write-Host "Installer: $setupPath" -ForegroundColor Green
Get-Item $setupPath | Format-List FullName, Length, LastWriteTime
