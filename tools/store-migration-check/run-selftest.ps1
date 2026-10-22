<#
.SYNOPSIS
  Proves the migration checker before it is trusted on a real test: a clean upgrade must pass, and five
  injected failures must each be caught. Uses a fake profile under %TEMP%; never touches real data.
.EXAMPLE
  .\tools\store-migration-check\run-selftest.ps1
#>
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) "PharmaRemit-migration-selftest"
$tool = Join-Path $work 'tool'
$sim = Join-Path $work 'sim'

# The self-test build replaces the profile with MIGRATIONCHECK_SELFTEST_PROFILE; the shipped build has no such switch.
dotnet build (Join-Path $here 'checker\MigrationCheck.csproj') -c Release -p:DefineConstants=SELFTEST -o $tool | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Checker self-test build failed.' }
dotnet build (Join-Path $here 'selftest\MigrationSelfTestSim.csproj') -c Release -o $sim | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Simulator build failed.' }

function Reset {
    Remove-Item (Join-Path $work 'selftest-profile'), (Join-Path $tool 'results') -Recurse -Force -ErrorAction SilentlyContinue
    $script:profileDir = Join-Path $work 'selftest-profile\tester'
    $env:MIGRATIONCHECK_SELFTEST_PROFILE = $script:profileDir
    $script:pkg = Join-Path $script:profileDir 'AppData\Local\Packages\S.A.E.R.PharmaBill_jprxtand66ex6\LocalCache\Local\PharmaBill'
    $script:chosen = Join-Path $script:profileDir 'Pharma Remit Data'
}
function Sim { & (Join-Path $sim 'Sim.exe') @args | Out-Null; if ($LASTEXITCODE -ne 0) { throw "Sim $($args[0]) failed." } }
function Check([string]$stage) { & (Join-Path $tool 'PharmaRemitMigrationCheck.exe') $stage | Out-Null; return $LASTEXITCODE }
$results = @()
function Expect([string]$name, [int]$actual, [int[]]$wanted) {
    $ok = $wanted -contains $actual
    $script:results += [pscustomobject]@{ Scenario = $name; Exit = $actual; Result = $(if ($ok) { 'OK' } else { 'WRONG' }) }
}

Reset; Sim seed $pkg
Expect 'clean: before' (Check before) @(0)
Sim upgrade $pkg $chosen
Expect 'clean: after-upgrade' (Check after-upgrade) @(0)
Sim reinstall $pkg $chosen
Expect 'clean: after-reinstall' (Check after-reinstall) @(0)

Reset; Sim seed $pkg; Check before | Out-Null; Sim upgrade $pkg $chosen
Sim tamper $chosen
Expect 'caught: bill lost in the move' (Check after-upgrade) @(1)

Reset; Sim seed $pkg; Check before | Out-Null; Sim upgrade $pkg $chosen
Set-Content (Join-Path $chosen 'data\pharmabill.db.empty-20261022-070000') 'x'
Expect 'caught: database replaced/quarantined' (Check after-upgrade) @(1)

Reset; Sim seed $pkg; Check before | Out-Null; Sim upgrade $pkg $chosen
Sim tamper $pkg
Expect 'caught: original changed after the move' (Check after-upgrade) @(1)

Reset; Sim seed $pkg; Sim rename $pkg 'Real Pharmacy Pvt Ltd'
Expect 'caught: not the test shop (stop)' (Check before) @(2)

Reset; Sim seed $pkg; Check before | Out-Null
Expect 'caught: upgrade never happened' (Check after-upgrade) @(1)

Remove-Item Env:\MIGRATIONCHECK_SELFTEST_PROFILE
$results | Format-Table -AutoSize
if ($results.Result -contains 'WRONG') { throw 'Self-test FAILED: the checker missed or misreported a scenario.' }
Write-Host 'Self-test passed: clean upgrade passes, all five injected failures caught.' -ForegroundColor Green
