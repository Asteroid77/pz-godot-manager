#!/usr/bin/env bash
# NOTE: 推荐用 `bash scripts/demo-infra.sh`（无需给文件加可执行权限 / 无需 sudo）。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "[demo] ERROR: dotnet required (.NET 8 SDK)."
  echo ""
  echo "Install (Ubuntu/Debian):"
  echo "  sudo apt update"
  echo "  sudo apt install -y dotnet-sdk-8.0"
  echo ""
  echo "Or run demo via docker (no dotnet install):"
  echo "  docker run --rm --user \"$(id -u):$(id -g)\" -e DOTNET_CLI_HOME=/tmp -e NUGET_PACKAGES=/tmp/nuget-packages -v \"$ROOT\":/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 bash -lc 'bash scripts/demo-infra.sh'"
  exit 1
fi

if find "$ROOT/src" "$ROOT/tests" "$ROOT/demo-data" -maxdepth 4 -user root -print -quit 2>/dev/null | grep -q .; then
  echo "[demo] ERROR: found root-owned files under this repo (likely from running docker as root)."
  echo ""
  echo "Fix ownership (recommended):"
  echo "  sudo chown -R \"$USER\":\"$USER\" \"$ROOT/src\" \"$ROOT/tests\" \"$ROOT/demo-data\""
  echo ""
  echo "Or cleanup build artifacts (safe to delete):"
  echo "  sudo rm -rf \"$ROOT/src\"/*/bin \"$ROOT/src\"/*/obj \"$ROOT/tests\"/*/bin \"$ROOT/tests\"/*/obj"
  exit 1
fi

echo "[demo] build (Release)..."
dotnet build "./src/PzManager.Manager/PzManager.Manager.csproj" -c Release
dotnet build "./src/PzManager.Client.Cli/PzManager.Client.Cli.csproj" -c Release

BIND="${PZ_MANAGER_BIND:-127.0.0.1}"
PORT="${PZ_MANAGER_PORT:-27100}"
MANAGER_URL="ws://${BIND}:${PORT}/ws"

TS="$(date +%Y%m%d-%H%M%S)"
DEMO_DIR="${DEMO_DATA_DIR:-"$ROOT/demo-data/infra-$TS"}"
SERVER_DATA_DIR="$DEMO_DIR/server"
ADMIN_DATA_DIR="$DEMO_DIR/client-admin"
RO_DATA_DIR="$DEMO_DIR/client-readonly"
LOG_FILE="$DEMO_DIR/manager.log"

mkdir -p "$DEMO_DIR"

run_cli() {
  dotnet run --no-build --project "./src/PzManager.Client.Cli/PzManager.Client.Cli.csproj" -c Release -- "$@"
}

expect_fail() {
  set +e
  "$@"
  local code=$?
  set -e
  if [[ $code -eq 0 ]]; then
    echo "[demo] ERROR: expected failure but command succeeded: $*"
    exit 1
  fi
}

fail_with_manager_log() {
  local msg="$1"
  echo "[demo] ERROR: $msg"
  echo "[demo] last log lines:"
  tail -n 80 "$LOG_FILE" || true
  exit 1
}

echo "[demo] data dir: $DEMO_DIR"
echo "[demo] start manager: $MANAGER_URL"

export PZ_MANAGER_BIND="$BIND"
export PZ_MANAGER_PORT="$PORT"
export PZ_MANAGER_DATA_DIR="$SERVER_DATA_DIR"
export PZ_MANAGER_BOOTSTRAP_TTL_SECONDS="${PZ_MANAGER_BOOTSTRAP_TTL_SECONDS:-1800}"
export PZ_GAME_RUNNER_MODE="none"

dotnet run --no-build --project "./src/PzManager.Manager/PzManager.Manager.csproj" -c Release >"$LOG_FILE" 2>&1 &
MANAGER_PID=$!
trap 'kill "$MANAGER_PID" 2>/dev/null || true' EXIT

echo "[demo] waiting for bootstrap pairing bundle (PZMB1)..."
BOOTSTRAP_BUNDLE=""
for _ in $(seq 1 120); do
  if grep -qiE "address already in use|failed to bind to address" "$LOG_FILE" 2>/dev/null; then
    fail_with_manager_log "manager failed to start (port ${PORT} already in use). stop existing manager or set PZ_MANAGER_PORT to another port."
  fi

  if ! kill -0 "$MANAGER_PID" 2>/dev/null; then
    fail_with_manager_log "manager process exited before bootstrap bundle was ready."
  fi

  BOOTSTRAP_BUNDLE="$(grep -oE 'PZMB1:[A-Za-z0-9_-]+' "$LOG_FILE" | tail -n 1 || true)"
  if [[ -n "$BOOTSTRAP_BUNDLE" ]]; then
    break
  fi
  sleep 0.25
done

if [[ -z "$BOOTSTRAP_BUNDLE" ]]; then
  fail_with_manager_log "bootstrap pairing bundle not found in log: $LOG_FILE"
fi

if ! kill -0 "$MANAGER_PID" 2>/dev/null; then
  fail_with_manager_log "manager process exited after printing bootstrap bundle."
fi

echo "[demo] bootstrap bundle: $BOOTSTRAP_BUNDLE"

echo "[demo] register admin device..."
run_cli --url "$MANAGER_URL" --data-dir "$ADMIN_DATA_DIR" --device-name "demo-admin" --pairing-bundle "$BOOTSTRAP_BUNDLE" auth

echo "[demo] create readonly pairing bundle..."
READONLY_BUNDLE="$(run_cli --url "$MANAGER_URL" --data-dir "$ADMIN_DATA_DIR" pairing create --role readonly --ttl 600 --print-bundle --bundle-url "$MANAGER_URL")"
echo "[demo] readonly bundle: $READONLY_BUNDLE"

echo "[demo] register readonly device..."
run_cli --url "$MANAGER_URL" --data-dir "$RO_DATA_DIR" --device-name "demo-ro" --pairing-bundle "$READONLY_BUNDLE" auth

echo "[demo] expected forbidden checks (readonly cannot devices.list / pairing.create)..."
expect_fail run_cli --url "$MANAGER_URL" --data-dir "$RO_DATA_DIR" devices list
expect_fail run_cli --url "$MANAGER_URL" --data-dir "$RO_DATA_DIR" pairing create --role readonly --ttl 600

echo "[demo] admin devices list..."
DEVICES_JSON="$(run_cli --url "$MANAGER_URL" --data-dir "$ADMIN_DATA_DIR" devices list)"
echo "$DEVICES_JSON"

RO_DEVICE_ID="$(run_cli --url "$MANAGER_URL" --data-dir "$ADMIN_DATA_DIR" devices find --name "demo-ro")"

echo "[demo] admin updates note..."
run_cli --url "$MANAGER_URL" --data-dir "$ADMIN_DATA_DIR" devices update --device-id "$RO_DEVICE_ID" --note "demo-note"

echo "[demo] admin revokes readonly device..."
run_cli --url "$MANAGER_URL" --data-dir "$ADMIN_DATA_DIR" devices revoke --device-id "$RO_DEVICE_ID" --note "demo-revoke"

echo "[demo] revoked device cannot connect (expected unauthorized)..."
expect_fail run_cli --url "$MANAGER_URL" --data-dir "$RO_DATA_DIR" whoami

echo "[demo] OK"
