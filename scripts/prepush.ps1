$ErrorActionPreference = "Stop"

$root = (git rev-parse --show-toplevel 2>$null)
if (-not $root) { $root = (Get-Location).Path }
Set-Location $root

Write-Host "[pre-push] offline acceptance..."
python "tools/acceptance/offline_acceptance.py" --manifest "acceptance/manifest.json"

Write-Host "[pre-push] arch check..."
python "tools/archcheck/archcheck.py"

if (-not (Get-Command codex -ErrorAction SilentlyContinue)) {
  throw "[pre-push] ERROR: codex CLI not found. Install Codex CLI to run AI acceptance."
}

Write-Host "[pre-push] AI acceptance (codex, gpt-5.2, low)..."
bash "tools/acceptance/run_codex_acceptance.sh"

Write-Host "[pre-push] OK"

