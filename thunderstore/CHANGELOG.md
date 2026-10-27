# Changelog

## 0.3.1

**Store page / story update**
- New cinematic key art and story images based on the actual fortress island, lighthouse, turrets, weapons and villagers.
- The store page now tells the core story: the final boss is dead, the sea is furious, a fortress island rises, and 100 waves of fish in sneakers come for the lighthouse pearl. Every tenth wave a boss returns with a second form; survive wave 100 to go OVER 99.
- No gameplay or save-format changes.

## 0.3.0

**Art passes**
- All nine turrets rebuilt for the low-poly game: Gun, Cannon, Harpoon, Mortar, Tesla, Flame, Sniper, Bubble and Mender each have a distinct silhouette, a proper base and four independent branch looks (A1/A2/B1/B2). The turret panel now shows a look card, detail panel, branch cards and build bar.
- All 17 custom weapons updated: a real shoulder-fired Rocket, the tuna Boomerang kept, a ten-barrel Gatling, mirrored icons and a rarity-aware ground look. Weapon stars now reach other players and survive a reload.
- Scene art pass: a rebuilt old stone gate, a split wreck with a convex hull, layered roads, a pier, signposts and shop emblems; the plaza has a compass rose.
- Helpers got idle life: villagers gather in coffee circles and gulls return to nests when there is no work.
- New FX pass and boss presentation: second-form palettes, procedural boss IK, ten new second-form moves with readable calls, and a sound for every boss move.

**Turrets, weapons and balance**
- Turret branch effects work end-to-end, including second splits at Lv9; menders stack, surge, guard against overheal and can cleanse.
- Magazines and reloads, a machine-gun rattle and a bomber fuse; the rocket throws fish like dynamite.
- The vault holds 60 slots to start (up from 40), with the same +15 per expansion.
- Late-wave fish and bosses hit buildings harder so they keep pace with tower health; bosses move with wind-up and follow-through instead of standing about.
- Health tags over damaged turrets, an upgrade line when your upgrade lands, and the boss staircase's ladder health, 4:00 crash, per-run souls and bar-by-bar health.

