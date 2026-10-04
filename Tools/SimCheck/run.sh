#!/usr/bin/env bash
# Runs the already-built validator (use Tools/simcheck.sh to rebuild).
exec "${DOTNET:-$HOME/Unity/Hub/Editor/6000.6.2f1/Editor/Data/DotNetSdk/dotnet}" "$(dirname "${BASH_SOURCE[0]}")/bin/Release/net8.0/SimCheck.dll" "$@"
