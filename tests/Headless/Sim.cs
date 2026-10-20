using System;
using System.Collections.Generic;
using System.Linq;

namespace HtfFortress.Headless
{
    // Balance read-outs built on the mod's real wave plans, rewards, drop rolls and catalog. Numbers to judge by, not pass / fail.
    public static class Sim
    {
        public static void All()
        {
            Economy(1);
            Economy(3);
            Turrets();
            DpsTable();
            Collection();
            ModPrices();
        }

        private static float Mean(Func<float> f, int n = 400) { float s = 0f; for (int i = 0; i < n; i++) s += f(); return s / n; }

        // What a wave weighs: fish, total health (minis from splitters included), and the coins it pays.
        public struct WaveSheet { public float Fish, Hp, Kills, Bodies, Bonus, BossHp; }

        public static WaveSheet Sheet(int w, int players)
        {
            var s = new WaveSheet();
            int n = 400;
            for (int i = 0; i < n; i++)
            {
                var (plan, extras, hpMult) = Tests.Plan(w, players);
                foreach (var (kind, elite) in plan)
                {
                    var tag = new EnemyTag { Kind = kind, Elite = elite };
                    float hp = Enemies.StatsOf(tag).BaseHp * hpMult;
                    int r = FortressPlugin.KillReward(tag, w);
                    s.Fish += 1; s.Hp += hp; s.Kills += r; s.Bodies += FortressPlugin.CorpseValue(r);
                    if (EnemyCatalog.IsBoss(kind)) s.BossHp += hp;
                    if (kind == EnemyKind.Splitter)
                    {
                        var mini = new EnemyTag { Kind = EnemyKind.Mini };
                        int mr = FortressPlugin.KillReward(mini, w);
                        s.Fish += 3; s.Hp += 3 * EnemyCatalog.Of(EnemyKind.Mini).BaseHp * hpMult; s.Kills += 3 * mr; s.Bodies += 3 * FortressPlugin.CorpseValue(mr);
                    }
                }
            }
            s.Fish /= n; s.Hp /= n; s.Kills /= n; s.Bodies /= n; s.BossHp /= n;
            s.Bonus = FortressPlugin.WaveBonus(w);
            return s;
        }

        // Coins in versus health to burn, wave by wave. "DPS for 60 s" = the turret damage per second it takes to kill the wave in a minute.
        private static void Economy(int players)
        {
            Console.WriteLine($"\n== economy, {players} player(s), in the game's dollars (x{Siege.MoneyScale} per coin): no start money; bodies counted as fully collected (column 'net')");
            // 'buys' = the turret DPS that much money could buy at Lv3-Lv5 prices (about 0.1 DPS a dollar), if a team spent half
            // of everything it ever earned on damage; where 'DPS for 60s' overtakes it, an ordinary team stalls.
            Console.WriteLine(" wave  fish   total hp  boss hp | kills  bodies(net)  bonus | $ so far     | DPS for 60s |  buys");
            float bank = 0f;
            for (int w = 1; w <= EnemyCatalog.FinalWave; w++)
            {
                var s = Sheet(w, players);
                bank += s.Kills + s.Bodies + s.Bonus;
                if (w <= 10 || w % 10 == 0 || w % 10 == 5)
                    Console.WriteLine($" {w,4} {s.Fish,5:0.0} {s.Hp,10:0} {s.BossHp,8:0} | {s.Kills,5:0} {s.Bodies,11:0} {s.Bonus,6:0} | {bank,12:0} | {s.Hp / 60f,11:0} | {bank * 0.5f * 0.1f,5:0}");
            }
        }

