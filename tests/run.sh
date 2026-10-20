#!/bin/zsh
# Headless checks on the mod's real code, no game window (needs the game installed for its DLLs).
# Usage: tests/run.sh          pass / fail checks
#        tests/run.sh sim      + economy, turret and gear-collection simulations
#        tests/run.sh variants + the collection simulation under other drop / crafting numbers
set -e
DIR="${0:A:h}"
dotnet build "$DIR/../HtfFortress" -c Release -p:Deploy=false -v q -nologo | grep -E " error |rror\(s\)" || true
dotnet build "$DIR/Headless" -c Release -v q -nologo | grep -E " error |rror\(s\)" || true
dotnet "$DIR/Headless/bin/Release/net10.0/Headless.dll" "$@"
