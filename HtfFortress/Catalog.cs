using System.Collections.Generic;
using UnityEngine;

namespace HtfFortress
{
    // Flame, Sniper and Bubble are unlocked by blueprints (from chests and bosses). The repair tower (Mender) is there from the start.
    public enum PieceType : byte { Wall, Floor, Ramp, Gun, Cannon, Harpoon, Mortar, Tesla, Spikes, Flame, Sniper, Bubble, Mender }

    // Every buildable thing's cost, health, turret stats and Lv3 branches. Tune balance here.
    // Levels have no cap: Lv1-3 are the tables below (turrets pick a branch on the way to Lv3); every level after that
    // multiplies damage by BeyondDamage and health by BeyondHp, while each step costs BeyondCost times the one before.
    public static class Catalog
    {
        public const int BranchLevel = 3;          // the last level in the tables; turrets choose A or B reaching it
        public const int SecondBranchLevel = BranchLevel + 6; // Lv9: six upgrades after the Lv3 branch, then A/B splits again (four kinds a turret)
        public const int OverdriveLevel = 20;      // a turret that shoots also blows up the fish round it every few seconds (Turrets.Nova)
        public static float NovaRadius(float range) => 9f + range * 0.25f;
        public static int FirstOf(int branch) => branch <= 2 ? branch : branch <= 4 ? 1 : 2;   // old saves: 1/2 are first-only
        public static int SecondOf(int branch) => branch <= 2 ? 0 : (branch == 3 || branch == 5) ? 1 : 2; // 3/5 = first side, 4/6 = second
        public static int BranchCode(int first, int second) => first == 1 ? (second == 1 ? 3 : 4) : (second == 1 ? 5 : 6);
        public static bool HasSecondBranch(PieceType t) => IsTurret(t);
        public static string BranchName(PieceType t, int branch) => branch <= 0 ? "" : L10n.T(BranchKey(t, branch));
        public static string BranchDesc(PieceType t, int branch) => branch <= 0 ? "" : L10n.T(BranchKey(t, branch) + "_d");
        public const float BeyondDamage = 1.3f, BeyondHp = 1.25f, BeyondCost = 1.35f, BeyondRange = 0.4f, BeyondRangeMax = 4f;
        // A repair tower gives a building a share of the building's own health a second (two over one building: only the stronger,
        // Turrets.Mend), and the lighthouse a quarter of that share (Owen 2026-10-26: two repair towers and he never had to
        // look at a tower's health; it used to be a flat amount growing ×1.3 a level, stacking, the lighthouse half of it).
        public const float MendCore = 0.25f, BeyondHeal = 1.03f;

        public static bool IsTurret(PieceType t) => (t >= PieceType.Gun && t <= PieceType.Tesla) || t >= PieceType.Flame;
        public static bool NeedsBlueprint(PieceType t) => t >= PieceType.Flame && t != PieceType.Mender;
        public static bool Shoots(PieceType t) => IsTurret(t) && t != PieceType.Bubble && t != PieceType.Mender;
        public static bool HasBranches(PieceType t) => IsTurret(t);
        // Every turret (art pass 5, tools/t5_<turret>.py) is modular: a turntable "Body" on the aimed Head and a swappable
        // class module carrying the branch's silhouette.
        public static bool HasModules(PieceType t) => IsTurret(t);
        public static string BodyModel(PieceType t) => t + "Body";
        public static string ModuleModel(PieceType t, int branch)
        {
            if (!HasModules(t)) return null;
            if (branch <= 0) return t + "Default";
            return branch <= 2 ? t + (branch == 1 ? "A" : "B")
                               : t + (FirstOf(branch) == 1 ? "A" : "B") + SecondOf(branch);
        }
        // The extra gun a turret grows at Lv5/10/15 (its own kind of barrel, not a copy of the whole head).
        public static string BarrelModel(PieceType t) => t + "Barrel";