        // Damage per second bought per coin, every turret and level (build + upgrades, full price).
        private static void Turrets()
        {
            Console.WriteLine("\n== turrets: single-target damage per second, and per coin spent on it");
            Console.WriteLine(" turret    lv  branch      dps   cost  dps/coin  range");
            foreach (PieceType t in Enum.GetValues(typeof(PieceType)))
            {
                if (!Catalog.Shoots(t)) continue;
                int cost = Catalog.BuildCost(t);
                for (int l = 1; l <= 10; l++) // Lv1-3, then the endless levels (Lv5 and Lv10 add a barrel: more fish / shells at once)
                {
                    if (l > 1) cost += Catalog.UpgradeCost(t, l - 1);
                    if (l > 3 && l != 5 && l != 10) continue;
                    foreach (int b in l >= Catalog.BranchLevel ? new[] { 1, 2 } : new[] { 0 })
                    {
                        var s = Catalog.Turret(t, l, b);
                        float dps = s.Damage * s.Shots / s.Interval + (t == PieceType.Flame ? s.Burn : 0f);
                        string br = b == 0 ? "" : L10n.T(Catalog.BranchKey(t, b));
                        Console.WriteLine($" {t,-8} {l,3}  {br,-15} {dps,6:0} {cost,6} {dps / cost,9:0.00} {s.Range,6:0}");
                    }
                }
            }
        }

        // ---------- what each weapon does a second ----------
        // At wave-1 strength (every fortress weapon grows with the wave's fish health alike, so this is how they stand at any
        // wave), rarity r, every mod line at `lvl`, held on: into one fish, and into a crowd of five within 3 m of where it's
        // aimed (LineHits / AreaHits: how many of them a line or a circle there takes in). Not counted: what control does (held,
        // pushed, +50% / +30% taken, wet, slowed, interrupted), a spitter's x3 to the shark, the hook's flung items, ramp-ups (a
        // gas cloud's stacks, the laser's first 2 s), misses. The game's guns: their kept-up 45 a second (WeaponItems.GunShare)
        // times the rarity; their mods roughly: bullets +8% a level, fire rate diluted by reloading.
        public const float CrowdRadius = 3f, GunKeptUp = 45f;
        public const int CrowdSize = 5;
        public static float LineHits(float w) { double x = Math.Min(1.0, w / CrowdRadius); return (float)(CrowdSize * 2.0 / Math.PI * (Math.Asin(x) + x * Math.Sqrt(1.0 - x * x))); }
        public static float AreaHits(float r) => CrowdSize * Math.Min(1f, r * r / (CrowdRadius * CrowdRadius));
        // How many of the crowd a strip `w` either side of a line `d` off its middle takes in.
        public static float StripHits(float w, float d)
        {
            double R = CrowdRadius;
            double F(double y) { y = Math.Max(-R, Math.Min(R, y)); return y * Math.Sqrt(R * R - y * y) + R * R * Math.Asin(y / R); }
            return (float)(CrowdSize * (F(d + w) - F(d - w)) / (Math.PI * R * R));
        }
        // A shot `deg` degrees off the aim, `at` metres out: how far off the aim it passes there.
        public static float Off(float deg, float at) => at * (float)Math.Tan(deg * Math.PI / 180.0);
        // How much of a circle of radius r the same circle `d` off its middle still covers (the crowd's share a side blast takes in).
        public static float Lens(float r, float d)
        {
            if (d >= 2f * r) return 0f;
            double a = 2.0 * r * r * Math.Acos(d / (2.0 * r)) - d / 2.0 * Math.Sqrt(4.0 * r * r - d * d);
            return (float)(a / (Math.PI * r * r));
        }

        public enum Role { Single, Area, Control, Gun, Special }
        public static Role RoleOf(Gear g) =>
            GearCatalog.IsGameGun(g) ? Role.Gun : g == Gear.Absorber ? Role.Special
          : g == Gear.Laser || g == Gear.LobsterClaw || g == Gear.Gatling ? Role.Single
          : g == Gear.Rocket || g == Gear.PufferBomb || g == Gear.PoisonGas || g == Gear.JellyGun || g == Gear.StarThrower || g == Gear.Boomerang ? Role.Area : Role.Control;

