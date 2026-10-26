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
        public static bool SkipBossCamera; // scenarios that look at the bosses themselves keep their own camera
        public static bool ForceGameItem;

        // Counters the rest of the mod bumps so a run can be judged from the report.
        public static readonly Dictionary<string, int> Counters = new Dictionary<string, int>();
        public static void Count(string key, int n = 1) { if (Running) { Counters.TryGetValue(key, out int v); Counters[key] = v + n; } }

        private readonly StringBuilder _report = new StringBuilder();
        private string _scenario, _arg;
        private float _t0;

        private static string Root => Paths.BepInExRootPath;

        private void Start()
        {
            string trigger = Path.Combine(Root, "fortress-autotest.txt");
            if (!File.Exists(trigger)) return;
            var lines = File.ReadAllLines(trigger);
            _scenario = lines.FirstOrDefault()?.Trim() ?? "probe";
            _arg = lines.Skip(1).FirstOrDefault()?.Trim() ?? ""; // a scenario's own setting, if it takes one
            File.Delete(trigger);
            Running = true;
            FortressIsland.ForceForTest = true;
            // Scenarios switch settings (tip cards off, the entrance seen...): keep those out of the player's config file.
            FortressPlugin.Instance.Config.SaveOnConfigSet = false;
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
                case "construct": yield return ConstructScenario(); break;
                case "newfoes": yield return NewFoesScenario(); break;
                case "gear2": yield return Gear2Scenario(); break;
                case "tips": yield return TipsScenario(); break;
                case "gear3": yield return Gear3Scenario(); break;
                case "weapons": yield return WeaponsScenario(); break;
                case "fx": yield return FxScenario(); break;
                case "npc": yield return NpcScenario(); break;
                case "village": yield return VillageScenario(); break;
                case "towers": yield return TowersScenario(); break;
                case "lamps": yield return LampsScenario(); break;
                case "absorber": yield return AbsorberScenario(); break;
                case "people": yield return PeopleScenario(); break;
                case "shopref": yield return ShopRefScenario(); break;
                case "stall": yield return StallLookScenario(); break;
                case "branchwalk": yield return BranchWalkScenario(); break;
                case "sharkaim": yield return SharkAimScenario(); break;
                case "ladder": yield return LadderScenario(); break;
                case "bossprobe": yield return BossProbeScenario(); break;
                case "bosses": yield return BossesScenario(); break;
                case "loot2": yield return Loot2Scenario(); break;
                case "rarity": yield return RarityScenario(); break;
                case "reveal": yield return RevealScenario(); break;
                case "vault2": yield return Vault2Scenario(); break;
                case "bossintro": yield return BossIntroScenario(); break;
                case "bodies": yield return BodiesScenario(); break;
                case "bossfx": yield return BossFxScenario(); break;
                case "playtest": yield return PlaytestScenario(); break;
                case "guns": yield return GunsScenario(); break;
                case "helpers": yield return HelpersScenario(); break;
                case "helperlook": yield return HelperLookScenario(); break;
                case "npcref": yield return NpcRefScenario(); break;
                case "worldref": yield return WorldRefScenario(); break;
                case "townref": yield return TownRefScenario(); break;
                case "cabinref": yield return CabinRefScenario(); break;
                case "villagelook": yield return VillageLookScenario(); break;
                case "hotbardrag": yield return HotbarDragScenario(); break;
                case "mods": yield return ModsScenario(); break;
                case "bossguard": yield return BossGuardScenario(); break;
                case "batch8": yield return Batch8Scenario(); break;
                case "prices": yield return PricesScenario(); break;
                case "hook": _arg = "hook"; yield return Batch8Scenario(); break;
                default: yield return IslandScenario(); break;
            }

            Finish();
        }

        private void Probe()
        {
            var p = Player.LocalPlayer.Transform.position;
            Note($"player at {p}, water there {WaterManager.GetWaterHeight(p):0.00}, ground layer mask {GameInfo.LevelLayer.value}, piece layer {BuildSystem.PieceLayer}");
            Note($"current island '{Island.CurIsland.name}' at {Island.IslandPos} size {Island.IslandSize}; CurIsland idx {OnlineIslandManager.CurIsland}, max unlocked {OnlineIslandManager.MaxIslandUnlocked}, total {IslandManager.TotalIslands}");
            DumpGameFx();
            // The game's own trees (to dress the fortress island with them instead of ours).
            var trees = Resources.FindObjectsOfTypeAll<MeshFilter>().Where(f => f && f.sharedMesh && f.gameObject.scene.IsValid()
                && System.Text.RegularExpressions.Regex.IsMatch(f.name + " " + f.sharedMesh.name, "(?i)palm|tree|coconut|leaf|trunk"))
                .GroupBy(f => f.sharedMesh.name).Select(g => $"{g.Key} x{g.Count()} (e.g. {PathOf(g.First().transform)})").Take(15);
            Note("game trees: " + string.Join("; ", trees));
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
            // The walking fish's materials (for dyeing enemies).
            var dripper = ((Dictionary<string, Item>)AccessTools.Field(typeof(GameInfo), "_nameToSpawnable").GetValue(null)).TryGetValue("dripper", out var dp) ? dp : null;
            if (dripper)
                foreach (var r in dripper.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials.Where(x => x))
                    {
                        var sh = m.shader;
                        var props = Enumerable.Range(0, sh.GetPropertyCount()).Select(i => sh.GetPropertyName(i) + ":" + sh.GetPropertyType(i) + (sh.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture ? "=" + (m.GetTexture(sh.GetPropertyName(i)) ? m.GetTexture(sh.GetPropertyName(i)).name : "null") : ""));
                        Note($"dripper renderer {r.name} ({r.GetType().Name}) material {m.name} shader {sh.name}: {string.Join(", ", props)}");
                    }

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
            if (Physics.Raycast(new Vector3(pos.x, pos.y + 40f, pos.z), Vector3.down, out var rh, 80f, GameInfo.LevelLayer)) pos.y = rh.point.y + FortressIsland.StandHeight;
            Player.LocalPlayer.LocalTeleport(pos, yaw, true);
            yield return new WaitForSecondsRealtime(settle);
            if (shot != null) FortressPlugin.Shot(shot);
            yield return new WaitForSecondsRealtime(0.5f);
        }

        private static float YawTo(Vector3 from, Vector3 to) { var d = to - from; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }

        // Everything drawn right in front of the eyes (held item, hands, arms, the net), to see why something shows or doesn't.
        private void DumpView(string tag)
        {
            var cam = GameInfo.CurCamera;
            if (!cam) { Note($"{tag}: no camera"); return; }
            Note($"{tag}: camera {cam.name} near {cam.nearClipPlane:F2} fov {cam.fieldOfView:F0} mask {cam.cullingMask:X} scale {cam.transform.lossyScale.x:F2}; cameras {string.Join(", ", Camera.allCameras.Select(c => $"{c.name} d{c.depth} m{c.cullingMask:X}"))}");
            foreach (var r in FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || r.forceRenderingOff || !r.gameObject.activeInHierarchy) continue;
                var b = r.bounds;
                var at = cam.transform.InverseTransformPoint(b.center);
                if (at.z < -0.3f || at.magnitude > 2.5f) continue;
                var vp = cam.WorldToViewportPoint(b.center);
                var m = r.sharedMaterial;
                Note($"    {PathOf(r.transform)} [{r.GetType().Name} layer {r.gameObject.layer}] cam {at:F2} screen ({vp.x:F2}, {vp.y:F2}) size {b.size.magnitude:F2} box {b.size:F2}, {(m ? $"{m.name}/{m.shader.name} q{m.renderQueue}" : "-")}, shadows {r.shadowCastingMode}");
            }
        }

        private static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;

        // Face `yaw`, looking `pitch` degrees below level.
        private static void Aim(float yaw, float pitch)
        {
            var cam = Player.LocalPlayer ? Player.LocalPlayer.Camera : null;
            var f = cam ? HarmonyLib.AccessTools.Field(typeof(PlayerCamera), "_rot") : null;
            if (f != null) f.SetValue(cam, new Vector3(pitch, yaw, 0f));
        }

        // Fortress weapons left lying around the test save by earlier runs (they pile up and get in the shots).
        private static void ClearLooseWeapons()
        {
            foreach (var it in FindObjectsOfType<Item>())
                if (it && it.IsSpawned && !it.Holder && WeaponItems.Is(it, out _, out _)) it.DestroyItem();
        }

        private static void SaveTexture(Texture tex, string path)
        {
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            System.IO.File.WriteAllBytes(path, ImageConversion.EncodeToPNG(copy));
            Destroy(copy);
        }

        // What the game has to draw effects with: its named particles and VFX, bullet types, instanced meshes (trees...),
        // the shaders in the build, and its see-through materials.
        private void DumpGameFx()
        {
            try
            {
                string Mat(Material m) => m ? $"{m.name}/{(m.shader ? m.shader.name : "-")} q{m.renderQueue}" : "-";
                var parts = HarmonyLib.AccessTools.Field(typeof(ParticleManager), "_particles").GetValue(null) as System.Collections.IDictionary;
                if (parts != null)
                    foreach (System.Collections.DictionaryEntry kv in parts)
                    {
                        var ps = HarmonyLib.AccessTools.Field(kv.Value.GetType(), "ParticleSystem").GetValue(kv.Value) as ParticleSystem;
                        var r = ps ? ps.GetComponent<ParticleSystemRenderer>() : null;
                        Note($"fx particle {kv.Key}: {(r ? $"{r.renderMode}, mesh {(r.mesh ? r.mesh.name : "-")}, {Mat(r.sharedMaterial)}, trail {Mat(r.trailMaterial)}" : "-")}, children {(ps ? ps.GetComponentsInChildren<ParticleSystem>().Length : 0)}");
                    }
                var vfx = HarmonyLib.AccessTools.Field(typeof(VFXManager), "_allVfx").GetValue(null) as System.Collections.IDictionary;
                if (vfx != null)
                    foreach (System.Collections.DictionaryEntry kv in vfx)
                    {
                        var asset = kv.Value?.GetType().GetProperty("visualEffectAsset")?.GetValue(kv.Value) as UnityEngine.Object;
                        Note($"fx vfx {kv.Key}: {(asset ? asset.name : "-")}");
                    }
                var types = ProjectileManager.Instance ? HarmonyLib.AccessTools.Field(typeof(ProjectileManager), "_types").GetValue(ProjectileManager.Instance) as ProjectileType[] : null;
                if (types != null)
                    for (int i = 0; i < types.Length; i++)
                        Note($"fx projectile {i}: mesh {types[i].MeshInstance} x{types[i].MeshScale}, hitscan {types[i].IsHitScan} fade {types[i].HitScanLineFadeSpeed}, hit {types[i].HitParticle}, landed {types[i].LandedMeshInstance}, decal {types[i].LandedDecal}, width {types[i].WidthRadius}");
                var inst = HarmonyLib.AccessTools.Field(typeof(InstanceManager), "_instanceTypeDic").GetValue(null) as Dictionary<string, InstanceManager.InstanceType>;
                if (inst != null) Note("fx instances: " + string.Join("; ", inst.Values.Select(t => $"{t.Name} ({(t.Mesh ? t.Mesh.name : "-")}, {Mat(t.Material)}{(t.UseParents ? ", parents" : "")})")));
                var all = (Dictionary<byte, Item>)HarmonyLib.AccessTools.Field(typeof(GameInfo), "_allItems").GetValue(null);
                foreach (var it in all.Values.Where(i => i is Weapon))
                {
                    var strings = it.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                        .Where(f => f.FieldType == typeof(string) && f.GetValue(it) is string v && v.Length > 0).Select(f => $"{f.Name}={f.GetValue(it)}");
                    Note($"fx weapon {it.name}: {string.Join(", ", strings)}");
                }
                foreach (var sh in new[] { "Universal Render Pipeline/Particles/Unlit", "Universal Render Pipeline/Particles/Simple Lit", "Universal Render Pipeline/Particles/Lit",
                                           "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Lit", "Sprites/Default", "UI/Default", "Legacy Shaders/Particles/Additive",
                                           "Particles/Standard Unlit", "Unlit/Transparent", "Unlit/Color", "Hidden/Internal-Colored" })
                    Note($"fx shader {sh}: {(Shader.Find(sh) ? "yes" : "no")}");
                var see = Resources.FindObjectsOfTypeAll<Material>().Where(m => m && m.renderQueue >= 2450).GroupBy(m => m.shader ? m.shader.name : "-");
                foreach (var g in see.OrderByDescending(g => g.Count()).Take(25))
                    Note($"fx see-through {g.Key} x{g.Count()}: {string.Join(", ", g.Take(8).Select(m => $"{m.name} q{m.renderQueue}"))}");
                var shaders = Resources.FindObjectsOfTypeAll<Shader>().Select(x => x.name).Where(n => !n.StartsWith("Hidden/")).Distinct().OrderBy(n => n);
                Note("fx shaders loaded: " + string.Join(", ", shaders));
                // The particle materials' textures, saved as pictures (and every property), to draw ours the same way.
                string dir = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "fortress-shots");
                System.IO.Directory.CreateDirectory(dir);
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>().Where(x => x && new[] { "DustParticles", "BloodParticle", "WaterParticle", "WaterParticleWhite", "SplashParticle", "EmissiveParticle", "ExplosionParticle", "AshesParticle", "PoopParticles", "SpitParticle" }.Contains(x.name)).GroupBy(x => x.name).Select(g => g.First()))
                {
                    var sh = m.shader;
                    var props = new List<string>();
                    for (int i = 0; i < sh.GetPropertyCount(); i++)
                    {
                        string n = sh.GetPropertyName(i);
                        switch (sh.GetPropertyType(i))
                        {
                            case UnityEngine.Rendering.ShaderPropertyType.Texture:
                                var tex = m.GetTexture(n);
                                props.Add($"{n}=tex:{(tex ? $"{tex.name} {tex.width}x{tex.height} {tex.filterMode}" : "-")}");
                                if (tex) SaveTexture(tex, System.IO.Path.Combine(dir, $"tex_{m.name}_{n}.png"));
                                break;
                            case UnityEngine.Rendering.ShaderPropertyType.Color: props.Add($"{n}={m.GetColor(n)}"); break;
                            case UnityEngine.Rendering.ShaderPropertyType.Vector: props.Add($"{n}={m.GetVector(n)}"); break;
                            default: props.Add($"{n}={m.GetFloat(n):0.##}"); break;
                        }
                    }
                    Note($"fx material {m.name} [{sh.name}] keywords {string.Join(" ", m.shaderKeywords)}: {string.Join(", ", props)}");
                }
            }
            catch (System.Exception ex) { Note("fx dump failed: " + ex); }
        }

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

            Siege.Instance.ServerAddCoins(Siege.Money(2000));
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
                Vector3 c = island.CounterPos + new Vector3(-3f, 0f, -1.5f);
                yield return Look(c, YawTo(c, island.CounterPos), "isl_crate", 1.2f);
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
            Siege.Instance.ServerAddCoins(Siege.Money(5000));
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
                    Vector3 eye = at - away.normalized * (EnemyCatalog.IsBoss(kv.Value.Kind) ? 14f : 4f);
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
            Siege.Instance.ServerAddCoins(Siege.Money(20000));
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
            Loot.Instance.ServerWipeVaultForTest(); // no gear left over from other tests
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true; // the god-mode collector would soak up every bite and skew the numbers
            Vector3 center = island.Center;
            Player.LocalPlayer.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return Until(() => FortressPlugin.Instance.Running, 15f, "round start");
            if (!FortressPlugin.Instance.Running) yield break;
            var plugin = FortressPlugin.Instance;
            // The test save has no money of its own; give the old 150-coin start so the numbers compare with earlier runs.
            if (Siege.Instance.ServerCoins < Siege.Money(150)) Siege.Instance.ServerAddCoins(Siege.Money(150) - Siege.Instance.ServerCoins);
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
                    me.LocalTeleport(island.CounterPos + new Vector3(-1.2f, 0.3f, 0f), 0f, true);
                else if (corpse != null) me.LocalTeleport(corpse.Value + Vector3.up * 0.5f, 0f, true);
                yield return new WaitForSecondsRealtime(1f);
                FortressNet.Instance.ServerScoopAround(me, 2.5f); // a swing of the hand net
                FortressNet.Instance.ServerSell(me); // [U] at the crate (does nothing anywhere else)
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
            Siege.Instance.ServerAddCoins(Siege.Money(1200)); // about what a team has saved up by wave 10
            SpendLikeAPlayer(center);
            var plugin = FortressPlugin.Instance;
            plugin.DebugJumpToWave(10);
            float start = Time.realtimeSinceStartup;
            int shots = 0;
            while (Time.realtimeSinceStartup - start < 260f && plugin.Running)
            {
                var boss = Enemies.Instance.ServerAll.FirstOrDefault(kv => EnemyCatalog.IsBoss(kv.Value.Kind)).Key;
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
            Siege.Instance.ServerAddCoins(Siege.Money(250));
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
                    if (FortressNet.Instance.ServerNetCount(me.ObjectId) >= 5) { me.LocalTeleport(island.CounterPos + new Vector3(-1.2f, 0.3f, 0f), 0f, true); yield return new WaitForSecondsRealtime(0.5f); FortressNet.Instance.ServerSell(me); }
                    else if (corpse != null && UnityEngine.Random.value < 0.5f) { me.LocalTeleport(corpse.Value + Vector3.up * 0.5f, 0f, true); yield return new WaitForSecondsRealtime(0.3f); FortressNet.Instance.ServerScoopAround(me, 2.5f); }
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
            Note($"respawn: {Vector3.Distance(Player.LocalPlayer.Transform.position, island.RespawnPos):F1} m from the respawn spot (y {Player.LocalPlayer.Transform.position.y:F1}, water {WaterManager.GetWaterHeight(Player.LocalPlayer.Transform.position):F1}), boat {boat:F1} m from dock");
            FortressPlugin.Shot("coop_results");
            yield return new WaitForSecondsRealtime(1f);
        }

        // Fish scales (dismantle, rank up, enhance), pickup and chest cards, the loadout panel and its flashes, drop pacing.
        private IEnumerator Gear3Scenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            var loot = Loot.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            loot.ServerWipeVaultForTest();
            Tips.Show.Value = false; // keep tip cards out of these shots
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            int C(string k) => Counters.TryGetValue(k, out int v) ? v : 0;
            var V = loot.Vault;
            void Do(VaultAction a, Gear g, Rarity r) => loot.ServerRequest(me, new VaultRequest { Action = a, Gear = g, Rarity = r, Count = (byte)(a == VaultAction.Enhance ? loot.Vault.EnhanceOf(g) : 1) });

            // Scales.
            loot.ServerGive(Gear.SharkArmor, Rarity.Legendary); loot.ServerGive(Gear.BigNet, Rarity.Common, 5);
            loot.ServerGive(Gear.Gunpowder, Rarity.Common); loot.ServerGive(Gear.Scope, Rarity.Epic); loot.ServerGive(Gear.Rebar, Rarity.Legendary);
            Do(VaultAction.Salvage, Gear.SharkArmor, Rarity.Legendary);
            for (int i = 0; i < 5; i++) Do(VaultAction.Salvage, Gear.BigNet, Rarity.Common);
            Note($"salvage: scales {V.Scales} (expect 25), big nets left {V.Count(Gear.BigNet, Rarity.Common)}");
            Do(VaultAction.Ascend, Gear.Gunpowder, Rarity.Common);
            Note($"rank up: gunpowder common {V.Count(Gear.Gunpowder, Rarity.Common)} rare {V.Count(Gear.Gunpowder, Rarity.Rare)}, scales {V.Scales} (expect 21)");
            Do(VaultAction.Ascend, Gear.Scope, Rarity.Epic);
            Note($"epic -> legendary by scales refused: scope epic {V.Count(Gear.Scope, Rarity.Epic)} legendary {V.Count(Gear.Scope, Rarity.Legendary)}");
            float before = V.ValueOf(Gear.Rebar, Rarity.Legendary);
            Do(VaultAction.Enhance, Gear.Rebar, Rarity.Legendary);
            Note($"enhance: rebar +{V.EnhanceOf(Gear.Rebar)}, value {before:F2} -> {V.ValueOf(Gear.Rebar, Rarity.Legendary):F2}, scales {V.Scales} (expect 6)");

            // The vault with scales.
            Vector3 door = island.VaultDoor;
            me.LocalTeleport(door + Vector3.up * 0.5f, YawTo(door, island.VaultPos), true);
            yield return new WaitForSecondsRealtime(1f);
            VaultUI.Instance.Open();
            VaultUI.Instance.DebugSelect(Gear.Rebar, Rarity.Legendary);
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("gear3_vault");
            yield return new WaitForSecondsRealtime(0.5f);
            VaultUI.Instance.Close();

            // A round with legendary gear on; the loadout panel; flashes.
            foreach (var g in new[] { Gear.Gunpowder, Gear.Scope, Gear.Rebar }) loot.DebugEquip(me, g, Rarity.Legendary);
            loot.DebugEquip(me, Gear.SharkArmor, Rarity.Epic);
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return Until(() => plugin.Running, 15f, "round start");
            yield return Until(() => plugin.InBuildTime, 5f, "build time");
            plugin.DebugExtendBuild(300f);
            Siege.Instance.ServerAddCoins(Siege.Money(2000));
            SpendLikeAPlayer(center);
            float g0 = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g0), 0f, Mathf.Sin(g0)), side = Vector3.Cross(Vector3.up, gd);
            Vector3 Ground(Vector3 w) { if (Physics.Raycast(w + Vector3.up * 10f, Vector3.down, out var rh, 30f, GameInfo.LevelLayer)) return rh.point; return w; }
            Vector3 eye = Ground(center + gd * 6f - side * 4f) + Vector3.up * 1.8f;
            me.LocalTeleport(eye, YawTo(eye, center + gd * 15f), true);
            for (int i = 0; i < 8; i++) plugin.ServerSpawnMinion(EnemyKind.Runner, center + gd * 21f + side * (i - 3.5f) * 1.2f + Vector3.up * 3f);
            yield return Until(() => C("proc_Gunpowder") > 0 || C("proc_Scope") > 0, 25f, "a legendary flash");
            FortressPlugin.Shot("gear3_proc");
            yield return new WaitForSecondsRealtime(0.5f);
            Note("flashes: " + string.Join(", ", Counters.Where(kv => kv.Key.StartsWith("proc_")).Select(kv => kv.Key.Substring(5) + " " + kv.Value)));
            plugin.DebugKillAll();
            yield return new WaitForSecondsRealtime(1f);

            // Pickup card, then a chest card.
            Vector3 here = me.Transform.position;
            loot.DebugDrop(Gear.Incendiary, Rarity.Epic, here + gd * 1.5f);
            yield return new WaitForSecondsRealtime(0.4f);
            me.LocalTeleport(here + gd * 1.5f + Vector3.up * 0.3f, YawTo(here, center + gd * 15f), true);
            yield return new WaitForSecondsRealtime(1.2f);
            loot.ServerSpawnChest(10);
            var chest = loot.ServerChestPos ?? center;
            yield return new WaitForSecondsRealtime(0.5f);
            me.LocalTeleport(chest + Vector3.up * 0.5f, 0f, true);
            yield return new WaitForSecondsRealtime(1.5f);
            FortressPlugin.Shot("gear3_cards");
            yield return new WaitForSecondsRealtime(0.5f);

            // Drop pacing: the first kill from wave 2 always drops.
            int drops = C("loot_dropped");
            loot.ServerNewRound();
            plugin.DebugJumpToWave(2);
            yield return Until(() => plugin.Wave == 2, 5f, "wave 2");
            plugin.DebugKillAll();
            yield return new WaitForSecondsRealtime(0.5f);
            var one = plugin.ServerSpawnMinion(EnemyKind.Runner, center + gd * 10f + Vector3.up * 3f);
            yield return new WaitForSecondsRealtime(0.5f);
            if (one) one.ServerChangeHp(100000);
            yield return new WaitForSecondsRealtime(1f);
            Note($"first kill of wave 2 dropped: {C("loot_dropped") - drops >= 1}");
            Tips.Show.Value = true;
        }

        // Tip cards, markers and the help panel, with screenshots. Leaves the tips unseen for the real player.
        private IEnumerator TipsScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            Tips.Reset.Value = true;
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            int C(string k) => Counters.TryGetValue(k, out int v) ? v : 0;
            Vector3 Ground(Vector3 w) { if (Physics.Raycast(w + Vector3.up * 10f, Vector3.down, out var rh, 30f, GameInfo.LevelLayer)) return rh.point; return w; }

            // On the island between rounds: the island tip, the circle and vault markers.
            Vector3 dock = Ground(island.DockPos + Vector3.forward * 4f) + Vector3.up * 1f;
            me.LocalTeleport(dock, 0f, true);
            yield return Until(() => C("tip_island") > 0, 10f, "island tip");
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("tips_island");
            yield return new WaitForSecondsRealtime(0.5f);
            Tips.Instance.ToggleHelp();
            yield return new WaitForSecondsRealtime(0.8f);
            FortressPlugin.Shot("tips_help");
            yield return new WaitForSecondsRealtime(0.5f);
            Tips.Instance.ToggleHelp();
            Note($"help panel toggled: opened {C("help_opened")}, open now {Tips.Instance.HelpOpen}");

            // The round: start tip, then a corpse next to us (net tip), then a full net (crate marker pulses).
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return Until(() => plugin.Running, 15f, "round start");
            yield return Until(() => plugin.InBuildTime, 5f, "build time");
            plugin.DebugExtendBuild(300f);
            yield return Until(() => C("tip_start") > 0, 10f, "start tip");
            yield return new WaitForSecondsRealtime(8f);
            Vector3 spot = Ground(center + new Vector3(0f, 0f, -12f)) + Vector3.up * 1f;
            me.LocalTeleport(spot, 0f, true);
            for (int i = 0; i < 6; i++) { var c = plugin.ServerSpawnMinion(EnemyKind.Runner, spot + new Vector3(i - 2.5f, 2f, 4f)); }
            yield return new WaitForSecondsRealtime(1f);
            plugin.DebugKillAll();
            yield return Until(() => C("tip_net") > 0, 15f, "net tip");
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("tips_net");
            yield return new WaitForSecondsRealtime(8f);
            // Scoop them all: the net fills.
            foreach (var p in FortressNet.Instance.ServerCorpsePositions().ToList()) { me.LocalTeleport(p + Vector3.up * 0.4f, 0f, true); yield return new WaitForSecondsRealtime(0.4f); }
            yield return Until(() => C("tip_netFull") > 0, 10f, "net-full tip");
            me.LocalTeleport(spot, YawTo(spot, island.CounterPos), true);
            yield return new WaitForSecondsRealtime(1.5f);
            FortressPlugin.Shot("tips_netfull");
            yield return new WaitForSecondsRealtime(9f);

            // A thief shows up.
            plugin.ServerSpawnMinion(EnemyKind.Thief, spot + new Vector3(0f, 2f, 6f));
            yield return Until(() => C("tip_foeThief") > 0, 10f, "thief tip");
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("tips_thief");
            yield return new WaitForSecondsRealtime(0.5f);
            Note("tips shown: " + string.Join(", ", Counters.Where(kv => kv.Key.StartsWith("tip_")).Select(kv => kv.Key.Substring(4))));
            plugin.DebugKillAll();
            Tips.Reset.Value = true; // the real player still gets every tip
            yield return new WaitForSecondsRealtime(0.5f);
        }

        // Blueprints and the new turrets, the five actives, the legendary specials.
        private IEnumerator Gear2Scenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            var loot = Loot.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            loot.ServerWipeVaultForTest();
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            int C(string k) => Counters.TryGetValue(k, out int v) ? v : 0;
            float g0 = FortressIsland.GapAngles[0] * Mathf.Deg2Rad, g1 = FortressIsland.GapAngles[1] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g0), 0f, Mathf.Sin(g0)), gd1 = new Vector3(Mathf.Cos(g1), 0f, Mathf.Sin(g1)), side = Vector3.Cross(Vector3.up, gd);

            // Blueprints: three to find, then none left.
            bool b1 = loot.ServerUnlockRandom(me), b2 = loot.ServerUnlockRandom(me), b3 = loot.ServerUnlockRandom(me), b4 = loot.ServerUnlockRandom(me);
            Note($"blueprints: {b1} {b2} {b3} then {b4} (expect true true true false), unlocked [{string.Join(",", loot.Vault.Unlocked)}]");

            // Legendary fortress gear for the fight.
            foreach (var g in new[] { Gear.Gunpowder, Gear.Scope, Gear.FrostCore, Gear.Rebar }) loot.DebugEquip(me, g, Rarity.Legendary);
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return Until(() => plugin.Running, 15f, "round start");
            yield return Until(() => plugin.InBuildTime, 5f, "build time");
            plugin.DebugExtendBuild(300f);
            Siege.Instance.ServerAddCoins(Siege.Money(3000));

            // The new turrets at the first gap, a wall line in front; the bubble covers the walls there.
            for (int k = -2; k <= 2; k++) WallAt(center + gd * 15f + side * (k * BuildSystem.Cell) + Vector3.up * 1.2f, gd);
            for (int k = -1; k <= 1; k++) WallAt(center + gd1 * 15f + Vector3.Cross(Vector3.up, gd1) * (k * BuildSystem.Cell) + Vector3.up * 1.2f, gd1);
            bool f = TurretAt(PieceType.Flame, center + gd * 12f + Vector3.up * 1.2f);
            bool sn = TurretAt(PieceType.Sniper, center + gd * 6f + side * 3f + Vector3.up * 1.2f);
            bool bu = TurretAt(PieceType.Bubble, center + gd * 11f + side * 3f + Vector3.up * 1.2f);
            Note($"placed flame {f}, sniper {sn}, bubble {bu}");
            Vector3 Ground(Vector3 w) { if (Physics.Raycast(w + Vector3.up * 10f, Vector3.down, out var rh, 30f, GameInfo.LevelLayer)) return rh.point; return w; }
            Vector3 look = Ground(center + gd * 6f - side * 5f) + Vector3.up * 1.8f;
            yield return Look(look, YawTo(look, center + gd * 12f + side * 1.5f), "gear2_turrets", 1.2f);

            // Bubble: the same blast on a covered wall and on an uncovered one.
            var covered = BuildSystem.Instance.ServerPieces.Where(p => p.Type == PieceType.Wall).OrderBy(p => (p.Pos - (center + gd * 15f)).sqrMagnitude).First();
            var bare = BuildSystem.Instance.ServerPieces.Where(p => p.Type == PieceType.Wall).OrderBy(p => (p.Pos - (center + gd1 * 15f)).sqrMagnitude).First();
            BuildSystem.Instance.ServerDamageArea(covered.Pos, 0.5f, 100);
            BuildSystem.Instance.ServerDamageArea(bare.Pos, 0.5f, 100);
            int lostCovered = covered.Hp - BuildSystem.Instance.ServerPieces.First(p => p.Id == covered.Id).Hp;
            int lostBare = bare.Hp - BuildSystem.Instance.ServerPieces.First(p => p.Id == bare.Id).Hp;
            Note($"bubble: wall under the bubble lost {lostCovered}, bare wall lost {lostBare} (expect 70 / 100)");

            // A fight at the first gap.
            Vector3 beach = center + gd * 21f + Vector3.up * 3f;
            for (int i = 0; i < 6; i++) plugin.ServerSpawnMinion(i == 0 ? EnemyKind.Tank : EnemyKind.Runner, beach + side * (i - 2.5f) * 1.2f);
            Vector3 eye = Ground(center + gd * 7f - side * 4f) + Vector3.up * 1.8f;
            me.LocalTeleport(eye, YawTo(eye, center + gd * 16f), true);
            yield return Until(() => C("shots_Flame") > 3 && C("shots_Sniper") > 0, 25f, "flame and sniper fire");
            FortressPlugin.Shot("gear2_fight");
            yield return Until(() => !FortressPlugin.Alive.Any(c => c && c._hp.Value > 0 && (c.transform.position - beach).sqrMagnitude < 30f * 30f), 30f, "fight over");
            Note($"turrets: flame shots {C("shots_Flame")}, sniper shots {C("shots_Sniper")}; legendary: bursts {C("legend_bursts")}, crits {C("legend_crits")}, freezes {C("legend_freezes")}, thorns {C("legend_thorns")}");

            // Actives, one after another.
            Vector3 spot = Ground(center + gd * 9f) + Vector3.up * 1f;
            me.LocalTeleport(spot, YawTo(spot, center + gd * 20f), true);
            yield return new WaitForSecondsRealtime(0.5f);
            loot.DebugEquip(me, Gear.Overclock, Rarity.Legendary);
            bool oc = Actives.Instance.ServerUse(me, Gear.Overclock);
            Note($"overclock: used {oc}, turrets overclocked for {Turrets.OverclockUntil - Time.time:F1}s; used again right away {Actives.Instance.ServerUse(me, Gear.Overclock)} (expect false: cooldown)");
            foreach (var w in BuildSystem.Instance.ServerPieces.Where(p => p.Type == PieceType.Wall && (p.Pos - spot).sqrMagnitude < 9f * 9f).ToList())
                BuildSystem.Instance.ServerDamageArea(w.Pos, 0.3f, 120);
            int hpBefore = BuildSystem.Instance.ServerPieces.Where(p => p.Type == PieceType.Wall).Sum(p => p.Hp);
            loot.DebugEquip(me, Gear.FieldRepair, Rarity.Legendary);
            Actives.Instance.DebugResetCooldown(me);
            Actives.Instance.ServerUse(me, Gear.FieldRepair);
            Note($"field repair: wall health {hpBefore} -> {BuildSystem.Instance.ServerPieces.Where(p => p.Type == PieceType.Wall).Sum(p => p.Hp)}");
            loot.DebugEquip(me, Gear.Decoy, Rarity.Legendary);
            Actives.Instance.DebugResetCooldown(me);
            Actives.Instance.ServerUse(me, Gear.Decoy);
            for (int i = 0; i < 3; i++) plugin.ServerSpawnMinion(EnemyKind.Runner, spot + gd * 6f + side * (i - 1) * 1.5f + Vector3.up * 2f);
            yield return Until(() => C("decoyed") > 0, 12f, "fish chase the duck");
            FortressPlugin.Shot("gear2_decoy");
            Note($"decoy: pecks {C("decoyed")}");
            loot.DebugEquip(me, Gear.Shockwave, Rarity.Legendary);
            Actives.Instance.DebugResetCooldown(me);
            int hit = Siege.Instance.ServerShockwave(me.Transform.position, 99f, 0f, 0); // count what's around without spending the active
            Actives.Instance.ServerUse(me, Gear.Shockwave);
            Note($"shockwave: used {C("active_Shockwave")}, fish in range {hit}");
            plugin.DebugKillAll();
            yield return new WaitForSecondsRealtime(1.5f);
            loot.DebugEquip(me, Gear.NetCannon, Rarity.Legendary);
            Actives.Instance.DebugResetCooldown(me);
            int netBefore = FortressNet.Instance.ServerNetCount(me.ObjectId);
            Actives.Instance.ServerUse(me, Gear.NetCannon);
            Note($"fish cannon: net {netBefore} -> {FortressNet.Instance.ServerNetCount(me.ObjectId)}");
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("gear2_hud");
            yield return new WaitForSecondsRealtime(0.5f);
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return new WaitForSecondsRealtime(2f);
            // The vault with the new kinds in it.
            foreach (var g in new[] { Gear.Overclock, Gear.Decoy, Gear.Shockwave }) loot.ServerGive(g, Rarity.Rare);
            Vector3 door = island.VaultDoor;
            me.LocalTeleport(door + Vector3.up * 0.5f, YawTo(door, island.VaultPos), true);
            yield return new WaitForSecondsRealtime(1f);
            VaultUI.Instance.Open();
            VaultUI.Instance.DebugSelect(Gear.Overclock, Rarity.Rare);
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("gear2_vault");
            yield return new WaitForSecondsRealtime(0.5f);
            VaultUI.Instance.Close();
        }

        // Fortress weapons, each legendary in turn against four runners held on the first beach. Shots go straight to the
        // host, the way a client's would; screenshots show the gun in view and each effect.
        private IEnumerator WeaponsScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            var loot = Loot.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            loot.ServerWipeVaultForTest();
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            int C(string k) => Counters.TryGetValue(k, out int v) ? v : 0;
            float g0 = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g0), 0f, Mathf.Sin(g0)), side = Vector3.Cross(Vector3.up, gd);
            Vector3 Ground(Vector3 w) { if (Physics.Raycast(w + Vector3.up * 10f, Vector3.down, out var rh, 30f, GameInfo.LevelLayer)) return rh.point; return w; }

            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return Until(() => plugin.Running, 15f, "round start");
            yield return Until(() => plugin.InBuildTime, 5f, "build time");
            plugin.DebugExtendBuild(900f);

            foreach (var g in GearCatalog.All.Where(GearCatalog.IsWeapon))
            {
                plugin.DebugKillAll();
                yield return new WaitForSecondsRealtime(1f);
                // The shark hunts at the waterline, the conch reaches 8 m: those two get the fish (and the player) placed for them.
                float fishAt = g == Gear.SharkWhistle ? 26f : 20f, standAt = g == Gear.Conch ? 15f : 9f;
                Vector3 eye = Ground(center + gd * standAt) + Vector3.up * 1.7f, mid = Ground(center + gd * fishAt);
                me.LocalTeleport(eye - Vector3.up * 1.6f, YawTo(eye, mid), true);
                var fish = new List<Creature>();
                for (int i = 0; i < 4; i++) fish.Add(plugin.ServerSpawnMinion(EnemyKind.Runner, mid + side * (i - 1.5f) * 1.4f + Vector3.up * 1.5f));
                foreach (var c in fish) if (c) Siege.Instance.Slow(c, 1f, 60f); // held where they land
                // The legendary weapon item, put straight into my hand (as if picked up with E).
                var weapon = WeaponItems.ServerSpawn(g, Rarity.Legendary, eye + gd * 0.6f);
                yield return new WaitForSecondsRealtime(0.4f);
                if (weapon) Server.Instance.RpcLogic___SetItemHolder___3876263354(weapon, me, null);
                yield return new WaitForSecondsRealtime(1.1f);
                Note($"  holding {(me.Holding.HeldItem ? me.Holding.HeldItem.GetName() : "nothing")} (item id {(me.Holding.HeldItem ? me.Holding.HeldItem.ID : 0)})");
                int hp0 = fish.Sum(c => c ? Mathf.Max(0, c._hp.Value) : 0);
                Vector3 from0 = fish[0] ? fish[0].transform.position : mid;
                WeaponFire At(Vector3 p, byte phase) => new WeaponFire { Phase = phase, Origin = eye, Dir = (p + Vector3.up * 0.5f - eye).normalized };
                Vector3 target = g == Gear.SharkWhistle ? center + gd * 30f : g == Gear.Hook || g == Gear.BubbleGun ? from0 : mid;
                if (g == Gear.Laser || g == Gear.WaterCannon)
                {
                    for (int t = 0; t <= 20; t++) { Weapons.Instance.ServerFire(me, At(from0, t == 0 ? (byte)0 : (byte)1)); yield return new WaitForSecondsRealtime(0.1f); }
                    Weapons.Instance.ServerFire(me, At(from0, 2));
                }
                else Weapons.Instance.ServerFire(me, At(target, 0));
                FortressPlugin.Shot("weapon_" + g, g == Gear.Laser ? 0f : 0.7f);
                if (g != Gear.Laser) FortressPlugin.Shot("weapon_" + g + "_fly", 0.22f); // the shot on its way
                yield return new WaitForSecondsRealtime(1.2f);
                Note($"  1.2 s after: {Weapons.Instance.DebugState(mid)}; fish {string.Join(", ", fish.Where(c => c).Select(c => $"{(c.transform.position - mid).Flat().magnitude:F1}m/{c._hp.Value}hp"))}");
                yield return new WaitForSecondsRealtime(g == Gear.SharkWhistle ? 10.8f : g == Gear.PoisonGas || g == Gear.Whirlpool ? 4.8f : 1.3f);
                int hp1 = fish.Sum(c => c ? Mathf.Max(0, c._hp.Value) : 0), dead = fish.Count(c => !c || c._hp.Value <= 0);
                float moved = fish[0] && fish[0]._hp.Value > 0 ? (fish[0].transform.position - from0).magnitude : -1f;
                Note($"weapon {g}: fired {C("weapon_" + g)}, fish health {hp0} -> {hp1}, dead {dead}/4, first fish moved {moved:F1} m; bubbled {C("bubbled")}");
            }
            Note(WeaponItems.DescribeBase());
            // A clean look at a weapon in hand and the hotbar: no bodies around, out to sea.
            plugin.DebugKillAll();
            FortressNet.Instance.ServerReset();
            yield return new WaitForSecondsRealtime(0.5f);
            Vector3 stand = Ground(center + gd * 11f) + Vector3.up * 0.2f;
            me.LocalTeleport(stand, YawTo(stand, center + gd * 30f), true);
            var shown = WeaponItems.ServerSpawn(Gear.Laser, Rarity.Epic, stand + Vector3.up * 1.5f);
            yield return new WaitForSecondsRealtime(0.4f);
            if (shown) Server.Instance.RpcLogic___SetItemHolder___3876263354(shown, me, null);
            yield return new WaitForSecondsRealtime(1.5f);
            DumpView("holding a weapon");
            FortressPlugin.Shot("weapon_view");
            yield return new WaitForSecondsRealtime(0.5f);
            // Right mouse: aiming down the sights (the game's own, for the pistol our weapons are built on).
            if (me.Holding.HeldItem is Weapon aimed)
            {
                aimed.SecondaryInput(default);
                yield return new WaitForSecondsRealtime(0.8f);
                FortressPlugin.Shot("weapon_ads");
                yield return new WaitForSecondsRealtime(0.4f);
                aimed.SecondaryInput(default);
                yield return new WaitForSecondsRealtime(0.6f);
            }
            // Candidate grips, to pick WeaponItems.Spec by eye (held_* shots next to pistol_view).
            var inHand = me.Holding.HeldItem;
            var model = WeaponItems.ModelOf(inHand);
            if (model && inHand.HandModelRight)
            {
                var grips = new (string tag, float longest, Vector3 from)[]
                {
                    ("a", 0.27f, new Vector3(0f, 0f, 0f)), ("b", 0.27f, new Vector3(0f, 0f, 0.05f)), ("c", 0.27f, new Vector3(-0.03f, 0.02f, 0.06f)),
                    ("d", 0.22f, new Vector3(-0.03f, 0.03f, 0.05f)), ("e", 0.32f, new Vector3(0f, -0.02f, 0.09f)),
                };
                foreach (var (tag, longest, from) in grips)
                {
                    WeaponItems.Place(model, inHand.HandModelRight, inHand.transform, new WeaponItems.Grip { Longest = longest, FromHand = from });
                    Weapons.Instance.DebugForgetHeld();
                    yield return new WaitForSecondsRealtime(0.4f);
                    FortressPlugin.Shot("held_" + tag);
                    yield return new WaitForSecondsRealtime(0.4f);
                }
                WeaponItems.Place(model, inHand.HandModelRight, inHand.transform, WeaponItems.Is(inHand, out var heldGear, out _) ? WeaponItems.Spec(heldGear) : WeaponItems.InHand);
                Weapons.Instance.DebugForgetHeld();
            }
            // The game's own pistol in the same hand, to compare size and place.
            var pistol = ItemManager.Instance.SpawnNewItem(GameInfo.GetSpawnable("pistol"), stand + Vector3.up * 1.5f, Quaternion.identity);
            yield return new WaitForSecondsRealtime(0.4f);
            if (pistol) Server.Instance.RpcLogic___SetItemHolder___3876263354(pistol, me, null);
            yield return new WaitForSecondsRealtime(1.5f);
            DumpView("holding the game's pistol");
            FortressPlugin.Shot("pistol_view");
            yield return new WaitForSecondsRealtime(0.5f);
            // The hand net out of a full hand: the weapon in slot 1 goes back into the hotbar, the hands go, the net comes out.
            me.Inventory.LocalTrySelectSlot(0);
            yield return new WaitForSecondsRealtime(1.5f);
            Note($"  slot 1: holding {(me.Holding.HeldItem ? me.Holding.HeldItem.GetName() : "nothing")}");
            FortressPlugin.Shot("weapon_slot1");
            yield return new WaitForSecondsRealtime(0.5f);
            Weapons.Instance.DebugNet(true);
            yield return new WaitForSecondsRealtime(1.5f);
            Note($"  net out: holding {(me.Holding.HeldItem ? me.Holding.HeldItem.GetName() : "nothing")}");
            DumpView("net out");
            FortressPlugin.Shot("net_view");
            // Four bodies right in front (held still, then killed without a fling), one swing, the bag fills.
            var prey = new List<Creature>();
            for (int i = 0; i < 4; i++)
            {
                var c = plugin.ServerSpawnMinion(EnemyKind.Runner, stand + gd * 2.2f + side * (i - 1.5f) * 0.6f + Vector3.up * 0.5f);
                if (c) { Siege.Instance.Slow(c, 1f, 60f); prey.Add(c); }
            }
            yield return new WaitForSecondsRealtime(1.5f);
            foreach (var c in prey) if (c) c.ServerChangeHp(c._hp.Value);
            yield return new WaitForSecondsRealtime(1f);
            Note($"  bodies before the swing: {string.Join(", ", FortressNet.Instance.ServerCorpsePositions().Select(p => $"{(p - me.Transform.position).Flat().magnitude:F1}m at {Vector3.Angle(gd, (p - me.Transform.position).Flat()):F0}deg"))}");
            FortressNet.Instance.ServerSwing(me, stand + Vector3.up * 1.6f, gd);
            yield return new WaitForSecondsRealtime(1f);
            Note($"hand net: scooped {C("net_scooped")}, in the net {FortressNet.Instance.ServerNetCount(me.ObjectId)}");
            FortressPlugin.Shot("net_full");
            yield return new WaitForSecondsRealtime(0.5f);
            Weapons.Instance.DebugNet(false); // F again: the weapon comes back out
            yield return new WaitForSecondsRealtime(1.5f);
            Note($"  net away: holding {(me.Holding.HeldItem ? me.Holding.HeldItem.GetName() : "nothing")}");
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return new WaitForSecondsRealtime(2f);
            // No round: the weapons still work, on the game's own fish too.
            var all = (Dictionary<byte, Item>)HarmonyLib.AccessTools.Field(typeof(GameInfo), "_allItems").GetValue(null);
            var wildKinds = all.Values.Where(i => i is Creature c && c.BossType == BossType.None && !(i is RunningFish)).Take(3).ToList();
            var wild = new List<Creature>();
            for (int i = 0; i < 4 && wildKinds.Count > 0; i++)
                if (ItemManager.Instance.SpawnNewItem(wildKinds[i % wildKinds.Count], stand + gd * 6f + side * (i - 1.5f) * 1.2f + Vector3.up * 0.5f, Quaternion.identity) is Creature c) wild.Add(c);
            yield return new WaitForSecondsRealtime(1.5f);
            int wild0 = wild.Sum(c => c ? Mathf.Max(0, c._hp.Value) : 0);
            Note($"  wild fish ({string.Join(", ", wildKinds.Select(k => k.name))}): {wild.Count} spawned, health {wild0}, round running {Siege.Instance.ServerActive}");
            foreach (var g in new[] { Gear.Boomerang, Gear.Hook })
            {
                var w = WeaponItems.ServerSpawn(g, Rarity.Legendary, stand + Vector3.up * 1.5f);
                yield return new WaitForSecondsRealtime(0.4f);
                if (w) Server.Instance.RpcLogic___SetItemHolder___3876263354(w, me, null);
                yield return new WaitForSecondsRealtime(0.8f);
                var aim = wild.FirstOrDefault(c => c && c._hp.Value > 0);
                Vector3 eye2 = me.Transform.position + Vector3.up * 1.6f;
                if (aim) Weapons.Instance.ServerFire(me, new WeaponFire { Phase = 0, Origin = eye2, Dir = (aim.transform.position + Vector3.up * 0.3f - eye2).normalized });
                yield return new WaitForSecondsRealtime(2f);
            }
            int wild1 = wild.Sum(c => c ? Mathf.Max(0, c._hp.Value) : 0);
            Note($"weapons outside a round: wild fish health {wild0} -> {wild1}, dead {wild.Count(c => !c || c._hp.Value <= 0)}/{wild.Count}");
            FortressPlugin.Shot("weapon_wild");
            yield return new WaitForSecondsRealtime(0.5f);
            // The vault's weapon tab.
            loot.ServerGive(Gear.Gunpowder, Rarity.Rare);
            Vector3 door = island.VaultDoor;
            me.LocalTeleport(door + Vector3.up * 0.5f, YawTo(door, island.VaultPos), true);
            yield return new WaitForSecondsRealtime(1f);
            VaultUI.Instance.Open();
            VaultUI.Instance.DebugSelect(Gear.Gunpowder, Rarity.Rare);
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("weapon_vault");
            yield return new WaitForSecondsRealtime(0.5f);
            VaultUI.Instance.Close();
        }

        // Every weapon's look, seen from the side: each effect plays across the view (locally: no damage), shot at a few moments.
        private IEnumerator FxScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            float g0 = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g0), 0f, Mathf.Sin(g0)), side = Vector3.Cross(Vector3.up, gd);
            Vector3 Ground(Vector3 w) { if (Physics.Raycast(w + Vector3.up * 10f, Vector3.down, out var rh, 30f, GameInfo.LevelLayer)) return rh.point; return w; }
            ClearLooseWeapons();
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true); // a round, so the target fish stay
            yield return Until(() => plugin.Running, 15f, "round start");
            yield return Until(() => plugin.InBuildTime, 5f, "build time");
            plugin.DebugExtendBuild(900f);
            yield return new WaitForSecondsRealtime(3f); // the round's own start (it gathers everyone) is over
            for (int i = 0; i < 5 && me.Holding.HeldItem; i++) { me.Inventory.LocalTrySelectSlot(-1); yield return new WaitForSecondsRealtime(0.5f); } // empty hands
            Note($"fx view: holding {(me.Holding.HeldItem ? me.Holding.HeldItem.name : "nothing")}");
            Vector3 stand = Ground(center + gd * 6f) + Vector3.up * 0.2f;
            float yaw = YawTo(stand, center + gd * 20f);
            me.LocalTeleport(stand, yaw, true);
            yield return null;
            Aim(yaw, 6f);
            yield return new WaitForSecondsRealtime(1.5f);
            Vector3 left = Ground(center + gd * 14f - side * 5f) + Vector3.up * 1.4f, right = Ground(center + gd * 14f + side * 5f);
            Vector3 mid = Ground(center + gd * 14f);
            var fishA = plugin.ServerSpawnMinion(EnemyKind.Runner, right + Vector3.up * 1f);
            var fishB = plugin.ServerSpawnMinion(EnemyKind.Runner, mid + Vector3.up * 1f);
            foreach (var c in new[] { fishA, fishB }) if (c) { Siege.Instance.Slow(c, 1f, 300f); c.ServerChangeHp(-100000); }
            yield return new WaitForSecondsRealtime(1.5f);
            var W = Weapons.Instance;
            void Play(FxKind k, Vector3 from, Vector3 to, int piece = 0, int arg = 0) => W.PlayFx(new FxEvent { Kind = k, From = from, To = to, PieceId = piece, Arg = arg, Who = 0 });
            IEnumerator Shots(string tag, params float[] at)
            {
                float t0 = 0f;
                foreach (float t in at) { yield return new WaitForSecondsRealtime(t - t0); t0 = t; FortressPlugin.Shot($"fx_{tag}_{t:0.00}"); }
                yield return new WaitForSecondsRealtime(0.4f);
            }
            // Laser: a beam held for a second onto the fish.
            var target = fishA ? fishA.transform.position + Vector3.up * 0.5f : right + Vector3.up;
            for (int i = 0; i < 14; i++) { Play(FxKind.Beam, left, target, 0, 150); if (i == 8) FortressPlugin.Shot("fx_laser"); yield return new WaitForSecondsRealtime(0.08f); }
            yield return new WaitForSecondsRealtime(0.6f);
            Play(FxKind.Gas, left, right, 0, 6000);
            yield return Shots("gas", 0.3f, 0.75f, 1.6f, 3.5f);
            yield return new WaitForSecondsRealtime(3f);
            Play(FxKind.Boomerang, left, right + Vector3.up);
            yield return Shots("boomerang", 0.2f, 0.45f, 0.8f);
            Play(FxKind.Bubble, left, target, fishA ? fishA.ObjectId : 0, 3000);
            yield return Shots("bubble", 0.15f, 0.6f, 1.5f, 3.2f);
            Play(FxKind.Hook, left, target, fishA ? fishA.ObjectId : 0);
            yield return Shots("hook", 0.08f, 0.2f, 0.5f);
            Play(FxKind.Hook, left, mid + side * 2f + Vector3.up * 0.3f);
            yield return Shots("hookmiss", 0.1f, 0.25f, 0.4f);
            Play(FxKind.Whirl, left, mid, 0, 5000);
            yield return Shots("whirl", 0.3f, 0.7f, 1.4f, 3f);
            yield return new WaitForSecondsRealtime(2f);
            Play(FxKind.Sonic, left, left + side * 8f);
            yield return Shots("conch", 0.1f, 0.25f, 0.45f);
            Play(FxKind.WeaponHit, target, target, 0, 25);
            yield return Shots("hit", 0.05f, 0.2f);
            // Shark: from the beach, out at sea in front, swimming across, then diving.
            Vector3 beach = Ground(center + gd * 19f) + Vector3.up * 0.2f;
            me.LocalTeleport(beach, YawTo(beach, center + gd * 30f), true);
            yield return null;
            Aim(YawTo(beach, center + gd * 30f), 10f);
            yield return new WaitForSecondsRealtime(1f);
            Vector3 sea = center + gd * 27f; sea.y = WaterManager.GetWaterHeight(sea);
            for (int i = 0; i < 16; i++) { var p = sea - side * 6f + side * (i * 0.8f); Play(FxKind.Shark, p, p + side, 77, 1); if (i == 0 || i == 8) FortressPlugin.Shot($"fx_shark_{i}"); yield return new WaitForSecondsRealtime(0.15f); }
            Play(FxKind.Shark, sea, sea, 77, 0);
            yield return Shots("sharkdive", 0.1f, 0.4f);
            Play(FxKind.Bubble, mid + Vector3.up, mid + Vector3.up, 0, 0);
            yield return Shots("bubblepop", 0.1f, 0.3f);
        }

        // The newer enemies, one behaviour at a time, during a long build phase (no waves get in the way).
        // Weapons of each rarity side by side on the ground (their colours, bands, crest and gems), then a legendary in hand.
        private IEnumerator RarityScenario()
        {
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            Tips.Show.Value = false;
            ClearLooseWeapons();
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            float g = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g), 0f, Mathf.Sin(g)), side = Vector3.Cross(Vector3.up, gd);
            var kinds = new[] { Gear.Laser, Gear.Hook, Gear.BubbleGun };
            Item legend = null;
            for (int k = 0; k < kinds.Length; k++)
                for (int r = 0; r < 4; r++)
                {
                    var it = WeaponItems.ServerSpawn(kinds[k], (Rarity)r, center + gd * (8f + 1.4f * k) + side * (r - 1.5f) * 1.1f + Vector3.up * 2.5f);
                    if (k == 0 && r == 3) legend = it;
                }
            yield return new WaitForSecondsRealtime(3f);
            Vector3 eye = center + gd * 5.2f + Vector3.up * (1.2f + FortressIsland.StandHeight);
            me.LocalTeleport(eye, YawTo(eye, center + gd * 9.4f), true);
            yield return null;
            Aim(YawTo(eye, center + gd * 9.4f), 22f);
            yield return new WaitForSecondsRealtime(1.5f);
            FortressPlugin.Shot("rarity_ground");
            yield return new WaitForSecondsRealtime(0.6f);
            if (legend)
            {
                legend.PickUp(me, calledFromLocal: true, sendToServer: true);
                yield return new WaitForSecondsRealtime(1f);
                Aim(YawTo(eye, center + gd * 20f), 0f);
                yield return new WaitForSecondsRealtime(1f);
                FortressPlugin.Shot("rarity_held_legendary");
                yield return new WaitForSecondsRealtime(0.6f);
            }
        }

        // Gear on the ground is the piece itself (with a gem in its rarity's colour); E picks it up into the vault; a full vault
        // turns new gear into scales; the quartermaster sells more slots and room, the smith a bigger rack.
        private IEnumerator Loot2Scenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            var loot = Loot.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            int C(string k) => Counters.TryGetValue(k, out int n) ? n : 0;
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * FortressIsland.StandHeight, 0f, true);
            yield return Until(() => plugin.Running, 15f, "round start");
            yield return Until(() => plugin.InBuildTime, 5f, "build time");
            plugin.DebugExtendBuild(3000f);
            yield return new WaitForSecondsRealtime(3f);
            // A row of drops in front of the player, one of each rarity, and a few kinds.
            float g = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g), 0f, Mathf.Sin(g)), side = Vector3.Cross(Vector3.up, gd);
            var drops = new[] { (Gear.Gunpowder, Rarity.Common), (Gear.Scope, Rarity.Rare), (Gear.SharkArmor, Rarity.Epic), (Gear.LuckyCharm, Rarity.Legendary), (Gear.WarChest, Rarity.Rare), (Gear.Overclock, Rarity.Epic) };
            for (int i = 0; i < drops.Length; i++) loot.DebugDrop(drops[i].Item1, drops[i].Item2, center + gd * 10f + side * (i - 2.5f) * 1.6f + Vector3.up * 2f);
            yield return new WaitForSecondsRealtime(1.5f);
            Vector3 eye = center + gd * 6.5f + Vector3.up * (1.2f + FortressIsland.StandHeight);
            me.LocalTeleport(eye, YawTo(eye, center + gd * 10f), true);
            yield return null;
            Aim(YawTo(eye, center + gd * 10f), 12f);
            yield return new WaitForSecondsRealtime(1.5f);
            FortressPlugin.Shot("loot_drops");
            yield return new WaitForSecondsRealtime(0.6f);
            // Look at one and press E (the game's own interaction picks our LootGrab).
            var grab = FindObjectsOfType<LootGrab>().OrderBy(x => (x.transform.position - me.Transform.position).sqrMagnitude).FirstOrDefault();
            if (grab)
            {
                Vector3 near = grab.transform.position - gd * 1.8f; near.y = grab.transform.position.y + 0.6f;
                me.LocalTeleport(near, YawTo(near, grab.transform.position), true);
                yield return null;
                Aim(YawTo(near, grab.transform.position), 25f);
                yield return new WaitForSecondsRealtime(1f);
                var hovering = HarmonyLib.AccessTools.Field(me.Holding.GetType(), "_interactable")?.GetValue(me.Holding);
                FortressPlugin.Shot("loot_hover");
                yield return new WaitForSecondsRealtime(0.6f);
                int before = loot.Vault.Count(grab.Gear, grab.Rarity);
                grab.Interact(me);
                yield return new WaitForSecondsRealtime(1f);
                Note($"E on a drop: hovering {(hovering != null ? hovering.GetType().Name : "nothing")}, {grab.Gear} {grab.Rarity} in the vault {before} -> {loot.Vault.Count(grab.Gear, grab.Rarity)}, picked {C("loot_picked")}");
            }
            // A full vault (every slot taken, and no part-filled stack of it): the next piece becomes scales.
            loot.Vault.Add(Gear.Incendiary, Rarity.Epic, -loot.Vault.Count(Gear.Incendiary, Rarity.Epic));
            while (!loot.Vault.VaultFull) loot.ServerGive(Gear.Rebar, Rarity.Common, GearCatalog.Stack);
            int scales0 = loot.Vault.Scales;
            loot.DebugDrop(Gear.Incendiary, Rarity.Epic, me.Transform.position + Vector3.up);
            yield return new WaitForSecondsRealtime(1.5f);
            Note($"full vault ({loot.Vault.SlotsUsed}/{loot.Vault.StorageSlots} slots): scales {scales0} -> {loot.Vault.Scales} (expect +8), overflow {C("overflow_salvaged")}");
            // The quartermaster's desk between rounds: a record good enough for a few expansions, and the scales.
            plugin.DebugStopRound();
            yield return Until(() => !plugin.Running, 10f, "round over");
            yield return new WaitForSecondsRealtime(2f);
            FortressIsland.Instance.CloseResults();
            loot.DebugBest(32);
            loot.Vault.Scales = 500;
            loot.ServerRequest(me, new VaultRequest { Action = VaultAction.Salvage, Gear = Gear.Rebar, Rarity = Rarity.Common });
            yield return new WaitForSecondsRealtime(0.5f);
            Vector3 door = island.VaultDoor;
            me.LocalTeleport(door + Vector3.up * FortressIsland.StandHeight, YawTo(door, island.VaultPos), true);
            yield return new WaitForSecondsRealtime(1f);
            VaultUI.Instance.Open(VaultUI.Desk.Quartermaster);
            yield return new WaitForSecondsRealtime(1.2f);
            FortressPlugin.Shot("loot_vault_slots");
            yield return new WaitForSecondsRealtime(0.6f);
            foreach (var e in new[] { Expansion.TeamSlot, Expansion.TeamSlot, Expansion.TeamSlot, Expansion.PersonalSlot, Expansion.Storage, Expansion.Storage })
            { loot.ServerRequest(me, new VaultRequest { Action = VaultAction.Expand, Slot = (byte)e, Count = (byte)loot.Vault.BoughtOf(e) }); yield return new WaitForSecondsRealtime(0.3f); }
            yield return new WaitForSecondsRealtime(1f);
            var v = loot.Vault;
            Note($"expanded: team {v.SlotsOf(0)} (expect 6: the 7th wants 50 waves), personal {v.SlotsOf(1)}, vault {v.StorageSlots} slots, scales left {v.Scales}");
            FortressPlugin.Shot("loot_vault_expanded");
            yield return new WaitForSecondsRealtime(0.6f);
            VaultUI.Instance.Close();
        }

        // OVER 99's bosses at the fort: each of the game's bosses comes in the way its wave sends it (beach, sea, sky), with our
        // health and boss bar; buildings round it take damage; it dies; one left too long crashes into the lighthouse; and
        // clearing wave 100 wins the run.
        // Every 10th wave from 10 to 100: the boss's entrance plays (a shot at its peak, Reveal) and the boss turns up after it.
        private IEnumerator BossIntroScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            var me = Player.LocalPlayer;
            me.LocalTeleport(island.Center + FortressIsland.ReadyLocal + Vector3.up * FortressIsland.StandHeight, 0f, true);
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return Until(() => plugin.Running && plugin.InBuildTime, 15f, "round start");
            int C(string k) => Counters.TryGetValue(k, out int n) ? n : 0;
            for (int w = 10; w <= EnemyCatalog.FinalWave; w += 10)
            {
                var kind = EnemyCatalog.BossFor(w);
                yield return Until(() => plugin.InBuildTime, 30f, "build time before wave " + w);
                plugin.DebugJumpToWave(w);
                yield return Until(() => plugin.Wave == w && !plugin.InBuildTime, 20f, "wave " + w);
                float started = Time.realtimeSinceStartup;
                Creature boss = null;
                yield return Until(() => (boss = FortressPlugin.Alive.FirstOrDefault(x => x && Enemies.Instance.TryGet(x, out var t) && t.Kind == kind)) != null, Reveal.BossSeconds(w) + 6f, kind + " to turn up");
                float came = Time.realtimeSinceStartup - started;
                yield return new WaitForSecondsRealtime(0.9f); // landed (land bosses drop for 0.6 s)
                Vector3 p0 = boss ? boss.transform.position : Vector3.zero;
                yield return new WaitForSecondsRealtime(1.2f);
                float moved = boss ? Vector3.Distance(p0, boss.transform.position) : -1f;
                Note($"wave {w}: {kind}'s entrance {C("boss_intro_" + kind)}; it came after {came:0.0} s (due at {Reveal.BossSpawnAt(w):0.0}), moved {moved:0.00} m while it stood for its entrance, others alive then {FortressPlugin.Alive.Count(x => x && x != boss)}");
                yield return Until(() => !Reveal.Playing, 10f, "the entrance to end");
                yield return new WaitForSecondsRealtime(1f);
                for (int i = 0; i < 20 && plugin.Running && plugin.Wave == w && !plugin.InBuildTime; i++) { plugin.DebugKillAll(); yield return new WaitForSecondsRealtime(1f); }
            }
            Note($"entrances played: {string.Join(", ", EnemyCatalog.BossOrder.Select(k => k + " " + C("boss_intro_" + k)))}");
            plugin.DebugStopRound();
        }

        // Every boss in turn against a small fort: how it looks, what it does for half a minute (its moves counted, a picture
        // every few seconds), its second form, a move broken by the conch; then the crash, and the run won at wave 100.
        private IEnumerator BossesScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            SkipBossCamera = true; // the shots of the bosses are this scenario's own
            yield return Until(() => island.Exists, 30f, "fortress island");
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            yield return Revive();
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * FortressIsland.StandHeight, 0f, true);
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return Until(() => plugin.Running && plugin.InBuildTime, 15f, "round start");
            plugin.DebugExtendBuild(3000f);
            Siege.Instance.ServerAddCoins(Siege.Money(1000000));
            int C(string k) => Counters.TryGetValue(k, out int n) ? n : 0;
            string Moves(EnemyKind k) => string.Join(", ", Counters.Where(kv => kv.Key.StartsWith(Prefix(k))).Select(kv => kv.Key.Substring(Prefix(k).Length) + " " + kv.Value));
            string Prefix(EnemyKind k) => k == EnemyKind.Steed ? "steed_" : k == EnemyKind.Stonefish ? "stone_" : k == EnemyKind.Skyfish ? "skyfish_" : k.ToString().ToLower() + "_";
            var only = _arg.Length > 0 ? _arg.Split(',').Select(x => (EnemyKind)System.Enum.Parse(typeof(EnemyKind), x, true)).ToList() : EnemyCatalog.BossOrder.ToList();
            for (int i = 0; i < EnemyCatalog.BossOrder.Length; i++)
            {
                var kind = EnemyCatalog.BossOrder[i];
                if (!only.Contains(kind)) continue;
                int gap = i % 3, wave = (i + 1) * 10;
                Fort(center);
                Siege.Instance.DamageCore(-Siege.Instance.CoreMax);
                int pieces0 = C("pieces_destroyed");
                var c = plugin.ServerSpawnBoss(kind, 25f, gap, wave, 3f);
                if (!c) { Note($"{kind}: did not spawn"); continue; }
                var brain = Bosses.Of(c);
                Vector3 g = FortressPlugin.BossSpot(kind, gap), toward = (center - g).Flat().normalized;
                Vector3 eye = g + toward * 16f + Vector3.Cross(Vector3.up, toward) * 6f;
                eye.y = BossFx.Ground(eye) + 3f;
                me.LocalTeleport(eye, YawTo(eye, g), true);
                yield return null;
                Aim(YawTo(eye, g), EnemyCatalog.BodyOf(kind) == EnemyCatalog.Body.Sky ? -12f : 4f);
                yield return new WaitForSecondsRealtime(1.5f);
                FortressPlugin.Shot("boss_" + kind + "_entrance");
                yield return new WaitForSecondsRealtime(2.5f);
                Note($"{kind}: {c.transform.localScale.x:F1}x, at {(c.transform.position - center).Flat().magnitude:F1} m out, {c.transform.position.y - BossFx.Ground(c.transform.position):F1} m up, hp {c._hp.Value}");
                FortressPlugin.Shot("boss_" + kind + "_look");
                // Half a minute of it, a picture every 5 s from a little way off, wherever it is.
                for (int s = 0; s < 6 && c && c._hp.Value > 0; s++)
                {
                    yield return new WaitForSecondsRealtime(5f);
                    if (!c) break;
                    // From outside it, looking in at it with the fort behind (straight down the middle if it's at the lighthouse).
                    Vector3 at = c.transform.position, off = (at - center).Flat();
                    off = off.magnitude > 3f ? off.normalized : (g - center).Flat().normalized;
                    Vector3 cam = at + off * 17f + Vector3.Cross(Vector3.up, off) * 5f + Vector3.up * 7f;
                    cam.y = Mathf.Max(cam.y, BossFx.Ground(cam) + 3f);
                    me.LocalTeleport(cam, YawTo(cam, at), true);
                    yield return null;
                    Aim(YawTo(cam, at), 12f);
                    yield return new WaitForSecondsRealtime(0.4f);
                    FortressPlugin.Shot($"boss_{kind}_{s}");
                }
                Note($"{kind}: moves {Moves(kind)}; pieces destroyed {C("pieces_destroyed") - pieces0}; core {Siege.Instance.CoreHp}/{Siege.Instance.CoreMax}");
                // Its second form, then a move broken by the conch.
                if (c) c.ServerChangeHp(c._hp.Value - plugin.MaxHpOf[c] * 45 / 100);
                yield return Until(() => !c || C("boss_phase2_" + kind) > 0, 5f, kind + " second form");
                yield return new WaitForSecondsRealtime(3f);
                if (c) FortressPlugin.Shot($"boss_{kind}_second");
                yield return Until(() => !c || (brain != null && brain.Winding), 25f, kind + " winding up a move");
                if (c) { Bosses.Instance.Interrupt(c); yield return new WaitForSecondsRealtime(0.5f); FortressPlugin.Shot($"boss_{kind}_broken"); }
                yield return new WaitForSecondsRealtime(6f);
                Note($"{kind}: second form {C("boss_phase2_" + kind)}, broken {C("boss_broken_" + kind)}, moves {Moves(kind)}");
                if (c) c.ServerChangeHp(1000000);
                yield return new WaitForSecondsRealtime(2.5f);
                Note($"{kind}: died {C("boss_died_" + kind)}, alive left {FortressPlugin.Alive.Count(x => x && x._hp.Value > 0)}, trophy dropped {C("trophy_dropped_" + kind)}");
                // Its trophy: a look at it where it fell, then into the fish crate for money (the first one), or the vault.
                var trophy = FindObjectsOfType<Item>().FirstOrDefault(x => x && x.IsSpawned && BossTrophies.Is(x, out var tk) && tk == kind);
                if (trophy)
                {
                    Vector3 tp = trophy.transform.position, te = tp + (center - tp).Flat().normalized * 3f + Vector3.up * 1.6f;
                    me.LocalTeleport(te, YawTo(te, tp), true);
                    yield return null;
                    Aim(YawTo(te, tp), 25f);
                    yield return new WaitForSecondsRealtime(0.8f);
                    FortressPlugin.Shot($"boss_{kind}_trophy");
                    int money0 = Siege.Instance.ServerCoins, stored0 = Loot.Instance.Vault.CountItem(BossTrophies.IdOf(kind));
                    if (i % 2 == 0) FortressNet.Instance.ServerSellThrown(trophy, island.CounterPos + Vector3.up);
                    else Loot.Instance.ServerStoreThrown(trophy, island.VaultPos + Vector3.up);
                    yield return new WaitForSecondsRealtime(1f);
                    Note($"{kind}: trophy '{trophy.GetName()}' worth {trophy.TotalWorth}: money {money0} -> {Siege.Instance.ServerCoins}, in the vault {stored0} -> {Loot.Instance.Vault.CountItem(BossTrophies.IdOf(kind))}");
                }
                foreach (var x in FortressPlugin.Alive.ToList()) if (x) x.ServerChangeHp(1000000);
                yield return new WaitForSecondsRealtime(1f);
            }
            if (_arg.Length > 0) { plugin.DebugStopRound(); yield break; }
            // Left alive too long: it crashes into the lighthouse.
            Siege.Instance.DamageCore(-Siege.Instance.CoreMax);
            int coreBefore = Siege.Instance.CoreHp;
            var late = plugin.ServerSpawnBoss(EnemyKind.Warden, 25f, 1, 10, 1f);
            yield return new WaitForSecondsRealtime(2f);
            Bosses.Instance.DebugAge(Bosses.CrashAfter - 1f);
            yield return new WaitForSecondsRealtime(2.5f);
            Note($"crash: core {coreBefore} -> {Siege.Instance.CoreHp} (expect about -60% at full health), boss gone {late == null || !late.IsSpawned || late.IsDestroying}, crashes {C("boss_crashed")}");
            // Wave 100 cleared: the run is won.
            Siege.Instance.DamageCore(-Siege.Instance.CoreMax);
            plugin.DebugJumpToWave(EnemyCatalog.FinalWave);
            yield return Until(() => plugin.Wave == EnemyCatalog.FinalWave && !plugin.InBuildTime, 20f, "wave 100");
            yield return new WaitForSecondsRealtime(Reveal.BossSeconds(EnemyCatalog.FinalWave) + 3f);
            var eyeBoss = FortressPlugin.Alive.FirstOrDefault(x => Enemies.Instance.TryGet(x, out var t) && t.Kind == EnemyKind.Eye);
            if (eyeBoss) { Vector3 e2 = center + new Vector3(0f, 1.2f + FortressIsland.StandHeight, 4f); me.LocalTeleport(e2, YawTo(e2, eyeBoss.transform.position), true); yield return null; Aim(YawTo(e2, eyeBoss.transform.position), -15f); }
            yield return new WaitForSecondsRealtime(1.5f);
            FortressPlugin.Shot("boss_wave100");
            Note($"wave 100: {FortressPlugin.Alive.Count()} enemies, the eye {(eyeBoss ? "here" : "missing")}");
            Siege.Instance.DamageCore(-Siege.Instance.CoreMax);
            for (int i = 0; i < 20 && plugin.Running; i++) { plugin.DebugKillAll(); yield return new WaitForSecondsRealtime(1f); }
            yield return new WaitForSecondsRealtime(3f);
            Note($"after wave 100: running {plugin.Running}, victory {C("victory")}, victory camera {C("victory_camera")}, playing {Reveal.Playing}");
            yield return Until(() => !Reveal.Playing && FortressIsland.Instance.ResultsShowing, 25f, "the results after the victory camera");
            yield return new WaitForSecondsRealtime(1f);
            Note($"results up {FortressIsland.Instance.ResultsShowing}, own camera back {GameInfo.CurCamera == me.Camera.Cam}");
            FortressPlugin.Shot("boss_victory_results");
        }

        // A small fort for the bosses to break: walls across each beach, two turrets behind them, a cannon by the lighthouse.
        private static void Fort(Vector3 center)
        {
            var types = new[] { PieceType.Gun, PieceType.Cannon, PieceType.Harpoon, PieceType.Tesla, PieceType.Sniper, PieceType.Flame };
            int gi = 0;
            foreach (float g in FortressIsland.GapAngles)
            {
                Vector3 dir = new Vector3(Mathf.Cos(g * Mathf.Deg2Rad), 0f, Mathf.Sin(g * Mathf.Deg2Rad)), side = Vector3.Cross(Vector3.up, dir);
                for (int k = -1; k <= 1; k++) WallAt(center + dir * 15f + side * (k * BuildSystem.Cell) + Vector3.up * 1.2f, dir);
                TurretAt(types[gi % types.Length], center + dir * 10f + side * 2f);
                TurretAt(types[(gi + 3) % types.Length], center + dir * 10f - side * 2.5f);
                gi++;
            }
            TurretAt(PieceType.Cannon, center + new Vector3(4f, 1.45f, 3f));
        }

        // Every boss's every move, played on the spot in front of a clean camera (the boss stood still: test key F11's
        // freeze), pictures at the moments that matter; the ten trophies at the end. For judging how things look.
        private IEnumerator BossFxScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            SkipBossCamera = true;
            yield return Until(() => island.Exists, 30f, "fortress island");
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            yield return Revive();
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * FortressIsland.StandHeight, 0f, true);
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return Until(() => plugin.Running && plugin.InBuildTime, 15f, "round start");
            plugin.DebugExtendBuild(3000f);
            Siege.Instance.ServerAddCoins(Siege.Money(1000000));
            // Which see-through shaders show a colour on a plain mesh: a row of red and green patches on the plaza, one per shader.
            if (_arg == "shaders")
            {
                string[] names = { "Sprites/Default", "UI/Default", "Universal Render Pipeline/Particles/Unlit", "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Particles/Lit", "Legacy Shaders/Particles/Alpha Blended", "Unlit/Transparent", "Unlit/Color" };
                Vector3 row0 = center + new Vector3(-7f, 0f, 7f);
                for (int k = 0; k < names.Length; k++)
                {
                    var sh = Shader.Find(names[k]);
                    Note($"shader '{names[k]}': {(sh ? (sh.isSupported ? "found" : "found, unsupported") : "missing")}");
                    if (!sh) continue;
                    foreach (bool vertex in new[] { true, false })
                    {
                        Vector3 p = row0 + new Vector3(k * 2f, 0f, vertex ? 0f : 2.2f);
                        var go = BossFx.Decal("circle", p, p + Vector3.forward, 0.9f, 0, Color.green);
                        var m = new Material(sh) { renderQueue = 3010 };
                        if (!vertex) { m.color = new Color(1f, 0.1f, 0.1f, 0.7f); if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(1f, 0.1f, 0.1f, 0.7f)); }
                        if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); }
                        go.GetComponent<MeshRenderer>().sharedMaterial = m;
                    }
                }
                Reveal.Instance.TestView(row0 + new Vector3(7f, 6f, -5f), row0 + new Vector3(7f, 0f, 1f));
                yield return new WaitForSecondsRealtime(1f);
                FortressPlugin.Shot("fx_shaders");
                Reveal.Instance.TestViewEnd();
                plugin.DebugStopRound();
                yield break;
            }
            var only = _arg.Length > 0 ? _arg.Split(',').Select(x => (EnemyKind)System.Enum.Parse(typeof(EnemyKind), x, true)).ToList() : EnemyCatalog.BossOrder.ToList();
            foreach (var kind in EnemyCatalog.BossOrder.Where(only.Contains))
            {
                int i = System.Array.IndexOf(EnemyCatalog.BossOrder, kind), gap = 0, wave = (i + 1) * 10;
                Fort(center);
                var c = plugin.ServerSpawnBoss(kind, 50f, gap, wave, 2.5f);
                if (!c) { Note($"{kind}: no body"); continue; }
                // Its whole entrance, then held still where it stands.
                var brain = Bosses.Of(c);
                for (float w = 0f; w < 25f && c && brain != null && !brain.IsLive; w += Time.unscaledDeltaTime) yield return null;
                FortressPlugin.Frozen = true;
                yield return new WaitForSecondsRealtime(0.4f);
                if (kind == only[0]) Note($"sky: sun {(RenderSettings.sun ? RenderSettings.sun.name + " " + RenderSettings.sun.intensity : "none")}, ambient {RenderSettings.ambientMode}, fog {RenderSettings.fog} {RenderSettings.fogMode} {RenderSettings.fogDensity} ({RenderSettings.fogStartDistance}-{RenderSettings.fogEndDistance}), skybox {(RenderSettings.skybox ? RenderSettings.skybox.shader.name : "none")}");
                Vector3 B = c.transform.position, g = new Vector3(B.x, BossFx.Ground(B), B.z);
                Vector3 inward = (center - g).Flat().normalized, side = Vector3.Cross(Vector3.up, inward);
                var bb = BossLooks.WorldBounds(c.transform);
                float r = Mathf.Max(3f, bb.extents.magnitude);
                Vector3 fwd = (c.transform.rotation * Quaternion.Inverse(BossLooks.Turn(kind)) * Vector3.forward);
                string axes = string.Join("; ", c.GetComponentsInChildren<SkinnedMeshRenderer>().Where(x => x.enabled).Select(x => $"{x.name} local {x.localBounds.size:F2} root {(x.rootBone ? (Quaternion.Inverse(c.transform.rotation) * x.rootBone.rotation).eulerAngles.ToString("F0") : "-")}"));
                Note($"{kind}: at {(B - center).Flat().magnitude:F1} m out, its feet {bb.min.y - BossFx.Ground(bb.center):F1} m over the ground under it, box {bb.size:F1} centred {bb.center - B:F1}, facing {fwd:F2} (toward the middle {Vector3.Angle(fwd.Flat(), inward):F0} degrees off); {axes}");
                var arrow = WeaponFx.Line("TestArrow", BossVfx.Glow(Color.yellow, 2f), 0.3f);
                arrow.positionCount = 2; arrow.SetPosition(0, bb.center); arrow.SetPosition(1, bb.center + fwd * r * 1.6f);
                // A camera on `look` from about `dist` away and `up` over it, from the first direction round `pref` with nothing
                // of the level in between and well clear of the ground (straight over it if there's none); `low`: just off the ground.
                Vector3 Eye(Vector3 look, Vector3 pref, float dist, float up, bool low = false)
                {
                    Vector3 p0 = pref.Flat().sqrMagnitude > 1e-4f ? pref.Flat().normalized : Vector3.forward;
                    for (int k = 0; k < 14; k++)
                    {
                        float yaw = (k % 2 == 0 ? 1f : -1f) * ((k + 1) / 2) * 26f;
                        Vector3 eye = look + Quaternion.Euler(0f, yaw, 0f) * p0 * dist;
                        eye.y = low ? BossFx.Ground(eye) + 0.8f : look.y + up;
                        if (!low && eye.y < BossFx.Ground(eye) + 1.5f) continue;
                        if (Physics.Linecast(look + Vector3.up * 0.3f, eye, GameInfo.LevelLayer)) continue;
                        return eye;
                    }
                    if (low) { Vector3 e = look + p0 * dist; e.y = BossFx.Ground(e) + 2.5f; return e; }
                    return look + Vector3.up * (dist + up);
                }
                void View(Vector3 eye, Vector3 look) => Reveal.Instance.TestView(eye, look);
                // Its look: from the front, from the side, and from low down.
                Vector3 flatFwd = fwd.Flat().sqrMagnitude > 0.01f ? fwd.Flat().normalized : inward, flatSide = Vector3.Cross(Vector3.up, flatFwd);
                View(Eye(bb.center, flatFwd * 2f + flatSide * 0.8f, r * 2.2f, r * 0.4f), bb.center);
                yield return new WaitForSecondsRealtime(0.8f);
                FortressPlugin.Shot($"fx_{kind}_look");
                View(Eye(bb.center, flatSide, r * 2.4f, r * 0.3f), bb.center);
                yield return new WaitForSecondsRealtime(0.4f);
                FortressPlugin.Shot($"fx_{kind}_side");
                if (arrow) Destroy(arrow.gameObject);
                Vector3 mid = bb.center + Vector3.up * bb.extents.y * 0.3f;
                View(Eye(mid, flatFwd - flatSide * 0.6f, r * 1.6f, 0f, low: true), mid);
                yield return new WaitForSecondsRealtime(0.4f);
                FortressPlugin.Shot($"fx_{kind}_low");
                // A spot in front of it, toward the fort, and a camera on it.
                Vector3 front = g + inward * (r + 4f);
                front.y = BossFx.Ground(front);
                int turret = TurretFor(front + side * 2f);
                Vector3 tp = BuildSystem.Instance.ServerPieces.Where(p => p.Id == turret).Select(p => p.Pos).DefaultIfEmpty(front).First();
                IEnumerator Shot(string move, Vector3 at, float wait, float dist = 13f, float up = 8f) { View(Eye(at, side * 1.2f + inward * 0.6f, dist, up), at); yield return new WaitForSecondsRealtime(wait); FortressPlugin.Shot($"fx_{kind}_{move}"); }
                IEnumerator Pause(float s) { yield return new WaitForSecondsRealtime(s); }
                void Fx(string key, Vector3 a, Vector3 b2, int arg = 0, int who = 0) => BossFx.Send(key, c, a, b2, arg, who);
                switch (kind)
                {
                    case EnemyKind.Warden:
                        Fx("w.circle", front, front, 1300, 260); yield return Shot("warn", front, 0.6f);
                        yield return Pause(0.8f);
                        Fx("warden.snap", front, B); yield return Shot("snap", front, 0.08f); yield return Shot("snap2", front, 0.35f);
                        Fx("warden.chain", B, tp, 3000, turret); yield return Shot("chain", Vector3.Lerp(g, tp, 0.5f), 0.8f, 16f, 9f);
                        yield return Pause(2.8f);
                        Fx("warden.shell", B, B, 3000); yield return Shot("shell", bb.center, 1f, r * 2.4f + 4f, r * 0.5f);
                        break;
                    case EnemyKind.Urchin:
                        Fx("urchin.bristle", B, B, 1000); yield return Shot("bristle", bb.center, 0.6f, r * 2.4f + 4f, r * 0.5f);
                        var spots = Enumerable.Range(0, 10).Select(k => g + Quaternion.Euler(0f, k * 36f, 0f) * Vector3.forward * UnityEngine.Random.Range(6f, 12f)).ToList();
                        foreach (var sp in spots) Fx("urchin.spike", B + Vector3.up * r * 0.5f, sp);
                        yield return Shot("spikes", g, 0.25f, 18f, 10f);
                        yield return Pause(0.3f);
                        foreach (var sp in spots) Fx("urchin.caltrop", sp, sp, 4000);
                        yield return Shot("caltrops", spots[0], 0.5f, 9f, 5f);
                        Fx("urchin.bloom", B, B, 0, Mathf.RoundToInt((r + 4f) * 100f)); yield return Shot("bloom", g, 0.15f, 20f, 10f); yield return Shot("bloom2", g, 0.25f, 20f, 10f);
                        break;
                    case EnemyKind.Angler:
                        Fx("angler.lure", B, B, 1700); yield return Shot("lure", bb.center, 1.5f, r * 2.4f + 4f, r * 0.4f);
                        yield return Pause(0.2f);
                        Fx("angler.flash", bb.center + Vector3.up * bb.extents.y + inward * bb.extents.z, B); yield return Shot("flash", bb.center, 0.1f, r * 2.4f + 4f, r * 0.4f);
                        yield return Pause(1.4f);
                        Fx("dark", B, B, 1); yield return Shot("dark", center, 2.2f, 20f, 11f); yield return Shot("dark2", bb.center, 1.2f, r * 2.4f + 6f, r * 0.5f);
                        Fx("dark", B, B, 0); yield return Pause(1.8f);
                        break;
                    case EnemyKind.Steed:
                        Fx("steed.stomp", g, g, 0, 750); yield return Shot("stomp", g, 0.12f, 18f, 9f); yield return Shot("stomp2", g, 0.35f, 18f, 9f);
                        for (int k = -1; k <= 1; k++) { Vector3 a = center + side * k * 6.5f - inward * 26f, b2 = center + side * k * 6.5f + inward * 26f; a.y = BossFx.Ground(a); b2.y = BossFx.Ground(b2); Fx("steed.phantom", a, b2, 1100); }
                        yield return Shot("phantoms", center, 0.45f, 22f, 12f);
                        yield return Pause(1f);
                        Fx("steed.golden", B, B); yield return Shot("golden", bb.center, 1f, r * 2.4f + 4f, r * 0.5f);
                        break;
                    case EnemyKind.Stonefish:
                        for (int k = -1; k <= 1; k++) Fx("stone.spires", g, g + Quaternion.Euler(0f, k * 30f, 0f) * inward * 18f);
                        yield return Shot("spires", g + inward * 8f, 0.6f, 18f, 10f);
                        yield return Pause(2.4f);
                        Fx("stone.pool", front + side * 4f, front, 4000, 240); Fx("stone.pool", front - side * 3f, front, 4000, 240); yield return Shot("pools", front, 0.8f, 14f, 8f);
                        yield return Pause(3.6f);
                        Fx("stone.gaze", B + Vector3.up * r * 0.5f, g + inward * 22f, 0, 35); Fx("stone.petrify", tp, B, 3500, turret);
                        IEnumerator Close(string move, float wait, float dist, float up) { View(Eye(tp + Vector3.up, -inward + side * 0.5f, dist, up), tp + Vector3.up); yield return new WaitForSecondsRealtime(wait); FortressPlugin.Shot($"fx_{kind}_{move}"); }
                        yield return Shot("gaze", tp, 0.15f, 12f, 6f); yield return Close("petrify", 0.75f, 7f, 3f); yield return Close("petrified", 1.2f, 6f, 2f);
                        yield return Pause(1.35f); yield return Close("unpetrify", 0.1f, 7f, 3f); yield return Close("unpetrify2", 0.4f, 7f, 3f);
                        break;
                    case EnemyKind.Skyfish:
                        for (int k = 0; k < 6; k++) Fx("sky.bomb", front + side * (k - 2.5f) * 3f, front);
                        yield return Shot("bombs", front, 0.15f, 16f, 8f);
                        yield return Pause(1.5f);
                        Vector3 s0 = center - side * 28f, s1 = center + side * 28f; s0.y = BossFx.Ground(s0) + 7f; s1.y = BossFx.Ground(s1) + 7f;
                        Fx("sky.boom", s0, s1, 600); yield return Shot("boom", center, 0.35f, 24f, 12f);
                        break;
                    case EnemyKind.Kraken:
                        Vector3 shore = new Vector3(B.x, WaterManager.GetWaterHeight(B), B.z);
                        Fx("kraken.tentacles", shore, shore + inward * 21f, 1400, 3); yield return Shot("tentacles", shore + inward * 4f, 1.0f, 18f, 8f); yield return Shot("lash", shore + inward * 9f, 0.6f, 16f, 7f);
                        yield return Pause(1f);
                        Fx("kraken.reach", tp, shore, 2000, turret); yield return Shot("reach", tp, 1.2f, 10f, 5f);
                        var po = BuildSystem.Instance.PieceObject(turret);
                        if (po) Note("turret parts: " + string.Join("; ", po.GetComponentsInChildren<Renderer>().Where(x => x.enabled).Select(x => $"{x.GetType().Name} {x.name}: {string.Join(",", x.sharedMaterials.Select(m => m ? m.name + "/" + (m.shader ? m.shader.name : "-") : "null"))}")));
                        Fx("kraken.grab", tp, shore, 0, turret); BuildSystem.Instance.ServerTakePiece(turret); yield return Shot("grab", Vector3.Lerp(tp, shore, 0.5f) + Vector3.up * 3f, 0.5f, 18f, 8f);
                        yield return Pause(1f);
                        Fx("kraken.ink", front, shore, 4000, 700); yield return Shot("ink", front, 1f, 14f, 9f);
                        Fx("kraken.whirl", shore + inward * 4f, shore, 4000, 1500); yield return Shot("whirl", shore + inward * 4f, 1.5f, 14f, 9f);
                        break;
                    case EnemyKind.Voxel:
                        Fx("voxel.select", front, front + side, 1600, 400); yield return Shot("select", front, 0.8f, 14f, 8f);
                        yield return Pause(0.8f);
                        Fx("voxel.delete", front, front + side, 0, 400); yield return Shot("delete", front, 0.25f, 14f, 8f);
                        yield return Pause(1f);
                        for (int k = 0; k < 6; k++) Fx("voxel.bolt", B, front + UnityEngine.Random.insideUnitSphere.Flat() * 5f);
                        yield return Shot("bolts", Vector3.Lerp(g, front, 0.5f), 0.2f, 16f, 8f);
                        yield return Pause(1f);
                        Fx("voxel.undo", B, B, 2000); yield return Shot("undo", bb.center, 1f, r * 2.4f + 4f, r * 0.4f);
                        break;
                    case EnemyKind.Koi:
                        Vector3 gate = center + (g - center).Flat().normalized * 38f; gate.y = WaterManager.GetWaterHeight(gate); // out at sea
                        Fx("koi.gate", gate, gate, 2200); yield return Shot("gate", gate + Vector3.up * 5f, 1.0f, 22f, 6f);
                        Fx("koi.jet", B + inward * r * 0.8f, g + inward * 24f, 1300, 28); yield return Shot("jet", g + inward * 12f, 0.6f, 16f, 9f);
                        yield return Pause(1f);
                        Fx("koi.splash", front, front, 0, 550); yield return Shot("splash", front, 0.15f, 16f, 9f);
                        yield return Pause(1f);
                        Fx("koi.breath", B + inward * r * 0.8f, g + inward * 21f, 1200, 30); yield return Shot("breath", g + inward * 10f, 0.6f, 16f, 9f);
                        yield return Pause(0.8f);
                        for (int k = 0; k < 4; k++) Fx("koi.thunder", front + UnityEngine.Random.insideUnitSphere.Flat() * 8f, front);
                        yield return Shot("thunder", front, 0.06f, 18f, 10f);
                        yield return Pause(1f);
                        Fx("koi.dragon", B, B, 2600); yield return Shot("dragon", bb.center, 2.8f, r * 2.4f + 4f, r * 0.5f);
                        break;
                    default:
                        Fx("eye.open", B, B, 1600); Fx("eye.beam", B, g + inward * 34f, 2400, 32); yield return Shot("beam", g + inward * 16f, 1.2f, 22f, 12f);
                        yield return Pause(1.3f);
                        for (int k = 0; k < 20; k++) Fx("eye.spear", center + UnityEngine.Random.insideUnitSphere.Flat() * 18f, center);
                        yield return Shot("spears", center, 0.35f, 24f, 14f); yield return Shot("spears2", center, 0.25f, 24f, 14f);
                        yield return Pause(1f);
                        Fx("eye.echoLane", center - side * 24f, center + side * 24f, 0, 500); Fx("eye.echo", front, front, 0, 260); yield return Shot("echoes", front, 0.3f, 18f, 10f);
                        yield return Pause(1f);
                        Fx("eye.judgment", B, Siege.Instance.CorePos, 3000); yield return Shot("judgment", Siege.Instance.CorePos + Vector3.up * 6f, 2.6f, 26f, 10f);
                        Fx("eye.verdict", Siege.Instance.CorePos, Siege.Instance.CorePos); yield return Shot("verdict", Siege.Instance.CorePos, 0.15f, 26f, 10f);
                        Fx("eye.red", B, B, 0);
                        break;
                }
                yield return new WaitForSecondsRealtime(1f);
                FortressPlugin.Frozen = false;
                Reveal.Instance.TestViewEnd();
                c.ServerChangeHp(10000000);
                yield return new WaitForSecondsRealtime(3f);
                Note($"{kind}: shown; trophy dropped {(Counters.TryGetValue("trophy_dropped_" + kind, out int td) ? td : 0)}");
            }
            // The ten trophies in a row on the plaza.
            Vector3 row = center + new Vector3(0f, 0f, 6f);
            for (int k = 0; k < EnemyCatalog.BossOrder.Length; k++)
            {
                Vector3 p = row + new Vector3((k - 4.5f) * 1.1f, 0f, 0f);
                p.y = BossFx.Ground(p) + 0.6f;
                BossTrophies.ServerSpawn(EnemyCatalog.BossOrder[k], p);
            }
            yield return new WaitForSecondsRealtime(2.5f);
            Reveal.Instance.TestView(row + new Vector3(0f, 3f, -6f), row);
            yield return new WaitForSecondsRealtime(0.8f);
            FortressPlugin.Shot("fx_trophies");
            Reveal.Instance.TestViewEnd();
            plugin.DebugStopRound();
        }

        // The 10/18 playtest's fixes one after another: fish scales on the ground walked over, the chest on E (not on a step),
        // a dropped weapon walked over into the vault, a take-out handed over, Shift+J mending everything, the lighthouse
        // mended at its door, a turret lost said to everyone, the run's bonus; pictures of the scales, the counter, the bar.
        private IEnumerator PlaytestScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            var loot = Loot.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            yield return Revive();
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            float stand = FortressIsland.StandHeight;
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * stand, 0f, true);
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return Until(() => plugin.Running && plugin.InBuildTime, 15f, "round start");
            plugin.DebugExtendBuild(3000f);
            Siege.Instance.ServerAddCoins(Siege.Money(100000));
            int C(string k) => Counters.TryGetValue(k, out int n) ? n : 0;
            while (loot.Vault.StorageSlots - loot.Vault.SlotsUsed < 8) loot.Vault.Bought[Expansion.Storage] = loot.Vault.BoughtOf(Expansion.Storage) + 1;
            IEnumerator Walk(Vector3 p) { me.LocalTeleport(p + Vector3.up * stand, 0f, true); yield return new WaitForSecondsRealtime(0.5f); }

            // Fish scales: a ring of them, a picture, then walked over one by one.
            Vector3 spot = center + new Vector3(6f, 0f, 6f);
            spot.y = BossFx.Ground(spot);
            int scales0 = loot.Vault.Scales;
            loot.ServerDropScales(spot, 12, 6, 2.5f);
            yield return new WaitForSecondsRealtime(1f);
            Reveal.Instance.TestView(spot + new Vector3(3.5f, 2.5f, -3.5f), spot);
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("pt_scales");
            Reveal.Instance.TestViewEnd();
            yield return new WaitForSecondsRealtime(1.5f);
            foreach (var p in loot.DebugScales) yield return Walk(p);
            yield return new WaitForSecondsRealtime(0.5f);
            FortressPlugin.Shot("pt_scales_counter");
            Note($"scales: dropped 12, picked {C("scales_picked")}, team scales {scales0} -> {loot.Vault.Scales} (expect +12), left lying {loot.DebugScales.Count}");

            // The chest: stepping on it does nothing now, E opens it.
            int opened0 = C("chests_opened");
            loot.ServerSpawnChest(plugin.Wave);
            yield return new WaitForSecondsRealtime(1f);
            var chest = loot.DebugChest;
            yield return Walk(chest.pos);
            yield return new WaitForSecondsRealtime(1f);
            int afterStep = C("chests_opened") - opened0;
            FishNet.InstanceFinder.ClientManager.Broadcast(new LootPick { Id = chest.id });
            yield return new WaitForSecondsRealtime(1.5f);
            Note($"chest: opened by stepping on it {afterStep} (expect 0), by E {C("chests_opened") - opened0 - afterStep} (expect 1), scales by it {loot.DebugScales.Count}");
            FortressPlugin.Shot("pt_chest");

            // A dropped weapon walked over goes into the vault; taken out, it's handed over.
            int hooks0 = loot.Vault.Count(Gear.Hook, Rarity.Rare), auto0 = C("weapon_auto_stored");
            Vector3 dropAt = center + new Vector3(-5f, 0f, 5f);
            loot.DebugDrop(Gear.Hook, Rarity.Rare, dropAt + Vector3.up);
            yield return new WaitForSecondsRealtime(1.5f);
            var dropped = FindObjectsOfType<Item>().FirstOrDefault(i => i && i.IsSpawned && WeaponItems.Is(i, out var dg, out var dr) && dg == Gear.Hook && dr == Rarity.Rare && !i.Holder && !i.IsInInventory);
            if (dropped) yield return Walk(dropped.transform.position);
            yield return new WaitForSecondsRealtime(1f);
            Note($"dropped weapon walked over: rare hooks in the vault {hooks0} -> {loot.Vault.Count(Gear.Hook, Rarity.Rare)} (expect +1), auto-stored {C("weapon_auto_stored") - auto0}");
            loot.ServerRequest(me, new VaultRequest { Action = VaultAction.Take, Gear = Gear.Hook, Rarity = Rarity.Rare, Count = 1 });
            yield return new WaitForSecondsRealtime(2f);
            bool inHand = me.Inventory && me.Inventory._items.Values.Any(i => i && WeaponItems.Is(i, out var hg, out var hr) && hg == Gear.Hook && hr == Rarity.Rare) || WeaponItems.Held(me, out var held, out _) && held == Gear.Hook;
            Note($"taken out of the vault: in my hands {inHand} (expect True)");

            // Shift+J: a small fort, battered, then everything mended at once (the build-time bar shows the price first).
            Fort(center);
            yield return new WaitForSecondsRealtime(1f);
            var ids = BuildSystem.Instance.ServerPieces.Select(p => p.Id).ToList();
            foreach (var id in ids) BuildSystem.Instance.ServerDamagePiece(id, 60);
            yield return new WaitForSecondsRealtime(1f);
            int hurt = BuildSystem.Instance.ServerPieces.Count(p => p.Hp < p.MaxHp);
            FortressPlugin.Shot("pt_buildbar");
            FishNet.InstanceFinder.ClientManager.Broadcast(new PieceUpgradeRequest { Id = -1, Repair = true });
            yield return new WaitForSecondsRealtime(1f);
            Note($"mend all: {hurt} damaged -> {BuildSystem.Instance.ServerPieces.Count(p => p.Hp < p.MaxHp)} (expect 0)");

            // The lighthouse at its door.
            Siege.Instance.ServerDamageCoreForTest(300);
            int core0 = Siege.Instance.View.CoreHp;
            yield return new WaitForSecondsRealtime(0.5f);
            core0 = Siege.Instance.CoreHp;
            FishNet.InstanceFinder.ClientManager.Broadcast(new GateRequest { Pull = 2 });
            yield return new WaitForSecondsRealtime(1f);
            Note($"lighthouse mended at its door: {core0} -> {Siege.Instance.CoreHp} of {Siege.Instance.CoreMax} (expect +10%), repairs {C("core_repaired")}");

            // A turret lost: told to everyone (bottom left).
            var gun = BuildSystem.Instance.ServerPieces.FirstOrDefault(p => Catalog.IsTurret(p.Type));
            if (gun.Id != 0) BuildSystem.Instance.ServerDamagePiece(gun.Id, 999999);
            yield return new WaitForSecondsRealtime(1.5f);
            FortressPlugin.Shot("pt_turret_lost");
            Note($"turret destroyed: pieces destroyed {C("pieces_destroyed")}");

            // The run's bonus: two waves held, then the round stopped.
            plugin.DebugJumpToWave(2);
            yield return Until(() => plugin.Wave >= 2 && !plugin.InBuildTime, 30f, "wave 2");
            for (int i = 0; i < 30 && plugin.Running && !plugin.InBuildTime; i++) { plugin.DebugKillAll(); yield return new WaitForSecondsRealtime(1f); }
            yield return Until(() => plugin.InBuildTime, 20f, "wave 2 cleared");
            int bonus0 = C("settle_bonus");
            plugin.DebugStopRound();
            yield return Until(() => !plugin.Running, 10f, "round over");
            yield return new WaitForSecondsRealtime(2f);
            FortressPlugin.Shot("pt_results");
            Note($"run bonus: {C("settle_bonus") - bonus0} (expect {FortressPlugin.SettleBonus(2)} for 2 waves)");
            FortressIsland.Instance.CloseResults();
        }

        // A fresh test world can start with the player drowned (the game's opening): brought back to life before pictures.
        private IEnumerator Revive()
        {
            var me = Player.LocalPlayer;
            if (!me || !me.Dying.IsDead) yield break;
            AccessTools.Method(typeof(PlayerDying), "LocalRespawn").Invoke(me.Dying, null);
            yield return Until(() => !Player.LocalPlayer.Dying.IsDead, 10f, "the player to respawn");
            yield return new WaitForSecondsRealtime(1f);
        }

        // A gun turret at `at` for a move to act on; its piece id.
        private static int TurretFor(Vector3 at)
        {
            int before = BuildSystem.Instance.ServerPieces.Select(x => x.Id).DefaultIfEmpty(0).Max();
            TurretAt(PieceType.Gun, at + Vector3.up * 2f);
            int id = BuildSystem.Instance.ServerPieces.Select(x => x.Id).DefaultIfEmpty(0).Max();
            return id > before ? id : before;
        }

        // The game's creatures as boss bodies: each blown up to boss size on a beach, a picture, and what it's made of.
        private IEnumerator BodiesScenario()
        {
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            var me = Player.LocalPlayer;
            var all = (Dictionary<string, Item>)AccessTools.Field(typeof(GameInfo), "_nameToSpawnable").GetValue(null);
            Vector3 center = island.Center;
            float a = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 spot = center + gd * 21f, eye = center + gd * 8f + Vector3.up * (FortressIsland.StandHeight + 2.5f);
            float ground = Physics.Raycast(spot + Vector3.up * 20f, Vector3.down, out var gh, 40f, GameInfo.LevelLayer) ? gh.point.y : spot.y;
            string[] names = (_arg.Length > 0 ? _arg : "dripper,anglerfish,oarfish,squid,lobster,browncrab,rockcrab,seahorse,starfish,stonefish,blobfish,eel,flyingfish,seagull,footsnail,tripodfish,sengarat,voxelfish,leech,clam,seaurchin,halibut,pike,bingbong,dogfish,gar,catfish,needlefish,triggerfish,tigerfish,koifish,goldfish,sponge,shrimp,mahi-mahi,spidercrab,pufferfish,manta,albatross,bowheadwhale,giantpiranha,mutatedbowheadwhale,goblinshark,theoldpike").Split(',');
            foreach (string name in names)
            {
                if (!all.TryGetValue(name, out var prefab) || !prefab) { Note($"{name}: no such spawnable"); continue; }
                var proto = prefab.GetComponent<Creature>();
                var b0 = new Bounds(prefab.transform.position, Vector3.zero);
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true)) if (!(r is ParticleSystemRenderer)) b0.Encapsulate(r.bounds);
                float size = Mathf.Max(b0.size.x, Mathf.Max(b0.size.y, b0.size.z)), scale = 7f / Mathf.Max(0.05f, size);
                var item = Instantiate(prefab, spot + Vector3.up * 2f, Quaternion.LookRotation(-gd));
                item.transform.localScale = Vector3.one * scale;
                var rs = item.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    item.transform.position += (spot - new Vector3(b.center.x, spot.y, b.center.z)) + Vector3.up * (ground + 0.2f - b.min.y);
                }
                foreach (var rb in item.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
                var c = item.GetComponent<Creature>();
                if (c) Bosses.Puppets.Add(c); // held where it's put (fish out of water flop, birds fly off)
                Server.Instance.Spawn(item.gameObject);
                me.LocalTeleport(eye, YawTo(eye, spot), true);
                yield return null;
                Aim(YawTo(eye, spot), 8f);
                yield return new WaitForSecondsRealtime(1.3f);
                FortressPlugin.Shot("body_" + name);
                var anim = item.GetComponentInChildren<Animator>();
                string parms = anim && anim.runtimeAnimatorController ? string.Join(" ", anim.parameters.Select(p => p.name + ":" + p.type)) : "-";
                string mats = string.Join(", ", rs.SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.name + "/" + (m.shader ? m.shader.name : "-") + (m.HasProperty("_Colors") ? "[Colors]" : m.HasProperty("_BaseMap") ? "[BaseMap]" : "")).Distinct());
                string comps = string.Join(",", item.GetComponentsInChildren<MonoBehaviour>(true).Select(m => m.GetType().Name).Where(n => !n.StartsWith("Network")).Distinct());
                Note($"{name}: {(proto ? proto.GetType().Name : "?")} {(proto ? proto.BossType.ToString() : "")}, size {size:F2} x{scale:F1}, max hp {(proto ? proto.MaxHp : 0)}, renderers {string.Join(",", rs.Select(r => r.GetType().Name).Distinct())} ({rs.Length}), animator {(anim ? anim.name : "-")} [{parms}], comps {comps}; mats {mats}");
                item.DestroyItem();
                yield return new WaitForSecondsRealtime(0.4f);
            }
        }

        // The game's own bosses (spider crab, whale, manta, albatross, pufferfish, piranha king): what they are, and what they
        // do when they turn up at the fortress.
        private IEnumerator BossProbeScenario()
        {
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            var me = Player.LocalPlayer;
            var all = (Dictionary<string, Item>)HarmonyLib.AccessTools.Field(typeof(GameInfo), "_nameToSpawnable").GetValue(null);
            var bosses = all.Where(kv => kv.Value && kv.Value.GetComponent<Creature>() is Creature c && c.BossType != BossType.None).ToList();
            foreach (var kv in bosses)
            {
                var c = kv.Value.GetComponent<Creature>();
                var b = new Bounds(kv.Value.transform.position, Vector3.zero);
                foreach (var r in kv.Value.GetComponentsInChildren<Renderer>(true)) b.Encapsulate(r.bounds);
                Note($"boss prefab '{kv.Key}': {c.GetType().Name}, {c.BossType}, max hp {c.MaxHp}, hp mult {c.BossHpMultiplier}, time {c.BossTimeInSeconds}s, size {b.size:F1}, scale {kv.Value.transform.localScale:F2}");
            }
            Vector3 center = island.Center;
            Vector3 stand = center + new Vector3(0f, 1.2f + FortressIsland.StandHeight, 6f);
            foreach (var kv in bosses)
            {
                var proto = kv.Value.GetComponent<Creature>();
                string cls = proto.GetType().Name;
                bool water = cls == "Manta" || cls == "Piranha", air = cls == "Albatross" || cls == "BowheadWhale";
                Vector3 at = center + new Vector3(0f, 0f, water ? 34f : 18f);
                at.y = water ? WaterManager.GetWaterHeight(at) - 1f : air ? 12f : 2.5f;
                var item = Instantiate(kv.Value, at, Quaternion.Euler(0f, 180f, 0f));
                Server.Instance.Spawn(item.gameObject);
                var boss = item.Creature ? item.Creature : item.GetComponent<Creature>();
                me.LocalTeleport(stand, 0f, true);
                yield return null;
                Aim(0f, -2f);
                float t0 = Time.realtimeSinceStartup;
                yield return new WaitForSecondsRealtime(4f);
                FortressPlugin.Shot("bossprobe_" + kv.Key + "_a");
                yield return new WaitForSecondsRealtime(5f);
                FortressPlugin.Shot("bossprobe_" + kv.Key + "_b");
                Note($"{kv.Key} ({cls}): at {(boss ? boss.transform.position - center : Vector3.zero):F1} after 9 s, hp {(boss ? boss._hp.Value : -1)}, boss fight {(BossManager.Boss ? BossManager.Boss.name : "none")}, max {BossManager.BossMaxHp}, dead {(boss && boss.IsDead)}");
                if (boss && boss.IsSpawned) boss.DestroyItem();
                yield return new WaitForSecondsRealtime(2.5f);
            }
        }

        // The shark whistle's indicator: the circle and the mark on the sea where you aim, the shark surfacing on the mark,
        // the grey of the cooldown, and a legendary's pair of marks.
        private IEnumerator SharkAimScenario()
        {
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            float g0 = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g0), 0f, Mathf.Sin(g0));
            Vector3 feet = center + gd * 15f;
            if (Physics.Raycast(feet + Vector3.up * 20f, Vector3.down, out var gh, 40f, GameInfo.LevelLayer)) feet.y = gh.point.y;
            Item Hold(Rarity r)
            {
                foreach (var i in me.Inventory._items.Values.Append(me.Holding.HeldItem).Where(x => x).Distinct().ToList()) { me.Inventory.RemoveItem(i); i.DestroyItem(); }
                var it = WeaponItems.ServerSpawn(Gear.SharkWhistle, r, feet + Vector3.up * 1.5f);
                if (it) Server.Instance.RpcLogic___SetItemHolder___3876263354(it, me, null);
                return it;
            }
            foreach (var (r, yawOff, tag) in new[] { (Rarity.Rare, 0f, "rare"), (Rarity.Legendary, 25f, "legend") })
            {
                Hold(r);
                me.LocalTeleport(feet + Vector3.up * FortressIsland.StandHeight, Mathf.Atan2(gd.x, gd.z) * Mathf.Rad2Deg + yawOff, true);
                yield return null;
                Aim(Mathf.Atan2(gd.x, gd.z) * Mathf.Rad2Deg + yawOff, 14f);
                yield return new WaitForSecondsRealtime(1.5f);
                Vector3 mark = Weapons.Instance.DebugCastMark;
                FortressPlugin.Shot("shark_aim_" + tag, 0f);
                yield return new WaitForSecondsRealtime(0.3f);
                var cam = GameInfo.CurCamera.transform;
                Weapons.Instance.ServerFire(me, new WeaponFire { Phase = 0, Origin = cam.position, Dir = cam.forward });
                yield return new WaitForSecondsRealtime(0.5f);
                Vector3 shark = Weapons.Instance.DebugLastShark;
                Note($"{tag}: mark at {(mark - center).Flat().magnitude:0.0} m from the island's middle, the shark surfaced {(shark - mark).Flat().magnitude:0.0} m from the mark (expect under 1.5)");
                yield return new WaitForSecondsRealtime(0.8f);
                FortressPlugin.Shot("shark_aim_" + tag + "_cooling", 0f);
                yield return new WaitForSecondsRealtime(0.3f);
            }
        }

        // U on a Lv2 gun while walking about (not in build mode): the branch choice opens and stays open (it used to close
        // itself at once outside build mode), picking B makes it Lv3 B, and walking off closes it.
        // The lighthouse's ladder: W at its foot, facing it, climbs to the gallery; turned round, W walks you off through the gap
        // in the railing and down the ladder to the ground.
        private IEnumerator LadderScenario()
        {
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists && Siege.Instance.Lighthouse, 30f, "fortress island");
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            var me = Player.LocalPlayer;
            var lh = Siege.Instance.Lighthouse;
            var input = AccessTools.FieldRefAccess<PlayerMovement, Vector2>("_moveInput");
            Vector3 Local() => lh.InverseTransformPoint(me.Transform.position);
            Vector3 foot = lh.TransformPoint(new Vector3(Ladder.FaceX - 1.3f, 1.2f, 0f));
            me.LocalTeleport(foot, Quaternion.LookRotation((lh.position - foot).Flat()).eulerAngles.y, true);
            yield return new WaitForSecondsRealtime(1.5f);
            FortressPlugin.Shot("ladder_foot");
            float t0 = Time.time;
            bool climbed = false;
            while (Time.time - t0 < 10f && !(Local().y > Ladder.DeckY && me.Movement.Grounded && !Ladder.Climbing))
            {
                input(me.Movement) = new Vector2(0f, 1f);
                climbed |= Ladder.Climbing;
                if (Time.time - t0 > 1.2f && Time.time - t0 < 1.3f) FortressPlugin.Shot("ladder_climbing");
                yield return null;
            }
            input(me.Movement) = Vector2.zero;
            Note($"up: climbed {climbed}, {Time.time - t0:0.0} s, now at {Local():F2} on the gallery {Local().y > Ladder.DeckY} (climb starts {C("ladder_climb")})");
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("ladder_top");
            Vector3 here = me.Transform.position;
            me.LocalTeleport(here, Quaternion.LookRotation((here - lh.position).Flat()).eulerAngles.y, true); // turned to face out
            yield return new WaitForSecondsRealtime(0.5f);
            t0 = Time.time;
            while (Time.time - t0 < 10f && !(Local().y < 1f && me.Movement.Grounded))
            {
                input(me.Movement) = new Vector2(0f, 1f);
                if (Time.time - t0 > 1.5f && Time.time - t0 < 1.6f) FortressPlugin.Shot("ladder_down");
                yield return null;
            }
            input(me.Movement) = Vector2.zero;
            Note($"down: {Time.time - t0:0.0} s, now at {Local():F2} (expect the ground in front of the ladder)");
            int C(string k) => Counters.TryGetValue(k, out int n) ? n : 0;
        }

        private IEnumerator BranchWalkScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            var me = Player.LocalPlayer;
            foreach (var it in me.Inventory._items.Values.Append(me.Holding.HeldItem).Where(x => x).Distinct().ToList()) { me.Inventory.RemoveItem(it); it.DestroyItem(); }
            Vector3 center = island.Center;
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * FortressIsland.StandHeight, 0f, true);
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return Until(() => plugin.Running && plugin.InBuildTime, 15f, "round start");
            plugin.DebugExtendBuild(600f);
            Siege.Instance.ServerAddCoins(Siege.Money(20000));
            var B = BuildSystem.Instance;
            float g = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g), 0f, Mathf.Sin(g));
            int before = B.ServerPieces.Select(x => x.Id).DefaultIfEmpty(0).Max();
            TurretAt(PieceType.Gun, center + gd * 9f + Vector3.up * 1.2f);
            int id = B.ServerPieces.Select(x => x.Id).DefaultIfEmpty(0).Max();
            if (id <= before) { Note("no gun placed"); yield break; }
            B.ServerUpgrade(id, 1);
            yield return new WaitForSecondsRealtime(1f);
            B.StopBuilding();
            B.TryGetState(id, out var s0);
            Vector3 eye = center + gd * 6.5f;
            if (Physics.Raycast(eye + Vector3.up * 20f, Vector3.down, out var gh, 40f, GameInfo.LevelLayer)) eye.y = gh.point.y + FortressIsland.StandHeight;
            me.LocalTeleport(eye, YawTo(eye, s0.Pos), true);
            yield return new WaitForSecondsRealtime(1f);
            B.DebugPressU(id);
            yield return new WaitForSecondsRealtime(0.8f);
            bool open = PieceMenu.IsOpen;
            FortressPlugin.Shot("branch_walkup", 0f);
            yield return new WaitForSecondsRealtime(0.4f);
            PieceMenu.Instance.DebugUpgrade(2);
            yield return new WaitForSecondsRealtime(1f);
            B.TryGetState(id, out var s1);
            bool stillOpen = PieceMenu.IsOpen;
            me.LocalTeleport(eye - gd * 8f, YawTo(eye, s0.Pos), true);
            yield return new WaitForSecondsRealtime(0.8f);
            Note($"walk-up U on a Lv{s0.Level} gun (building {BuildSystem.Building}): menu open {open} (expect True), after picking B Lv{s1.Level} branch {s1.Branch} (expect 3, 2), menu {(stillOpen ? "still open" : "closed")}; walked 8 m off: menu open {PieceMenu.IsOpen} (expect False)");
            plugin.DebugStopRound();
        }

        // The fish stall from round about (front, both front corners, the side, the back) and close over its counter.
        private IEnumerator StallLookScenario()
        {
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            var me = Player.LocalPlayer;
            foreach (var it in me.Inventory._items.Values.Append(me.Holding.HeldItem).Where(x => x).Distinct().ToList()) { me.Inventory.RemoveItem(it); it.DestroyItem(); }
            yield return new WaitForSecondsRealtime(2f);
            Vector3 c = island.MarketPos + Vector3.up * 1.2f;
            var face = Quaternion.Euler(0f, FortressIsland.MarketYaw, 0f); // the customers' side
            var views = new[] { (0f, 6.5f, 8f, "front"), (-40f, 6.5f, 10f, "left"), (40f, 6.5f, 10f, "right"), (100f, 6f, 8f, "side"), (180f, 6f, 8f, "back"), (0f, 3f, 22f, "counter") };
            foreach (var (ang, dist, pitch, tag) in views)
            {
                Vector3 eye = c + face * (Quaternion.Euler(0f, ang, 0f) * Vector3.forward) * dist;
                if (Physics.Raycast(eye + Vector3.up * 30f, Vector3.down, out var gh, 60f, GameInfo.LevelLayer)) eye.y = gh.point.y + FortressIsland.StandHeight;
                me.LocalTeleport(eye, YawTo(eye, c), true);
                yield return null;
                Aim(YawTo(eye, c), pitch);
                yield return new WaitForSecondsRealtime(1.3f);
                FortressPlugin.Shot("stall_" + tag);
                yield return new WaitForSecondsRealtime(0.3f);
            }
        }

        // Reference for the fish stall: the game's own shops, stands and counters wherever they are (other islands' too, switched
        // off), listed, and each one written out as an OBJ with its palette coordinates for Blender.
        private IEnumerator ShopRefScenario()
        {
            yield return new WaitForSecondsRealtime(2f);
            string dir = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "fortress-shots");
            var words = new[] { "shop", "store", "kiosk", "stand", "stall", "market", "grill", "counter", "booth", "vendor", "cart", "awning", "casino", "bar" };
            var roots = Resources.FindObjectsOfTypeAll<Transform>()
                .Where(t => t && t.gameObject.scene.IsValid() && words.Any(w => t.name.ToLower().Contains(w)) && t.GetComponentsInChildren<MeshRenderer>(true).Length > 0)
                .Where(t => !t.name.StartsWith("Fortress") && !PathOf(t).Contains("Fortress"))
                .ToList();
            // Only the outermost of nested matches.
            roots = roots.Where(t => !roots.Any(o => o != t && t.IsChildOf(o))).ToList();
            int k = 0;
            foreach (var t in roots.OrderByDescending(t => t.GetComponentsInChildren<MeshRenderer>(true).Length).Take(14))
            {
                var rends = t.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.GetComponent<MeshFilter>() && r.GetComponent<MeshFilter>().sharedMesh).ToList();
                Note($"shop {k}: {PathOf(t)} active {t.gameObject.activeInHierarchy} at {t.position:F0}, {rends.Count} meshes, mats {string.Join("/", rends.SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.name).Distinct().Take(6))}");
                // One OBJ in the shop's own space: every part's mesh (readable or not: baked through a copy) with its UVs.
                var sb = new System.Text.StringBuilder();
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                int vbase = 1;
                foreach (var r in rends)
                {
                    var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                    Vector3[] vs; Vector2[] uvs; int[] tris;
                    try { vs = mesh.vertices; uvs = mesh.uv; tris = mesh.triangles; } catch { continue; }
                    if (vs.Length == 0) continue;
                    var m = t.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    sb.Append($"o {r.name}\n");
                    foreach (var v in vs) { var w = m.MultiplyPoint3x4(v); sb.Append($"v {(-w.x).ToString("R", inv)} {w.y.ToString("R", inv)} {w.z.ToString("R", inv)}\n"); }
                    bool hasUv = uvs.Length == vs.Length;
                    if (hasUv) foreach (var uv in uvs) sb.Append($"vt {uv.x.ToString("R", inv)} {uv.y.ToString("R", inv)}\n");
                    for (int q = 0; q < tris.Length; q += 3)
                    {
                        int a = tris[q] + vbase, b = tris[q + 2] + vbase, c = tris[q + 1] + vbase;
                        sb.Append(hasUv ? $"f {a}/{a} {b}/{b} {c}/{c}\n" : $"f {a} {b} {c}\n");
                    }
                    vbase += vs.Length;
                }
                if (vbase > 1) System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"shopref_{k}_{t.name.Replace(' ', '_')}.obj"), sb.ToString());
                k++;
            }
            // Photographs of the ones in this island's scene, from round about and close up.
            var me = Player.LocalPlayer;
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            foreach (var t in roots.Where(t => t.gameObject.activeInHierarchy && t.name.StartsWith("TreeShop") || t.name.StartsWith("SlotMachine")).Take(2))
            {
                var b = t.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
                Note($"{t.name}: bounds {b.size:F1} centre {b.center:F1}");
                for (int a = 0; a < 4; a++)
                {
                    float ang = a * 90f + 30f;
                    Vector3 dirTo = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                    Vector3 eye = b.center + dirTo * Mathf.Max(4f, b.extents.magnitude * 1.6f);
                    if (Physics.Raycast(eye + Vector3.up * 30f, Vector3.down, out var gh, 60f, GameInfo.LevelLayer)) eye.y = gh.point.y + FortressIsland.StandHeight;
                    me.LocalTeleport(eye, YawTo(eye, b.center), true);
                    yield return null;
                    Aim(YawTo(eye, b.center), Mathf.Clamp(Mathf.Atan2(eye.y - b.center.y, (b.center - eye).Flat().magnitude) * Mathf.Rad2Deg, -20f, 30f));
                    yield return new WaitForSecondsRealtime(1.4f);
                    FortressPlugin.Shot($"shopref_{t.name.Replace(' ', '_').Replace("(", "").Replace(")", "")}_{a}");
                    yield return new WaitForSecondsRealtime(0.3f);
                }
            }
        }

        // The village's people as the game draws its own: every keeper close up and full length, the fishmonger talking (her
        // hand up, her mouth going), and the stall's helpers in their different outfits.
        private IEnumerator PeopleScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            var loot = Loot.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            var me = Player.LocalPlayer;
            yield return new WaitForSecondsRealtime(2f);
            float Height(Component p) { float top = float.MinValue; foreach (var r in p.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y); return top - p.transform.position.y; }
            Vector3 Stand(Vector3 w) { if (Physics.Raycast(w + Vector3.up * 20f, Vector3.down, out var rh, 40f, GameInfo.LevelLayer)) w.y = rh.point.y; return w + Vector3.up * FortressIsland.StandHeight; }
            IEnumerator Photo(Transform who, float dist, float pitch, string shot)
            {
                Vector3 at = Stand(who.position + who.forward * dist);
                me.LocalTeleport(at, YawTo(at, who.position), true);
                yield return null;
                Aim(YawTo(at, who.position), pitch);
                yield return new WaitForSecondsRealtime(1.4f);
                FortressPlugin.Shot(shot);
                yield return new WaitForSecondsRealtime(0.4f);
            }
            foreach (var it in me.Inventory._items.Values.Append(me.Holding.HeldItem).Where(x => x).Distinct().ToList()) { me.Inventory.RemoveItem(it); it.DestroyItem(); }
            // Side by side with the game's own swamp girl, in the same light: two of ours in her outfit.
            var swamp = Resources.FindObjectsOfTypeAll<NPC>().FirstOrDefault(n => n && n.gameObject.scene.IsValid() && n.gameObject.activeInHierarchy && n.name.StartsWith("Swamp Daughter"));
            if (swamp)
            {
                var spot = swamp.transform;
                var pa = Person.Make(new GameObject("CompareA").transform, "Helper1");
                var pb = Person.Make(new GameObject("CompareB").transform, "Helper1");
                pa.transform.parent.SetPositionAndRotation(spot.position + spot.right * 1.0f, spot.rotation);
                pb.transform.parent.SetPositionAndRotation(spot.position - spot.right * 1.0f, spot.rotation);
                Vector3 cam = Stand(spot.position + spot.forward * 3.2f);
                me.LocalTeleport(cam, YawTo(cam, spot.position), true);
                yield return null;
                Aim(YawTo(cam, spot.position), 4f);
                yield return new WaitForSecondsRealtime(1.5f);
                FortressPlugin.Shot("people_compare");
                yield return new WaitForSecondsRealtime(0.4f);
                Destroy(pa.transform.parent.gameObject); Destroy(pb.transform.parent.gameObject);
            }
            // A line-up on the open ground by the start circle: every keeper and eight helpers, facing the camera; one walking,
            // one carrying, one waving.
            {
                Vector3 c0 = Stand(island.Center + FortressIsland.ReadyLocal) - Vector3.up * FortressIsland.StandHeight;
                Vector3 across = Vector3.right, toCam = Vector3.forward;
                var ids = new[] { "Market", "Smith", "Vault", "Lighthouse", "Helper1", "Helper2", "Helper3", "Helper4", "Helper5", "Helper6", "Helper7", "Helper8" };
                var line = new List<Person>();
                for (int n = 0; n < ids.Length; n++)
                {
                    var holder = new GameObject("Lineup" + ids[n]).transform;
                    Vector3 at = c0 + across * ((n % 6) - 2.5f) * 1.1f - toCam * (n / 6) * 2.2f;
                    if (Physics.Raycast(at + Vector3.up * 10f, Vector3.down, out var gh, 30f, GameInfo.LevelLayer)) at.y = gh.point.y;
                    holder.SetPositionAndRotation(at, Quaternion.LookRotation(toCam));
                    var person = Person.Make(holder, ids[n]);
                    line.Add(person);
                    float top = person.GetComponentsInChildren<Renderer>().Select(r => r.bounds.max.y).DefaultIfEmpty(at.y).Max();
                    Note($"line-up {ids[n]}: {(person.transform.Find("Character") ? "the game's body" : "modelled")}, {top - at.y:0.00} m tall");
                }
                bool posing = true;
                IEnumerator Animate() { float t = 0f; while (posing) { t += Time.deltaTime; for (int n = 0; n < line.Count; n++) if (line[n]) line[n].Pose(n == 5 ? t * 7f : 0f, n == 5 ? 1f : 0f, n == 6, n == 0 ? 1f : 0f, 0f, 0f); yield return null; } }
                StartCoroutine(Animate());
                Vector3 cam = Stand(c0 + toCam * 4.2f - across * 0.6f);
                me.LocalTeleport(cam, YawTo(cam, c0 - toCam * 1f), true);
                yield return null;
                Aim(YawTo(cam, c0 - toCam * 1f), 8f);
                yield return new WaitForSecondsRealtime(1.5f);
                FortressPlugin.Shot("people_lineup");
                yield return new WaitForSecondsRealtime(0.4f);
                for (int n = 0; n < 4; n++)
                {
                    var face = line[n].transform.position + Vector3.up * 1.45f;
                    Vector3 near = Stand(line[n].transform.position + toCam * 1.5f);
                    me.LocalTeleport(near, YawTo(near, face), true);
                    yield return null;
                    Aim(YawTo(near, face), 2f);
                    yield return new WaitForSecondsRealtime(1.2f);
                    FortressPlugin.Shot("people_close_" + ids[n]);
                    yield return new WaitForSecondsRealtime(0.3f);
                }
                Vector3 side = Stand(c0 + across * 5f + toCam * 1.5f);
                me.LocalTeleport(side, YawTo(side, c0), true);
                yield return null;
                Aim(YawTo(side, c0), 8f);
                yield return new WaitForSecondsRealtime(1.2f);
                FortressPlugin.Shot("people_lineup_side");
                yield return new WaitForSecondsRealtime(0.4f);
                posing = false;
                foreach (var person in line) if (person) Destroy(person.transform.parent.gameObject);
            }
            foreach (var k in island.Keepers)
            {
                var p = k.Person;
                Note($"{k.Role}: {(p.transform.Find("Character") ? "the game's body" : "modelled")}, parts {string.Join(", ", p.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.gameObject.activeSelf).Select(r => r.name + ":" + (r.sharedMesh ? r.sharedMesh.name : "-")))}, {Height(p):0.00} m tall");
                yield return Photo(k.transform, 1.9f, 4f, "people_" + k.Role + "_face");
                yield return Photo(k.transform, 3.6f, 14f, "people_" + k.Role);
            }
            var market = island.Keepers.FirstOrDefault(k => k.Role == "Market");
            if (market)
            {
                HarmonyLib.AccessTools.Field(typeof(Keeper), "_talkUntil").SetValue(market, Time.time + 4f);
                yield return Photo(market.transform, 2.6f, 8f, "people_talking");
            }
            // The helpers about the stall.
            loot.Vault.Helpers = 8;
            loot.SaveVault();
            me.LocalTeleport(island.Center + FortressIsland.ReadyLocal + Vector3.up * FortressIsland.StandHeight, 0f, true);
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return Until(() => plugin.Running, 15f, "round start");
            yield return new WaitForSecondsRealtime(6f);
            var helpers = FindObjectsOfType<Person>().Where(x => x.name.StartsWith("PersonHelper")).ToList();
            Note($"helpers: {helpers.Count}: {string.Join(", ", helpers.Select(h => h.name + " " + string.Join("/", h.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.gameObject.activeSelf && r.name != "CharacterHead").Select(r => r.sharedMesh ? r.sharedMesh.name : "-"))))}");
            for (int i = 0; i < Mathf.Min(3, helpers.Count); i++)
            {
                var h = helpers[i].transform;
                Vector3 at = Stand(h.position + (h.forward + h.right * 0.4f).normalized * 3.2f);
                me.LocalTeleport(at, YawTo(at, h.position), true);
                yield return null;
                Aim(YawTo(at, h.position), 12f);
                yield return new WaitForSecondsRealtime(0.8f);
                FortressPlugin.Shot("people_helper" + i);
                yield return new WaitForSecondsRealtime(0.3f);
            }
            plugin.DebugStopRound();
        }

        // The absorber: "$???" in the shop till the first boss is beaten, then bought; it sucks in a trophy (in its glass, the
        // banner at the top), uses all ten trophies' moves on tough fish, copies a laser, swaps it for a pistol (the laser spat
        // out), shoots copies of the pistol, and spits the pistol out at a fish.
        private IEnumerator AbsorberScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            var loot = Loot.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            yield return Revive();
            var me = Player.LocalPlayer;
            ulong key = Loot.KeyOf(me);
            var V = loot.Vault;
            Vector3 center = island.Center;
            float stand = FortressIsland.StandHeight;
            int C(string k) => Counters.TryGetValue(k, out int n) ? n : 0;
            int best0 = V.Best;
            V.Absorbed.Remove(key);
            V.Best = 0;
            loot.SaveVault();
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * stand, 0f, true);
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return Until(() => plugin.Running && plugin.InBuildTime, 15f, "round start");
            plugin.DebugExtendBuild(3000f);
            Siege.Instance.ServerAddCoins(Siege.Money(20000));
            foreach (var i in me.Inventory._items.Values.Append(me.Holding.HeldItem).Where(i => i).Distinct().ToList()) { me.Inventory.RemoveItem(i); i.DestroyItem(); }

            // The shop: locked, then open and bought.
            Vector3 anvil = island.SmithyPos + Quaternion.Euler(0f, FortressIsland.SmithyYaw, 0f) * new Vector3(-0.2f, 1.6f, 0.45f);
            Vector3 forge = island.SmithyPos + Quaternion.Euler(0f, FortressIsland.SmithyYaw, 0f) * new Vector3(0.6f, stand + 0.2f, 2.6f);
            me.LocalTeleport(forge, YawTo(forge, anvil), true);
            yield return new WaitForSecondsRealtime(1f);
            int coins0 = Siege.Instance.ServerCoins;
            loot.ServerRequest(me, new VaultRequest { Action = VaultAction.Buy, Gear = Gear.Absorber, Rarity = Rarity.Common });
            yield return new WaitForSecondsRealtime(1.5f);
            Note($"locked (best 0): money {coins0} -> {Siege.Instance.ServerCoins} (expect the same), in hand '{(me.Holding.HeldItem ? me.Holding.HeldItem.GetName() : "nothing")}'");
            VaultUI.Instance.Open(VaultUI.Desk.Smith);
            VaultUI.Instance.DebugTab(2);
            VaultUI.Instance.DebugSelect(Gear.Absorber, Rarity.Common);
            yield return new WaitForSecondsRealtime(2f);
            FortressPlugin.Shot("absorber_shop_locked"); yield return new WaitForSecondsRealtime(0.5f);
            V.Best = Mathf.Max(best0, Loot.SpecialBest);
            loot.SaveVault();
            yield return Until(() => loot.ClientVault.Best >= Loot.SpecialBest, 5f, "unlocked");
            VaultUI.Instance.DebugSelect(Gear.Absorber, Rarity.Common);
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("absorber_shop_open"); yield return new WaitForSecondsRealtime(0.5f);
            VaultUI.Instance.Close();
            coins0 = Siege.Instance.ServerCoins;
            loot.ServerRequest(me, new VaultRequest { Action = VaultAction.Buy, Gear = Gear.Absorber, Rarity = Rarity.Common });
            yield return new WaitForSecondsRealtime(2f);
            Note($"bought: money {coins0} -> {Siege.Instance.ServerCoins} (expect -{Loot.ShopPrice(Gear.Absorber)}), in hand '{(me.Holding.HeldItem ? me.Holding.HeldItem.GetName() : "nothing")}', counted {C("weapon_bought")}");
            if (!(WeaponItems.Held(me, out var hg, out _) && hg == Gear.Absorber))
            {
                var bought = WeaponItems.ServerSpawn(Gear.Absorber, Rarity.Common, me.Transform.position + Vector3.up);
                yield return new WaitForSecondsRealtime(0.4f);
                if (bought) Server.Instance.RpcLogic___SetItemHolder___3876263354(bought, me, null);
            }

            // Suck in a trophy lying ahead.
            float g0 = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g0), 0f, Mathf.Sin(g0)), side = Vector3.Cross(Vector3.up, gd);
            Vector3 Ground(Vector3 w) { if (Physics.Raycast(w + Vector3.up * 10f, Vector3.down, out var rh, 30f, GameInfo.LevelLayer)) return rh.point; return w; }
            Vector3 feet = Ground(center + gd * 6f), eye = feet + Vector3.up * 1.6f, prop = Ground(center + gd * 10f), mid = Ground(center + gd * 16f);
            me.LocalTeleport(feet + Vector3.up * 0.2f, YawTo(eye, prop), true);
            plugin.DebugKillAll();
            var trophy = BossTrophies.ServerSpawn(EnemyKind.Warden, prop + Vector3.up * 0.6f);
            yield return new WaitForSecondsRealtime(2f);
            WeaponFire At(Vector3 p, byte phase) => new WeaponFire { Phase = phase, Origin = eye, Dir = (p - eye).normalized };
            Vector3 thing = trophy ? trophy.transform.position : prop;
            for (int t = 0; t < 8; t++) { Weapons.Instance.ServerFire(me, At(thing, t == 0 ? (byte)0 : (byte)1)); if (t == 2) FortressPlugin.Shot("absorber_sucking", 0f); yield return new WaitForSecondsRealtime(0.1f); }
            Weapons.Instance.ServerFire(me, At(thing, 2));
            yield return Until(() => loot.ClientVault.AbsorbedOf(key) != null, 5f, "absorbed");
            var heldNow = loot.ClientVault.AbsorbedOf(key);
            Note($"sucked in: id {(heldNow != null ? heldNow.Id : 0)} (the warden's trophy is {BossTrophies.IdOf(EnemyKind.Warden)}), worth {(heldNow != null ? heldNow.Worth : 0)}, the trophy gone {!trophy}, absorbed {C("absorbed")}");
            yield return new WaitForSecondsRealtime(1f);
            Aim(YawTo(eye, mid), 12f);
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("absorber_holding"); yield return new WaitForSecondsRealtime(0.4f);

            // Every trophy's move on two tough fish side by side.
            Creature Tank(Vector3 at) { var c = plugin.ServerSpawnMinion(EnemyKind.Tank, at + Vector3.up * 1.5f); if (c) Siege.Instance.Slow(c, 1f, 60f); return c; }
            foreach (var k in EnemyCatalog.BossOrder)
            {
                plugin.DebugKillAll();
                yield return new WaitForSecondsRealtime(0.4f);
                V.Absorbed[key] = new StoredItem { Saved = new SavedItem { Exists = true, ItemID = BossTrophies.IdOf(k) }, Worth = BossTrophies.Worth(k) };
                loot.SaveVault();
                var a = Tank(mid);
                var b = Tank(Ground(mid + side * 2.2f));
                me.LocalTeleport(feet + Vector3.up * 0.2f, YawTo(eye, mid), true);
                yield return new WaitForSecondsRealtime(1.3f);
                Weapons.Instance.DebugReady(me);
                int a0 = a ? a._hp.Value : 0, b0 = b ? b._hp.Value : 0, n0 = C("absorber_move_" + k);
                Weapons.Instance.ServerFire(me, At((a ? a.transform.position : mid) + Vector3.up * 0.5f, 0));
                FortressPlugin.Shot("absorber_" + k, k == EnemyKind.Koi ? 0.6f : k == EnemyKind.Voxel ? 0.4f : 0.25f);
                yield return new WaitForSecondsRealtime(k == EnemyKind.Kraken ? 3.2f : 2f);
                Note($"{k} move: went off {C("absorber_move_" + k) - n0}, fish health {a0} -> {(a ? a._hp.Value : 0)} and {b0} -> {(b ? b._hp.Value : 0)}");
            }
            plugin.DebugKillAll();

            // Copy a laser lying ahead (right button: the trophy in it is spat out on the way), then fire as it.
            yield return new WaitForSecondsRealtime(0.5f);
            V.Absorbed[key] = new StoredItem { Saved = new SavedItem { Exists = true, ItemID = BossTrophies.IdOf(EnemyKind.Urchin) }, Worth = BossTrophies.Worth(EnemyKind.Urchin) };
            loot.SaveVault();
            Weapons.Instance.DebugReady(me);
            var laser = WeaponItems.ServerSpawn(Gear.Laser, Rarity.Rare, prop + Vector3.up * 0.4f);
            yield return new WaitForSecondsRealtime(2f);
            int spat0 = C("absorber_spat");
            Vector3 lp = laser ? laser.transform.position : prop;
            Weapons.Instance.ServerFire(me, At(lp, Weapons.RightDown));
            Weapons.Instance.ServerFire(me, At(lp, Weapons.RightUp));
            yield return new WaitForSecondsRealtime(1.5f);
            heldNow = loot.ClientVault.AbsorbedOf(key);
            var spatTrophy = FindObjectsOfType<BossTrophyItem>().FirstOrDefault(t => t.Boss == EnemyKind.Urchin && t.GetComponent<Item>().IsSpawned);
            Note($"swap: now holds id {(heldNow != null ? heldNow.Id : 0)} (a rare laser is {WeaponItems.IdOf(Gear.Laser, Rarity.Rare)}), spat {C("absorber_spat") - spat0} (expect 1), the urchin's trophy back in the world {(bool)spatTrophy}");
            var tank = Tank(mid);
            yield return new WaitForSecondsRealtime(1.3f);
            int t0 = tank ? tank._hp.Value : 0, beam0 = C("absorber_copy_Laser");
            Vector3 TankAt() => (tank ? tank.transform.position : mid) + Vector3.up * 0.5f; // re-aimed every time: it bobs about in the shallows
            Vector3 tp = TankAt(), tp0 = tp;
            Weapons.Instance.ServerFire(me, At(tp, 0));
            for (int t = 0; t < 15; t++) { Weapons.Instance.ServerFire(me, At(TankAt(), 1)); if (t == 8) FortressPlugin.Shot("absorber_copy_laser", 0f); yield return new WaitForSecondsRealtime(0.1f); }
            Weapons.Instance.ServerFire(me, At(TankAt(), 2));
            Note($"copied laser: started {C("absorber_copy_Laser") - beam0}, tank health {t0} -> {(tank ? tank._hp.Value : 0)}, it drifted {(TankAt() - tp0).magnitude:0.0} m");

            // A pistol: shot out as copies of itself, then spat at the tank.
            var pistol = ItemManager.Instance.SpawnNewItem(GameInfo.GetSpawnable("pistol"), prop + Vector3.up * 0.4f, Quaternion.identity);
            yield return new WaitForSecondsRealtime(2f);
            Weapons.Instance.DebugReady(me);
            Vector3 pp = pistol ? pistol.transform.position : prop;
            Weapons.Instance.ServerFire(me, At(pp, Weapons.RightDown));
            Weapons.Instance.ServerFire(me, At(pp, Weapons.RightUp));
            yield return new WaitForSecondsRealtime(1.2f);
            heldNow = loot.ClientVault.AbsorbedOf(key);
            int s0 = C("absorber_shot");
            if (tank) tank.ServerChangeHp(999999); // the laser spat out on the way flies into it: a fresh one
            tank = Tank(mid);
            yield return new WaitForSecondsRealtime(1.3f);
            t0 = tank ? tank._hp.Value : 0;
            for (int i = 0; i < 3; i++) { Weapons.Instance.ServerFire(me, At(TankAt(), 0)); Weapons.Instance.ServerFire(me, At(TankAt(), 2)); if (i == 1) FortressPlugin.Shot("absorber_item_shot", 0.05f); yield return new WaitForSecondsRealtime(1f); }
            Note($"item shots: holds id {(heldNow != null ? heldNow.Id : 0)} worth {(heldNow != null ? heldNow.Worth : 0)}, shots {C("absorber_shot") - s0} (expect 3), tank health {t0} -> {(tank ? tank._hp.Value : 0)}");
            if (!tank || tank._hp.Value <= 0) { tank = Tank(mid); yield return new WaitForSecondsRealtime(1.3f); }
            t0 = tank ? tank._hp.Value : 0;
            int flung0 = C("hook_fling_hit");
            var pistols0 = FindObjectsOfType<Item>().Where(x => x && x.ID == 66).ToList();
            Weapons.Instance.ServerFire(me, At(TankAt(), Weapons.SpitKey));
            var path = new List<string>();
            for (int k = 0; k < 15; k++)
            {
                yield return new WaitForSecondsRealtime(0.1f);
                var sp = FindObjectsOfType<Item>().FirstOrDefault(x => x && x.ID == 66 && !pistols0.Contains(x));
                if (sp && tank) path.Add($"{(sp.transform.position - eye).magnitude:0.0}/{(sp.transform.position - (tank.transform.position + Vector3.up * 0.5f)).magnitude:0.0}{(sp.Rig ? (sp.Rig.isKinematic ? "k" : "") : "?")}");
            }
            Note($"spat pistol: metres from me / from the tank every 0.1 s: {string.Join(" ", path)}; the tank {(tank ? (TankAt() - eye).magnitude : -1f):0.0} m away");
            Note($"spat out: holds {(loot.ClientVault.AbsorbedOf(key) != null ? "something" : "nothing")} (expect nothing), hits {C("hook_fling_hit") - flung0}, tank health {t0} -> {(tank ? tank._hp.Value : 0)}");
            FortressPlugin.Shot("absorber_empty", 0f);
            V.Best = best0;
            loot.SaveVault();
            plugin.DebugStopRound();
        }

        // The ring of lamps: guns at Lv3, 4, 12, 20 and 25 in a row, seen from a step back and close up.
        private IEnumerator LampsScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * FortressIsland.StandHeight, 0f, true);
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return Until(() => plugin.Running && plugin.InBuildTime, 15f, "round start");
            plugin.DebugExtendBuild(900f);
            yield return new WaitForSecondsRealtime(3f);
            Siege.Instance.ServerAddCoins(Siege.Money(3000000));
            var B = BuildSystem.Instance;
            float g = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g), 0f, Mathf.Sin(g)), side = Vector3.Cross(Vector3.up, gd);
            Vector3 Spot(int i) => center + gd * 9f + side * (i - 2) * BuildSystem.Cell;
            int[] levels = { 3, 4, 12, 20, 25 };
            var ids = new List<int>();
            for (int i = 0; i < levels.Length; i++)
            {
                int before = B.ServerPieces.Select(x => x.Id).DefaultIfEmpty(0).Max();
                TurretAt(PieceType.Gun, Spot(i) + Vector3.up * 1.2f);
                int id = B.ServerPieces.Select(x => x.Id).DefaultIfEmpty(0).Max();
                for (int l = 1; l < levels[i] && id > before; l++) B.ServerUpgrade(id, 1);
                ids.Add(id > before ? id : 0);
            }
            yield return new WaitForSecondsRealtime(1f);
            foreach (int id in ids)
            {
                B.TryGetState(id, out var st);
                var look = B.PieceObject(id)?.transform.Find("LevelLook");
                Note($"gun {id}: Lv{st.Level}, lamps lit {BuildSystem.LampsLit(st.Level)}/{BuildSystem.Lamps} (expect {Mathf.Min(BuildSystem.Lamps, st.Level - 3)}), dim {(look && look.Find("LampsDim") ? "yes" : "no")}, lit {(look && look.Find("LampsLit") ? "yes" : "no")}, studs {(B.PieceObject(id)?.transform.Find("RankStuds") ? "yes" : "no")} (expect no)");
            }
            IEnumerator View(Vector3 eye, Vector3 at, string shot, float pitch)
            {
                if (Physics.Raycast(eye + Vector3.up * 40f, Vector3.down, out var rh, 80f, GameInfo.LevelLayer)) eye.y = rh.point.y + FortressIsland.StandHeight;
                me.LocalTeleport(eye, YawTo(eye, at), true);
                yield return null;
                Aim(YawTo(eye, at), pitch);
                yield return new WaitForSecondsRealtime(1.5f);
                FortressPlugin.Shot(shot);
                yield return new WaitForSecondsRealtime(0.4f);
            }
            yield return View(center + gd * 4f, Spot(2), "lamps_row", 14f);
            for (int i = 1; i < levels.Length; i++)
                yield return View(Spot(i) - gd * 2.6f + side * 0.5f, Spot(i), "lamps_lv" + levels[i], 32f);
        }

        // Towers without a level cap: how a gun looks at Lv1, 2, 3 (each branch), 5 and 10, a Lv5 gun shooting two fish at once,
        // what the next levels cost, and moving a tower (it keeps its level and health).
        private IEnumerator TowersScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * FortressIsland.StandHeight, 0f, true);
            yield return Until(() => plugin.Running, 15f, "round start");
            yield return Until(() => plugin.InBuildTime, 5f, "build time");
            plugin.DebugExtendBuild(900f);
            yield return new WaitForSecondsRealtime(3f);
            Siege.Instance.ServerAddCoins(Siege.Money(200000));
            var B = BuildSystem.Instance;
            float g = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g), 0f, Mathf.Sin(g)), side = Vector3.Cross(Vector3.up, gd);
            // Four guns in a row, upgraded to different levels, plus a cannon on its B branch.
            int Place(PieceType t, Vector3 at) { int before = B.ServerPieces.Select(x => x.Id).DefaultIfEmpty(0).Max(); TurretAt(t, at); return B.ServerPieces.Select(x => x.Id).DefaultIfEmpty(0).Max() is int id && id > before ? id : 0; }
            var ids = new List<(int id, int level, int branch)>();
            int[] levels = { 1, 2, 3, 5, 10 };
            for (int i = 0; i < levels.Length; i++)
            {
                int id = Place(PieceType.Gun, center + gd * 9f + side * (i - 2) * BuildSystem.Cell + Vector3.up * 1.2f);
                for (int l = 1; l < levels[i]; l++) B.ServerUpgrade(id, 1);
                ids.Add((id, levels[i], 1));
            }
            int cannon = Place(PieceType.Cannon, center + gd * 12f + Vector3.up * 1.2f);
            for (int l = 1; l < 6; l++) B.ServerUpgrade(cannon, 2);
            yield return new WaitForSecondsRealtime(1f);
            foreach (var (id, lv, br) in ids)
                Note($"gun {id}: {(B.TryGetState(id, out var st) ? $"Lv{st.Level} branch {st.Branch} hp {st.MaxHp}" : "missing")}, barrels {B.PieceObject(id)?.transform.Find("Head")?.Cast<Transform>().Count(t => t.name.StartsWith("Barrel") && t.name != "BarrelCrystal")}, studs {B.PieceObject(id)?.transform.Find("RankStuds")?.childCount ?? 0}");
            Note($"gun costs to the next level: {string.Join(", ", Enumerable.Range(1, 12).Select(l => $"Lv{l}->{l + 1} ${Catalog.UpgradeCost(PieceType.Gun, l)}"))}");
            Note($"gun stats: {string.Join("; ", new[] { 1, 3, 4, 5, 10, 15 }.Select(l => { var t = Catalog.Turret(PieceType.Gun, l, 1); return $"Lv{l} dmg {t.Damage} every {t.Interval:0.00}s range {t.Range:0.0} targets {t.Targets}"; }))}");
            Vector3 eye = center + gd * 4.5f + Vector3.up * 1.6f;
            yield return Look(eye, YawTo(eye, center + gd * 9f), "towers_row", 1.5f);
            Vector3 close = center + gd * 6.2f + side * 1.5f * BuildSystem.Cell;
            yield return Look(close, YawTo(close, center + gd * 9f + side * 1.5f * BuildSystem.Cell), "towers_lv5_lv10", 1.5f);
            Vector3 cc = center + gd * 12f;
            yield return Look(cc - gd * 3.2f + side * 1.2f, YawTo(cc - gd * 3.2f + side * 1.2f, cc), "towers_cannon_lv6B", 1.5f);

            // Move the Lv10 gun two cells over: same level and health, new spot; a taken spot is refused.
            var (gun10, _, _) = ids[4];
            B.TryGetState(gun10, out var before10);
            Vector3 to = before10.Pos + side * 2f * BuildSystem.Cell;
            if (Physics.Raycast(to + Vector3.up * 5f, Vector3.down, out var gh, 12f, GameInfo.LevelLayer)) to.y = gh.point.y;
            bool moved = B.ServerMove(gun10, to, 90f);
            bool refused = !B.ServerMove(ids[0].id, to, 0f);
            yield return new WaitForSecondsRealtime(1f);
            B.TryGetState(gun10, out var after10);
            var go10 = B.PieceObject(gun10);
            Note($"moved Lv10 gun: ok {moved}, onto a taken spot refused {refused}, Lv{before10.Level}->Lv{after10.Level}, hp {before10.Hp}->{after10.Hp}, now {(after10.Pos - before10.Pos).magnitude:0.0} m away, object at {(go10 ? (go10.transform.position - after10.Pos).magnitude.ToString("0.00") : "-")} m from its state");
            Vector3 eye2 = after10.Pos - gd * 4f + Vector3.up * 1.2f;
            yield return Look(eye2, YawTo(eye2, after10.Pos), "towers_moved", 1.5f);
            // The look panel and the menu on the Lv10 gun.
            B.DebugSetBuilding(true, PieceType.Gun);
            Aim(YawTo(eye2, after10.Pos), 10f);
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("towers_look");
            yield return new WaitForSecondsRealtime(0.6f);
            PieceMenu.Instance.Open(gun10);
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("towers_menu");
            yield return new WaitForSecondsRealtime(0.6f);
            PieceMenu.Instance.Close();
            B.StopBuilding();

            // A lone Lv5 gun on two tough, stunned fish: both lose health (its second barrel takes the second fish).
            B.ServerClearAll();
            yield return new WaitForSecondsRealtime(0.5f);
            int lone = Place(PieceType.Gun, center + gd * 9f + Vector3.up * 1.2f);
            for (int l = 1; l < 5; l++) B.ServerUpgrade(lone, 1);
            var g5 = B.ServerPieces.FirstOrDefault(x => x.Id == lone); // the host's own copy (the broadcast reaches our client a moment later)
            var fishA = plugin.ServerSpawnMinion(EnemyKind.Runner, g5.Pos + gd * 6f + side * 2.5f + Vector3.up * 1f);
            var fishB = plugin.ServerSpawnMinion(EnemyKind.Runner, g5.Pos + gd * 6f - side * 2.5f + Vector3.up * 1f);
            foreach (var c in new[] { fishA, fishB }) if (c) { Siege.Instance.Slow(c, 1f, 300f); c.ServerChangeHp(-100000); }
            yield return new WaitForSecondsRealtime(0.3f);
            int hpA = fishA ? fishA._hp.Value : -1, hpB = fishB ? fishB._hp.Value : -1;
            yield return new WaitForSecondsRealtime(1.5f);
            Note($"lone Lv{g5.Level} gun on two fish: A {hpA} -> {(fishA ? fishA._hp.Value : 0)}, B {hpB} -> {(fishB ? fishB._hp.Value : 0)} (both should drop)");
        }

        // The village by the dock: the stations and their keepers, talking to them (the game's own E prompt), the smith's
        // weapon rack (drop on the anvil, forge 3 into 1, take one off, rack the one in your hand) and the fish stall.
        // The vault takes anything thrown at its door (swallowed with a gulp) and keeps each of the game's items as it was,
        // handing it back so; the smithy's anvil takes weapons (sparks and smoke), the crate fish (a splash); the bench
        // forges; Shift+F7 clears the ground; E at the lighthouse's door pulls the gate, E again lets it go, and the gate
        // comes down for the round and goes back up after.
        // The rocket launcher and the game's guns as fortress weapons: the guns' own numbers, a hit on a fortress fish by
        // rarity, real shots, the rocket, game guns into the vault and onto the anvil, the shop, the starter, the help page.
        private IEnumerator GunsScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            var loot = Loot.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            yield return Revive();
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            float stand = FortressIsland.StandHeight;
            int C(string k) => Counters.TryGetValue(k, out int n) ? n : 0;
            var guns = GearCatalog.WeaponKinds.Where(GearCatalog.IsGameGun).ToList();
            T F<T>(object o, string name) { try { return (T)HarmonyLib.AccessTools.Field(o.GetType(), name).GetValue(o); } catch { return default; } }
            foreach (var g in guns)
            {
                var w = GameInfo.GetSpawnable(WeaponItems.GameName(g)) as Weapon;
                string dmg; try { dmg = w.Damage.ToString(); } catch (Exception e) { dmg = "? " + e.GetType().Name; }
                int mag; try { mag = w.Attachments.AmmoPerMag; } catch { mag = -1; }
                Note($"{g} ({WeaponItems.GameName(g)} id {WeaponItems.GameId(g)}, ours {WeaponItems.IdOf(g, Rarity.Common)}-{WeaponItems.IdOf(g, Rarity.Legendary)}): damage {dmg}, " +
                     $"every {F<float>(w, "_timeBetweenShots"):0.###} s, {F<int>(w, "_projectileCountPerShot")} per shot, mag {mag}, full auto {F<bool>(w, "_fullAuto")}, worth {WeaponItems.Prefab(g, Rarity.Common)?.TotalWorth}");
            }

            // The starter: a rocket launcher by the circle.
            loot.Vault.Starter.Remove(Loot.KeyOf(me));
            foreach (var i in me.Inventory._items.Values.Append(me.Holding.HeldItem).Where(i => i && WeaponItems.Is(i, out _, out _)).Distinct().ToList()) { me.Inventory.RemoveItem(i); i.DestroyItem(); }
            Note($"before the round: carries a weapon {WeaponItems.Carries(me)}, running {plugin.Running}");
            yield return new WaitForSecondsRealtime(0.5f);
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * stand, 0f, true);
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return Until(() => plugin.Running && plugin.InBuildTime, 15f, "round start");
            plugin.DebugExtendBuild(3000f);
            Siege.Instance.ServerAddCoins(Siege.Money(100000));
            yield return new WaitForSecondsRealtime(1f);
            var starters = FindObjectsOfType<Item>().Where(i => i && i.IsSpawned && !i.Holder && WeaponItems.Is(i, out _, out _) && (i.transform.position - (center + FortressIsland.ReadyLocal)).Flat().magnitude < 6f).Select(i => i.GetName()).ToList();
            Note($"starter: given {C("starter_weapon")}, by the circle: {string.Join(", ", starters)}");

            // A game gun in hand at each rarity: its name, the game's own model, a hit on a fortress fish (the host's
            // multiplier), then real shots of the game's own at a tank.
            float g0 = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g0), 0f, Mathf.Sin(g0));
            Vector3 Ground(Vector3 w) { if (Physics.Raycast(w + Vector3.up * 10f, Vector3.down, out var rh, 30f, GameInfo.LevelLayer)) return rh.point; return w; }
            var shootM = HarmonyLib.AccessTools.Method(typeof(Weapon), "Shoot");
            foreach (var g in guns)
                foreach (var r in new[] { Rarity.Common, Rarity.Legendary })
                {
                    plugin.DebugKillAll();
                    yield return new WaitForSecondsRealtime(0.5f);
                    Vector3 feet = Ground(center + gd * 9f), eye = feet + Vector3.up * 1.6f, at = Ground(center + gd * 16f);
                    me.LocalTeleport(feet + Vector3.up * 0.2f, YawTo(eye, at), true);
                    var tank = plugin.ServerSpawnMinion(EnemyKind.Tank, at + Vector3.up * 1.5f);
                    if (tank) Siege.Instance.Slow(tank, 1f, 60f);
                    var gun = WeaponItems.ServerSpawn(g, r, eye + gd * 0.6f);
                    yield return new WaitForSecondsRealtime(0.4f);
                    if (gun) Server.Instance.RpcLogic___SetItemHolder___3876263354(gun, me, null);
                    yield return new WaitForSecondsRealtime(1.2f);
                    var held = me.Holding.HeldItem;
                    int hp0 = tank ? tank._hp.Value : 0;
                    if (tank && r == Rarity.Common) Server.Instance.RpcLogic___HitCreature___215526726(tank, me, 10, tank.transform.position, Vector3.forward);
                    yield return new WaitForSecondsRealtime(0.3f);
                    int hp1 = tank ? tank._hp.Value : 0;
                    float expect = 10f * FortressPlugin.HandDamage.Value * WeaponItems.GunDamage(g, r);
                    Aim(YawTo(eye, at), Mathf.Atan2(eye.y - (at.y + 1f), (at - eye).Flat().magnitude) * Mathf.Rad2Deg);
                    yield return new WaitForSecondsRealtime(0.5f);
                    int shots = 0;
                    if (held is Weapon full) { Note($"  ammo {full.Ammo}"); try { HarmonyLib.AccessTools.Property(typeof(Weapon), "Ammo").SetValue(full, 30); } catch { } }
                    for (int k = 0; k < 4 && held is Weapon hw; k++)
                    {
                        try { shootM.Invoke(hw, null); shots++; } catch (Exception e) { Note("  shoot: " + e.InnerException?.Message); break; }
                        yield return new WaitForSecondsRealtime(Mathf.Max(0.35f, F<float>(hw, "_timeBetweenShots") + 0.05f));
                    }
                    yield return new WaitForSecondsRealtime(0.8f);
                    if (r == Rarity.Common) FortressPlugin.Shot("gun_" + g, 0f);
                    int hp2 = tank ? tank._hp.Value : 0;
                    Note($"{g} {r}: holding '{(held ? held.GetName() : "nothing")}' (id {(held ? held.ID : 0)}), ours-blocked {(held is Weapon bw && WeaponItemNoShoot.Ours(bw))}; " +
                         (r == Rarity.Common ? $"a 10 hit took {hp0 - hp1} (expect {expect:0.#}); " : "") + $"{shots} shots of its own took {hp1 - hp2} (tank {hp2}/{(tank ? tank.MaxHp : 0)})");
                    if (held) { me.Inventory.RemoveItem(held); held.DestroyItem(); }
                    if (tank) tank.ServerChangeHp(99999);
                }

            // The rocket: a legendary fires three; each bursts on the fish (four runners held in a row).
            foreach (var r in new[] { Rarity.Common, Rarity.Legendary })
            {
                plugin.DebugKillAll();
                FortressNet.Instance.ServerReset(); // the last test's bodies: new fish would stand on them
                yield return new WaitForSecondsRealtime(0.8f);
                Vector3 feet = Ground(center + gd * 6f), eye = feet + Vector3.up * 1.6f, mid = Ground(center + gd * 20f);
                Vector3 side = Vector3.Cross(Vector3.up, gd);
                me.LocalTeleport(feet + Vector3.up * 0.2f, YawTo(eye, mid), true);
                var fish = new List<Creature>();
                for (int i = 0; i < 4; i++) fish.Add(plugin.ServerSpawnMinion(EnemyKind.Runner, mid + side * (i - 1.5f) * 1.4f + Vector3.up * 1.5f));
                foreach (var c in fish) if (c) Siege.Instance.Slow(c, 1f, 60f);
                var rocket = WeaponItems.ServerSpawn(Gear.Rocket, r, eye + gd * 0.6f);
                yield return new WaitForSecondsRealtime(0.4f);
                if (rocket) Server.Instance.RpcLogic___SetItemHolder___3876263354(rocket, me, null);
                yield return new WaitForSecondsRealtime(1.5f);
                int hp0 = fish.Sum(c => c ? Mathf.Max(0, c._hp.Value) : 0), blasts0 = C("rocket_blast");
                Weapons.Instance.ServerFire(me, new WeaponFire { Phase = 0, Origin = eye, Dir = (mid + Vector3.up * 0.5f - eye).normalized });
                FortressPlugin.Shot("rocket_" + r + "_fly", 0.2f);
                FortressPlugin.Shot("rocket_" + r, 0.48f);
                yield return new WaitForSecondsRealtime(1.5f);
                int hp1 = fish.Sum(c => c ? Mathf.Max(0, c._hp.Value) : 0);
                Note($"rocket {r}: holding '{(me.Holding.HeldItem ? me.Holding.HeldItem.GetName() : "nothing")}', fired {C("weapon_Rocket")}, blasts {C("rocket_blast") - blasts0}, fish health {hp0} -> {hp1}, dead {fish.Count(c => !c || c._hp.Value <= 0)}/4");
                if (me.Holding.HeldItem) { var h = me.Holding.HeldItem; me.Inventory.RemoveItem(h); h.DestroyItem(); }
            }
            FortressPlugin.Shot("rocket_view");

            // The game's guns thrown at the vault's door and onto the anvil turn into commons of their kind.
            while (loot.Vault.StorageSlots - loot.Vault.SlotsUsed < 8) loot.Vault.Bought[Expansion.Storage] = loot.Vault.BoughtOf(Expansion.Storage) + 1;
            var chute = FindObjectOfType<VaultChute>().transform.position;
            int pistols0 = loot.Vault.Count(Gear.Pistol, Rarity.Common), items0 = loot.Vault.Items.Count;
            ItemManager.Instance.SpawnNewItem(GameInfo.GetSpawnable("pistol"), chute + Vector3.up * 3f, Quaternion.identity);
            yield return new WaitForSecondsRealtime(2.5f);
            Note($"vault door: common pistols {pistols0} -> {loot.Vault.Count(Gear.Pistol, Rarity.Common)} (expect +1), game items {items0} -> {loot.Vault.Items.Count} (expect the same)");
            Vector3 anvil = island.SmithyPos + Quaternion.Euler(0f, FortressIsland.SmithyYaw, 0f) * new Vector3(-0.2f, 1.6f, 0.45f);
            int shotguns0 = loot.Vault.Count(Gear.Shotgun, Rarity.Common);
            ItemManager.Instance.SpawnNewItem(GameInfo.GetSpawnable("shotgun"), anvil + Vector3.up * 0.3f, Quaternion.identity);
            yield return new WaitForSecondsRealtime(2.5f);
            Note($"anvil: common shotguns {shotguns0} -> {loot.Vault.Count(Gear.Shotgun, Rarity.Common)} (expect +1)");

            // Three commons forge into a rare; the shop sells a sniper straight into my hands.
            loot.Vault.Add(Gear.Pistol, Rarity.Common, 3);
            int rare0 = loot.Vault.Count(Gear.Pistol, Rarity.Rare);
            loot.ServerRequest(me, new VaultRequest { Action = VaultAction.Merge, Gear = Gear.Pistol, Rarity = Rarity.Common });
            yield return new WaitForSecondsRealtime(1f);
            Note($"forge: rare pistols {rare0} -> {loot.Vault.Count(Gear.Pistol, Rarity.Rare)} (expect +1)");
            Vector3 forge = island.SmithyPos + Quaternion.Euler(0f, FortressIsland.SmithyYaw, 0f) * new Vector3(0.6f, stand + 0.2f, 2.6f);
            me.LocalTeleport(forge, YawTo(forge, anvil), true);
            yield return new WaitForSecondsRealtime(1f);
            int coins0 = Siege.Instance.ServerCoins;
            loot.ServerRequest(me, new VaultRequest { Action = VaultAction.Buy, Gear = Gear.Sniper, Rarity = Rarity.Common });
            yield return new WaitForSecondsRealtime(2f);
            Note($"shop: money {coins0} -> {Siege.Instance.ServerCoins}, in my hands '{(me.Holding.HeldItem ? me.Holding.HeldItem.GetName() : "nothing")}'");
            FortressPlugin.Shot("gun_hud", 0f);
            yield return new WaitForSecondsRealtime(0.6f);
            VaultUI.Instance.Open(VaultUI.Desk.Smith);
            VaultUI.Instance.DebugTab(2);
            VaultUI.Instance.DebugSelect(Gear.Rocket, Rarity.Common);
            yield return new WaitForSecondsRealtime(3f);
            FortressPlugin.Shot("gun_shop"); yield return new WaitForSecondsRealtime(0.5f);
            VaultUI.Instance.DebugTab(0);
            VaultUI.Instance.DebugSelect(Gear.Pistol, Rarity.Rare);
            yield return new WaitForSecondsRealtime(2f);
            FortressPlugin.Shot("gun_bench"); yield return new WaitForSecondsRealtime(0.5f);
            VaultUI.Instance.Close();
            yield return new WaitForSecondsRealtime(0.5f);
            Tips.Instance.DebugWeaponsPage();
            yield return new WaitForSecondsRealtime(2f);
            if (Tips.Instance.HelpOpen) Tips.Instance.ToggleHelp();
            yield return new WaitForSecondsRealtime(0.3f);
            Tips.Instance.DebugWeaponsPage(); // built again with the guns' pictures
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("gun_help");
            yield return new WaitForSecondsRealtime(1.5f);
            if (Tips.Instance.HelpOpen) Tips.Instance.ToggleHelp();
        }

        // A boss can't be hurt while it makes its entrance (a hit by hand, a weapon, a turret in range), can once it's live,
        // and the test keys' "clear the field" still clears it.
        private IEnumerator BossGuardScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            SkipBossCamera = true;
            yield return Until(() => island.Exists, 30f, "fortress island");
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            yield return Revive();
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            int C(string k) => Counters.TryGetValue(k, out int n) ? n : 0;
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * FortressIsland.StandHeight, 0f, true);
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return Until(() => plugin.Running && plugin.InBuildTime, 15f, "round start");
            plugin.DebugExtendBuild(3000f);
            var c = plugin.ServerSpawnBoss(EnemyKind.Warden, 1f, 0, 10, 8f);
            var brain = Bosses.Of(c);
            yield return new WaitForSecondsRealtime(1f);
            int hp0 = c ? c._hp.Value : -1, blocked0 = C("boss_hit_in_entrance");
            bool entering = Bosses.Entering(c);
            c.ServerChangeHp(100);
            Server.Instance.RpcLogic___HitCreature___215526726(c, me, 50, c.transform.position, Vector3.forward);
            yield return new WaitForSecondsRealtime(0.5f);
            Note($"in its entrance (live {brain?.IsLive}, entering {entering}): health {hp0} -> {(c ? c._hp.Value : -1)} (expect the same), hits turned away {C("boss_hit_in_entrance") - blocked0}");
            yield return Until(() => brain != null && brain.IsLive, 20f, "the entrance to end");
            int hp1 = c ? c._hp.Value : -1;
            c.ServerChangeHp(100);
            yield return new WaitForSecondsRealtime(0.3f);
            Note($"live (entering {Bosses.Entering(c)}): health {hp1} -> {(c ? c._hp.Value : -1)} (expect -100)");
            plugin.DebugKillAll();
            yield return new WaitForSecondsRealtime(3f);
            var c2 = plugin.ServerSpawnBoss(EnemyKind.Warden, 1f, 1, 10, 8f);
            yield return new WaitForSecondsRealtime(1f);
            bool still = Bosses.Entering(c2);
            plugin.DebugKillAll();
            yield return new WaitForSecondsRealtime(0.5f);
            Note($"test key clear in an entrance (entering {still}): health left {(c2 ? c2._hp.Value : -1)} (expect 0)");
            yield return new WaitForSecondsRealtime(3f);
            plugin.DebugStopRound();
        }

        // The game's own economy, for pricing the mod's things against it: every item's price and worth, every shop's price in
        // the loaded world, the hotbar slots', the bullet upgrades'.
        private IEnumerator PricesScenario()
        {
            yield return new WaitForSecondsRealtime(1f);
            var all = (Dictionary<byte, Item>)HarmonyLib.AccessTools.Field(typeof(GameInfo), "_allItems").GetValue(null);
            string Name(Item i) { try { return i.GetName(); } catch { return i.name; } }
            foreach (var i in all.Values.Where(i => i).OrderBy(i => i.Cost).ThenBy(i => i.DefaultWorth))
                Note($"item {i.ID,3} {i.name} '{Name(i)}': cost {i.Currency} {i.Cost}, worth {i.DefaultWorth}, total {i.TotalWorth}{(i.Creature ? " (creature)" : "")}");
            T F<T>(object o, string n) { try { return (T)HarmonyLib.AccessTools.Field(o.GetType(), n).GetValue(o); } catch { return default; } }
            foreach (var p in Resources.FindObjectsOfTypeAll<Purchasable>().Where(p => p && p.gameObject.scene.IsValid()))
                Note($"shop {p.GetType().Name} '{p.name}' at {p.transform.position:F0}: custom cost {F<int>(p, "_customCost")}, free {F<bool>(p, "_isFree")}, item {(F<Item>(p, "_itemToPurchase") is Item it && it ? it.name + " $" + it.Cost : "-")}");
            var me = Player.LocalPlayer;
            if (me && me.Inventory) Note("extra hotbar slots: " + string.Join(", ", F<int[]>(me.Inventory, "_extraSlotCosts") ?? new int[0]));
            foreach (var g in GearCatalog.WeaponKinds.Where(GearCatalog.IsGameGun))
            {
                var att = WeaponMods.PrefabAttachments(g);
                var tiers = att ? HarmonyLib.AccessTools.Field(typeof(Attachments), "_bulletUpgrades").GetValue(att) as BulletUpgrade[] : null;
                Note($"{g} bullet upgrades: " + (tiers == null ? "-" : string.Join(", ", tiers.Select(t => $"{t.Damage} dmg for {F<Currency>(t, "_currency")} {F<int>(t, "_cost")}"))));
            }
            Note($"money now ${MoneyManager.Money}, euro {MoneyManager.Euro}");
        }

        // The 10/19 evening's asks: the record going up wave by wave, calling the next wave mid-wave, the hook flinging what it
        // catches (by its worth), the laser's colour by its bite, the stall with its ten-times helpers.
        private IEnumerator Batch8Scenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            var loot = Loot.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            if (PlayerManager.InGodMode) PlayerManager.ToggleGodMode(); // a real round: the record counts
            IgnorePlayers = true;
            Tips.Show.Value = false;
            yield return Revive();
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            float stand = FortressIsland.StandHeight;
            int C(string k) => Counters.TryGetValue(k, out int n) ? n : 0;
            int best0 = loot.Vault.Best;
            loot.Vault.Best = 0;
            loot.SaveVault();
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * stand, 0f, true);
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return Until(() => plugin.Running && plugin.InBuildTime, 15f, "round start");
            Siege.Instance.ServerAddCoins(Siege.Money(1000000));

            // The record: wave 1 held -> the save's best is 1 straight away, mid-round.
            if (_arg == "hook") goto Hook;
            plugin.SkipBuildTime();
            yield return Until(() => plugin.Wave == 1 && !plugin.InBuildTime, 15f, "wave 1");
            yield return new WaitForSecondsRealtime(8f);
            for (int i = 0; i < 20 && !plugin.InBuildTime; i++) { plugin.DebugKillAll(); yield return new WaitForSecondsRealtime(1f); }
            yield return new WaitForSecondsRealtime(1f);
            Note($"record mid-round: cleared {plugin.Cleared}, the save's best {loot.Vault.Best} (expect {plugin.Cleared}), the client's {loot.ClientVault.Best}");

            // Calling the next wave mid-wave: wave 2 starts, then 3 is called on top; clearing both pays both.
            plugin.SkipBuildTime();
            yield return Until(() => plugin.Wave == 2 && !plugin.InBuildTime, 15f, "wave 2");
            yield return Until(() => plugin.CanCallNext, 20f, "wave 2 landed");
            int alive2 = FortressPlugin.Alive.Count(c => c && c._hp.Value > 0), coins0 = Siege.Instance.ServerCoins;
            plugin.DebugCallNext();
            yield return new WaitForSecondsRealtime(9f);
            Note($"called early: wave {plugin.Wave} (expect 3), fish {alive2} -> {FortressPlugin.Alive.Count(c => c && c._hp.Value > 0)}, calls {C("wave_called_early")}; can call a boss wave: {(plugin.Wave + 1) % 10 == 0 && plugin.CanCallNext}");
            for (int i = 0; i < 25 && !plugin.InBuildTime; i++) { plugin.DebugKillAll(); yield return new WaitForSecondsRealtime(1f); }
            yield return new WaitForSecondsRealtime(1f);
            int paid = Siege.Instance.ServerCoins - coins0;
            Note($"both cleared: cleared {plugin.Cleared}, money +{paid} (both bonuses {FortressPlugin.WaveBonus(2) + FortressPlugin.WaveBonus(3)} and the kills)");

            // The hook: a trophy lying about comes back on the line, then flung at a tank: it hits by its worth.
            Hook:
            plugin.DebugKillAll();
            yield return new WaitForSecondsRealtime(1f);
            float g0 = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g0), 0f, Mathf.Sin(g0));
            Vector3 Ground(Vector3 w) { if (Physics.Raycast(w + Vector3.up * 10f, Vector3.down, out var rh, 30f, GameInfo.LevelLayer)) return rh.point; return w; }
            Vector3 feet = Ground(center + gd * 6f), eye = feet + Vector3.up * 1.6f, prop = Ground(center + gd * 11f), at = Ground(center + gd * 18f);
            me.LocalTeleport(feet + Vector3.up * 0.2f, YawTo(eye, prop), true);
            foreach (var i in me.Inventory._items.Values.Append(me.Holding.HeldItem).Where(i => i).Distinct().ToList()) { me.Inventory.RemoveItem(i); i.DestroyItem(); }
            var hook = WeaponItems.ServerSpawn(Gear.Hook, Rarity.Common, eye + gd * 0.6f);
            yield return new WaitForSecondsRealtime(0.4f);
            if (hook) Server.Instance.RpcLogic___SetItemHolder___3876263354(hook, me, null);
            var thingItem = BossTrophies.ServerSpawn(EnemyKind.Warden, prop + Vector3.up * 0.6f);
            yield return new WaitForSecondsRealtime(2f);
            var tank = plugin.ServerSpawnMinion(EnemyKind.Tank, at + Vector3.up * 1.5f);
            if (tank) Siege.Instance.Slow(tank, 1f, 60f);
            yield return new WaitForSecondsRealtime(1f);
            Vector3 toThing = thingItem ? (thingItem.transform.position + Vector3.up * 0.2f - eye).normalized : gd;
            Note($"hook sweep toward the trophy: {Weapons.Instance.DebugHookScan(eye, toThing, me)}");
            Weapons.Instance.ServerFire(me, new WeaponFire { Phase = 0, Origin = eye, Dir = toThing });
            yield return new WaitForSecondsRealtime(1f);
            Note($"hooked: '{(thingItem ? thingItem.GetName() : "nothing")}' worth {(thingItem ? thingItem.TotalWorth : 0)}, on the line {Weapons.OnLine(thingItem)}, {(thingItem ? (thingItem.transform.position - eye).magnitude : -1f):0.0} m from my eyes, hooked {C("hooked_item")}");
            FortressPlugin.Shot("hook_holding", 0f);
            yield return new WaitForSecondsRealtime(2.5f); // the hook's cooldown
            int hp0 = tank ? tank._hp.Value : 0;
            Vector3 toTank = tank ? (tank.transform.position + Vector3.up * 0.5f - eye).normalized : gd;
            Weapons.Instance.ServerFire(me, new WeaponFire { Phase = 0, Origin = eye, Dir = toTank });
            FortressPlugin.Shot("hook_fling", 0.25f);
            yield return new WaitForSecondsRealtime(2f);
            Note($"flung: hits {C("hook_fling_hit")}, tank health {hp0} -> {(tank ? tank._hp.Value : 0)} (the hook alone does {GearCatalog.Value(Gear.Hook, Rarity.Common):0}, x{Weapons.FlingMult(thingItem ? thingItem.TotalWorth : 0):0.0} for its worth), the trophy now {(thingItem ? (thingItem.transform.position - at).magnitude : -1f):0.0} m from the tank");
            if (tank) tank.ServerChangeHp(999999);
            if (_arg == "hook") { plugin.DebugStopRound(); yield break; }

            // The laser at each rarity, first touch and held on (its bite): its colour.
            foreach (var r in GearCatalog.Rarities)
            {
                plugin.DebugKillAll();
                yield return new WaitForSecondsRealtime(0.5f);
                foreach (var i in me.Inventory._items.Values.Append(me.Holding.HeldItem).Where(i => i).Distinct().ToList()) { me.Inventory.RemoveItem(i); i.DestroyItem(); }
                var laser = WeaponItems.ServerSpawn(Gear.Laser, r, eye + gd * 0.6f);
                yield return new WaitForSecondsRealtime(0.4f);
                if (laser) Server.Instance.RpcLogic___SetItemHolder___3876263354(laser, me, null);
                var t2 = plugin.ServerSpawnMinion(EnemyKind.Tank, at + Vector3.up * 1.5f);
                if (t2) Siege.Instance.Slow(t2, 1f, 60f);
                me.LocalTeleport(feet + Vector3.up * 0.2f, YawTo(eye, at) + 25f, true);
                yield return new WaitForSecondsRealtime(1.2f);
                Vector3 dir = t2 ? (t2.transform.position + Vector3.up * 0.5f - eye).normalized : gd;
                Weapons.Instance.ServerFire(me, new WeaponFire { Phase = 0, Origin = eye, Dir = dir });
                for (float t = 0f; t < 2.4f; t += 0.1f) { Weapons.Instance.ServerFire(me, new WeaponFire { Phase = 1, Origin = eye, Dir = dir }); if (t < 0.15f) FortressPlugin.Shot("laser_" + r + "_first", 0f); yield return new WaitForSecondsRealtime(0.1f); }
                FortressPlugin.Shot("laser_" + r + "_held", 0f);
                yield return new WaitForSecondsRealtime(0.2f);
                Weapons.Instance.ServerFire(me, new WeaponFire { Phase = 2, Origin = eye, Dir = dir });
                if (t2) t2.ServerChangeHp(999999);
            }

            // The stall: thirty helpers' worth of rows.
            loot.Vault.Helpers = 7; loot.Vault.HelperSpeed = 12; loot.Vault.HelperLoad = 40;
            loot.SaveVault();
            yield return new WaitForSecondsRealtime(2f);
            StallMenu.Instance.Open();
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("stall_helpers");
            yield return new WaitForSecondsRealtime(0.5f);
            StallMenu.Instance.Close();
            Note($"helpers out: {Helpers.Instance.DebugCount} (expect 7)");
            plugin.DebugStopRound();
            yield return new WaitForSecondsRealtime(2f);
            Note($"after the round: best {loot.Vault.Best} (was {best0} before the test), errors {C("errors")}");
            loot.Vault.Best = Mathf.Max(best0, loot.Vault.Best);
            loot.SaveVault();
        }

        // Weapon mods: what the game's guns start from, each line working in play (power, speed and the trait on the rocket;
        // bullets, rate of fire, magazine and reload on an SMG; the bow's draw), bought through the smithy's mods tab.
        private IEnumerator ModsScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            var loot = Loot.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            yield return Revive();
            var me = Player.LocalPlayer;
            ulong key = Loot.KeyOf(me);
            var V = loot.Vault;
            Vector3 center = island.Center;
            float stand = FortressIsland.StandHeight;
            int C(string k) => Counters.TryGetValue(k, out int n) ? n : 0;
            T F<T>(object o, string name) { try { return (T)HarmonyLib.AccessTools.Field(o.GetType(), name).GetValue(o); } catch { return default; } }
            foreach (var k in V.Mods.Keys.Where(k => k.Item1 == key).ToList()) V.Mods.Remove(k);
            V.Scales = 5000;
            loot.SaveVault();

            // What the mods work from.
            foreach (var g in GearCatalog.WeaponKinds.Where(GearCatalog.IsGameGun))
            {
                var att = WeaponMods.PrefabAttachments(g);
                var w = WeaponItems.Prefab(g, Rarity.Common) as Weapon;
                string bullets = att ? string.Join(" / ", Enumerable.Range(0, 6).Select(l => WeaponMods.BulletDamage(att, 0, l))) : "no attachments";
                Note($"{g}: bullets Lv0-5 {bullets}; mag {(att ? WeaponMods.DefaultMag(att) : -1)}, every {(w ? WeaponMods.Interval(w) : -1f):0.###} s, waits for its shot's animation {(w && WeaponMods.AnimGated(w))}"
                     + (g == Gear.Bow && att ? $", draw speed {att.ChargeSpeed:0.##}" : ""));
            }

            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * stand, 0f, true);
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return Until(() => plugin.Running && plugin.InBuildTime, 15f, "round start");
            plugin.DebugExtendBuild(3000f);
            float g0 = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g0), 0f, Mathf.Sin(g0)), side = Vector3.Cross(Vector3.up, gd);
            Vector3 Ground(Vector3 w) { if (Physics.Raycast(w + Vector3.up * 10f, Vector3.down, out var rh, 30f, GameInfo.LevelLayer)) return rh.point; return w; }
            IEnumerator Buy(Gear g, int track, int to)
            {
                for (int l = V.ModOf(key, g, track); l < to; l++) loot.ServerRequest(me, new VaultRequest { Action = VaultAction.Mod, Gear = g, Slot = (byte)track, Count = (byte)l });
                yield return Until(() => loot.ClientVault.ModOf(key, g, track) == to, 5f, $"{g} line {track} at {to}");
            }
            void Clear(Gear g) { for (int t = 0; t < WeaponMods.Tracks; t++) V.Mods.Remove((key, g, t)); loot.SaveVault(); }
            Item InHand(Gear g, Vector3 at)
            {
                foreach (var i in me.Inventory._items.Values.Append(me.Holding.HeldItem).Where(i => i).Distinct().ToList()) { me.Inventory.RemoveItem(i); i.DestroyItem(); }
                var it = WeaponItems.ServerSpawn(g, Rarity.Common, at);
                if (it) Server.Instance.RpcLogic___SetItemHolder___3876263354(it, me, null);
                return it;
            }

            // The rocket: power (damage), trait (a fish 4.2 m from the blast: out of reach at Lv0, in it at Lv5 with 5 m), speed
            // (fired again 1.1 s later: still cooling at Lv0's 1.6 s, ready at Lv5's 0.96 s).
            foreach (int lvl in new[] { 0, 5 })
            {
                Clear(Gear.Rocket);
                if (lvl > 0) { yield return Buy(Gear.Rocket, WeaponMods.Power, lvl); yield return Buy(Gear.Rocket, WeaponMods.Rate, lvl); yield return Buy(Gear.Rocket, WeaponMods.Trait, lvl); }
                plugin.DebugKillAll();
                FortressNet.Instance.ServerReset();
                yield return new WaitForSecondsRealtime(0.8f);
                Vector3 feet = Ground(center + gd * 6f), eye = feet + Vector3.up * 1.6f, mid = Ground(center + gd * 20f);
                me.LocalTeleport(feet + Vector3.up * 0.2f, YawTo(eye, mid), true);
                var near = plugin.ServerSpawnMinion(EnemyKind.Tank, mid + Vector3.up * 1.5f);
                var far = plugin.ServerSpawnMinion(EnemyKind.Tank, Ground(mid + side * 4.2f) + Vector3.up * 1.5f);
                foreach (var c in new[] { near, far }) if (c) Siege.Instance.Slow(c, 1f, 60f);
                InHand(Gear.Rocket, eye + gd * 0.6f);
                yield return new WaitForSecondsRealtime(1.5f);
                int n0 = near ? near._hp.Value : 0, f0 = far ? far._hp.Value : 0, fired0 = C("weapon_Rocket");
                float apart = near && far ? (far.transform.position - near.transform.position).magnitude : -1f;
                Vector3 aim = ((near ? near.transform.position : mid) + Vector3.up * 0.5f - eye).normalized;
                Weapons.Instance.ServerFire(me, new WeaponFire { Phase = 0, Origin = eye, Dir = aim });
                yield return new WaitForSecondsRealtime(1.1f);
                Weapons.Instance.ServerFire(me, new WeaponFire { Phase = 0, Origin = eye, Dir = aim });
                yield return new WaitForSecondsRealtime(1.2f);
                int n1 = near ? near._hp.Value : 0, f1 = far ? far._hp.Value : 0;
                float reach = Weapons.RocketRadius + WeaponMods.TraitAdd(Gear.Rocket, lvl);
                Note($"rocket mods Lv{lvl}: shots that went off {C("weapon_Rocket") - fired0}/2 (expect {(lvl > 0 ? 2 : 1)}), the fish it hit lost {n0 - n1}, the one {apart:0.0} m off lost {f0 - f1} (blast {reach:0.#} m: expect {(apart <= reach ? "some" : "none")})");
                if (lvl > 0) FortressPlugin.Shot("mods_rocket", 0f);
                foreach (var c in new[] { near, far }) if (c) c.ServerChangeHp(99999);
            }

            // An SMG: bullets (what a shot does), rate of fire (the time between shots after a shot), magazine (a full load)
            // and reload (how long reloading takes), each at 0 and 5.
            foreach (int lvl in new[] { 0, 5 })
            {
                Clear(Gear.Smg);
                if (lvl > 0) for (int t = 0; t < WeaponMods.Tracks; t++) yield return Buy(Gear.Smg, t, lvl);
                Vector3 feet = Ground(center + gd * 6f), eye = feet + Vector3.up * 1.6f;
                me.LocalTeleport(feet + Vector3.up * 0.2f, YawTo(eye, eye + gd), true);
                InHand(Gear.Smg, eye + gd * 0.6f);
                yield return Until(() => me.Holding.HeldItem is Weapon, 5f, "the SMG in hand");
                var ammoSeen = new List<string>();
                for (int k = 0; k < 6; k++) { if (me.Holding.HeldItem is Weapon sw) ammoSeen.Add($"{sw.Ammo}{(sw.Holder ? "" : "(no holder)")}"); yield return new WaitForSecondsRealtime(0.2f); }
                Note($"  SMG Lv{lvl} ammo in the first second: {string.Join(" ", ammoSeen)}");
                if (!(me.Holding.HeldItem is Weapon w)) { Note("no SMG in hand"); continue; }
                int dmg = (int)HarmonyLib.AccessTools.Method(typeof(Weapon), "GetShotDamage").Invoke(w, null), fresh = w.Ammo;
                HarmonyLib.AccessTools.Property(typeof(Weapon), "Ammo").SetValue(w, 5);
                HarmonyLib.AccessTools.Method(typeof(Weapon), "Shoot").Invoke(w, null);
                float interval = WeaponMods.Interval(w);
                yield return new WaitForSecondsRealtime(0.6f);
                float t0 = Time.time;
                HarmonyLib.AccessTools.Method(typeof(Weapon), "LocalReload").Invoke(w, null);
                var anim = WeaponMods.Anim(w);
                string clip = WeaponMods.ReloadAnim(w);
                float speed = anim && !string.IsNullOrEmpty(clip) && anim[clip] ? anim[clip].speed : -1f;
                yield return Until(() => !F<bool>(w, "_isReloading"), 8f, "the reload");
                Note($"SMG mods Lv{lvl}: a shot {dmg} (HUD {WeaponMods.ShotDamage(w)}), every {interval:0.###} s, a load {w.Attachments.AmmoPerMag} (fresh in hand {fresh}, after reloading {w.Ammo}), reload {Time.time - t0:0.00} s at {speed:0.00}x");
                if (lvl > 0) FortressPlugin.Shot("mods_smg_hud", 0f);
            }

            // The bow: its rate of fire is its draw.
            Clear(Gear.Bow);
            {
                Vector3 feet = Ground(center + gd * 6f), eye = feet + Vector3.up * 1.6f;
                InHand(Gear.Bow, eye + gd * 0.6f);
                yield return Until(() => me.Holding.HeldItem is Weapon, 5f, "the bow in hand");
                yield return new WaitForSecondsRealtime(1f);
                float d0 = me.Holding.HeldItem is Weapon b0 ? b0.Attachments.ChargeSpeed : -1f;
                yield return Buy(Gear.Bow, WeaponMods.FireRate, 5);
                float d5 = me.Holding.HeldItem is Weapon b5 ? b5.Attachments.ChargeSpeed : -1f;
                bool refused = !V.Apply(VaultAction.Mod, Gear.Bow, Rarity.Common, WeaponMods.Magazine, key);
                Note($"bow: draw speed {d0:0.##} -> {d5:0.##} at Lv5 (expect x{1f / WeaponMods.TimeMult(5):0.##}); a magazine mod refused {refused}");
            }

            // The smithy's mods tab: the rocket's lines, bought by its own buttons.
            Clear(Gear.Rocket);
            Vector3 anvil = island.SmithyPos + Quaternion.Euler(0f, FortressIsland.SmithyYaw, 0f) * new Vector3(-0.2f, 1.6f, 0.45f);
            Vector3 forge = island.SmithyPos + Quaternion.Euler(0f, FortressIsland.SmithyYaw, 0f) * new Vector3(0.6f, stand + 0.2f, 2.6f);
            me.LocalTeleport(forge, YawTo(forge, anvil), true);
            yield return new WaitForSecondsRealtime(1f);
            VaultUI.Instance.Open(VaultUI.Desk.Smith);
            VaultUI.Instance.DebugTab(3);
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("mods_tab_empty"); yield return new WaitForSecondsRealtime(0.5f);
            VaultUI.Instance.DebugSelect(Gear.Rocket, Rarity.Common);
            yield return new WaitForSecondsRealtime(1f);
            int scales0 = loot.ClientVault.Scales;
            var buttons = VaultUI.Instance.DebugButtons();
            Note($"mods tab: {buttons.Length} buttons: {string.Join(" | ", buttons.Select(b => b.GetComponentInChildren<TMPro.TextMeshProUGUI>()?.text))}");
            if (buttons.Length > 0) buttons[0].onClick.Invoke();
            yield return Until(() => loot.ClientVault.ModOf(key, Gear.Rocket, WeaponMods.Power) == 1, 5f, "power Lv1 from the button");
            if (VaultUI.Instance.DebugButtons() is var again && again.Length > 3) { again[3].onClick.Invoke(); yield return new WaitForSecondsRealtime(0.6f); VaultUI.Instance.DebugButtons()[3].onClick.Invoke(); } // the fourth row: the trait
            yield return new WaitForSecondsRealtime(1.5f);
            Note($"mods tab: rocket now {loot.ClientVault.ModOf(key, Gear.Rocket, 0)}/{loot.ClientVault.ModOf(key, Gear.Rocket, 1)}/{loot.ClientVault.ModOf(key, Gear.Rocket, 2)}/{loot.ClientVault.ModOf(key, Gear.Rocket, 3)} (expect 1/0/2/0), scales {scales0} -> {loot.ClientVault.Scales} (expect -{WeaponMods.Cost(0) * 2 + WeaponMods.Cost(1)})");
            FortressPlugin.Shot("mods_tab_rocket"); yield return new WaitForSecondsRealtime(0.5f);
            VaultUI.Instance.DebugSelect(Gear.Smg, Rarity.Common);
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("mods_tab_smg"); yield return new WaitForSecondsRealtime(0.5f);
            VaultUI.Instance.DebugSelect(Gear.Bow, Rarity.Common);
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("mods_tab_bow"); yield return new WaitForSecondsRealtime(0.5f);
            VaultUI.Instance.DebugTab(0);
            V.Add(Gear.Laser, Rarity.Legendary);
            loot.SaveVault();
            yield return Until(() => loot.ClientVault.Count(Gear.Laser, Rarity.Legendary) > 0, 5f, "a legendary laser in the vault");
            VaultUI.Instance.DebugSelect(Gear.Laser, Rarity.Legendary);
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("mods_bench_legend"); yield return new WaitForSecondsRealtime(0.5f);
            VaultUI.Instance.Close();

            // The hotbar: each weapon ringed in its rarity's colour, "+N" for my mods on its kind (rocket +3, SMG +15, bow +5).
            foreach (var i in me.Inventory._items.Values.Append(me.Holding.HeldItem).Where(i => i).Distinct().ToList()) { me.Inventory.RemoveItem(i); i.DestroyItem(); }
            V.Add(Gear.Rocket, Rarity.Legendary); V.Add(Gear.Smg, Rarity.Epic); V.Add(Gear.Bow, Rarity.Rare);
            loot.SaveVault();
            Vector3 inside = island.VaultPos + Quaternion.Euler(0f, FortressIsland.VaultYaw, 0f) * new Vector3(-0.3f, 0.5f, 0.8f);
            me.LocalTeleport(inside, YawTo(inside, island.VaultPos), true);
            yield return new WaitForSecondsRealtime(1.5f);
            VaultUI.Instance.Open(VaultUI.Desk.Quartermaster);
            yield return new WaitForSecondsRealtime(1f);
            var usable = VaultUI.Instance.DebugHotbarSlots;
            var put = new[] { (Gear.Rocket, Rarity.Legendary), (Gear.Smg, Rarity.Epic), (Gear.Bow, Rarity.Rare) };
            for (int k = 0; k < put.Length && k < usable.Count; k++) { VaultUI.Instance.DebugHotbarTake(put[k].Item1, put[k].Item2, usable[k]); yield return new WaitForSecondsRealtime(2.5f); }
            VaultUI.Instance.Close();
            yield return new WaitForSecondsRealtime(1f);
            var badges = FindObjectsOfType<UnityEngine.UI.Image>().Where(i => i.name == "FortressBadge" && i.gameObject.activeInHierarchy).ToList();
            Note($"hotbar: {string.Join(", ", usable.Select(i => me.Inventory._items.TryGetValue((byte)i, out var it) && it ? it.GetName() : "-"))}; badges showing {badges.Count}: "
                 + string.Join(", ", badges.Select(b => $"{ColorUtility.ToHtmlStringRGB(b.color)} '{b.GetComponentInChildren<TMPro.TextMeshProUGUI>()?.text}'")));
            FortressPlugin.Shot("mods_hotbar"); yield return new WaitForSecondsRealtime(0.5f);
            Note($"modded {C("modded")} times; errors {C("errors")}");
        }

        // The fish stall's helpers (bought, fetching corpses, selling them, knocked out by fish) and the repair tower.
        private IEnumerator HelpersScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            var loot = Loot.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            yield return Revive();
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            float stand = FortressIsland.StandHeight;
            int C(string k) => Counters.TryGetValue(k, out int n) ? n : 0;
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * stand, 0f, true);
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return Until(() => plugin.Running && plugin.InBuildTime, 15f, "round start");
            plugin.DebugExtendBuild(3000f);
            Siege.Instance.ServerAddCoins(Siege.Money(100000));
            var v = loot.Vault;
            v.Helpers = v.HelperSpeed = v.HelperLoad = 0;
            v.Oven = false;

            // Bought at the stall: two helpers, faster legs, a bigger basket, the oven; a fourth helper only after the third.
            int coins0 = Siege.Instance.ServerCoins;
            var bought = new[] { Helpers.Instance.ServerBuy(0), Helpers.Instance.ServerBuy(0), Helpers.Instance.ServerBuy(1), Helpers.Instance.ServerBuy(2), Helpers.Instance.ServerBuy(3), Helpers.Instance.ServerBuy(3) };
            Note($"bought {string.Join(" ", bought)} (expect True x5, then False: one oven): helpers {v.Helpers}, speed Lv{v.HelperSpeed}, basket Lv{v.HelperLoad}, oven {v.Oven}; money {coins0} -> {Siege.Instance.ServerCoins}");
            var reread = VaultData.Read(v.Write());
            Note($"saved and read back: helpers {reread.Helpers}, speed {reread.HelperSpeed}, basket {reread.HelperLoad}, oven {reread.Oven}");
            Vector3 stall = island.MarketPos;
            Vector3 front = stall + (center - stall).Flat().normalized * 3f;
            me.LocalTeleport(new Vector3(front.x, BossFx.Ground(front) + stand, front.z), YawTo(front, stall), true);
            yield return new WaitForSecondsRealtime(1.5f);
            StallMenu.Instance.Open();
            yield return new WaitForSecondsRealtime(1.5f);
            FortressPlugin.Shot("helpers_stall");
            yield return new WaitForSecondsRealtime(0.6f);
            StallMenu.Instance.Close();
            Note("helpers: " + Helpers.Instance.DebugState());

            // Corpses: eight runners killed in a ring between the stall and the lighthouse.
            Vector3 field = Vector3.Lerp(stall, center, 0.5f);
            for (int i = 0; i < 8; i++) plugin.ServerSpawnMinion(EnemyKind.Runner, field + Quaternion.Euler(0f, 45f * i, 0f) * Vector3.forward * 5f + Vector3.up * 1.5f);
            yield return new WaitForSecondsRealtime(1.5f);
            plugin.DebugKillAll();
            yield return new WaitForSecondsRealtime(1f);
            int corpses = FortressNet.Instance.ServerCorpses().Count(), money0 = Siege.Instance.ServerCoins;
            Note($"corpses lying: {corpses}");
            Vector3 eye = field + Quaternion.Euler(0f, 90f, 0f) * (field - stall).Flat().normalized * 9f;
            eye.y = BossFx.Ground(eye) + stand;
            me.LocalTeleport(eye, YawTo(eye, field), true);
            Aim(YawTo(eye, field), 12f);
            yield return new WaitForSecondsRealtime(2f);
            Vector3 h1 = Helpers.Instance.DebugPos(0), side1 = h1 + new Vector3(2.5f, 0f, 2.5f);
            side1.y = BossFx.Ground(side1) + stand;
            me.LocalTeleport(side1, YawTo(side1, h1), true);
            Aim(YawTo(side1, h1), 18f);
            yield return new WaitForSecondsRealtime(0.4f);
            FortressPlugin.Shot("helpers_walk");
            me.LocalTeleport(eye, YawTo(eye, field), true);
            Aim(YawTo(eye, field), 12f);
            yield return new WaitForSecondsRealtime(1.6f);
            FortressPlugin.Shot("helpers_fetch");
            Note("helpers at 4 s: " + Helpers.Instance.DebugState());
            yield return Until(() => C("helper_sold") >= 2 && FortressNet.Instance.ServerCorpses().Count() == 0, 60f, "helpers selling");
            Note($"helpers: picked {C("helper_picked")} of {corpses}, sold {C("helper_sold")} times for {C("helper_money")} (money {money0} -> {Siege.Instance.ServerCoins}), corpses left {FortressNet.Instance.ServerCorpses().Count()}");

            // Knocked out: four fish held round a helper standing at the stall.
            yield return new WaitForSecondsRealtime(4f);
            Vector3 hp = Helpers.Instance.DebugPos(0);
            var biters = new List<Creature>();
            for (int i = 0; i < 4; i++) biters.Add(plugin.ServerSpawnMinion(EnemyKind.Runner, hp + Quaternion.Euler(0f, 90f * i, 0f) * Vector3.forward * 1.1f + Vector3.up * 1f));
            foreach (var b in biters) if (b) Siege.Instance.Slow(b, 1f, 60f);
            me.LocalTeleport(new Vector3(hp.x + 4f, BossFx.Ground(hp + Vector3.right * 4f) + stand, hp.z + 4f), YawTo(hp + new Vector3(4f, 0f, 4f), hp), true);
            yield return Until(() => C("helper_down") >= 1, 15f, "a helper knocked out");
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("helpers_ko");
            Note($"knocked out: {C("helper_down")}; " + Helpers.Instance.DebugState());
            plugin.DebugKillAll();
            yield return new WaitForSecondsRealtime(1f);

            // The repair tower: a wall beside it and the lighthouse get health back, once a second.
            Vector3 core = island.CorePos;
            Vector3 towerAt = core + (center - core).Flat().normalized * 0f + new Vector3(5f, 0f, 0f);
            bool placed = TurretAt(PieceType.Mender, towerAt);
            float cell = BuildSystem.Cell;
            Vector3 wallAt = towerAt + new Vector3(3f, 0f, 0f);
            wallAt = new Vector3((Mathf.Floor(wallAt.x / cell) + 0.5f) * cell, BossFx.Ground(wallAt), Mathf.Floor(wallAt.z / cell) * cell);
            bool wallPlaced = BuildSystem.Instance.ServerPlace(PieceType.Wall, wallAt, 0f);
            yield return new WaitForSecondsRealtime(0.5f);
            var wall = BuildSystem.Instance.ServerPieces.Where(p => p.Type == PieceType.Wall).OrderBy(p => (p.Pos - wallAt).sqrMagnitude).FirstOrDefault();
            if (wall.Id != 0) BuildSystem.Instance.ServerDamagePiece(wall.Id, 120);
            Siege.Instance.ServerDamageCoreForTest(300);
            yield return new WaitForSecondsRealtime(0.2f);
            int wall0 = BuildSystem.Instance.ServerPieces.FirstOrDefault(p => p.Id == wall.Id).Hp, core0 = Siege.Instance.CoreHp, mended0 = C("mended");
            Vector3 look = towerAt + new Vector3(-6f, 0f, -6f);
            me.LocalTeleport(new Vector3(look.x, BossFx.Ground(look) + stand, look.z), YawTo(look, towerAt), true);
            Aim(YawTo(look, towerAt), 12f);
            yield return new WaitForSecondsRealtime(6f);
            FortressPlugin.Shot("mender");
            int wall1 = BuildSystem.Instance.ServerPieces.FirstOrDefault(p => p.Id == wall.Id).Hp;
            var ts = Catalog.Turret(PieceType.Mender, 1, 0);
            Note($"repair tower placed {placed}, wall {wallPlaced} at {(wall.Pos - towerAt).Flat().magnitude:0.0} m: wall {wall0} -> {wall1}/{wall.MaxHp} (expect +{ts.Damage}/s), lighthouse {core0} -> {Siege.Instance.CoreHp} (expect +{ts.Damage * Catalog.MendCore:0.#}/s while within {ts.Range} m: it's {(core - towerAt).Flat().magnitude:0.0} m), pulses {C("mended") - mended0}");

            // Every kind of walker side by side (and an elite runner), each in its own build and colour.
            plugin.DebugKillAll();
            yield return new WaitForSecondsRealtime(1f);
            float g0 = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g0), 0f, Mathf.Sin(g0)), side = Vector3.Cross(Vector3.up, gd);
            var kinds = new[] { EnemyKind.Runner, EnemyKind.Charger, EnemyKind.Bomber, EnemyKind.Tank, EnemyKind.Thief, EnemyKind.Shield, EnemyKind.Digger, EnemyKind.Healer, EnemyKind.Splitter };
            // In an arc 7 m round where I stand, all facing me.
            Vector3 cam = center + gd * 10f;
            cam.y = BossFx.Ground(cam);
            for (int i = 0; i <= kinds.Length; i++)
            {
                Vector3 at = cam + Quaternion.Euler(0f, (i - kinds.Length / 2f) * 11f, 0f) * gd * 7f;
                at.y = BossFx.Ground(at);
                plugin.DebugShowcase(i < kinds.Length ? kinds[i] : EnemyKind.Runner, i == kinds.Length, at, YawTo(at, cam));
            }
            me.LocalTeleport(cam + Vector3.up * stand, YawTo(cam, cam + gd), true);
            Aim(YawTo(cam, cam + gd), 14f);
            yield return new WaitForSecondsRealtime(2.5f);
            FortressPlugin.Shot("enemy_lineup");
            yield return new WaitForSecondsRealtime(0.6f);
        }

        // A close look at a stall helper: standing with a full basket, then knocked out.
        private IEnumerator HelperLookScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            var loot = Loot.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            yield return Revive();
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            float stand = FortressIsland.StandHeight;
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * stand, 0f, true);
            plugin.TestKey(UnityEngine.InputSystem.Key.F9, false);
            yield return Until(() => plugin.Running && plugin.InBuildTime, 15f, "round start");
            plugin.DebugExtendBuild(3000f);
            if (loot.Vault.Helpers < 1) loot.Vault.Helpers = 1;
            yield return Until(() => Helpers.Instance.DebugCount > 0, 5f, "a helper");
            Vector3 at = island.MarketPos + (center - island.MarketPos).Flat().normalized * 6f;
            at.y = BossFx.Ground(at);
            Vector3 eye = at + Quaternion.Euler(0f, 35f, 0f) * (center - island.MarketPos).Flat().normalized * 3.2f;
            eye.y = BossFx.Ground(eye) + stand;
            Helpers.Instance.DebugStage(0, at, YawTo(at, eye), 3, false, 6f);
            me.LocalTeleport(eye, YawTo(eye, at), true);
            Aim(YawTo(eye, at), 14f);
            yield return new WaitForSecondsRealtime(3f);
            Note("helper body: " + Helpers.Instance.DebugBody());
            FortressPlugin.Shot("helper_close");
            yield return new WaitForSecondsRealtime(0.6f);
            Helpers.Instance.DebugStage(0, at, YawTo(at, eye), 0, true, 6f);
            yield return new WaitForSecondsRealtime(2.5f);
            FortressPlugin.Shot("helper_close_ko");
            yield return new WaitForSecondsRealtime(0.6f);
            // Walking with a full basket: the helper heads for the crate while I watch from beside its path.
            Helpers.Instance.DebugStage(0, at, YawTo(at, island.CounterPos), 3, false, 0f);
            Vector3 side = at + (island.CounterPos - at).Flat().normalized * 3f + Vector3.Cross(Vector3.up, (island.CounterPos - at).Flat().normalized) * 3f;
            side.y = BossFx.Ground(side) + stand;
            me.LocalTeleport(side, YawTo(side, at), true);
            Aim(YawTo(side, at), 12f);
            yield return new WaitForSecondsRealtime(0.9f);
            FortressPlugin.Shot("helper_walking");
            yield return new WaitForSecondsRealtime(0.6f);

            // Every keeper from in front, the smith talking (waving).
            foreach (var k in island.Keepers)
            {
                Vector3 front = k.transform.position + k.transform.forward * 3f;
                front.y = BossFx.Ground(front) + stand;
                me.LocalTeleport(front, YawTo(front, k.transform.position), true);
                Aim(YawTo(front, k.transform.position), 6f);
                yield return new WaitForSecondsRealtime(1.2f);
                if (k.Role == "Smith") { k.Said(); VaultUI.Instance.Close(); yield return new WaitForSecondsRealtime(0.3f); }
                FortressPlugin.Shot("keeper_" + k.Role);
                yield return new WaitForSecondsRealtime(0.6f);
            }
        }

        // Reference for drawing our villagers in the game's own style: every NPC on the island photographed from the front
        // and the side, its parts listed (bones, meshes, sizes), and each part's mesh, as it stands now, written out as OBJ.
        private IEnumerator NpcRefScenario()
        {
            var me = Player.LocalPlayer;
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            string dir = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "fortress-shots");
            System.IO.Directory.CreateDirectory(dir);
            var npcs = Resources.FindObjectsOfTypeAll<NPC>().Where(n => n && n.gameObject.scene.IsValid() && n.gameObject.activeInHierarchy).ToList();
            Note($"npcs: {npcs.Count}: {string.Join(", ", npcs.Select(n => n.name + " at " + n.transform.position.ToString("F0")))}");
            int shot = 0;
            foreach (var n in npcs.Take(8))
            {
                var t = n.transform;
                var rends = n.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
                Bounds b = rends.Count > 0 ? rends[0].bounds : new Bounds(t.position, Vector3.one);
                foreach (var r in rends) b.Encapsulate(r.bounds);
                var sb = new System.Text.StringBuilder($"{n.name}: bounds {b.size:F2} from {b.min.y - t.position.y:F2} to {b.max.y - t.position.y:F2} over its feet");
                foreach (var r in n.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    sb.Append($"\n    {r.name}: mesh {(r.sharedMesh ? r.sharedMesh.name + " " + r.sharedMesh.vertexCount + "v " + r.sharedMesh.bounds.size.ToString("F2") : "-")}, {r.bones.Length} bones, mats {string.Join("/", r.sharedMaterials.Where(m => m).Select(m => m.name))}, on {(r.enabled ? "yes" : "no")}");
                var character = t.Find("Character");
                if (character)
                    foreach (var bone in character.GetComponentsInChildren<Transform>(true).Where(x => !x.GetComponent<Renderer>()))
                        sb.Append($"\n    bone {BonePath(bone, character)}: local {bone.localPosition:F3} rot {bone.localEulerAngles:F0} world-up {(bone.position.y - t.position.y):F2}");
                Note(sb.ToString());
                // Each part as it stands now, for Blender.
                foreach (var r in n.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh))
                {
                    var baked = new Mesh();
                    r.BakeMesh(baked);
                    WriteObj(System.IO.Path.Combine(dir, $"npc_{shot}_{r.name}.obj"), baked, t.InverseTransformPoint(r.transform.position), Quaternion.Inverse(t.rotation) * r.transform.rotation, r.transform.lossyScale);
                }
                // Photographs: front and side, a little above the eyes.
                foreach (var (angle, tag) in new[] { (0f, "front"), (90f, "side"), (35f, "three") })
                {
                    Vector3 dirTo = Quaternion.Euler(0f, angle, 0f) * t.forward;
                    Vector3 eye = t.position + dirTo * 2.4f + Vector3.up * 0.2f;
                    me.LocalTeleport(eye, YawTo(eye, t.position), true);
                    Aim(YawTo(eye, t.position), -4f);
                    yield return new WaitForSecondsRealtime(1.2f);
                    FortressPlugin.Shot($"npc_{shot}_{tag}");
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                shot++;
            }
        }

        private static string BonePath(Transform t, Transform root) { var s = t.name; while (t.parent && t.parent != root) { t = t.parent; s = t.name + "/" + s; } return s; }

        private static void WriteObj(string file, Mesh mesh, Vector3 offset, Quaternion rot, Vector3 scale)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            foreach (var v in mesh.vertices) { var w = offset + rot * Vector3.Scale(v, scale); sb.Append("v ").Append((-w.x).ToString("R", inv)).Append(' ').Append(w.y.ToString("R", inv)).Append(' ').Append(w.z.ToString("R", inv)).Append('\n'); }
            foreach (var uv in mesh.uv) sb.Append("vt ").Append(uv.x.ToString("R", inv)).Append(' ').Append(uv.y.ToString("R", inv)).Append('\n');
            var tris = mesh.triangles;
            bool uvs = mesh.uv.Length == mesh.vertexCount;
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i] + 1, b = tris[i + 2] + 1, c = tris[i + 1] + 1; // mirrored x: the winding flips too
                sb.Append(uvs ? $"f {a}/{a} {b}/{b} {c}/{c}\n" : $"f {a} {b} {c}\n");
            }
            System.IO.File.WriteAllText(file, sb.ToString());
        }

        // Reference for redrawing our buildings and decorations in the game's own style: the starting island seen from
        // round about, its biggest pieces (houses, docks, props) each from close by and listed; then our fortress island's
        // buildings from the same distance, to compare.
        private IEnumerator WorldRefScenario()
        {
            var me = Player.LocalPlayer;
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            Vector3 c = me.Transform.position;
            // The biggest things near where we stand: houses, docks, the lighthouse, boats, props.
            var parts = FindObjectsOfType<MeshRenderer>()
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && (r.bounds.center - c).Flat().magnitude < 90f && !r.name.StartsWith("Fortress"))
                .GroupBy(r => r.transform.parent ? r.transform.parent : r.transform)
                .Select(g => (root: g.Key, b: g.Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; }), n: g.Count()))
                .Where(x => x.b.size.magnitude > 2.5f && x.b.size.magnitude < 45f)
                .OrderByDescending(x => x.b.size.x * x.b.size.y * x.b.size.z).Take(16).ToList();
            Note("biggest pieces: " + string.Join("; ", parts.Select(x => $"{x.root.name} < {(x.root.parent ? x.root.parent.name : "-")} ({x.n} meshes) {x.b.size:F1} at {x.b.center - c:F0}")));
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad;
                Vector3 eye = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 26f + Vector3.up * 12f;
                Reveal.Instance.TestView(eye, c);
                yield return new WaitForSecondsRealtime(1.2f);
                FortressPlugin.Shot($"world_round_{i}");
                yield return new WaitForSecondsRealtime(0.4f);
            }
            int k = 0;
            foreach (var (root, b, n) in parts)
            {
                Vector3 look = b.center, eye = look + new Vector3(0.7f, 0.45f, -0.7f).normalized * Mathf.Max(4f, b.size.magnitude * 1.15f);
                Reveal.Instance.TestView(eye, look);
                yield return new WaitForSecondsRealtime(1f);
                FortressPlugin.Shot($"world_piece_{k++}_{root.name.Replace(' ', '_')}");
                yield return new WaitForSecondsRealtime(0.4f);
            }
            // Ours, to compare: the fortress island's buildings.
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            foreach (var (name, at) in new[] { ("vault", island.VaultPos), ("smithy", island.SmithyPos), ("stall", island.MarketPos), ("lighthouse", island.CorePos), ("center", island.Center) })
            {
                Vector3 eye = at + new Vector3(0.7f, 0.5f, -0.7f).normalized * 9f;
                Reveal.Instance.TestView(eye, at + Vector3.up);
                yield return new WaitForSecondsRealtime(1.2f);
                FortressPlugin.Shot($"ours_{name}");
                yield return new WaitForSecondsRealtime(0.4f);
            }
            Reveal.Instance.TestViewEnd();
        }

        // The game's other islands (where its towns are), for the look of their houses and props: the player is put on each
        // unlocked one in turn, and once it has streamed in it's photographed round about and its biggest pieces close up.
        private IEnumerator TownRefScenario()
        {
            var me = Player.LocalPlayer;
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            var spots = new[] { new Vector3(500f, 0f, -100f), new Vector3(310f, 0f, -570f), new Vector3(125f, 0f, -950f) };
            for (int s = 0; s < spots.Length; s++)
            {
                Vector3 c = spots[s];
                me.LocalTeleport(c + Vector3.up * 30f, 0f, true);
                yield return new WaitForSecondsRealtime(10f);
                var near = FindObjectsOfType<MeshRenderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy && (r.bounds.center - c).Flat().magnitude < 140f && !r.name.StartsWith("Fortress")).ToList();
                if (near.Count == 0) { Note($"island {s} at {c}: nothing streamed in"); continue; }
                var all = near.Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
                Note($"island {s} at {c}: {near.Count} meshes over {all.size:F0} round {all.center:F0}");
                var parts = near.GroupBy(r => r.transform.parent ? r.transform.parent : r.transform)
                    .Select(g => (root: g.Key, b: g.Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; }), n: g.Count()))
                    .Where(x => x.b.size.magnitude > 2.5f && x.b.size.magnitude < 40f)
                    .OrderByDescending(x => x.b.size.x * x.b.size.y * x.b.size.z).Take(14).ToList();
                Note("  biggest: " + string.Join("; ", parts.Select(x => $"{x.root.name} < {(x.root.parent ? x.root.parent.name : "-")} ({x.n}) {x.b.size:F1}")));
                for (int i = 0; i < 3; i++)
                {
                    float a = (i * 120f + 30f) * Mathf.Deg2Rad;
                    float r = Mathf.Max(30f, Mathf.Max(all.size.x, all.size.z) * 0.6f);
                    Vector3 eye = all.center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r + Vector3.up * r * 0.45f;
                    Reveal.Instance.TestView(eye, all.center);
                    yield return new WaitForSecondsRealtime(1.2f);
                    FortressPlugin.Shot($"town{s}_round_{i}");
                    yield return new WaitForSecondsRealtime(0.4f);
                }
                int k = 0;
                foreach (var (root, b, n) in parts)
                {
                    Vector3 look = b.center, eye = look + new Vector3(0.7f, 0.42f, -0.7f).normalized * Mathf.Max(5f, b.size.magnitude * 1.2f);
                    Reveal.Instance.TestView(eye, look);
                    yield return new WaitForSecondsRealtime(0.9f);
                    FortressPlugin.Shot($"town{s}_piece_{k++}_{root.name.Replace(' ', '_')}");
                    yield return new WaitForSecondsRealtime(0.3f);
                }
                Reveal.Instance.TestViewEnd();
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        // The forest island's cabin from close by, and every kind of thing on that island by its mesh's name (what props the
        // game has and how it draws them).
        private IEnumerator CabinRefScenario()
        {
            var me = Player.LocalPlayer;
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            Vector3 c = new Vector3(500f, 0f, -100f);
            me.LocalTeleport(c + Vector3.up * 30f, 0f, true);
            yield return new WaitForSecondsRealtime(10f);
            var near = FindObjectsOfType<MeshFilter>().Where(f => f && f.sharedMesh && f.GetComponent<MeshRenderer>() is MeshRenderer r && r.enabled && (r.bounds.center - c).Flat().magnitude < 140f).ToList();
            Note("meshes here: " + string.Join(", ", near.GroupBy(f => f.sharedMesh.name).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} x{g.Count()} {g.First().GetComponent<MeshRenderer>().bounds.size:F1}")));
            // Not trees, rocks or the ground: the man-made things.
            bool Natural(string n) => n.Contains("Tree") || n.Contains("Rock") || n.Contains("Grass") || n.Contains("Island") || n.Contains("Bush") || n.Contains("Fern") || n.Contains("Plant") || n.Contains("Terrain") || n.Contains("Water");
            var made = near.Where(f => !Natural(f.sharedMesh.name) && !Natural(f.name)).GroupBy(f => f.transform.parent ? f.transform.parent : f.transform)
                .Select(g => (root: g.Key, b: g.Select(f => f.GetComponent<MeshRenderer>().bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; })))
                .Where(x => x.b.size.magnitude > 0.6f && x.b.size.magnitude < 30f).OrderByDescending(x => x.b.size.magnitude).Take(12).ToList();
            Note("made things: " + string.Join("; ", made.Select(x => $"{x.root.name} {x.b.size:F1}")));
            int k = 0;
            foreach (var (root, b) in made)
            {
                foreach (float yaw in k < 2 ? new[] { 35f, 145f, 250f } : new[] { 35f })
                {
                    Vector3 look = b.center, eye = look + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0.35f, -1f).normalized * Mathf.Max(3f, b.size.magnitude * 1.3f);
                    Reveal.Instance.TestView(eye, look);
                    yield return new WaitForSecondsRealtime(0.9f);
                    FortressPlugin.Shot($"made_{k}_{(int)yaw}_{root.name.Replace(' ', '_')}");
                    yield return new WaitForSecondsRealtime(0.3f);
                }
                k++;
            }
            Reveal.Instance.TestViewEnd();
        }

        // The village as a visitor sees it: up the dock road, each station close by, the lighthouse plaza, the pier.
        private IEnumerator VillageLookScenario()
        {
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            yield return Revive();
            var me = Player.LocalPlayer;
            Vector3 c = island.Center;
            me.LocalTeleport(c + FortressIsland.ReadyLocal + Vector3.up * FortressIsland.StandHeight, 0f, true);
            yield return new WaitForSecondsRealtime(3f);
            Vector3 Up(Vector3 p, float h) => new Vector3(p.x, BossFx.Ground(p) + h, p.z);
            var views = new (string name, Vector3 eye, Vector3 look)[]
            {
                ("road", Up(c + new Vector3(0f, 0f, -27f), 2.2f), c + new Vector3(0f, 2f, -12f)),
                ("vault", Up(island.VaultPos + (c + new Vector3(0f, 0f, -18f) - island.VaultPos).Flat().normalized * 5.5f, 1.9f), island.VaultPos + Vector3.up * 1.3f),
                ("smithy", Up(island.SmithyPos + (c + new Vector3(0f, 0f, -18f) - island.SmithyPos).Flat().normalized * 6f, 1.9f), island.SmithyPos + Vector3.up * 1.3f),
                ("stall", Up(island.MarketPos + (c + new Vector3(0f, 0f, -18f) - island.MarketPos).Flat().normalized * 5.5f, 1.9f), island.MarketPos + Vector3.up * 1.2f),
                ("plaza", Up(c + new Vector3(7.5f, 0f, -7.5f), 3f), c + Vector3.up * 1.5f),
                ("pier", Up(c + new Vector3(3f, 0f, -42f), 2.5f), c + new Vector3(0f, 1.5f, -27f)),
                ("vault_in", island.VaultPos + Quaternion.Euler(0f, FortressIsland.VaultYaw, 0f) * new Vector3(-0.4f, 1.7f, 1.3f), island.VaultPos + Quaternion.Euler(0f, FortressIsland.VaultYaw, 0f) * new Vector3(1.2f, 1.2f, -0.6f)),
                ("rack", island.SmithyPos + Quaternion.Euler(0f, FortressIsland.SmithyYaw, 0f) * new Vector3(-0.84f, 1.8f, 1.0f), island.SmithyPos + Quaternion.Euler(0f, FortressIsland.SmithyYaw, 0f) * new Vector3(-0.84f, 1.95f, -1.3f)),
            };
            foreach (var (name, eye, look) in views)
            {
                Reveal.Instance.TestView(eye, look);
                yield return new WaitForSecondsRealtime(1.3f);
                FortressPlugin.Shot("village_" + name);
                yield return new WaitForSecondsRealtime(0.4f);
            }
            Reveal.Instance.TestViewEnd();
        }

        // The vault's screen and the game's hotbar: a weapon dragged from the vault onto an empty hotbar slot lands in it;
        // dragged from that slot back onto the vault, it goes in.
        private IEnumerator HotbarDragScenario()
        {
            var island = FortressIsland.Instance;
            var loot = Loot.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            if (!PlayerManager.InGodMode) PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            yield return Revive();
            var me = Player.LocalPlayer;
            Vector3 inside = island.VaultPos + Quaternion.Euler(0f, FortressIsland.VaultYaw, 0f) * new Vector3(-0.3f, 0.5f, 0.8f);
            me.LocalTeleport(inside, YawTo(inside, island.VaultPos), true);
            yield return new WaitForSecondsRealtime(1.5f);
            Note($"standing in the vault at {(me.Transform.position - island.VaultPos).Flat().magnitude:0.0} m from its middle, height {me.Transform.position.y - island.VaultPos.y:0.00}");
            while (loot.Vault.StorageSlots - loot.Vault.SlotsUsed < 4) loot.Vault.Bought[Expansion.Storage] = loot.Vault.BoughtOf(Expansion.Storage) + 1;
            loot.Vault.Add(Gear.Laser, Rarity.Common, 2);
            loot.SaveVault();
            yield return new WaitForSecondsRealtime(0.5f);
            // Hands and hotbar cleared of earlier tests' weapons (one held but not in the hotbar would take the first empty slot).
            foreach (var i in me.Inventory._items.Values.Append(me.Holding.HeldItem).Where(i => i && WeaponItems.Is(i, out _, out _)).Distinct().ToList()) { me.Inventory.RemoveItem(i); i.DestroyItem(); }
            yield return new WaitForSecondsRealtime(0.5f);
            VaultUI.Instance.Open(VaultUI.Desk.Quartermaster);
            yield return new WaitForSecondsRealtime(1f);
            var usable = VaultUI.Instance.DebugHotbarSlots;
            int free = usable.Where(i => !me.Inventory._items.TryGetValue((byte)i, out var it) || !it).DefaultIfEmpty(-1).Last();
            Note($"hotbar handles {VaultUI.Instance.DebugHotbarHandles} over slots {string.Join(",", usable)}, an empty one: {free}");
            VaultUI.Instance.DebugHotbarLight(true);
            yield return new WaitForSecondsRealtime(0.4f);
            FortressPlugin.Shot("hotbar_lit");
            yield return new WaitForSecondsRealtime(0.4f);
            VaultUI.Instance.DebugHotbarLight(false);
            int lasers0 = loot.Vault.Count(Gear.Laser, Rarity.Common);
            if (free >= 0) VaultUI.Instance.DebugHotbarTake(Gear.Laser, Rarity.Common, free);
            yield return new WaitForSecondsRealtime(2.5f);
            var there = free >= 0 && me.Inventory._items.TryGetValue((byte)free, out var t1) ? t1 : null;
            Note($"vault -> hotbar slot {free}: in it '{(there ? there.GetName() : "nothing")}', common lasers {lasers0} -> {loot.Vault.Count(Gear.Laser, Rarity.Common)} (expect -1)");
            FortressPlugin.Shot("hotbar_after_take");
            yield return new WaitForSecondsRealtime(0.4f);
            int lasers1 = loot.Vault.Count(Gear.Laser, Rarity.Common);
            if (free >= 0) VaultUI.Instance.DebugHotbarStore(free);
            yield return new WaitForSecondsRealtime(1.5f);
            var after = free >= 0 && me.Inventory._items.TryGetValue((byte)free, out var t2) ? t2 : null;
            Note($"hotbar slot {free} -> vault: in it now '{(after ? after.GetName() : "nothing")}' (expect nothing), common lasers {lasers1} -> {loot.Vault.Count(Gear.Laser, Rarity.Common)} (expect +1)");
            VaultUI.Instance.Close();
        }

        private IEnumerator Vault2Scenario()
        {
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            var me = Player.LocalPlayer;
            var loot = Loot.Instance;
            var plugin = FortressPlugin.Instance;
            int C(string k) => Counters.TryGetValue(k, out int n) ? n : 0;
            IEnumerator View(Vector3 eye, Vector3 at, string shot, float pitch, float wait = 1.2f)
            {
                me.LocalTeleport(eye, YawTo(eye, at), true);
                yield return null;
                Aim(YawTo(eye, at), pitch);
                yield return new WaitForSecondsRealtime(wait);
                if (shot != null) { FortressPlugin.Shot(shot); yield return new WaitForSecondsRealtime(0.4f); }
            }
            int Lying() => FindObjectsOfType<Item>().Count(i => i && i.IsSpawned && !i.IsDestroying && !i.Holder && !i.IsInInventory && (!i.Creature || i.Creature.IsDead)
                                                              && island.OnIsland(i.transform.position, FortressIsland.IslandRadius + 15f));
            var all = (Dictionary<byte, Item>)HarmonyLib.AccessTools.Field(typeof(GameInfo), "_allItems").GetValue(null);
            var fishKind = all.Values.FirstOrDefault(i => i is Creature c && c.BossType == BossType.None && !(i is RunningFish) && i.name.ToLower().Contains("bass"))
                        ?? all.Values.First(i => i is Creature c && c.BossType == BossType.None && !(i is RunningFish));
            Item DeadFish(Vector3 at)
            {
                var f = Instantiate(fishKind, at, Quaternion.identity, Server.Instance.DynamicObjectsHolder);
                f.Creature.ServerKillOnSpawn();
                Server.Instance.Spawn(f.gameObject);
                return f;
            }
            var knifePrefab = GameInfo.GetSpawnable("knife");
            byte knifeId = knifePrefab.ID;

            // Shift+F7: what lies about goes.
            for (int i = 0; i < 3; i++) ItemManager.Instance.SpawnNewItem(knifePrefab, island.Center + new Vector3(2f + i, 3f, -9f), Quaternion.identity);
            WeaponItems.ServerSpawn(Gear.Laser, Rarity.Common, island.Center + new Vector3(-2f, 3f, -9f));
            yield return new WaitForSecondsRealtime(1.5f);
            int lying = Lying();
            plugin.TestKey(UnityEngine.InputSystem.Key.F7, true);
            yield return new WaitForSecondsRealtime(1f);
            Note($"Shift+F7: {lying} things lying on the island -> {Lying()} (expect 0), cleared {C("ground_cleared")}");

            // Room in the test vault first (runs before have filled it up).
            while (loot.Vault.StorageSlots - loot.Vault.SlotsUsed < 8) loot.Vault.Bought[Expansion.Storage] = loot.Vault.BoughtOf(Expansion.Storage) + 1;
            // The vault's door swallows a dead fish, a knife and a fortress weapon (and the game's own guns, as a chest gives them).
            var chute = FindObjectOfType<VaultChute>().transform.position;
            Vector3 front = island.VaultPos + Quaternion.Euler(0f, FortressIsland.VaultYaw, 0f) * new Vector3(1.6f, FortressIsland.StandHeight + 0.3f, 4.2f);
            yield return View(front, chute, null, 6f);
            int stored0 = C("vault_stored"), fish0 = loot.Vault.CountItem(fishKind.ID), knives0 = loot.Vault.CountItem(knifeId), rare0 = loot.Vault.Count(Gear.Laser, Rarity.Rare);
            var fish = DeadFish(chute + Vector3.up * 2.2f);
            float weight = fish.RandomizedWeight;
            ItemManager.Instance.SpawnNewItem(knifePrefab, chute + Vector3.up * 2.8f, Quaternion.identity);
            WeaponItems.ServerSpawn(Gear.Laser, Rarity.Rare, chute + Vector3.up * 3.4f);
            int guns0 = loot.Vault.Items.Count, commons0 = loot.Vault.Count(Gear.Shotgun, Rarity.Common) + loot.Vault.Count(Gear.Pistol, Rarity.Common);
            foreach (var gun in new[] { "shotgun", "pistol" }) if (GameInfo.GetSpawnable(gun) is Item gp && gp) ItemManager.Instance.SpawnNewItem(gp, chute + Vector3.up * 4f, Quaternion.identity);
            yield return new WaitForSecondsRealtime(0.6f);
            FortressPlugin.Shot("vault_swallow");
            yield return new WaitForSecondsRealtime(2f);
            Note($"vault door: stored {C("vault_stored") - stored0} (expect 3); {fishKind.name} {fish0} -> {loot.Vault.CountItem(fishKind.ID)} (weight {weight:0.000}), knives {knives0} -> {loot.Vault.CountItem(knifeId)}, rare lasers {rare0} -> {loot.Vault.Count(Gear.Laser, Rarity.Rare)}; slots {loot.Vault.SlotsUsed}/{loot.Vault.StorageSlots}");

            // Out again as it went in.
            loot.ServerRequest(me, new VaultRequest { Action = VaultAction.TakeItem, Item = fishKind.ID, Slot = 0, Count = 1 });
            yield return new WaitForSecondsRealtime(1.5f);
            var back = FindObjectsOfType<Item>().Where(i => i && i.IsSpawned && i.ID == fishKind.ID && Mathf.Abs(i.RandomizedWeight - weight) < 0.001f).FirstOrDefault();
            Note(back ? $"fish taken out: weight {back.RandomizedWeight:0.000} (went in at {weight:0.000}), dead {back.Creature && back.Creature.IsDead}, in my hands {back.Holder == me || back.IsInInventory}, taken {C("item_taken")}" : "fish taken out: NONE");

            // The vault's screen (the game's items get their pictures taken meanwhile).
            yield return View(island.VaultDoor + Vector3.up * FortressIsland.StandHeight, island.VaultPos, null, 5f, 0.6f);
            VaultUI.Instance.Open(VaultUI.Desk.Quartermaster);
            yield return new WaitForSecondsRealtime(2.5f);
            FortressPlugin.Shot("vault_ui_all"); yield return new WaitForSecondsRealtime(0.6f);
            VaultUI.Instance.DebugTab(3);
            yield return new WaitForSecondsRealtime(1.5f);
            FortressPlugin.Shot("vault_ui_items"); yield return new WaitForSecondsRealtime(0.6f);
            Note($"game items in the vault: {string.Join(", ", loot.ClientVault.ItemCount.Select(kv => (GameInfo.GetSpawnable(kv.Key) ? GameInfo.GetSpawnable(kv.Key).name : "#" + kv.Key) + " x" + kv.Value))} (host list {loot.Vault.Items.Count}, guns kept as game items {loot.Vault.Items.Count - guns0} (expect 0), turned into commons {loot.Vault.Count(Gear.Shotgun, Rarity.Common) + loot.Vault.Count(Gear.Pistol, Rarity.Common) - commons0} (expect 2))");
            VaultUI.Instance.DebugTab(1);
            var owned = loot.ClientVault.Owned.Keys.FirstOrDefault(k => !GearCatalog.IsWeapon(k.Item1));
            VaultUI.Instance.DebugSelect(owned.Item1, owned.Item2);
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("vault_ui_gear"); yield return new WaitForSecondsRealtime(0.6f);
            VaultUI.Instance.Close();

            // The fish stall's grilled fish: bought, it lands on the counter, cooked.
            int food0 = C("stall_food");
            FishNet.InstanceFinder.ClientManager.Broadcast(new NetRequest { Food = true });
            yield return new WaitForSecondsRealtime(1.5f);
            var grilled = FindObjectsOfType<Item>().Where(i => i && i.IsSpawned && i.Creature && i.Creature.IsDead && (i.transform.position - island.MarketPos).sqrMagnitude < 16f).ToList();
            Note($"stall food: bought {C("stall_food") - food0}, dead fish at the stall {grilled.Count}, cookness {string.Join(", ", grilled.Select(g => g.Cookness.ToString("0.0")))}");

            // The anvil takes weapons (sparks), and the bench forges three into one.
            Vector3 anvil = island.SmithyPos + Quaternion.Euler(0f, FortressIsland.SmithyYaw, 0f) * new Vector3(-0.2f, 1.6f, 0.45f);
            Vector3 forge = island.SmithyPos + Quaternion.Euler(0f, FortressIsland.SmithyYaw, 0f) * new Vector3(0.6f, FortressIsland.StandHeight + 0.2f, 2.6f);
            yield return View(forge, anvil, null, 12f);
            int common0 = loot.Vault.Count(Gear.Laser, Rarity.Common);
            for (int i = 0; i < 3; i++) WeaponItems.ServerSpawn(Gear.Laser, Rarity.Common, anvil + Vector3.up * (0.3f + 0.3f * i));
            yield return new WaitForSecondsRealtime(0.55f);
            FortressPlugin.Shot("anvil_swallow");
            yield return new WaitForSecondsRealtime(2f);
            Note($"anvil: common lasers {common0} -> {loot.Vault.Count(Gear.Laser, Rarity.Common)} (expect +3)");
            VaultUI.Instance.Open(VaultUI.Desk.Smith);
            VaultUI.Instance.DebugSelect(Gear.Laser, Rarity.Common);
            yield return new WaitForSecondsRealtime(1.5f);
            FortressPlugin.Shot("smith_bench"); yield return new WaitForSecondsRealtime(0.6f);
            int rareB = loot.Vault.Count(Gear.Laser, Rarity.Rare);
            loot.ServerRequest(me, new VaultRequest { Action = VaultAction.Merge, Gear = Gear.Laser, Rarity = Rarity.Common });
            yield return new WaitForSecondsRealtime(1.2f);
            Note($"bench: rare lasers {rareB} -> {loot.Vault.Count(Gear.Laser, Rarity.Rare)} (expect +1)");
            FortressPlugin.Shot("smith_bench_after"); yield return new WaitForSecondsRealtime(0.6f);
            VaultUI.Instance.Close();

            // The crate: a fish splashes in.
            Vector3 crateFront = island.CounterPos + new Vector3(-2.6f, FortressIsland.StandHeight + 0.4f, -1.4f);
            yield return View(crateFront, island.CounterPos + Vector3.up, null, 15f);
            DeadFish(island.CounterPos + Vector3.up * 2.6f);
            yield return new WaitForSecondsRealtime(0.85f);
            FortressPlugin.Shot("crate_swallow");
            yield return new WaitForSecondsRealtime(1.5f);
            Note($"crate: sold {C("crate_sold_item")}");

            // The lighthouse: E opens the gate (it sinks, the clam opens, the lantern flashes red), E again shuts it, E and wait:
            // the round, the gate sunk, the clam open.
            var door = FindObjectOfType<LighthouseDoor>();
            Vector3 lh = island.LighthousePos;
            yield return View(lh + new Vector3(0.6f, FortressIsland.StandHeight + 0.3f, -3.4f), lh + Vector3.up * 1.6f, "lighthouse_door", 2f);
            var gate = door ? door.transform.parent.Find("Gate") : null;
            float gateShut = gate ? gate.localPosition.y : -1f;
            var lid = door ? door.transform.parent.Find("ClamHinge") : null;
            if (door) door.Interact(me);
            yield return new WaitForSecondsRealtime(1.6f);
            Note($"E at the door: counting {island.Starting}, countdown {Siege.Instance.View.ReadyCountdown}, pulled {C("gate_pulled")}");
            FortressPlugin.Shot("gate_countdown"); yield return new WaitForSecondsRealtime(0.4f);
            if (door) door.Interact(me);
            yield return new WaitForSecondsRealtime(1.2f);
            Note($"E again: counting {island.Starting} (expect False), cancelled {C("gate_cancelled")}, round {plugin.Running}");
            if (door) door.Interact(me);
            yield return Until(() => plugin.Running, 10f, "the round to start");
            yield return new WaitForSecondsRealtime(1.5f);
            yield return new WaitForSecondsRealtime(3f);
            Note($"round started from the door: {plugin.Running}; gate {gateShut:0.00} -> {(gate ? gate.localPosition.y : -1f):0.00} (sunk), clam open {(lid ? 360f - lid.localEulerAngles.x : -1f):0} deg");
            FortressPlugin.Shot("gate_open"); yield return new WaitForSecondsRealtime(0.4f);

            // The smith's shop (only during a round): its tab, a common boomerang bought onto the counter; in hand, what it does on the HUD.
            Siege.Instance.ServerAddCoins(Siege.Money(2000));
            yield return View(forge, anvil, null, 12f);
            VaultUI.Instance.Open(VaultUI.Desk.Smith);
            VaultUI.Instance.DebugTab(2);
            VaultUI.Instance.DebugSelect(Gear.Boomerang, Rarity.Common);
            yield return new WaitForSecondsRealtime(1.2f);
            FortressPlugin.Shot("smith_shop"); yield return new WaitForSecondsRealtime(0.5f);
            int bought0 = C("weapon_bought");
            loot.ServerRequest(me, new VaultRequest { Action = VaultAction.Buy, Gear = Gear.Boomerang });
            yield return new WaitForSecondsRealtime(1f);
            VaultUI.Instance.Close();
            var bought = FindObjectsOfType<Item>().FirstOrDefault(i => i && i.IsSpawned && WeaponItems.Is(i, out var bg, out var br) && bg == Gear.Boomerang && br == Rarity.Common);
            Note($"shop: bought {C("weapon_bought") - bought0}, a common boomerang {(bought ? (bought.Holder == me || bought.IsInInventory ? "in my hands" : "on the ground") : "NOWHERE")}");
            if (bought) { if (!(bought.Holder == me || bought.IsInInventory)) bought.PickUp(me, calledFromLocal: true, sendToServer: true); yield return new WaitForSecondsRealtime(1.5f); FortressPlugin.Shot("weapon_hud"); yield return new WaitForSecondsRealtime(0.5f); }
            plugin.DebugStopRound();
            yield return Until(() => !plugin.Running, 10f, "round over");
            yield return new WaitForSecondsRealtime(1f);
            FortressIsland.Instance.CloseResults();
            yield return new WaitForSecondsRealtime(2.5f);
            Note($"after the round: gate at {(gate ? gate.localPosition.y : -1f):0.00} (rising back to {gateShut:0.00})");
            FortressPlugin.Shot("gate_shut");
            yield return new WaitForSecondsRealtime(0.4f);
        }

        // The island's entrance (from the main island: the camera flies out, the island rises, the lighthouse lights up, OVER 99),
        // a skip part way, a win on the save (gold lighthouse, the record board), and the camera round the lighthouse on a win.
        // Each time the player must get their own camera and controls back, and the island must be back in its place.
        private IEnumerator RevealScenario()
        {
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            var me = Player.LocalPlayer;
            int C(string k) => Counters.TryGetValue(k, out int n) ? n : 0;
            Note("sounds: " + string.Join(", ", new[] { "BossFightIntro", "BossFightOutro", "QuestFinished", "WhaleLanding_0", "WhaleLanding_2", "CasinoWinEpic", "Outro" }
                .Select(n => n + (AudioManager.GetClip(n) ? " ok" : " MISSING"))));
            yield return new WaitForSecondsRealtime(1f);
            var lh = Siege.Instance.Lighthouse;
            Note($"lighthouse between rounds: {(lh ? (lh.position - island.Center).ToString("F2") : "none")} (expect about (0, 1.4, 0))");
            Note($"player {Vector3.Distance(me.Transform.position, island.Center):0} m from the island; seen before {Reveal.Seen.Value}");

            Reveal.Instance.Replay();
            yield return Until(() => Reveal.Playing, 5f, "the entrance to start");
            yield return new WaitForSecondsRealtime(3.2f);
            Note($"playing: camera {(GameInfo.CurCamera ? GameInfo.CurCamera.name : "none")}, own camera on {me.Camera.Cam.enabled}, inputs blocked {me.BlockInputs}, island {island.Sink:0.0} m under");
            yield return Until(() => !Reveal.Playing, 30f, "the entrance to end");
            yield return new WaitForSecondsRealtime(1.5f);
            Note($"entrance done: own camera back {GameInfo.CurCamera == me.Camera.Cam && me.Camera.Cam.enabled}, inputs blocked {me.BlockInputs}, island in place {island.Sink == 0f}, " +
                 $"started {C("reveal_started")} done {C("reveal_done")}, seen {Reveal.Seen.Value}");
            FortressPlugin.Shot("reveal_after"); yield return new WaitForSecondsRealtime(0.6f);

            Reveal.Instance.Replay();
            yield return new WaitForSecondsRealtime(4f);
            Reveal.Instance.Skip();
            yield return Until(() => !Reveal.Playing, 6f, "the skipped entrance to end");
            yield return new WaitForSecondsRealtime(1f);
            Note($"skipped: playing {Reveal.Playing}, island in place {island.Sink == 0f}, own camera back {GameInfo.CurCamera == me.Camera.Cam}, inputs blocked {me.BlockInputs}");

            // A win on the save: the board lists it, the lighthouse goes gold.
            Loot.Instance.ServerWin("Tester、Owen");
            yield return new WaitForSecondsRealtime(1.5f);
            Note($"wins on the save {Loot.Instance.ClientVault.Wins.Count}: {string.Join(" / ", Loot.Instance.ClientVault.Wins.Select(w => w.date + " " + w.team))}");
            IEnumerator View(Vector3 eye, Vector3 at, string shot, float pitch)
            {
                me.LocalTeleport(eye, YawTo(eye, at), true);
                yield return null;
                Aim(YawTo(eye, at), pitch);
                yield return new WaitForSecondsRealtime(1.5f);
                FortressPlugin.Shot(shot);
                yield return new WaitForSecondsRealtime(0.5f);
            }
            var board = FindObjectOfType<RecordBoard>();
            if (board)
            {
                Vector3 front = board.transform.position - board.transform.forward * 2.6f + Vector3.up * (FortressIsland.StandHeight + 0.4f);
                yield return View(front, board.transform.position + Vector3.up * 1.45f, "record_board", 4f);
                var bt = board.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                Note(bt ? $"board text: chars {bt.textInfo.characterCount}, length {bt.text.Length}, font {(bt.font ? bt.font.name : "none")}, shader {(bt.materialForRendering ? bt.materialForRendering.shader.name : "none")}, " +
                          $"colour {bt.color}, active {bt.isActiveAndEnabled}, culled {bt.canvasRenderer.cull}, canvas {(bt.canvas ? bt.canvas.renderMode.ToString() + " layer " + bt.canvas.gameObject.layer : "none")}, world bounds {bt.rectTransform.position} scale {bt.rectTransform.lossyScale.x:0.0000}"
                        : "board text: NONE");
            }
            else Note("NO record board");
            Vector3 lhFoot = island.LighthousePos;
            yield return View(lhFoot + new Vector3(2f, FortressIsland.StandHeight + 0.6f, -11f), lhFoot + Vector3.up * 3f, "lighthouse_gold", -12f);

            // The victory camera, from the start circle.
            me.LocalTeleport(island.Center + FortressIsland.ReadyLocal + Vector3.up * (FortressIsland.StandHeight + 0.2f), 0f, true);
            yield return new WaitForSecondsRealtime(1f);
            Reveal.Instance.Victory(island.LighthousePos);
            yield return Until(() => Reveal.Playing, 5f, "the victory camera to start");
            yield return Until(() => !Reveal.Playing, 20f, "the victory camera to end");
            yield return new WaitForSecondsRealtime(1f);
            Note($"victory camera done: ran {C("victory_camera")}, own camera back {GameInfo.CurCamera == me.Camera.Cam}, inputs blocked {me.BlockInputs}");
        }

        private IEnumerator VillageScenario()
        {
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            Tips.Show.Value = false;
            var me = Player.LocalPlayer;
            var loot = Loot.Instance;
            ClearLooseWeapons();
            yield return new WaitForSecondsRealtime(1f);
            Note($"keepers: {string.Join(", ", island.Keepers.Select(k => $"{k.Role} at {k.transform.position - island.Center:F1}"))}");
            Note($"stations: market {island.MarketPos - island.Center:F1}, smithy {island.SmithyPos - island.Center:F1}, vault {island.VaultPos - island.Center:F1}, crate {island.CounterPos - island.Center:F1}");
            IEnumerator View(Vector3 eye, Vector3 at, string shot, float pitch = 8f)
            {
                me.LocalTeleport(eye, YawTo(eye, at), true);
                yield return null;
                Aim(YawTo(eye, at), pitch);
                yield return new WaitForSecondsRealtime(1.2f);
                FortressPlugin.Shot(shot);
                yield return new WaitForSecondsRealtime(0.4f);
            }
            Vector3 c = island.Center;
            yield return View(c + new Vector3(0f, 1.2f + FortressIsland.StandHeight, -6.5f), c + new Vector3(0f, 0f, -16f), "village_overview", 12f);
            Vector3 Front(Vector3 pos, float yaw, float d) => pos + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, FortressIsland.StandHeight, d);
            yield return View(Front(island.MarketPos, FortressIsland.MarketYaw, 5f), island.MarketPos + Vector3.up * 1.2f, "village_market");
            yield return View(Front(island.SmithyPos, FortressIsland.SmithyYaw, 5.5f), island.SmithyPos + Vector3.up * 1.4f, "village_smithy");
            yield return View(Front(island.VaultPos, FortressIsland.VaultYaw, 5f) + Quaternion.Euler(0f, FortressIsland.VaultYaw, 0f) * new Vector3(1.5f, 0f, 0f), island.VaultPos + Vector3.up * 1.2f, "village_vault");
            var lk = island.Keepers.FirstOrDefault(k => k.Role == "Lighthouse");
            if (lk) yield return View(lk.transform.position + new Vector3(1.2f, 0.3f, -3.2f), lk.transform.position + Vector3.up * 1.2f, "village_lighthouse_keeper", 5f);

            // Look at the smith: the game's own hover (outline, "Talk [E]") should pick our keeper.
            var smith = island.Keepers.FirstOrDefault(k => k.Role == "Smith");
            if (smith)
            {
                Vector3 stand = Front(smith.transform.position, FortressIsland.SmithyYaw, 2.2f);
                yield return View(stand, smith.transform.position + Vector3.up * 1.3f, "village_smith_hover", 6f);
                var it = HarmonyLib.AccessTools.Field(me.Holding.GetType(), "_interactable")?.GetValue(me.Holding);
                Note($"looking at the smith: hovering {(it != null ? it.GetType().Name : "nothing")}");
                smith.Said();
                yield return new WaitForSecondsRealtime(1f);
                Note($"smith desk open {VaultUI.IsOpen} ({VaultUI.Instance.CurrentDesk})");
                FortressPlugin.Shot("village_smith_ui_empty"); yield return new WaitForSecondsRealtime(0.6f);
            }
            // Three common lasers dropped on the anvil go on the rack; forge them; take the rare one off.
            int before = loot.Vault.Count(Gear.Laser, Rarity.Common), rareBefore = loot.Vault.Count(Gear.Laser, Rarity.Rare);
            Vector3 anvil = island.SmithyPos + Quaternion.Euler(0f, FortressIsland.SmithyYaw, 0f) * new Vector3(-0.2f, 1.6f, 0.45f);
            for (int i = 0; i < 3; i++) { WeaponItems.ServerSpawn(Gear.Laser, Rarity.Common, anvil + Vector3.up * 0.3f * i); yield return new WaitForSecondsRealtime(0.3f); }
            yield return Until(() => loot.Vault.Count(Gear.Laser, Rarity.Common) >= before + 3, 6f, "lasers on the rack");
            Note($"rack: common lasers {before} -> {loot.Vault.Count(Gear.Laser, Rarity.Common)} (expect +3)");
            loot.ServerRequest(me, new VaultRequest { Action = VaultAction.Merge, Gear = Gear.Laser, Rarity = Rarity.Common });
            Note($"forged: rare lasers {rareBefore} -> {loot.Vault.Count(Gear.Laser, Rarity.Rare)} (expect +1), common {loot.Vault.Count(Gear.Laser, Rarity.Common)}");
            VaultUI.Instance.DebugSelect(Gear.Laser, Rarity.Rare);
            yield return new WaitForSecondsRealtime(1f);
            FortressPlugin.Shot("village_smith_ui"); yield return new WaitForSecondsRealtime(0.6f);
            loot.ServerRequest(me, new VaultRequest { Action = VaultAction.Take, Gear = Gear.Laser, Rarity = Rarity.Rare });
            yield return new WaitForSecondsRealtime(1.5f);
            var taken = FindObjectsOfType<Item>().Where(x => x && x.IsSpawned && WeaponItems.Is(x, out var tg, out var tr) && tg == Gear.Laser && tr == Rarity.Rare).ToList();
            Note($"taken: rare lasers on the rack {loot.Vault.Count(Gear.Laser, Rarity.Rare)}, in the world {taken.Count} at {string.Join(" ", taken.Select(x => (x.transform.position - island.SmithyCounter).magnitude.ToString("0.0") + "m from the counter"))}");
            VaultUI.Instance.Close();
            // Rack the one in your hand from the desk.
            if (taken.Count > 0)
            {
                taken[0].PickUp(me, calledFromLocal: true, sendToServer: true);
                yield return new WaitForSecondsRealtime(1f);
                Note($"holding {(me.Holding.HeldItem ? me.Holding.HeldItem.GetName() : "nothing")}");
                loot.ServerRequest(me, new VaultRequest { Action = VaultAction.Deposit, Gear = Gear.Laser, Rarity = Rarity.Rare });
                yield return new WaitForSecondsRealtime(1f);
                Note($"racked from the hand: rare lasers on the rack {loot.Vault.Count(Gear.Laser, Rarity.Rare)}, holding {(me.Holding.HeldItem ? me.Holding.HeldItem.GetName() : "nothing")}");
            }
            // The rack on the wall.
            Vector3 rackEye = island.SmithyPos + Quaternion.Euler(0f, FortressIsland.SmithyYaw, 0f) * new Vector3(-1.2f, FortressIsland.StandHeight + 0.12f, 0.9f);
            yield return View(rackEye, island.SmithyPos + Quaternion.Euler(0f, FortressIsland.SmithyYaw, 0f) * new Vector3(-0.75f, 1.6f, -1.3f), "village_rack", -5f);

            // The fish stall.
            var fishmonger = island.Keepers.FirstOrDefault(k => k.Role == "Market");
            if (fishmonger)
            {
                yield return View(Front(fishmonger.transform.position, FortressIsland.MarketYaw, 2.2f), fishmonger.transform.position + Vector3.up * 1.3f, "village_market_hover", 6f);
                fishmonger.Said();
                yield return new WaitForSecondsRealtime(1f);
                Note($"stall open {StallMenu.IsOpen}");
                FortressPlugin.Shot("village_stall"); yield return new WaitForSecondsRealtime(0.6f);
                StallMenu.Instance.Close();
            }
            if (lk) { lk.Said(); yield return new WaitForSecondsRealtime(0.8f); Note($"help open {Tips.Instance.HelpOpen}"); FortressPlugin.Shot("village_help"); yield return new WaitForSecondsRealtime(0.6f); Tips.Instance.ToggleHelp(); }

            // Under the ground (in the water below the island): put back on top.
            int C(string k) => Counters.TryGetValue(k, out int n) ? n : 0;
            me.LocalTeleport(island.Center + new Vector3(3f, -1.0f, 9f), 0f, true);
            yield return new WaitForSecondsRealtime(2f);
            Note($"under the island: now at {me.Transform.position - island.Center:F1}, rescued {C("rescued_from_below")}");
            // Die and come back: on the respawn spot, standing on the ground.
            PlayerManager.ToggleGodMode();
            me.Vitals.TakeDamage(9999, me.Transform.position, Vector3.zero, true);
            yield return Until(() => !PlayerManager.AlivePlayers.Contains(me), 5f, "death");
            yield return new WaitForSecondsRealtime(2f);
            HarmonyLib.AccessTools.Method(typeof(PlayerDying), "LocalRespawn").Invoke(me.Dying, null);
            yield return Until(() => PlayerManager.AlivePlayers.Contains(me), 15f, "respawn");
            yield return new WaitForSecondsRealtime(3f);
            Vector3 back = me.Transform.position;
            bool stands = Physics.Raycast(back + Vector3.up * 0.3f, Vector3.down, out var under, 3f, GameInfo.LevelLayer);
            Note($"respawned {Vector3.Distance(back, island.RespawnPos):F1} m from the spot, at y {back.y - island.Center.y:F1}, ground {(stands ? (under.point.y - island.Center.y).ToString("F1") : "none")} below, rescued {C("rescued_from_below")}");
            var body = me.GetComponentsInChildren<CapsuleCollider>().Select(cc => $"{PathOf(cc.transform)} center {cc.center.y:F2} height {cc.height:F2} r {cc.radius:F2} scale {cc.transform.lossyScale.y:F2} at {cc.transform.position.y - back.y:F2}");
            Note($"player body: {string.Join("; ", body)}");
            FortressPlugin.Shot("village_respawn"); yield return new WaitForSecondsRealtime(0.6f);
        }

        // The game's people: its NPCs, the player's own character and the outfits, and whether a copy of the character can
        // stand on the fortress island as a shopkeeper.
        private IEnumerator NpcScenario()
        {
            var island = FortressIsland.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            Tips.Show.Value = false;
            var me = Player.LocalPlayer;
            // Where a character rig can always be had: every skinned head in memory (scene objects and prefabs), and the dead player.
            foreach (var g in Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>().Where(r => r && r.sharedMesh && r.sharedMesh.name == "CharacterHead").GroupBy(r => r.transform.root.name).Take(30))
            {
                var r0 = g.First();
                Note($"rig: {PathOf(r0.transform)} x{g.Count()} scene {(r0.gameObject.scene.IsValid() ? r0.gameObject.scene.name : "-")} active {r0.gameObject.activeInHierarchy} bones {r0.bones.Length} [{string.Join(",", r0.bones.Select(b => b ? b.name : "null"))}] siblings [{string.Join(",", r0.transform.parent ? r0.transform.parent.Cast<Transform>().Select(t => t.name) : new string[0])}]");
            }
            foreach (var dp in Resources.FindObjectsOfTypeAll<DeadPlayer>().Take(5))
                Note($"dead player {PathOf(dp.transform)} scene {(dp.gameObject.scene.IsValid() ? "yes" : "prefab")} comps [{string.Join(",", dp.GetComponentsInChildren<Component>(true).Select(c => c.GetType().Name).Distinct())}]");
            string Smr(SkinnedMeshRenderer r) => r ? $"{PathOf(r.transform)} mesh {(r.sharedMesh ? r.sharedMesh.name : "-")} mat {(r.sharedMaterial ? r.sharedMaterial.name + "/" + r.sharedMaterial.shader.name : "-")} bones {r.bones.Length} root {(r.rootBone ? r.rootBone.name : "-")} layer {r.gameObject.layer} shadows {r.shadowCastingMode} on {r.enabled && r.gameObject.activeInHierarchy}" : "-";
            foreach (var npc in Resources.FindObjectsOfTypeAll<NPC>().Where(n => n && n.gameObject.scene.IsValid()))
            {
                Note($"npc {npc.name} id {npc.ID} at {npc.transform.position} active {npc.gameObject.activeInHierarchy} comps [{string.Join(",", npc.GetComponents<Component>().Select(c => c.GetType().Name))}]");
                foreach (var r in npc.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Note("    " + Smr(r));
                Note($"    children [{string.Join(",", npc.transform.Cast<Transform>().Select(t => t.name))}]");
            }
            var skin = me.GetComponentsInChildren<Component>(true).FirstOrDefault(c => c && c.GetType().Name == "PlayerSkin");
            Note($"player skin component: {(skin ? skin.GetType().FullName + " on " + PathOf(skin.transform) : "none")}");
            var parts = new List<SkinnedMeshRenderer>();
            if (skin)
                foreach (var f in skin.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Where(f => f.FieldType == typeof(SkinnedMeshRenderer)))
                {
                    var r = f.GetValue(skin) as SkinnedMeshRenderer;
                    Note($"    {f.Name}: {Smr(r)}");
                    if (r && !f.Name.StartsWith("_local")) parts.Add(r);
                }
            var sm = HarmonyLib.AccessTools.Field(typeof(SkinManager), "_instance").GetValue(null) as SkinManager;
            if (sm)
                foreach (var f in typeof(SkinManager).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Where(f => f.FieldType == typeof(Clothes)))
                {
                    var c = f.GetValue(sm) as Clothes;
                    Note($"clothes {f.Name}: hat {(c && c.HatMesh ? c.HatMesh.name : "-")}, outfit {(c && c.OutfitMesh ? c.OutfitMesh.name : "-")}, accessory {(c && c.AccessoryMesh ? c.AccessoryMesh.name : "-")}");
                }
            // The player prefab (other players' bodies come from it): a fallback when no NPC is loaded.
            foreach (var ps in Resources.FindObjectsOfTypeAll<Component>().Where(c => c && c.GetType().Name == "PlayerSkin" && !c.gameObject.scene.IsValid()))
            {
                Note($"prefab skin on {PathOf(ps.transform)}");
                foreach (var f in ps.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Where(f => f.FieldType == typeof(SkinnedMeshRenderer)))
                    Note($"    {f.Name}: {Smr(f.GetValue(ps) as SkinnedMeshRenderer)}");
            }
            var src = Resources.FindObjectsOfTypeAll<NPC>().FirstOrDefault(n => n && n.gameObject.scene.IsValid());
            if (!src) yield break;
            var skinField = HarmonyLib.AccessTools.Field(typeof(NPC), "_skinColor");
            Vector3 skinColor = (Vector3)skinField.GetValue(src);
            Note($"cloning {src.name}, skin {skinColor}");
            var holder = new GameObject("NpcProbeHolder");
            holder.SetActive(false);
            var copy = Instantiate(src.gameObject, holder.transform);
            foreach (Transform ch in copy.transform.Cast<Transform>().ToList())
                if (ch.name != "Character" && !ch.name.StartsWith("Hand")) DestroyImmediate(ch.gameObject);
            foreach (var c in copy.GetComponentsInChildren<Component>(true))
                if (c && !(c is Transform || c is Renderer || c is MeshFilter)) DestroyImmediate(c);
            Note($"copy keeps [{string.Join(",", copy.GetComponentsInChildren<Component>(true).Select(c => c.GetType().Name).Distinct())}], tree [{string.Join(",", copy.GetComponentsInChildren<Transform>(true).Select(t => t.name))}]");
            Vector3 spot = island.VaultPos + new Vector3(3.5f, 0f, -1.5f);
            if (Physics.Raycast(spot + Vector3.up * 10f, Vector3.down, out var gh, 30f, GameInfo.LevelLayer)) spot = gh.point;
            copy.transform.SetParent(null, false);
            copy.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, 180f, 0f));
            Destroy(holder);
            var b = new Bounds(spot, Vector3.zero);
            foreach (var r in copy.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            Note($"copy at {spot}, bounds {b.center} size {b.size}, source scale {src.transform.lossyScale}, source rot {src.transform.eulerAngles}");
            Vector3 eye = spot + new Vector3(0f, 0f, -2.4f);
            me.LocalTeleport(eye, 0f, true);
            yield return null;
            Aim(0f, 4f);
            yield return new WaitForSecondsRealtime(1.5f);
            FortressPlugin.Shot("npc_copy_plain");
            void Dress(string clothes, Vector3 skin)
            {
                var c = HarmonyLib.AccessTools.Field(typeof(SkinManager), clothes).GetValue(sm) as Clothes;
                foreach (var r in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (r.name == "Outfit") r.sharedMesh = c.OutfitMesh;
                    if (r.name == "Hat") r.sharedMesh = c.HatMesh;
                    if (r.name == "Accessory") r.sharedMesh = c.AccessoryMesh;
                    ShaderManager.SetPlayerColors(r, skin);
                }
            }
            var skins = new[] { skinColor, new Vector3(0.55f, 0.36f, 0.25f), new Vector3(0.95f, 0.78f, 0.62f) };
            int k = 0;
            foreach (var c in new[] { "_gunStoreClerc", "_kioskLady", "_military", "_scientist", "_grillmaster", "_swampMan", "_storeGrandma" })
            {
                Dress(c, skins[k++ % skins.Length]);
                yield return new WaitForSecondsRealtime(0.6f);
                FortressPlugin.Shot("npc_copy" + c);
            }
        }

        private IEnumerator NewFoesScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return Until(() => plugin.Running, 15f, "round start");
            yield return Until(() => plugin.InBuildTime, 5f, "build time");
            plugin.DebugExtendBuild(300f);
            Siege.Instance.ServerAddCoins(Siege.Money(500));
            int C(string k) => Counters.TryGetValue(k, out int v) ? v : 0;
            Vector3 Gap(int i, float r) { float a = FortressIsland.GapAngles[i] * Mathf.Deg2Rad; var p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r + Vector3.up * 3f; return p; }

            // Gallery: the new kinds, frozen in a row.
            Vector3 row = center + new Vector3(0f, FortressIsland.CoreLocal.y + 0.3f, 4.5f);
            var kinds = new[] { EnemyKind.Thief, EnemyKind.Shield, EnemyKind.Digger, EnemyKind.Healer, EnemyKind.Splitter, EnemyKind.Mini };
            var shown = new List<Creature>();
            for (int i = 0; i < kinds.Length; i++) shown.Add(plugin.DebugShowcase(kinds[i], false, row + new Vector3((i - 2.5f) * 1.6f, 0f, 0f), 180f));
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Look(row + new Vector3(0f, 0.3f, 3.8f), 180f, "foes_gallery", 0.6f);
            foreach (var c in shown) if (c) c.ServerChangeHp(1000000);
            me.LocalTeleport(center + new Vector3(0f, 2f, 0f), 0f, true);

            // Dye check: a plain runner next to a thief, a healer and a splitter, up close.
            Vector3 row2 = center + new Vector3(0f, FortressIsland.CoreLocal.y + 0.3f, -9f);
            var dyed = new List<Creature>();
            var dk = new[] { EnemyKind.Runner, EnemyKind.Thief, EnemyKind.Healer, EnemyKind.Splitter };
            for (int i = 0; i < dk.Length; i++) dyed.Add(plugin.DebugShowcase(dk[i], false, row2 + new Vector3((i - 1.5f) * 1.3f, 0f, 0f), 0f));
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Look(row2 + new Vector3(0f, 0.4f, 3f), 180f, "foes_dyes", 0.6f);
            foreach (var c in dyed) if (c) c.ServerChangeHp(1000000);

            // Thief, no corpses around: it goes for the crate's coins, then runs for the sea.
            int coins0 = Siege.Instance.ServerCoins;
            plugin.ServerSpawnMinion(EnemyKind.Thief, island.CounterPos + new Vector3(4f, 2f, 3f));
            yield return Until(() => C("thief_stole") > 0, 15f, "thief steals");
            Note($"thief stole: coins {coins0} -> {Siege.Instance.ServerCoins}");
            yield return Until(() => C("thief_escaped") > 0, 25f, "thief escapes");
            Note($"thief escaped {C("thief_escaped")}");

            // Healer next to a hurt runner.
            var runner = plugin.ServerSpawnMinion(EnemyKind.Runner, Gap(1, 12f));
            plugin.ServerSpawnMinion(EnemyKind.Healer, Gap(1, 12f) + Vector3.right * 1.5f);
            yield return new WaitForSecondsRealtime(0.5f);
            if (runner) runner.ServerChangeHp(runner._hp.Value * 7 / 10);
            int hurt = runner ? runner._hp.Value : -1;
            yield return Until(() => C("healed") > 0, 8f, "healer heals");
            Note($"healer: runner hp {hurt} -> {(runner ? runner._hp.Value : -1)}, heals {C("healed")}");

            // Splitter dies into three minis.
            var split = plugin.ServerSpawnMinion(EnemyKind.Splitter, Gap(2, 14f));
            yield return new WaitForSecondsRealtime(1f);
            if (split) split.ServerChangeHp(1000000);
            yield return new WaitForSecondsRealtime(1f);
            Note($"splitter: minis spawned {C("spawned_Mini")}");

            // Digger meets a wall line and tunnels under it.
            float g0 = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g0), 0f, Mathf.Sin(g0)), side = Vector3.Cross(Vector3.up, gd);
            for (int k = -2; k <= 2; k++) WallAt(center + gd * 15f + side * (k * BuildSystem.Cell) + Vector3.up * 1.2f, gd);
            var digger = plugin.ServerSpawnMinion(EnemyKind.Digger, Gap(0, 21f));
            yield return Until(() => C("digger_burrowed") > 0, 20f, "digger burrows");
            yield return new WaitForSecondsRealtime(3f);
            Note($"digger: burrowed {C("digger_burrowed")}, now {(digger ? Vector3.Distance(digger.transform.position, center).ToString("F1") : "dead")} m from the lighthouse (walls at 15 m)");

            // Shield bearer walking at a gun that faces it.
            TurretAt(PieceType.Gun, center + gd * 10f + Vector3.up * 1.2f);
            plugin.ServerSpawnMinion(EnemyKind.Shield, Gap(0, 22f) + side * 2f);
            yield return Until(() => C("shield_blocks") > 5, 20f, "shield blocks");
            Note($"shield: blocked {C("shield_blocks")} hits");

            plugin.DebugKillAll();
            yield return new WaitForSecondsRealtime(1f);
        }

        // Building takes time (faster with a teammate), abandoned sites refund, the right-click menu, hold-H removal.
        private IEnumerator ConstructScenario()
        {
            var island = FortressIsland.Instance;
            var plugin = FortressPlugin.Instance;
            var con = Construction.Instance;
            var bs = BuildSystem.Instance;
            yield return Until(() => island.Exists, 30f, "fortress island");
            PlayerManager.ToggleGodMode();
            IgnorePlayers = true;
            var me = Player.LocalPlayer;
            Vector3 center = island.Center;
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return Until(() => plugin.Running, 15f, "round start");
            Siege.Instance.ServerAddCoins(Siege.Money(2000));
            float c = BuildSystem.Cell;
            Vector3 Cell(Vector3 w) { var p = new Vector3((Mathf.Floor(w.x / c) + 0.5f) * c, w.y + 5f, (Mathf.Floor(w.z / c) + 0.5f) * c); if (Physics.Raycast(p, Vector3.down, out var rh, 12f, GameInfo.LevelLayer)) p.y = rh.point.y; return p; }
            float g0 = FortressIsland.GapAngles[0] * Mathf.Deg2Rad;
            Vector3 gd = new Vector3(Mathf.Cos(g0), 0f, Mathf.Sin(g0)), side = Vector3.Cross(Vector3.up, gd);
            bool Built(Vector3 pos) => bs.ServerPieces.Any(p => (p.Pos - pos).sqrMagnitude < 0.01f);

            // Hold on one spot (optionally with a stand-in teammate) and time it.
            IEnumerator Hold(PieceType t, Vector3 pos, bool teammate, string shotAt, System.Action<float> done)
            {
                float t0 = Time.time, next = 0f;
                bool shot = false;
                while (!Built(pos) && Time.time - t0 < 4f)
                {
                    if (Time.time >= next)
                    {
                        next = Time.time + Construction.SendEvery;
                        con.Work(t, pos, 0f);
                        if (teammate) con.ServerWork(777777, new BuildWork { Type = t, Pos = pos, RotY = 0f });
                    }
                    if (shotAt != null && !shot && Time.time - t0 > 0.45f) { shot = true; FortressPlugin.Shot(shotAt); }
                    yield return null;
                }
                done(Built(pos) ? Time.time - t0 : -1f);
            }

            Vector3 eye = Cell(center + gd * 5f + Vector3.up * 2f) + Vector3.up * 0.5f;
            me.LocalTeleport(eye, YawTo(eye, center + gd * 11f), true);
            yield return new WaitForSecondsRealtime(1f);
            Vector3 wallPos = new Vector3((Mathf.Floor((center + gd * 14f).x / c) + 0.5f) * c, Mathf.Floor((center.y + 1.45f + 0.25f) / c) * c + c * 0.5f, (Mathf.Floor((center + gd * 14f).z / c)) * c);
            int coins = Siege.Instance.ServerCoins;
            float tw = 0f; yield return Hold(PieceType.Wall, wallPos, false, null, x => tw = x);
            Note($"wall alone: {tw:F2}s (expect ~{Construction.BuildTime(PieceType.Wall):F1}), cost {coins - Siege.Instance.ServerCoins}");
            Vector3 gunPos = Cell(center + gd * 11f);
            float tg = 0f; yield return Hold(PieceType.Gun, gunPos, false, "construct_mid", x => tg = x);
            Note($"gun alone: {tg:F2}s (expect ~{Construction.BuildTime(PieceType.Gun):F1})");
            Vector3 canPos = Cell(center + gd * 11f + side * c);
            float tc = 0f; yield return Hold(PieceType.Cannon, canPos, true, null, x => tc = x);
            Note($"cannon with a teammate: {tc:F2}s (expect ~{Construction.BuildTime(PieceType.Cannon) / (1f + Construction.TeamBonus):F2})");

            // Abandon: work a moment, let go, the site disappears and the coins come back.
            Vector3 morPos = Cell(center + gd * 11f - side * c);
            coins = Siege.Instance.ServerCoins;
            for (float t = 0f; t < 0.3f; t += 0.15f) { con.Work(PieceType.Mortar, morPos, 0f); yield return new WaitForSeconds(0.15f); }
            int during = Siege.Instance.ServerCoins;
            yield return new WaitForSeconds(Construction.Abandon + 0.6f);
            Note($"abandoned mortar: coins {coins} -> {during} while building -> {Siege.Instance.ServerCoins} after (refund), sites left {con.ServerSiteCount}, built {Built(morPos)}");

            // The right-click menu on the gun: upgrade, then the branch choice.
            int gunId = bs.ServerPieces.First(p => (p.Pos - gunPos).sqrMagnitude < 0.01f).Id;
            bs.DebugSetBuilding(true, PieceType.Gun);
            PieceMenu.Instance.Open(gunId);
            yield return new WaitForSecondsRealtime(0.8f);
            FortressPlugin.Shot("menu_upgrade");
            yield return new WaitForSecondsRealtime(0.4f);
            PieceMenu.Instance.DebugUpgrade(1);
            yield return new WaitForSecondsRealtime(0.8f);
            FortressPlugin.Shot("menu_branch");
            yield return new WaitForSecondsRealtime(0.4f);
            PieceMenu.Instance.DebugUpgrade(2);
            yield return new WaitForSecondsRealtime(0.8f);
            var gun = bs.ServerPieces.First(p => p.Id == gunId);
            Note($"menu: gun now Lv{gun.Level} branch {gun.Branch} (expect Lv3 branch 2), menu open {PieceMenu.IsOpen}, inputs blocked {me.BlockInputs}");
            PieceMenu.Instance.Close();

            // Hold H on the wall: it sinks, then goes.
            int wallId = bs.ServerPieces.First(p => (p.Pos - wallPos).sqrMagnitude < 0.01f).Id;
            float h0 = Time.time;
            bool shotRemove = false;
            while (Time.time - h0 < BuildSystem.RemoveTime + 0.3f && bs.ServerPieces.Any(p => p.Id == wallId))
            {
                bs.DebugRemoveHold(wallId);
                if (!shotRemove && Time.time - h0 > 0.35f) { shotRemove = true; FortressPlugin.Shot("remove_hold"); }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.5f);
            Note($"hold-H remove: wall gone {!bs.ServerPieces.Any(p => p.Id == wallId)} after {Time.time - h0:F2}s");
            bs.DebugSetBuilding(false, PieceType.Wall);
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
            Vector3 door = island.VaultDoor;
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
            Siege.Instance.ServerAddCoins(Siege.Money(3000));
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
            yield return Look(eye, YawTo(eye, turretAt), "ui_walkup", 1.2f); // the same turret, walked up to without the hammer

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
            while (loot.Vault.Fortress.Count > 0) loot.ServerRequest(me, new VaultRequest { Action = VaultAction.UnequipFortress, Gear = loot.Vault.Fortress[0].Item1, Rarity = loot.Vault.Fortress[0].Item2 });
            while (loot.Vault.Slots(Loot.KeyOf(me)).Count > 0) { var m0 = loot.Vault.Slots(Loot.KeyOf(me))[0]; loot.ServerRequest(me, new VaultRequest { Action = VaultAction.UnequipPersonal, Gear = m0.Item1, Rarity = m0.Item2 }); }
            Equip(Gear.Incendiary, Gear.FrostCore, Gear.TeslaCoil, Gear.Scope, Gear.BigNet, Gear.FishGloves);
            Note($"loadout 2: fortress [{string.Join(",", loot.Vault.Fortress)}], mine [{string.Join(",", loot.Vault.Slots(Loot.KeyOf(me)))}]");
            PlayerManager.ToggleGodMode();
            me.LocalTeleport(center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
            yield return Until(() => plugin.Running, 15f, "round 2 start");
            Note($"net capacity {FortressNet.Instance.ServerCapOf(me.ObjectId)} (expect 20)");
            Siege.Instance.ServerAddCoins(Siege.Money(600));
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
            Siege.Instance.ServerAddCoins(Siege.Money(400));
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
                    if (plugin.Wave == 5 && !jumped) { jumped = true; Siege.Instance.ServerAddCoins(Siege.Money(1500)); SpendLikeAPlayer(center); plugin.DebugJumpToWave(10); }
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
            loot.FlushVault(); // written with the game's save, not at every change
            string file = loot.ServerVaultFile;
            Note($"vault file {file}: {(file != null && File.Exists(file) ? File.ReadAllLines(file).Length + " lines" : "missing")}");
            { var m1 = loot.Vault.Slots(Loot.KeyOf(me))[0]; loot.ServerRequest(me, new VaultRequest { Action = VaultAction.UnequipPersonal, Gear = m1.Item1, Rarity = m1.Item2 }); }
            Note($"unequip: mine [{string.Join(",", loot.Vault.Slots(Loot.KeyOf(me)))}], armor back in vault {loot.Vault.Count(Gear.SharkArmor, Rarity.Rare)}");
            // The vault building and its menu.
            Vector3 door = island.VaultDoor;
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
                    var up = BuildSystem.Instance.ServerPieces.Where(p => p.Type != PieceType.Floor) // no cap: the cheapest upgrade going
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