        // Art pass 5 (tools/t5_<turret>.py, Owen's reference sheets): a turret is a static plinth (<T>Base, it doesn't turn
        // with the aim), the head on the plinth's top (<T>Body, the turntable, and the class head), one more piece round the
        // plinth at each of Lv2..Lv8 (<T>Kit2..Kit8: iron caps, brass band, supplies, armour, a second supply, its pennant,
        // its crest) and from Lv9 its final class's own dressing (<T>DressA1..B2), which takes the supplies' places.
        public static string BaseModel(PieceType t) => t + "Base";
        public const int FirstKit = 2, LastKit = 8;
        public static bool Dressed(int level, int branch) => level >= SecondBranchLevel && branch >= 3;
        public static string DressModel(PieceType t, int branch) => t + "Dress" + (FirstOf(branch) == 1 ? "A" : "B") + SecondOf(branch);
        public static List<string> TurretKit(PieceType t, int level, int branch)
        {
            var kit = new List<string>();
            bool dressed = Dressed(level, branch);
            for (int n = FirstKit; n <= Mathf.Min(level, LastKit); n++)
                if (!(dressed && (n == 4 || n == 6))) kit.Add(t + "Kit" + n);
            if (dressed) kit.Add(DressModel(t, branch));
            return kit;
        }
        // The head grows a little with every level up to Lv13 (the pieces round the plinth show the rest).
        public static float HeadScale(int level) => 1f + 0.025f * Mathf.Clamp(level - 1, 0, 12);

        // The numbers each turret's model was built round (tools/layout5/<T>.json; tests/Headless checks they agree): the
        // head pivot on top of the plinth, the muzzle at Lv1, and where the extra barrels stand on the turntable (head space:
        // x either side, up, forward), the first pair at BarrelMount and each further pair one BarrelStep on.
        public static float HeadHeight(PieceType t)
        {
            switch (t)
            {
                case PieceType.Gun: return 1.1f;
                case PieceType.Cannon: return 0.78f;
                case PieceType.Harpoon: return 0.74f;
                case PieceType.Mortar: return 0.8f;
                case PieceType.Tesla: return 0.52f;
                case PieceType.Flame: return 0.56f;
                case PieceType.Sniper: return 1.12f;
                case PieceType.Bubble: return 0.46f;
                default: return 0.76f; // Mender
            }
        }
        public static float MuzzleHeight(PieceType t)
        {
            switch (t)
            {
                case PieceType.Gun: return 1.424f;
                case PieceType.Cannon: return 1.332f;
                case PieceType.Harpoon: return 1.364f;
                case PieceType.Mortar: return 1.71f;
                case PieceType.Tesla: return 1.756f;
                case PieceType.Flame: return 1.064f;
                case PieceType.Sniper: return 1.48f;
                case PieceType.Bubble: return 1.022f;
                default: return 1.4f; // Mender
            }
        }
        // Where the shot leaves at this level (the head, muzzle and all, grows with HeadScale).
        public static float MuzzleHeight(PieceType t, int level) => HeadHeight(t) + (MuzzleHeight(t) - HeadHeight(t)) * HeadScale(level);
        public static Vector3 BarrelMount(PieceType t)
        {
            switch (t)
            {
                case PieceType.Gun: return new Vector3(0.372f, 0.192f, 0.144f);
                case PieceType.Cannon: return new Vector3(0.432f, 0.096f, -0.132f);
                case PieceType.Harpoon: return new Vector3(0.36f, 0.096f, -0.144f);
                case PieceType.Mortar: return new Vector3(0.396f, 0.06f, 0.144f);
                case PieceType.Tesla: return new Vector3(0.24f, 0.06f, 0.06f);
                case PieceType.Flame: return new Vector3(0.324f, 0.096f, 0.06f);
                case PieceType.Sniper: return new Vector3(0.282f, 0.084f, 0.024f);
                default: return Vector3.zero; // no extra barrels
            }
        }
        public static Vector3 BarrelStep(PieceType t)
        {
            switch (t)
            {
                case PieceType.Gun: return new Vector3(0f, 0.156f, 0f);
                case PieceType.Cannon: return new Vector3(0f, 0.18f, 0f);
                case PieceType.Harpoon: return new Vector3(0f, 0.156f, 0f);
                case PieceType.Mortar: return new Vector3(0f, 0f, -0.192f);
                case PieceType.Tesla: return new Vector3(0f, 0f, -0.24f);
                case PieceType.Flame: return new Vector3(0f, 0.144f, 0f);
                case PieceType.Sniper: return new Vector3(0f, 0.12f, 0f);
                default: return Vector3.zero;
            }
        }
        public static bool BarrelMirror(PieceType t) => false;
        // How many barrels the volley physically has (and how many fish/shells it fires at once): the model follows the stat, so
        // an upgrade that adds a shot also grows a barrel (Owen: "要跟他同時可以射幾發有關").
        public static int Barrels(PieceType t, int level, int branch)
        {
            switch (t)
            {
                case PieceType.Gun: case PieceType.Sniper: return Mathf.Max(1, Turret(t, level, branch).Targets);
                case PieceType.Cannon: case PieceType.Harpoon: case PieceType.Mortar: return Mathf.Max(1, Turret(t, level, branch).Shots);
                case PieceType.Tesla: case PieceType.Flame: return 1 + Milestones(level);
                default: return 1; // bubble/mender: no barrels
            }
        }
        public static int BuildCost(PieceType t) => Siege.Money(BaseCost(t)); // in the game's dollars

