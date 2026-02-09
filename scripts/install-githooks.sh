#!/usr/bin/env bash
set -euo pipefail

git config core.hooksPath .githooks
echo "OK: git hooksPath set to .githooks"

