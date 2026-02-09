#!/usr/bin/env bash
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$ROOT"

echo "[pre-push] unit tests..."
if command -v dotnet >/dev/null 2>&1; then
  dotnet build "src/PzManager.Manager/PzManager.Manager.csproj" -c Release
  dotnet build "src/PzManager.Client.Cli/PzManager.Client.Cli.csproj" -c Release
  dotnet test "tests/PzManager.Domain.Tests/PzManager.Domain.Tests.csproj" -c Release
  dotnet test "tests/PzManager.Application.Tests/PzManager.Application.Tests.csproj" -c Release
  dotnet test "tests/PzManager.Client.Tests/PzManager.Client.Tests.csproj" -c Release
elif command -v docker >/dev/null 2>&1; then
  docker run --rm \
    --user "$(id -u):$(id -g)" \
    -e DOTNET_CLI_HOME=/tmp \
    -e NUGET_PACKAGES=/tmp/nuget-packages \
    -v "$ROOT":/src -w /src \
    mcr.microsoft.com/dotnet/sdk:8.0 bash -lc '
    set -euo pipefail
    dotnet build "src/PzManager.Manager/PzManager.Manager.csproj" -c Release
    dotnet build "src/PzManager.Client.Cli/PzManager.Client.Cli.csproj" -c Release
    dotnet test "tests/PzManager.Domain.Tests/PzManager.Domain.Tests.csproj" -c Release
    dotnet test "tests/PzManager.Application.Tests/PzManager.Application.Tests.csproj" -c Release
    dotnet test "tests/PzManager.Client.Tests/PzManager.Client.Tests.csproj" -c Release
  '
else
  echo "[pre-push] ERROR: dotnet or docker required to run unit tests."
  exit 1
fi

echo "[pre-push] offline acceptance..."
python3 "tools/acceptance/offline_acceptance.py" --manifest "acceptance/manifest.json"

echo "[pre-push] arch check..."
python3 "tools/archcheck/archcheck.py"

if ! command -v codex >/dev/null 2>&1; then
  echo "[pre-push] ERROR: codex CLI not found. Install Codex CLI to run AI acceptance."
  exit 1
fi

echo "[pre-push] AI acceptance (codex, gpt-5.2, low)..."
bash "tools/acceptance/run_codex_acceptance.sh"

echo "[pre-push] OK"
