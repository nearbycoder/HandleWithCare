# Shared by the self-test scripts (sourced). Output stays inside the repo (Logs/ is gitignored),
# not in /tmp, which is a shared RAM disk on the machine this was made on.
#   selftest_out NAME [DIR]   -> prints the output folder (default Logs/selftest/NAME), recreated empty
#   cap_log FILE              -> keeps only the last 5 MB of a log
#   sandbox_config DIR        -> the player's config (Unity prefs, save) goes to DIR/config, not ~/.config
#   check_config              -> fails if the real config folder (or the real Pictures folder's
#                                "Handle With Care", where SAVE GIF and F12 write) changed since sandbox_config
selftest_out() {
  local d="${2:-$ROOT/Logs/selftest/$1}"
  rm -rf "$d"; mkdir -p "$d"; echo "$d"
}
cap_log() {
  local f="$1" max=$((5 * 1024 * 1024))
  [ -f "$f" ] || return 0
  if [ "$(stat -c %s "$f")" -gt "$max" ]; then tail -c "$max" "$f" > "$f.cap" && mv "$f.cap" "$f"; fi
}

# Unity writes its prefs file on every launch, and the game its save, under $XDG_CONFIG_HOME/unity3d.
REAL_CONFIG="${XDG_CONFIG_HOME:-$HOME/.config}/unity3d/Mossbury Parcel Post"
config_sum() {
  if [ -d "$REAL_CONFIG" ]; then (cd "$REAL_CONFIG" && find . -type f -print0 | sort -z | xargs -0 -r sha256sum)
  else echo "absent"; fi
}
REAL_PICS="$HOME/Pictures/Handle With Care"
pics_sum() {
  if [ -d "$REAL_PICS" ]; then (cd "$REAL_PICS" && find . -type f -printf '%p %s %T@\n' | sort); else echo "absent"; fi
}
sandbox_config() {
  REAL_BEFORE="$(config_sum)"
  PICS_BEFORE="$(pics_sum)"
  export XDG_CONFIG_HOME="$1/config"
  mkdir -p "$XDG_CONFIG_HOME"
}
check_config() {
  if [ "$(config_sum)" != "$REAL_BEFORE" ]; then
    echo "FAIL config: the real config folder changed during the run: $REAL_CONFIG"
    return 1
  fi
  if [ "$(pics_sum)" != "$PICS_BEFORE" ]; then
    echo "FAIL config: the real pictures folder changed during the run: $REAL_PICS"
    return 1
  fi
  echo "config: the real folder is unchanged; the player wrote to ${XDG_CONFIG_HOME#$ROOT/}"
}