        private static int BaseCost(PieceType t)
        {
            switch (t)
            {
                case PieceType.Wall: return 10;
                case PieceType.Floor: case PieceType.Ramp: return 8;
                case PieceType.Gun: return 50;
                case PieceType.Cannon: return 80;
                case PieceType.Harpoon: return 70;
                case PieceType.Mortar: return 110;
                case PieceType.Tesla: return 90;
                case PieceType.Flame: return 100;
                case PieceType.Sniper: return 120;
                case PieceType.Bubble: return 130;
                case PieceType.Mender: return 120;
                default: return 25; // Spikes
            }
        }

        // Levels past the tables, and what they multiply (capped so nothing overflows at absurd levels).
        public static int Beyond(int level) => Mathf.Max(0, level - BranchLevel);
        private static float Grow(float per, int level) => Mathf.Min(1e6f, Mathf.Pow(per, Beyond(level)));
        private static int Table(int level) => Mathf.Clamp(level, 1, BranchLevel) - 1;

        // Cost to go from `level` to level + 1: base x level up to Lv3, then x1.35 per level (matched to the x1.3 damage growth).
        public static int UpgradeCost(PieceType t, int level)
        {
            int baseCost;
            switch (t)
            {
                case PieceType.Wall: baseCost = 15; break;
                case PieceType.Gun: baseCost = 40; break;
                case PieceType.Cannon: baseCost = 60; break;
                case PieceType.Harpoon: baseCost = 50; break;
                case PieceType.Mortar: baseCost = 80; break;
                case PieceType.Tesla: baseCost = 70; break;
                case PieceType.Spikes: baseCost = 15; break;
                case PieceType.Flame: baseCost = 70; break;
                case PieceType.Sniper: baseCost = 80; break;
                case PieceType.Bubble: baseCost = 90; break;
                case PieceType.Mender: baseCost = 80; break;
                default: baseCost = 10; break;
            }
            float coins = level < BranchLevel ? baseCost * level : baseCost * BranchLevel * Mathf.Pow(BeyondCost, level - BranchLevel);
            return Siege.Money(Mathf.RoundToInt(Mathf.Min(coins, 1e8f)));
        }

        // Repairing back to full costs 1 coin's worth per 10 missing HP (at least 1): the playtest found repairs ate the money.
        public static int RepairCost(int hp, int maxHp) => Siege.Money(Mathf.Max(1, Mathf.CeilToInt((maxHp - hp) / 10f)));

        public static int MaxHp(PieceType t, int level)
        {
            int i = Table(level);
            int hp;
            switch (t)
            {
                case PieceType.Wall: hp = new[] { 200, 400, 650 }[i]; break;   // wood, stone, metal
                case PieceType.Floor:
                case PieceType.Ramp: hp = new[] { 100, 200, 350 }[i]; break;
                case PieceType.Spikes: hp = new[] { 120, 200, 300 }[i]; break;
                default: hp = new[] { 300, 450, 600 }[i]; break;               // turrets
            }
            return Mathf.RoundToInt(Mathf.Min(1e8f, hp * Grow(BeyondHp, level)));
        }

        public struct TurretStats
        {
            public int Damage; public float Interval, Range, MinRange, Splash;
            public int Chain, Shots, Targets; public bool Pierce; public float Slow, SlowTime, Burn;
            public float BossMult, WaterMult;
            public float Cone, Shield; // flamethrower half-angle; bubble damage reduction
            public float Heal, CoreMend; // repair tower: share of a building's health a second; the lighthouse's share of that
        }

