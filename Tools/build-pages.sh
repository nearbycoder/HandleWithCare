#!/usr/bin/env bash
# Builds the browser version and writes the GitHub Pages site to Builds/Pages (gitignored):
# index.html at its root, .nojekyll, everything with relative URLs so it works under /HandleWithCare/.
#
#   Tools/build-pages.sh            build (Tools/unity.sh build-web, log Logs/web-build.log), then make the site
#   Tools/build-pages.sh --no-build make the site from the existing Builds/WebGL
#
# Then try it as GitHub serves it: Tools/check-pages.mjs --serve Builds/Pages
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WEB="$ROOT/Builds/WebGL"
SITE="$ROOT/Builds/Pages"
if [ "${1:-}" != "--no-build" ]; then
  mkdir -p "$ROOT/Logs"
  # one heavy job: the IL2CPP/Emscripten link and the texture crunch use every core they can get
  if ! LOG="$ROOT/Logs/web-build.log" nice -n 10 "$ROOT/Tools/unity.sh" build-web; then
    echo "web build failed; see Logs/web-build.log" >&2
    grep -E "error|\[HWC\]" "$ROOT/Logs/web-build.log" | tail -20 >&2 || true
    exit 1
  fi
  grep "\[HWC\] WebGL" "$ROOT/Logs/web-build.log" | tail -3   # the DXT build, the ETC2 build and its files
fi
[ -f "$WEB/index.html" ] || { echo "no browser build at $WEB" >&2; exit 1; }
rm -rf "$SITE"
mkdir -p "$SITE"
cp -r "$WEB/." "$SITE/"
touch "$SITE/.nojekyll"
chmod -R a+rX "$SITE"   # Unity writes the compressed files owner-only
# GitHub refuses files over 100 MB (and warns over 50 MB)
huge=$(find "$SITE" -type f -size +95M)
if [ -n "$huge" ]; then echo "files too big for GitHub:" >&2; ls -l $huge >&2; exit 1; fi
big=$(find "$SITE" -type f -size +50M)
if [ -n "$big" ]; then echo "note: over 50 MB (GitHub warns, Pages still serves it): $(basename $big)"; fi
echo "site: $SITE ($(du -sh "$SITE" | cut -f1))"
find "$SITE" -type f -printf '%s\t%P\n' | sort -rn | head -6 | awk -F'\t' '{ printf "  %8.1f MB  %s\n", $1 / 1048576, $2 }'
