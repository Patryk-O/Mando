#requires -Version 5.1
<#
.SYNOPSIS
  Uruchamia testy E2E (Playwright, Edge) na świeżej bazie i własnej instancji aplikacji.

.DESCRIPTION
  1. wybiera wolny port (domyślnie 5300-5399) i tworzy bazę AnonymousSurvey_E2E_<guid> na LocalDB,
  2. startuje aplikację (dotnet run --no-launch-profile --urls http://localhost:<port>) z tą bazą,
  3. czeka na gotowość (GET /), ustawia BASE_URL i E2E_DB, uruchamia `npx playwright test`,
  4. w finally zabija drzewo procesów aplikacji i usuwa bazę. Kod wyjścia = kod testów.

.PARAMETER Port
  Port aplikacji. Domyślnie pierwszy wolny z zakresu 5300-5399 (losowo).

.PARAMETER Filter
  Filtr tytułów testów Playwright (--grep).

.PARAMETER AppRoot
  Katalog z kodem aplikacji (src\AnonymousSurvey.Web). Domyślnie to repozytorium; przydatne do sprawdzenia skryptu na innej wersji aplikacji.

.PARAMETER StartupTimeoutSeconds
  Maksymalny czas oczekiwania na start aplikacji (z kompilacją).
#>
[CmdletBinding()]
param(
    [int]$Port = 0,
    [string]$Filter,
    [int]$StartupTimeoutSeconds = 180,
    [string]$AppRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$e2eDir = Join-Path $repoRoot 'tests\e2e'
$appRoot = if ($AppRoot) { $AppRoot } else { $repoRoot }
$sqlServer = '(localdb)\mssqllocaldb'

$nodeDir = 'C:\Users\MAX\AppData\Local\Microsoft\WinGet\Packages\OpenJS.NodeJS.LTS_Microsoft.Winget.Source_8wekyb3d8bbwe\node-v24.19.0-win-x64'
if (-not (Get-Command node -ErrorAction SilentlyContinue) -and (Test-Path $nodeDir)) {
    $env:PATH = "$nodeDir;$env:PATH"
}

function Invoke-Sql {
    param([string]$Query, [switch]$IgnoreErrors)
    $output = & sqlcmd -S $sqlServer -b -h -1 -W -Q "SET NOCOUNT ON; $Query" 2>&1
    if ($LASTEXITCODE -ne 0 -and -not $IgnoreErrors) {
        throw "sqlcmd zakończył się kodem ${LASTEXITCODE}: $output"
    }
    return $output
}

function Test-PortFree([int]$Candidate) {
    $addresses = @([System.Net.IPAddress]::Loopback)
    if ([System.Net.Sockets.Socket]::OSSupportsIPv6) { $addresses += [System.Net.IPAddress]::IPv6Loopback }
    foreach ($address in $addresses) {
        $listener = $null
        try {
            $listener = New-Object System.Net.Sockets.TcpListener($address, $Candidate)
            $listener.Start()
        }
        catch {
            return $false
        }
        finally {
            if ($listener) { $listener.Stop() }
        }
    }
    return $true
}

function Find-FreePort {
    $candidates = 5300..5399 | Get-Random -Count 100
    foreach ($candidate in $candidates) {
        if (Test-PortFree $candidate) { return $candidate }
    }
    throw 'Brak wolnego portu w zakresie 5300-5399.'
}

function Remove-Database([string]$Name) {
    Invoke-Sql "IF DB_ID(N'$Name') IS NOT NULL BEGIN ALTER DATABASE [$Name] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Name]; END" -IgnoreErrors | Out-Null
}

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { throw 'Brak sqlcmd w PATH (wymagany do bazy E2E).' }
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'Brak node w PATH.' }

if ($Port -eq 0) {
    $Port = Find-FreePort
}
elseif (-not (Test-PortFree $Port)) {
    throw "Port $Port jest zajęty."
}

$dbName = "AnonymousSurvey_E2E_$([guid]::NewGuid().ToString('N'))"
$baseUrl = "http://localhost:$Port"
$logDir = Join-Path ([IO.Path]::GetTempPath()) "anonymous-survey-e2e-$Port"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$outLog = Join-Path $logDir 'app.out.log'
$errLog = Join-Path $logDir 'app.err.log'

$savedEnv = @{
    ConnectionStrings__SurveyDb = $env:ConnectionStrings__SurveyDb
    ASPNETCORE_ENVIRONMENT      = $env:ASPNETCORE_ENVIRONMENT
    BASE_URL                    = $env:BASE_URL
    E2E_DB                      = $env:E2E_DB
    NO_COLOR                    = $env:NO_COLOR
}

$app = $null
$testExitCode = 1
try {
    # Przy starcie sprzątamy osierocone bazy E2E starsze niż godzina (przerwane przebiegi).
    $stale = Invoke-Sql "SELECT name FROM sys.databases WHERE name LIKE 'AnonymousSurvey[_]E2E[_]%' AND create_date < DATEADD(HOUR, -1, GETDATE())" -IgnoreErrors
    foreach ($name in @($stale | Where-Object { $_ -and $_.Trim() })) {
        Write-Host "Usuwam osieroconą bazę $name"
        Remove-Database $name.Trim()
    }

    Write-Host "Baza E2E: $dbName, adres: $baseUrl" -ForegroundColor Cyan
    Invoke-Sql "CREATE DATABASE [$dbName];" | Out-Null

    $env:ConnectionStrings__SurveyDb = "Server=$sqlServer;Database=$dbName;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:BASE_URL = $baseUrl
    $env:E2E_DB = $dbName

    $app = Start-Process -FilePath 'dotnet' -PassThru -WindowStyle Hidden `
        -WorkingDirectory $appRoot `
        -RedirectStandardOutput $outLog -RedirectStandardError $errLog `
        -ArgumentList @('run', '--project', 'src\AnonymousSurvey.Web', '--no-launch-profile', '--urls', $baseUrl)

    Write-Host "Czekam na aplikację (PID $($app.Id), limit ${StartupTimeoutSeconds}s)..."
    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        if ($app.HasExited) {
            throw "Aplikacja zakończyła się przedwcześnie (kod $($app.ExitCode)). Log: $outLog / $errLog"
        }
        try {
            $response = Invoke-WebRequest -Uri "$baseUrl/" -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $ready) {
        throw "Aplikacja nie odpowiedziała na GET / w ${StartupTimeoutSeconds}s. Log: $outLog / $errLog"
    }
    Write-Host 'Aplikacja gotowa.' -ForegroundColor Green

    Push-Location $e2eDir
    try {
        if (-not (Test-Path 'node_modules\@playwright\test')) {
            Write-Host 'Instaluję zależności npm (npm ci)...'
            & npm ci --no-audit --no-fund
            if ($LASTEXITCODE -ne 0) { throw "npm ci zakończył się kodem $LASTEXITCODE" }
        }

        $playwrightArgs = @('playwright', 'test')
        if ($Filter) { $playwrightArgs += @('--grep', $Filter) }
        # Stderr z Node (np. ostrzeżenie NO_COLOR/FORCE_COLOR) nie może przerywać skryptu przy przekierowaniu 2>&1 u wywołującego.
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $env:NO_COLOR = $null
        & npx @playwrightArgs
        $testExitCode = $LASTEXITCODE
        $ErrorActionPreference = $previousPreference
    }
    finally {
        Pop-Location
    }
}
catch {
    Write-Host "BŁĄD: $($_.Exception.Message)" -ForegroundColor Red
    foreach ($log in @($errLog, $outLog)) {
        if ((Test-Path $log) -and (Get-Item $log).Length -gt 0) {
            Write-Host "--- ostatnie linie $log ---" -ForegroundColor Yellow
            Get-Content $log -Tail 25 | ForEach-Object { Write-Host $_ }
        }
    }
    $testExitCode = 1
}
finally {
    $ErrorActionPreference = 'Continue'
    if ($app) {
        if (-not $app.HasExited) {
            & taskkill.exe /PID $app.Id /T /F 2>&1 | Out-Null
            $app.WaitForExit(15000) | Out-Null
        }
        # `dotnet run` uruchamia aplikację jako proces potomny — upewniamy się, że port jest zwolniony.
        $listeners = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
        foreach ($listener in @($listeners)) {
            Stop-Process -Id $listener.OwningProcess -Force -ErrorAction SilentlyContinue
        }
    }

    Remove-Database $dbName
    $left = Invoke-Sql "SELECT COUNT(*) FROM sys.databases WHERE name = N'$dbName'" -IgnoreErrors
    if ("$left".Trim() -ne '0') {
        Write-Host "OSTRZEŻENIE: nie udało się usunąć bazy $dbName" -ForegroundColor Yellow
    }

    foreach ($key in $savedEnv.Keys) {
        [Environment]::SetEnvironmentVariable($key, $savedEnv[$key], 'Process')
    }

    if ($testExitCode -eq 0) {
        Remove-Item -Recurse -Force $logDir -ErrorAction SilentlyContinue
    }
    else {
        Write-Host "Logi aplikacji: $logDir" -ForegroundColor Yellow
    }
}

exit $testExitCode
