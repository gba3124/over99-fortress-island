#!/bin/bash
# The store zips, both from one HtfFortress.dll. The game shows the build as <version>-<first 6 hex of the DLL's SHA-256>
# and warns when a friend's differs, so Thunderstore and Nexus players can only play together on the very same DLL:
# never build twice for one release.
#   tools/release.sh                       build once, then both zips into dist/<version>/
#   tools/release.sh --from <thunderstore.zip, or the folder Safari unzipped it to>
#                                          no build: the Nexus zip from the package already on Thunderstore (its DLL as is)
# Thunderstore: manifest.json, README.md, CHANGELOG.md and icon.png at the root, the mod beside them (the mod manager
# puts it all in BepInEx/plugins/<package>/). Nexus: laid out like the game folder, unzip where How to Fish.exe is.
set -euo pipefail
cd "$(dirname "$0")/.."

ver=$(grep -o 'Version = "[^"]*"' HtfFortress/FortressPlugin.cs | cut -d'"' -f2)
store=$(grep -o '"version_number": *"[^"]*"' thunderstore/manifest.json | cut -d'"' -f4)
log=$(grep -m1 -o '^## [0-9][^ ]*' thunderstore/CHANGELOG.md | cut -c4-)
if [ "$ver" != "$store" ] || [ "$ver" != "$log" ]; then
  echo "Versions differ: FortressPlugin.cs $ver, thunderstore/manifest.json $store, thunderstore/CHANGELOG.md $log" >&2
  exit 1
fi

stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT
mod="$stage/mod" # HtfFortress.dll, Models/ and Icons/: what both zips carry
mkdir -p "$mod"
if [ "${1:-}" = "--from" ]; then
  if [ -d "${2:-}" ]; then cp -R "$2" "$stage/ts"
  elif [ -f "${2:-}" ]; then unzip -q "$2" -d "$stage/ts"
  else echo "Not found: ${2:-} (usage: tools/release.sh --from <thunderstore.zip or its unzipped folder>)" >&2; exit 1; fi
  dll=$(find "$stage/ts" -name HtfFortress.dll | head -1)
  [ -n "$dll" ] || { echo "No HtfFortress.dll in $2" >&2; exit 1; }
  got=$(grep -o '"version_number": *"[^"]*"' "$stage/ts/manifest.json" | cut -d'"' -f4)
  [ "$got" = "$ver" ] || { echo "$2 is $got, this checkout is $ver" >&2; exit 1; }
  src=$(dirname "$dll")
  cp "$dll" "$mod/"
  cp -R "$src/Models" "$src/Icons" "$mod/"
else
  dotnet build HtfFortress -c Release -p:Deploy=false -nologo -v q >/dev/null
  cp HtfFortress/bin/Release/netstandard2.1/HtfFortress.dll "$mod/"
  mkdir -p "$mod/Models" "$mod/Icons"
  cp HtfFortress/Models/*.obj HtfFortress/Models/*.mtl HtfFortress/Models/*.decor.txt "$mod/Models/"
  cp $(ls HtfFortress/Icons/*.png | grep -v '/_') "$mod/Icons/"
fi

build="$ver-$(shasum -a 256 "$mod/HtfFortress.dll" | cut -c1-6)"
out="dist/$ver"
mkdir -p "$out"

if [ "${1:-}" != "--from" ]; then
  mkdir -p "$stage/ts"
  cp -R "$mod/." "$stage/ts/"
  cp thunderstore/manifest.json thunderstore/README.md thunderstore/CHANGELOG.md thunderstore/icon.png "$stage/ts/"
  rm -f "$out/OVER_99_Fortress_Island-$ver.zip"
  (cd "$stage/ts" && zip -qr - .) > "$out/OVER_99_Fortress_Island-$ver.zip"
  echo "Thunderstore: $out/OVER_99_Fortress_Island-$ver.zip"
fi

mkdir -p "$stage/nexus/BepInEx/plugins"
cp -R "$mod" "$stage/nexus/BepInEx/plugins/HtfFortress"
rm -f "$out/OVER99-$build-nexus.zip"
(cd "$stage/nexus" && zip -qr - BepInEx) > "$out/OVER99-$build-nexus.zip"
echo "Nexus:        $out/OVER99-$build-nexus.zip"
echo "In game both show build $build. On Nexus, set the file's version to $ver."
