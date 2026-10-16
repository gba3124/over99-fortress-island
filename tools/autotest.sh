#!/bin/zsh
# Arm the in-mod auto test (one-shot) and clear old results. Then start the game from Pixel Port.
# Usage: tools/autotest.sh [scenario]   (probe | siege | island ...)
G="$HOME/Library/Application Support/PixelPort/GameData/prefixes/free-steam/drive_c/Program Files (x86)/Steam/steamapps/common/How to Fish/How to Fish"
B="$G/BepInEx"
rm -f "$B/fortress-test-report.txt"
rm -rf "$B/fortress-shots"
echo "${1:-siege}" > "$B/fortress-autotest.txt"
echo "armed: ${1:-siege}"
