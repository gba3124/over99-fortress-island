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

    // Client -> host: E at the lighthouse's door (pull the gate to start a round, or let it go again).
    public struct GateRequest : IBroadcast { public byte Pull; }

    // Host -> everyone when a round ends.
    public struct RoundResult : IBroadcast { public int Waves; public int Kills; public int Built; public int Best; public bool NewRecord; public bool CoreLost; public bool Victory; public string Table; public int Souls, SoulsTotal, SoulsFrom; public string SoulLines; }

    // The fortress island: a Blender-made island every client builds locally off the coast of the current island,
    // shown on the radar once unlocked. E at the lighthouse's door pulls its gate: 5 seconds later the round starts.
    public class FortressIsland : MonoBehaviour
    {
        public static FortressIsland Instance;
        public static ConfigEntry<IslandUnlock> Unlock;
        public static bool ForceForTest;

        // Layout in island-local coordinates (must match the Blender model, see Models/fortress_models.blend).
        public static readonly Vector3 CoreLocal = new Vector3(0f, 1.45f, 0f);
        public static readonly Vector3 ReadyLocal = new Vector3(0f, 1.46f, -4.5f);
        public static readonly Vector3 DockLocal = new Vector3(0f, 0.6f, -36f);
        // Where you come back after going down: the grass at the land end of the dock, between the stall and the vault (the
        // dock itself is a narrow pier over the water; people landed in the sea).
        public static readonly Vector3 RespawnLocal = new Vector3(0.5f, 0f, -18.5f);
        // The village either side of the dock road (tools/make_island.py flattens the ground for it; the stations are
        // tools/make_station_models.py): each faces the road with its keeper behind the counter (Keepers.cs).
        public static readonly Vector3 VaultLocal = new Vector3(-8f, 0f, -17f);    // the quartermaster's shed: gear
        public const float VaultYaw = 90f;                                         // door to the road
        public static readonly Vector3 MarketLocal = new Vector3(5.5f, 0f, -16.8f); // the fish stall: sell the catch, bigger nets
        public const float MarketYaw = -90f;
        public static readonly Vector3 SmithyLocal = new Vector3(6.2f, 0f, -10.8f); // the smithy: the weapon rack, forging 3 into 1
        public const float SmithyYaw = -90f;
        public static readonly Vector3 KeeperLocal = new Vector3(-2.6f, 0f, -3.4f); // the lighthouse keeper, beside the start circle
        public static readonly Vector3 BoardLocal = new Vector3(3.6f, 0f, -6.2f);   // the record board, the other side of it
        public const float BoardYaw = 35f;                                          // read from the road up from the dock
        public const float BuildRadius = 27f;
        public const float IslandRadius = 36f;
        public static readonly float[] GapAngles = { 90f, 210f, 330f };
        private const float ReadySeconds = 5f;
        private const float Offshore = 230f;

        public bool Exists => _root;
        public Vector3 Center { get; private set; }
        public Vector3 CorePos => Center + CoreLocal;
        public Vector3 DockPos => Center + DockLocal;
        // The player's position is the middle of the body (it stands about 0.9 m above the ground): put it a little higher
        // and let them drop. Only 0.3 m up, the body started half in the ground and the physics often pushed it down through.
        public Vector3 RespawnPos => (Loot.Ground(Center + RespawnLocal + Vector3.up * 6f, 12f, out var g) ? g : Center + RespawnLocal + Vector3.up * 1.2f) + Vector3.up * StandHeight;
        public const float StandHeight = 1.1f;
        // In the water beside the dock, where a respawn (or the F4 test key) parks the boat.
        public static readonly Vector3 BerthLocal = new Vector3(4f, 0f, -42f);
        public Vector3 BerthPos { get { var p = Center + BerthLocal; p.y = WaterManager.GetWaterHeight(p); return p; } }
        // The island model's parts that are only drawn, not solid: the buoys bobbing off the pier (white and red; the red also
        // floats the net rack's net). Solid, one sat 2 m from the berth and the boat was pinned on it (Owen's playtest: 「白色
        // 浮標卡到他」, and they were stuck on the island).
        public static readonly string[] NotSolid = { "White", "IslBuoyRed" };
        public void ServerMoveBoatToBerth() => BoatManager.Instance.TryMoveBoat(BerthPos, Quaternion.identity);
        // On the ground in front of the fish stall's counter: where nets are sold, the helpers sell, a thief steals coins.
        public Vector3 CounterPos { get; private set; }
        public Vector3 VaultPos { get; private set; }
        public Vector3 VaultDoor => VaultPos + Quaternion.Euler(0f, VaultYaw, 0f) * new Vector3(0f, 0f, 2.6f);
        public Vector3 MarketPos { get; private set; }
        public Vector3 SmithyPos { get; private set; }
        // Where a weapon taken off the rack lands: on the ground in front of the smithy, at the customer's feet.
        public Vector3 SmithyCounter => SmithyPos + Quaternion.Euler(0f, SmithyYaw, 0f) * new Vector3(0f, 1.2f, 2.1f);
        public Transform Rack { get; private set; }
        public IslandDecor Decor { get; private set; }
        // Fish wading through a tide pool on the south-west beach are slowed (Siege).
        public bool InPool(Vector3 p) => Decor && Decor.InPool(p);
        public readonly List<Keeper> Keepers = new List<Keeper>();
        // Where the lighthouse stands (on the ground under CorePos, like the round's core); it goes up and down with the
        // island when the reveal raises it out of the sea.
        public Vector3 LighthousePos => (_root ? _root.transform.position : Center) + _lighthouseLocal;
        private Vector3 _lighthouseLocal;
        // Metres below its place: the reveal (Reveal.cs) starts it under the sea and brings it up.
        public float Sink
        {
            get => _sink;
            set { _sink = value; if (_root) _root.transform.position = Center + Vector3.down * value; }
        }
        private float _sink;

        private GameObject _root;
        private Vector3 _anchor = new Vector3(float.NaN, 0f, 0f);
        private NetworkManager _registeredOn;
        private bool _wasUnlocked;


        // client: results panel
        private GameObject _results;
        private float _resultsUntil;
        public bool ResultsShowing => _results && _results.activeSelf;

        static FortressIsland()
        {
            GenericWriter<RoundResult>.SetWrite((w, v) => { w.WriteInt32(v.Waves); w.WriteInt32(v.Kills); w.WriteInt32(v.Built); w.WriteInt32(v.Best); w.WriteBoolean(v.NewRecord); w.WriteBoolean(v.CoreLost); w.WriteBoolean(v.Victory); w.WriteString(v.Table ?? ""); w.WriteInt32(v.Souls); w.WriteInt32(v.SoulsTotal); w.WriteInt32(v.SoulsFrom); w.WriteString(v.SoulLines ?? ""); });
            GenericWriter<GateRequest>.SetWrite((w, v) => w.WriteByte(v.Pull));
            GenericReader<GateRequest>.SetRead(r => new GateRequest { Pull = r.ReadByte() });
            GenericReader<RoundResult>.SetRead(r => new RoundResult { Waves = r.ReadInt32(), Kills = r.ReadInt32(), Built = r.ReadInt32(), Best = r.ReadInt32(), NewRecord = r.ReadBoolean(), CoreLost = r.ReadBoolean(), Victory = r.ReadBoolean(), Table = r.ReadString(), Souls = r.ReadInt32(), SoulsTotal = r.ReadInt32(), SoulsFrom = r.ReadInt32(), SoulLines = r.ReadString() });
        }

        private void Awake() => Instance = this;

        // The host's own rule; clients follow the host (sent in SiegeState), so a teammate's setting can't hide or add the island.
        public static bool HostUnlocked => ForceForTest || Unlock.Value == IslandUnlock.Always
                                           || (NPCManager.Instance && NPCManager.Instance.FinalBossKilled);
        public static bool Unlocked => InstanceFinder.IsServerStarted ? HostUnlocked : Siege.Instance && Siege.Instance.View.IslandOpen;

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
                nm.ClientManager.RegisterBroadcast<RoundResult>((r, ch) => _pendingResult = r); // shown once the victory camera is done
                nm.ServerManager.RegisterBroadcast(Net.Safe<GateRequest>((conn, req, ch) =>
                {
                    var who = PlayerManager.Players.FirstOrDefault(p => p && p.Owner == conn);
                    if (req.Pull == 2) Siege.Instance.ServerRepairCore(who); // in a round: E at the door mends the lighthouse
                    else ServerToggleStart(who);
                }));
                nm.ClientManager.OnClientConnectionState += a => { if (a.ConnectionState == FishNet.Transporting.LocalConnectionState.Stopped) Despawn(); };
            }

            if (_pendingResult.HasValue && !Reveal.Playing) { ShowResults(_pendingResult.Value); _pendingResult = null; }

            // Don't gate on IslandManager.IsLoading/CurIsland: the game streams mini islands in and out while you move,
            // and the fortress must not vanish (and drop everyone in the sea) during those loads.
            bool inWorld = InstanceFinder.IsClientStarted && Player.LocalPlayer && (Island.CurIsland || _root);
            bool known = inWorld && (InstanceFinder.IsServerStarted || (Siege.Instance && Siege.Instance.HasView)); // a client waits for the host's word
            bool unlocked = known && Unlocked;
            // The final boss just went down: the island rises out of the sea for everyone (Reveal ends on the banner).
            if (unlocked && !_wasUnlocked && _wasUnlockedKnown && Unlock.Value == IslandUnlock.AfterFinalBoss) Reveal.Instance.Queue();
            _wasUnlocked = unlocked; _wasUnlockedKnown = known;

            if (!unlocked) { Despawn(); return; }
            // Sit offshore of whichever island is loaded; only move between rounds. A client that joins while a round is
            // running still has to place it once: skipping the move just because a round was on left a client that had
            // never built the island with Center at the world origin, a kilometre from the host's island (and F4, the
            // spawn point and the lighthouse all went there).
            Vector3 anchor = MainIslandPos();
            bool anchorOk = !float.IsNaN(anchor.x) && !float.IsNaN(anchor.z);
            bool liveRound = Siege.Instance && Siege.Instance.ViewActive;
            bool moved = float.IsNaN(_anchor.x) || (anchor - _anchor).sqrMagnitude > 1f;
            if (anchorOk && (!_root || (!liveRound && moved)))
            {
                _anchor = anchor;
                Center = PickCenter(_anchor);
                Despawn();
            }
            if (!_root)
            {
                if (!anchorOk) return; // the main island isn't loaded yet: never build at the origin
                Spawn();
            }

            // Joining a round that is already on spawns you at the main island's dock, 230 m away; bring you to the
            // fortress with the others (only right after this client built the island = a fresh join, and only once).
            BringJoinersIn();

            RescueFromBelow();
            // First time you come near the island: a welcome banner.
            if (!_welcomed && Player.LocalPlayer && Player.LocalPlayer.Transform && OnIsland(Player.LocalPlayer.Transform.position, IslandRadius + 12f))
            { _welcomed = true; Banner(L10n.T("islandWelcome"), 4f); }
            if (InstanceFinder.IsServerStarted) ServerReadyCheck();
        }

        private float _placedAt;
        private bool _broughtIn;

        private void BringJoinersIn()
        {
            var me = Player.LocalPlayer;
            if (_broughtIn || !_root || !me || !me.Transform) return;
            bool roundOn = InstanceFinder.IsServerStarted ? (Siege.Instance && Siege.Instance.ServerActive)
                                                          : (Siege.Instance && Siege.Instance.ViewActive);
            if (!roundOn) { _broughtIn = true; return; }                      // no round: the game's own spawn is fine
            if (Time.time - _placedAt > 60f) { _broughtIn = true; return; }  // not a join: you walked out there yourself
            if ((me.Transform.position - Center).sqrMagnitude < 150f * 150f) { _broughtIn = true; return; } // already here
            _broughtIn = true;
            me.LocalTeleport(RespawnPos, 0f, true);
            Banner(L10n.T("islandWelcome"), 3f);
            BuildSystem.Log.LogInfo($"Joined a round in progress: brought the player to the island at {RespawnPos}.");
        }
        private bool _wasUnlockedKnown;
        private bool _welcomed;
        private RoundResult? _pendingResult;

        // Whoever ends up under the ground (loading into a save before the island was built here, coming back to life as
        // it was being rebuilt...) is put back on top: nothing to stand on below them, the island's ground above them.
        private float _nextRescue;
        private void RescueFromBelow()
        {
            var me = Player.LocalPlayer;
            if (!_root || !me || !me.Transform || !me.Transform.gameObject.activeInHierarchy || Time.time < _nextRescue) return;
            _nextRescue = Time.time + 0.5f;
            Vector3 p = me.Transform.position;
            if (!OnIsland(p, BuildRadius) || Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, 8f, GameInfo.LevelLayer)) return;
            // Back onto the surface they fell through: the lowest one above them (not a roof over it).
            var above = Physics.RaycastAll(new Vector3(p.x, Center.y + 40f, p.z), Vector3.down, 80f, GameInfo.LevelLayer).Where(h => h.point.y > p.y + 0.3f).ToList();
            if (above.Count == 0) return;
            Vector3 floor = above.OrderBy(h => h.point.y).First().point;
            me.LocalTeleport(floor + Vector3.up * StandHeight, me.Transform.eulerAngles.y, true);
            BuildSystem.Log.LogInfo($"Under the island at {p - Center:F1}: back up to {floor - Center:F1}.");
            AutoTest.Count("rescued_from_below");
        }

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
            _placedAt = Time.time; _broughtIn = false; // a fresh island here: if a round is on, this is a join (see BringJoinersIn)
            _sink = 0f;
            DontDestroyOnLoad(_root);
            var model = Models.Attach("FortressIsland", _root.transform, new Vector3(60f, 2f, 60f), new Vector3(0f, 0.2f, 0f));
            var mf = model.GetComponent<MeshFilter>();
            var mc = model.AddComponent<MeshCollider>();
            if (mf && mf.sharedMesh) mc.sharedMesh = Models.ColliderOf("FortressIsland", NotSolid) ?? mf.sharedMesh;
            Decor = _root.AddComponent<IslandDecor>();
            Decor.Load(System.IO.Path.Combine(Models.Dir, "FortressIsland.decor.txt"));

            // The fish stall: sell your net and buy a bigger one at its counter, and whatever is thrown onto the counter is sold,
            // like at the game's shopkeepers. (A fish crate stood in front of it doing the same till Owen: "感覺小幫手直接賣店家…
            // 比做一個功能重複的漁網箱子好 可以直接拔掉漁網箱子".)
            var market = Station("MarketStall", MarketLocal, MarketYaw, out var mp);
            MarketPos = mp;
            Lamp(market.transform, new Vector3(0f, 2.15f, 0.6f), new Color(1f, 0.9f, 0.75f), 4.5f, 0.9f, false); // under the awning, over the counter
            CounterPos = Ground(market.transform.TransformPoint(new Vector3(0f, 0f, 1.6f)));
            var counter = new GameObject("StallCounter"); // over the counter top (tools/make_station_models.py: ~1 m up, 3 m wide)
            counter.layer = BuildSystem.PieceLayer;
            counter.transform.SetParent(market.transform, false);
            counter.transform.localPosition = new Vector3(0f, 0.2f, 0.6f);
            var trigger = counter.AddComponent<BoxCollider>(); trigger.isTrigger = true; trigger.size = new Vector3(3f, 1.1f, 1f); trigger.center = new Vector3(0f, 1.3f, 0f);
            counter.AddComponent<SellCounter>();

            // The quartermaster's shed: the team's gear between rounds (see VaultUI).
            VaultPos = Ground(Center + VaultLocal);
            var vault = new GameObject("Vault");
            vault.layer = BuildSystem.PieceLayer;
            vault.transform.SetParent(_root.transform, false);
            vault.transform.position = VaultPos;
            vault.transform.rotation = Quaternion.Euler(0f, VaultYaw, 0f);
            // A storehouse you walk into (Owen: "the vault should be one you can walk into"): its own mesh is its collider,
            // doorway and all; the quartermaster stands behind his counter inside.
            var vmodel = Models.Attach("Vault", vault.transform, new Vector3(4f, 2.6f, 3.4f), new Vector3(0f, 1.3f, 0f));
            if (vmodel.GetComponent<MeshFilter>() is MeshFilter vmf && vmf.sharedMesh) vmodel.AddComponent<MeshCollider>().sharedMesh = vmf.sharedMesh;
            else vmodel.AddComponent<BoxCollider>();
            // Its doorway swallows whatever is thrown at it (VaultChute), from just outside.
            var chute = new GameObject("VaultChute");
            chute.layer = BuildSystem.PieceLayer;
            chute.transform.SetParent(vault.transform, false);
            chute.transform.localPosition = new Vector3(0f, 1.0f, 2.15f);
            var mouthBox = chute.AddComponent<BoxCollider>(); mouthBox.isTrigger = true; mouthBox.size = new Vector3(1.6f, 2.0f, 0.9f);
            chute.AddComponent<VaultChute>();

            // The smithy: the weapon rack on its back wall, the anvil where weapons are dropped to go on the rack.
            var smithy = Station("Smithy", SmithyLocal, SmithyYaw, out var sp);
            SmithyPos = sp;
            Lamp(smithy.transform, new Vector3(0f, 2.3f, 0.2f), new Color(1f, 0.9f, 0.75f), 4.5f, 1.4f, false);   // under the roof
            Lamp(smithy.transform, new Vector3(1.25f, 1.25f, -0.9f), new Color(1f, 0.5f, 0.15f), 3.5f, 2.2f, true); // the forge
            var anvil = new GameObject("SmithyCounter");
            anvil.layer = BuildSystem.PieceLayer;
            anvil.transform.SetParent(smithy.transform, false);
            var drop = anvil.AddComponent<BoxCollider>(); drop.isTrigger = true; drop.size = new Vector3(2.2f, 1.4f, 1.5f); drop.center = new Vector3(0f, 1.0f, 0.55f);
            anvil.AddComponent<SmithyCounter>();
            Rack = new GameObject("WeaponRack").transform;
            Rack.SetParent(smithy.transform, false);
            Rack.gameObject.AddComponent<WeaponRack>();

            // Their keepers, and the lighthouse keeper by the start circle.
            Keepers.Clear();
            void Keeper(Transform at, Vector3 local, float yaw, string clothes, string role, System.Action talk)
            {
                var k = HtfFortress.Keepers.Make(at, at.TransformPoint(local), yaw, clothes, role, talk);
                if (k) Keepers.Add(k);
            }
            Keeper(market.transform, new Vector3(0f, 0.1f, -0.4f), MarketYaw, "_kioskLady", "Market", () => StallMenu.Instance.Open());
            Keeper(smithy.transform, new Vector3(-0.4f, 0.12f, -0.55f), SmithyYaw, "_gunStoreClerc", "Smith", () => VaultUI.Instance.Open(VaultUI.Desk.Smith));
            Keeper(vault.transform, new Vector3(1.3f, 0.26f, -0.05f), VaultYaw, "_military", "Vault", () => VaultUI.Instance.Open(VaultUI.Desk.Quartermaster)); // behind his counter inside
            var plaza = Ground(Center + KeeperLocal);
            Keeper(_root.transform, _root.transform.InverseTransformPoint(plaza), 200f, "_lighthouseKeeper", "Lighthouse", () => { if (KeeperMenu.Instance) KeeperMenu.Instance.Toggle(); });

            var board = new GameObject("RecordBoard");
            board.layer = BuildSystem.PieceLayer;
            board.transform.SetParent(_root.transform, false);
            board.transform.SetPositionAndRotation(Ground(Center + BoardLocal), Quaternion.Euler(0f, BoardYaw, 0f));
            board.AddComponent<RecordBoard>();

            // From inside the lighthouse's own collider the ray doesn't see it (Siege.ServerStart snaps the round's core alike).
            _lighthouseLocal = (Physics.Raycast(CorePos + Vector3.up * 2f, Vector3.down, out var lh, 6f, GameInfo.LevelLayer) ? lh.point : CorePos) - Center;
            BuildSystem.Log.LogInfo($"Fortress island at {Center} (off island at {_anchor}).");
        }

        // A warm light under a station's roof (the keepers' faces are lit by it, not the sky), or the forge's flicker.
        private static void Lamp(Transform station, Vector3 local, Color c, float range, float intensity, bool flicker)
        {
            var go = new GameObject("Lamp");
            go.transform.SetParent(station, false);
            go.transform.localPosition = local;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            if (flicker) go.AddComponent<Flicker>();
        }

        private Vector3 Ground(Vector3 p) =>
            Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out var hit, 40f, GameInfo.LevelLayer) ? hit.point : p;

        // A station model standing on the ground, solid (its own mesh as the collider).
        private GameObject Station(string model, Vector3 local, float yaw, out Vector3 pos)
        {
            pos = Ground(Center + local);
            var go = new GameObject(model);
            go.layer = BuildSystem.PieceLayer;
            go.transform.SetParent(_root.transform, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var m = Models.Attach(model, go.transform, new Vector3(3f, 2.5f, 2.5f), new Vector3(0f, 1.25f, 0f));
            var mf = m.GetComponent<MeshFilter>();
            if (mf && mf.sharedMesh) m.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            else m.AddComponent<BoxCollider>();
            return go;
        }

        private void Despawn()
        {
            if (_root) Destroy(_root);
            _root = null;
        }

        // ---------- host: someone pulls the lighthouse's gate (E at its door) -> 5 s -> the round starts ----------

        // E again during the countdown lets it go. Only between rounds, and only from the lighthouse.
        private float _startAt = -1f;
        public bool Starting => _startAt > 0f;

        public void ServerToggleStart(Player who)
        {
            var plugin = FortressPlugin.Instance;
            if (!who || !who.Transform || plugin == null || plugin.Running || !_root) return;
            if ((who.Transform.position - LighthousePos).sqrMagnitude > 7f * 7f) return;
            if (_startAt > 0f) { _startAt = -1f; Fx.Announce("gateCancelled"); AutoTest.Count("gate_cancelled"); return; }
            _startAt = Time.time + ReadySeconds;
            Fx.Announce("gatePulled", Mathf.RoundToInt(ReadySeconds));
            AutoTest.Count("gate_pulled");
        }

        private void ServerReadyCheck()
        {
            var plugin = FortressPlugin.Instance;
            if (plugin == null || plugin.Running || !_root || _startAt < 0f) { _startAt = -1f; Siege.Instance.ServerReady(0, 0, 0); return; }
            float left = _startAt - Time.time;
            Siege.Instance.ServerReady(0, 0, Mathf.Max(1, Mathf.CeilToInt(left)));
            if (left <= 0f) { _startAt = -1f; plugin.StartRound(CorePos); }
        }

        // ---------- host: where enemies come from ----------

        // A beach in one of the three rock gaps, a little inside the waterline.
        public bool WalkerSpawn(out Vector3 pos) => WalkerSpawn(-1, out pos);

        // gap = 0..2 for a specific beach, -1 for any.
        public bool WalkerSpawn(int gap, out Vector3 pos)
        {
            for (int i = 0; i < 10; i++)
            {
                // A wide spread: at ±6° on a 25 m beach, a burst of walkers was a handful of metres of sand and the fish
                // spawned inside one another (they shoved apart like they had exploded: Owen). ±20° and a deeper band.
                float a = (GapAngles[gap >= 0 ? gap % GapAngles.Length : Random.Range(0, GapAngles.Length)] + Random.Range(-20f, 20f)) * Mathf.Deg2Rad;
                float r = Random.Range(22f, 29f);
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
            float h = 132f + 128f + 154f + 46f + 52f + 40f * rows.Count + (mvps.Count > 0 ? 64f : 0f) + 66f;
            var panel = Hud.Box(Hud.Root, C, new Vector2(0f, 20f), new Vector2(1000f, h), Hud.PanelSolid, 26f);
            _results = panel.gameObject;
            var p = panel.transform;
            float y = 0f;

            var head = Hud.Box(p, T, new Vector2(0f, -10f), new Vector2(980f, 104f), r.Victory ? new Color(0.5f, 0.36f, 0.05f, 0.95f) : new Color(0.42f, 0.09f, 0.07f, 0.9f), 20f);
            Hud.Label(head.transform, L10n.T(r.Victory ? "resultVictoryTitle" : r.CoreLost ? "resultCoreTitle" : "resultWipeTitle"), 44f, C, new Vector2(0f, 10f), new Vector2(940f, 60f), TextAlignmentOptions.Center, bold: true);
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

            // The souls settlement: what the run paid and exactly how (Owen: "結算的表單應該要有那種計算爽感").
            {
                var box = Hud.Box(p, T, new Vector2(0f, y), new Vector2(940f, 144f), new Color(0.16f, 0.13f, 0.05f, 0.95f), 16f, pivot: T);
                var bt = box.transform;
                Hud.Label(bt, L10n.T("soulSettleTitle"), 24f, new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(420f, 30f), TextAlignmentOptions.Left, Hud.Gold, bold: true);
                string left = r.Souls > 0
                    ? L10n.T("soulSettleFrom", r.SoulsFrom, r.Waves) + "\n" + r.SoulLines + "\n" + L10n.T("soulSettleHint")
                    : L10n.T("soulSettleNone");
                Hud.Label(bt, left, 16f, new Vector2(0f, 1f), new Vector2(18f, -46f), new Vector2(580f, 92f), TextAlignmentOptions.TopLeft, Hud.Ink);
                Hud.Label(bt, $"<size=165%><color={Hud.Hex(Hud.Gold)}><b>+{r.Souls}</b></color></size>", 34f, new Vector2(1f, 1f), new Vector2(-24f, -34f), new Vector2(320f, 66f), TextAlignmentOptions.Center);
                Hud.Label(bt, L10n.T("soulSettleHave", r.SoulsTotal), 18f, new Vector2(1f, 0f), new Vector2(-24f, 22f), new Vector2(320f, 26f), TextAlignmentOptions.Center, Hud.Muted);
                y -= 154f;
            }

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
            Hud.Label(p, Hud.Keys(L10n.T("resultAgain") + "　·　" + L10n.T("resultClose")), 22f, new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(940f, 34f), TextAlignmentOptions.Center, Hud.Muted);

            _results.SetActive(false);
            _resultsUntil = Time.time + ResultsSeconds;
            Hud.HideBanner();
            Fx.Sound(r.NewRecord ? "CasinoWinEpic" : "Death", Player.LocalPlayer ? Player.LocalPlayer.Transform.position : Center, 1f, AudioDistance.Long);
        }

        private const float ResultsSeconds = 15f;
        public void CloseResults() => _resultsUntil = 0f;

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
            if (dead && Time.time < _resultsUntil) _resultsUntil = Time.time + ResultsSeconds;
            if (Siege.Instance && Siege.Instance.ViewActive) _resultsUntil = 0f; // the next round is on
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
        private static void PlayerPos(Vector3 __0, ref Vector3 __result) { if (Near(__0)) __result = FortressIsland.Instance.RespawnPos; }

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

    // The game pulls a boat nobody is near back toward its island (Island.IslandPos: 230 m off, and the mini islands it streams
    // in move it about). At the fortress that dragged the parked boat along the shore, back and forth, into the pier's buoys
    // (Owen's playtest: 「船自己滑來滑去」). Near the fortress the boat stays where it was left.
    [HarmonyPatch(typeof(Boat), "ApplyReturnForce")]
    internal static class BoatStaysAtFortress
    {
        public const float Reach = FortressIsland.IslandRadius + 30f;
        private static bool Prefix(Boat __instance) =>
            !(FortressIsland.Instance && FortressIsland.Instance.Exists && __instance.HiddenPhysicsRig && FortressIsland.Instance.OnIsland(__instance.HiddenPhysicsRig.position, Reach));
    }
}
