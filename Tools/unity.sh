#!/usr/bin/env bash
# Runs the Unity 6.6 editor against this project.
#
# The editor links against libxml2.so.2 but this machine ships libxml2.so.16, so we point the
# loader at a local copy (Tools/.libs, gitignored; copied from ~/.local/share/ptt-unity-libs).
#
#   Tools/unity.sh                 open the project in the editor (GUI)
#   Tools/unity.sh setup           batch: apply project setup (materials, URP, player, scene)
#   Tools/unity.sh build-linux     batch: Builds/Linux/HandleWithCare.x86_64
#   Tools/unity.sh build-mac       batch: Builds/Mac/HandleWithCare.app (universal, unsigned; Mac Build Support)
#   Tools/unity.sh build-windows   batch: Builds/Windows/HandleWithCare.exe (needs Windows Build Support)
#   Tools/unity.sh build-web       batch: Builds/WebGL (browser build; Tools/build-pages.sh makes the site from it)
#   Tools/unity.sh exec Method     batch: run any static editor method
#   Tools/unity.sh serve           headless resident editor for `unity command` (stop with: Tools/unity.sh stop)
set -euo pipefail
UNITY="${UNITY:-$HOME/Unity/Hub/Editor/6000.6.2f1/Editor/Unity}"
PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [ ! -e "$PROJECT/Tools/.libs/libxml2.so.2" ] && [ -d "$HOME/.local/share/ptt-unity-libs" ]; then
  mkdir -p "$PROJECT/Tools/.libs" && cp -L "$HOME/.local/share/ptt-unity-libs/"* "$PROJECT/Tools/.libs/"
fi
export LD_LIBRARY_PATH="$PROJECT/Tools/.libs${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
LOG="${LOG:-$PROJECT/Logs/batch.log}"
batch() { "$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" -logFile "$LOG" "$@"; }
case "${1:-open}" in
  open) exec "$UNITY" -projectPath "$PROJECT" ;;
  setup) batch -executeMethod HWC.EditorTools.ProjectSetup.Apply ;;
  build-linux) batch -executeMethod HWC.EditorTools.BuildScript.BuildLinux ;;
  build-mac) batch -buildTarget OSXUniversal -executeMethod HWC.EditorTools.BuildScript.BuildMac ;;
  build-windows) batch -buildTarget Win64 -executeMethod HWC.EditorTools.BuildScript.BuildWindows ;;
  build-web) batch -buildTarget WebGL -executeMethod HWC.EditorTools.BuildScript.BuildWebGL ;;
  exec) shift; batch -executeMethod "$@" ;;
  serve) nohup "$UNITY" -batchmode -projectPath "$PROJECT" -logFile "$PROJECT/Logs/serve.log" > /dev/null 2>&1 &
         echo "serving (pid $!), log: Logs/serve.log" ;;
  stop) pkill -f -- "-batchmode -projectPath $PROJECT -logFile $PROJECT/Logs/serve.log" && echo stopped || echo "not running" ;;
  *) echo "usage: $0 [open|setup|build-linux|build-mac|build-windows|build-web|exec Method|serve|stop]" >&2; exit 2 ;;
esac
