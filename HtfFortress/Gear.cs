using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace HtfFortress
{
    public enum Rarity : byte { Common, Rare, Epic, Legendary }

    // What the quartermaster can add: more team and personal slots, a bigger vault.
    public enum Expansion : byte { TeamSlot, PersonalSlot, Storage }

    // Fortress gear first (team loadout), then personal gear (each player's own slots). New kinds go on the end (byte on the wire).
    public enum Gear : byte
    {
        Gunpowder, Scope, ApRounds, Incendiary, FrostCore, TeslaCoil, Rebar, CoreShield, WarChest,
        SharkArmor, BigNet, FishGloves, Gauntlet, ToolBelt, LuckyCharm,
        Overclock, FieldRepair, Decoy, NetCannon, Shockwave, // active gear: press K, then a cooldown
        Laser, PoisonGas, Boomerang, SharkWhistle, BubbleGun, Hook, Whirlpool, Conch, // fortress weapons: items you carry, mouse to use (Weapons.cs)
        NetLauncher, JellyGun, WaterCannon, PufferBomb, StarThrower, LobsterClaw,       // ...and the second set (everything from Laser on is a weapon)
        Rocket,                                                                       // the rocket launcher everyone starts with
        Pistol, Shotgun, Smg, Sniper, Rifle, Bow,                                     // the game's own guns as fortress weapons: their model and shooting, damage by rarity
        Absorber,                                                                     // special: only the smithy sells it, once unlocked; never dropped (Absorber.cs)
        Gatling,                                                                      // special too: the smithy's once the wave 80 boss is beaten (Weapons.GatlingTick)
    }

    // What every piece of gear does at each rarity. Tune here.
    public static class GearCatalog
    {
        public const int FortressSlots = 4, PersonalSlots = 2, ActiveSlots = 1, WeaponSlots = 0; // to start with; weapons are items you carry (WeaponItems), not vault gear

        // Expansions, priced in fish scales only (Owen: "倉庫格子的限制 只用魚鱗限制就好 不要用關卡 然後就合適的量" --
        // no wave requirement any more, the scales are the gate): team slots 5-8, personal slots 3-4, the vault
        // 30 -> 130 slots (a slot holds up to 50 of one thing). Priced against what dismantling brings in (a 30-wave run
        // drops about 70 pieces; dismantling the spares is roughly 60-100 scales): the first steps come within a run or
        // two, the last team slot is a long-term goal next to legendary enhancing (15-90 a level). The second number stays
        // in the tuple (the format the vault, the shop and the tests share) but is 0 = no wave needed.
        public static readonly (int scales, int best)[] TeamSlotPrices = { (40, 0), (100, 0), (200, 0), (350, 0), (500, 0), (700, 0) };
        public static readonly (int scales, int best)[] PersonalSlotPrices = { (60, 0), (180, 0) };
        public static readonly (int scales, int best)[] StoragePrices =
            { (20, 0), (30, 0), (45, 0), (60, 0), (80, 0), (100, 0), (125, 0), (150, 0), (180, 0), (220, 0) };
        public const int StorageBase = 40, StorageStep = 15, Stack = 50;

        public static (int scales, int best)[] PricesOf(Expansion e) =>
            e == Expansion.TeamSlot ? TeamSlotPrices : e == Expansion.PersonalSlot ? PersonalSlotPrices : StoragePrices;
        // The next step's price, or null when it's all bought.
        public static (int scales, int best)? NextPrice(Expansion e, int bought) => bought < PricesOf(e).Length ? PricesOf(e)[bought] : ((int, int)?)null;
        public static readonly Gear[] All = (Gear[])Enum.GetValues(typeof(Gear));
        public static readonly Rarity[] Rarities = (Rarity[])Enum.GetValues(typeof(Rarity));
        public const int MaxStars = 3;
        public static bool IsFortress(Gear g) => g <= Gear.WarChest;
        public static bool IsActive(Gear g) => g >= Gear.Overclock && g <= Gear.Shockwave;
        public static bool IsWeapon(Gear g) => g >= Gear.Laser;
        // Retired equipment: there is no hand net to hold any more, so the net's personal/active gear has no job.
        // The enum members stay so old vault files still parse; they are just never dropped, bought or shown.
        public static bool Retired(Gear g) => g == Gear.BigNet || g == Gear.NetCannon;
        public static bool IsGameGun(Gear g) => g >= Gear.Pistol && g <= Gear.Bow;
        public static bool IsSpecial(Gear g) => g >= Gear.Absorber;
        public static readonly Gear[] WeaponKinds = All.Where(IsWeapon).ToArray();
        public static readonly Gear[] Droppable = WeaponKinds.Where(g => !IsSpecial(g)).ToArray(); // what fish can drop
        // Which row a piece is worn in: 0 fortress (team), 1 personal passive, 2 active (K), 3 weapon (L). Also the vault's tabs.
        public static int SlotKind(Gear g) => IsFortress(g) ? 0 : IsActive(g) ? 2 : IsWeapon(g) ? 3 : 1;
        public static int SlotsOf(int kind) => kind == 0 ? FortressSlots : kind == 1 ? PersonalSlots : kind == 2 ? ActiveSlots : WeaponSlots;

        // Common / Rare / Epic / Legendary.
        private static readonly float[][] Values =
        {
            new[] { 0.05f, 0.10f, 0.16f, 0.25f }, // Gunpowder: turret damage +%
            new[] { 0.08f, 0.15f, 0.22f, 0.30f }, // Scope: turret range +%
            new[] { 0.10f, 0.20f, 0.35f, 0.50f }, // ApRounds: vs tank / boss +%
            new[] { 2f, 4f, 7f, 12f },            // Incendiary: burn damage per second, 3 s
            new[] { 0.10f, 0.15f, 0.20f, 0.30f }, // FrostCore: every turret hit slows
            new[] { 1f, 2f, 3f, 4f },             // TeslaCoil: extra chain targets
            new[] { 0.15f, 0.30f, 0.50f, 0.80f }, // Rebar: wall health +%
            new[] { 0.10f, 0.20f, 0.35f, 0.50f }, // CoreShield: lighthouse health +%
            new[] { 0.25f, 0.5f, 0.8f, 1.2f },    // WarChest: wave-clear bonus +
            new[] { 0.10f, 0.20f, 0.30f, 0.45f }, // SharkArmor: damage taken -%
            new[] { 3f, 6f, 10f, 15f },           // BigNet: net capacity +
            new[] { 0.10f, 0.20f, 0.35f, 0.50f }, // FishGloves: corpse sale value +%
            new[] { 0.15f, 0.30f, 0.50f, 0.80f }, // Gauntlet: hand damage vs fortress enemies +%
            new[] { 0.05f, 0.10f, 0.15f, 0.25f }, // ToolBelt: build / upgrade / repair cost -%
            new[] { 1.25f, 1.5f, 2f, 3f },        // LuckyCharm: drop chance x
            new[] { 7f, 8.5f, 10f, 12f },         // Overclock: seconds of double turret fire rate
            new[] { 0.4f, 0.5f, 0.6f, 0.8f },     // FieldRepair: share of max health back, within 10 m
            new[] { 10f, 12f, 14f, 16f },         // Decoy: seconds the duck holds the fish's attention
            new[] { 12f, 14f, 16f, 20f },         // NetCannon: radius scooped into your net
            new[] { 1.5f, 1.8f, 2.2f, 3f },       // Shockwave: seconds stunned (6 m, 40 damage)
            // Weapons: damage is at wave-1 strength and grows with the wave's fish health (like the fish), so a weapon stays as useful at wave 20.
            new[] { 30f, 40f, 54f, 72f },         // Laser: beam damage per second, up to x3 the longer the beam stays on
            new[] { 4.5f, 6.5f, 9.5f, 15f },      // PoisonGas: poison per stack per second, up to 5 stacks
            new[] { 24f, 28f, 34f, 46f },         // Boomerang: damage on the way out and again on the way back
            new[] { 60f, 78f, 102f, 105f },       // SharkWhistle: damage per bite (x3 on spitters)
            new[] { 2f, 2.5f, 3f, 3.5f },         // BubbleGun: seconds a fish floats helpless
            new[] { 23f, 30f, 39f, 51f },         // Hook: damage, then the pull
            new[] { 3f, 3.5f, 4f, 5f },           // Whirlpool: seconds it pulls
            new[] { 4f, 4.5f, 5f, 6f },           // Conch: metres the fish are pushed back
            new[] { 2.5f, 3f, 3.5f, 4.5f },       // NetLauncher: seconds the fish under the net are held fast
            new[] { 12f, 16f, 22f, 32f },         // JellyGun: sting damage per second to every fish under the jellyfish (6 s)
            new[] { 6f, 7f, 8.5f, 10f },          // WaterCannon: metres a second the spray pushes fish back (it hurts a little too)
            new[] { 60f, 82f, 112f, 115f },       // PufferBomb: the burst, to every fish round it (and two stacks of poison)
            new[] { 13f, 17f, 21f, 27f },         // StarThrower: damage per starfish (3-5 thrown in a fan, each through two fish)
            new[] { 90f, 126f, 174f, 180f },      // LobsterClaw: the fish grabbed is thrown: damage when it lands, half to the fish it lands on
            new[] { 40f, 54f, 72f, 73f },         // Rocket: the blast, to every fish within RocketRadius
            // The game's guns: their own damage times this (and the wave, like the others; WeaponItems.GunDamage).
            new[] { 1f, 1.4f, 1.9f, 2.6f }, new[] { 1f, 1.4f, 1.9f, 2.6f }, new[] { 1f, 1.4f, 1.9f, 2.6f },
            new[] { 1f, 1.4f, 1.9f, 2.6f }, new[] { 1f, 1.4f, 1.9f, 2.6f }, new[] { 1f, 1.4f, 1.9f, 2.6f },
            new[] { 18f, 24f, 31f, 40f },         // Absorber: what its own shots hit for (what it holds multiplies it, or brings its own)
            new[] { 11.5f, 15f, 19f, 24f },       // Gatling: damage a bullet, Weapons.GatlingRate a second once the barrels are spun up
        };

        private static readonly float[][] WeaponCooldowns =
        {
            new[] { 0f, 0f, 0f, 0f }, new[] { 2.2f, 2f, 1.8f, 1.5f }, new[] { 0f, 0f, 0f, 0f }, new[] { 34f, 30f, 26f, 22f },  // laser: heat; boomerang: until it's back
            new[] { 0.9f, 0.8f, 0.75f, 0.6f }, new[] { 2.2f, 2f, 1.7f, 1.2f }, new[] { 9f, 8f, 7f, 5.5f }, new[] { 4.5f, 4f, 3.5f, 2.6f },
            new[] { 7f, 6f, 5.2f, 4f }, new[] { 3.8f, 3.4f, 3f, 2.4f }, new[] { 0f, 0f, 0f, 0f }, new[] { 3.8f, 3.4f, 3f, 2.6f }, // net, jelly, water (a tank), puffer
            new[] { 0.7f, 0.6f, 0.52f, 0.42f }, new[] { 1.9f, 1.7f, 1.4f, 1.1f },                                              // starfish, claw
            new[] { 1.6f, 1.45f, 1.3f, 1.2f },                                                                                 // rocket
            new[] { 0f, 0f, 0f, 0f }, new[] { 0f, 0f, 0f, 0f }, new[] { 0f, 0f, 0f, 0f },                                      // the game's guns: their own rate of fire
            new[] { 0f, 0f, 0f, 0f }, new[] { 0f, 0f, 0f, 0f }, new[] { 0f, 0f, 0f, 0f },
            new[] { 0.9f, 0.8f, 0.7f, 0.55f },                                                                                 // absorber: an item shot (a trophy's move: a few of these)
            new[] { 0f, 0f, 0f, 0f },                                                                                          // gatling: a stream while held (its rate in Weapons)
        };

        // Active gear has none: each one is used up (Actives).
        public static float Cooldown(Gear g, Rarity r) => IsWeapon(g) ? WeaponCooldowns[g - Gear.Laser][(int)r] : 0f;

        // The same kind worn twice or more adds up (Owen: "同一件裝備可以疊", the playtest: "想坦我就穿三個鯊魚衣"): a cut
        // (damage taken, prices, the frost's slow) multiplies down so it never reaches all of it, the lucky charm adds its
        // extra chance (two legendaries: x5, not x9), the rest add. 0 when none is worn.
        public static float Stacked(Gear g, IEnumerable<float> worn)
        {
            float sum = 0f, left = 1f;
            int n = 0;
            foreach (float v in worn) { n++; sum += g == Gear.LuckyCharm ? v - 1f : v; left *= 1f - v; }
            return n == 0 ? 0f : g == Gear.SharkArmor || g == Gear.ToolBelt || g == Gear.FrostCore ? 1f - left : g == Gear.LuckyCharm ? 1f + sum : sum;
        }

        public static float Value(Gear g, Rarity r) => Values[(int)g][(int)r];

        // Fish scales: what dismantling gives, what ranking up costs (into legendary too, dearly: it was find-or-merge only,
        // and Owen with thousands of scales asked 「為什麼不行 明明就夠阿」),
        // and what enhancing a legendary costs (+1..+5, 10% stronger per level; weapons have each player's mods instead).
        public static readonly int[] SalvageScales = { 1, 3, 8, 20 };
        public static readonly int[] AscendCosts = { 4, 10, 50 }; // common -> rare, rare -> epic, epic -> legendary
        public static int AscendCost(Rarity r) => r < Rarity.Legendary ? AscendCosts[(int)r] : -1;
        public static readonly int[] EnhanceCosts = { 15, 25, 40, 60, 90 };
        public const int MaxEnhance = 5;
        public const float EnhanceStep = 0.1f;

        private static bool IsPercent(Gear g) => !(g == Gear.Incendiary || g == Gear.TeslaCoil || g == Gear.BigNet || g == Gear.LuckyCharm)
                                                 && (!IsActive(g) || g == Gear.FieldRepair) && !IsWeapon(g);

        public static string FormatValue(Gear g, Rarity r) => FormatValue(g, Value(g, r));
        public static string FormatValue(Gear g, float v)
        {
            if (IsPercent(g)) return Mathf.RoundToInt(v * 100f) + "%";
            if (g == Gear.LuckyCharm || IsGameGun(g)) return "×" + v.ToString("0.##");
            if (g == Gear.BigNet || g == Gear.TeslaCoil) return Mathf.RoundToInt(v).ToString(); // whole fish / targets, rounded as in play
            return v.ToString("0.#"); // seconds, metres, burn per second (a rare shockwave stuns 1.2 s, not "1")
        }

        public static string Name(Gear g) => L10n.T("gear" + g);
        public static string Effect(Gear g, Rarity r) => Effect(g, r, Value(g, r));
        public static string Effect(Gear g, Rarity r, float v, float cooldownMult = 1f) =>
            L10n.T("gearFx" + g, FormatValue(g, v)) + (Cooldown(g, r) > 0f ? "<size=85%><color=#9aa4b0>　" + L10n.T("cooldown", (Cooldown(g, r) * cooldownMult).ToString("0.#")) + "</color></size>" : "");
        public static string RarityName(Rarity r) => L10n.T("rarity" + r);
        public static Color Color(Rarity r) => r == Rarity.Legendary ? new Color(1f, 0.71f, 0.18f) : r == Rarity.Epic ? new Color(0.71f, 0.36f, 1f) : r == Rarity.Rare ? new Color(0.29f, 0.64f, 1f) : new Color(0.91f, 0.91f, 0.91f);
        public static string Hex(Rarity r) => ColorUtility.ToHtmlStringRGB(Color(r));
        public static string Label(Gear g, Rarity r) => $"<color=#{Hex(r)}>[{RarityName(r)}] {Name(g)}</color>";

        // Drop rarity weights (common / rare / epic / legendary) at wave 1 and from wave 20 on, straight line in between.
        public static readonly float[] RollFirst = { 70, 22, 7, 1 }, RollLate = { 35, 35, 22, 8 };

        public static Rarity Roll(int wave, Rarity atLeast = Rarity.Common)
        {
            float t = Mathf.Clamp01((wave - 1) / 19f);
            float[] w = { Mathf.Lerp(RollFirst[0], RollLate[0], t), Mathf.Lerp(RollFirst[1], RollLate[1], t), Mathf.Lerp(RollFirst[2], RollLate[2], t), Mathf.Lerp(RollFirst[3], RollLate[3], t) };
            for (int i = 0; i < (int)atLeast; i++) w[i] = 0f;
            float pick = UnityEngine.Random.value * w.Sum();
            for (int i = 0; i < 4; i++) { if (pick < w[i]) return (Rarity)i; pick -= w[i]; }
            return Rarity.Legendary;
        }

        // A weapon comes up as often as when there were fourteen (players found that the right amount), any of them alike.
        public static Gear RandomGear()
        {
            Gear g;
            do { g = All[UnityEngine.Random.Range(0, (int)Gear.LobsterClaw + 1)]; } while (Retired(g));
            return IsWeapon(g) ? Droppable[UnityEngine.Random.Range(0, Droppable.Length)] : g;
        }
    }

    // One of the game's own items in the vault, the way the game saves an item (weight, cookness, skin, attachments...),
    // so it comes back out the same; Dead: a fish (a creature) that was dead when it went in.
    public class StoredItem
    {
        public SavedItem Saved;
        public bool Dead;
        public int Worth; // what it was worth going in (kept for the absorber; not in the item line)
        public byte Id => Saved.ItemID;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string F(float f) => f.ToString("R", Inv);
        private static string B(bool b) => b ? "1" : "0";

        // "<id> <dead> <the rest>": the vault file's item line after "item".
        public string Write()
        {
            var s = Saved;
            return $"{s.ItemID} {B(Dead)} {F(s.Cookness)} {F(s.BettingMultiplier)} {F(s.Weight)} {F(s.KillScoreMultiplier)} {B(s.IsDripCreature)} " +
                   $"{s.SkinIndex} {s.Sharpness} {s.Quiver} {s.Sight} {s.BarrelAttachment} {s.AmmoType} {B(s.ExtendedMag)} {B(s.LaserSight)}";
        }

        public static StoredItem Read(string id, string dead, string rest)
        {
            var f = rest.Split(' ');
            float P(int i) => float.Parse(f[i], Inv);
            byte Y(int i) => byte.Parse(f[i], Inv);
            return new StoredItem
            {
                Dead = dead == "1",
                Saved = new SavedItem
                {
                    Exists = true, ItemID = byte.Parse(id, Inv), Cookness = P(0), BettingMultiplier = P(1), Weight = P(2), KillScoreMultiplier = P(3),
                    IsDripCreature = f[4] == "1", SkinIndex = Y(5), Sharpness = Y(6), Quiver = Y(7), Sight = Y(8), BarrelAttachment = Y(9),
                    AmmoType = Y(10), ExtendedMag = f[11] == "1", LaserSight = f[12] == "1",
                },
            };
        }
    }

    // The team vault: what's owned (not equipped), the fortress loadout and each player's personal slots, and the game's own
    // items thrown in. Plain text, one record per line, so the save file and the network message are the same thing (the
    // message only counts the game's items: their states stay with the host).
    // A piece in the vault: equipment kind, rarity, and stars (0 = the base "no star", 1..3 = forged).
    // Implicit conversion from the old (Gear,Rarity) keeps existing call sites readable while the star field rolls in.
    public readonly struct GearKey : System.IEquatable<GearKey>
    {
        public readonly Gear G; public readonly Rarity R; public readonly int S;
        public Gear Item1 => G; public Rarity Item2 => R; public int Stars => S; public int Item3 => S;
        public GearKey(Gear g, Rarity r, int s) { G = g; R = r; S = Mathf.Clamp(s, 0, GearCatalog.MaxStars); }
        public static implicit operator GearKey((Gear, Rarity) t) => new GearKey(t.Item1, t.Item2, 0);
        public static implicit operator (Gear, Rarity)(GearKey k) => (k.G, k.R);
        public static bool operator ==(GearKey a, GearKey b) => a.G == b.G && a.R == b.R && a.S == b.S;
        public static bool operator !=(GearKey a, GearKey b) => !(a == b);
        public bool Equals(GearKey o) => this == o;
        public override bool Equals(object o) => o is GearKey k && this == k;
        public override int GetHashCode() => ((int)G * 397 ^ (int)R) * 31 + S;
        public override string ToString() => $"{R} {G}" + (S > 0 ? " " + new string('*', S) : "");
        public void Deconstruct(out Gear g, out Rarity r) { g = G; r = R; }
        public void Deconstruct(out Gear g, out Rarity r, out int s) { g = G; r = R; s = S; }
    }

    public class VaultData
    {
        public readonly Dictionary<GearKey, int> Owned = new Dictionary<GearKey, int>();
        public readonly List<GearKey> Fortress = new List<GearKey>();
        public readonly Dictionary<ulong, List<GearKey>> Personal = new Dictionary<ulong, List<GearKey>>();
        public readonly Dictionary<ulong, string> Names = new Dictionary<ulong, string>();
        public readonly HashSet<PieceType> Unlocked = new HashSet<PieceType>(); // blueprint turrets the team has found
        public int Scales;                                                      // fish scales, the team's crafting currency
        public readonly Dictionary<Gear, int> Enhance = new Dictionary<Gear, int>(); // +levels on a kind's legendaries (not weapons)
        public readonly Dictionary<(ulong, Gear, int), int> Mods = new Dictionary<(ulong, Gear, int), int>(); // (player, weapon kind, line): level (WeaponMods)
        public int Best;                                                        // most waves cleared in a real round on this save
        public int Souls;                                                       // souls from records (wave 100 and beyond): paid at the lighthouse keeper
        public int SoulDmg, SoulCrit, SoulCoins, SoulScales, SoulCore, SoulDiscount, SoulTurretDmg, SoulKnock, SoulHealth; // the keeper's permanent tree (levels, bought with souls)
        public float DamageMult => Mathf.Pow(1.15f, Mathf.Max(0, SoulDmg));
        public float CritChance => Mathf.Min(1f, 0.02f * Mathf.Max(0, SoulCrit));
        public float ScaleMult => Mathf.Pow(1.15f, Mathf.Max(0, SoulScales));
        public float CoreMult => Mathf.Pow(1.15f, Mathf.Max(0, SoulCore));
        public float TowerDiscount => Mathf.Max(0.1f, 1f - 0.1f * Mathf.Max(0, SoulDiscount)); // 10% a level, capped at 90% off
        public float TurretDamageMult => Mathf.Pow(1.10f, Mathf.Max(0, SoulTurretDmg)); // tower damage only: +10% a level
        public float KnockbackMult => 1f + 0.25f * Mathf.Max(0, SoulKnock); // explosions / water push +25% a level
        public float HealthTakenMult => 1f / (1f + 0.10f * Mathf.Max(0, SoulHealth)); // character health: +10% effective HP a level
        public float SettleMult => 1f + 0.10f * Mathf.Max(0, SoulCoins); // settlement money, +10% a level
        public readonly HashSet<ulong> Starter = new HashSet<ulong>();          // players who got their first laser gun
        public readonly Dictionary<Expansion, int> Bought = new Dictionary<Expansion, int>(); // expansions bought so far
        public readonly List<(string date, string team)> Wins = new List<(string, string)>();  // every OVER 99 on this save
        public int Helpers, HelperSpeed, HelperLoad;                            // the fish stall's helpers: how many, their speed and basket levels
        public bool Oven;                                                       // the stall's oven: the helpers' fish are sold grilled
        public readonly List<string> Unknown = new List<string>();              // lines this version can't read, written back as they were

        public int BoughtOf(Expansion e) => Bought.TryGetValue(e, out int n) ? n : 0;
        // Slots of a kind for this vault (0 team, 1 personal, 2 active, 3 weapon), with what's been bought.
        public int SlotsOf(int kind) => kind == 0 ? GearCatalog.FortressSlots + BoughtOf(Expansion.TeamSlot)
                                      : kind == 1 ? GearCatalog.PersonalSlots + BoughtOf(Expansion.PersonalSlot) : GearCatalog.SlotsOf(kind);

        // Storage is counted in slots: one slot holds up to Stack of one thing (one gear or weapon at one rarity, or one of
        // the game's items).
        public readonly List<StoredItem> Items = new List<StoredItem>();                // host: the game's items, each as it was
        public readonly Dictionary<byte, int> ItemCount = new Dictionary<byte, int>();  // how many of each (all a client gets)
        public int StorageSlots => GearCatalog.StorageBase + GearCatalog.StorageStep * BoughtOf(Expansion.Storage);
        private static int Stacks(int n) => (n + GearCatalog.Stack - 1) / GearCatalog.Stack;
        // One vault slot per (gear, rarity); its 0-3 star counts are sub-stacks inside that slot.
        public int SlotsUsed => Owned.Keys.Select(k => (k.G, k.R)).Distinct().Count() + ItemCount.Values.Sum(Stacks);
        public bool VaultFull => SlotsUsed >= StorageSlots;
        private bool HasGroup(Gear g, Rarity r) => Owned.Keys.Any(k => k.G == g && k.R == r);
        public bool Fits(Gear g, Rarity r, int stars = 0) => HasGroup(g, r) || !VaultFull;
        public bool FitsItem(byte id) => CountItem(id) % GearCatalog.Stack != 0 || !VaultFull;

        public int CountItem(byte id) => ItemCount.TryGetValue(id, out int n) ? n : 0;
        public void AddItem(StoredItem s)
        {
            Items.Add(s);
            ItemCount[s.Id] = CountItem(s.Id) + 1;
        }
        // The last one of that kind put in (host).
        // What each player's absorber holds (theirs, like their mods): the host keeps the item as it went in, a client its id.
        public readonly Dictionary<ulong, StoredItem> Absorbed = new Dictionary<ulong, StoredItem>();
        public StoredItem AbsorbedOf(ulong player) => Absorbed.TryGetValue(player, out var s) ? s : null;

        public StoredItem TakeItem(byte id)
        {
            int i = Items.FindLastIndex(x => x.Id == id);
            if (i < 0) return null;
            var s = Items[i];
            Items.RemoveAt(i);
            if ((ItemCount[id] = CountItem(id) - 1) <= 0) ItemCount.Remove(id);
            return s;
        }

        public int EnhanceOf(Gear g) => Enhance.TryGetValue(g, out int n) ? n : 0;
        // A star is +8% on the piece's own rarity value; three lower stars still stay below the next rarity's base.
        public static float StarMult(int stars) => 1f + 0.08f * Mathf.Clamp(stars, 0, GearCatalog.MaxStars);
        public float ValueOf(Gear g, Rarity r, int stars = 0) => GearCatalog.Value(g, r) * StarMult(stars) * (r == Rarity.Legendary ? 1f + GearCatalog.EnhanceStep * EnhanceOf(g) : 1f);
        // "[Legendary] Laser +3": gear's +N is its enhancement, a weapon's `player`'s mods on its kind.
        public string LabelOf(Gear g, Rarity r, ulong player = 0) => GearCatalog.Label(g, r) + (PlusOf(g, r, player) is int n && n > 0 ? $"<color=#{GearCatalog.Hex(r)}> +{n}</color>" : "");
        public int PlusOf(Gear g, Rarity r, ulong player) => GearCatalog.IsWeapon(g) ? ModTotal(player, g) : r == Rarity.Legendary ? EnhanceOf(g) : 0;
        public int ModOf(ulong player, Gear g, int track) => Mods.TryGetValue((player, g, track), out int n) ? n : 0;
        public int ModTotal(ulong player, Gear g) => Enumerable.Range(0, WeaponMods.Tracks).Sum(t => ModOf(player, g, t));
        // A weapon's number for one player: with their power (the game's guns have bullets instead, on the shooter's machine).
        public float WeaponValue(ulong player, Gear g, Rarity r, int stars = 0) => ValueOf(g, r, stars) * (GearCatalog.IsGameGun(g) ? 1f : WeaponMods.PowerMult(ModOf(player, g, WeaponMods.Power), g));
        // What a piece does for one player: a weapon with their power and speed, the rest as the team has it.
        public string EffectFor(ulong player, Gear g, Rarity r) =>
            !GearCatalog.IsWeapon(g) ? GearCatalog.Effect(g, r, ValueOf(g, r)) : GearCatalog.Effect(g, r, WeaponValue(player, g, r), WeaponMods.TimeMult(ModOf(player, g, WeaponMods.Rate)));
        // The best star this player has for a weapon kind (their loadout, or a copy still in the vault).
        public int BestStar(ulong player, Gear g, Rarity r)
        {
            int best = 0;
            if (Personal.TryGetValue(player, out var l))
                foreach (var x in l) if (x.Item1 == g && x.Item2 == r) best = Mathf.Max(best, x.Stars);
            foreach (var kv in Owned) if (kv.Key.Item1 == g && kv.Key.Item2 == r) best = Mathf.Max(best, kv.Key.Stars);
            return best;
        }
        public bool OwnsLegendary(Gear g) =>
            Owned.Keys.Any(k => k.Item1 == g && k.Item2 == Rarity.Legendary) || Fortress.Any(f => f.Item1 == g && f.Item2 == Rarity.Legendary) || Personal.Values.Any(l => l.Any(x => x.Item1 == g && x.Item2 == Rarity.Legendary));

        // The quartermaster's one-click sale (Owen: "一鍵賣（可以按類別或品質）"): everything in a tab (0 all, 1 gear, 2 weapons,
        // 3 the game's items) of one colour, or of every colour (null; only then the game's items, which have none). Weapons
        // and the game's items go for money as at the stall's counter, gear (nothing the counter takes) melts into scales as
        // at the smith's. SaleOf only counts the game's items (a client has their counts, not what each is worth).
        private static bool OnSale(Gear g, Rarity r, int tab, Rarity? colour) =>
            tab != 3 && (tab != 1 || !GearCatalog.IsWeapon(g)) && (tab != 2 || GearCatalog.IsWeapon(g)) && (colour == null || colour == r);
        private static bool ItemsOnSale(int tab, Rarity? colour) => colour == null && (tab == 0 || tab == 3);
        public (int money, int scales, int count) SaleOf(int tab, Rarity? colour)
        {
            int money = 0, scales = 0, n = 0;
            foreach (var kv in Owned.Where(kv => OnSale(kv.Key.Item1, kv.Key.Item2, tab, colour)))
            {
                if (GearCatalog.IsWeapon(kv.Key.Item1)) money += Mathf.RoundToInt(WeaponItems.Worth(kv.Key.Item2) * (1f + 0.25f * kv.Key.Stars)) * kv.Value;
                else scales += Mathf.RoundToInt(GearCatalog.SalvageScales[(int)kv.Key.Item2] * (1f + 0.5f * kv.Key.Stars)) * kv.Value;
                n += kv.Value;
            }
            if (ItemsOnSale(tab, colour)) n += ItemCount.Values.Sum();
            return (money, scales, n);
        }
        // Host: sold (`worth`: what the counter pays for one of the game's items); the scales are the team's at once.
        public (int money, int scales, int count) SellAll(int tab, Rarity? colour, Func<StoredItem, int> worth)
        {
            var sale = SaleOf(tab, colour);
            foreach (var k in Owned.Keys.Where(k => OnSale(k.Item1, k.Item2, tab, colour)).ToList()) Owned.Remove(k);
            if (ItemsOnSale(tab, colour)) { sale.money += Items.Sum(worth); Items.Clear(); ItemCount.Clear(); }
            Scales += sale.scales;
            return sale;
        }

        public bool CanBuild(PieceType t) => !Catalog.NeedsBlueprint(t) || Unlocked.Contains(t);

        public int Count(Gear g, Rarity r, int stars = 0) => Owned.TryGetValue(new GearKey(g, r, stars), out int n) ? n : 0;
        public int Count(GearKey k) => Owned.TryGetValue(k, out int n) ? n : 0;
        public void AddStar(Gear g, Rarity r, int stars, int n = 1)
        {
            if (GearCatalog.Retired(g)) return;
            var k = new GearKey(g, r, stars);
            int c = Count(k) + n;
            if (c <= 0) Owned.Remove(k); else Owned[k] = c;
        }
        public void Add(Gear g, Rarity r, int n = 1) => AddStar(g, r, 0, n);
        public void Add(GearKey k, int n = 1) => AddStar(k.G, k.R, k.S, n);

        public List<GearKey> Slots(ulong player) => Personal.TryGetValue(player, out var l) ? l : (Personal[player] = new List<GearKey>());

        // What one kind of gear worn adds, every one of it together (GearCatalog.Stacked): the fortress loadout, or one
        // player's slots (0 if none).
        public float FortressValue(Gear g) => GearCatalog.Stacked(g, Fortress.Where(x => x.Item1 == g).Select(x => ValueOf(g, x.Item2, x.Stars)));
        public float PersonalValue(ulong player, Gear g) =>
            Personal.TryGetValue(player, out var l) ? GearCatalog.Stacked(g, l.Where(x => x.Item1 == g).Select(x => ValueOf(g, x.Item2, x.Stars))) : 0f;

        // forClients: the game's items as counts only (their states stay with the host).
        public string Write(bool forClients = false)
        {
            var lines = new List<string>();
            foreach (var kv in Owned) lines.Add($"own {kv.Key.Item1} {kv.Key.Item2} {kv.Key.Stars} {kv.Value}");
            foreach (var f in Fortress) lines.Add($"fort {f.Item1} {f.Item2} {f.Stars}");
            foreach (var kv in Personal) foreach (var s in kv.Value) lines.Add($"pers {kv.Key} {s.Item1} {s.Item2} {s.Stars}");
            foreach (var kv in Names) lines.Add($"name {kv.Key} {kv.Value.Replace("\n", " ")}");
            foreach (var t in Unlocked) lines.Add($"unlock {t} 1");
            lines.Add($"scales {Scales} 0");
            foreach (var kv in Enhance) lines.Add($"enh {kv.Key} {kv.Value}");
            foreach (var kv in Mods) lines.Add($"mod {kv.Key.Item1} {kv.Key.Item2} {kv.Key.Item3} {kv.Value}");
            lines.Add($"best {Best} 0");
            lines.Add($"souls {Souls} 0");
            lines.Add($"soulup {SoulDmg} {SoulCrit} {SoulCoins} {SoulScales} {SoulCore} {SoulDiscount} {SoulTurretDmg} {SoulKnock} {SoulHealth}");
            foreach (var k in Starter) lines.Add($"starter {k} 1");
            foreach (var kv in Bought) lines.Add($"buy {kv.Key} {kv.Value}");
            foreach (var w in Wins) lines.Add($"win {w.date} {w.team.Split('、').Length} {w.team.Replace("\n", " ")}");
            lines.Add($"helpers {Helpers} {HelperSpeed} {HelperLoad}");
            lines.Add($"oven {(Oven ? 1 : 0)} 0");
            lines.Add($"modv {ModVersion} 0");
            foreach (var kv in Absorbed) lines.Add(forClients ? $"absorbn {kv.Key} {kv.Value.Id} {kv.Value.Worth}" : $"absorb {kv.Key} {kv.Value.Worth} {kv.Value.Write()}");
            if (forClients) foreach (var kv in ItemCount) lines.Add($"itemn {kv.Key} {kv.Value}");
            else foreach (var s in Items) lines.Add("item " + s.Write());
            if (!forClients) lines.AddRange(Unknown);
            return string.Join("\n", lines);
        }

        // How many merges are possible right now (three of a kind into the next rarity), and do all of them (chains included).
        public int Mergeable()
        {
            int n = 0;
            foreach (var kv in Owned)
            {
                if (kv.Value < 3) continue;
                if (kv.Key.Stars < GearCatalog.MaxStars || kv.Key.Item2 < Rarity.Legendary) n += kv.Value / 3;
            }
            return n;
        }
        public int MergeAll()
        {
            int total = 0;
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var k in Owned.Keys.OrderBy(k => k.Stars).ThenBy(k => k.Item2).ToList())
                {
                    if (k.Stars >= GearCatalog.MaxStars && k.Item2 >= Rarity.Legendary) continue;
                    while (Count(k) >= 3)
                    {
                        Add(k, -3);
                        if (k.Stars < GearCatalog.MaxStars) AddStar(k.Item1, k.Item2, k.Stars + 1);
                        else AddStar(k.Item1, k.Item2 + 1, 0);
                        total++;
                        changed = true;
                    }
                }
            }
            if (total > 0) AutoTest.Count("merged", total);
            return total;
        }

        // One vault action by player `key` (host). Returns false and changes nothing when it isn't allowed.
        public bool Apply(VaultAction action, Gear g, Rarity r, int slot, ulong key, int stars = 0)
        {
            if (GearCatalog.Retired(g)) return false;
            switch (action)
            {
                case VaultAction.MergeAll:
                    return MergeAll() > 0;
                case VaultAction.Merge:
                    if (Count(g, r, stars) < 3) return false;
                    if (stars >= GearCatalog.MaxStars && r >= Rarity.Legendary) return false;
                    AddStar(g, r, stars, -3);
                    if (stars < GearCatalog.MaxStars) AddStar(g, r, stars + 1);
                    else AddStar(g, r + 1, 0);
                    AutoTest.Count("merged");
                    return true;
                case VaultAction.EquipFortress:
                    if (!GearCatalog.IsFortress(g) || Count(g, r, stars) < 1 || Fortress.Count >= SlotsOf(0)) return false;
                    if (Fortress.Any(f => f.Item1 == g)) return false; // one of each kind: stacking four Gunpowders made a fort one-shot wave 70
                    AddStar(g, r, stars, -1);
                    Fortress.Add(new GearKey(g, r, stars));
                    return true;
                case VaultAction.EquipPersonal:
                    var slots = Slots(key);
                    int kind = GearCatalog.SlotKind(g);
                    if (kind == 0 || kind == 2 || Count(g, r, stars) < 1 || slots.Count(s => GearCatalog.SlotKind(s.Item1) == kind) >= SlotsOf(kind)) return false;
                    AddStar(g, r, stars, -1);
                    slots.Add(new GearKey(g, r, stars));
                    return true;
                case VaultAction.Salvage:
                    if (Count(g, r, stars) < 1) return false;
                    AddStar(g, r, stars, -1);
                    Scales += Mathf.RoundToInt(GearCatalog.SalvageScales[(int)r] * (1f + 0.5f * stars));
                    AutoTest.Count("salvaged");
                    return true;
                case VaultAction.Ascend:
                    int cost = GearCatalog.AscendCost(r);
                    if (cost < 0 || Count(g, r) < 1 || Scales < cost) return false;
                    Add(g, r, -1);
                    Add(g, r + 1);
                    Scales -= cost;
                    AutoTest.Count("ascended");
                    return true;
                case VaultAction.Enhance:
                    int lvl = EnhanceOf(g);
                    if (GearCatalog.IsWeapon(g) || !OwnsLegendary(g) || lvl >= GearCatalog.MaxEnhance || Scales < GearCatalog.EnhanceCosts[lvl]) return false;
                    Scales -= GearCatalog.EnhanceCosts[lvl];
                    Enhance[g] = lvl + 1;
                    AutoTest.Count("enhanced");
                    return true;
                // A player's own weapon upgrade: `slot` is the line.
                case VaultAction.Mod:
                    int ml = ModOf(key, g, slot);
                    int mc = WeaponMods.Cost(ml);
                    if (!WeaponMods.Has(g, slot) || Scales < mc) return false;
                    Scales -= mc;
                    Mods[(key, g, slot)] = ml + 1;
                    AutoTest.Count("modded");
                    return true;
                case VaultAction.Expand:
                    var e = (Expansion)slot;
                    if (!Enum.IsDefined(typeof(Expansion), e)) return false;
                    var price = GearCatalog.NextPrice(e, BoughtOf(e));
                    if (price == null || Scales < price.Value.scales || Best < price.Value.best) return false;
                    Scales -= price.Value.scales;
                    Bought[e] = BoughtOf(e) + 1;
                    AutoTest.Count("expanded_" + e);
                    return true;
                // Taken off by what it is, not by its place: with two players at the desk, places shift under you.
                case VaultAction.UnequipFortress:
                    int fi = Fortress.FindIndex(f => f.Item1 == g && f.Item2 == r && f.Stars == stars);
                    if (fi < 0) return false;
                    Fortress.RemoveAt(fi);
                    AddStar(g, r, stars);
                    return true;
                case VaultAction.UnequipPersonal:
                    var mine = Slots(key);
                    int pi = mine.FindIndex(x => x.Item1 == g && x.Item2 == r && x.Stars == stars);
                    if (pi < 0) return false;
                    mine.RemoveAt(pi);
                    AddStar(g, r, stars);
                    return true;
            }
            return false;
        }

        // The mods' layout: 2 since the range line (track 3), which took over the range traits of before.
        public const int ModVersion = 2;
        private static readonly Gear[] RangeTraits = { Gear.Boomerang, Gear.Conch, Gear.WaterCannon, Gear.Absorber };

        public static VaultData Read(string text)
        {
            var v = new VaultData();
            int modv = 1;
            foreach (var line in (text ?? "").Split('\n'))
            {
                string raw = line.Trim();
                if (raw.Length == 0) continue;
                var f = raw.Split(new[] { ' ' }, 4);
                bool known = f.Length >= 3;
                if (known)
                try
                {
                    switch (f[0])
                    {
                        case "own":
                            {
                                var g = P<Gear>(f[1]); var r = P<Rarity>(f[2]);
                                if (GearCatalog.Retired(g)) break;
                                var cells = f[3].Split(' ');
                                if (cells.Length >= 2) v.AddStar(g, r, int.Parse(cells[0]), int.Parse(cells[1])); // old line: count only = 0 stars
                                else v.Add(g, r, int.Parse(cells[0]));
                                break;
                            }
                        case "fort":
                            {
                                var g = P<Gear>(f[1]); var r = P<Rarity>(f[2]);
                                if (GearCatalog.Retired(g)) break;
                                int st = f.Length > 3 ? int.Parse(f[3]) : 0;
                                int i = v.Fortress.FindIndex(x => x.Item1 == g);
                                if (i < 0) v.Fortress.Add(new GearKey(g, r, st)); else if (r > v.Fortress[i].Item2 || (r == v.Fortress[i].Item2 && st > v.Fortress[i].Stars)) v.Fortress[i] = new GearKey(g, r, st);
                                break;
                            }
                        case "pers": // active gear isn't worn any more: one worn before goes back into the vault
                            var pp = f[3].Split(' ');
                            var pg = P<Gear>(f[2]); var pr = P<Rarity>(pp[0]);
                            int ps = pp.Length > 1 ? int.Parse(pp[1]) : 0;
                            if (GearCatalog.Retired(pg)) break;
                            if (GearCatalog.IsActive(pg)) v.AddStar(pg, pr, ps);
                            else v.Slots(ulong.Parse(f[1])).Add(new GearKey(pg, pr, ps));
                            break;
                        case "name": v.Names[ulong.Parse(f[1])] = f.Length > 3 ? f[2] + " " + f[3] : f[2]; break;
                        case "unlock": v.Unlocked.Add(P<PieceType>(f[1])); break;
                        case "scales": v.Scales = int.Parse(f[1]); break;
                        case "enh": v.Enhance[P<Gear>(f[1])] = int.Parse(f[2]); break;
                        case "mod": var tl = f[3].Split(' '); v.Mods[(ulong.Parse(f[1]), P<Gear>(f[2]), int.Parse(tl[0]))] = int.Parse(tl[1]); break;
                        case "best": v.Best = int.Parse(f[1]); break;
                        case "souls": v.Souls = int.Parse(f[1]); break;
                        case "soulup":
                            var su = f[3].Split(' ');
                            v.SoulDmg = int.Parse(f[1]); v.SoulCrit = Mathf.Max(0, int.Parse(f[2]));
                            v.SoulCoins = Mathf.Max(0, int.Parse(su[0])); v.SoulScales = Mathf.Max(0, int.Parse(su[1])); v.SoulCore = Mathf.Max(0, int.Parse(su[2]));
                            v.SoulDiscount = su.Length > 3 ? Mathf.Max(0, int.Parse(su[3])) : 0;
                            v.SoulTurretDmg = su.Length > 4 ? Mathf.Max(0, int.Parse(su[4])) : 0;
                            v.SoulKnock = su.Length > 5 ? Mathf.Max(0, int.Parse(su[5])) : 0;
                            v.SoulHealth = su.Length > 6 ? Mathf.Max(0, int.Parse(su[6])) : 0; break;
                        case "starter": v.Starter.Add(ulong.Parse(f[1])); break;
                        case "buy": v.Bought[P<Expansion>(f[1])] = int.Parse(f[2]); break;
                        case "win": v.Wins.Add((f[1], f.Length > 3 ? f[3] : "")); break;
                        case "helpers": v.Helpers = int.Parse(f[1]); v.HelperSpeed = int.Parse(f[2]); v.HelperLoad = f.Length > 3 ? int.Parse(f[3]) : 0; break;
                        case "oven": v.Oven = f[1] == "1"; break;
                        case "modv": modv = int.Parse(f[1]); break;
                        case "item": if (f.Length > 3) v.AddItem(StoredItem.Read(f[1], f[2], f[3])); break;
                        case "itemn": v.ItemCount[byte.Parse(f[1])] = int.Parse(f[2]); break;
                        case "absorb": var ab = f[3].Split(new[] { ' ' }, 3); var held = StoredItem.Read(ab[0], ab[1], ab[2]); held.Worth = int.Parse(f[2]); v.Absorbed[ulong.Parse(f[1])] = held; break;
                        case "absorbn": v.Absorbed[ulong.Parse(f[1])] = new StoredItem { Saved = new SavedItem { Exists = true, ItemID = byte.Parse(f[2]) }, Worth = int.Parse(f[3]) }; break;
                        default: known = false; break;
                    }
                }
                catch (Exception) { known = false; } // one bad line never loses the rest of the vault
                if (!known) v.Unknown.Add(raw); // kept as it is and written back (a newer version's gear an older host can't read)
            }
            // A weapon's legendary enhancement was the team's: it becomes the power line of everyone who has played on the save.
            foreach (var kv in v.Enhance.Where(kv => GearCatalog.IsWeapon(kv.Key)).ToList())
            {
                foreach (ulong p in v.Starter.Concat(v.Names.Keys).Concat(v.Personal.Keys).Distinct())
                    v.Mods[(p, kv.Key, WeaponMods.Power)] = Math.Max(v.ModOf(p, kv.Key, WeaponMods.Power), Math.Min(GearCatalog.MaxEnhance, kv.Value));
                v.Enhance.Remove(kv.Key);
            }
            // Before the range line these four's trait was how far they reach: those levels are their range now.
            if (modv < 2)
                foreach (var k in v.Mods.Keys.Where(k => k.Item3 == WeaponMods.Trait && RangeTraits.Contains(k.Item2)).ToList())
                {
                    v.Mods[(k.Item1, k.Item2, WeaponMods.Reach)] = v.Mods[k];
                    v.Mods.Remove(k);
                }
            return v;
        }

        private static T P<T>(string s) => (T)Enum.Parse(typeof(T), s);
    }

    // What equipped gear adds right now. Team(...) and Of(...) are host-side (the host's vault);
    // MyCost is client-side so the build bar shows your own discounted prices.
    public static class Perks
    {
        private static VaultData V => Loot.Instance ? Loot.Instance.Vault : null;

        // The team's loadout, worked out once a frame: every turret hit asks for several kinds (a few hundred hits a second in a
        // big fort), and it only changes at the vault.
        private static int _frame = -1;
        private static VaultData _of;
        private static readonly Dictionary<Gear, float> TeamNow = new Dictionary<Gear, float>();
        private static readonly Dictionary<Gear, bool> LegendNow = new Dictionary<Gear, bool>();
        private static void Fresh()
        {
            var v = V;
            if (Time.frameCount == _frame && v == _of) return;
            _frame = Time.frameCount; _of = v;
            TeamNow.Clear(); LegendNow.Clear();
        }

        public static float Team(Gear g)
        {
            Fresh();
            if (!TeamNow.TryGetValue(g, out float value)) TeamNow[g] = value = V?.FortressValue(g) ?? 0f;
            return value;
        }
        public static float Of(Player p, Gear g) => p && V != null ? V.PersonalValue(Loot.KeyOf(p), g) : 0f;
        public static float Of(int playerObjectId, Gear g) => Of(PlayerManager.Players.FirstOrDefault(x => x && x.ObjectId == playerObjectId), g);

        // Legendary gear also has a special effect of its own (see "gearLegend..." strings).
        public static bool TeamLegend(Gear g)
        {
            Fresh();
            if (!LegendNow.TryGetValue(g, out bool on)) LegendNow[g] = on = HasLegend(g);
            return on;
        }
        // (Apart from TeamLegend: the lambda captures `g`, so its closure was built on every call, cached answer or not.)
        private static bool HasLegend(Gear g) => V != null && V.Fortress.Any(f => f.Item1 == g && f.Item2 == Rarity.Legendary);
        public static bool Legend(Player p, Gear g) =>
            p && V != null && V.Personal.TryGetValue(Loot.KeyOf(p), out var l) && l.Any(x => x.Item1 == g && x.Item2 == Rarity.Legendary);
        public static bool Legend(int playerObjectId, Gear g) => Legend(PlayerManager.Players.FirstOrDefault(x => x && x.ObjectId == playerObjectId), g);

        // Host: a legendary special just went off (shown to everyone as "★ name" and a flash in the loadout). Max once per second per kind.
        private static readonly Dictionary<Gear, float> LastProc = new Dictionary<Gear, float>();
        public static void Proc(Gear g, Vector3 at)
        {
            if (LastProc.TryGetValue(g, out float t) && Time.time - t < 1f) return;
            LastProc[g] = Time.time;
            Fx.Send(FxKind.Proc, at, at, 0, (int)g);
        }

        public static int Cost(int baseCost, Player builder) => Discount(baseCost, Of(builder, Gear.ToolBelt));
        public static int MyCost(int baseCost) =>
            Discount(baseCost, Player.LocalPlayer && Loot.Instance ? Loot.Instance.ClientVault.PersonalValue(Loot.KeyOf(Player.LocalPlayer), Gear.ToolBelt) : 0f);
        // The keeper's discount is only for tower upgrades (not building or repairs).
        public static float Knockback => V != null ? V.KnockbackMult : 1f;
        public static int TowerUpgradeCost(int baseCost, Player builder)
        {
            int c = Cost(baseCost, builder);
            return Discount(c, V != null ? 1f - V.TowerDiscount : 0f);
        }
        public static int MyTowerUpgradeCost(int baseCost)
        {
            int c = MyCost(baseCost);
            var v = Loot.Instance ? Loot.Instance.ClientVault : null;
            return Discount(c, v != null ? 1f - v.TowerDiscount : 0f);
        }
        private static int Discount(int c, float off) => off > 0f ? Mathf.Max(1, Mathf.CeilToInt(c * (1f - Mathf.Clamp01(off)))) : c;

        public static int MaxHp(PieceType t, int level) =>
            t == PieceType.Wall ? Mathf.RoundToInt(Catalog.MaxHp(t, level) * (1f + Team(Gear.Rebar))) : Catalog.MaxHp(t, level);
    }
}
