using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using FishNet;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Serializing;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HtfFortress
{
    public enum IslandUnlock { AfterFinalBoss, Always }

    // Host -> everyone when a round ends.
    public struct RoundResult : IBroadcast { public int Waves; public int Kills; public int Built; public int Best; public bool NewRecord; public bool CoreLost; public string Table; }

    // The fortress island: a Blender-made island every client builds locally off the coast of the current island,
    // shown on the radar once unlocked. Stand in the glowing circle by the lighthouse to start a round.
    public class FortressIsland : MonoBehaviour
    {
        public static FortressIsland Instance;
        public static ConfigEntry<IslandUnlock> Unlock;
        public static bool ForceForTest;

        // Layout in island-local coordinates (must match the Blender model, see Models/fortress_models.blend).
        public static readonly Vector3 CoreLocal = new Vector3(0f, 1.45f, 0f);
        public static readonly Vector3 ReadyLocal = new Vector3(0f, 1.46f, -4.5f);
        public static readonly Vector3 DockLocal = new Vector3(0f, 0.6f, -36f);
        public static readonly Vector3 CrateLocal = new Vector3(4.5f, 0f, -20f); // on the beach beside the dock
        public static readonly Vector3 VaultLocal = new Vector3(-5.5f, 0f, -20.5f); // the other side of the dock
        public const float ReadyRadius = 1.8f;
        public const float BuildRadius = 27f;
        public const float IslandRadius = 36f;
        public static readonly float[] GapAngles = { 90f, 210f, 330f };
        private const float ReadySeconds = 5f;
        private const float Offshore = 230f;

        public bool Exists => _root;
        public Vector3 Center { get; private set; }
        public Vector3 CorePos => Center + CoreLocal;
        public Vector3 DockPos => Center + DockLocal;
        // In the water beside the dock, where a respawn (or the F4 test key) parks the boat.
        public Vector3 BerthPos { get { var p = Center + new Vector3(4f, 0f, -42f); p.y = WaterManager.GetWaterHeight(p); return p; } }
        public void ServerMoveBoatToBerth() => BoatManager.Instance.TryMoveBoat(BerthPos, Quaternion.identity);
        public Vector3 CratePos { get; private set; }
        public Vector3 VaultPos { get; private set; }

        private GameObject _root, _ring;
        private Material _ringMat;
        private Vector3 _anchor = new Vector3(float.NaN, 0f, 0f);
        private NetworkManager _registeredOn;
        private bool _wasUnlocked;

        // host: ready countdown
        private float _readySince = -1f;
        private float _nextReadyLog;

        // client: results panel
        private GameObject _results;
        private float _resultsUntil;

        static FortressIsland()
        {
            GenericWriter<RoundResult>.SetWrite((w, v) => { w.WriteInt32(v.Waves); w.WriteInt32(v.Kills); w.WriteInt32(v.Built); w.WriteInt32(v.Best); w.WriteBoolean(v.NewRecord); w.WriteBoolean(v.CoreLost); w.WriteString(v.Table ?? ""); });
            GenericReader<RoundResult>.SetRead(r => new RoundResult { Waves = r.ReadInt32(), Kills = r.ReadInt32(), Built = r.ReadInt32(), Best = r.ReadInt32(), NewRecord = r.ReadBoolean(), CoreLost = r.ReadBoolean(), Table = r.ReadString() });
        }

        private void Awake() => Instance = this;

        public static bool Unlocked => ForceForTest || Unlock.Value == IslandUnlock.Always
                                       || (NPCManager.Instance && NPCManager.Instance.FinalBossKilled);

        public bool OnIsland(Vector3 p, float radius = IslandRadius)
        {
            if (!Exists) return false;
            Vector3 d = p - Center; d.y = 0f;
            return d.sqrMagnitude <= radius * radius;
        }

        // ---------- placement & lifecycle ----------

        private void Update()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm && nm != _registeredOn)
            {
                _registeredOn = nm;
                nm.ClientManager.RegisterBroadcast<RoundResult>((r, ch) => ShowResults(r));
                nm.ClientManager.OnClientConnectionState += a => { if (a.ConnectionState == FishNet.Transporting.LocalConnectionState.Stopped) Despawn(); };
            }

            // Don't gate on IslandManager.IsLoading/CurIsland: the game streams mini islands in and out while you move,
            // and the fortress must not vanish (and drop everyone in the sea) during those loads.
            bool inWorld = InstanceFinder.IsClientStarted && Player.LocalPlayer && (Island.CurIsland || _root);
            bool unlocked = inWorld && Unlocked;
            if (unlocked && !_wasUnlocked && _wasUnlockedKnown && Unlock.Value == IslandUnlock.AfterFinalBoss) Banner(L10n.T("islandUnlocked"), 8f);
            _wasUnlocked = unlocked; _wasUnlockedKnown = inWorld;

            if (!unlocked) { Despawn(); return; }
            // Sit offshore of whichever island is loaded; only move between rounds.
            Vector3 anchor = MainIslandPos();
            if (!(Siege.Instance && Siege.Instance.ViewActive) && (float.IsNaN(_anchor.x) || (anchor - _anchor).sqrMagnitude > 1f))
            {
                _anchor = anchor;
                Center = PickCenter(_anchor);
                Despawn();
            }
            if (!_root) Spawn();

            UpdateRing();
            // First time you come near the island: a welcome banner.
            if (!_welcomed && Player.LocalPlayer && Player.LocalPlayer.Transform && OnIsland(Player.LocalPlayer.Transform.position, IslandRadius + 12f))
            { _welcomed = true; Banner(L10n.T("islandWelcome"), 4f); }
            if (InstanceFinder.IsServerStarted) ServerReadyCheck();
        }
        private bool _wasUnlockedKnown;
        private bool _welcomed;

        // The main island you're on (fixed per island). Island.IslandPos also follows the random mini islands
        // the game streams in ahead of the boat, which would make the fortress jump around.
        private static Vector3 MainIslandPos()
        {
            try { return IslandManager.GetIslandInfo(OnlineIslandManager.CurIsland).IslandPosition; }
            catch { return Island.IslandPos; }
        }

        // Offshore, pointing away from the other islands so it never overlaps one.
        private static Vector3 PickCenter(Vector3 cur)
        {
            Vector3 sum = Vector3.zero; int n = 0;
            for (int i = 0; i < 16; i++)
            {
                IslandInfo info = null;
                try { info = IslandManager.GetIslandInfo(i); } catch { }
                if (info == null) break;
                try { sum += info.IslandPosition; n++; } catch { }
            }
            Vector3 dir = n > 0 ? cur - sum / n : Vector3.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1f) dir = Vector3.forward;
            Vector3 c = cur + dir.normalized * Offshore;
            c.y = 0f;
            return c;
        }

        private void Spawn()
        {
            _root = new GameObject("FortressIsland");
            _root.layer = BuildSystem.PieceLayer;
            _root.transform.position = Center;
            DontDestroyOnLoad(_root);
            var model = Models.Attach("FortressIsland", _root.transform, new Vector3(60f, 2f, 60f), new Vector3(0f, 0.2f, 0f));
            var mf = model.GetComponent<MeshFilter>();
            var mc = model.AddComponent<MeshCollider>();
            if (mf && mf.sharedMesh) mc.sharedMesh = mf.sharedMesh;

            // Fish crate: sell your net here (and buy a bigger one).
            CratePos = Center + CrateLocal;
            if (Physics.Raycast(CratePos + Vector3.up * 20f, Vector3.down, out var gh, 40f, GameInfo.LevelLayer)) CratePos = gh.point;
            var crate = new GameObject("FishCrate");
            crate.layer = BuildSystem.PieceLayer;
            crate.transform.SetParent(_root.transform, false);
            crate.transform.position = CratePos;
            crate.transform.rotation = Quaternion.Euler(0f, 20f, 0f);
            var box = crate.AddComponent<BoxCollider>(); box.size = new Vector3(1.6f, 1.1f, 1.2f); box.center = new Vector3(0f, 0.55f, 0f);
            Models.Attach("FishCrate", crate.transform, new Vector3(1.6f, 1.1f, 1.2f), new Vector3(0f, 0.55f, 0f));

            // Vault: the team's gear between rounds (see VaultUI).
            VaultPos = Center + VaultLocal;
            if (Physics.Raycast(VaultPos + Vector3.up * 20f, Vector3.down, out var vh, 40f, GameInfo.LevelLayer)) VaultPos = vh.point;
            var vault = new GameObject("Vault");
            vault.layer = BuildSystem.PieceLayer;
            vault.transform.SetParent(_root.transform, false);
            vault.transform.position = VaultPos;
            vault.transform.rotation = Quaternion.Euler(0f, -20f, 0f);
            var vbox = vault.AddComponent<BoxCollider>(); vbox.size = new Vector3(2.4f, 2.2f, 1.8f); vbox.center = new Vector3(0f, 1.1f, 0f);
            Models.Attach("Vault", vault.transform, new Vector3(2.4f, 2.2f, 1.8f), new Vector3(0f, 1.1f, 0f));

            _ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(_ring.GetComponent<Collider>());
            _ring.name = "ReadyCircle";
            _ring.transform.SetParent(_root.transform, false);
            _ring.transform.localPosition = ReadyLocal;
            _ring.transform.localScale = new Vector3(ReadyRadius * 2f, 0.02f, ReadyRadius * 2f);
            _ringMat = Models.Make(new Color(0.3f, 0.9f, 1f), new Color(0.3f, 0.9f, 1f));
            _ring.GetComponent<Renderer>().sharedMaterial = _ringMat;
            BuildSystem.Log.LogInfo($"Fortress island at {Center} (off island at {_anchor}).");
        }

        private void Despawn()
        {
            if (_root) Destroy(_root);
            _root = null;
        }

        private void UpdateRing()
        {
            if (!_ring) return;
            bool show = !(Siege.Instance && Siege.Instance.ViewActive);
            _ring.SetActive(show);
            if (!show) return;
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 3f);
            var s = Siege.Instance ? Siege.Instance.View : default;
            Color c = s.ReadyCountdown > 0 ? Color.Lerp(new Color(1f, 0.8f, 0.2f), Color.white, pulse) : new Color(0.3f, 0.9f, 1f) * (0.7f + 0.3f * pulse);
            Models.Paint(_ring, c);
        }

        // ---------- host: everybody on the island stands in the circle for 5 s -> round starts ----------

        private void ServerReadyCheck()
        {
            var plugin = FortressPlugin.Instance;
            if (plugin == null || plugin.Running || !_root) { _readySince = -1f; Siege.Instance.ServerReady(0, 0, 0); return; }
            var onIsland = PlayerManager.AlivePlayers.Where(p => p && p.Transform && OnIsland(p.Transform.position)).ToList();
            int inCircle = onIsland.Count(p => InCircle(p.Transform.position));
            if (AutoTest.Running && Time.time >= _nextReadyLog)
            {
                _nextReadyLog = Time.time + 1f;
                BuildSystem.Log.LogInfo($"ready check: alive {PlayerManager.AlivePlayers.Count}, players {PlayerManager.Players.Count}, on island {onIsland.Count}, in circle {inCircle}, since {_readySince:0.0}, now {Time.time:0.0}, pos {(Player.LocalPlayer ? Player.LocalPlayer.Transform.position.ToString() : "-")}");
            }
            if (onIsland.Count > 0 && inCircle == onIsland.Count)
            {
                if (_readySince < 0f) _readySince = Time.time;
                float left = ReadySeconds - (Time.time - _readySince);
                Siege.Instance.ServerReady(onIsland.Count, inCircle, Mathf.Max(1, Mathf.CeilToInt(left)));
                if (left <= 0f) { _readySince = -1f; plugin.StartRound(CorePos); }
            }
            else
            {
                _readySince = -1f;
                Siege.Instance.ServerReady(onIsland.Count, inCircle, 0);
            }
        }

        public bool InCircle(Vector3 p)
        {
            Vector3 d = p - (Center + ReadyLocal);
            return Mathf.Abs(d.y) < 3f && new Vector2(d.x, d.z).sqrMagnitude <= (ReadyRadius + 0.3f) * (ReadyRadius + 0.3f);
        }

        // ---------- host: where enemies come from ----------

        // A beach in one of the three rock gaps, a little inside the waterline.
        public bool WalkerSpawn(out Vector3 pos) => WalkerSpawn(-1, out pos);

        // gap = 0..2 for a specific beach, -1 for any.
        public bool WalkerSpawn(int gap, out Vector3 pos)
        {
            for (int i = 0; i < 10; i++)
            {
                float a = (GapAngles[gap >= 0 ? gap % GapAngles.Length : Random.Range(0, GapAngles.Length)] + Random.Range(-6f, 6f)) * Mathf.Deg2Rad;
                float r = Random.Range(23.5f, 26f);
                Vector3 p = Center + new Vector3(Mathf.Cos(a) * r, 10f, Mathf.Sin(a) * r);
                if (Physics.Raycast(p, Vector3.down, out var rh, 20f, GameInfo.LevelLayer) && rh.point.y > WaterManager.GetWaterHeight(rh.point) + 0.2f)
                {
                    pos = rh.point + Vector3.up * 0.8f;
                    return true;
                }
            }
            pos = default;
            return false;
        }

        // In the water just off one of the landing beaches, where a spitter can see the walls.
        public Vector3 SpitterSpot()
        {
            float a = (GapAngles[Random.Range(0, GapAngles.Length)] + Random.Range(-25f, 25f)) * Mathf.Deg2Rad;
            float r = Random.Range(27.5f, 29.5f); // just past the waterline, in reach of the beach turrets
            Vector3 p = Center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            p.y = WaterManager.GetWaterHeight(p) - 0.35f;
            return p;
        }

        public Vector3 SeaSpawn()
        {
            float a = Random.Range(0f, Mathf.PI * 2f), r = Random.Range(38f, 45f);
            Vector3 p = Center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            p.y = WaterManager.GetWaterHeight(p) - 0.6f;
            return p;
        }

        // ---------- client: banner + results ----------

        public void Banner(string text, float seconds) => Hud.Banner(text, seconds);

        // End of round: title, three stat tiles, record, every player's row, MVP titles, how to go again.
        private void ShowResults(RoundResult r)
        {
            if (_results) Destroy(_results);
            if (!Hud.Font) return;
            Vector2 C = new Vector2(0.5f, 0.5f), T = new Vector2(0.5f, 1f);
            var rows = Stats.Parse(r.Table).OrderByDescending(x => x.Kills + x.TurretKills).ToList();
            var mvps = Mvps(rows);
            float h = 132f + 128f + 46f + 52f + 40f * rows.Count + (mvps.Count > 0 ? 64f : 0f) + 66f;
            var panel = Hud.Box(Hud.Root, C, new Vector2(0f, 20f), new Vector2(1000f, h), Hud.PanelSolid, 26f);
            _results = panel.gameObject;
            var p = panel.transform;
            float y = 0f;

            var head = Hud.Box(p, T, new Vector2(0f, -10f), new Vector2(980f, 104f), new Color(0.42f, 0.09f, 0.07f, 0.9f), 20f);
            Hud.Label(head.transform, L10n.T(r.CoreLost ? "resultCoreTitle" : "resultWipeTitle"), 44f, C, new Vector2(0f, 10f), new Vector2(940f, 60f), TextAlignmentOptions.Center, bold: true);
            Hud.Label(head.transform, r.NewRecord ? $"<color={Hud.Hex(Hud.Gold)}>★ {L10n.T("resultRecordShort")} ★</color>" : L10n.T("resultBest", r.Best),
                      22f, C, new Vector2(0f, -28f), new Vector2(940f, 30f), TextAlignmentOptions.Center, Hud.Muted, bold: r.NewRecord);
            y -= 132f;

            var tiles = new[] { (r.Waves.ToString(), L10n.T("tileWaves")), (r.Kills.ToString(), L10n.T("tileKills")), (r.Built.ToString(), L10n.T("tileBuilt")) };
            for (int i = 0; i < 3; i++)
            {
                var tile = Hud.Box(p, T, new Vector2((i - 1) * 316f, y - 6f), new Vector2(300f, 108f), Hud.Raised, 16f, pivot: T);
                Hud.Label(tile.transform, tiles[i].Item1, 52f, T, new Vector2(0f, -6f), new Vector2(290f, 64f), TextAlignmentOptions.Center, i == 0 ? Hud.Accent : Hud.Ink, bold: true);
                Hud.Label(tile.transform, tiles[i].Item2, 21f, new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(290f, 30f), TextAlignmentOptions.Bottom, Hud.Muted);
            }
            y -= 128f;

            // Player table: fixed columns (percent of the row), the header is the six cells of "tableHeader".
            int[] cols = { 3, 36, 49, 66, 80, 91 };
            string Line(IEnumerable<string> cells) => string.Concat(cells.Select((c, i) => $"<pos={cols[i]}%>{c}"));
            var hdr = Hud.Box(p, T, new Vector2(0f, y), new Vector2(940f, 40f), Hud.Raised, 10f, pivot: T);
            Hud.Label(hdr.transform, Line(L10n.T("tableHeader").Split(new[] { "   " }, System.StringSplitOptions.None)), 19f, C, Vector2.zero, new Vector2(920f, 40f), TextAlignmentOptions.Left, Hud.Muted);
            y -= 46f;
            foreach (var row in rows)
            {
                string name = row.Name.Length > 16 ? row.Name.Substring(0, 15) + "…" : row.Name;
                Hud.Label(p, Line(new[] { $"<b>{name}</b>", row.Kills.ToString(), row.TurretKills.ToString(), row.Fished.ToString(), row.Built.ToString(), row.Downs.ToString() }),
                          22f, T, new Vector2(0f, y), new Vector2(920f, 38f), TextAlignmentOptions.Left, pivot: T);
                y -= 40f;
            }
            y -= 12f;

            if (mvps.Count > 0)
            {
                float w = 300f, x0 = -(mvps.Count - 1) * (w + 12f) / 2f;
                for (int i = 0; i < mvps.Count; i++)
                {
                    var pill = Hud.Box(p, T, new Vector2(x0 + i * (w + 12f), y), new Vector2(w, 50f), new Color(0.35f, 0.26f, 0.06f, 0.75f), 25f, pivot: T);
                    Hud.Label(pill.transform, $"<color={Hud.Hex(Hud.Gold)}><b>{mvps[i].title}</b></color>  {mvps[i].who}", 20f, C, Vector2.zero, new Vector2(w - 16f, 50f), TextAlignmentOptions.Center);
                }
                y -= 64f;
            }
            Hud.Label(p, Hud.Keys(L10n.T("resultAgain")), 22f, new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(940f, 34f), TextAlignmentOptions.Center, Hud.Muted);

            _results.SetActive(false);
            _resultsUntil = Time.time + 18f;
            Hud.HideBanner();
            Fx.Sound(r.NewRecord ? "CasinoWinEpic" : "Death", Player.LocalPlayer ? Player.LocalPlayer.Transform.position : Center, 1f, AudioDistance.Long);
        }

        // MVP titles, each only when someone earned it (and only with company, except in tests).
        private static List<(string title, string who)> Mvps(List<Stats.Row> rows)
        {
            var list = new List<(string, string)>();
            if (rows.Count == 0 || (rows.Count < 2 && !AutoTest.Running)) return list;
            void Add(string key, Stats.Row best, int value) { if (best != null && value > 0) list.Add((L10n.T(key), $"{best.Name} ({value})")); }
            var k = rows.OrderByDescending(x => x.Kills).First(); Add("mvpKills", k, k.Kills);
            var t = rows.OrderByDescending(x => x.TurretKills).ThenByDescending(x => x.Built).First(); Add("mvpBuilder", t, t.TurretKills);
            var f = rows.OrderByDescending(x => x.Fished).First(); Add("mvpFisher", f, f.Fished);
            var s = rows.OrderBy(x => x.Downs).ThenByDescending(x => x.Kills).First(); if (s.Downs == 0) list.Add((L10n.T("mvpSurvivor"), s.Name));
            return list;
        }

        private void LateUpdate()
        {
            // Hold the panel while the game's death screen is up; it shows once you are back on your feet.
            bool dead = Player.LocalPlayer && !PlayerManager.AlivePlayers.Contains(Player.LocalPlayer);
            if (dead && Time.time < _resultsUntil) _resultsUntil = Time.time + 18f;
            if (_results) _results.SetActive(!dead && Time.time < _resultsUntil);
        }

        // ---------- radar ----------

        private static MapDot _dot;
        private static readonly System.Reflection.MethodInfo UpdateDot = AccessTools.Method(typeof(RadarUI), "UpdateDot");

        [HarmonyPatch(typeof(RadarUI), "UpdateIslandDots")]
        internal static class RadarDotPatch
        {
            private static void Postfix(RadarUI __instance, MapDot[] ____islandDots)
            {
                if (Instance == null || !Instance.Exists || ____islandDots == null || ____islandDots.Length == 0) return;
                if (_dot == null || !(Image)AccessTools.Field(typeof(MapDot), "_dot").GetValue(_dot))
                {
                    var srcImg = (Image)AccessTools.Field(typeof(MapDot), "_dot").GetValue(____islandDots[0]);
                    if (!srcImg) return;
                    var img = Instantiate(srcImg.gameObject, srcImg.transform.parent).GetComponent<Image>();
                    img.color = new Color(1f, 0.55f, 0.1f, 0f);
                    _dot = new MapDot();
                    AccessTools.Field(typeof(MapDot), "_dot").SetValue(_dot, img);
                }
                UpdateDot.Invoke(__instance, new object[] { _dot, Instance.Center });
            }
        }
    }

    // A wipe on the fortress respawns everyone (and moves the boat) to the island's spawn point. Send them to the
    // fortress dock instead, so "try again" is a short walk instead of a sail back. Revives stay where the body was.
    [HarmonyPatch]
    internal static class RespawnAtFortress
    {
        private static bool Near(Vector3 pos) => FortressIsland.Instance != null && FortressIsland.Instance.Exists && FortressIsland.Instance.OnIsland(pos, 70f);

        [HarmonyPostfix, HarmonyPatch(typeof(SpawnManager), nameof(SpawnManager.GetPlayerSpawnPos))]
        private static void PlayerPos(Vector3 __0, ref Vector3 __result) { if (Near(__0)) __result = FortressIsland.Instance.DockPos; }

        [HarmonyPostfix, HarmonyPatch(typeof(SpawnManager), nameof(SpawnManager.GetPlayerSpawnRot))]
        private static void PlayerRot(Vector3 __0, ref float __result) { if (Near(__0)) __result = 0f; } // facing +Z, up the dock

        // Host: the wipe branch of the respawn moves the boat home; move it to the fortress berth afterwards.
        [HarmonyPrefix, HarmonyPatch(typeof(Server), "RpcLogic___RespawnPlayer___2210451296")]
        private static void WipeStart(Vector3 __1, out bool __state) => __state = PlayerManager.AlivePlayers.Count == 0 && Near(__1);

        [HarmonyPostfix, HarmonyPatch(typeof(Server), "RpcLogic___RespawnPlayer___2210451296")]
        private static void WipeEnd(bool __state) { if (__state) FortressIsland.Instance.StartCoroutine(MoveBoatToBerth()); }

        // Wait a moment so this lands after the game's own move home.
        private static System.Collections.IEnumerator MoveBoatToBerth()
        {
            yield return new WaitForSeconds(0.5f);
            FortressIsland.Instance.ServerMoveBoatToBerth();
        }
    }
}
