# Shared by the self-test scripts (sourced). Output stays inside the repo (Logs/ is gitignored),
# not in /tmp, which is a shared RAM disk on the machine this was made on.
#   selftest_out NAME [DIR]   -> prints the output folder (default Logs/selftest/NAME), recreated empty
#   cap_log FILE              -> keeps only the last 5 MB of a log
selftest_out() {
  local d="${2:-$ROOT/Logs/selftest/$1}"
  rm -rf "$d"; mkdir -p "$d"; echo "$d"
}
cap_log() {
  local f="$1" max=$((5 * 1024 * 1024))
  [ -f "$f" ] || return 0
  if [ "$(stat -c %s "$f")" -gt "$max" ]; then tail -c "$max" "$f" > "$f.cap" && mv "$f.cap" "$f"; fi
}
