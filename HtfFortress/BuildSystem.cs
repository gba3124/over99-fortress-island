using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using TMPro;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Serializing;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HtfFortress
{
    // Wire messages. The mod is built without FishNet codegen, so serializers are registered by hand below.
    public struct PiecePlaceRequest : IBroadcast { public PieceType Type; public Vector3 Pos; public float RotY; }
    public struct PieceRemoveRequest : IBroadcast { public int Id; }
    public struct PieceUpgradeRequest : IBroadcast { public int Id; }
    public struct PieceState : IBroadcast { public int Id; public PieceType Type; public Vector3 Pos; public float RotY; public bool Removed; public int Hp; public int MaxHp; public int Level; }

    // Fortnite-style building on a fixed world grid.
    // B toggles build mode, Q cycles pieces, hold LMB to keep placing, R flips, U upgrades and X removes the piece you look at.
    // The host owns the piece list and the coins; clients send requests and mirror what the host broadcasts.
    public class BuildSystem : MonoBehaviour
    {
        public const float Cell = 3f;
        private const float Reach = 12f;

        public static ManualLogSource Log;
        public static ConfigEntry<Key> BuildKey;
        public static BuildSystem Instance;

        // host side
        private readonly Dictionary<int, PieceState> _serverPieces = new Dictionary<int, PieceState>();
        private readonly Dictionary<int, int> _spent = new Dictionary<int, int>();
        private int _nextId = 1;
        public IEnumerable<PieceState> ServerPieces => _serverPieces.Values;

        // every client (host included)
        private readonly Dictionary<int, GameObject> _pieces = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, PieceState> _states = new Dictionary<int, PieceState>();
        private readonly Dictionary<GameObject, int> _pieceIds = new Dictionary<GameObject, int>();
        public GameObject PieceObject(int id) => _pieces.TryGetValue(id, out var go) ? go : null;

        private NetworkManager _registeredOn;
        private bool _building;
        private PieceType _type = PieceType.Wall;
        private bool _flip;
        private GameObject _ghost;
        private PieceType _ghostType = (PieceType)255;
        private bool _ghostValid;
        private Vector3 _lastSentPos = Vector3.positiveInfinity;
        private TextMeshProUGUI _hud;
        private string _lookInfo = "";

        public static int PieceLayer
        {
            get
            {
                int mask = GameInfo.LevelLayer.value;
                for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
                return 0;
            }
        }

        static BuildSystem()
        {
            GenericWriter<PiecePlaceRequest>.SetWrite((w, v) => { w.WriteByte((byte)v.Type); w.WriteVector3(v.Pos); w.WriteSingle(v.RotY); });
            GenericReader<PiecePlaceRequest>.SetRead(r => new PiecePlaceRequest { Type = (PieceType)r.ReadByte(), Pos = r.ReadVector3(), RotY = r.ReadSingle() });
            GenericWriter<PieceRemoveRequest>.SetWrite((w, v) => w.WriteInt32(v.Id));
            GenericReader<PieceRemoveRequest>.SetRead(r => new PieceRemoveRequest { Id = r.ReadInt32() });
            GenericWriter<PieceUpgradeRequest>.SetWrite((w, v) => w.WriteInt32(v.Id));
            GenericReader<PieceUpgradeRequest>.SetRead(r => new PieceUpgradeRequest { Id = r.ReadInt32() });
            GenericWriter<PieceState>.SetWrite((w, v) =>
            {
                w.WriteInt32(v.Id); w.WriteByte((byte)v.Type); w.WriteVector3(v.Pos); w.WriteSingle(v.RotY); w.WriteBoolean(v.Removed);
                w.WriteInt32(v.Hp); w.WriteInt32(v.MaxHp); w.WriteInt32(v.Level);
            });
            GenericReader<PieceState>.SetRead(r => new PieceState
            {
                Id = r.ReadInt32(), Type = (PieceType)r.ReadByte(), Pos = r.ReadVector3(), RotY = r.ReadSingle(), Removed = r.ReadBoolean(),
                Hp = r.ReadInt32(), MaxHp = r.ReadInt32(), Level = r.ReadInt32()
            });
        }

        private void Awake() => Instance = this;

        private void Update()
        {
            EnsureRegistered();

            var kb = Keyboard.current;
            if (kb == null || ChatManager.IsTyping) return;
            if (kb[BuildKey.Value].wasPressedThisFrame) SetBuilding(!_building);
            if (!_building) return;
            if (!InstanceFinder.IsClientStarted || !GameInfo.CurCamera || !Player.LocalPlayer || Cursor.lockState != CursorLockMode.Locked)
            {
                if (_ghost) _ghost.SetActive(false);
                return;
            }

            if (kb.escapeKey.wasPressedThisFrame) { SetBuilding(false); return; }
            if (kb.qKey.wasPressedThisFrame) _type = (PieceType)(((int)_type + 1) % Catalog.TypeCount);
            if (kb.rKey.wasPressedThisFrame) _flip = !_flip;

            UpdateGhost();
            UpdateLookInfo(out int lookedId);

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed && _ghostValid && _ghost.transform.position != _lastSentPos)
            {
                _lastSentPos = _ghost.transform.position;
                InstanceFinder.ClientManager.Broadcast(new PiecePlaceRequest { Type = _type, Pos = _lastSentPos, RotY = _ghost.transform.eulerAngles.y });
            }
            if (mouse != null && mouse.leftButton.wasReleasedThisFrame) _lastSentPos = Vector3.positiveInfinity;
            if (lookedId != 0 && kb.xKey.wasPressedThisFrame) InstanceFinder.ClientManager.Broadcast(new PieceRemoveRequest { Id = lookedId });
            if (lookedId != 0 && kb.uKey.wasPressedThisFrame) InstanceFinder.ClientManager.Broadcast(new PieceUpgradeRequest { Id = lookedId });
        }

        private void SetBuilding(bool on)
        {
            _building = on;
            if (!on && _ghost) _ghost.SetActive(false);
        }

        // ---------- placement ----------

        // Snap the aim point to the grid the way Fortnite does: walls on cell edges, floors on cell tops,
        // ramps across a cell, turrets standing in the middle of a cell.
        private bool ComputePlacement(out Vector3 pos, out Quaternion rot)
        {
            Transform cam = GameInfo.CurCamera.transform;
            int mask = GameInfo.LevelLayer.value | (1 << PieceLayer);
            bool hit = Physics.Raycast(cam.position, cam.forward, out var rh, Reach, mask, QueryTriggerInteraction.Ignore);
            Vector3 aim = hit ? rh.point - cam.forward * 0.05f : cam.position + cam.forward * Reach;

            Vector3 fwd = cam.forward; fwd.y = 0f;
            bool alongX = Mathf.Abs(fwd.x) >= Mathf.Abs(fwd.z);
            float yaw = alongX ? (fwd.x >= 0 ? 90f : 270f) : (fwd.z >= 0 ? 0f : 180f);
            if (_flip) yaw += 180f;

            int cx = Mathf.FloorToInt(aim.x / Cell), cz = Mathf.FloorToInt(aim.z / Cell);
            int level = Mathf.FloorToInt((aim.y + 0.25f) / Cell);
            float baseY = level * Cell;
            Vector3 cellCenter = new Vector3((cx + 0.5f) * Cell, baseY, (cz + 0.5f) * Cell);

            switch (_type)
            {
                case PieceType.Wall:
                {
                    // Edge between the player's cell and the aimed cell; the far edge when aiming into your own cell.
                    Vector3 me = Player.LocalPlayer.Transform.position;
                    int px = Mathf.FloorToInt(me.x / Cell), pz = Mathf.FloorToInt(me.z / Cell);
                    if (alongX)
                    {
                        int side = cx != px ? (cx > px ? 0 : 1) : (fwd.x >= 0 ? 1 : 0);
                        pos = new Vector3((cx + side) * Cell, baseY + Cell * 0.5f, cellCenter.z);
                        rot = Quaternion.Euler(0f, 90f, 0f);
                    }
                    else
                    {
                        int side = cz != pz ? (cz > pz ? 0 : 1) : (fwd.z >= 0 ? 1 : 0);
                        pos = new Vector3(cellCenter.x, baseY + Cell * 0.5f, (cz + side) * Cell);
                        rot = Quaternion.identity;
                    }
                    break;
                }
                case PieceType.Floor:
                    pos = cellCenter;
                    rot = Quaternion.identity;
                    break;
                case PieceType.Ramp: // rises toward where you look
                    pos = cellCenter + Vector3.up * Cell * 0.5f;
                    rot = Quaternion.Euler(-45f, yaw, 0f);
                    break;
                default: // turret stands in the middle of the cell, on whatever surface is there (ground or a floor)
                    pos = new Vector3(cellCenter.x, baseY, cellCenter.z);
                    if (Physics.Raycast(pos + Vector3.up * (Cell + 1f), Vector3.down, out var ground, Cell * 2f, mask, QueryTriggerInteraction.Ignore))
                        pos.y = ground.point.y;
                    rot = Quaternion.Euler(0f, yaw, 0f);
                    break;
            }
            return hit;
        }

        private static bool Clashes(PieceType a, PieceType b) => a == b || (Catalog.IsTurret(a) && Catalog.IsTurret(b));

        private void UpdateGhost()
        {
            if (!_ghost || _ghostType != _type)
            {
                if (_ghost) Destroy(_ghost);
                _ghost = CreatePieceObject(_type, 0);
                Destroy(_ghost.GetComponent<Collider>());
                _ghost.name = "FortressGhost";
                _ghostType = _type;
                DontDestroyOnLoad(_ghost);
            }

            bool hit = ComputePlacement(out var pos, out var rot);
            bool aboveWater = pos.y + Cell > WaterManager.GetWaterHeight(pos);
            bool free = !_states.Values.Any(s => Clashes(s.Type, _type) && (s.Pos - pos).sqrMagnitude < 0.01f);
            bool affordable = !Siege.Instance.ViewActive || Siege.Instance.ViewCoins >= Catalog.BuildCost(_type);
            _ghostValid = hit && aboveWater && free && affordable;

            _ghost.SetActive(true);
            _ghost.transform.SetPositionAndRotation(pos, rot);
            Models.Paint(_ghost, _ghostValid ? new Color(0.3f, 0.6f, 1f) : new Color(0.9f, 0.2f, 0.2f));
        }

        // Root holds the box collider (cheap, matches the grid); the Blender model is a child. Turrets get a "Head" to aim.
        private static GameObject CreatePieceObject(PieceType t, int layer)
        {
            Vector3 size = Catalog.Size(t);
            var go = new GameObject("FortressPiece");
            go.layer = layer;
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
            if (Catalog.IsTurret(t))
            {
                col.center = new Vector3(0f, size.y * 0.5f, 0f);
                Models.Attach("TurretBase", go.transform, new Vector3(1.4f, 0.8f, 1.4f), new Vector3(0f, 0.4f, 0f));
                var head = new GameObject("Head");
                head.layer = layer;
                head.transform.SetParent(go.transform, false);
                head.transform.localPosition = new Vector3(0f, 0.85f, 0f);
                Models.Attach(t == PieceType.Cannon ? "CannonHead" : "GunHead", head.transform, new Vector3(0.6f, 0.5f, 1.4f));
            }
            else Models.Attach(t.ToString(), go.transform, size);
            return go;
        }

        // Shows what you're looking at and returns its id (0 = nothing).
        private void UpdateLookInfo(out int id)
        {
            id = 0;
            _lookInfo = "";
            Transform cam = GameInfo.CurCamera.transform;
            if (!Physics.Raycast(cam.position, cam.forward, out var rh, Reach, 1 << PieceLayer, QueryTriggerInteraction.Ignore)) return;
            var root = rh.collider.attachedRigidbody ? rh.collider.attachedRigidbody.gameObject : rh.collider.gameObject;
            if (!_pieceIds.TryGetValue(root, out id) || !_states.TryGetValue(id, out var s)) { id = 0; return; }
            string up = s.Level < Catalog.MaxLevel ? L10n.T("upgradeHint", Catalog.UpgradeCost(s.Type, s.Level)) : L10n.T("maxLevel");
            _lookInfo = L10n.T("lookInfo", PieceName(s.Type, s.Level), s.Level, s.Hp, s.MaxHp, up);
        }

        public static string PieceName(PieceType t, int level)
        {
            if (t == PieceType.Wall) return L10n.T(level >= 3 ? "WallMetal" : level == 2 ? "WallStone" : "Wall");
            return L10n.T(t.ToString());
        }

        private void LateUpdate()
        {
            if (!_building) { if (_hud) _hud.gameObject.SetActive(false); return; }
            if (!_hud) _hud = Hud.CreateText("BuildHint", new Vector2(0.5f, 0f), new Vector2(0f, 80f), 26f, TextAlignmentOptions.Bottom);
            if (!_hud) return;
            _hud.gameObject.SetActive(true);
            string cost = Siege.Instance.ViewActive ? L10n.T("cost", Catalog.BuildCost(_type)) : L10n.T("free");
            _hud.text = (_lookInfo.Length > 0 ? _lookInfo + "\n" : "") + L10n.T("buildHud", PieceName(_type, 1) + cost);
        }

        // ---------- networking ----------

        private void EnsureRegistered()
        {
            var nm = InstanceFinder.NetworkManager;
            if (!nm || nm == _registeredOn) return;
            _registeredOn = nm;
            nm.ServerManager.RegisterBroadcast<PiecePlaceRequest>(OnPlaceRequest);
            nm.ServerManager.RegisterBroadcast<PieceRemoveRequest>(OnRemoveRequest);
            nm.ServerManager.RegisterBroadcast<PieceUpgradeRequest>(OnUpgradeRequest);
            nm.ClientManager.RegisterBroadcast<PieceState>(OnPieceState);
            nm.SceneManager.OnClientLoadedStartScenes += OnClientLoaded;
            nm.ServerManager.OnServerConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) { _serverPieces.Clear(); _spent.Clear(); } };
            nm.ClientManager.OnClientConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) ClearLocalPieces(); };
            Log.LogInfo("Build system registered on NetworkManager.");
        }

        private void OnPlaceRequest(NetworkConnection conn, PiecePlaceRequest req, Channel ch)
        {
            if (_serverPieces.Values.Any(p => Clashes(p.Type, req.Type) && (p.Pos - req.Pos).sqrMagnitude < 0.01f)) return;
            int cost = Catalog.BuildCost(req.Type);
            if (!Siege.Instance.ServerTrySpend(cost)) return; // free outside fortress mode
            int maxHp = Catalog.MaxHp(req.Type, 1);
            var state = new PieceState { Id = _nextId++, Type = req.Type, Pos = req.Pos, RotY = req.RotY, Hp = maxHp, MaxHp = maxHp, Level = 1 };
            _serverPieces[state.Id] = state;
            _spent[state.Id] = Siege.Instance.ServerActive ? cost : 0;
            InstanceFinder.ServerManager.Broadcast(state);
        }

        private void OnUpgradeRequest(NetworkConnection conn, PieceUpgradeRequest req, Channel ch)
        {
            if (!_serverPieces.TryGetValue(req.Id, out var s) || s.Level >= Catalog.MaxLevel) return;
            int cost = Catalog.UpgradeCost(s.Type, s.Level);
            if (!Siege.Instance.ServerTrySpend(cost)) return;
            if (Siege.Instance.ServerActive) _spent[s.Id] += cost;
            s.Level++;
            int newMax = Catalog.MaxHp(s.Type, s.Level);
            s.Hp += newMax - s.MaxHp; // upgrading also repairs by the difference
            s.MaxHp = newMax;
            _serverPieces[s.Id] = s;
            InstanceFinder.ServerManager.Broadcast(s);
        }

        private void OnRemoveRequest(NetworkConnection conn, PieceRemoveRequest req, Channel ch)
        {
            if (!_serverPieces.TryGetValue(req.Id, out var state)) return;
            if (_spent.TryGetValue(req.Id, out int spent)) Siege.Instance.ServerAddCoins(spent / 2);
            Remove(state);
        }

        private void Remove(PieceState state)
        {
            _serverPieces.Remove(state.Id);
            _spent.Remove(state.Id);
            state.Removed = true;
            InstanceFinder.ServerManager.Broadcast(state);
        }

        // Host only: damage the piece owning this collider. Returns false if it isn't one of ours.
        public bool ServerDamage(Collider col, int dmg)
        {
            if (!col || !_pieceIds.TryGetValue(col.gameObject, out int id) || !_serverPieces.TryGetValue(id, out var state)) return false;
            state.Hp -= dmg;
            if (state.Hp <= 0) { Remove(state); return true; }
            _serverPieces[id] = state;
            InstanceFinder.ServerManager.Broadcast(state);
            return true;
        }

        // Pieces a walker has to break through (it walks over floors and ramps).
        public static bool IsWall(Collider col) => col && (col.gameObject.name.StartsWith("FortressPiece_Wall")
            || col.gameObject.name.StartsWith("FortressPiece_Cannon") || col.gameObject.name.StartsWith("FortressPiece_Gun"));

        private void OnClientLoaded(NetworkConnection conn, bool asServer)
        {
            if (!asServer) return;
            foreach (var p in _serverPieces.Values) InstanceFinder.ServerManager.Broadcast(conn, p);
        }

        private void OnPieceState(PieceState s, Channel ch)
        {
            _pieces.TryGetValue(s.Id, out var go);
            if (s.Removed)
            {
                if (go) { _pieceIds.Remove(go); Destroy(go); }
                _pieces.Remove(s.Id);
                _states.Remove(s.Id);
                return;
            }
            _states.TryGetValue(s.Id, out var prev);
            if (!go)
            {
                go = CreatePieceObject(s.Type, PieceLayer); // ground layer, so players and creatures can stand and walk on it
                go.name = "FortressPiece_" + s.Type + "_" + s.Id;
                go.transform.position = s.Pos;
                go.transform.rotation = s.Type == PieceType.Ramp ? Quaternion.Euler(-45f, s.RotY, 0f) : Quaternion.Euler(0f, s.RotY, 0f);
                _pieces[s.Id] = go;
                _pieceIds[go] = s.Id;
                prev.Level = 0;
            }
            if (prev.Level != s.Level) Models.SetLevelLook(go, s.Type, s.Level);
            _states[s.Id] = s;
            Models.Tint(go, s.MaxHp > 0 ? Mathf.Clamp01((float)s.Hp / s.MaxHp) : 1f);
        }

        private void ClearLocalPieces()
        {
            foreach (var p in _pieces.Values) if (p) Destroy(p);
            _pieces.Clear();
            _states.Clear();
            _pieceIds.Clear();
        }
    }
}
