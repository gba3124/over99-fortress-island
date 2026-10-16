using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using K = UnityEngine.InputSystem.Key;

namespace HtfFortress
{
    // Unattended playtest. Runs only when BepInEx/fortress-autotest.txt exists (it is deleted on start, so it is one-shot).
    // The file's first line picks the scenario. It hosts an offline game on a dedicated "FortressTest" save,
    // plays the scenario, writes BepInEx/fortress-test-report.txt plus screenshots, and quits the game.
    public class AutoTest : MonoBehaviour
    {
        public static bool Running { get; private set; }
        public static bool IgnorePlayers;
        public static bool ForceGameItem;

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
                case "enemies": yield return EnemiesScenario(); break;
                case "build": yield return BuildScenario(); break;
                case "balance": yield return BalanceScenario(); break;
                case "boss": yield return BossScenario(); break;
                case "coop": yield return CoopScenario(); break;
                case "cheats": yield return CheatsScenario(); break;
                case "loot": yield return LootScenario(); break;
                case "gear": yield return GearScenario(); break;
                case "ui": yield return UiScenario(); break;
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
            var clips = AccessTools.Field(typeof(AudioManager), "_realClips")?.GetValue(null) as System.Collections.IDictionary;
            if (clips != null) File.WriteAllText(Path.Combine(Root, "fortress-sounds.txt"), string.Join("\n", clips.Keys.Cast<object>().Select(k => k.ToString()).OrderBy(k => k)));
            Note("sound clips: " + (clips?.Count ?? -1));
            // Every non-creature spawnable: name | type | worth | components (for picking chest rewards).
            var items = ((Dictionary<string, Item>)AccessTools.Field(typeof(GameInfo), "_nameToSpawnable").GetValue(null))
                .Where(kv => kv.Value && !kv.Value.GetComponent<Creature>()).OrderBy(kv => kv.Key)
                .Select(kv => $"{kv.Key}|{kv.Value.Type}|{kv.Value.DefaultWorth}|{string.Join(",", kv.Value.GetComponents<MonoBehaviour>().Select(m => m.GetType().Name).Where(n => n != "Item" && !n.StartsWith("Network")).Distinct())}");
            File.WriteAllText(Path.Combine(Root, "fortress-items.txt"), string.Join("\n", items));
            // The game's own UI sprites and fonts (to build our UI from the same pieces).
            string PathOf(Transform t) { var parts = new List<string>(); for (; t; t = t.parent) parts.Add(t.name); parts.Reverse(); return string.Join("/", parts); }
            var ui = Resources.FindObjectsOfTypeAll<UnityEngine.UI.Image>().Where(i => i.sprite)
                .Select(i => $"{i.sprite.name}|{i.sprite.rect.width}x{i.sprite.rect.height}|border {i.sprite.border}|{i.type}|{i.color}|{PathOf(i.transform)}")
                .Distinct().OrderBy(x => x);
            var fonts = Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>().Select(f => "FONT " + f.name);
            File.WriteAllText(Path.Combine(Root, "fortress-ui.txt"), string.Join("\n", fonts.Concat(ui)));
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

        // Stand on whatever is under `pos` (never drop the player into the sea) and take a screenshot.
        private IEnumerator Look(Vector3 pos, float yaw, string shot, float settle = 1.5f)
        {
            if (Physics.Raycast(new Vector3(pos.x, pos.y + 40f, pos.z), Vector3.down, out var rh, 80f, GameInfo.LevelLayer)) pos.y = rh.point.y + 0.2f;
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
            {
                var rends = GameObject.Find("FortressIsland")?.GetComponentsInChildren<Renderer>();
                bool ground = Physics.Raycast(island.Center + Vector3.up * 50f, Vector3.down, out var gh, 100f, GameInfo.LevelLayer);
                Note($"island renderers {rends?.Length ?? -1}, bounds {(rends != null && rends.Length > 0 ? rends[0].bounds.ToString() : "-")}, enabled {(rends != null && rends.Length > 0 && rends[0].enabled)}, layer {GameObject.Find("FortressIsland")?.layer}, ground ray {(ground ? gh.point.ToString() + " on " + gh.collider.name : "MISS")}");
                var cam = GameInfo.CurCamera;
                Note($"camera cull mask {(cam ? cam.cullingMask : 0)}, far {(cam ? cam.farClipPlane : 0)}");
            }
            Vector3 center = island.Center;
            PlayerManager.ToggleGodMode(); // the visual run shouldn't end because the camera-player got bitten

            yield return Look(island.DockPos, YawTo(island.DockPos, center), "isl_dock", 3f);
            Vector3 high = center + new Vector3(24f, 0f, 0f); // on top of the rocks east of the plaza
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
            // Build mode HUD (bar + hints) looking at a wall near the north gap.
            {
                float g0 = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
                Vector3 gd = new Vector3(Mathf.Cos(g0), 0f, Mathf.Sin(g0));
                Vector3 eye = center + gd * 9.5f;
                BuildSystem.Instance.DebugSetBuilding(true, PieceType.Harpoon);
                yield return Look(eye, YawTo(eye, center + gd * 15f), "isl_buildmode", 1.5f);
                BuildSystem.Instance.DebugSetBuilding(false, PieceType.Wall);
                Vector3 c = island.CratePos + new Vector3(-3f, 0f, -1.5f);
                yield return Look(c, YawTo(c, island.CratePos), "isl_crate", 1.2f);
            }

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
                Vector3 eye = center + new Vector3(Mathf.Cos(g), 0f, Mathf.Sin(g)) * 6f;
                yield return Look(eye, YawTo(eye, center + new Vector3(Mathf.Cos(g), 0f, Mathf.Sin(g)) * 25f), "isl_fight", 6f);
            }
            Note($"fight over: wave {plugin.Wave}, enemies {FortressPlugin.Alive.Count}, core {Siege.Instance.CoreHp}, running {plugin.Running}");
            if (plugin.Running) { Siege.Instance.ServerDamageCoreForTest(100000); yield return new WaitForSecondsRealtime(1.5f); }
            yield return Look(center + new Vector3(0f, 2.5f, -12f), 0f, "isl_results", 2f);
        }

