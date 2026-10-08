param(
    [string]$MachineId,
    [ValidateSet('OneMonth','OneYear','Lifetime')]
    [string]$Duration = 'OneYear'
)

$ErrorActionPreference = 'Stop'
$privateKeyPath = Join-Path $PSScriptRoot 'tools\licensing\pharmabill-license-private.pem'
if (-not (Test-Path $privateKeyPath)) {
    throw "Private key not found at $privateKeyPath"
}

if (-not $MachineId) {
    $MachineId = Read-Host "Enter Customer Machine ID (e.g. PB-MID-XXXXXXXXXXXX)"
}

$MachineId = $MachineId.Trim().ToUpperInvariant()
$grantDays = switch ($Duration) { 'OneMonth' { 30 } 'Lifetime' { 0 } Default { 365 } }
$expiryToken = if ($Duration -eq 'Lifetime') { 'LIFETIME' } else { [DateTime]::UtcNow.AddDays($grantDays).ToString('yyyyMMdd') }
$payload = "$MachineId|$expiryToken|FULL|$grantDays"

$signerDir = Join-Path $PSScriptRoot 'tools\licensing\SignKey'
New-Item -ItemType Directory -Force -Path $signerDir | Out-Null

$projXml = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>'
Set-Content (Join-Path $signerDir 'SignKey.csproj') $projXml -Encoding UTF8

$progLines = @(
'using System.Security.Cryptography;',
'using System.Text;',
'var pemPath = args[0];',
'var payloadText = args[1];',
'using var rsa = RSA.Create();',
'rsa.ImportFromPem(File.ReadAllText(pemPath));',
'var payload = Encoding.UTF8.GetBytes(payloadText);',
'var sig = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);',
'static string B64Url(byte[] d) => Convert.ToBase64String(d).TrimEnd(''='').Replace(''+'',''-'').Replace(''/'',''_'');',
'Console.WriteLine("PBILL2." + B64Url(payload) + "." + B64Url(sig));'
)
Set-Content (Join-Path $signerDir 'Program.cs') $progLines -Encoding UTF8

$key = & dotnet run --project (Join-Path $signerDir 'SignKey.csproj') -c Release -- $privateKeyPath $payload
if ($LASTEXITCODE -ne 0) { throw 'RSA signing failed.' }

Write-Host ''
Write-Host '==============================================' -ForegroundColor Cyan
Write-Host '  PharmaBill RSA Annual License Key Generated ' -ForegroundColor Cyan
Write-Host '==============================================' -ForegroundColor Cyan
Write-Host "Machine ID  : $MachineId"
Write-Host "Duration    : $Duration"
Write-Host "Expiry      : $expiryToken"
Write-Host "License Key : $key" -ForegroundColor Green
Write-Host '=============================================='
Set-Clipboard -Value $key
Write-Host '(Copied to clipboard)' -ForegroundColor Yellow