        public static (float one, float crowd) Dps(Gear g, Rarity r, int lvl)
        {
            float v = GearCatalog.Value(g, r) * WeaponMods.PowerMult(lvl, g), cd = Math.Max(0.01f, GearCatalog.Cooldown(g, r) * WeaponMods.TimeMult(lvl));
            float pour = 1f / WeaponMods.TimeMult(lvl), t = WeaponMods.TraitAdd(g, lvl);
            if (WeaponMods.Lingers(g)) v *= pour; // their fire rate: they sting faster
            bool legend = r == Rarity.Legendary;
            switch (g)
            {
                case Gear.Laser:
                {
                    // Held: bursts from 30% heat to overheating and the lock between, the ramp staying up (it only starts over when let go).
                    float burst = 0.7f * Weapons.LaserHeatSeconds[(int)r], one = v * (Weapons.LaserMaxRamp + t) * pour * burst / (burst + Weapons.OverheatLock);
                    return (one, legend ? one * LineHits(Weapons.LaserRadius) : one);
                }
                case Gear.PoisonGas: return (v * Weapons.MaxPoisonStacks, v * Weapons.MaxPoisonStacks * AreaHits(Weapons.GasRadius)); // five stacks kept up
                case Gear.Boomerang:
                {
                    // Each of its fan out and back: into one fish 10 m off (those passing within reach of it), into the crowd 12 m off.
                    var fan = Weapons.BoomerangFan(r);
                    float trip = (Weapons.BoomerangLeg * 2f + 0.15f) * WeaponMods.TimeMult(lvl);
                    float one = fan.Count(a => Math.Abs(Off(a, 10f)) <= Weapons.BoomerangHitRadius), crowd = fan.Sum(a => StripHits(Weapons.BoomerangHitRadius, Off(a, 12f)));
                    return (one * 2f * v / trip, crowd * 2f * v / trip);
                }
                case Gear.SharkWhistle:
                {
                    float one = (legend ? 2 : 1) * v / Weapons.SharkBiteEvery * Math.Min(1f, (Weapons.SharkLife[(int)r] + t) / cd);
                    return (one, one);
                }
                case Gear.BubbleGun:
                {
                    float pop = (legend ? Weapons.BubblePop : 0f) + t;
                    return (pop / Math.Max(cd, v), pop * AreaHits(3f) / cd);
                }
                case Gear.Hook: return (v / cd, (legend ? 2 : 1) * v / cd);
                case Gear.Whirlpool:
                {
                    float one = (Weapons.WhirlDps + t) * Math.Min(1f, v / cd) + (legend ? Weapons.WhirlBurst / Math.Max(cd, v) : 0f);
                    return (one, one * AreaHits(Weapons.WhirlRadius));
                }
                case Gear.Conch: return (Weapons.ConchDamage / cd, 4f * Weapons.ConchDamage / cd);
                case Gear.NetLauncher: return (Weapons.NetDamage / cd, AreaHits((Weapons.NetRadius + t) * (legend ? 1.5f : 1f)) * Weapons.NetDamage / cd);
                case Gear.JellyGun:
                {
                    float n = Math.Min(2f, (Weapons.JellyLife + t) / cd), split = legend ? 0.5f * v * 3f / cd : 0f; // a legendary splits into three small ones at the end
                    return (v * n + split, v * n * AreaHits(Weapons.JellyRadius) + 3f * split);
                }
                case Gear.WaterCannon:
                {
                    float burst = 0.7f * (Weapons.WaterTankSeconds[(int)r] + t), one = v * Weapons.WaterHurt * pour * burst / (burst + Weapons.OverheatLock);
                    return (one, one * LineHits(Weapons.WaterRadius));
                }
                case Gear.PufferBomb:
                {
                    // The burst (a legendary's twice) and the poison it leaves: 2 stacks at 4% of the burst a second, 4 s (4 stacks for two).
                    float hit = v * (legend ? 2f : 1f) + 0.04f * v * (legend ? 4f : 2f) * Weapons.PoisonLinger;
                    return (hit / cd, hit * AreaHits(Weapons.PufferRadius + t) / cd);
                }
                case Gear.StarThrower:
                {
                    // StarSpread apart, 8 m out: one fish meets the ones passing within reach of it, the crowd each one's strip (as many as it cuts through).
                    int n = Weapons.StarCount[(int)r], pierce = (legend ? 3 : 2) + (int)Math.Round(t);
                    var angles = Enumerable.Range(0, n).Select(i => (i - (n - 1) * 0.5f) * Weapons.StarSpread).ToList();
                    float one = angles.Count(a => Math.Abs(Off(a, 8f)) <= Weapons.ShotRadius), crowd = angles.Sum(a => Math.Min(pierce, StripHits(Weapons.ShotRadius, Off(a, 8f))));
                    return (one * v / cd, crowd * v / cd);
                }
                case Gear.LobsterClaw: return (v / cd, (legend ? 3 : 1) * 1.25f * v / cd); // half again where it lands, now and then
                case Gear.Rocket:
                {
                    // A legendary's three, RocketSpread apart: at 12 m each blast still takes in the one fish, and as much of the
                    // crowd as its circle covers there.
                    int n = legend ? 3 : 1;
                    float rr = Weapons.RocketRadius + t, side = Lens(rr, 12f * (float)Math.Tan(Weapons.RocketSpread * Math.PI / 180.0));
                    return (n * v / cd, AreaHits(rr) * (1f + (n - 1) * side) * v / cd);
                }
                case Gear.Absorber: { var a = AbsorberDps(r, lvl); return (a.one, a.crowd); }
                case Gear.Gatling:
                {
                    // Held, spun up: its top rate, faster with the fire rate; a legendary's bullets glance on for half.
                    float one = v * Weapons.GatlingShots(1f) * pour;
                    return (one, legend ? one * 1.5f : one);
                }
                default:
                {
                    float one = GunKeptUp * GearCatalog.Value(g, r) * (1f + 0.08f * lvl) * (float)Math.Pow(1f / WeaponMods.TimeMult(lvl), 0.6);
                    return (one, g == Gear.Shotgun ? one * 1.5f : one);
                }
            }
        }

