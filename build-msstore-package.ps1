<#
.SYNOPSIS
  Builds the Microsoft Store edition of Pharma Remit: publishes PharmaBill.App with -p:Edition=Store (Release,
  MASTER_ADMIN_BUILD off) and packs an unsigned MSIX plus .msixupload. The Local Market edition is built by
  build-installer.ps1. Product name and version come from PharmaBill.App\Editions\Store.props.

  Tests are not run here. They run once, against the shared code, with:
    dotnet test PharmaBill.slnx -c Release
.PARAMETER PackageName
  Partner Center Package/Identity/Name. Default in the manifest is SAER.PharmaBill.
.PARAMETER Publisher
  Partner Center Package/Identity/Publisher (CN=...). Default is CN=REPLACE-WITH-PARTNER-CENTER-CN.
.PARAMETER PublisherDisplayName
  Partner Center Package/Properties/PublisherDisplayName. Default in the manifest is S.A.E.R.
.PARAMETER DisplayName
  Reserved app name (manifest DisplayName).
.PARAMETER Version
  Four-part package version ending in .0. Default: the Version in Editions\Store.props plus .0.
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

$edition = 'Store'
$props = dotnet msbuild $appProject -p:Configuration=Release "-p:Edition=$edition" -p:MasterAdminBuild=false `
    -getProperty:Version -getProperty:DefineConstants -getProperty:Product | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "Could not evaluate the $edition edition properties." }
if (-not $Version) {
    $v = $props.Properties.Version
    if (-not $v) { throw 'No Version in PharmaBill.App\Editions\Store.props.' }
    $parts = @($v.ToString().Split('.'))
    while ($parts.Count -lt 3) { $parts += '0' }
    $Version = ($parts[0..2] -join '.') + '.0'
}
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') {
    throw "Version '$Version' must be Major.Minor.Build.0 (Store requires the revision to be 0)."
}

$defineConstants = $props.Properties.DefineConstants
if ($defineConstants -match 'MASTER_ADMIN_BUILD') {
    throw "Release DefineConstants still contain MASTER_ADMIN_BUILD: $defineConstants"
}
if ($defineConstants -notmatch '(^|;)STORE_EDITION(;|$)' -or $defineConstants -match 'LOCAL_MARKET_EDITION') {
    throw "Release constants are not the Store edition: $defineConstants"
}
Write-Host "$($props.Properties.Product) $Version, constants: $defineConstants (MASTER_ADMIN_BUILD off)"

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

Invoke-Step 'Publish Store edition (Release, win-x64, self-contained, MASTER_ADMIN_BUILD off)' {
    # Not single-file: single-file leaves Assembly.Location empty and WPF content-file pack URIs
    # (e.g. the window icon) then crash MainWindow at startup.
    dotnet publish $appProject -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false `
        "-p:Edition=$edition" -p:MasterAdminBuild=false -p:GenerateAppxPackageOnBuild=false -p:PublishAppxPackage=false `
        "-p:BuildDateUtc=$buildDate" -o $layout
}

$exePath = Join-Path $layout 'PharmaBill.App.exe'
if (-not (Test-Path $exePath)) { throw 'PharmaBill.App.exe missing from publish output.' }
$exeProduct = (Get-Item $exePath).VersionInfo.ProductName
if ($exeProduct -ne $props.Properties.Product) { throw "Published exe reports product '$exeProduct', expected '$($props.Properties.Product)'." }

# App code lives in PharmaBill.*.dll (not single-file), so scan those as well as the exe.
$scanned = Get-ChildItem $layout -File | Where-Object { $_.Name -like 'PharmaBill*.dll' -or $_.Name -eq 'PharmaBill.App.exe' }
if (-not ($scanned | Where-Object Name -eq 'PharmaBill.App.dll')) { throw 'PharmaBill.App.dll missing from publish output.' }
foreach ($file in $scanned) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    foreach ($secret in @(
        'RSA private key not found',
        'PHARMABILL_LICENSE_PRIVATE_KEY_PEM',
        'pharmabill-license-private.pem'
    )) {
        if ([PharmaBillMsixScan]::ContainsUtf16($bytes, $secret)) {
            throw "Published $($file.Name) contains master-admin signing text ('$secret'). MASTER_ADMIN_BUILD leaked into this Release publish."
        }
    }
}
Remove-Variable bytes -ErrorAction SilentlyContinue

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
# Store policy 10.6.3 denied unvirtualizedResources for this app; never ship it (or the settings that need it) again.
if ($caps -contains 'unvirtualizedResources') { throw 'Staged manifest requests unvirtualizedResources, which the Store denied (policy 10.6.3).' }
if ($check.SelectNodes('//*[local-name()="FileSystemWriteVirtualization" or local-name()="RegistryWriteVirtualization"]').Count -gt 0) {
    throw 'Staged manifest disables file-system/registry write virtualization, which requires unvirtualizedResources.'
}
$allowedCaps = @('runFullTrust')
$extra = $caps | Where-Object { $allowedCaps -notcontains $_ }
if ($extra) { throw "Staged manifest declares capabilities beyond runFullTrust: $($extra -join ', ')." }

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

$msix = Join-Path $outDir "PharmaRemit_${Version}_x64.msix"
Invoke-Step 'Pack MSIX' { & $MakeAppxPath pack /d $layout /p $msix /o }

if ($CertPath) {
    $signtool = Join-Path (Split-Path $MakeAppxPath) 'signtool.exe'
    if (-not (Test-Path $signtool)) { throw "signtool.exe not found next to makeappx.exe ($signtool)." }
    $signArgs = @('sign', '/fd', 'SHA256', '/f', $CertPath)
    if ($CertPassword) { $signArgs += @('/p', $CertPassword) }
    $signArgs += $msix
    Invoke-Step 'Sign MSIX (local sideload only)' { & $signtool @signArgs }
}

$upload = Join-Path $outDir "PharmaRemit_${Version}_x64.msixupload"
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
