$ErrorActionPreference = "Stop"

$root = (git rev-parse --show-toplevel 2>$null)
if (-not $root) { $root = (Get-Location).Path }
Set-Location $root

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "[demo] ERROR: dotnet required (.NET 8 SDK)."
}

Write-Host "[demo] build (Release)..."
dotnet build "src/PzManager.Manager/PzManager.Manager.csproj" -c Release
dotnet build "src/PzManager.Client.Cli/PzManager.Client.Cli.csproj" -c Release

$bind = $env:PZ_MANAGER_BIND
if (-not $bind) { $bind = "127.0.0.1" }
$port = $env:PZ_MANAGER_PORT
if (-not $port) { $port = "27100" }

$managerUrl = "ws://$bind`:$port/ws"

$ts = Get-Date -Format "yyyyMMdd-HHmmss"
$demoDir = $env:DEMO_DATA_DIR
if (-not $demoDir) { $demoDir = Join-Path $root "demo-data/infra-$ts" }

$serverDataDir = Join-Path $demoDir "server"
$adminDataDir = Join-Path $demoDir "client-admin"
$roDataDir = Join-Path $demoDir "client-readonly"
$logFile = Join-Path $demoDir "manager.log"

New-Item -ItemType Directory -Force -Path $demoDir | Out-Null

function Run-Cli {
  param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Args,
    [bool]$ExpectSuccess = $true
  )

  $output = & dotnet run --no-build --project "src/PzManager.Client.Cli/PzManager.Client.Cli.csproj" -c Release -- @Args 2>&1
  $code = $LASTEXITCODE

  if ($ExpectSuccess -and $code -ne 0) {
    throw "[demo] ERROR: command failed ($code): dotnet run ... $Args`n$output"
  }

  if (-not $ExpectSuccess -and $code -eq 0) {
    throw "[demo] ERROR: expected failure but succeeded: dotnet run ... $Args"
  }

  return $output
}

Write-Host "[demo] data dir: $demoDir"
Write-Host "[demo] start manager: $managerUrl"

$env:PZ_MANAGER_BIND = $bind
$env:PZ_MANAGER_PORT = $port
$env:PZ_MANAGER_DATA_DIR = $serverDataDir
if (-not $env:PZ_MANAGER_BOOTSTRAP_TTL_SECONDS) { $env:PZ_MANAGER_BOOTSTRAP_TTL_SECONDS = "1800" }
$env:PZ_GAME_RUNNER_MODE = "none"

$proc = Start-Process dotnet -PassThru -NoNewWindow `
  -RedirectStandardOutput $logFile -RedirectStandardError $logFile `
  -ArgumentList @("run","--no-build","--project","src/PzManager.Manager/PzManager.Manager.csproj","-c","Release")

try {
  Write-Host "[demo] waiting for bootstrap pairing bundle (PZMB1)..."
  $bootstrapBundle = $null
  for ($i = 0; $i -lt 240; $i++) {
    if (Test-Path $logFile) {
      $content = Get-Content -Raw -Path $logFile
      $m = [regex]::Match($content, "PZMB1:[A-Za-z0-9_-]+")
      if ($m.Success) {
        $bootstrapBundle = $m.Value
        break
      }
    }
    Start-Sleep -Milliseconds 250
  }

  if (-not $bootstrapBundle) {
    $tail = ""
    if (Test-Path $logFile) { $tail = (Get-Content -Tail 60 -Path $logFile | Out-String) }
    throw "[demo] ERROR: bootstrap bundle not found in log: $logFile`n$tail"
  }

  Write-Host "[demo] bootstrap bundle: $bootstrapBundle"

  Write-Host "[demo] register admin device..."
  Run-Cli --url $managerUrl --data-dir $adminDataDir --device-name "demo-admin" --pairing-bundle $bootstrapBundle auth | Write-Host

  Write-Host "[demo] create readonly pairing bundle..."
  $readonlyBundle = Run-Cli --url $managerUrl --data-dir $adminDataDir pairing create --role readonly --ttl 600 --print-bundle --bundle-url $managerUrl
  Write-Host "[demo] readonly bundle: $readonlyBundle"

  Write-Host "[demo] register readonly device..."
  Run-Cli --url $managerUrl --data-dir $roDataDir --device-name "demo-ro" --pairing-bundle $readonlyBundle auth | Write-Host

  Write-Host "[demo] expected forbidden checks (readonly cannot devices.list / pairing.create)..."
  Run-Cli --url $managerUrl --data-dir $roDataDir devices list -ExpectSuccess:$false | Write-Host
  Run-Cli --url $managerUrl --data-dir $roDataDir pairing create --role readonly --ttl 600 -ExpectSuccess:$false | Write-Host

  Write-Host "[demo] admin devices list..."
  $devicesJson = Run-Cli --url $managerUrl --data-dir $adminDataDir devices list
  Write-Host $devicesJson

  $roDeviceId = (Run-Cli --url $managerUrl --data-dir $adminDataDir devices find --name "demo-ro").Trim()
  if (-not $roDeviceId) { throw "[demo] ERROR: failed to locate readonly deviceId." }

  Write-Host "[demo] admin updates note..."
  Run-Cli --url $managerUrl --data-dir $adminDataDir devices update --device-id $roDeviceId --note "demo-note" | Write-Host

  Write-Host "[demo] admin revokes readonly device..."
  Run-Cli --url $managerUrl --data-dir $adminDataDir devices revoke --device-id $roDeviceId --note "demo-revoke" | Write-Host

  Write-Host "[demo] revoked device cannot connect (expected unauthorized)..."
  Run-Cli --url $managerUrl --data-dir $roDataDir whoami -ExpectSuccess:$false | Write-Host

  Write-Host "[demo] OK"
}
finally {
  try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch {}
}
