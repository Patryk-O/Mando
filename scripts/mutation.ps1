#requires -Version 5.1
<#
.SYNOPSIS
  Mutacje Stryker.NET dla SurveyResponseValidator, SlugGenerator i SurveyItemsEditorModel.

.DESCRIPTION
  Przywraca lokalne narzędzie dotnet-stryker (.config/dotnet-tools.json) i uruchamia dwa przebiegi
  (AnonymousSurvey.Application oraz AnonymousSurvey.Web) ze wspólnym stryker-config.json.
  Próg 80% jest doradczy: break = 0, więc wynik poniżej progu nie kończy skryptu błędem.
  Ocalałe mutanty wypisuje reporter cleartext. Raporty HTML trafiają do reports/stryker/ (poza gitem).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$env:VSLANG = '1033'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

Write-Host '=== Przywracanie dotnet-stryker ===' -ForegroundColor Cyan
dotnet tool restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$reportRoot = Join-Path $repoRoot 'reports\stryker'
New-Item -ItemType Directory -Force -Path $reportRoot | Out-Null

$targets = @(
    @{
        Name    = 'Application (SurveyResponseValidator, SlugGenerator)'
        Project = 'src\AnonymousSurvey.Application\AnonymousSurvey.Application.csproj'
        Output  = Join-Path $reportRoot 'application'
    },
    @{
        Name    = 'Web (SurveyItemsEditorModel)'
        Project = 'src\AnonymousSurvey.Web\AnonymousSurvey.Web.csproj'
        Output  = Join-Path $reportRoot 'editor'
    }
)

$exitCode = 0
foreach ($target in $targets) {
    Write-Host ""
    Write-Host "=== Stryker: $($target.Name) ===" -ForegroundColor Cyan
    $log = Join-Path $reportRoot "$([IO.Path]::GetFileName($target.Output)).log"
    $errLog = "$log.err"
    # Start-Process, nie potok: w Windows PowerShell 5.1 LASTEXITCODE po `| Tee-Object` pochodzi z Tee-Object, nie ze Strykera.
    $proc = Start-Process -FilePath 'dotnet' -WorkingDirectory $repoRoot -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $log -RedirectStandardError $errLog `
        -ArgumentList @(
            'tool', 'run', 'dotnet-stryker', '--',
            '--config-file', (Join-Path $repoRoot 'stryker-config.json'),
            '--project', $target.Project,
            '--output', $target.Output
        )
    $code = $proc.ExitCode
    Get-Content -Path $log, $errLog -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
    if ($code -ne 0 -and $exitCode -eq 0) { $exitCode = $code }

    $text = (Get-Content -Path $log, $errLog -Raw -ErrorAction SilentlyContinue) -join "`n"
    $score = [regex]::Match($text, 'mutation score is\s+([0-9]+(?:[.,][0-9]+)?)\s*%')
    $killed = [regex]::Match($text, 'Killed:\s*(\d+)')
    $survived = [regex]::Match($text, 'Survived:\s*(\d+)')
    $scoreText = if ($score.Success) { "$($score.Groups[1].Value)%" } else { 'brak w logu' }
    $killedText = if ($killed.Success) { $killed.Groups[1].Value } else { '?' }
    $survivedText = if ($survived.Success) { $survived.Groups[1].Value } else { '?' }
    Write-Host ("Wynik {0}: score {1}, killed {2}, survived {3}, kod {4}" -f $target.Name, $scoreText, $killedText, $survivedText, $code) -ForegroundColor Yellow
    Write-Host "Log: $log"
}

Write-Host ""
if ($exitCode -eq 0) {
    Write-Host 'Stryker zakończony. Próg 80% jest doradczy (break = 0) — ocalałe mutanty są w logu cleartext powyżej.' -ForegroundColor Green
}
else {
    Write-Host "Stryker zakończył się kodem $exitCode (awaria przebiegu, nie sam wynik procentowy)." -ForegroundColor Red
}

exit $exitCode
