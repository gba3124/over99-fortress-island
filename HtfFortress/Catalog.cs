using UnityEngine;

namespace HtfFortress
{
    // Flame, Sniper and Bubble are unlocked by blueprints (from chests and bosses).
    public enum PieceType : byte { Wall, Floor, Ramp, Gun, Cannon, Harpoon, Mortar, Tesla, Spikes, Flame, Sniper, Bubble }

    // Every buildable thing's cost, health, turret stats and Lv3 branches. Tune balance here.
    public static class Catalog
    {
        public const int MaxLevel = 3;
        public const int TypeCount = 12;

        public static bool IsTurret(PieceType t) => (t >= PieceType.Gun && t <= PieceType.Tesla) || t >= PieceType.Flame;
        public static bool NeedsBlueprint(PieceType t) => t >= PieceType.Flame;
        public static bool HasBranches(PieceType t) => IsTurret(t);
        public static string HeadModel(PieceType t) => t + "Head";

        public static int BuildCost(PieceType t)
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
                default: return 25; // Spikes
            }
        }

        // Cost to go from `level` to level + 1.
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
                default: baseCost = 10; break;
            }
            return baseCost * level;
        }

        // Repairing back to full costs 1 coin per 5 missing HP (at least 1).
        public static int RepairCost(int hp, int maxHp) => Mathf.Max(1, Mathf.CeilToInt((maxHp - hp) / 5f));

        public static int MaxHp(PieceType t, int level)
        {
            int i = Mathf.Clamp(level, 1, MaxLevel) - 1;
            switch (t)
            {
                case PieceType.Wall: return new[] { 200, 400, 650 }[i];   // wood, stone, metal
                case PieceType.Floor:
                case PieceType.Ramp: return new[] { 100, 200, 350 }[i];
                case PieceType.Spikes: return new[] { 120, 200, 300 }[i];
                default: return new[] { 300, 450, 600 }[i];               // turrets
            }
        }

        public struct TurretStats
        {
            public int Damage; public float Interval, Range, MinRange, Splash;
            public int Chain, Shots; public bool Pierce; public float Slow, SlowTime, Burn;
            public float BossMult, WaterMult;
            public float Cone, Shield; // flamethrower half-angle; bubble damage reduction
        }

        // Branch 1 = A, 2 = B (chosen when upgrading to Lv3).
        public static TurretStats Turret(PieceType t, int level, int branch)
        {
            int i = Mathf.Clamp(level, 1, MaxLevel) - 1;
            var s = new TurretStats { Shots = 1, BossMult = 1f, WaterMult = 1f };
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
                    s.Damage = new[] { 4, 6, 9 }[i]; s.Interval = new[] { 0.2f, 0.18f, 0.15f }[i]; s.Range = new[] { 7f, 8f, 9f }[i]; s.Cone = 35f;
                    s.Burn = new[] { 3f, 5f, 8f }[i];
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
                case PieceType.Tesla:
                    s.Damage = new[] { 10, 16, 24 }[i]; s.Interval = new[] { 1.2f, 1f, 0.85f }[i]; s.Range = new[] { 10f, 12f, 14f }[i];
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
        public static int SpikeDamage(int level) => new[] { 4, 7, 11 }[Mathf.Clamp(level, 1, MaxLevel) - 1];
    }
}