        // Branch 1 = A, 2 = B (chosen when upgrading to Lv3).
        public static TurretStats Turret(PieceType t, int level, int branch)
        {
            int first = FirstOf(branch), second = SecondOf(branch);
            var s = TableTurret(t, level, first);
            if (second > 0) SecondSplit(t, first, second, ref s); // by ref: from 0.2.9 a copy took the split and was dropped
            if (Beyond(level) == 0) return s;
            float k = Grow(BeyondDamage, level);
            s.Damage = Mathf.RoundToInt(Mathf.Min(1e8f, s.Damage * k));
            if (t != PieceType.Mender) s.Interval = Mathf.Max(0.05f, s.Interval * Mathf.Pow(0.96f, Beyond(level))); // every level also fires faster
            s.Heal *= Grow(BeyondHeal, level);
            s.Burn *= k;
            s.Range += Mathf.Min(BeyondRangeMax, BeyondRange * Beyond(level));
            if (t == PieceType.Bubble) s.Shield = Mathf.Min(0.85f, s.Shield + 0.025f * Beyond(level));
            // Every 5th level (Lv5, Lv10...) the turret gets something new, and another barrel to show it.
            int m = Milestones(level);
            switch (t)
            {
                case PieceType.Gun: case PieceType.Sniper: s.Targets += m; break;                     // one more fish at a time
                case PieceType.Cannon: case PieceType.Mortar: case PieceType.Harpoon: s.Shots += m; break; // one more shell / harpoon
                case PieceType.Tesla: s.Chain += 2 * m; break;
                case PieceType.Flame: s.Cone = Mathf.Min(90f, s.Cone + 10f * m); break;
                case PieceType.Bubble: case PieceType.Mender: s.Range += 2f * m; break;
            }
            return s;
        }

        // Lv5, Lv10, Lv15... reached (capped at 3 extra barrels' worth).
        public static int Milestones(int level) => Mathf.Min(3, level / 5);

        private static TurretStats TableTurret(PieceType t, int level, int branch)
        {
            int first = FirstOf(branch);
            int i = Table(level);
            var s = new TurretStats { Shots = 1, Targets = 1, BossMult = 1f, WaterMult = 1f };
            switch (t)
            {
                case PieceType.Gun:
                    s.Damage = new[] { 6, 9, 13 }[i]; s.Interval = new[] { 0.25f, 0.2f, 0.15f }[i]; s.Range = new[] { 16f, 18f, 20f }[i];
                    if (first == 1) s.Interval *= 0.5f;            // Storm
                    if (first == 2) s.BossMult = 3f;               // Armour piercing: x3 vs tanks/boss
                    break;
                case PieceType.Cannon:
                    s.Damage = new[] { 25, 40, 60 }[i]; s.Interval = new[] { 2f, 1.7f, 1.4f }[i]; s.Range = new[] { 18f, 21f, 24f }[i]; s.Splash = 3f;
                    if (first == 1) { s.Splash = 5f; s.Shots = 3; s.Damage = Mathf.RoundToInt(s.Damage * 0.6f); } // Cluster
                    if (first == 2) s.Damage = Mathf.RoundToInt(s.Damage * 2.5f);                                   // Heavy
                    break;
                case PieceType.Harpoon:
                    s.Damage = new[] { 18, 28, 40 }[i]; s.Interval = new[] { 1.6f, 1.4f, 1.2f }[i]; s.Range = new[] { 20f, 23f, 26f }[i];
                    s.Pierce = true; s.Slow = 0.35f; s.SlowTime = 2f;
                    if (first == 1) s.Shots = 3;                          // Multi
                    if (first == 2) { s.Slow = 0.7f; s.SlowTime = 3.5f; } // Freeze
                    break;
                case PieceType.Mortar:
                    s.Damage = new[] { 35, 55, 80 }[i]; s.Interval = new[] { 3.5f, 3f, 2.6f }[i]; s.Range = new[] { 32f, 36f, 40f }[i]; s.MinRange = 8f; s.Splash = 4f;
                    if (first == 1) s.Burn = 12f;   // Fire: the ground burns for a few seconds
                    if (first == 2) s.Shots = 3;    // Barrage
                    break;
                case PieceType.Flame:
                    s.Damage = new[] { 6, 9, 13 }[i]; s.Interval = new[] { 0.2f, 0.18f, 0.15f }[i]; s.Range = new[] { 7f, 8f, 9f }[i]; s.Cone = 35f;
                    s.Burn = new[] { 5f, 8f, 12f }[i];
                    if (first == 1) s.Burn *= 2f;                          // Inferno
                    if (first == 2) { s.Cone = 60f; s.Range += 2f; }       // Wide
                    break;
                case PieceType.Sniper:
                    s.Damage = new[] { 80, 120, 180 }[i]; s.Interval = new[] { 3f, 2.6f, 2.2f }[i]; s.Range = new[] { 40f, 45f, 50f }[i];
                    if (first == 1) s.BossMult = 2f;                       // Armour-breaker
                    if (first == 2) s.Interval *= 0.6f;                    // Rapid
                    break;
                case PieceType.Bubble: // no attack: buildings within Range take less damage
                    s.Damage = 0; s.Interval = 1f; s.Range = new[] { 8f, 9f, 10f }[i]; s.Shield = new[] { 0.3f, 0.4f, 0.5f }[i];
                    if (first == 1) s.Range += 4f;                         // Expanse
                    if (first == 2) s.Shield = 0.65f;                      // Hardened
                    break;
                case PieceType.Mender: // no attack: every damaged building within Range gets Heal of its health back a second
                    s.Damage = 0; s.Interval = 1f; s.Range = new[] { 7f, 8f, 9f }[i];
                    s.Heal = new[] { 0.008f, 0.01f, 0.012f }[i]; s.CoreMend = MendCore;
                    if (first == 1) s.Range += 4f;                         // Wide
                    if (first == 2) s.Heal *= 1.6f;                        // Surge
                    break;
                case PieceType.Tesla:
                    s.Damage = new[] { 14, 22, 33 }[i]; s.Interval = new[] { 1.2f, 1f, 0.85f }[i]; s.Range = new[] { 11f, 13f, 15f }[i];
                    s.Chain = new[] { 3, 4, 5 }[i]; s.WaterMult = 2f;
                    if (first == 1) s.Chain += 3;                                   // Spread
                    if (first == 2) { s.Damage *= 2; s.Interval *= 1.5f; }           // Overload
                    break;
            }
            return s;
        }

