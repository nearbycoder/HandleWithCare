#!/usr/bin/env bash
# Runs the built Linux player. XWayland startup hangs on this machine, so prefer native Wayland.
set -euo pipefail
GAME="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/Builds/Linux/HandleWithCare.x86_64"
[ -x "$GAME" ] || { echo "No build yet. Run Tools/unity.sh build-linux first." >&2; exit 1; }
args=(-screen-fullscreen 0 -screen-width 1920 -screen-height 1080)
[ -n "${WAYLAND_DISPLAY:-}" ] && args+=(-force-wayland)
exec "$GAME" "${args[@]}" "$@"