        // Jump to the waves where each enemy first shows up and photograph them up close.
        private IEnumerator EnemiesScenario()
        {
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            Vector3 center = island.Center;
            Player.LocalPlayer.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return Until(() => FortressPlugin.Instance.Running, 15f, "round start");
            if (!FortressPlugin.Instance.Running) yield break;
            Siege.Instance.ServerAddCoins(5000);
            foreach (float g in FortressIsland.GapAngles)
            {
                Vector3 dir = new Vector3(Mathf.Cos(g * Mathf.Deg2Rad), 0f, Mathf.Sin(g * Mathf.Deg2Rad));
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                for (int k = -1; k <= 1; k++) WallAt(center + dir * 15f + side * (k * BuildSystem.Cell) + Vector3.up * 1.2f, dir);
            }
            var plugin = FortressPlugin.Instance;
            // Gallery: one of each, frozen in a row on the plaza, photographed from the front.
            Vector3 row = center + new Vector3(0f, FortressIsland.CoreLocal.y + 0.3f, 4.5f);
            var kinds = new[] { (EnemyKind.Runner, false), (EnemyKind.Charger, false), (EnemyKind.Spitter, false), (EnemyKind.Bomber, false), (EnemyKind.Tank, false), (EnemyKind.Runner, true) };
            var shown = new List<Creature>();
            for (int i = 0; i < kinds.Length; i++)
                shown.Add(plugin.DebugShowcase(kinds[i].Item1, kinds[i].Item2, row + new Vector3((i - 2.5f) * 1.6f, 0f, 0f), 180f));
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Look(row + new Vector3(0f, 0.3f, 3.6f), 180f, "gallery", 0.6f);
            foreach (var c in shown) if (c) c.ServerChangeHp(1000000);
            var boss = plugin.DebugShowcase(EnemyKind.Boss, false, center + new Vector3(0f, 1.8f, 9f), 180f);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Look(center + new Vector3(0f, 0.8f, 2.2f), 0f, "gallery_boss", 0.6f);
            if (boss) boss.ServerChangeHp(1000000);
            yield return new WaitForSecondsRealtime(1f);

            var seen = new HashSet<string>();
            foreach (int w in new[] { 3, 5, 6, 8, 10 })
            {
                Siege.Instance.DamageCore(-100000); // keep the core alive so every wave gets a look
                plugin.DebugJumpToWave(w);
                yield return new WaitForSecondsRealtime(5f);
                Note($"wave {w}: {FortressPlugin.Alive.Count} enemies, boss hp {Siege.Instance.View.BossHp}/{Siege.Instance.View.BossMax}");
                foreach (var kv in Enemies.Instance.ServerAll.ToList())
                {
                    string key = kv.Value.Kind + (kv.Value.Elite ? "_elite" : "");
                    if (seen.Contains(key) || !kv.Key) continue;
                    seen.Add(key);
                    Vector3 at = kv.Key.transform.position;
                    Vector3 away = at - center; away.y = 0f;
                    Vector3 eye = at - away.normalized * (kv.Value.Kind == EnemyKind.Boss ? 9f : 4f);
                    Note($"  {key} at {at}, scale {kv.Key.transform.localScale.x:0.00}, gear {kv.Key.GetComponentsInChildren<Transform>().Count(x => x.name.EndsWith("Gear"))}");
                    yield return Look(eye, YawTo(eye, at), "enemy_" + key, 1.2f);
                }
                yield return new WaitForSecondsRealtime(4f);
                plugin.DebugKillAll();
                yield return new WaitForSecondsRealtime(2f);
            }
            Note("core " + Siege.Instance.CoreHp);
        }