        // A trophy's move a second (Weapons.TrophyMove): one fish, and the crowd. `v`: one absorber shot; `cd`: the move's cooldown.
        public static (float one, float crowd) MoveDps(EnemyKind boss, float v, float cd)
        {
            float hit = v * Weapons.MoveMult(boss);
            switch (boss)
            {
                case EnemyKind.Warden: return (hit / cd, hit / cd);                                    // the first fish ahead
                case EnemyKind.Urchin: return (hit / cd, 2f * hit / cd);                               // twelve needles all round: one or two meet a crowd ahead
                case EnemyKind.Angler: return (hit / cd, AreaHits(5f) * hit / cd);                     // drawn in, then the burst
                case EnemyKind.Steed: return (hit / cd, AreaHits(5f) * hit / cd);                      // the stomp
                case EnemyKind.Stonefish: { float h = hit + 0.04f * hit * 3f * Weapons.PoisonLinger; return (h / cd, LineHits(1.8f) * h / cd); } // spires and three stacks
                case EnemyKind.Skyfish: return (1.5f * hit / cd, AreaHits(3f) * 1.5f * hit / cd);      // six bombs 4 m apart: one or two on a spot
                case EnemyKind.Kraken: return (3f * hit / cd, AreaHits(5f) * 3f * hit / cd);           // a second for 3 s, to what's held
                case EnemyKind.Voxel: return (hit / cd, AreaHits(4f) * hit / cd);                      // what isn't deleted
                case EnemyKind.Koi: return (6f * hit / cd, 4f * 6f * hit / cd);                        // six breaths into the cone
                default: return ((hit + 2f * v) / cd, (LineHits(1.6f) * hit + CrowdSize * 2f * v) / cd); // the beam through, spears on five
            }
        }

        // The absorber at rarity r, mods at `lvl`: the best of what it fires itself (an item shot, a common fortress-made gun
        // inside: $400; a trophy's move) and what it copies (any fortress weapon at the rarer of the two), into fish (x
        // Weapons.AbsorberBoost) or into a boss (as it is).
        public static (float one, float crowd, string oneHow, string crowdHow) AbsorberDps(Rarity r, int lvl, bool boss = false)
        {
            float k = boss ? 1f : Weapons.AbsorberBoost;
            float v = GearCatalog.Value(Gear.Absorber, r) * WeaponMods.PowerMult(lvl), cd = GearCatalog.Cooldown(Gear.Absorber, r) * WeaponMods.TimeMult(lvl);
            float scale = cd / GearCatalog.Cooldown(Gear.Absorber, Rarity.Common); // a move's cooldown at this rarity and fire rate
            var modes = new List<(string how, float one, float crowd)> { ("item shot", v * Weapons.ShotMult(400) / cd, v * Weapons.ShotMult(400) / cd) };
            foreach (var b in EnemyCatalog.BossOrder) { var m = MoveDps(b, v, Weapons.MoveSeconds(b) * scale); modes.Add((b + " move", m.one, m.crowd)); }
            foreach (var g in GearCatalog.WeaponKinds.Where(x => !GearCatalog.IsGameGun(x) && !GearCatalog.IsSpecial(x))) { var d = Dps(g, r, lvl); modes.Add(("a copied " + g, d.one, d.crowd)); }
            var o = modes.OrderByDescending(m => m.one).First();
            var c = modes.OrderByDescending(m => m.crowd).First();
            return (o.one * k, c.crowd * k, o.how, c.how);
        }

