using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HtfFortress
{
    public enum Rarity : byte { Common, Rare, Epic, Legendary }

    // Fortress gear first (team loadout), then personal gear (each player's own slots).
    public enum Gear : byte
    {
        Gunpowder, Scope, ApRounds, Incendiary, FrostCore, TeslaCoil, Rebar, CoreShield, WarChest,
        SharkArmor, BigNet, FishGloves, Gauntlet, ToolBelt, LuckyCharm,
    }

    // What every piece of gear does at each rarity. Tune here.
    public static class GearCatalog
    {
        public const int FortressSlots = 4, PersonalSlots = 2;
        public static readonly Gear[] All = (Gear[])Enum.GetValues(typeof(Gear));
        public static bool IsFortress(Gear g) => g <= Gear.WarChest;

        // Common / Rare / Epic / Legendary.
        private static readonly float[][] Values =
        {
            new[] { 0.08f, 0.15f, 0.25f, 0.40f }, // Gunpowder: turret damage +%
            new[] { 0.08f, 0.15f, 0.22f, 0.30f }, // Scope: turret range +%
            new[] { 0.20f, 0.40f, 0.70f, 1.00f }, // ApRounds: vs tank / boss +%
            new[] { 2f, 4f, 7f, 12f },            // Incendiary: burn damage per second, 3 s
            new[] { 0.10f, 0.15f, 0.20f, 0.30f }, // FrostCore: every turret hit slows
            new[] { 1f, 2f, 3f, 4f },             // TeslaCoil: extra chain targets
            new[] { 0.15f, 0.30f, 0.50f, 0.80f }, // Rebar: wall health +%
            new[] { 0.20f, 0.40f, 0.70f, 1.00f }, // CoreShield: lighthouse health +%
            new[] { 50f, 100f, 175f, 275f },      // WarChest: starting coins +
            new[] { 0.10f, 0.20f, 0.30f, 0.45f }, // SharkArmor: damage taken -%
            new[] { 3f, 6f, 10f, 15f },           // BigNet: net capacity +
            new[] { 0.10f, 0.20f, 0.35f, 0.50f }, // FishGloves: corpse sale value +%
            new[] { 0.15f, 0.30f, 0.50f, 0.80f }, // Gauntlet: hand damage vs fortress enemies +%
            new[] { 0.05f, 0.10f, 0.15f, 0.25f }, // ToolBelt: build / upgrade / repair cost -%
            new[] { 1.25f, 1.5f, 2f, 3f },        // LuckyCharm: drop chance x
        };

        public static float Value(Gear g, Rarity r) => Values[(int)g][(int)r];

        private static bool IsPercent(Gear g) => !(g == Gear.Incendiary || g == Gear.TeslaCoil || g == Gear.WarChest || g == Gear.BigNet || g == Gear.LuckyCharm);

        public static string FormatValue(Gear g, Rarity r)
        {
            float v = Value(g, r);
            return IsPercent(g) ? Mathf.RoundToInt(v * 100f) + "%" : g == Gear.LuckyCharm ? "×" + v.ToString("0.##") : v.ToString("0");
        }

        public static string Name(Gear g) => L10n.T("gear" + g);
        public static string Effect(Gear g, Rarity r) => L10n.T("gearFx" + g, FormatValue(g, r));
        public static string RarityName(Rarity r) => L10n.T("rarity" + r);
        public static Color Color(Rarity r) => r == Rarity.Legendary ? new Color(1f, 0.71f, 0.18f) : r == Rarity.Epic ? new Color(0.71f, 0.36f, 1f) : r == Rarity.Rare ? new Color(0.29f, 0.64f, 1f) : new Color(0.91f, 0.91f, 0.91f);
        public static string Hex(Rarity r) => ColorUtility.ToHtmlStringRGB(Color(r));
        public static string Label(Gear g, Rarity r) => $"<color=#{Hex(r)}>[{RarityName(r)}] {Name(g)}</color>";

        // Drop rarity: wave 1 = 70/22/7/1, wave 20+ = 35/35/22/8.
        public static Rarity Roll(int wave, Rarity atLeast = Rarity.Common)
        {
            float t = Mathf.Clamp01((wave - 1) / 19f);
            float[] w = { Mathf.Lerp(70, 35, t), Mathf.Lerp(22, 35, t), Mathf.Lerp(7, 22, t), Mathf.Lerp(1, 8, t) };
            for (int i = 0; i < (int)atLeast; i++) w[i] = 0f;
            float pick = UnityEngine.Random.value * w.Sum();
            for (int i = 0; i < 4; i++) { if (pick < w[i]) return (Rarity)i; pick -= w[i]; }
            return Rarity.Legendary;
        }

        public static Gear RandomGear() => All[UnityEngine.Random.Range(0, All.Length)];
    }

    // The team vault: what's owned (not equipped), the fortress loadout and each player's personal slots.
    // Plain text, one record per line, so the save file and the network message are the same thing.
    public class VaultData
    {
        public readonly Dictionary<(Gear, Rarity), int> Owned = new Dictionary<(Gear, Rarity), int>();
        public readonly List<(Gear, Rarity)> Fortress = new List<(Gear, Rarity)>();
        public readonly Dictionary<ulong, List<(Gear, Rarity)>> Personal = new Dictionary<ulong, List<(Gear, Rarity)>>();
        public readonly Dictionary<ulong, string> Names = new Dictionary<ulong, string>();

        public int Count(Gear g, Rarity r) => Owned.TryGetValue((g, r), out int n) ? n : 0;
        public void Add(Gear g, Rarity r, int n = 1)
        {
            int c = Count(g, r) + n;
            if (c <= 0) Owned.Remove((g, r)); else Owned[(g, r)] = c;
        }

        public List<(Gear, Rarity)> Slots(ulong player) => Personal.TryGetValue(player, out var l) ? l : (Personal[player] = new List<(Gear, Rarity)>());

        // Best equipped rarity of one kind of gear: fortress loadout, or one player's slots (0 if none).
        public float FortressValue(Gear g) => Fortress.Where(x => x.Item1 == g).Select(x => GearCatalog.Value(g, x.Item2)).DefaultIfEmpty(0f).Max();
        public float PersonalValue(ulong player, Gear g) =>
            Personal.TryGetValue(player, out var l) ? l.Where(x => x.Item1 == g).Select(x => GearCatalog.Value(g, x.Item2)).DefaultIfEmpty(0f).Max() : 0f;

        public string Write()
        {
            var lines = new List<string>();
            foreach (var kv in Owned) lines.Add($"own {kv.Key.Item1} {kv.Key.Item2} {kv.Value}");
            foreach (var f in Fortress) lines.Add($"fort {f.Item1} {f.Item2}");
            foreach (var kv in Personal) foreach (var s in kv.Value) lines.Add($"pers {kv.Key} {s.Item1} {s.Item2}");
            foreach (var kv in Names) lines.Add($"name {kv.Key} {kv.Value.Replace("\n", " ")}");
            return string.Join("\n", lines);
        }

        public static VaultData Read(string text)
        {
            var v = new VaultData();
            foreach (var line in (text ?? "").Split('\n'))
            {
                var f = line.Trim().Split(new[] { ' ' }, 4);
                if (f.Length < 3) continue;
                try
                {
                    switch (f[0])
                    {
                        case "own": v.Add(P<Gear>(f[1]), P<Rarity>(f[2]), int.Parse(f[3])); break;
                        case "fort": v.Fortress.Add((P<Gear>(f[1]), P<Rarity>(f[2]))); break;
                        case "pers": v.Slots(ulong.Parse(f[1])).Add((P<Gear>(f[2]), P<Rarity>(f[3]))); break;
                        case "name": v.Names[ulong.Parse(f[1])] = f.Length > 3 ? f[2] + " " + f[3] : f[2]; break;
                    }
                }
                catch (Exception) { } // one bad line never loses the rest of the vault
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

        public static float Team(Gear g) => V?.FortressValue(g) ?? 0f;
        public static float Of(Player p, Gear g) => p && V != null ? V.PersonalValue(Loot.KeyOf(p), g) : 0f;
        public static float Of(int playerObjectId, Gear g) => Of(PlayerManager.Players.FirstOrDefault(x => x && x.ObjectId == playerObjectId), g);

        public static int Cost(int baseCost, Player builder) => Discount(baseCost, Of(builder, Gear.ToolBelt));
        public static int MyCost(int baseCost) =>
            Discount(baseCost, Player.LocalPlayer && Loot.Instance ? Loot.Instance.ClientVault.PersonalValue(Loot.KeyOf(Player.LocalPlayer), Gear.ToolBelt) : 0f);
        private static int Discount(int c, float off) => off > 0f ? Mathf.Max(1, Mathf.CeilToInt(c * (1f - off))) : c;

        public static int MaxHp(PieceType t, int level) =>
            t == PieceType.Wall ? Mathf.RoundToInt(Catalog.MaxHp(t, level) * (1f + Team(Gear.Rebar))) : Catalog.MaxHp(t, level);
    }
}
