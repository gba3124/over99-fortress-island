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
    public struct RoundResult : IBroadcast { public int Waves; public int Kills; public int Built; public int Best; public bool NewRecord; public bool CoreLost; }

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

        private GameObject _root, _ring;
        private Material _ringMat;
        private Vector3 _anchor = new Vector3(float.NaN, 0f, 0f);
        private NetworkManager _registeredOn;
        private bool _wasUnlocked;

        // host: ready countdown
        private float _readySince = -1f;

        // client: results panel
        private TextMeshProUGUI _results;
        private float _resultsUntil;
        private TextMeshProUGUI _banner;
        private float _bannerUntil;

        static FortressIsland()
        {
            GenericWriter<RoundResult>.SetWrite((w, v) => { w.WriteInt32(v.Waves); w.WriteInt32(v.Kills); w.WriteInt32(v.Built); w.WriteInt32(v.Best); w.WriteBoolean(v.NewRecord); w.WriteBoolean(v.CoreLost); });
            GenericReader<RoundResult>.SetRead(r => new RoundResult { Waves = r.ReadInt32(), Kills = r.ReadInt32(), Built = r.ReadInt32(), Best = r.ReadInt32(), NewRecord = r.ReadBoolean(), CoreLost = r.ReadBoolean() });
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
            if (!(Siege.Instance && Siege.Instance.ViewActive) && (float.IsNaN(_anchor.x) || (Island.IslandPos - _anchor).sqrMagnitude > 1f))
            {
                _anchor = Island.IslandPos;
                Center = PickCenter(_anchor);
                Despawn();
            }
            if (!_root) Spawn();

            UpdateRing();
            if (InstanceFinder.IsServerStarted) ServerReadyCheck();
        }
        private bool _wasUnlockedKnown;

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
        public bool WalkerSpawn(out Vector3 pos)
        {
            for (int i = 0; i < 10; i++)
            {
                float a = (GapAngles[Random.Range(0, GapAngles.Length)] + Random.Range(-6f, 6f)) * Mathf.Deg2Rad;
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
            float r = Random.Range(29f, 33f);
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

        public void Banner(string text, float seconds)
        {
            if (!_banner) _banner = Hud.CreateText("FortressBanner", new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), 46f, TextAlignmentOptions.Center);
            if (!_banner) return;
            _banner.text = text;
            _bannerUntil = Time.time + seconds;
        }

        private void ShowResults(RoundResult r)
        {
            if (!_results) _results = Hud.CreateText("FortressResults", new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), 34f, TextAlignmentOptions.Center);
            if (!_results) return;
            _results.rectTransform.sizeDelta = new Vector2(1000f, 400f);
            _results.text = L10n.T(r.CoreLost ? "resultCore" : "resultWipe") + "\n\n"
                          + L10n.T("resultStats", r.Waves, r.Kills, r.Built) + "\n"
                          + (r.NewRecord ? L10n.T("resultRecord") : L10n.T("resultBest", r.Best)) + "\n\n"
                          + L10n.T("resultAgain");
            _resultsUntil = Time.time + 12f;
        }

        private void LateUpdate()
        {
            if (_results) _results.gameObject.SetActive(Time.time < _resultsUntil);
            if (_banner) _banner.gameObject.SetActive(Time.time < _bannerUntil);
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

    // Dying mid-round respawns you at the fortress dock instead of back home.
    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.OnPlayerResurrected))]
    internal static class RespawnAtDock
    {
        private static void Postfix(Player player)
        {
            var island = FortressIsland.Instance;
            if (player != Player.LocalPlayer || island == null || !island.Exists || !(Siege.Instance && Siege.Instance.ViewActive)) return;
            var d = island.Center - island.DockPos;
            player.LocalTeleport(island.DockPos, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, true);
        }
    }
}
