#!/bin/bash
# Build + copy to the local game for testing, WITHOUT touching the version number or the store files.
# Use this for Owen's own test builds; the public version only changes in tools/release.sh when he says to publish.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build HtfFortress -c Release -nologo -v q
p="$HOME/Library/Application Support/PixelPort/GameData/prefixes/free-steam/drive_c/Program Files (x86)/Steam/steamapps/common/How to Fish/How to Fish/BepInEx/plugins/HtfFortress"
echo "installed: $(shasum -a 256 "$p/HtfFortress.dll" | cut -c1-6)"
