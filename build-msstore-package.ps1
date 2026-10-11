<#
.SYNOPSIS
  Publishes PharmaBill.App (Release, MASTER_ADMIN_BUILD off) and packs a Store MSIX plus .msixupload.
  Does not call build-installer.ps1 or Inno Setup.
.PARAMETER PackageName
  Partner Center Package/Identity/Name. Default in the manifest is SAER.PharmaBill.
.PARAMETER Publisher
  Partner Center Package/Identity/Publisher (CN=...). Default is CN=REPLACE-WITH-PARTNER-CENTER-CN.
.PARAMETER PublisherDisplayName
  Partner Center Package/Properties/PublisherDisplayName. Default in the manifest is S.A.E.R.
.PARAMETER DisplayName
  Reserved app name (manifest DisplayName).
.PARAMETER Version
  Four-part package version ending in .0. Default: csproj Version plus .0.
.PARAMETER CertPath
  Optional PFX for local sideload only. Leave unsigned for Store submission.
.PARAMETER CertPassword
  Password for CertPath.
.PARAMETER MakeAppxPath
  Full path to makeappx.exe if it is not in the Windows SDK or the Windows SDK BuildTools package.
#>
[CmdletBinding()]
param(
    [string]$PackageName,
    [string]$Publisher,
    [string]$PublisherDisplayName,
    [string]$DisplayName,
    [string]$Version,
    [string]$CertPath,
    [string]$CertPassword,
    [string]$MakeAppxPath
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$appDir = Join-Path $root 'PharmaBill.App'
$appProject = Join-Path $appDir 'PharmaBill.App.csproj'
$manifestSrc = Join-Path $appDir 'Package.appxmanifest'
$outDir = Join-Path $root 'artifacts\msstore'
$layout = Join-Path $outDir 'layout'
$buildDate = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    $global:LASTEXITCODE = 0
    & $Action
    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "$Name failed (exit code $LASTEXITCODE)." }
}

if (-not ('PharmaBillMsixScan' -as [type])) {
    Add-Type -TypeDefinition @'
using System.Text;
public static class PharmaBillMsixScan {
    public static bool ContainsUtf16(byte[] hay, string needle) {
        byte[] n = Encoding.Unicode.GetBytes(needle);
        if (n.Length == 0 || hay.Length < n.Length) return false;
        for (int i = 0; i <= hay.Length - n.Length; i++) {
            int j = 0;
            for (; j < n.Length; j++) {
                if (hay[i + j] != n[j]) break;
            }
            if (j == n.Length) return true;
        }
        return false;
    }
}
'@
}

if (-not (Test-Path $appProject)) { throw "App project not found: $appProject" }
if (-not (Test-Path $manifestSrc)) { throw "Manifest not found: $manifestSrc" }

[xml]$csproj = Get-Content $appProject
if (-not $Version) {
    $v = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    if (-not $v) { throw 'No <Version> found in PharmaBill.App.csproj.' }
    $parts = @($v.ToString().Split('.'))
    while ($parts.Count -lt 3) { $parts += '0' }
    $Version = ($parts[0..2] -join '.') + '.0'
}
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') {
    throw "Version '$Version' must be Major.Minor.Build.0 (Store requires the revision to be 0)."
}

$defineConstants = dotnet msbuild $appProject -getProperty:DefineConstants -p:Configuration=Release -p:MasterAdminBuild=false
if ($LASTEXITCODE -ne 0) { throw 'Could not evaluate Release DefineConstants.' }
if ($defineConstants -match 'MASTER_ADMIN_BUILD') {
    throw "Release DefineConstants still contain MASTER_ADMIN_BUILD: $defineConstants"
}
Write-Host "Release constants: $defineConstants (MASTER_ADMIN_BUILD off)"

if (-not $MakeAppxPath) {
    $candidates = @()
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path $kits) {
        $candidates += Get-ChildItem $kits -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' }
    }
    $pkgs = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools'
    if (Test-Path $pkgs) {
        $candidates += Get-ChildItem $pkgs -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' }
    }
    $MakeAppxPath = ($candidates | Sort-Object FullName -Descending | Select-Object -First 1).FullName
}
if (-not $MakeAppxPath -or -not (Test-Path $MakeAppxPath)) {
    throw 'makeappx.exe not found. Install the Windows 10 SDK or pass -MakeAppxPath.'
}

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $layout | Out-Null

Invoke-Step 'Publish (Release, win-x64, self-contained, MASTER_ADMIN_BUILD off)' {
    dotnet publish $appProject -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false `
        -p:MasterAdminBuild=false -p:GenerateAppxPackageOnBuild=false -p:PublishAppxPackage=false `
        "-p:BuildDateUtc=$buildDate" -o $layout
}

$exePath = Join-Path $layout 'PharmaBill.App.exe'
if (-not (Test-Path $exePath)) { throw 'PharmaBill.App.exe missing from publish output.' }

$exeBytes = [IO.File]::ReadAllBytes($exePath)
foreach ($secret in @(
    'RSA private key not found',
    'PHARMABILL_LICENSE_PRIVATE_KEY_PEM',
    'pharmabill-license-private.pem'
)) {
    if ([PharmaBillMsixScan]::ContainsUtf16($exeBytes, $secret)) {
        throw "Published PharmaBill.App.exe contains master-admin signing text ('$secret'). MASTER_ADMIN_BUILD leaked into this Release publish."
    }
}
Remove-Variable exeBytes

Get-ChildItem $layout -Recurse -File | Where-Object { $_.Extension -eq '.pdb' } | Remove-Item -Force

