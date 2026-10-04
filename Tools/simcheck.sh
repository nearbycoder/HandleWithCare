#!/usr/bin/env bash
# Builds and runs the level validator (Tools/SimCheck) with Unity's bundled .NET SDK.
#   Tools/simcheck.sh               validate every delivery
#   Tools/simcheck.sh run 3 naive   simulate one packing and print the timeline
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/Unity/Hub/Editor/6000.6.2f1/Editor/Data/DotNetSdk/dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
"$DOTNET" build "$ROOT/Tools/SimCheck/SimCheck.csproj" -c Release -v q -nologo -clp:NoSummary > /tmp/simcheck-build.log 2>&1 || { cat /tmp/simcheck-build.log; exit 1; }
exec "$DOTNET" "$ROOT/Tools/SimCheck/bin/Release/net8.0/SimCheck.dll" "$@"
