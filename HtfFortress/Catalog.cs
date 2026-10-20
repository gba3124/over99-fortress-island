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
        public const float BeyondDamage = 1.2f, BeyondHp = 1.15f, BeyondCost = 1.5f, BeyondRange = 0.4f, BeyondRangeMax = 4f;
        public const int TypeCount = 13;
        public const float MendCore = 0.5f; // a repair tower gives the lighthouse this share of what it gives a building

        public static bool IsTurret(PieceType t) => (t >= PieceType.Gun && t <= PieceType.Tesla) || t >= PieceType.Flame;
        public static bool NeedsBlueprint(PieceType t) => t >= PieceType.Flame && t != PieceType.Mender;
        public static bool Shoots(PieceType t) => IsTurret(t) && t != PieceType.Bubble && t != PieceType.Mender;
        public static bool HasBranches(PieceType t) => IsTurret(t);
        public static string HeadModel(PieceType t) => t + "Head";

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

        // Cost to go from `level` to level + 1: base x level up to Lv3, then x1.5 per level.
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
        }

        // Branch 1 = A, 2 = B (chosen when upgrading to Lv3).
        public static TurretStats Turret(PieceType t, int level, int branch)
        {
            var s = TableTurret(t, level, branch);
            if (Beyond(level) == 0) return s;
            float k = Grow(BeyondDamage, level);
            s.Damage = Mathf.RoundToInt(Mathf.Min(1e8f, s.Damage * k));
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
            int i = Table(level);
            var s = new TurretStats { Shots = 1, Targets = 1, BossMult = 1f, WaterMult = 1f };
            switch (t)
            {
                case PieceType.Gun:
                    s.Damage = new[] { 6, 9, 13 }[i]; s.Interval = new[] { 0.25f, 0.2f, 0.15f }[i]; s.Range = new[] { 16f, 18f, 20f }[i];
                    if (branch == 1) s.Interval *= 0.5f;            // Storm
                    if (branch == 2) s.BossMult = 3f;               // Armour piercing: x3 vs tanks/boss
                    break;
                case PieceType.Cannon:
                    s.Damage = new[] { 25, 40, 60 }[i]; s.Interval = new[] { 2f, 1.7f, 1.4f }[i]; s.Range = new[] { 18f, 21f, 24f }[i]; s.Splash = 3f;
                    if (branch == 1) { s.Splash = 5f; s.Shots = 3; s.Damage = Mathf.RoundToInt(s.Damage * 0.6f); } // Cluster
                    if (branch == 2) s.Damage = Mathf.RoundToInt(s.Damage * 2.5f);                                   // Heavy
                    break;
                case PieceType.Harpoon:
                    s.Damage = new[] { 18, 28, 40 }[i]; s.Interval = new[] { 1.6f, 1.4f, 1.2f }[i]; s.Range = new[] { 20f, 23f, 26f }[i];
                    s.Pierce = true; s.Slow = 0.35f; s.SlowTime = 2f;
                    if (branch == 1) s.Shots = 3;                          // Multi
                    if (branch == 2) { s.Slow = 0.7f; s.SlowTime = 3.5f; } // Freeze
                    break;
                case PieceType.Mortar:
                    s.Damage = new[] { 35, 55, 80 }[i]; s.Interval = new[] { 3.5f, 3f, 2.6f }[i]; s.Range = new[] { 32f, 36f, 40f }[i]; s.MinRange = 8f; s.Splash = 4f;
                    if (branch == 1) s.Burn = 12f;   // Fire: the ground burns for a few seconds
                    if (branch == 2) s.Shots = 3;    // Barrage
                    break;
                case PieceType.Flame:
                    s.Damage = new[] { 6, 9, 13 }[i]; s.Interval = new[] { 0.2f, 0.18f, 0.15f }[i]; s.Range = new[] { 7f, 8f, 9f }[i]; s.Cone = 35f;
                    s.Burn = new[] { 5f, 8f, 12f }[i];
                    if (branch == 1) s.Burn *= 2f;                          // Inferno
                    if (branch == 2) { s.Cone = 60f; s.Range += 2f; }       // Wide
                    break;
                case PieceType.Sniper:
                    s.Damage = new[] { 80, 120, 180 }[i]; s.Interval = new[] { 3f, 2.6f, 2.2f }[i]; s.Range = new[] { 40f, 45f, 50f }[i];
                    if (branch == 1) s.BossMult = 2f;                       // Armour-breaker
                    if (branch == 2) s.Interval *= 0.6f;                    // Rapid
                    break;
                case PieceType.Bubble: // no attack: buildings within Range take less damage
                    s.Damage = 0; s.Interval = 1f; s.Range = new[] { 8f, 9f, 10f }[i]; s.Shield = new[] { 0.3f, 0.4f, 0.5f }[i];
                    if (branch == 1) s.Range += 4f;                         // Expanse
                    if (branch == 2) s.Shield = 0.65f;                      // Hardened
                    break;
                case PieceType.Mender: // no attack: once a second every damaged building within Range gets Damage health back, the lighthouse MendCore of it
                    s.Damage = new[] { 5, 8, 12 }[i]; s.Interval = 1f; s.Range = new[] { 7f, 8f, 9f }[i];
                    if (branch == 1) s.Range += 4f;                         // Wide
                    if (branch == 2) s.Damage *= 2;                         // Surge
                    break;
                case PieceType.Tesla:
                    s.Damage = new[] { 14, 22, 33 }[i]; s.Interval = new[] { 1.2f, 1f, 0.85f }[i]; s.Range = new[] { 11f, 13f, 15f }[i];
                    s.Chain = new[] { 3, 4, 5 }[i]; s.WaterMult = 2f;
                    if (branch == 1) s.Chain += 3;                                   // Spread
                    if (branch == 2) { s.Damage *= 2; s.Interval *= 1.5f; }           // Overload
                    break;
            }
            return s;
        }

        public static string BranchKey(PieceType t, int branch) => branch == 0 ? null : t + (branch == 1 ? "A" : "B");

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
