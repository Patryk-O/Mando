#requires -Version 5.1
<#
.SYNOPSIS
  Pełna weryfikacja repozytorium: build -> testy jednostkowe -> testy integracyjne -> (opcjonalnie) E2E.

.DESCRIPTION
  Kończy działanie kodem != 0 przy pierwszym nieudanym kroku. Na końcu wypisuje podsumowanie.

.PARAMETER E2E
  Dodatkowo uruchamia scripts\run-e2e.ps1 (Playwright, Edge).

.PARAMETER E2EFilter
  Filtr tytułu testów E2E (przekazywany jako -Filter do run-e2e.ps1).

.PARAMETER E2EPort
  Port aplikacji dla E2E. Domyślnie 5210 (strumień G). Wartość 0 wybiera wolny port z zakresu 5300-5399.

.PARAMETER Mutation
  Dodatkowo uruchamia scripts\mutation.ps1 (Stryker.NET). Wynik poniżej 80% nie przerywa skryptu — próg jest doradczy.

.EXAMPLE
  powershell -File scripts\verify.ps1
  powershell -File scripts\verify.ps1 -E2E
  powershell -File scripts\verify.ps1 -E2E -E2EPort 0
  powershell -File scripts\verify.ps1 -Mutation
#>
[CmdletBinding()]
param(
    [switch]$E2E,
    [string]$E2EFilter,
    [int]$E2EPort = 5210,
    [switch]$Mutation
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# Angielskie komunikaty dotnet, żeby podsumowanie dało się sparsować niezależnie od języka systemu.
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$env:VSLANG = '1033'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$nodeDir = 'C:\Users\MAX\AppData\Local\Microsoft\WinGet\Packages\OpenJS.NodeJS.LTS_Microsoft.Winget.Source_8wekyb3d8bbwe\node-v24.19.0-win-x64'
if (-not (Get-Command node -ErrorAction SilentlyContinue) -and (Test-Path $nodeDir)) {
    $env:PATH = "$nodeDir;$env:PATH"
}

$results = New-Object System.Collections.Generic.List[object]

function Invoke-Step {
    param([string]$Name, [scriptblock]$Action, [string]$Parse = 'none')

    Write-Host ""
    Write-Host "=== $Name ===" -ForegroundColor Cyan
    $started = Get-Date
    $captured = New-Object System.Collections.Generic.List[string]
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $global:LASTEXITCODE = 0
    & $Action 2>&1 | ForEach-Object {
        $line = "$_"
        $captured.Add($line)
        Write-Host $line
    }
    $code = $LASTEXITCODE
    $ErrorActionPreference = $previousPreference
    $elapsed = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)

    $detail = ''
    $text = $captured -join "`n"
    if ($Parse -eq 'test') {
        $summaries = [regex]::Matches($text, '(Passed|Failed)!\s+-\s+Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)')
        if ($summaries.Count -gt 0) {
            $failed = ($summaries | ForEach-Object { [int]$_.Groups[2].Value } | Measure-Object -Sum).Sum
            $passed = ($summaries | ForEach-Object { [int]$_.Groups[3].Value } | Measure-Object -Sum).Sum
            $skipped = ($summaries | ForEach-Object { [int]$_.Groups[4].Value } | Measure-Object -Sum).Sum
            $detail = "zaliczone: $passed, niezaliczone: $failed, pominięte: $skipped"
        }
    }
    elseif ($Parse -eq 'build') {
        $warn = [regex]::Match($text, '(\d+)\s+Warning\(s\)')
        $err = [regex]::Match($text, '(\d+)\s+Error\(s\)')
        if ($warn.Success -and $err.Success) {
            $detail = "błędy: $($err.Groups[1].Value), ostrzeżenia: $($warn.Groups[1].Value)"
        }
    }

    $ok = ($code -eq 0)
    $results.Add([pscustomobject]@{ Step = $Name; Ok = $ok; Seconds = $elapsed; Detail = $detail })
    if (-not $ok) {
        Write-Summary
        Write-Host "PRZERWANO: krok '$Name' zakończył się kodem $code." -ForegroundColor Red
        exit $code
    }
}

function Write-Summary {
    Write-Host ""
    Write-Host "=== Podsumowanie ===" -ForegroundColor Cyan
    foreach ($r in $results) {
        $mark = if ($r.Ok) { 'OK    ' } else { 'BŁĄD  ' }
        $color = if ($r.Ok) { 'Green' } else { 'Red' }
        $line = '{0} {1,-32} {2,6}s  {3}' -f $mark, $r.Step, $r.Seconds, $r.Detail
        Write-Host $line -ForegroundColor $color
    }
}

Invoke-Step 'Build (AnonymousSurvey.sln)' { dotnet build AnonymousSurvey.sln -warnaserror:false } 'build'
Invoke-Step 'Testy jednostkowe' { dotnet test tests\AnonymousSurvey.UnitTests --no-build } 'test'
Invoke-Step 'Testy integracyjne' { dotnet test tests\AnonymousSurvey.IntegrationTests --no-build } 'test'

if ($E2E) {
    $e2eArgs = @{}
    if ($E2EPort -ne 0) { $e2eArgs.Port = $E2EPort }
    if ($E2EFilter) { $e2eArgs.Filter = $E2EFilter }
    Invoke-Step 'Testy E2E (Playwright)' { & (Join-Path $PSScriptRoot 'run-e2e.ps1') @e2eArgs } 'none'
}

if ($Mutation) {
    Invoke-Step 'Mutacje (Stryker, próg doradczy)' { & (Join-Path $PSScriptRoot 'mutation.ps1') } 'none'
}

# Informacyjnie: pozostałe bazy testowe mogą pochodzić z równolegle działających przebiegów innych agentów.
$leftovers = @()
if (Get-Command sqlcmd -ErrorAction SilentlyContinue) {
    $leftovers = @(sqlcmd -S '(localdb)\mssqllocaldb' -h -1 -W -Q "SET NOCOUNT ON; SELECT name FROM sys.databases WHERE name LIKE 'AnonymousSurvey[_]Test[_]%' OR name LIKE 'AnonymousSurvey[_]E2E[_]%'" 2>$null |
        Where-Object { $_ -and $_.Trim() })
}

Write-Summary
if ($leftovers.Count -gt 0) {
    Write-Host ("UWAGA: w LocalDB są bazy testowe ({0}): {1} — mogą należeć do równoległego przebiegu; osierocone starsze niż 1 h usuwa kolejny przebieg testów." -f $leftovers.Count, ($leftovers -join ', ')) -ForegroundColor Yellow
}
else {
    Write-Host "Brak pozostawionych baz testowych (AnonymousSurvey_Test_*, AnonymousSurvey_E2E_*)." -ForegroundColor Green
}

Write-Host ""
Write-Host "WERYFIKACJA ZAKOŃCZONA POMYŚLNIE." -ForegroundColor Green
exit 0
