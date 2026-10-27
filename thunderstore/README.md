# OVER 99 Fortress Island

![Four co-op players defend the lighthouse while fish in sneakers storm the island](https://raw.githubusercontent.com/gba3124/over99-fortress-island/main/img/promo-story-hero-0.3.0.png)

**The final boss is dead. The sea is furious.**

Build the island. Bring your friends. Outrun 100 waves of fish.

*Key art is cinematic promo art based on the mod's actual models. Feature shots use the real in-game 3D models on AI-enhanced backdrops.*

Beat the final boss and the sea strikes back with a fortress island and 100 waves of fish in sneakers. Raise the walls, pick your turrets, and hold the lighthouse together: then go OVER 99.

## What it adds

- A fortress island that rises out of the sea after the final boss, with a lighthouse to defend
- 100 waves of fish in sneakers, in 10 kinds that each have their own trick
- 10 original bosses, one every 10 waves: and every boss changes form mid-fight
- **9 turrets** with no upgrade cap; at level 3 you choose a branch, at level 9 it splits into two more
- 17 custom weapons, from a tuna boomerang to a shark whistle, each with a Legendary trick
- 20 kinds of gear you keep between runs, 5 of them active gadgets on **K**
- A village on the pier with a fish stall, a smithy and a vault the whole team shares
- Four-player co-op, one shared vault, and a lot of screaming

## The story

![The fortress island rises out of the sea around the lighthouse](https://raw.githubusercontent.com/gba3124/over99-fortress-island/main/img/promo-story-rising-0.3.0.png)

You beat the final boss. The sea did not take it well.

The camera flies out, the water splits, and a fortress island rises around a lighthouse and its pearl. The fish put on their sneakers and come for it. You have a hammer, a hand net, a lighthouse door, and three friends.

Hold the beaches for 100 waves. Every tenth wave a boss arrives: and every boss has a second form. Build, fish, upgrade, scream, repeat. Survive wave 100 and you have gone **OVER 99**.

![A boss fight at the lighthouse: turrets fire, fish swarm and a giant sea boss rises](https://raw.githubusercontent.com/gba3124/over99-fortress-island/main/img/promo-story-boss-0.3.0.png)

## Installation

**With a mod manager (recommended):** install BepInEx 5.4.2305 (the Thunderstore "BepInExPack") in the same profile, then this mod. The manager puts `HtfFortress.dll` together with `Models/` and `Icons/` under `BepInEx/plugins/` and the game loads it at startup. Everyone joining a co-op room must install the **same version** of this mod; a different build is told so and removed from the room.

**By hand:** install BepInEx 5.4.2305 into the game folder (`How to Fish.exe` beside `BepInEx/`), then create `BepInEx/plugins/HtfFortress/` and copy `HtfFortress.dll`, `Models/` and `Icons/` into it. All three must sit together: the mod loads its models from its own folder.

If the mod does not show up, check that `HtfFortress.dll` is directly inside `BepInEx/plugins/` (or one folder under it), not inside a nested `plugins/plugins/`.

## Languages

All 16 languages How to Fish ships with: English, Traditional/Simplified Chinese, French, German, Italian, Spanish (Spain/Mexico), Portuguese (Brazil), Russian, Polish, Turkish, Ukrainian, Japanese, Korean and Swedish. Auto follows your game's locale; the config can override it.

## How it works

![One run](https://raw.githubusercontent.com/gba3124/over99-fortress-island/main/img/loop.png)

1. Beat the final boss and hand in the quest. The camera flies out to sea and you watch the island rise. From then on, an orange dot on your radar shows the way.
2. Walk up to the lighthouse door and press **E**. The fish arrive five seconds later.
3. You get 90 seconds to build before each wave. Press **G**, pick a building with **T** and hold **left click**. Walls block the fish and turrets shoot them.
4. Fish come ashore on three beaches and run for the pearl inside the lighthouse. Kill them, scoop up the bodies with your hand net (**F**) and sell them at the fish stall. That's where most of your money comes from.
5. Every 10th wave brings a boss. Red on the ground shows where it's about to hit, so get out of it. When a gold ring shrinks over its head, everyone should shoot it, because that breaks the move.
6. Gear, weapons and fish scales drop as you go. Forge three into one, upgrade what you like, and keep all of it for the next run.
7. Survive wave 100 and you've gone OVER 99. You get fireworks, your names on the record board and a lighthouse that stays golden for good.

The money is the game's own, shared by the whole team. Fishing pays for your first walls, and the later waves pay better than fishing ever did.

## Troubleshooting

**The mod does not appear in game.**
1. Install BepInEx 5.4.2305 (the Thunderstore "BepInExPack") first; the game must have `BepInEx/` beside `How to Fish.exe`.
2. The mod's three parts must sit together: `BepInEx/plugins/HtfFortress/HtfFortress.dll`, `BepInEx/plugins/HtfFortress/Models/` and `BepInEx/plugins/HtfFortress/Icons/`. If you unzipped a store package, make sure the DLL is not under a nested `plugins/plugins/` folder.
3. Start the game once and check `BepInEx/LogOutput.log`: it should say `HTF Fortress build 0.1.3-...` and BepInEx should list `HtfFortress.dll` as loaded. If the log does not exist, BepInEx itself is not installed or the wrong version (BepInEx 6 will not work).

**"Different Fortress build" and I get removed from a co-op room.**
Everyone in the room must run the exact same version of this mod. Open the [I] panel to read your build (`0.1.3-xxxxxx`) and compare; update the odd ones out and restart the game. A host must also restart after replacing the DLL: BepInEx only loads it at startup.

**The version shown in game is older than the one I installed.**
The game was not restarted, or there is a second copy of `HtfFortress.dll` somewhere under `BepInEx/plugins/`. Delete old folders, install one copy, restart.

**The mod's models are invisible, magenta, or the game crashes on the fortress island.**
`Models/` and `Icons/` must stay next to `HtfFortress.dll` (do not move the DLL alone). Reinstall the whole package, then check the last lines of `BepInEx/LogOutput.log` for the exception that names the missing file.

**Crash or black screen on startup.**
Check `BepInEx/LogOutput.log` (BepInEx's own log) and `Player.log` (`%USERPROFILE%\AppData\LocalLow\Dazed Games\How to Fish\Player.log` on Windows; on Linux/Proton inside the prefix at `drive_c/users/steamuser/AppData/LocalLow/Dazed Games/How to Fish/Player.log`; on macOS Pixel Port it is in the same place inside the Pixel Port prefix). Send the last 30 lines. A crash before the menu is almost always BepInEx 6 instead of 5, or two different copies of the mod.

**Co-op: things stop and jump for everyone, or only one player lags.**
The mod writes a `perf host:` / `perf client:` line to `BepInEx/LogOutput.log` every 10 s with fps, worst frame, wave, enemies, helpers, drops, scales, pieces and `fx/10s`. A long host frame is what every client sees as "everything freezes, then jumps", so include those lines (both sides) when reporting. The release ships a basic host preset: classic turret heads, enemy/prop shadows **off**, corpse cap **160**, debug keys and verbose logging **off**. Only change `Performance/CorpseCap` if a late wave needs more bodies; leave the model settings at their defaults unless a machine is struggling.

**The vault seems empty after a session.**
The team's vault is `BepInEx/fortress-vault/<save name>.txt` on the host; every sell-all leaves a `*-pre-sell-vault.txt` in `BepInEx/fortress-backups/`. Copy the newest backup over the vault file to restore it (with the game closed). Do not delete `fortress-backups/`.

**Mac / Linux.**
The same `BepInEx/plugins/HtfFortress/` layout applies inside the Proton/Pixel Port prefix. The game folder on this setup is under the prefix's `drive_c/Program Files (x86)/Steam/steamapps/common/How to Fish/How to Fish/`; the log is in `BepInEx/LogOutput.log` there.

**The cheat menu does not open.**
Set `Debug/DebugKeys = true` in `BepInEx/config/owen.htf.fortress.cfg` and restart; the menu opens with `Debug/MenuKey` (default ~).

## Meet the fish

![The fish](https://raw.githubusercontent.com/gba3124/over99-fortress-island/main/img/fish.png)

They all run on sneakers, and each kind has its own trick.

| Fish | From wave | What to do |
|---|---|---|
| Runner | 1 | The plain one. Walls stop it and turrets eat it. |
| Charger | 3 | Small, fast and red, with a horned helmet. Don't leave gaps. |
| Thief | 4 | Hops your walls, grabs fish bodies (or your cash) and runs for the sea. Chase it down and kill it to get the loot back. |
| Spitter | 5 | Floats off the beach and spits at buildings and people. |
| Splitter | 5 | Covered in bubbles. When it pops, out come three Minis. |
| Bomber | 6 | Carries a bomb and blows up on walls or people. Kill it before it gets close. |
| Healer | 6 | Heals every fish near it. Kill it first. |
| Shield Bearer | 7 | Its shield blocks most turret fire from the front, so hit it from the side. |
| Tank Crab | 8 | Slow and very tough under its rock shell, and it does double damage to walls. |
| Digger | 9 | Tunnels under your walls, so guard the inside too. |
| Elite | every 5th | Gold, with a crown. Bigger and tougher, and worth triple rewards. |

### The bosses

Every boss has its own moves, a big entrance and a hint card. Beat one and you win its trophy.

| Wave | Boss |
|---|---|
| 10 | Iron-Claw Warden |
| 20 | Thousand-Needle Urchin |
| 30 | Abyssal Lantern |
| 40 | Silver Steed |
| 50 | Venom Rock Emperor |
| 60 | Sky Torpedo Admiral |
| 70 | Crimson Kraken |
| 80 | Shattered Pixel King |
| 90 | Koi of the Dragon Gate |
| 100 | The Eye at the End |

## Weapons and gear

![The toy arsenal: 17 low-poly weapons arranged like a toy collection](https://raw.githubusercontent.com/gba3124/over99-fortress-island/main/img/promo-weapons-0.3.0.png)

Seventeen custom weapons, from a tuna boomerang to a rocket launcher that throws fish like dynamite. They are real items, like the game's own guns. Pick them up with **E**, switch with **1** to **9**, drop with **Q** and fire with **left click**. Each one comes as Common, Rare, Epic or Legendary, and every Legendary has a special trick of its own.

| Weapon | What it does |
|---|---|
| Rocket Launcher | Everyone gets one on their first run. |
| Laser Gun | Gets stronger the longer you keep it on a target. A Legendary at full power turns into a rainbow. |
| Gas Gun, Pufferfish Bomb | Poison clouds. Poisoned fish can't be healed. |
| Tuna Boomerang | Flies through the whole line and comes back. |
| Shark Whistle | Calls a shark out of the sea. |
| Bubble Gun | Traps a fish in a floating bubble, where everything hits it harder. |
| Grapple Gun, Lobster Claw | Grab a fish, pull it in and throw it around. |
| Whirlpool Cannon | Fires a whirlpool that drags nearby fish into the middle. |
| Net Launcher | Shoots a net that holds every fish under it. |
| Jellyfish Gun | Lobs a jellyfish that hangs in the air and stings every fish below. |
| Water Cannon | Hold it to spray fish back. |
| Starfish Thrower | Throws a fan of starfish that cut through fish. |
| Sonic Conch | Blasts fish back and breaks a boss's move. |
| Gatling Gun | On sale once you beat the wave 80 boss. Spins up to 12 shots a second. |
| Absorber | The strongest one. It sucks up anything and fires it back, and if you feed it a boss trophy you get that boss's move. |

You also get fortress versions of the game's own pistol, shotgun, SMG, sniper, rifle and bow.

Gear stays with you between runs. Four team slots power up the turrets, the walls and the lighthouse, and two personal slots hold your armor, a bigger net or a lucky charm. Press **K** to use an active gadget such as the decoy duck, the overclock chip or the shockwave hammer.

## Build your fortress

![Nine chunky turrets on a seaside plaza, with a worried fish for scale](https://raw.githubusercontent.com/gba3124/over99-fortress-island/main/img/promo-turrets-0.3.0.png)

Nine turrets, each with its own silhouette and upgrades. Pick a side at level 3, then split again at level 9: and every turret gets another barrel every five levels.

| Build | What it does |
|---|---|
| Wall, Floor, Ramp | Block the fish and build up high. Walls go from wood to stone to iron. |
| Machine gun | Fast, one target at a time. |
| Cannon | Lobs shells that splash. |
| Harpoon | Pierces a whole line of fish and slows them. |
| Mortar | Very long range. |
| Tesla coil | Chain lightning that does double damage to fish in water. |
| Spike trap | Hurts and slows whatever walks on it. |
| Flamethrower (blueprint) | Close-range fire that keeps burning. |
| Sniper Tower (blueprint) | Very long range, and always aims at the toughest fish. |
| Bubble Shield (blueprint) | Buildings inside its soap bubble take less damage. |
| Repair Tower | Heals buildings around it. |

The three marked (blueprint) can't be built until you find their blueprint. Blueprints come from chests after wave 10 and from every boss.

Upgrades have no cap. At level 3 you pick one of two branches, and a turret gets an extra barrel every 5 levels. Look at a building and press **U** to upgrade it, **J** to repair it or **M** to move it, or hold **H** to tear it down.

### The village on the pier

| Stop | What you do there |
|---|---|
| Fish stall | Sell fish bodies, get a bigger net, and hire helpers who collect everything lying around the island. |
| Smithy | Forge three into one, raise rarity with fish scales, give your weapons permanent mods, and buy weapons, K gadgets and explosives. |
| Vault | Drop anything at the door to store it, including gear, weapons and even the game's own items. The whole team shares it. |
| Lighthouse keeper | Explains how everything works. |

## Keys

| Key | What it does |
|---|---|
| **E** at the lighthouse door | Start a run (press it again during the countdown to cancel) |
| **G** / **T** / hold **left click** | Build mode / next building / build it |
| **U** / **J** / **M** / hold **H** | Upgrade / repair / move / tear down (just look at the building) |
| **F** | Hand net: scoop up fish bodies to sell at the stall |
| **K** / **Shift+K** | Use your active gadget / switch to another one |
| **N** (host) | Start the next wave early |
| **I** | The in-game guide, with Settings on its last page |
| hold **O** | Push yourself out if you get stuck between your own buildings |
| **F4** | Go back to the lighthouse at any time |
| hold **Space** | Skip a boss entrance |

## Playing with friends

![A boss fight at the lighthouse: turrets fire, fish swarm and a giant sea boss rises](https://raw.githubusercontent.com/gba3124/over99-fortress-island/main/img/promo-story-boss-0.3.0.png)

**Everyone in the room needs this mod, on the same version.** If someone's version is different, they get a warning banner when they join. You can find the version at the bottom of the **I** guide.

The host's game runs the fish, the money and the loot, and the shared vault is saved on the host's computer. If you go down during a run, a teammate has to pick you up (**E**) and carry you back. When everyone is down, the run is over.

## Settings

Press **I** and go to the last page for damage numbers, teammate markers and lite effects (turn those on if the game lags).

The default config is the host-friendly preset. Only a few options are meant for normal play: `General/Language`, `Performance/CorpseCap`, and `Island/Unlock = Always` if you want the island without beating the final boss first. Advanced wave/economy/damage settings are there for testing, not required for hosting.

## About your save

Fortress weapons are real items in your save, so **if you remove the mod, the game can't read them anymore**. Back up your save first, or play the mod on a separate one.

The vault is saved together with the game, in `BepInEx/fortress-vault/`. Copies of your save and the vault from the last 5 sessions go to `BepInEx/fortress-backups/`. If you switch from a manual install to a mod manager, copy those two folders into your profile's `BepInEx` folder, or your vault will look empty the next time you host.

Made by gba3124 and friends. See you at wave 100.