        // Every turret and trap, upgraded with branches, against mid waves.
        private IEnumerator BuildScenario()
        {
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            Vector3 center = island.Center;
            Player.LocalPlayer.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return Until(() => FortressPlugin.Instance.Running, 15f, "round start");
            if (!FortressPlugin.Instance.Running) yield break;
            Siege.Instance.ServerAddCoins(20000);
            var turrets = new[] { PieceType.Gun, PieceType.Cannon, PieceType.Harpoon, PieceType.Mortar, PieceType.Tesla };
            int placed = 0;
            for (int gi = 0; gi < FortressIsland.GapAngles.Length; gi++)
            {
                float g = FortressIsland.GapAngles[gi];
                Vector3 dir = new Vector3(Mathf.Cos(g * Mathf.Deg2Rad), 0f, Mathf.Sin(g * Mathf.Deg2Rad));
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                for (int k = -1; k <= 1; k++) placed += WallAt(center + dir * 15f + side * (k * BuildSystem.Cell) + Vector3.up * 1.2f, dir) ? 1 : 0;
                placed += TurretAt(PieceType.Spikes, center + dir * 19f) ? 1 : 0;
                placed += TurretAt(turrets[gi], center + dir * 10f + side * 2f) ? 1 : 0;
                placed += TurretAt(turrets[(gi + 3) % turrets.Length], center + dir * 10f - side * 2.5f) ? 1 : 0;
            }
            Note($"placed {placed}");
            // Upgrade every turret to Lv3, alternating branches; damage + repair one wall.
            int up = 0, b = 1;
            foreach (var p in BuildSystem.Instance.ServerPieces.Where(p => Catalog.IsTurret(p.Type)).Select(p => p.Id).ToList())
            {
                BuildSystem.Instance.ServerUpgrade(p, 0); BuildSystem.Instance.ServerUpgrade(p, b); b = 3 - b; up++;
            }
            var wall = BuildSystem.Instance.ServerPieces.FirstOrDefault(p => p.Type == PieceType.Wall);
            BuildSystem.Instance.ServerDamageArea(wall.Pos, 0.5f, 60);
            BuildSystem.Instance.ServerUpgrade(wall.Id, 0, repair: true);
            Note($"upgraded {up}; branches: " + string.Join(", ", BuildSystem.Instance.ServerPieces.Where(p => Catalog.IsTurret(p.Type)).Select(p => p.Type + "L" + p.Level + "b" + p.Branch)));
            yield return new WaitForSecondsRealtime(2f);
            foreach (var p in BuildSystem.Instance.ServerPieces.Where(p => Catalog.IsTurret(p.Type)).GroupBy(p => p.Type).Select(gq => gq.First()).ToList())
            {
                Vector3 eye = p.Pos + (center - p.Pos).Flat().normalized * 3.2f;
                yield return Look(eye, YawTo(eye, p.Pos), "turret_" + p.Type, 1f);
            }
            var plugin = FortressPlugin.Instance;
            foreach (int w in new[] { 4, 6, 8 })
            {
                Siege.Instance.DamageCore(-100000);
                plugin.DebugJumpToWave(w);
                for (int i = 0; i < 4; i++)
                {
                    float g = FortressIsland.GapAngles[i % 3] * Mathf.Deg2Rad;
                    Vector3 eye = center + new Vector3(Mathf.Cos(g), 0f, Mathf.Sin(g)) * 6f;
                    yield return Look(eye, YawTo(eye, center + new Vector3(Mathf.Cos(g), 0f, Mathf.Sin(g)) * 25f), "build_w" + w, 3f);
                }
                Note($"wave {w}: alive {FortressPlugin.Alive.Count}, core {Siege.Instance.CoreHp}");
                plugin.DebugKillAll();
                yield return new WaitForSecondsRealtime(1.5f);
            }
        }

        // A greedy "average player" builder: spends whatever the team has during build time, waves play out for real.
        private IEnumerator BalanceScenario()
        {
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true; // the god-mode collector would soak up every bite and skew the numbers
            Vector3 center = island.Center;
            Player.LocalPlayer.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return Until(() => FortressPlugin.Instance.Running, 15f, "round start");
            if (!FortressPlugin.Instance.Running) yield break;
            var plugin = FortressPlugin.Instance;
            int lastWave = -1;
            Note("dripper game max hp: " + (BuildPoolProbe() ?? "?"));
            float end = Time.realtimeSinceStartup + 900f;
            while (plugin.Running && Time.realtimeSinceStartup < end && plugin.Wave < 15)
            {
                if (plugin.InBuildTime) { SpendLikeAPlayer(center); yield return new WaitForSecondsRealtime(1f); plugin.SkipBuildTime(); }
                if (plugin.Wave != lastWave)
                {
                    lastWave = plugin.Wave;
                    Note($"wave {plugin.Wave}: core {Siege.Instance.CoreHp}, coins {Siege.Instance.ServerCoins}, pieces {BuildSystem.Instance.ServerPieces.Count()}, enemies {FortressPlugin.Alive.Count}");
                    if (plugin.Wave % 3 == 0) { Vector3 eye = center + new Vector3(0f, 0f, -6f); yield return Look(eye, 0f, "bal_w" + plugin.Wave, 0.5f); }
                }
                // Play the net: walk to a corpse on the island, sell when the net is full (or every few pickups).
                var me = Player.LocalPlayer;
                var corpse = FortressNet.Instance.ServerCorpsePositions().Where(p => island.OnIsland(p, 26f)).OrderBy(p => (p - me.Transform.position).sqrMagnitude).Cast<Vector3?>().FirstOrDefault();
                if (FortressNet.Instance.ServerNetCount(me.ObjectId) >= 5 || (corpse == null && FortressNet.Instance.ServerNetCount(me.ObjectId) > 0))
                    me.LocalTeleport(island.CratePos + new Vector3(-1.2f, 0.3f, 0f), 0f, true);
                else if (corpse != null) me.LocalTeleport(corpse.Value + Vector3.up * 0.5f, 0f, true);
                yield return new WaitForSecondsRealtime(1f);
            }
            Note($"balance result: reached wave {plugin.Wave}, running {plugin.Running}, core {Siege.Instance.CoreHp}");
        }

