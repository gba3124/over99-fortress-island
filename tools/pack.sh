#!/bin/bash
# Zips for friends, laid out like the game folder (unzip into "How to Fish/", where How to Fish.exe is):
#   dist/OVER99-<version>-<id>.zip          the mod alone (for someone who already has BepInEx 5)
#   dist/OVER99-<version>-<id>-windows.zip  BepInEx 5 (from this game install) and the mod: one unzip and it's in
# The name carries the build id the game shows in the [I] guide, so everyone can check they match.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build HtfFortress -c Release -p:Deploy=false -nologo -v q >/dev/null
dll=HtfFortress/bin/Release/netstandard2.1/HtfFortress.dll
ver=$(grep -o 'Version = "[^"]*"' HtfFortress/FortressPlugin.cs | cut -d'"' -f2)
name="OVER99-$ver-$(shasum -a 256 "$dll" | cut -c1-6)"
game="$HOME/Library/Application Support/PixelPort/GameData/prefixes/free-steam/drive_c/Program Files (x86)/Steam/steamapps/common/How to Fish/How to Fish"
stage=$(mktemp -d)
mkdir -p "$stage/BepInEx/plugins/HtfFortress/Models" "$stage/BepInEx/plugins/HtfFortress/Icons" dist
cp "$dll" "$stage/BepInEx/plugins/HtfFortress/"
cp HtfFortress/Models/*.obj HtfFortress/Models/*.mtl HtfFortress/Models/*.decor.txt "$stage/BepInEx/plugins/HtfFortress/Models/"
cp $(ls HtfFortress/Icons/*.png | grep -v '/_') "$stage/BepInEx/plugins/HtfFortress/Icons/"
rm -f "dist/$name.zip" "dist/$name-windows.zip"
(cd "$stage" && zip -qr - BepInEx) > "dist/$name.zip"
echo "dist/$name.zip"
# The loader itself: BepInEx's core, its doorstop (version.dll) and that one's settings. No configs, no other plugins.
if [ -f "$game/version.dll" ] && [ -d "$game/BepInEx/core" ]; then
  cp -R "$game/BepInEx/core" "$stage/BepInEx/core"
  cp "$game/version.dll" "$game/doorstop_config.ini" "$stage/"
  [ -f "$game/changelog.txt" ] && cp "$game/changelog.txt" "$stage/BepInEx-changelog.txt"
  (cd "$stage" && zip -qr - BepInEx version.dll doorstop_config.ini $( [ -f BepInEx-changelog.txt ] && echo BepInEx-changelog.txt )) > "dist/$name-windows.zip"
  echo "dist/$name-windows.zip"
fi
rm -rf "$stage"