        // What the absorber fires itself (an item shot or a move, not a copy), best into one fish and into the crowd, into fish.
        public static (float one, float crowd) AbsorberOwn(Rarity r, int lvl)
        {
            float v = GearCatalog.Value(Gear.Absorber, r) * WeaponMods.PowerMult(lvl), cd = GearCatalog.Cooldown(Gear.Absorber, r) * WeaponMods.TimeMult(lvl);
            float scale = cd / GearCatalog.Cooldown(Gear.Absorber, Rarity.Common), one = v * Weapons.ShotMult(400) / cd, crowd = one;
            foreach (var b in EnemyCatalog.BossOrder) { var m = MoveDps(b, v, Weapons.MoveSeconds(b) * scale); one = Math.Max(one, m.one); crowd = Math.Max(crowd, m.crowd); }
            return (one * Weapons.AbsorberBoost, crowd * Weapons.AbsorberBoost);
        }

        public static void DpsTable()
        {
            Console.WriteLine("\n== weapons: damage a second at wave-1 strength (one fish | a crowd of 5 within 3 m), Lv0 / every mod line Lv5");
            Console.WriteLine(" weapon        role     |   common Lv0      common Lv5  |    legend Lv0      legend Lv5");
            string Cell((float one, float crowd) d) => $"{d.one,6:0} |{d.crowd,6:0}";
            foreach (var g in GearCatalog.WeaponKinds)
                Console.WriteLine($" {g,-13} {RoleOf(g),-8} | {Cell(Dps(g, Rarity.Common, 0))}  {Cell(Dps(g, Rarity.Common, 5))}  | {Cell(Dps(g, Rarity.Legendary, 0))}  {Cell(Dps(g, Rarity.Legendary, 5))}");
            Console.WriteLine(" every rarity, what its role is judged on (single: one fish; area: the crowd; control and guns: one fish), Lv0 -> Lv5");
            foreach (var g in GearCatalog.WeaponKinds.Where(x => x != Gear.Absorber))
                Console.WriteLine($" {g,-13} {RoleOf(g),-8} | " + string.Join("  ", GearCatalog.Rarities.Select(r =>
                {
                    var (a0, b0) = Dps(g, r, 0); var (a5, b5) = Dps(g, r, 5);
                    return RoleOf(g) == Role.Area ? $"{b0,5:0} -> {b5,5:0}" : $"{a0,5:0} -> {a5,5:0}";
                })));
            foreach (var r in GearCatalog.Rarities)
                foreach (int l in new[] { 0, 5 })
                {
                    var a = AbsorberDps(r, l);
                    var own = AbsorberOwn(r, l);
                    Console.WriteLine($"   absorber {r,-9} Lv{l}: one {a.one,6:0} ({a.oneHow}), crowd {a.crowd,6:0} ({a.crowdHow}); its own shots {own.one:0} | {own.crowd:0}");
                }
            // Against a boss: what a solo player's absorber takes off its health (wave-1 units: both grow with the wave alike).
            foreach (int wave in new[] { 50, 100 })
            {
                var boss = EnemyCatalog.BossFor(wave);
                float hp = BossHp(wave);
                var best = GearCatalog.WeaponKinds.Where(x => x != Gear.Absorber).Max(x => Dps(x, Rarity.Legendary, 5).one);
                foreach (var (r, l) in new[] { (Rarity.Common, 0), (Rarity.Legendary, 0), (Rarity.Legendary, 5) })
                {
                    float d = AbsorberDps(r, l, boss: true).one;
                    Console.WriteLine($"   wave {wave} {boss} ({hp:0} health solo, x2 for three): {r} absorber Lv{l} {d:0} a second, {hp / d:0.0} s alone (best other legendary Lv5: {hp / best:0.0} s)");
                }
            }
            // ...and with what a team has by then: a solo player and a trio, each player with the best single-target weapon
            // (not the absorber) at that colour and mod level.
            Console.WriteLine(" bosses: how long they last (wave-1 units; health x1.5 a second player, x2 for three, as in play)");
            foreach (var (wave, r, l) in new[] { (10, Rarity.Common, 0), (20, Rarity.Rare, 1), (30, Rarity.Epic, 2), (50, Rarity.Legendary, 5), (70, Rarity.Legendary, 8), (100, Rarity.Legendary, 12) })
            {
                float d = BestSingle(r, l);
                Console.WriteLine($"   wave {wave,3} {EnemyCatalog.BossFor(wave),-9} {BossHp(wave),7:0} health: {r,-9} Lv{l,-2} {d,5:0} a second -> solo {BossHp(wave) / d,5:0.0} s, three {2f * BossHp(wave) / (3f * d),5:0.0} s");
            }
        }