        // Wave 10 against the "average player" build: does the behemoth reach the walls, and does it die in time?
        private IEnumerator BossScenario()
        {
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            Vector3 center = island.Center;
            Player.LocalPlayer.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return Until(() => FortressPlugin.Instance.Running, 15f, "round start");
            if (!FortressPlugin.Instance.Running) yield break;
            Siege.Instance.ServerAddCoins(1200); // about what a team has saved up by wave 10
            SpendLikeAPlayer(center);
            var plugin = FortressPlugin.Instance;
            plugin.DebugJumpToWave(10);
            float start = Time.realtimeSinceStartup;
            int shots = 0;
            while (Time.realtimeSinceStartup - start < 260f && plugin.Running)
            {
                var boss = Enemies.Instance.ServerAll.FirstOrDefault(kv => kv.Value.Kind == EnemyKind.Boss).Key;
                if (Time.realtimeSinceStartup - start > 6f && !boss) { Note($"boss dead after {Time.realtimeSinceStartup - start:0}s, core {Siege.Instance.CoreHp}"); break; }
                if (boss && shots < 4 && (int)(Time.realtimeSinceStartup - start) % 20 == 0)
                {
                    shots++;
                    Note($"boss at {boss.transform.position} hp {boss._hp.Value}, dist to core {(boss.transform.position - Siege.Instance.CorePos).Flat().magnitude:0.0}");
                    Vector3 at = boss.transform.position, eye = at + (center - at).Flat().normalized * 10f;
                    yield return Look(eye, YawTo(eye, at), "boss_" + shots, 0.8f);
                }
                yield return new WaitForSecondsRealtime(1f);
            }
            Note($"boss test end: wave {plugin.Wave}, building {plugin.InBuildTime}, core {Siege.Instance.CoreHp}");
        }

        // No god mode: stand outside the north wall, net corpses, see the fish bite, and read the per-player results.
        private IEnumerator CoopScenario()
        {
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            Vector3 center = island.Center;
            Player.LocalPlayer.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return Until(() => FortressPlugin.Instance.Running, 15f, "round start");
            if (!FortressPlugin.Instance.Running) yield break;
            Siege.Instance.ServerAddCoins(250);
            SpendLikeAPlayer(center);
            var plugin = FortressPlugin.Instance;
            plugin.SkipBuildTime();
            float g = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gapDir = new Vector3(Mathf.Cos(g), 0f, Mathf.Sin(g));
            float end = Time.realtimeSinceStartup + 240f;
            bool shotBite = false;
            while (Time.realtimeSinceStartup < end && plugin.Running)
            {
                if (plugin.InBuildTime) { SpendLikeAPlayer(center); plugin.SkipBuildTime(); }
                var me = Player.LocalPlayer;
                if (PlayerManager.AlivePlayers.Contains(me))
                {
                    var corpse = FortressNet.Instance.ServerCorpsePositions().Where(q => island.OnIsland(q, 26f)).Cast<Vector3?>().FirstOrDefault();
                    if (FortressNet.Instance.ServerNetCount(me.ObjectId) >= 5) me.LocalTeleport(island.CratePos + new Vector3(-1.2f, 0.3f, 0f), 0f, true);
                    else if (corpse != null && UnityEngine.Random.value < 0.5f) me.LocalTeleport(corpse.Value + Vector3.up * 0.5f, 0f, true);
                    else { Vector3 stand = center + gapDir * 18f; me.LocalTeleport(stand, YawTo(stand, center + gapDir * 26f), true); }
                }
                if (!shotBite && AutoTest.Counters.TryGetValue("player_hits", out int hits) && hits > 0) { shotBite = true; FortressPlugin.Shot("coop_bitten"); }
                yield return new WaitForSecondsRealtime(1.5f);
            }
            Note($"coop end: wave {plugin.Wave}, running {plugin.Running}, my hp {Player.LocalPlayer.Vitals.Health}, alive {PlayerManager.AlivePlayers.Count}");
            yield return new WaitForSecondsRealtime(2f);
            FortressPlugin.Shot("coop_dead");
            // Press "respawn" like the death screen does: we should wake up on the fortress dock with the boat beside it,
            // and only then see the results panel.
            if (!PlayerManager.AlivePlayers.Contains(Player.LocalPlayer)) AccessTools.Method(typeof(PlayerDying), "LocalRespawn").Invoke(Player.LocalPlayer.Dying, null);
            yield return Until(() => PlayerManager.AlivePlayers.Contains(Player.LocalPlayer), 15f, "respawn");
            yield return new WaitForSecondsRealtime(2f);
            float boat = BoatManager.Boat ? Vector3.Distance(BoatManager.Boat.HiddenPhysicsRig.position, island.DockPos) : -1f;
            Note($"respawn: {Vector3.Distance(Player.LocalPlayer.Transform.position, island.DockPos):F1} m from dock, boat {boat:F1} m from dock");
            FortressPlugin.Shot("coop_results");
            yield return new WaitForSecondsRealtime(1f);
        }

