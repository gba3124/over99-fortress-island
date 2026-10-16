using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
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
    public enum PieceType : byte { Wall, Floor, Ramp }

    // Wire messages. The mod is built without FishNet codegen, so serializers are registered by hand below.
    public struct PiecePlaceRequest : IBroadcast { public PieceType Type; public Vector3 Pos; public float RotY; }
    public struct PieceRemoveRequest : IBroadcast { public int Id; }
    public struct PieceState : IBroadcast { public int Id; public PieceType Type; public Vector3 Pos; public float RotY; public bool Removed; }

    // Fortnite-style building on a fixed world grid.
    // B toggles build mode, Q cycles wall/floor/ramp, hold LMB to keep placing, R flips direction, X removes the piece you look at.
    // The host owns the piece list; clients send requests and mirror what the host broadcasts.
    public class BuildSystem : MonoBehaviour
    {
        public const float Cell = 3f;
        private const float Thick = 0.3f;
        private const float Reach = 12f;

        public static ManualLogSource Log;
        public static ConfigEntry<Key> BuildKey;

        // host side
        private readonly Dictionary<int, PieceState> _serverPieces = new Dictionary<int, PieceState>();
        private int _nextId = 1;

        // every client (host included)
        private readonly Dictionary<int, GameObject> _pieces = new Dictionary<int, GameObject>();
        private readonly Dictionary<GameObject, int> _pieceIds = new Dictionary<GameObject, int>();

        private NetworkManager _registeredOn;
        private bool _building;
        private PieceType _type = PieceType.Wall;
        private bool _flip;
        private GameObject _ghost;
        private PieceType _ghostType = (PieceType)255;
        private Material _ghostMat;
        private bool _ghostValid;
        private Vector3 _lastSentPos = Vector3.positiveInfinity;

        private static int PieceLayer
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
            GenericWriter<PieceState>.SetWrite((w, v) => { w.WriteInt32(v.Id); w.WriteByte((byte)v.Type); w.WriteVector3(v.Pos); w.WriteSingle(v.RotY); w.WriteBoolean(v.Removed); });
            GenericReader<PieceState>.SetRead(r => new PieceState { Id = r.ReadInt32(), Type = (PieceType)r.ReadByte(), Pos = r.ReadVector3(), RotY = r.ReadSingle(), Removed = r.ReadBoolean() });
        }

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
            if (kb.qKey.wasPressedThisFrame) _type = (PieceType)(((int)_type + 1) % 3);
            if (kb.rKey.wasPressedThisFrame) _flip = !_flip;

            UpdateGhost();

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed && _ghostValid && _ghost.transform.position != _lastSentPos)
            {
                _lastSentPos = _ghost.transform.position;
                InstanceFinder.ClientManager.Broadcast(new PiecePlaceRequest { Type = _type, Pos = _lastSentPos, RotY = _ghost.transform.eulerAngles.y });
            }
            if (mouse != null && mouse.leftButton.wasReleasedThisFrame) _lastSentPos = Vector3.positiveInfinity;
            if (kb.xKey.wasPressedThisFrame && LookedAtPiece(out int id))
                InstanceFinder.ClientManager.Broadcast(new PieceRemoveRequest { Id = id });
        }

        private void SetBuilding(bool on)
        {
            _building = on;
            if (!on && _ghost) _ghost.SetActive(false);
        }

        // ---------- placement ----------

        // Snap the aim point to the grid the way Fortnite does: walls on cell edges, floors on cell tops, ramps across a cell.
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
                    Vector3 me = Player.LocalPlayer.transform.position;
                    int px = Mathf.FloorToInt(me.x / Cell), pz = Mathf.FloorToInt(me.z / Cell);
                    float edge;
                    if (alongX)
                    {
                        int side = cx != px ? (cx > px ? 0 : 1) : (fwd.x >= 0 ? 1 : 0);
                        edge = (cx + side) * Cell;
                        pos = new Vector3(edge, baseY + Cell * 0.5f, cellCenter.z);
                        rot = Quaternion.Euler(0f, 90f, 0f);
                    }
                    else
                    {
                        int side = cz != pz ? (cz > pz ? 0 : 1) : (fwd.z >= 0 ? 1 : 0);
                        edge = (cz + side) * Cell;
                        pos = new Vector3(cellCenter.x, baseY + Cell * 0.5f, edge);
                        rot = Quaternion.identity;
                    }
                    break;
                }
                case PieceType.Floor:
                    pos = cellCenter;
                    rot = Quaternion.identity;
                    break;
                default: // Ramp rises toward where you look
                    pos = cellCenter + Vector3.up * Cell * 0.5f;
                    rot = Quaternion.Euler(-45f, yaw, 0f);
                    break;
            }
            return hit;
        }

        private void UpdateGhost()
        {
            if (!_ghost || _ghostType != _type)
            {
                if (_ghost) Destroy(_ghost);
                _ghost = CreatePieceObject(_type);
                Destroy(_ghost.GetComponent<Collider>());
                _ghost.name = "FortressGhost";
                _ghostMat = _ghost.GetComponent<Renderer>().material;
                _ghostType = _type;
                DontDestroyOnLoad(_ghost);
            }

            bool hit = ComputePlacement(out var pos, out var rot);
            bool aboveWater = pos.y + Cell > WaterManager.GetWaterHeight(pos);
            bool free = !_pieces.Values.Any(p => p && (p.transform.position - pos).sqrMagnitude < 0.01f && SameType(p, _type));
            _ghostValid = hit && aboveWater && free;

            _ghost.SetActive(true);
            _ghost.transform.SetPositionAndRotation(pos, rot);
            SetColor(_ghostMat, _ghostValid ? new Color(0.3f, 0.6f, 1f) : new Color(0.9f, 0.2f, 0.2f));
        }

        private static bool SameType(GameObject go, PieceType t) => go.name.StartsWith("FortressPiece_" + t);

        private static GameObject CreatePieceObject(PieceType t)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.localScale = t == PieceType.Wall ? new Vector3(Cell, Cell, Thick)
                                    : t == PieceType.Floor ? new Vector3(Cell, Thick, Cell)
                                    : new Vector3(Cell, Thick, Cell * 1.4142f);
            return go;
        }

        private bool LookedAtPiece(out int id)
        {
            id = 0;
            Transform cam = GameInfo.CurCamera.transform;
            return Physics.Raycast(cam.position, cam.forward, out var rh, Reach, 1 << PieceLayer, QueryTriggerInteraction.Ignore)
                   && _pieceIds.TryGetValue(rh.collider.gameObject, out id);
        }

        // HUD uses TextMeshPro with the game's own font asset, whose fallbacks cover Chinese (IMGUI fonts don't under Wine).
        private TextMeshProUGUI _hud;

        private void LateUpdate()
        {
            if (!_building) { if (_hud) _hud.gameObject.SetActive(false); return; }
            if (!_hud) _hud = CreateHud();
            if (!_hud) return;
            _hud.gameObject.SetActive(true);
            _hud.text = L10n.T("buildHud", L10n.T(_type.ToString()));
        }

        private static TextMeshProUGUI CreateHud()
        {
            var font = AccessTools.Field(typeof(LocalizationManager), "_defaultFontAsset")?.GetValue(
                AccessTools.Field(typeof(LocalizationManager), "_instance")?.GetValue(null)) as TMP_FontAsset;
            if (!font) return null;

            var canvasGo = new GameObject("FortressHUD");
            DontDestroyOnLoad(canvasGo);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            var textGo = new GameObject("BuildHint");
            textGo.transform.SetParent(canvasGo.transform, false);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.fontSize = 28;
            tmp.alignment = TextAlignmentOptions.Bottom;
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = Color.black;
            var rt = tmp.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 90f);
            rt.sizeDelta = new Vector2(1400f, 60f);
            return tmp;
        }

        // ---------- networking ----------

        private void EnsureRegistered()
        {
            var nm = InstanceFinder.NetworkManager;
            if (!nm || nm == _registeredOn) return;
            _registeredOn = nm;
            nm.ServerManager.RegisterBroadcast<PiecePlaceRequest>(OnPlaceRequest);
            nm.ServerManager.RegisterBroadcast<PieceRemoveRequest>(OnRemoveRequest);
            nm.ClientManager.RegisterBroadcast<PieceState>(OnPieceState);
            nm.SceneManager.OnClientLoadedStartScenes += OnClientLoaded;
            nm.ServerManager.OnServerConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) _serverPieces.Clear(); };
            nm.ClientManager.OnClientConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) ClearLocalPieces(); };
            Log.LogInfo("Build system registered on NetworkManager.");
        }

        private void OnPlaceRequest(NetworkConnection conn, PiecePlaceRequest req, Channel ch)
        {
            // ponytail: host only rejects duplicates; add cost / build-phase / reach checks with the economy step.
            if (_serverPieces.Values.Any(p => p.Type == req.Type && (p.Pos - req.Pos).sqrMagnitude < 0.01f)) return;
            var state = new PieceState { Id = _nextId++, Type = req.Type, Pos = req.Pos, RotY = req.RotY };
            _serverPieces[state.Id] = state;
            InstanceFinder.ServerManager.Broadcast(state);
        }

        private void OnRemoveRequest(NetworkConnection conn, PieceRemoveRequest req, Channel ch)
        {
            if (!_serverPieces.TryGetValue(req.Id, out var state)) return;
            _serverPieces.Remove(req.Id);
            state.Removed = true;
            InstanceFinder.ServerManager.Broadcast(state);
        }

        private void OnClientLoaded(NetworkConnection conn, bool asServer)
        {
            if (!asServer) return;
            foreach (var p in _serverPieces.Values) InstanceFinder.ServerManager.Broadcast(conn, p);
        }

        private void OnPieceState(PieceState s, Channel ch)
        {
            if (_pieces.TryGetValue(s.Id, out var existing))
            {
                if (existing) { _pieceIds.Remove(existing); Destroy(existing); }
                _pieces.Remove(s.Id);
            }
            if (s.Removed) return;

            var go = CreatePieceObject(s.Type);
            go.name = "FortressPiece_" + s.Type + "_" + s.Id;
            go.layer = PieceLayer; // same layer as the ground, so players and creatures can stand and walk on it
            go.transform.position = s.Pos;
            go.transform.rotation = s.Type == PieceType.Ramp ? Quaternion.Euler(-45f, s.RotY, 0f) : Quaternion.Euler(0f, s.RotY, 0f);
            SetColor(go.GetComponent<Renderer>().material, new Color(0.6f, 0.45f, 0.3f));
            _pieces[s.Id] = go;
            _pieceIds[go] = s.Id;
        }

        private void ClearLocalPieces()
        {
            foreach (var p in _pieces.Values) if (p) Destroy(p);
            _pieces.Clear();
            _pieceIds.Clear();
        }

        private static void SetColor(Material m, Color c)
        {
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }
    }
}