        // The Lv6 split continues its own Lv3 choice (Owen: "沿著第一個選擇延伸，每個要夠獨特"): A1/A2 only along A,
        // B1/B2 only along B, each with its own name and effect.
        private static void SecondSplit(PieceType t, int first, int second, ref TurretStats s)
        {
            switch (t)
            {
                case PieceType.Gun:
                    if (first == 1) { if (second == 1) { s.Interval *= 0.6f; s.Targets++; } else { s.Interval *= 0.72f; s.Slow = 0.35f; s.SlowTime = 1.5f; s.Damage = Mathf.RoundToInt(s.Damage * 0.85f); } }
                    else { if (second == 1) { s.BossMult *= 2f; s.Pierce = true; s.Damage = Mathf.RoundToInt(s.Damage * 1.3f); } else { s.Damage = Mathf.RoundToInt(s.Damage * 1.6f); s.Interval *= 1.2f; } }
                    break;
                case PieceType.Cannon:
                    if (first == 1) { if (second == 1) { s.Shots += 2; s.Splash += 1f; s.Damage = Mathf.RoundToInt(s.Damage * 0.75f); } else { s.Burn = 10f; s.Splash += 1f; } }
                    else { if (second == 1) { s.Damage = Mathf.RoundToInt(s.Damage * 1.4f); s.Splash += 1.5f; s.Interval *= 1.1f; } else { s.Splash *= 2f; s.Slow = 0.4f; s.SlowTime = 1.2f; s.Damage = Mathf.RoundToInt(s.Damage * 0.85f); } }
                    break;
                case PieceType.Harpoon:
                    if (first == 1) { if (second == 1) { s.Shots += 2; s.Damage = Mathf.RoundToInt(s.Damage * 0.8f); } else { s.Chain += 2; s.Pierce = true; } }
                    else { if (second == 1) { s.Slow = 0.9f; s.SlowTime = 5f; s.Range += 2f; } else { s.Damage = Mathf.RoundToInt(s.Damage * 1.6f); s.BossMult = 1.5f; } }
                    break;
                case PieceType.Mortar:
                    if (first == 1) { if (second == 1) { s.Burn *= 2f; s.Splash += 1f; } else { s.Burn = 18f; s.Splash += 2f; s.Interval *= 1.2f; } }
                    else { if (second == 1) { s.Shots += 2; s.Interval *= 0.9f; } else { s.Shots++; s.Splash += 2f; s.Damage = Mathf.RoundToInt(s.Damage * 0.8f); } }
                    break;
                case PieceType.Tesla:
                    if (first == 1) { if (second == 1) { s.Chain += 4; s.Damage = Mathf.RoundToInt(s.Damage * 0.85f); } else { s.WaterMult = 3f; s.Slow = 0.2f; s.SlowTime = 1f; s.Chain++; } }
                    else { if (second == 1) { s.Damage = Mathf.RoundToInt(s.Damage * 1.5f); s.Interval *= 1.1f; } else { s.Splash = 3f; s.Chain += 2; s.Damage = Mathf.RoundToInt(s.Damage * 0.8f); } }
                    break;
                case PieceType.Flame:
                    if (first == 1) { if (second == 1) { s.Burn *= 2f; s.Damage = Mathf.RoundToInt(s.Damage * 1.2f); } else { s.BossMult = 2.5f; s.Burn *= 1.5f; } }
                    else { if (second == 1) { s.Cone += 15f; s.Range += 2f; s.Burn *= 1.2f; } else { s.Slow = 0.4f; s.SlowTime = 2f; s.Burn = Mathf.RoundToInt(s.Burn * 0.8f); } }
                    break;
                case PieceType.Sniper:
                    if (first == 1) { if (second == 1) { s.BossMult *= 3f; s.Damage = Mathf.RoundToInt(s.Damage * 1.2f); } else { s.Pierce = true; s.Range += 8f; s.Damage = Mathf.RoundToInt(s.Damage * 1.1f); } }
                    else { if (second == 1) { s.Interval *= 0.55f; } else { s.Damage = Mathf.RoundToInt(s.Damage * 1.5f); s.Interval *= 1.2f; } }
                    break;
                case PieceType.Bubble:
                    if (first == 1) { if (second == 1) { s.Range += 3f; s.Shield = Mathf.Min(0.85f, s.Shield + 0.1f); } else { s.Shield = Mathf.Min(0.85f, s.Shield + 0.15f); s.Range += 1f; } }
                    else { if (second == 1) { s.Shield = Mathf.Min(0.9f, s.Shield + 0.2f); } else { s.Range += 2f; s.Shield = Mathf.Min(0.9f, s.Shield + 0.1f); } }
                    break;
                case PieceType.Mender:
                    if (first == 1) { if (second == 1) { s.Range += 4f; s.Heal *= 1.3f; } else { s.Range += 2f; s.Heal *= 1.5f; s.CoreMend *= 3f; } } // grand / core mender
                    else { if (second == 1) { s.Heal *= 1.4f; } else { s.Range += 2f; s.Interval *= 0.8f; } }                                   // overheal / cleanse
                    break;
            }
        }