        // A screenshot of every piece of fortress UI.
        private IEnumerator UiScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            Loot.Instance.ServerGive(Gear.Gunpowder, Rarity.Legendary);
            Loot.Instance.ServerGive(Gear.SharkArmor, Rarity.Epic, 3);
            Loot.Instance.ServerGive(Gear.Rebar, Rarity.Rare, 2);
            Loot.Instance.ServerGive(Gear.LuckyCharm, Rarity.Common, 4);

            // Lobby: the top bar explains how to start; at the vault door, the prompt.
            Vector3 door = island.VaultPos + Quaternion.Euler(0f, -20f, 0f) * new Vector3(0f, 0f, 2.6f);
            me.LocalTeleport(door + Vector3.up * 0.5f, YawTo(door, island.VaultPos), true);
            yield return new WaitForSecondsRealtime(2f);
            FortressPlugin.Shot("ui_lobby_prompt");
            yield return new WaitForSecondsRealtime(0.5f);
            VaultUI.Instance.Open();
            VaultUI.Instance.DebugSelect(Gear.SharkArmor, Rarity.Epic);
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("ui_vault");
            yield return new WaitForSecondsRealtime(0.5f);
            VaultUI.Instance.Close();

            // Build time: cards, key hints, and the look-at panel on a turret in front of us.
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return Until(() => plugin.Running, 15f, "round start");
            Siege.Instance.ServerAddCoins(3000);
            SpendLikeAPlayer(center);
            float g0 = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g0), 0f, Mathf.Sin(g0));
            Vector3 turretAt = center + gd * 10f + Vector3.Cross(Vector3.up, gd) * 0.7f * BuildSystem.Cell;
            Vector3 eye = turretAt - gd * 3.2f + Vector3.up * 0.2f;
            BuildSystem.Instance.DebugSetBuilding(true, PieceType.Cannon);
            yield return Look(eye, YawTo(eye, turretAt), "ui_build_look", 1.2f);
            Vector3 open = center + new Vector3(0f, 0.5f, -8f);
            yield return Look(open, 180f, "ui_build", 1.2f);
            BuildSystem.Instance.DebugSetBuilding(false, PieceType.Wall);

            // A wave: top bar, phase, net panel, edge arrows; the wave banner.
            plugin.SkipBuildTime();
            yield return new WaitForSecondsRealtime(1.2f);
            FortressPlugin.Shot("ui_banner");
            yield return new WaitForSecondsRealtime(5f);
            yield return Look(center + new Vector3(0f, 0.5f, -3f), 180f, "ui_fight", 0.5f);

            // The boss bar.
            plugin.DebugKillAll();
            yield return Until(() => plugin.InBuildTime, 10f, "wave cleared");
            plugin.DebugJumpToWave(10);
            yield return Until(() => Counters.ContainsKey("spawned_Boss"), 20f, "boss");
            yield return new WaitForSecondsRealtime(2f);
            FortressPlugin.Shot("ui_boss");
            yield return new WaitForSecondsRealtime(1f);

            // Results (alive, so the panel shows right away).
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return new WaitForSecondsRealtime(1.5f);
            FortressPlugin.Shot("ui_results");
            yield return new WaitForSecondsRealtime(1f);
        }

        // Every gear effect in a real round: two loadouts, then a chest that throws out a game item.
        private IEnumerator GearScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            var loot = Loot.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            loot.ServerWipeVaultForTest();
            IgnorePlayers = true;
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            void Equip(params Gear[] gear)
            {
                foreach (var g in gear)
                {
                    loot.ServerGive(g, Rarity.Legendary);
                    loot.ServerRequest(me, new VaultRequest { Action = GearCatalog.IsFortress(g) ? VaultAction.EquipFortress : VaultAction.EquipPersonal, Gear = g, Rarity = Rarity.Legendary });
                }
            }

            // Round 1: Gunpowder, Rebar, Lighthouse Shield, War Chest + Shark Armor, Tool Belt.
            Equip(Gear.Gunpowder, Gear.Rebar, Gear.CoreShield, Gear.WarChest, Gear.SharkArmor, Gear.ToolBelt);
            Note($"loadout 1: fortress [{string.Join(",", loot.Vault.Fortress)}], mine [{string.Join(",", loot.Vault.Slots(Loot.KeyOf(me)))}]");
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return Until(() => plugin.Running, 15f, "round 1 start");
            Note($"core {Siege.Instance.CoreHp}/{Siege.Instance.CoreMax} (expect 2000), coins {Siege.Instance.ServerCoins} (expect 425)");
            int coins = Siege.Instance.ServerCoins;
            float g0 = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g0), 0f, Mathf.Sin(g0));
            WallAt(center + gd * 15f + Vector3.up * 1.2f, gd);
            var wall = BuildSystem.Instance.ServerPieces.OrderByDescending(p => p.Id).FirstOrDefault();
            Note($"wall: cost {coins - Siege.Instance.ServerCoins} (expect 8), max hp {wall.MaxHp} (expect 360)");
            yield return new WaitForSecondsRealtime(1f);
            int hp0 = me.Vitals.Health;
            Siege.HurtPlayer(me, 40, Vector3.zero);
            yield return new WaitForSecondsRealtime(1f);
            Note($"bite of 40 with shark armor: hp {hp0} -> {me.Vitals.Health} (expect -22)");
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return new WaitForSecondsRealtime(2f);

            // Round 2: Incendiary, Frost Core, Tesla Coil, Scope + Big Net, Fishmonger's Gloves.
            while (loot.Vault.Fortress.Count > 0) loot.ServerRequest(me, new VaultRequest { Action = VaultAction.UnequipFortress, Slot = 0 });
            while (loot.Vault.Slots(Loot.KeyOf(me)).Count > 0) loot.ServerRequest(me, new VaultRequest { Action = VaultAction.UnequipPersonal, Slot = 0 });
            Equip(Gear.Incendiary, Gear.FrostCore, Gear.TeslaCoil, Gear.Scope, Gear.BigNet, Gear.FishGloves);
            Note($"loadout 2: fortress [{string.Join(",", loot.Vault.Fortress)}], mine [{string.Join(",", loot.Vault.Slots(Loot.KeyOf(me)))}]");
            PlayerManager.ToggleGodMode();
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return Until(() => plugin.Running, 15f, "round 2 start");
            Note($"net capacity {FortressNet.Instance.ServerCapOf(me.ObjectId)} (expect 20)");
            Siege.Instance.ServerAddCoins(600);
            SpendLikeAPlayer(center);
            plugin.SkipBuildTime();
            yield return Until(() => Counters.ContainsKey("burn_ticks"), 40f, "incendiary burn");
            Note($"burn ticks {(Counters.TryGetValue("burn_ticks", out int bt) ? bt : 0)}, tesla shots {(Counters.TryGetValue("shots_Tesla", out int ts) ? ts : 0)}");
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return new WaitForSecondsRealtime(2f);

            // A chest that is sure to throw out a game item.
            ForceGameItem = true;
            loot.ServerSpawnChest(20);
            var chest = loot.ServerChestPos ?? center;
            yield return new WaitForSecondsRealtime(0.5f);
            me.LocalTeleport(chest + Vector3.up * 0.5f, 0f, true);
            yield return new WaitForSecondsRealtime(2f);
            ForceGameItem = false;
            Note("game item: " + string.Join(", ", Counters.Where(kv => kv.Key.StartsWith("game_item_")).Select(kv => kv.Key.Substring(10))));
            yield return Look(chest + new Vector3(-2.5f, 1.2f, -3f), YawTo(chest + new Vector3(-2.5f, 0f, -3f), chest), "chest_item", 1f);
        }

        // Drops, the wave-5 chest, the boss drop, leftovers at round end, then vault actions and the save file.
        private IEnumerator LootScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            var loot = Loot.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            loot.ServerWipeVaultForTest();
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Vector3 center = island.Center;
            var me = Player.LocalPlayer;
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return Until(() => plugin.Running, 15f, "round start");
            if (!plugin.Running) yield break;
            Siege.Instance.ServerAddCoins(400);
            bool shotDrop = false, shotChest = false, jumped = false;
            float end = Time.realtimeSinceStartup + 420f;
            while (plugin.Running && Time.realtimeSinceStartup < end && plugin.Wave <= 10)
            {
                if (plugin.InBuildTime)
                {
                    SpendLikeAPlayer(center);
                    if (loot.ServerChestPos is Vector3 chest)
                    {
                        if (!shotChest) { shotChest = true; yield return Look(chest + new Vector3(-3f, 1f, -4f), YawTo(chest + new Vector3(-3f, 0f, -4f), chest), "loot_chest", 1f); }
                        me.LocalTeleport(chest + Vector3.up * 0.5f, 0f, true);
                        yield return new WaitForSecondsRealtime(1f);
                    }
                    if (plugin.Wave == 5 && !jumped) { jumped = true; Siege.Instance.ServerAddCoins(1500); SpendLikeAPlayer(center); plugin.DebugJumpToWave(10); }
                    else plugin.SkipBuildTime();
                }
                var drop = loot.ServerDropPositions.Cast<Vector3?>().FirstOrDefault();
                if (drop is Vector3 d)
                {
                    if (!shotDrop) { shotDrop = true; yield return Look(d + new Vector3(0f, 0.8f, -2.5f), 0f, "loot_drop", 0.8f); }
                    me.LocalTeleport(d + Vector3.up * 0.3f, 0f, true);
                }
                yield return new WaitForSecondsRealtime(1f);
            }
            int ownedBeforeStop = loot.Vault.Owned.Values.Sum();
            if (plugin.Running) plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return new WaitForSecondsRealtime(1.5f);
            Note($"loot: wave reached {plugin.Wave}, vault {ownedBeforeStop} before stop, {loot.Vault.Owned.Values.Sum()} after; drops left {loot.ServerDropCount}");
            Note("vault: " + string.Join(", ", loot.Vault.Owned.Select(kv => $"{kv.Key.Item1}/{kv.Key.Item2} x{kv.Value}")));

            // Vault actions, as a client would ask for them.
            loot.ServerGive(Gear.Gunpowder, Rarity.Common, 3);
            loot.ServerGive(Gear.SharkArmor, Rarity.Rare, 1);
            int rareBefore = loot.Vault.Count(Gear.Gunpowder, Rarity.Rare);
            loot.ServerRequest(me, new VaultRequest { Action = VaultAction.Merge, Gear = Gear.Gunpowder, Rarity = Rarity.Common });
            Note($"merge: gunpowder rare {rareBefore} -> {loot.Vault.Count(Gear.Gunpowder, Rarity.Rare)}, common left {loot.Vault.Count(Gear.Gunpowder, Rarity.Common)}");
            loot.ServerRequest(me, new VaultRequest { Action = VaultAction.EquipFortress, Gear = Gear.Gunpowder, Rarity = Rarity.Rare });
            loot.ServerRequest(me, new VaultRequest { Action = VaultAction.EquipFortress, Gear = Gear.SharkArmor, Rarity = Rarity.Rare }); // personal gear: refused
            loot.ServerRequest(me, new VaultRequest { Action = VaultAction.EquipPersonal, Gear = Gear.SharkArmor, Rarity = Rarity.Rare });
            Note($"equip: fortress [{string.Join(",", loot.Vault.Fortress)}], mine [{string.Join(",", loot.Vault.Slots(Loot.KeyOf(me)))}], gunpowder value {loot.Vault.FortressValue(Gear.Gunpowder)}, armor value {loot.Vault.PersonalValue(Loot.KeyOf(me), Gear.SharkArmor)}");
            yield return new WaitForSecondsRealtime(0.5f);
            Note($"client mirror matches host: {loot.ClientVault.Write() == loot.Vault.Write()}");
            string file = loot.ServerVaultFile;
            Note($"vault file {file}: {(file != null && File.Exists(file) ? File.ReadAllLines(file).Length + " lines" : "missing")}");
            loot.ServerRequest(me, new VaultRequest { Action = VaultAction.UnequipPersonal, Slot = 0 });
            Note($"unequip: mine [{string.Join(",", loot.Vault.Slots(Loot.KeyOf(me)))}], armor back in vault {loot.Vault.Count(Gear.SharkArmor, Rarity.Rare)}");
            // The vault building and its menu.
            Vector3 door = island.VaultPos + Quaternion.Euler(0f, -20f, 0f) * new Vector3(0f, 0f, 2.6f);
            yield return Look(door + Vector3.up * 0.5f + (door - island.VaultPos) * 1.5f, YawTo(door, island.VaultPos), "vault_outside", 1f);
            me.LocalTeleport(door + Vector3.up * 0.5f, YawTo(door, island.VaultPos), true);
            yield return new WaitForSecondsRealtime(1f);
            loot.ServerGive(Gear.Rebar, Rarity.Epic, 1);
            loot.ServerGive(Gear.LuckyCharm, Rarity.Common, 4);
            yield return new WaitForSecondsRealtime(0.5f);
            VaultUI.Instance.Open();
            VaultUI.Instance.DebugSelect(Gear.LuckyCharm, Rarity.Common);
            yield return new WaitForSecondsRealtime(1f);
            Note($"vault menu open {VaultUI.IsOpen}, inputs blocked {me.BlockInputs}, cursor {Cursor.lockState}");
            FortressPlugin.Shot("vault_ui");
            yield return new WaitForSecondsRealtime(1f);
            VaultUI.Instance.Close();
            Note($"vault menu closed: open {VaultUI.IsOpen}, inputs blocked {me.BlockInputs}");
        }

        // Every test key, called the way the key handler calls it.
        private IEnumerator CheatsScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            plugin.TestKey(K.F4, false);
            yield return new WaitForSecondsRealtime(2f);
            Note($"F4: {Vector3.Distance(Player.LocalPlayer.Transform.position, island.Center + FortressIsland.ReadyLocal):F1} m from the ready circle, boat {Vector3.Distance(BoatManager.Boat.HiddenPhysicsRig.position, island.DockPos):F1} m from dock");
            yield return Until(() => plugin.Running, 10f, "round start from the ready circle");
            if (!plugin.Running) { plugin.TestKey(K.F9, false); yield return Until(() => plugin.Running, 5f, "F9 start"); }
            int coins = Siege.Instance.ServerCoins;
            plugin.TestKey(K.F5, false);
            Note($"F5: coins {coins} -> {Siege.Instance.ServerCoins}");
            bool god = PlayerManager.InGodMode;
            plugin.TestKey(K.F6, false);
            bool god2 = PlayerManager.InGodMode;
            plugin.TestKey(K.F6, false);
            Note($"F6: god {god} -> {god2} -> {PlayerManager.InGodMode}");
            plugin.TestKey(K.F8, false);
            yield return Until(() => plugin.Wave == 1 && !plugin.InBuildTime, 5f, "F8 skips build time");
            Note($"F8: wave {plugin.Wave}, building {plugin.InBuildTime}");
            yield return new WaitForSecondsRealtime(9f); // all three bursts are out
            int alive = FortressPlugin.Alive.Count;
            plugin.TestKey(K.F7, false);
            yield return new WaitForSecondsRealtime(1.5f);
            Note($"F7: alive {alive} -> {FortressPlugin.Alive.Count}");
            yield return Until(() => plugin.InBuildTime, 5f, "wave cleared after F7");
            plugin.TestKey(K.F8, true);
            yield return Until(() => plugin.Wave >= 6, 5f, "Shift+F8 jump");
            Note($"Shift+F8: wave {plugin.Wave}");
            FortressPlugin.Shot("cheats_help");
            yield return new WaitForSecondsRealtime(1f);
            plugin.TestKey(K.F9, false);
            yield return new WaitForSecondsRealtime(1f);
            Note($"F9: running {plugin.Running}");
        }

        private static string BuildPoolProbe()
        {
            var all = (Dictionary<string, Item>)AccessTools.Field(typeof(GameInfo), "_nameToSpawnable").GetValue(null);
            return all.TryGetValue("dripper", out var d) && d.GetComponent<Creature>() is Creature c ? c.MaxHp.ToString() : null;
        }

        private static readonly List<(PieceType type, float r, float sideOffset, int gap)> Plan = BuildPlan();
        private static List<(PieceType, float, float, int)> BuildPlan()
        {
            var p = new List<(PieceType, float, float, int)>();
            for (int g = 0; g < 3; g++) { p.Add((PieceType.Wall, 15f, 0f, g)); p.Add((PieceType.Wall, 15f, 1f, g)); p.Add((PieceType.Wall, 15f, -1f, g)); }
            for (int g = 0; g < 3; g++) p.Add((PieceType.Gun, 10f, 0.7f, g));
            for (int g = 0; g < 3; g++) p.Add((PieceType.Spikes, 19f, 0f, g));
            p.Add((PieceType.Cannon, 4f, 0f, 0));
            for (int g = 0; g < 3; g++) p.Add((PieceType.Harpoon, 10f, -0.8f, g));
            p.Add((PieceType.Tesla, 4f, 0f, 1)); p.Add((PieceType.Mortar, 4f, 0f, 2));
            for (int g = 0; g < 3; g++) { p.Add((PieceType.Wall, 12f, 1f, g)); p.Add((PieceType.Wall, 12f, -1f, g)); }
            return p;
        }

        private static void SpendLikeAPlayer(Vector3 center)
        {
            // 1) repair anything below half, 2) build the next planned piece, 3) upgrade the cheapest upgradable piece.
            foreach (var p in BuildSystem.Instance.ServerPieces.Where(p => p.Hp < p.MaxHp / 2).ToList())
                BuildSystem.Instance.ServerUpgrade(p.Id, 0, repair: true);
            for (int guard = 0; guard < 40; guard++)
            {
                bool did = false;
                foreach (var (type, r, side, gi) in Plan)
                {
                    float g = FortressIsland.GapAngles[gi] * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Cos(g), 0f, Mathf.Sin(g)), sv = Vector3.Cross(Vector3.up, dir);
                    Vector3 at = center + dir * r + sv * side * BuildSystem.Cell + Vector3.up * 1.2f;
                    bool ok = type == PieceType.Wall ? WallAt(at, dir) : TurretAt(type, at);
                    if (ok) { did = true; break; }
                }
                if (!did)
                {
                    var up = BuildSystem.Instance.ServerPieces.Where(p => p.Level < Catalog.MaxLevel && p.Type != PieceType.Floor)
                        .OrderBy(p => Catalog.UpgradeCost(p.Type, p.Level)).FirstOrDefault();
                    if (up.Id == 0 || Siege.Instance.ServerCoins < Catalog.UpgradeCost(up.Type, up.Level)) break;
                    BuildSystem.Instance.ServerUpgrade(up.Id, 1 + (up.Id % 2));
                }
                if (Siege.Instance.ServerCoins < 8) break;
            }
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
