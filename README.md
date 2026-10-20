# OVER 99 Fortress Island

![OVER 99](img/banner.png)

A co-op fortress mode for *How to Fish*, as a BepInEx 5 mod. Beat the final boss and the sea strikes back with a fortress island and 100 waves of fish in sneakers. Hold the lighthouse with your friends and go OVER 99.

**Get it:** [Thunderstore](https://thunderstore.io/c/how-to-fish/p/gba3124/OVER_99_Fortress_Island/) · **Languages:** English, Traditional Chinese, Simplified Chinese (follows the game) · **Full guide (Traditional Chinese):** [docs/README.zh-TW.md](docs/README.zh-TW.md) · **Design notes:** [DESIGN.md](DESIGN.md)

## What it adds

- A fortress island that rises out of the sea after the final boss, with a lighthouse to defend
- 100 waves of fish in sneakers, in 10 kinds that each have their own trick
- 10 original bosses, one every 10 waves
- Walls, 7 turrets, a spike trap and 2 support towers, with no cap on upgrades
- 17 new weapons and 20 kinds of gear you keep between runs
- A village on the pier with a fish stall, a smithy and a shared vault
- Solo or co-op. Everyone must run the same version; the mod checks and warns you

## Build

You need the .NET SDK, the game with BepInEx 5.4.23.5 installed, and `dotnet build` pointed at your game folder:

```bash
dotnet build HtfFortress -c Release -p:GameDir="/path/to/How to Fish"
```

The default `GameDir` in `HtfFortress.csproj` is a Pixel Port (Mac) install, so on any other setup pass your own. `tools/pack.sh` makes the zips for friends (it also assumes the Pixel Port path).

## Test

```bash
tests/run.sh          # headless checks on the real code, no game window
tests/run.sh sim      # plus economy, turret and gear simulations
```

The headless tests need the game's DLLs next to your install. The in-game scenarios (`tools/autotest.sh <scenario>`) are described in [docs/README.zh-TW.md](docs/README.zh-TW.md).

## Contributing

Bug reports, translations and small fixes are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) first, and open an issue before starting anything big.

## License and credit

[MIT](LICENSE). You may use, modify and redistribute it, but you must keep the copyright notice and the license in every copy. On top of that, please keep visible credit when you publish a fork or a derived mod: **"Based on OVER 99 Fortress Island by gba3124"** with a link to this repo, on your page or in your README.

This is a fan mod, not affiliated with the makers of *How to Fish*. The repo has no game files. You need your own copy of the game to build and run it.
