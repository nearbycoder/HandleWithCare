#!/usr/bin/env bash
# Builds the Linux player. Uses the resident editor (Tools/unity.sh serve) when it is running,
# otherwise a one-shot batch-mode editor.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if "$ROOT/Tools/uc" console_status > /dev/null 2>&1; then
  "$ROOT/Tools/uc" build -- --target StandaloneLinux64 --outputPath Builds/Linux/HandleWithCare.x86_64 --confirm true > /dev/null
  for i in $(seq 1 120); do
    sleep 5
    st=$("$ROOT/Tools/uc" build_status 2>&1 | tail -1 | grep -o '"status":"[a-z]*"' | head -1)
    case "$st" in *completed*) break;; esac
  done
  "$ROOT/Tools/uc" build_status 2>&1 | tail -1 | grep -o '"result":"[A-Za-z]*"\|"totalErrors":[0-9]*' | tr '\n' ' '; echo
else
  "$ROOT/Tools/unity.sh" build-linux
fi