        // A boss's health solo, in wave-1 units (what FortressPlugin.ServerSpawnBoss gives it, over the wave's own multiplier).
        public static float BossHp(int wave) => EnemyCatalog.Of(EnemyCatalog.BossFor(wave)).BaseHp * FortressPlugin.BossHpMult(wave);
        public static float BestSingle(Rarity r, int lvl) => GearCatalog.WeaponKinds.Where(x => !GearCatalog.IsSpecial(x)).Max(x => Dps(x, r, lvl).one);

        // ---------- collecting gear over many rounds ----------

        // What a team is after: solo 4 fortress + 2 personal + 1 active + 1 weapon; a trio also kits out three players (16 kinds).
        private static readonly Gear[] Solo = { Gear.Gunpowder, Gear.Scope, Gear.FrostCore, Gear.Rebar, Gear.LuckyCharm, Gear.ToolBelt, Gear.Overclock, Gear.Laser };
        private static readonly Gear[] Trio = { Gear.Gunpowder, Gear.Scope, Gear.FrostCore, Gear.Rebar, Gear.SharkArmor, Gear.Gauntlet, Gear.Shockwave,
                                                Gear.BigNet, Gear.FishGloves, Gear.NetCannon, Gear.ToolBelt, Gear.LuckyCharm, Gear.Overclock,
                                                Gear.Laser, Gear.Hook, Gear.SharkWhistle };

        // Gear one round hands out to a team that clears `cleared` waves (and dies in the next one, halfway through it).
        public static List<(Gear, Rarity)> RoundLoot(int cleared, int players)
        {
            var got = new List<(Gear, Rarity)>();
            int roundDrops = 0;
            for (int w = 1; w <= cleared + 1; w++)
            {
                var (plan, _, _) = Tests.Plan(w, players);
                var kills = new List<(EnemyKind kind, bool elite)>(plan);
                foreach (var p in plan) if (p.kind == EnemyKind.Splitter) for (int i = 0; i < 3; i++) kills.Add((EnemyKind.Mini, false));
                if (w == cleared + 1) kills = kills.Take(kills.Count / 2).Where(k => !EnemyCatalog.IsBoss(k.kind)).ToList(); // the wave that ends it
                foreach (var (kind, elite) in kills)
                {
                    if (Shim.Value() >= Loot.DropChance(kind, elite, w, roundDrops, 0f)) continue;
                    got.Add((GearCatalog.RandomGear(), GearCatalog.Roll(w, EnemyCatalog.IsBoss(kind) ? Rarity.Epic : Rarity.Common)));
                    roundDrops++;
                }
                if (w <= cleared && w % 5 == 0) got.AddRange(Loot.ChestRoll(w));
            }
            return got;
        }

