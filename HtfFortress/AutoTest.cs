using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace HtfFortress
{
    // Unattended playtest. Runs only when BepInEx/fortress-autotest.txt exists (it is deleted on start, so it is one-shot).
    // The file's first line picks the scenario. It hosts an offline game on a dedicated "FortressTest" save,
    // plays the scenario, writes BepInEx/fortress-test-report.txt plus screenshots, and quits the game.
    public class AutoTest : MonoBehaviour
    {
        public static bool Running { get; private set; }

        // Counters the rest of the mod bumps so a run can be judged from the report.
        public static readonly Dictionary<string, int> Counters = new Dictionary<string, int>();
        public static void Count(string key, int n = 1) { if (Running) { Counters.TryGetValue(key, out int v); Counters[key] = v + n; } }

        private readonly StringBuilder _report = new StringBuilder();
        private string _scenario;
        private float _t0;

        private static string Root => Paths.BepInExRootPath;

        private void Start()
        {
            string trigger = Path.Combine(Root, "fortress-autotest.txt");
            if (!File.Exists(trigger)) return;
            _scenario = File.ReadAllLines(trigger).FirstOrDefault()?.Trim() ?? "probe";
            File.Delete(trigger);
            Running = true;
            FortressIsland.ForceForTest = true;
            Application.logMessageReceived += OnLog;
            StartCoroutine(Run());
        }

        private void Note(string line)
        {
            string s = $"[{Time.realtimeSinceStartup - _t0,6:0.0}s] {line}";
            _report.AppendLine(s);
            BuildSystem.Log.LogInfo("AutoTest " + s);
        }

        private void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
            {
                Count("errors");
                if (Counters["errors"] <= 15) _report.AppendLine("  ! " + type + ": " + msg.Split('\n')[0] + " @ " + stack.Split('\n').FirstOrDefault());
            }
        }

        private IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < end)
            {
                bool ok;
                try { ok = cond(); } catch { ok = false; }
                if (ok) yield break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Note("TIMEOUT waiting for " + what);
        }

        private IEnumerator Run()
        {
            _t0 = Time.realtimeSinceStartup;
            Note("scenario = " + _scenario);
            yield return Until(() => ConnectionManager.Instance != null && Time.realtimeSinceStartup - _t0 > 6f, 60f, "main menu");

            // Host an offline game on our own save so the player's worlds are never touched.
            try
            {
                SaveManager.LoadAllServers();
                if (SaveManager.ServerSaves.Any(s => s.Name == "FortressTest")) SaveManager.SelectServer("FortressTest");
                else SaveManager.CreateServer("FortressTest", false, false, Difficulty.Default);
                ConnectionManager.Instance.CreateOfflineLobby();
                SaveManager.OnServerLoaded();
                Note("hosting offline lobby on save FortressTest");
            }
            catch (Exception e) { Note("FAILED to host: " + e); }

            yield return Until(() => Player.LocalPlayer && Player.LocalPlayer.Transform && IslandManager.IsInitialized && !IslandManager.IsLoading && Island.CurIsland, 400f, "world + local player");
            yield return new WaitForSecondsRealtime(6f);
            Probe();
            FortressPlugin.Shot("test_spawn");
            yield return new WaitForSecondsRealtime(2f);

            switch (_scenario)
            {
                case "probe": break;
                case "siege": yield return SiegeScenario(); break;
                default: yield return IslandScenario(); break;
            }

            Finish();
        }

        private void Probe()
        {
            var p = Player.LocalPlayer.Transform.position;
            Note($"player at {p}, water there {WaterManager.GetWaterHeight(p):0.00}, ground layer mask {GameInfo.LevelLayer.value}, piece layer {BuildSystem.PieceLayer}");
            Note($"current island '{Island.CurIsland.name}' at {Island.IslandPos} size {Island.IslandSize}; CurIsland idx {OnlineIslandManager.CurIsland}, max unlocked {OnlineIslandManager.MaxIslandUnlocked}, total {IslandManager.TotalIslands}");
            for (int i = 0; i < 12; i++)
            {
                IslandInfo info = null;
                try { info = IslandManager.GetIslandInfo(i); } catch { }
                if (info == null) break;
                string live = "";
                try { live = info.IslandPosition.ToString(); } catch { live = "n/a"; }
                Note($"  island info {i}: spawn {live}, archived {info.ArchivedIslandPosition}");
            }
            Note("final boss killed: " + (NPCManager.Instance ? NPCManager.Instance.FinalBossKilled.ToString() : "no NPCManager"));
            Models.Make(Color.white); // logs the material template and its properties
        }

        // Fortress round where the player stands: pre-built ring of walls + turrets, waves start immediately.
        private IEnumerator SiegeScenario()
        {
            var plugin = FortressPlugin.Instance;
            plugin.StartRound(Player.LocalPlayer.Transform.position);
            yield return new WaitForSecondsRealtime(1f);
            Vector3 core = Siege.Instance.CorePos;
            Note($"round started, core at {core}");

            // Ring of walls two cells out, a gun and a cannon inside.
            float c = BuildSystem.Cell;
            Vector3 cell = new Vector3((Mathf.Floor(core.x / c) + 0.5f) * c, Mathf.Floor((core.y + 0.25f) / c) * c, (Mathf.Floor(core.z / c) + 0.5f) * c);
            int placed = 0;
            for (int i = -1; i <= 1; i++)
            {
                placed += BuildSystem.Instance.ServerPlace(PieceType.Wall, cell + new Vector3(i * c, c * 0.5f, 2.5f * c), 0f) ? 1 : 0;
                placed += BuildSystem.Instance.ServerPlace(PieceType.Wall, cell + new Vector3(i * c, c * 0.5f, -1.5f * c), 0f) ? 1 : 0;
                placed += BuildSystem.Instance.ServerPlace(PieceType.Wall, cell + new Vector3(2.5f * c, c * 0.5f, i * c), 90f) ? 1 : 0;
                placed += BuildSystem.Instance.ServerPlace(PieceType.Wall, cell + new Vector3(-1.5f * c, c * 0.5f, i * c), 90f) ? 1 : 0;
            }
            placed += BuildSystem.Instance.ServerPlace(PieceType.Gun, cell + new Vector3(c, 0.2f, 0f), 0f) ? 1 : 0;
            placed += BuildSystem.Instance.ServerPlace(PieceType.Cannon, cell + new Vector3(-c, 0.2f, c), 0f) ? 1 : 0;
            Note($"placed {placed} pieces, coins left {Siege.Instance.ServerCoins}");

            // Look at the core from above-behind for the screenshots.
            Player.LocalPlayer.LocalTeleport(core + new Vector3(0f, 0.5f, -9f), 0f, true);
            yield return new WaitForSecondsRealtime(2f);
            FortressPlugin.Shot("test_built");

            plugin.SkipBuildTime();
            float end = Time.realtimeSinceStartup + 150f;
            int lastWave = 0;
            while (Time.realtimeSinceStartup < end && plugin.Running)
            {
                if (plugin.Wave != lastWave) { lastWave = plugin.Wave; Note($"wave {lastWave} spawned: {FortressPlugin.Alive.Count} alive, core {Siege.Instance.CoreHp}"); }
                if (plugin.InBuildTime && plugin.Wave >= 3) break;
                if (plugin.InBuildTime) plugin.SkipBuildTime();
                yield return new WaitForSecondsRealtime(1f);
                if (Mathf.FloorToInt(Time.realtimeSinceStartup) % 12 == 0) FortressPlugin.Shot("test_fight");
            }
            Note($"end of fight: wave {plugin.Wave}, alive {FortressPlugin.Alive.Count}, core {Siege.Instance.CoreHp}, coins {Siege.Instance.ServerCoins}, running {plugin.Running}");
            FortressPlugin.Shot("test_end");
            yield return new WaitForSecondsRealtime(2f);
        }

        private IEnumerator Look(Vector3 pos, float yaw, string shot, float settle = 1.5f)
        {
            Player.LocalPlayer.LocalTeleport(pos, yaw, true);
            yield return new WaitForSecondsRealtime(settle);
            if (shot != null) FortressPlugin.Shot(shot);
            yield return new WaitForSecondsRealtime(0.5f);
        }

        private static float YawTo(Vector3 from, Vector3 to) { var d = to - from; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }

        // A wall on the cell edge at `world`, facing `outward` (snapped to the build grid like the player's ghost).
        private static bool WallAt(Vector3 world, Vector3 outward)
        {
            float c = BuildSystem.Cell;
            int cx = Mathf.FloorToInt(world.x / c), cz = Mathf.FloorToInt(world.z / c);
            float baseY = Mathf.FloorToInt((world.y + 0.25f) / c) * c;
            if (Mathf.Abs(outward.x) >= Mathf.Abs(outward.z))
                return BuildSystem.Instance.ServerPlace(PieceType.Wall, new Vector3((cx + (outward.x > 0 ? 1 : 0)) * c, baseY + c * 0.5f, (cz + 0.5f) * c), 90f);
            return BuildSystem.Instance.ServerPlace(PieceType.Wall, new Vector3((cx + 0.5f) * c, baseY + c * 0.5f, (cz + (outward.z > 0 ? 1 : 0)) * c), 0f);
        }

        private static bool TurretAt(PieceType t, Vector3 world)
        {
            float c = BuildSystem.Cell;
            var p = new Vector3((Mathf.Floor(world.x / c) + 0.5f) * c, world.y + 5f, (Mathf.Floor(world.z / c) + 0.5f) * c);
            if (Physics.Raycast(p, Vector3.down, out var rh, 12f, GameInfo.LevelLayer)) p.y = rh.point.y; else p.y = world.y;
            return BuildSystem.Instance.ServerPlace(t, p, 0f);
        }

        // The real flow: sail-in view, ready circle, build at the gaps, fight, results.
        private IEnumerator IslandScenario()
        {
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            Note($"fortress island at {island.Center}, core {island.CorePos}");
            Vector3 center = island.Center;

            yield return Look(island.DockPos, YawTo(island.DockPos, center), "isl_dock", 3f);
            Vector3 high = center + new Vector3(-16f, 8f, -16f);
            yield return Look(high, YawTo(high, center), "isl_overview", 2f);

            // Ready circle -> countdown -> round.
            Player.LocalPlayer.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return new WaitForSecondsRealtime(2f);
            FortressPlugin.Shot("isl_ready");
            yield return Until(() => FortressPlugin.Instance.Running, 15f, "round start from ready circle");
            Note("round running: " + FortressPlugin.Instance.Running);
            if (!FortressPlugin.Instance.Running) yield break;

            Siege.Instance.ServerAddCoins(2000);
            int placed = 0;
            foreach (float g in FortressIsland.GapAngles)
            {
                Vector3 dir = new Vector3(Mathf.Cos(g * Mathf.Deg2Rad), 0f, Mathf.Sin(g * Mathf.Deg2Rad));
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                for (int k = -1; k <= 1; k++)
                    placed += WallAt(center + dir * 15f + side * (k * BuildSystem.Cell) + Vector3.up * 1.2f, dir) ? 1 : 0;
                placed += TurretAt(PieceType.Gun, center + dir * 9f + Vector3.up * 1.2f) ? 1 : 0;
            }
            placed += TurretAt(PieceType.Cannon, center + new Vector3(4f, 1.45f, 3f)) ? 1 : 0;
            Note($"placed {placed} pieces, coins {Siege.Instance.ServerCoins}");
            yield return Look(high, YawTo(high, center), "isl_built", 2f);

            var plugin = FortressPlugin.Instance;
            plugin.SkipBuildTime();
            float end = Time.realtimeSinceStartup + 200f;
            int lastWave = 0, view = 0;
            while (Time.realtimeSinceStartup < end && plugin.Running)
            {
                if (plugin.Wave != lastWave) { lastWave = plugin.Wave; Note($"wave {lastWave}: {FortressPlugin.Alive.Count} enemies, core {Siege.Instance.CoreHp}"); }
                if (plugin.InBuildTime && plugin.Wave >= 3) break;
                if (plugin.InBuildTime) plugin.SkipBuildTime();
                // Watch a gap from behind its wall.
                float g = FortressIsland.GapAngles[view++ % 3] * Mathf.Deg2Rad;
                Vector3 eye = center + new Vector3(Mathf.Cos(g), 0f, Mathf.Sin(g)) * 8f + Vector3.up * 3.5f;
                yield return Look(eye, YawTo(eye, center + new Vector3(Mathf.Cos(g), 0f, Mathf.Sin(g)) * 25f), "isl_fight", 6f);
            }
            Note($"fight over: wave {plugin.Wave}, enemies {FortressPlugin.Alive.Count}, core {Siege.Instance.CoreHp}, running {plugin.Running}");
            if (plugin.Running) { Siege.Instance.ServerDamageCoreForTest(100000); yield return new WaitForSecondsRealtime(1.5f); }
            yield return Look(center + new Vector3(0f, 2.5f, -12f), 0f, "isl_results", 2f);
        }

        private void Finish()
        {
            Note("counters: " + string.Join(", ", Counters.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value)));
            File.WriteAllText(Path.Combine(Root, "fortress-test-report.txt"), _report.ToString());
            StartCoroutine(QuitSoon());
        }

        private IEnumerator QuitSoon()
        {
            yield return new WaitForSecondsRealtime(3f);
            Application.Quit();
        }
    }
}