$manifest = New-Object System.Xml.XmlDocument
$manifest.PreserveWhitespace = $true
$manifest.Load($manifestSrc)
$ns = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$ns.AddNamespace('d', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$id = $manifest.SelectSingleNode('/d:Package/d:Identity', $ns)
$props = $manifest.SelectSingleNode('/d:Package/d:Properties', $ns)
if (-not $id -or -not $props) { throw 'Package.appxmanifest is missing Identity or Properties.' }
if ($PackageName) { $id.SetAttribute('Name', $PackageName) }
if ($Publisher) { $id.SetAttribute('Publisher', $Publisher) }
$id.SetAttribute('Version', $Version)
if ($PublisherDisplayName) { $props.SelectSingleNode('d:PublisherDisplayName', $ns).InnerText = $PublisherDisplayName }
if ($DisplayName) {
    $props.SelectSingleNode('d:DisplayName', $ns).InnerText = $DisplayName
    $visual = $manifest.SelectSingleNode('//*[local-name()="VisualElements"]')
    if ($visual) { $visual.SetAttribute('DisplayName', $DisplayName) }
    $tile = $manifest.SelectSingleNode('//*[local-name()="DefaultTile"]')
    if ($tile) { $tile.SetAttribute('ShortName', $DisplayName) }
}

$stagedManifest = Join-Path $layout 'AppxManifest.xml'
$writerSettings = New-Object System.Xml.XmlWriterSettings
$writerSettings.Indent = $true
$writerSettings.Encoding = New-Object System.Text.UTF8Encoding($false)
$writer = [System.Xml.XmlWriter]::Create($stagedManifest, $writerSettings)
try { $manifest.Save($writer) } finally { $writer.Dispose() }

$check = New-Object System.Xml.XmlDocument
$check.Load($stagedManifest)
$checkNs = New-Object System.Xml.XmlNamespaceManager($check.NameTable)
$checkNs.AddNamespace('d', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$family = $check.SelectSingleNode('/d:Package/d:Dependencies/d:TargetDeviceFamily', $checkNs)
$app = $check.SelectSingleNode('/d:Package/d:Applications/d:Application', $checkNs)
$caps = $check.SelectNodes('//*[local-name()="Capability"]') | ForEach-Object { $_.GetAttribute('Name') }
if ($family.GetAttribute('Name') -ne 'Windows.Desktop' -or $family.GetAttribute('MinVersion') -ne '10.0.17763.0') {
    throw 'Staged manifest must target Windows.Desktop with MinVersion 10.0.17763.0.'
}
if ($app.GetAttribute('Executable') -ne 'PharmaBill.App.exe' -or $app.GetAttribute('EntryPoint') -ne 'Windows.FullTrustApplication') {
    throw 'Staged manifest must launch PharmaBill.App.exe as Windows.FullTrustApplication.'
}
if ($caps -notcontains 'runFullTrust') { throw 'Staged manifest is missing runFullTrust.' }

$stagedPublisher = $check.SelectSingleNode('/d:Package/d:Identity', $checkNs).GetAttribute('Publisher')
if ($stagedPublisher -like '*REPLACE-WITH-PARTNER-CENTER-CN*') {
    Write-Warning 'Publisher is still CN=REPLACE-WITH-PARTNER-CENTER-CN. Re-run with -Publisher from Partner Center (Product identity) before you upload.'
}

$assetsDst = Join-Path $layout 'Assets\Store'
New-Item -ItemType Directory -Force -Path $assetsDst | Out-Null
foreach ($asset in @(
    'Square44x44Logo.png',
    'Square150x150Logo.png',
    'Wide310x150Logo.png',
    'StoreLogo.png',
    'SplashScreen.png'
)) {
    $srcAsset = Join-Path $appDir "Assets\Store\$asset"
    if (-not (Test-Path $srcAsset)) { throw "Missing store asset: $srcAsset" }
    Copy-Item $srcAsset (Join-Path $assetsDst $asset) -Force
}

$msix = Join-Path $outDir "PharmaBill_${Version}_x64.msix"
Invoke-Step 'Pack MSIX' { & $MakeAppxPath pack /d $layout /p $msix /o }

if ($CertPath) {
    $signtool = Join-Path (Split-Path $MakeAppxPath) 'signtool.exe'
    if (-not (Test-Path $signtool)) { throw "signtool.exe not found next to makeappx.exe ($signtool)." }
    $signArgs = @('sign', '/fd', 'SHA256', '/f', $CertPath)
    if ($CertPassword) { $signArgs += @('/p', $CertPassword) }
    $signArgs += $msix
    Invoke-Step 'Sign MSIX (local sideload only)' { & $signtool @signArgs }
}

$upload = Join-Path $outDir "PharmaBill_${Version}_x64.msixupload"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path $upload) { Remove-Item $upload -Force }
$zipStream = [System.IO.File]::Open($upload, [System.IO.FileMode]::CreateNew)
try {
    $archive = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $msix,
            [System.IO.Path]::GetFileName($msix),
            [System.IO.Compression.CompressionLevel]::NoCompression) | Out-Null
    } finally {
        $archive.Dispose()
    }
} finally {
    if ($zipStream) { $zipStream.Dispose() }
}

Write-Host "MSIX:       $msix" -ForegroundColor Green
Write-Host "MSIXUPLOAD: $upload" -ForegroundColor Green
Write-Host 'Upload the .msixupload. Leave it unsigned so Partner Center can sign it.'
