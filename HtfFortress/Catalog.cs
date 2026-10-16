using UnityEngine;

namespace HtfFortress
{
    public enum PieceType : byte { Wall, Floor, Ramp, Cannon, Gun }

    // Every buildable thing's cost, health and turret stats, by level (1..MaxLevel). Tune balance here.
    public static class Catalog
    {
        public const int MaxLevel = 3;
        public const int TypeCount = 5;

        public static bool IsTurret(PieceType t) => t == PieceType.Cannon || t == PieceType.Gun;

        public static int BuildCost(PieceType t)
        {
            switch (t)
            {
                case PieceType.Wall: return 10;
                case PieceType.Floor: return 8;
                case PieceType.Ramp: return 8;
                case PieceType.Cannon: return 80;
                default: return 50; // Gun
            }
        }

        // Cost to go from `level` to level + 1.
        public static int UpgradeCost(PieceType t, int level)
        {
            int baseCost = t == PieceType.Wall ? 15 : t == PieceType.Cannon ? 60 : t == PieceType.Gun ? 40 : 10;
            return baseCost * level;
        }

        public static int MaxHp(PieceType t, int level)
        {
            int i = Mathf.Clamp(level, 1, MaxLevel) - 1;
            switch (t)
            {
                case PieceType.Wall: return new[] { 150, 300, 500 }[i];   // wood, stone, metal
                case PieceType.Floor:
                case PieceType.Ramp: return new[] { 100, 200, 350 }[i];
                default: return new[] { 200, 300, 400 }[i];               // turrets
            }
        }

        public struct TurretStats { public int Damage; public float Interval; public float Range; public float Splash; }

        public static TurretStats Turret(PieceType t, int level)
        {
            int i = Mathf.Clamp(level, 1, MaxLevel) - 1;
            if (t == PieceType.Cannon)
                return new TurretStats { Damage = new[] { 25, 40, 60 }[i], Interval = new[] { 2f, 1.7f, 1.4f }[i], Range = new[] { 18f, 21f, 24f }[i], Splash = 3f };
            return new TurretStats { Damage = new[] { 4, 6, 9 }[i], Interval = new[] { 0.25f, 0.2f, 0.15f }[i], Range = new[] { 14f, 16f, 18f }[i], Splash = 0f };
        }

        // Collider size of each piece (turrets sit on the cell, everything else is centred on its grid slot).
        public static Vector3 Size(PieceType t)
        {
            const float c = BuildSystem.Cell, th = 0.3f;
            switch (t)
            {
                case PieceType.Wall: return new Vector3(c, c, th);
                case PieceType.Floor: return new Vector3(c, th, c);
                case PieceType.Ramp: return new Vector3(c, th, c * 1.4142f);
                default: return new Vector3(1.6f, 1.6f, 1.6f);
            }
        }
    }
}