        // After each round, through the real vault buttons: merge three of a wanted kind, dismantle the rest (and spare copies
        // once a kind is legendary), rank up the best copy of each wanted kind, enhance legendaries (a wanted weapon: player 1's
        // mods on it, once it's legendary). Returns how many wanted kinds are legendary.
        private static int Tidy(VaultData v, Gear[] wanted)
        {
            bool did = true;
            while (did)
            {
                did = false;
                foreach (var kv in v.Owned.ToList())
                {
                    var (g, r) = kv.Key;
                    if (!wanted.Contains(g)) { while (v.Apply(VaultAction.Salvage, g, r, 0, 1)) did = true; continue; }
                    if (r < Rarity.Legendary && v.Count(g, r) >= 3 && v.Apply(VaultAction.Merge, g, r, 0, 1)) did = true;
                }
                foreach (var g in wanted)
                    if (v.Count(g, Rarity.Legendary) > 0)
                        foreach (Rarity r in new[] { Rarity.Common, Rarity.Rare, Rarity.Epic })
                            while (v.Apply(VaultAction.Salvage, g, r, 0, 1)) did = true;
                foreach (var g in wanted)
                {
                    if (v.Count(g, Rarity.Legendary) > 0) continue;
                    if (v.Count(g, Rarity.Rare) > 0 && v.Apply(VaultAction.Ascend, g, Rarity.Rare, 0, 1)) did = true;
                    else if (v.Count(g, Rarity.Common) > 0 && v.Count(g, Rarity.Rare) == 0 && v.Apply(VaultAction.Ascend, g, Rarity.Common, 0, 1)) did = true;
                }
            }
            foreach (var g in wanted)
            {
                if (!GearCatalog.IsWeapon(g)) while (v.Apply(VaultAction.Enhance, g, Rarity.Legendary, 0, 1)) { }
                else if (v.Count(g, Rarity.Legendary) > 0) for (int t = 0; t < WeaponMods.Tracks; t++) while (v.ModOf(1, g, t) < 5 && v.Apply(VaultAction.Mod, g, Rarity.Legendary, t, 1)) { }
            }
            return wanted.Count(g => v.Count(g, Rarity.Legendary) > 0);
        }

        public static void Collection()
        {
            Console.WriteLine($"\n== collecting (rarity {string.Join("/", GearCatalog.RollFirst)} -> {string.Join("/", GearCatalog.RollLate)}, rank up {string.Join("/", GearCatalog.AscendCosts)}, dismantle {string.Join("/", GearCatalog.SalvageScales)})");
            Console.WriteLine("  team, waves cleared  | gear/round | 1st wanted legendary | all wanted epic+ | all wanted legendary | legendaries after 10 rounds | +5 on everything");
            foreach (int players in new[] { 1, 3 })
                foreach (int depth in new[] { 7, 11, 15, 0 }) // 0 = a team that gets better: 7 waves in round 1, one more each round, up to 15
                {
                    var wanted = players == 1 ? Solo : Trio;
                    const int teams = 600, rounds = 60;
                    var perRound = new List<int>();
                    var firstWanted = new List<int>(); var all = new List<int>(); var epicAll = new List<int>(); var maxed = new List<int>();
                    var legendsAt10 = new List<int>();
                    for (int team = 0; team < teams; team++)
                    {
                        var v = new VaultData();
                        int fw = -1, fa = -1, fe = -1, fm = -1;
                        for (int round = 1; round <= rounds; round++)
                        {
                            int cleared = depth > 0 ? depth : Math.Min(15, 6 + round);
                            var loot = RoundLoot(cleared, players);
                            perRound.Add(loot.Count);
                            foreach (var (g, r) in loot) v.Add(g, r);
                            int legends = Tidy(v, wanted);
                            if (fw < 0 && legends > 0) fw = round;
                            if (fa < 0 && legends == wanted.Length) fa = round;
                            if (fe < 0 && wanted.All(g => v.Count(g, Rarity.Epic) + v.Count(g, Rarity.Legendary) > 0)) fe = round;
                            if (fm < 0 && wanted.All(g => GearCatalog.IsWeapon(g) ? Enumerable.Range(0, WeaponMods.Tracks).All(t => !WeaponMods.Has(g, t) || v.ModOf(1, g, t) >= 5)
                                                                                : v.EnhanceOf(g) >= GearCatalog.MaxEnhance)) fm = round;
                            if (round == 10) legendsAt10.Add(legends);
                        }
                        firstWanted.Add(fw < 0 ? 999 : fw); all.Add(fa < 0 ? 999 : fa); epicAll.Add(fe < 0 ? 999 : fe); maxed.Add(fm < 0 ? 999 : fm);
                    }
                    int Med(List<int> l) { var o = l.OrderBy(x => x).ToList(); return o[o.Count / 2]; }
                    string R(int x) => x >= 999 ? $">{rounds}" : x.ToString();
                    Console.WriteLine($"  {players}p, clears {(depth > 0 ? depth.ToString() : "7→15"),4}     | {perRound.Average(),10:0.0} | {R(Med(firstWanted)),20} | {R(Med(epicAll)),16} | {R(Med(all)),20} | {legendsAt10.Average(),11:0.0} of {wanted.Length,-2}            | {R(Med(maxed)),6}");
                }
        }

