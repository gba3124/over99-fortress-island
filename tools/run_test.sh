#!/bin/zsh
# Run one auto-test scenario end to end: arm it, start the game, wait for the report, print it.
# Usage: tools/run_test.sh <probe|island|enemies|build|balance> [timeout_seconds]
DIR="${0:A:h}"
B="$HOME/Library/Application Support/PixelPort/GameData/prefixes/free-steam/drive_c/Program Files (x86)/Steam/steamapps/common/How to Fish/How to Fish/BepInEx"
"$DIR/autotest.sh" "${1:-island}" >/dev/null
"$DIR/launch.sh" >/tmp/htf_launch.log 2>&1 &
end=$((SECONDS + ${2:-1200}))
sleep 20
while (( SECONDS < end )); do
  [[ -f "$B/fortress-test-report.txt" ]] && break
  pgrep -f "How to Fish.exe" >/dev/null || break
  sleep 3
done
sleep 4
if [[ -f "$B/fortress-test-report.txt" ]]; then cat "$B/fortress-test-report.txt"; else echo "NO REPORT (game exited or timed out)"; fi
echo "--- shots:"; ls "$B/fortress-shots" 2>/dev/null | tr '\n' ' '; echo
