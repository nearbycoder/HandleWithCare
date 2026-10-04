#!/usr/bin/env bash
# Tiles images into a grid: Tools/montage.sh OUT.png COLS W H img1 img2 ...
set -euo pipefail
out="$1"; cols="$2"; w="$3"; h="$4"; shift 4
n=$#; args=(); filt=""; layout=""
i=0
for f in "$@"; do
  args+=(-i "$f")
  filt+="[$i:v]scale=${w}:${h}[v$i];"
  c=$((i % cols)); r=$((i / cols))
  layout+="$((c*w))_$((r*h))|"
  i=$((i+1))
done
inputs=""; for ((k=0;k<n;k++)); do inputs+="[v$k]"; done
ffmpeg -y -loglevel error "${args[@]}" -filter_complex "${filt}${inputs}xstack=inputs=$n:layout=${layout%|}:fill=black" "$out"
echo "$out"
