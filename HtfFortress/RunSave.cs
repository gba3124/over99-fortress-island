using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using FishNet;
using UnityEngine;

namespace HtfFortress
{
    // A mid-run checkpoint: the fort as it stood when a wave started (or just before the next one), so a host can quit
    // and pick the run up again. Enemies are deliberately not stored: continue always replays the wave from its start.
    public static class RunSave
    {
        private const int Version = 1;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public struct Snapshot
        {
            public int NextWave, Coins, CoreHp, CoreMax;
            public List<PieceState> Pieces;
        }

        private static string Safe(string save) => string.Concat(save.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        private static string FilePath()
        {
            string save = ManageSaveName();
            if (string.IsNullOrEmpty(save)) return null;
            return Path.Combine(Paths.BepInExRootPath, "fortress-runs", Safe(save) + ".txt");
        }
        private static string ManageSaveName()
        {
            try { return SaveManager.CurServerSave != null ? SaveManager.CurServerSave.Name : null; } catch { return null; }
        }

        public static bool Exists => FilePath() is string p && File.Exists(p);

        public static void Save(int nextWave)
        {
            if (!InstanceFinder.IsServerStarted || !BuildSystem.Instance || !Siege.Instance) return;
            string path = FilePath();
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var sb = new StringBuilder();
                sb.AppendLine("v " + Version);
                sb.AppendLine("next " + nextWave);
                sb.AppendLine("coins " + Siege.Instance.ServerCoins);
                sb.AppendLine("core " + Siege.Instance.CoreHp + " " + Siege.Instance.CoreMax);
                var pieces = BuildSystem.Instance.ServerPieces;
                sb.AppendLine("pieces " + pieces.Count);
                foreach (var p in pieces)
                    sb.AppendLine($"piece {(int)p.Type} {F(p.Pos.x)} {F(p.Pos.y)} {F(p.Pos.z)} {F(p.RotY)} {p.Level} {p.Branch} {p.Hp} {p.MaxHp}");
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e) { BuildSystem.Log.LogWarning("Run checkpoint save failed: " + e.Message); }
        }

        public static bool Load(out Snapshot snap)
        {
            snap = default;
            string path = FilePath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            try
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length < 5 || lines[0].Trim() != "v " + Version) return false;
                snap.Pieces = new List<PieceState>();
                foreach (var raw in lines)
                {
                    var p = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length == 0) continue;
                    switch (p[0])
                    {
                        case "next": snap.NextWave = int.Parse(p[1], Inv); break;
                        case "coins": snap.Coins = int.Parse(p[1], Inv); break;
                        case "core": snap.CoreHp = int.Parse(p[1], Inv); snap.CoreMax = int.Parse(p[2], Inv); break;
                        case "piece":
                            if (p.Length < 10) break;
                            snap.Pieces.Add(new PieceState
                            {
                                Type = (PieceType)int.Parse(p[1], Inv), Pos = new Vector3(F(p[2]), F(p[3]), F(p[4])),
                                RotY = F(p[5]), Level = int.Parse(p[6], Inv), Branch = int.Parse(p[7], Inv),
                                Hp = int.Parse(p[8], Inv), MaxHp = int.Parse(p[9], Inv),
                            });
                            break;
                    }
                }
                return snap.NextWave > 0 && snap.CoreMax > 0;
            }
            catch (Exception e) { BuildSystem.Log.LogWarning("Run checkpoint read failed: " + e.Message); return false; }
        }

        public static void Clear()
        {
            string path = FilePath();
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
            catch (Exception e) { BuildSystem.Log.LogWarning("Run checkpoint delete failed: " + e.Message); }
        }

        private static string F(float v) => v.ToString("0.###", Inv);
        private static float F(string s) => float.Parse(s, Inv);
    }
}