        public static string BranchKey(PieceType t, int branch) => branch == 0 ? null : branch <= 2
            ? t + (branch == 1 ? "A" : "B")
            : t + (FirstOf(branch) == 1 ? "A" : "B") + SecondOf(branch); // GunA1, GunB2...: each side has its own two

        // Collider size of each piece (turrets and spikes sit on the cell, everything else is centred on its grid slot).
        public static Vector3 Size(PieceType t)
        {
            const float c = BuildSystem.Cell, th = 0.3f;
            switch (t)
            {
                case PieceType.Wall: return new Vector3(c, c, th);
                case PieceType.Floor: return new Vector3(c, th, c);
                case PieceType.Ramp: return new Vector3(c, th, c * 1.4142f);
                case PieceType.Spikes: return new Vector3(c, 0.15f, c);
                default: return new Vector3(1.6f, 1.6f, 1.6f);
            }
        }

        // Spikes: damage per tick (0.5 s) to walkers standing on them.
        public static int SpikeDamage(int level) => Mathf.RoundToInt(Mathf.Min(1e8f, new[] { 4, 7, 11 }[Table(level)] * Grow(BeyondDamage, level)));

        // ★★☆ up to Lv3, then ★★★+2 for Lv5 and so on.
        public static string Stars(int level, string dim = "#FFFFFF40") =>
            new string('★', Mathf.Min(level, BranchLevel)) + (level < BranchLevel ? $"<color={dim}>{new string('★', BranchLevel - level)}</color>" : "")
            + (Beyond(level) > 0 ? $"<size=85%>+{Beyond(level)}</size>" : "");
    }
}