        // Scales a line takes from Lv0 to `lvl`.
        public static long ModsTo(int lvl) => Enumerable.Range(0, lvl).Sum(l => (long)WeaponMods.Cost(l));

        // Scales one round brings in if every piece is dismantled: the gear, the bosses' and chests' piles, an elite's now and then.
        public static float RoundScales(int cleared, int players)
        {
            float got = RoundLoot(cleared, players).Sum(x => GearCatalog.SalvageScales[(int)x.Item2]);
            for (int w = 1; w <= cleared; w++)
            {
                var (plan, _, _) = Tests.Plan(w, players);
                got += plan.Count(p => p.elite && !EnemyCatalog.IsBoss(p.kind)) * Loot.EliteScaleChance;
                if (plan.Any(p => EnemyCatalog.IsBoss(p.kind))) got += Loot.BossScales(w);
                if (w % 5 == 0) got += 2 + Math.Min(6, w / 10);
            }
            return got;
        }

        // The weapon mods' prices (no top level) against what rounds bring in.
        public static void ModPrices()
        {
            Console.WriteLine($"\n== weapon mods: no top level, Lv1-5 {string.Join("/", Enumerable.Range(0, 5).Select(WeaponMods.Cost))}, then x{WeaponMods.CostGrowth} a level");
            foreach (int players in new[] { 1, 3 })
                Console.WriteLine($"  scales a round if everything is dismantled, {players}p: " + string.Join(", ", new[] { 15, 30, 50, 100 }.Select(c => $"{c} waves {Mean(() => RoundScales(c, players), 40):0}")));
            float run = Mean(() => RoundScales(30, 1), 40);
            Console.WriteLine("  level  next   one line in all   runs of 30 waves (solo, all dismantled)");
            foreach (int l in new[] { 5, 10, 15, 20, 25, 30, 40, 50 })
                Console.WriteLine($"  Lv{l,-4} {WeaponMods.Cost(l),9:N0} {ModsTo(l),14:N0}   {ModsTo(l) / run,8:0.0}");
        }

        // The same simulation under other drop and crafting numbers (the shipped ones are put back afterwards).
        // Finding (2026-10-17): every knob here either barely slows an improving team or starves a weak one; see DESIGN.md section 10.
        public static void Variants()
        {
            var first = GearCatalog.RollFirst.ToArray(); var late = GearCatalog.RollLate.ToArray(); var asc = GearCatalog.AscendCosts.ToArray();
            float normal = Loot.NormalDrop;
            foreach (var (l, a, nd, what) in new[]
            {
                (late, asc, normal, "shipped"),
                (late, new[] { 5, 20 }, normal, "pricier rank-ups"),
                (new float[] { 45, 33, 16, 6 }, new[] { 5, 20 }, normal, "pricier rank-ups, fewer epics / legendaries deep in a round"),
                (late, asc, 0.04f, "ordinary kills drop 4% instead of 6%"),
            })
            {
                l.CopyTo(GearCatalog.RollLate, 0); a.CopyTo(GearCatalog.AscendCosts, 0); Loot.NormalDrop = nd;
                Console.Write("\n-- " + what);
                Collection();
            }
            late.CopyTo(GearCatalog.RollLate, 0); asc.CopyTo(GearCatalog.AscendCosts, 0); Loot.NormalDrop = normal;
            first.CopyTo(GearCatalog.RollFirst, 0);
        }
    }
}
