#!/bin/zsh
# Start How to Fish directly under Pixel Port's Wine with the env Pixel Port uses (DXMT, msync, Steam app id).
# Steam must already be running in the prefix (Pixel Port starts it); this skips Steam's launch queue,
# which ignores requests while it still believes an old session is running.
PP="$HOME/Library/Application Support/PixelPort/GameData"
RT="$PP/runtimes/mgp-wine"
export WINEPREFIX="$PP/prefixes/free-steam"
export WINEMSYNC=1
export WINEDEBUG=-all
export WINELOADER="$RT/bin/wine"
export DYLD_FALLBACK_LIBRARY_PATH="$RT/lib:/usr/local/lib:/usr/lib"
export VK_DRIVER_FILES="$RT/share/vulkan/icd.d/MoltenVK_icd.json"
export MTL_HUD_ENABLED=0
export DXMT_CONFIG_FILE='Z:\Users\owen\Library\Application Support\PixelPort\GameData\dxmt-managed.conf'
export WINEDLLOVERRIDES="d3d11,dxgi,d3d10core=n,b"
export SteamAppId=4001890 SteamGameId=4001890
cd "$WINEPREFIX/drive_c/Program Files (x86)/Steam/steamapps/common/How to Fish/How to Fish" || exit 1
exec "$RT/bin/wine" 'How to Fish.exe' -force-d3d11