**Quality of life**
- The test menu is now bound to **~** instead of F12, so Steam can use F12 for screenshots; old F12 configs migrate once.
- The test menu can reset the save's progress; the keeper's permanent tree can be reset for every soul spent between runs.
- Helpers come along when the island moves, the vault keeper accepts thrown items, repair markers sit flat on the ground, the merge-all sound plays, and the legendary cube is gone.
- A measured performance pass: turret, weapon, enemy, HUD, boss and network hot paths allocate far less per frame and per shot; details are in [PERF-REPORT.md](https://github.com/gba3124/over99-fortress-island/blob/main/PERF-REPORT.md).

**Fixes**
- Fixed boss second forms being missed when a burst would skip them; fragments and puppets no longer trigger the boss transformation.
- Fixed the turret panel HP bar, the active-item strip being clipped off 16:10 screens, and several F4/teleport and stuck-in-geometry cases.

## 0.2.9

**Gear stars**

- Every rarity now has **0–3 stars**: 3 × same gear/rarity/star forge the next star; 3 × 3★ forge the next rarity at 0★. Legendary 3★ is the cap. New drops are 0★; old saves load as 0★.
- Each star is **+8%** effect (3★ = +24%), with +50%/star dismantling scales and +25%/star weapon sale value.
- The vault stores stars as substacks inside one slot per (gear, rarity): base capacity **40**, each expansion **+15**, up to **190**. The tile shows `—/★/★★/★★★`; the card shows the full star breakdown and next merge; the smithy has 0★–3★ buttons, merge chains, and star-aware merge/salvage/take/deposit/equip.

**Souls**

- Souls are now paid for **every new 10-wave record from wave 10** (5, 10 … 45), with the big **100** at wave 100; a full new 100-wave record pays 325, and a 76→100 run pays 185.
- Soul damage/crit now also applies to the player's own guns, knife and fists, not only fortress weapons and turrets; the settlement lists each soul milestone.
- Soul payouts are still **record-only**; practice/test rounds and repeated equal records pay nothing.

**Helpers**

- Helpers now take a **local KNN batch**: nearest reachable job, then up to a basketful of nearby jobs (6 m), instead of walking to the oldest item across the island. Other helpers skip claimed work.
- Decisions stay at 2–4 Hz, but movement, pickup, and selling run **every frame**; a dense pile can be collected at walking speed rather than 2–4 items a second. Helpers no longer re-scan every fish for every item.

**Languages**

- All **16 How to Fish locales** are supported, with Auto following the game: English, Traditional/Simplified Chinese, French, German, Italian, Spanish (Spain/Mexico), Portuguese (Brazil), Russian, Polish, Turkish, Ukrainian, Japanese, Korean, Swedish. Non-Chinese/English locales are machine-translated first drafts.

**Chests and loot**

- Chests **accumulate** and never auto-open; each is opened with E. Later-wave chests upgrade their material: wood → iron → brass → cyan-glow steel → white/gold at 80+, with trim and a glowing orb.
- Chest drop models now sit on the ground instead of half-buried; rarity gems were rebuilt as a closed octahedron so they no longer show as broken halves.

**Settlement and UI**

- The results panel rolls each figure in sequence with coin/soul/scale sounds; three top cards show **souls / money / fish scales** directly, then the itemised settlement.
- Test/practice victories now say **「練習／測試模式・這局不計紀錄」** instead of pretending to be a first clear.
- Fixed the turret panel HP bar running under its number; moved the active-item strip **inside** the right panel (it was clipped off 16:10 screens); moved 「一鍵全賣」 to its own row with a hover total.
- **F4** now checks for solid ground and searches a small ring before teleporting; falling into the sea near the island is rescued automatically.

**Difficulty and combat**

- Late-game enemy HP now accelerates after wave 30 (wave 70 ≈2.6×, wave 100 ≈4.7× the old curve) and boss health accelerates faster (wave 100 ≈26×), while the first 50 waves stay close to the old pacing. Tower late-upgrade multipliers are back at their original 1.3× damage / 1.35× cost.
- Turret gears are limited to **one per tower kind**; Gunpowder/ApRounds/CoreShield values are lower, so four legendary Gunpowders can no longer one-shot late bosses.
- The repair tower no longer draws a bubble-like ring: it uses four green repair crosses and a floating cross badge, distinct from the Bubble Shield.
- Turrets use the **classic single-piece heads** by default; the modular body/module set is not exposed as a release setting. Enemy and prop shadows are off by default, corpse cap is 160, and debug keys/verbose logging are off — the basic host-friendly preset.

**Art / models**

- Weapon model audit added: [GUN-MODEL-AUDIT.md](GUN-MODEL-AUDIT.md) lists every gun's model, held scale, muzzle, icon and display mismatch, with per-gun redesign briefs. Model work is scheduled separately; this release does not yet replace the gun meshes.

## 0.2.8

- The test-key list in the top-right corner is clickable now: click it (or F12) to open the clickable cheat menu; the list also says so.

## 0.2.7

- Corpse cap 110 → **160**, and it is now `Performance/CorpseCap` (40-400). A wave 100 is about 200 fish, so 110 was throwing away bodies of a wave still being cleared; 160 leaves a whole late wave plus a backlog for the net and helpers, still bounded.

## 0.2.6

Client performance pass:

- Small props (loot, chests, fish scales, gulls, helper baskets) share one material and one swatch texture each kind instead of making a new pair per instance: fewer draw calls and much less memory on every screen.
- Nameplates and health bars only rebuild when their text/colour/health actually changes (they were rewritten every frame), and bars no longer touch their layout when unchanged.
- Helpers and gulls more than 120 m away are no longer drawn (the host still simulates them).

## 0.2.5

Host performance pass (the host's long frames are what every client sees as "everything stops, then jumps"):

- The weapon target list is built ten times a second and shared by every weapon, cloud, jelly, shark and poison tick (it used to be a fresh LINQ list for every shot — thousands of allocations a second on a late wave).
- Turrets pick their targets in plain loops instead of LINQ per shot, and their extra barrels no longer allocate a Where/OrderBy chain per volley.
- Corpses are capped at 110: the oldest fish body vanishes when a late wave would otherwise leave hundreds on the island (physics and rendering on every machine).
- `perf` now also logs corpses, items and turrets.

## 0.2.4

- **Permanent upgrade tree** at the lighthouse keeper, bought with souls and kept forever: damage ×1.15 a level, crit +2% a level (up to 100%), starting money +500 coins a level, fish scales ×1.15 a level, lighthouse health ×1.15 a level. Each level costs 15% more than the last; the keeper screen shows level and price.

## 0.2.3

- Fish scales on the ground are a proper 3D **scallop shell** now (one to three of them in a pile), blue and glowing.
- A soul settlement sends glowing **soul orbs** rising from the lighthouse, so the payout is visible in the world, not only on the results panel.

## 0.2.2

- **Bubble gun**: keep shooting a bubbled fish and the bubble rises higher, its pop timer refreshes, and its burst damage stacks with every shot — one bubble can float a fish up and come down for everything put into it.
- **Shark whistle**: it no longer summons a swimming shark. Where it lands a pool of water opens and a shark leaps out from below to bite what is there (a legendary's two pools); the pool splashes and the shark dives again.

## 0.2.1

- **Hook grapple**: a hook that hits nothing now plants in the wall/ground and yanks the shooter to it (up to 9 m) instead of just missing — a movement tool as well as a pull.
- **Boomerang collects**: the return trip picks up gear drops and scales it passes (into the vault, like a helper would), so it is also a looting run.

## 0.2.0

A first pass at making the weapons play differently from each other.

- **Poison burns**: a rocket or puffer burst in a gas cloud sets it off (a burst in its place, and the cloud is gone).
- **Wet fish freeze longer**: Frost Core's freeze lasts 2.6 s on a soaked fish instead of 1.5 s.
- **Frozen fish shatter**: explosions (rocket, puffer) do 50% more to a frozen fish.
- **Starfish ricochet**: a thrown star bounces off the ground and walls up to three times, still cutting a fish on each leg (great in a corridor).

## 0.1.13

- Bosses think on their feet: instead of stopping dead in front of every turret or short wall, they probe a way around it (up to eight directions, remembered for a moment) and only trample when the way really is walled off. They also turn faster (220°/s, 260° while dodging) and break through faster (0.55 s between stomps).

## 0.1.12

- Rockets now **throw what they hit**: the blast shoves fish away (further out, less of a shove; up to about 6 m in the middle), slows them and plays the explosion. Bosses hold their ground.

## 0.1.11

- Turrets: every level past the branch also fires faster (3% off the interval a level), so any turret upgrade speeds its shooting, on top of the branch and Overclock.
- Weapon card: shows the actual **shots a second** with this player's fire-rate mods, so a rate upgrade can be seen working.

## 0.1.10

- Poison is a volume now: fish flying over a gas cloud stay clean, each stack slows a little, and the damage ticks show their number once a second.
- Spikes hit by the cell **plus the fish's own width** (a tank's body counts), skip flying fish, hit harder in the pause and show a damage number with the game's hit sound.

## 0.1.9

- The results screen has a **souls settlement**: the wave record it beat, every ten-wave step with the souls it paid (100, 115, 132 … ×1.15), the run's total, and the souls you now hold. No new record says so.

## 0.1.8

- Cheat menu: the Ctrl and Shift combos are buttons now (clear the island floor, break a boss's move, ten trophies, jump to the next boss wave), so nothing needs Ctrl. Alt/Option works in place of Ctrl for the hotkeys too.

## 0.1.7

- Player damage no longer rises with the wave: your weapon is worth what it is worth, and growth comes from the smithy's **Power** line (now **×1.15 a level**, and cheaper to climb: each level costs 12% more, was 22%), rarity, better weapon kinds and gear. The enemy health curve was lowered to match (wave 100 is about ×350, was ×2020).
- The weapon card's DPS and the absorber's numbers follow that: no hidden wave multiplier.
- Game guns (the rifle etc.) are fixed at their own damage too; your fortress weapons overtake them with upgrades.

## 0.1.6

- The weapon card shows a **computed DPS** with your mods and this wave (the laser as a range while it ramps, the gatling at full spin), instead of the bare weapon value.
- The big weapon card now collapses to just its bar **3 seconds after you switch weapons**.

## 0.1.5

- Boss trophies: helpers fetch them **last** (only when there is nothing else left), put them in the vault and never sell them; a trophy always fits even when storage is full, and storing one by hand works.
- Bosses always drop **Legendary** gear.

## 0.1.4

- Boss trophies are the vault's relics now: helpers leave them on the ground, they always fit even when storage is full, and storing one by hand works. Bosses always drop **Legendary** gear.
- A wave's enemies no longer all appear in one frame: `Waves/SpawnGap` (0.06 s by default) walks them in one at a time.
- Small fixes from the 0.1.3 playtest: test cheat menu (F12), smithy Merge all, gatling barrels, souls at the keeper.

## 0.1.3

- Enemies break in through one wall: the whole horde picks the same breach instead of each fish nibbling at whatever wall it meets, so a fully walled-in fort comes down at one point (and the fish that used to stand about when a path was blocked now walk to that breach).
- Joining a run that is already in progress: the fortress island is placed at the host's island instead of the world origin (one player used to land on a copy of the island a kilometre away, and F4 took them there too), and late joiners are brought to the island with the rest of the team.
- Fish scales on the HUD no longer overlap the game's money box: the line sits just above it at any resolution.
- Mend all (Shift+J) now has a 5 s cooldown per player, and pressing it with nothing damaged doesn't use it up.
- Smoother: far fewer allocations per frame on the host and on clients (enemy steering, the round loop, turrets and bosses, the buying/selling helpers' raycasts and queries, HUD text rebuilt 10 times a second instead of every frame) — the stop-the-world GC pauses behind "everything freezes for a moment, then jumps" should be much rarer.
- `BepInEx/LogOutput.log` now logs a `perf host:` / `perf client:` line every 10 s (fps, worst frame, wave, enemies) to help pin down any remaining stutter.
- Vault expansions (team and personal gear slots, storage room) cost fish scales only now: the old "hold N waves" requirement is gone.
- Legendary Frost Core: the 6 % freeze is capped to once per enemy every 4 s, and bosses are only slowed to half speed — a wall of turrets used to keep them frozen solid.
- Everyone in a room needs this exact build (it shows 0.1.3 in game): a mismatched client is now told which two builds disagree and removed after a couple of seconds instead of playing on.
- Boss trophies are the vault's relics: helpers leave them on the ground, they fit even when storage is full, and storing one by hand always works. Bosses now always drop **Legendary** gear.
- Cheat menu (needs `Debug/DebugKeys`, opens with `Debug/MenuKey`, default F12): every test key as a clickable button, plus cheats for souls, scales, random gear and blueprints, and a size control remembered in `Debug/MenuScale`.
- Smithy: one-click **Merge all** (button on the tab row) merges every three of a kind and the chains that creates, instead of one kind at a time.
- Gatling gun: bigger in the hand, 2 s of spin-up (the trait still shortens it), and a honeycomb cylinder of barrels: 10 at common, 20 rare, 30 epic, 40 legendary, fired in a short random order instead of one frame.
- Souls: clearing wave 100, and every ten waves past it on a new record, pays souls (100, then 115, 132, ... ×1.15 each ten waves). The lighthouse keeper (E) opens a small screen for them: the host can skip ten waves for one soul during build time, never past the save's own record.
- Waves arrive one fish at a time instead of all at once: the three bursts are 1.2 s apart (was 3.5) and `Waves/SpawnGap` (default 0.06 s) spaces the fish out, so a big wave no longer instantiates half of itself in one frame. Build time is unchanged at 90 s.
- Fish scales grow with the wave: bosses give 6 at wave 1, 32 at 30, 110 at 50 and about 2000 at 100 (was a flat 36 at the end), and elites and chests scale too.
- A boss crashing into the lighthouse now hurts it by how much of its own health is left: 60% of the lighthouse at full health, 15% when nearly dead (was a flat 35%).
- Vault: the gear tab has 全部／給人／給塔 views (personal + active gear vs the team's fortress/tower buffs), and the loadout headings say 給塔／給人.
- Waves: walkers spread across the whole beach (±20° and 22–29 m from the core, not ±6° and 23.5–26 m) and every new fish is pushed clear of ones already standing there, so a burst no longer spawns several into the same spot and shoves them apart.
- Shadows: the small mod props (loot, chests, baskets, gulls) no longer cast shadows by default (the shadow camera draws casters you cannot see); `Performance/PropShadows = true` turns it back on.
- Network: effects are batched into one message per frame (a late wave produced dozens of tiny broadcasts a second) and the helpers' positions into one message per tick (thirty helpers used to be up to 300 little broadcasts a second). `perf` now also reports `fx N/10s`.
- The vault and stall panels no longer rebuild on every vault sync (that was a redraw twice a second while helpers worked): rebuilds are signature-gated and rate-limited, and a slow one is timed in the log.
- Helpers: the first 15 are villagers; the 16th and up fly as **seagull helpers** — quicker, they pick things up from farther and search the whole island, and fish cannot catch them. The stall can dismiss the newest helper for free (seagulls go first).
- "Sell all" in the vault is the host's alone by default (friends can still sell single items): every sell-all is named in the host log and leaves a pre-sell snapshot in fortress-backups. `Economy/VaultSellAllAnyPlayer` lets the whole team do it.

## 0.1.2

- The Silver Steed (wave 40) has a new body: a silver winged unicorn in armor, with a twisted horn and a golden mane. It gallops, beats its wings when it leaps and rears up on its hind hooves, and its phantom cavalry gallops too.
- The Eye at the End (wave 100) has a new body: a giant eye in an obsidian socket. Its lids open from a red crack, the eyeball follows you, and a corona and a ring of shards turn behind it.
- Boss afterimages and phantoms keep all their colors.
- The game now shows the same version as the store (0.1.2). Everyone in a room needs to update.

## 0.1.1

A clearer store page: a short pitch, what the mod adds and the languages first, then the details. The mod itself is unchanged and still shows 0.1.0-cd4461 in game.

## 0.1.0

First release on Thunderstore, with the fortress island, 100 waves and 10 bosses, the fish in sneakers, the weapons and gear, the village and co-op.
