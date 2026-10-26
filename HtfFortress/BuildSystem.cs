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
    // FromLevel: the level the player saw. Two players upgrading the same turret at once (or one click repeated on a slow
    // link) raise it one level and pay once.
    public struct PieceUpgradeRequest : IBroadcast { public int Id; public int Branch; public bool Repair; public int FromLevel; public bool BranchOnly; }
    public struct PieceMoveRequest : IBroadcast { public int Id; public Vector3 Pos; public float RotY; }
    public struct PieceState : IBroadcast { public int Id; public PieceType Type; public Vector3 Pos; public float RotY; public bool Removed; public int Hp; public int MaxHp; public int Level; public int Branch; }

    // Fortnite-style building on a fixed world grid.
    // G toggles build mode, T cycles pieces, hold LMB to keep placing, Y flips, U upgrades, M picks up the piece you look at
    // to put it somewhere else, and H removes it.
    // (The game already uses B, Q, R, X, E, F, C, V, Z and 1-9.) While building, item clicks are blocked so you don't swing what you hold.
    // The host owns the piece list and the coins; clients send requests and mirror what the host broadcasts.
    public class BuildSystem : MonoBehaviour
    {
        public const float Cell = 3f;
        private const float Reach = 12f;          // what you can look at and work on in build mode
        public const float PlaceReach = 7.5f;     // where a new piece can go: two and a half cells ahead, like Fortnite, so the preview stays put
        public const float InteractReach = 4.5f;  // walking up to a piece outside build mode

        public static ManualLogSource Log;
        public static ConfigEntry<Key> BuildKey;
        public static BuildSystem Instance;

        // host side
        private readonly Dictionary<int, PieceState> _serverPieces = new Dictionary<int, PieceState>();
        private readonly Dictionary<int, int> _spent = new Dictionary<int, int>();
        private int _nextId = 1;
        // The concrete collection (not IEnumerable): callers that walk it every frame (turrets, bosses, siege) then get the
        // struct enumerator and no boxing allocation.
        public Dictionary<int, PieceState>.ValueCollection ServerPieces => _serverPieces.Values;

        // every client (host included)
        private readonly Dictionary<int, GameObject> _pieces = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, PieceState> _states = new Dictionary<int, PieceState>();
        private readonly Dictionary<GameObject, int> _pieceIds = new Dictionary<GameObject, int>();
        public GameObject PieceObject(int id) => _pieces.TryGetValue(id, out var go) ? go : null;
        public int PieceCount => _pieces.Count;

        private NetworkManager _registeredOn;
        private bool _building;
        public static bool Building => Instance && Instance._building;
        private PieceType _type = PieceType.Wall;
        private bool _flip;
        private int _turns; // R: quarter turns from the camera-facing direction
        private GameObject _ghost;
        private PieceType _ghostType = (PieceType)255;
        private bool _ghostValid;
        private int _lookId; // the piece under the crosshair (0 = none): the look card shows it
        private string _blockReason = "";
        public int RoundBuilt;

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
            GenericWriter<PieceUpgradeRequest>.SetWrite((w, v) => { w.WriteInt32(v.Id); w.WriteInt32(v.Branch); w.WriteBoolean(v.Repair); w.WriteInt32(v.FromLevel); w.WriteBoolean(v.BranchOnly); });
            GenericReader<PieceUpgradeRequest>.SetRead(r => new PieceUpgradeRequest { Id = r.ReadInt32(), Branch = r.ReadInt32(), Repair = r.ReadBoolean(), FromLevel = r.ReadInt32(), BranchOnly = r.ReadBoolean() });
            GenericWriter<PieceMoveRequest>.SetWrite((w, v) => { w.WriteInt32(v.Id); w.WriteVector3(v.Pos); w.WriteSingle(v.RotY); });
            GenericReader<PieceMoveRequest>.SetRead(r => new PieceMoveRequest { Id = r.ReadInt32(), Pos = r.ReadVector3(), RotY = r.ReadSingle() });
            GenericWriter<PieceState>.SetWrite((w, v) =>
            {
                w.WriteInt32(v.Id); w.WriteByte((byte)v.Type); w.WriteVector3(v.Pos); w.WriteSingle(v.RotY); w.WriteBoolean(v.Removed);
                w.WriteInt32(v.Hp); w.WriteInt32(v.MaxHp); w.WriteInt32(v.Level); w.WriteInt32(v.Branch);
            });
            GenericReader<PieceState>.SetRead(r => new PieceState
            {
                Id = r.ReadInt32(), Type = (PieceType)r.ReadByte(), Pos = r.ReadVector3(), RotY = r.ReadSingle(), Removed = r.ReadBoolean(),
                Hp = r.ReadInt32(), MaxHp = r.ReadInt32(), Level = r.ReadInt32(), Branch = r.ReadInt32()
            });
        }

        private void Awake() => Instance = this;

        private void Update()
        {
            EnsureRegistered();

            var kb = Keyboard.current;
            if (kb == null || ChatManager.IsTyping || Hud.ModalOpen) return;
            // Shift+J, anywhere in a round: every damaged building mended at once (the playtest asked for one repair for all).
            // Five seconds between presses (each player's own): it repairs everything for money, and holding the keys used to
            // fire a request every frame it was down (Owen: 「全部修復應該要有cd 5秒一次」).
            if (kb.jKey.wasPressedThisFrame && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) && Siege.Instance && Siege.Instance.ViewActive && InstanceFinder.IsClientStarted)
            {
                if (ClientRepairAllCost <= 0) return; // nothing is hurt: nothing to do, and it shouldn't eat the cooldown
                if (Time.time < _repairAllReady) { Hud.Banner(L10n.T("repairAllCd", Mathf.CeilToInt(_repairAllReady - Time.time)), 1.5f); return; }
                _repairAllReady = Time.time + RepairAllCooldown;
                InstanceFinder.ClientManager.Broadcast(new PieceUpgradeRequest { Id = -1, Repair = true });
                return;
            }
            if (kb[BuildKey.Value].wasPressedThisFrame) SetBuilding(!_building);
            if (!_building) { WalkUp(kb); return; }
            _walkUp = false;
            if (!InstanceFinder.IsClientStarted || !GameInfo.CurCamera || !Player.LocalPlayer || Cursor.lockState != CursorLockMode.Locked)
            {
                if (_ghost) _ghost.SetActive(false);
                HideCell();
                return;
            }

            if (kb.escapeKey.wasPressedThisFrame) { SetBuilding(false); return; }
            var mouse = Mouse.current;
            if (_moveId != 0) { UpdateMove(kb, mouse); return; }
            if (_waitRelease && (mouse == null || !mouse.leftButton.isPressed)) _waitRelease = false; // the click that put a moved piece down
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (kb.tKey.wasPressedThisFrame)
                for (int n = 0; n < BarOrder.Length; n++) // in the bar's order, skipping turrets still waiting for a blueprint
                {
                    _type = BarOrder[(System.Array.IndexOf(BarOrder, _type) + (shift ? BarOrder.Length - 1 : 1)) % BarOrder.Length];
                    _turns = 0;
                    if (Loot.Instance.ClientVault.CanBuild(_type)) break;
                }
            if (kb.yKey.wasPressedThisFrame) _flip = !_flip;
            if (kb.rKey.wasPressedThisFrame) _turns = (_turns + 1) & 3; // R turns the preview; ramps can be aligned without moving the camera

            UpdateGhost();
            UpdateLookInfo(out int lookedId, Reach);
            // Hold the mouse to build: on a teammate's site under the crosshair, or on the preview.
            var cam = GameInfo.CurCamera.transform;
            bool helping = Construction.Instance.SiteUnderAim(new Ray(cam.position, cam.forward), Reach, out var site);
            if (helping && _ghost) { _ghost.SetActive(false); HideCell(); }
            if (mouse != null && mouse.leftButton.isPressed && !_waitRelease && Time.time >= _nextWork && (helping || _ghostValid))
            {
                _nextWork = Time.time + Construction.SendEvery;
                if (helping) Construction.Instance.Work(site.Type, site.Pos, site.RotY);
                else Construction.Instance.Work(_type, _ghost.transform.position, _ghost.transform.eulerAngles.y);
            }
            // Hold H to take a piece down (it shakes and sinks); U / J are instant shortcuts; right click opens the menu.
            UpdateRemoveHold(kb.hKey.isPressed ? lookedId : _debugHold);
            _debugHold = 0;
            if (lookedId != 0 && kb.uKey.wasPressedThisFrame) Upgrade(lookedId, shift);
            if (lookedId != 0 && kb.eKey.wasPressedThisFrame && Time.frameCount - PieceMenu.ClosedFrame > 1) PieceMenu.Instance.Open(lookedId); // every piece: E shows its details
            if (lookedId != 0 && kb.jKey.wasPressedThisFrame) InstanceFinder.ClientManager.Broadcast(new PieceUpgradeRequest { Id = lookedId, Repair = true });
            if (lookedId != 0 && mouse != null && mouse.rightButton.wasPressedThisFrame && Time.frameCount - PieceMenu.ClosedFrame > 1) PieceMenu.Instance.Open(lookedId);
            if (lookedId != 0 && kb.mKey.wasPressedThisFrame) StartMove(lookedId);
        }

        // ---------- moving a piece: M on it, the preview carries it (same placement rules), LMB puts it down ----------

        private int _moveId;            // the piece being carried, 0 = none
        private bool _moveFromWalkUp;   // build mode came on just for this move
        private bool _waitRelease;      // the click that put it down mustn't also start building
        public bool Moving => _moveId != 0;

        public void StartMove(int id)
        {
            if (!_states.ContainsKey(id)) return;
            if (!_building) { _moveFromWalkUp = true; SetBuilding(true); }
            _moveId = id;
            Fx.Sound("PickupWhoosh", _states[id].Pos, 0.6f, AudioDistance.Short);
        }

        private void EndMove()
        {
            if (_moveId == 0) return;
            _moveId = 0;
            _waitRelease = true;
            if (_moveFromWalkUp) { _moveFromWalkUp = false; SetBuilding(false); }
        }

        private void UpdateMove(Keyboard kb, Mouse mouse)
        {
            _lookId = 0;
            if (!_states.ContainsKey(_moveId) || !(Siege.Instance && Siege.Instance.ViewActive)) { EndMove(); return; }
            UpdateGhost();
            if (kb.mKey.wasPressedThisFrame || (mouse != null && mouse.rightButton.wasPressedThisFrame)) { EndMove(); return; }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && _ghostValid)
            {
                InstanceFinder.ClientManager.Broadcast(new PieceMoveRequest { Id = _moveId, Pos = _ghost.transform.position, RotY = _ghost.transform.eulerAngles.y });
                EndMove();
            }
        }

        // Auto-test: move a piece the way the host would after a player's click.
        public bool ServerMove(int id, Vector3 pos, float rotY) => OnMoveRequest(null, new PieceMoveRequest { Id = id, Pos = pos, RotY = rotY }, Channel.Reliable);

        // Outside build mode, the way tower-defence shooters do it (Dungeon Defenders, Orcs Must Die!): walk up to a piece of
        // the fort and look at it to upgrade, repair or take it down. No hammer, no menu.
        private bool _walkUp;
        public static bool AimingAtPiece => Instance && Instance._walkUp; // U belongs to the piece, not the stall or the vault

        private void WalkUp(Keyboard kb)
        {
            _walkUp = false;
            _lookId = 0;
            bool can = Siege.Instance && Siege.Instance.ViewActive && !Weapons.Drawn && InstanceFinder.IsClientStarted && GameInfo.CurCamera && Player.LocalPlayer
                       && Cursor.lockState == CursorLockMode.Locked && PlayerManager.AlivePlayers.Contains(Player.LocalPlayer);
            int id = 0;
            if (can) UpdateLookInfo(out id, InteractReach);
            _walkUp = id != 0;
            UpdateRemoveHold(id != 0 && kb.hKey.isPressed ? id : _debugHold);
            _debugHold = 0;
            if (id == 0) return;
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (kb.uKey.wasPressedThisFrame) Upgrade(id, shift);
            if (kb.eKey.wasPressedThisFrame && Time.frameCount - PieceMenu.ClosedFrame > 1) PieceMenu.Instance.Open(id, walkUp: true); // E: details of any tower/wall
            if (kb.jKey.wasPressedThisFrame) InstanceFinder.ClientManager.Broadcast(new PieceUpgradeRequest { Id = id, Repair = true });
            if (kb.mKey.wasPressedThisFrame) StartMove(id);
        }

        // U: one level up. Into Lv3, where a turret chooses its branch, U opens the choice (it used to take A without
        // saying so, and the playtest found out late), in build mode or walking up to it; Shift+U still takes B straight away.
        private void Upgrade(int id, bool shift)
        {
            if (_states.TryGetValue(id, out var s) && !shift)
            {
                bool firstSplit = s.Level == Catalog.BranchLevel - 1 && Catalog.HasBranches(s.Type);
                bool secondSplit = s.Level == Catalog.SecondBranchLevel - 1 && Catalog.HasSecondBranch(s.Type) && s.Branch > 0;
                if (firstSplit || secondSplit)
                {
                    if (Time.frameCount - PieceMenu.ClosedFrame > 1) PieceMenu.Instance.Open(id, walkUp: !_building);
                    return;
                }
            }
            InstanceFinder.ClientManager.Broadcast(new PieceUpgradeRequest { Id = id, Branch = shift ? 2 : 1, FromLevel = LevelOf(id) });
            AskedUp(id);
        }

        public void DebugPressU(int id) => Upgrade(id, false); // auto-test: U on a piece, as from the keyboard

        // What mending everything would cost this player now (the build-time bar shows it).
        public int ClientRepairAllCost => _states.Values.Where(s => s.Hp < s.MaxHp).Sum(s => Perks.MyCost(Catalog.RepairCost(s.Hp, s.MaxHp)));

        public const float RepairAllCooldown = 5f; // Shift+J, each player their own
        private float _repairAllReady; // this client's next allowed press
        private readonly Dictionary<int, float> _repairAllNext = new Dictionary<int, float>(); // host: per player, by object id

        // Host, Shift+J: every damaged building mended, the worst first, as far as the money goes; everyone's told how it went.
        private void ServerRepairAll(Player who, int key)
        {
            if (_repairAllNext.TryGetValue(key, out float next) && Time.time < next) return; // a request inside the cooldown
            _repairAllNext[key] = Time.time + RepairAllCooldown;
            int done = 0, left = 0;
            foreach (var s in _serverPieces.Values.Where(x => x.Hp < x.MaxHp).OrderBy(x => (float)x.Hp / Mathf.Max(1, x.MaxHp)).ToList())
            {
                if (!Siege.Instance.ServerTrySpend(Perks.Cost(Catalog.RepairCost(s.Hp, s.MaxHp), who))) { left++; continue; }
                var t = s;
                t.Hp = t.MaxHp;
                _serverPieces[t.Id] = t;
                InstanceFinder.ServerManager.Broadcast(t);
                done++;
            }
            AutoTest.Count("repairs", done);
            if (left > 0) Fx.Announce("repairAllPartial", left);
            else if (done > 0) Fx.Announce("repairAllDone", done);
        }

        private float _nextWork;
        public const float RemoveTime = 0.6f;
        private int _removeId;
        private float _removeHold;
        private Vector3 _removeHome;

        private void UpdateRemoveHold(int id)
        {
            if (_removeId != 0 && (id != _removeId || !_pieces.ContainsKey(_removeId)))
            {
                if (_pieces.TryGetValue(_removeId, out var old) && old) old.transform.position = _removeHome;
                _removeId = 0;
            }
            if (id == 0 || !_pieces.TryGetValue(id, out var go) || !go) return;
            if (_removeId == 0) { _removeId = id; _removeHold = 0f; _removeHome = go.transform.position; }
            _removeHold += Time.deltaTime;
            float k = Mathf.Clamp01(_removeHold / RemoveTime);
            go.transform.position = _removeHome + Vector3.down * 0.35f * k + Random.insideUnitSphere * 0.05f * k;
            Hud.Ring(k, L10n.T("removing", RefundEstimate(_states[id])), Hud.Bad);
            if (k >= 1f)
            {
                InstanceFinder.ClientManager.Broadcast(new PieceRemoveRequest { Id = id });
                _removeId = 0;
            }
        }

        // What taking a piece down gives back: half of what went into it (build + upgrades), as the host counts it.
        public static int RefundEstimate(PieceState s)
        {
            int spent = Perks.MyCost(Catalog.BuildCost(s.Type));
            for (int l = 1; l < s.Level; l++) spent += Perks.MyTowerUpgradeCost(Catalog.UpgradeCost(s.Type, l));
            return spent / 2;
        }

        public bool TryGetState(int id, out PieceState s) => _states.TryGetValue(id, out s);

        // Auto-test: hold H on this piece for one frame.
        private int _debugHold;
        public void DebugRemoveHold(int id) => _debugHold = id;

        public void DebugSetBuilding(bool on, PieceType type) { _type = type; SetBuilding(on); }

        public void StopBuilding() => SetBuilding(false);

        // Leaving a session (host lost, main menu): the build bar, ghost and cell outline must not stay on screen.
        public void ForceCloseUI()
        {
            SetBuilding(false);
            _moveId = 0;
            if (_ghost) { _ghost.SetActive(false); Object.Destroy(_ghost); _ghost = null; _ghostType = (PieceType)255; }
            HideCell();
            if (_cards) _cards.gameObject.SetActive(false);
            if (_infoBox) _infoBox.gameObject.SetActive(false);
            if (_warnBox) _warnBox.gameObject.SetActive(false);
            if (_lookBox) _lookBox.gameObject.SetActive(false);
        }

        private void SetBuilding(bool on)
        {
            if (!on) { _moveFromWalkUp = false; EndMove(); }
            _building = on;
            // The repair towers' range crosses only while building (Owen's list: 只在建造模式顯示); out of it they cluttered the island.
            foreach (var kv in _pieces)
                if (kv.Value && _states.TryGetValue(kv.Key, out var ps) && ps.Type == PieceType.Mender && kv.Value.transform.Find("BubbleRing") is Transform ring)
                    ring.gameObject.SetActive(on);
            if (on && Weapons.Drawn) Weapons.Instance.Holster(); // the hammer and a fortress weapon don't go together
            if (!on && _ghost) _ghost.SetActive(false);
            if (!on) HideCell();
        }

        // ---------- placement ----------

        // Snap the aim point to the grid the way Fortnite does: walls on cell edges, floors on cell tops,
        // ramps across a cell, turrets standing in the middle of a cell. The preview never strays further than PlaceReach
        // (looking past it, or at the sky, it sits on the ground at the end of the reach), and a level's floor is the ground
        // under that cell (or the level of the piece you aim at), so nothing sinks into the island or floats over it.
        private bool ComputePlacement(out Vector3 pos, out Quaternion rot) => ComputePlacement(0, out pos, out rot);

        // What the preview is: the carried piece while moving, else the piece picked on the bar.
        private PieceType GhostType => _moveId != 0 && _states.TryGetValue(_moveId, out var ms) ? ms.Type : _type;

        // Candidate wall spots near `aim`: extend an existing wall by one cell, or stand a wall on a floor edge. Returns
        // the nearest candidate within reach; UpdateGhost still does the final overlap/afford checks.
        private bool TrySnapWall(Vector3 aim, ref Vector3 pos, ref Quaternion rot)
        {
            float best = 2.25f * 2.25f; bool found = false;
            foreach (var s in _states.Values)
            {
                if (s.Type == PieceType.Wall)
                {
                    Quaternion sr = Quaternion.Euler(0f, s.RotY, 0f);
                    Vector3 dir = sr * Vector3.right, normal = sr * Vector3.forward;
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        // Straight continuation, then a 90-degree corner at that end: magnetic, not only same-axis.
                        Vector3 c = s.Pos + dir * (Cell * sign);
                        float d = (new Vector2(c.x - aim.x, c.z - aim.z)).sqrMagnitude;
                        if (d < best) { best = d; pos = c; rot = sr; found = true; }
                        for (int side = -1; side <= 1; side += 2)
                        {
                            Vector3 corner = s.Pos + dir * (Cell * 0.5f * sign) + normal * (Cell * 0.5f * side);
                            float cd = (new Vector2(corner.x - aim.x, corner.z - aim.z)).sqrMagnitude;
                            if (cd < best) { best = cd; pos = corner; rot = Quaternion.Euler(0f, s.RotY + 90f, 0f); found = true; }
                        }
                    }
                }
                else if (s.Type == PieceType.Floor)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        bool alongX = i < 2;
                        float off = (i % 2 == 0 ? -0.5f : 0.5f) * Cell;
                        Vector3 c = s.Pos + (alongX ? new Vector3(0f, 0f, off) : new Vector3(off, 0f, 0f));
                        c.y = s.Pos.y + Cell * 0.5f;
                        float d = (new Vector2(c.x - aim.x, c.z - aim.z)).sqrMagnitude;
                        if (d < best) { best = d; pos = c; rot = Quaternion.Euler(0f, alongX ? 0f : 90f, 0f); found = true; }
                    }
                }
            }
            return found;
        }

        private bool ComputePlacement(int extend, out Vector3 pos, out Quaternion rot)
        {
            var type = GhostType;
            Transform cam = GameInfo.CurCamera.transform;
            int mask = GameInfo.LevelLayer.value | (1 << PieceLayer);
            bool hit = Physics.Raycast(cam.position, cam.forward, out var rh, PlaceReach, mask, QueryTriggerInteraction.Ignore);
            Vector3 aim;
            float? onLevel = null; // the base of the level of the piece under the crosshair
            PieceState? hitPiece = null;
            if (hit)
            {
                aim = rh.point - cam.forward * 0.05f;
                var root = rh.collider.attachedRigidbody ? rh.collider.attachedRigidbody.gameObject : rh.collider.gameObject;
                if (_pieceIds.TryGetValue(root, out int pid) && _states.TryGetValue(pid, out var ps)) { onLevel = BaseOf(ps); hitPiece = ps; }
            }
            else
            {
                Vector3 far = cam.position + cam.forward * PlaceReach;
                hit = Physics.Raycast(far + Vector3.up * 3f, Vector3.down, out var down, 30f, mask, QueryTriggerInteraction.Ignore);
                aim = hit ? down.point : far;
            }

            Vector3 fwd = cam.forward; fwd.y = 0f;
            bool alongX = Mathf.Abs(fwd.x) >= Mathf.Abs(fwd.z);
            float yaw = alongX ? (fwd.x >= 0 ? 90f : 270f) : (fwd.z >= 0 ? 0f : 180f);
            if (_flip) yaw += 180f;
            yaw = Mathf.Repeat(yaw + _turns * 90f, 360f);

            int cx = Mathf.FloorToInt(aim.x / Cell), cz = Mathf.FloorToInt(aim.z / Cell);
            if (extend != 0) { if (alongX) cx += fwd.x >= 0 ? extend : -extend; else cz += fwd.z >= 0 ? extend : -extend; }
            Vector3 mid = new Vector3((cx + 0.5f) * Cell, aim.y, (cz + 0.5f) * Cell);
            float baseY = onLevel.HasValue ? onLevel.Value + Cell * Mathf.Floor((aim.y - onLevel.Value + 0.25f) / Cell)
                        : Loot.Ground(mid + Vector3.up * 2.5f, 8f, out var under) ? under.y : aim.y;
            Vector3 cellCenter = new Vector3(mid.x, baseY, mid.z);

            // Ramps: if a floor or another ramp is on one of the four sides, turn the ramp to face it and use its level.
            // This is the "auto edge" that the old ramp lacked; R still overrides the facing.
            if (type == PieceType.Ramp && _turns == 0 && !FreePlace && !(hitPiece.HasValue && hitPiece.Value.Type == PieceType.Ramp))
            {
                PieceState? near = null; float best = Cell * 1.65f * (Cell * 1.65f);
                foreach (var p in _states.Values)
                {
                    if (p.Type != PieceType.Floor && p.Type != PieceType.Ramp) continue;
                    Vector3 d = p.Pos - cellCenter; d.y = 0f;
                    float d2 = d.sqrMagnitude;
                    if (d2 < best && d2 > 0.2f) { best = d2; near = p; }
                }
                if (near.HasValue)
                {
                    Vector3 d = near.Value.Pos - cellCenter; d.y = 0f;
                    yaw = Mathf.Repeat(Mathf.Round(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg / 90f) * 90f, 360f);
                    baseY = near.Value.Type == PieceType.Floor ? near.Value.Pos.y : BaseOf(near.Value);
                    cellCenter.y = baseY;
                }
            }

            switch (type)
            {
                case PieceType.Wall:
                {
                    // Edge between the player's cell and the aimed cell; the far edge when aiming into your own cell.
                    Vector3 me = Player.LocalPlayer.Transform.position;
                    int px = Mathf.FloorToInt(me.x / Cell), pz = Mathf.FloorToInt(me.z / Cell);
                    bool edgeX = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) > 0.5f; // a 90/270 wall stands on an X edge
                    if (edgeX)
                    {
                        int side = cx != px ? (cx > px ? 0 : 1) : (fwd.x >= 0 ? 1 : 0);
                        pos = new Vector3((cx + side) * Cell, baseY + Cell * 0.5f, cellCenter.z);
                        rot = Quaternion.Euler(0f, 90f, 0f);
                    }
                    else
                    {
                        int side = cz != pz ? (cz > pz ? 0 : 1) : (fwd.z >= 0 ? 1 : 0);
                        pos = new Vector3(cellCenter.x, baseY + Cell * 0.5f, (cz + side) * Cell);
                        rot = Quaternion.Euler(0f, yaw, 0f);
                    }
                    break;
                }
                case PieceType.Floor:
                    pos = cellCenter;
                    rot = Quaternion.identity;
                    break;
                case PieceType.Ramp: // rises toward where you look
                    if (_turns == 0 && hitPiece.HasValue && hitPiece.Value.Type == PieceType.Ramp)
                    {
                        // Standing on/climbing a ramp and aiming along it: continue the staircase in that direction, one
                        // level up (or down when looking back down it). This is what "aim at the ramp and keep building"
                        // should do; the old code kept placing the next ramp at the same level.
                        var rp = hitPiece.Value;
                        float rYaw = rp.RotY;
                        Vector3 upSlope = new Vector3(Mathf.Sin(rYaw * Mathf.Deg2Rad), 0f, Mathf.Cos(rYaw * Mathf.Deg2Rad));
                        // Follow the staircase's own direction, not the camera's grid axis. If the player is standing below
                        // the ramp, the next one goes up; standing above it, the next one goes down. Camera look is only
                        // the fallback when the player is level with the ramp.
                        float along = Vector3.Dot(Player.LocalPlayer.Transform.position - rp.Pos, upSlope);
                        bool upward = Mathf.Abs(along) > 0.65f ? along < 0f : Vector3.Dot(fwd, upSlope) >= 0f;
                        Vector3 dir = upward ? upSlope : -upSlope;
                        float baseLevel = BaseOf(rp) + (upward ? Cell : -Cell);
                        pos = new Vector3(rp.Pos.x + dir.x * Cell, baseLevel + Cell * 0.5f, rp.Pos.z + dir.z * Cell);
                        rot = Quaternion.Euler(-45f, rYaw, 0f);
                        break;
                    }
                    pos = cellCenter + Vector3.up * Cell * 0.5f;
                    rot = Quaternion.Euler(-45f, yaw, 0f);
                    break;
                case PieceType.Spikes: // a mat on the ground (or a floor) in the middle of the cell
                    pos = new Vector3(cellCenter.x, baseY, cellCenter.z);
                    if (Physics.Raycast(pos + Vector3.up * (Cell + 1f), Vector3.down, out var mat, Cell * 2f, mask, QueryTriggerInteraction.Ignore))
                        pos.y = mat.point.y + 0.07f;
                    rot = Quaternion.identity;
                    break;
                default: // turret stands in the middle of the cell, on whatever surface is there (ground or a floor)
                    pos = new Vector3(cellCenter.x, baseY, cellCenter.z);
                    if (Physics.Raycast(pos + Vector3.up * (Cell + 1f), Vector3.down, out var ground, Cell * 2f, mask, QueryTriggerInteraction.Ignore))
                        pos.y = ground.point.y;
                    rot = Quaternion.Euler(0f, yaw, 0f);
                    break;
            }
            // Magnetic wall joins: aiming near an existing wall/floor snaps the new wall to its line or edge instead of
            // asking the player to line up a 3 m grid by eye. R / free placement opt out.
            if (type == PieceType.Wall && !FreePlace && _turns == 0 && TrySnapWall(aim, ref pos, ref rot)) { }

            // Hold Alt for free placement: no 3 m cell snap, only the ground. For turrets and traps this is the fine
            // adjustment the grid made impossible; structural pieces still look best on the grid, but the player decides.
            if (hit && FreePlace)
            {
                float fy = baseY;
                if (Loot.Ground(new Vector3(aim.x, aim.y, aim.z) + Vector3.up * 2.5f, 8f, out var freeGround)) fy = freeGround.y;
                pos.x = aim.x; pos.z = aim.z;
                pos.y = type == PieceType.Wall || type == PieceType.Ramp ? fy + Cell * 0.5f : type == PieceType.Spikes ? fy + 0.07f : fy;
            }
            return hit;
        }

        private static bool FreePlace => Keyboard.current != null && Keyboard.current.leftAltKey.isPressed;

        // Where a piece's level starts (walls and ramps are centred a half cell up).
        private static float BaseOf(PieceState s) => s.Type == PieceType.Wall || s.Type == PieceType.Ramp ? s.Pos.y - Cell * 0.5f : s.Pos.y;

        // Two pieces that can't share a spot: the same kind, or two turrets, at one place on the grid (levels a half cell apart).
        internal static bool Overlap(PieceType a, Vector3 pa, PieceType b, Vector3 pb) =>
            Clashes(a, b) && new Vector2(pa.x - pb.x, pa.z - pb.z).sqrMagnitude < 0.04f && Mathf.Abs(pa.y - pb.y) < Cell * 0.5f;

        // Building happens on the fortress island during a round.
        internal static bool AllowedAt(Vector3 pos) => FortressIsland.Instance && FortressIsland.Instance.OnIsland(pos, FortressIsland.BuildRadius);

        private static bool Clashes(PieceType a, PieceType b) => a == b || (Catalog.IsTurret(a) && Catalog.IsTurret(b));

        private void UpdateGhost()
        {
            var type = GhostType;
            if (!_ghost || _ghostType != type)
            {
                if (_ghost) Destroy(_ghost);
                _ghost = CreatePieceVisual(type);
                _ghost.name = "FortressGhost";
                _ghostType = type;
                DontDestroyOnLoad(_ghost);
            }

            bool hit = ComputePlacement(out var pos, out var rot);
            bool Taken(Vector3 at) => _states.Values.Any(s => s.Id != _moveId && Overlap(s.Type, s.Pos, type, at));
            if (!FreePlace && hit && Taken(pos) && ComputePlacement(1, out var next, out var nextRot) && !Taken(next)) { pos = next; rot = nextRot; } // aiming at your last piece: the next one along
            bool aboveWater = pos.y + Cell > WaterManager.GetWaterHeight(pos);
            bool free = !Taken(pos);
            bool moving = _moveId != 0; // a piece being moved was paid for and unlocked already
            bool affordable = moving || Siege.Instance.ViewCoins >= Perks.MyCost(Catalog.BuildCost(type));
            bool zone = AllowedAt(pos), unlocked = moving || Loot.Instance.ClientVault.CanBuild(type);
            _blockReason = !unlocked ? L10n.T("needBlueprint")
                         : !Siege.Instance.ViewActive ? L10n.T("needRound")
                         : !zone ? L10n.T("needIsland")
                         : !affordable ? L10n.T("needCoins")
                         : "";
            _ghostValid = hit && aboveWater && free && affordable && zone && unlocked && Siege.Instance.ViewActive;

            _ghost.SetActive(true);
            _ghost.transform.SetPositionAndRotation(pos, rot);
            Color tint = _ghostValid ? new Color(0.3f, 0.6f, 1f) : new Color(0.9f, 0.2f, 0.2f);
            Models.Paint(_ghost, tint);
            if (FreePlace) HideCell(); else DrawCell(pos, tint); // no cell outline in free placement: it is no longer snapped to one
        }

        // The cell the preview is in, outlined on the ground under it (where exactly it goes, at a glance).
        private readonly Transform[] _cellEdges = new Transform[4];
        private Material _cellMat;
        private void DrawCell(Vector3 at, Color c)
        {
            if (!_cellMat) _cellMat = Models.Make(c, c);
            Models.SetMatColor(_cellMat, c);
            var type = GhostType;
            float y = type == PieceType.Wall || type == PieceType.Ramp ? at.y - Cell * 0.5f : at.y;
            // The cell: a wall sits on an edge, so outline the cell on the far side of it.
            Vector3 fwd = GameInfo.CurCamera.transform.forward; fwd.y = 0f;
            Vector3 probe = type == PieceType.Wall ? at + fwd.normalized * (Cell * 0.5f) : at;
            Vector3 mid = new Vector3((Mathf.Floor(probe.x / Cell) + 0.5f) * Cell, y + 0.06f, (Mathf.Floor(probe.z / Cell) + 0.5f) * Cell);
            for (int i = 0; i < 4; i++)
            {
                if (!_cellEdges[i])
                {
                    var e = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(e.GetComponent<Collider>());
                    var r = e.GetComponent<Renderer>();
                    r.sharedMaterial = _cellMat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    DontDestroyOnLoad(e);
                    _cellEdges[i] = e.transform;
                }
                bool alongX = i < 2;
                float off = (i % 2 == 0 ? -0.5f : 0.5f) * Cell;
                _cellEdges[i].gameObject.SetActive(true);
                _cellEdges[i].position = mid + (alongX ? new Vector3(0f, 0f, off) : new Vector3(off, 0f, 0f));
                _cellEdges[i].localScale = alongX ? new Vector3(Cell, 0.04f, 0.07f) : new Vector3(0.07f, 0.04f, Cell);
            }
        }
        private void HideCell() { foreach (var e in _cellEdges) if (e) e.gameObject.SetActive(false); }

        // The look of a piece without its collider (previews and construction sites).
        internal static GameObject CreatePieceVisual(PieceType t)
        {
            var go = CreatePieceObject(t, 0);
            Destroy(go.GetComponent<Collider>());
            return go;
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
                Models.Attach(Catalog.BaseModel(t), go.transform, new Vector3(1.2f, 0.8f, 1.2f), new Vector3(0f, 0.4f, 0f)); // stays put
                var head = new GameObject("Head");
                head.layer = layer;
                head.transform.SetParent(go.transform, false);
                head.transform.localPosition = new Vector3(0f, Catalog.HeadHeight(t), 0f);
                Models.Attach(Catalog.BodyModel(t), head.transform, new Vector3(0.6f, 0.15f, 0.6f));
                AttachHeadModule(head.transform, t, 0);
            }
            else Models.Attach(t.ToString(), go.transform, size);
            return go;
        }

        // Out of the way at once (Destroy waits for the end of the frame, and several level-ups can land in one frame: Find
        // would keep finding the old one).
        private static void Drop(Transform t)
        {
            if (!t) return;
            t.name = "_gone";
            t.gameObject.SetActive(false);
            Destroy(t.gameObject);
        }

        // How far it reaches. The bubble tower: a soap bubble over the ground it protects (BubbleShell). The repair tower: a
        // ring of green on the ground (no bubble: it doesn't cover anything).
        // A flat plus sign, used by the repair tower's range marker and its head badge.
        private static void CrossMark(Transform parent, float arm, float thick, Material m)
        {
            for (int i = 0; i < 2; i++)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(b.GetComponent<Collider>());
                b.transform.SetParent(parent, false);
                b.transform.localScale = i == 0 ? new Vector3(arm, 0.02f, thick) : new Vector3(thick, 0.02f, arm); // (the second one used to be turned 90° on x: stood on end)
                b.GetComponent<Renderer>().sharedMaterial = m;
            }
        }

        private static Material _mendRimMat;
        // The top of the island (or the sea) at p, under the fort's own pieces.
        private static Vector3 OnGround(Vector3 p)
        {
            float y = WaterManager.GetWaterHeight(p);
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 20f, Vector3.down, 40f, GameInfo.LevelLayer, QueryTriggerInteraction.Ignore))
                if (!h.collider.gameObject.name.StartsWith("FortressPiece") && h.collider.gameObject.name != "FortressCore") y = Mathf.Max(y, h.point.y);
            return new Vector3(p.x, y, p.z);
        }
        private static void SetBubbleRing(GameObject go, PieceType type, int level, int branch)
        {
            Drop(go.transform.Find("BubbleRing"));
            var ring = new GameObject("BubbleRing").transform;
            ring.SetParent(go.transform, false);
            float r = Catalog.Turret(type, level, branch).Range;
            if (type == PieceType.Bubble) { BubbleShell.Make(ring, r); return; }
            // Repair tower: no soap dome or solid ring (it looked like a second bubble shield). Four broken green
            // "repair cross" markers on the ground mark the reach instead, shown only while building (SetBuilding).
            ring.gameObject.SetActive(Building);
            if (!_mendRimMat) _mendRimMat = BossVfx.Wash(new Color(0.36f, 1f, 0.52f, 0.55f));
            for (int k = 0; k < 4; k++)
            {
                var mark = new GameObject("MendMark" + k).transform;
                mark.SetParent(ring, false);
                float a = k * Mathf.PI * 0.5f;
                mark.localPosition = new Vector3(Mathf.Cos(a) * r, 0.06f, Mathf.Sin(a) * r);
                mark.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                mark.position = OnGround(mark.position) + Vector3.up * 0.06f; // at the tower's foot they hung in the air over a hollow, or in a cliff
                CrossMark(mark, 0.42f, 0.16f, _mendRimMat);
            }
            return;
        }

        // How a turret shows its level (art pass 5): every level from Lv2 to Lv8 adds a piece round its plinth, Lv3 and Lv9 swap
        // in the class's head, Lv9 adds the final class's dressing (Catalog.TurretKit); at Lv5, Lv10 and Lv15 another barrel
        // stands on the turntable (what it does then, one more fish, shell, harpoon, chain jump..., is Catalog.Milestones);
        // from Lv4 its ring of lamps lights up, one a level (SetLamps); the head grows a little up to Lv13.
        private static Material _collarMat, _trimMat, _studMat;
        // The class head on the turntable: Default, then A / B from Lv3, A1 / A2 / B1 / B2 from Lv9; swapped whenever the branch changes.
        private static void AttachHeadModule(Transform head, PieceType type, int branch)
        {
            if (!head) return;
            Drop(head.Find("HeadModule"));
            if (!Catalog.HasModules(type)) return;
            var model = Catalog.ModuleModel(type, branch);
            if (string.IsNullOrEmpty(model)) return;
            var module = Models.Attach(model, head, Vector3.one * 0.4f);
            if (module) module.name = "HeadModule";
        }

        private static void SetTurretLook(GameObject go, PieceType type, int level, int branch)
        {
            Drop(go.transform.Find("LevelLook"));
            var head = go.transform.Find("Head");
            if (head)
            {
                foreach (Transform c in head.Cast<Transform>().ToList()) if (c.name.StartsWith("Barrel")) Drop(c);
                head.localScale = Vector3.one * Catalog.HeadScale(level);
            }
            var look = new GameObject("LevelLook").transform;
            look.SetParent(go.transform, false);
            if (!_collarMat) { _collarMat = Models.Make(new Color(0.24f, 0.26f, 0.3f)); _trimMat = Models.Make(new Color(1f, 0.78f, 0.3f), new Color(0.9f, 0.6f, 0.15f)); }
            Models.AttachAll(Catalog.TurretKit(type, level, branch), look, "Pieces"); // one renderer for them all
            AttachHeadModule(head, type, branch);
            // One barrel per simultaneous shot/target (the real stat), not per milestone: a Cluster cannon shows three, a
            // Barrage mortar three, and every Lv5/10/15 milestone adds another because Catalog.Turret adds the shot too. They
            // stand on the turntable beside the head, right then left, each further pair one Catalog.BarrelStep on.
            int extra = Catalog.Barrels(type, level, branch) - 1;
            var mount = Catalog.BarrelMount(type);
            var step = Catalog.BarrelStep(type);
            for (int i = 1; i <= extra && head; i++)
            {
                var barrel = new GameObject("Barrel" + i).transform;
                barrel.SetParent(head, false);
                float side = i % 2 == 1 ? 1f : -1f;
                int k = (i - 1) / 2;
                barrel.localPosition = new Vector3(side * (mount.x + k * step.x), mount.y + k * step.y, mount.z + k * step.z);
                barrel.localScale = new Vector3(Catalog.BarrelMirror(type) && side < 0f ? -1f : 1f, 1f, 1f);
                Models.Attach(Catalog.BarrelModel(type), barrel, new Vector3(0.1f, 0.12f, 0.5f));
            }
            SetLamps(look, level);
        }

        // Past Lv3 a ring of lamps round the foot lights up, one a level (Owen: "我寧願做一圈暗的燈，然後越亮越多顆"; it was a
        // laurel wreath): from the back, a side at a time, the last two meeting at the front at Lv20 (+17 on its card), about
        // the most one turret gets in a full run (a gun to Lv20 costs $7M; a solo run to wave 100 earns $7.5M, a trio
        // $12.5M). Past that the full ring burns whiter, white by Lv25. The unlit ones are there from Lv1: dark glass to fill.
        public const int Lamps = 17, LampsWhite = 25;
        private const float LampRing = 1f; // just outside the 1.6 m base
        public static int LampsLit(int level) => Mathf.Min(Lamps, Catalog.Beyond(level));
        private static readonly Dictionary<int, (Mesh dim, Mesh lit)> LampMeshes = new Dictionary<int, (Mesh, Mesh)>();
        private static readonly Dictionary<int, Material> LampGlows = new Dictionary<int, Material>();
        private static readonly Dictionary<int, Mesh> LampArcs = new Dictionary<int, Mesh>();
        private static Mesh _lampSockets, _lampBezels;

        private static void SetLamps(Transform look, int level)
        {
            int lit = LampsLit(level);
            var all = Enumerable.Range(0, Lamps).Select(LampAt).ToList();
            if (!_lampSockets) _lampSockets = Combine("FortressLampSockets", WeaponFx.Cyl, all, new Vector3(0.2f, 0.06f, 0.2f));
            if (!_lampBezels) _lampBezels = Combine("FortressLampBezels", WeaponFx.Cyl, all.Select(p => p + Vector3.up * 0.06f), new Vector3(0.15f, 0.025f, 0.15f));
            if (!LampMeshes.TryGetValue(lit, out var m))
            {
                var bulbs = all.Select(p => p + Vector3.up * 0.14f).ToList();
                LampMeshes[lit] = m = (Combine("FortressLampsDim" + lit, WeaponFx.Ball, bulbs.Skip(lit), Vector3.one * 0.17f),
                                       Combine("FortressLampsLit" + lit, WeaponFx.Ball, bulbs.Take(lit), Vector3.one * 0.17f));
            }
            WeaponFx.Piece(_lampSockets, _collarMat, look).name = "LampSockets";
            WeaponFx.Piece(_lampBezels, _trimMat, look).name = "LampBezels";
            if (lit < Lamps) WeaponFx.Piece(m.dim, BossVfx.Solid(new Color(0.09f, 0.1f, 0.12f)), look).name = "LampsDim";
            if (lit == 0) return;
            int step = Mathf.Clamp(level, Catalog.BranchLevel + Lamps, LampsWhite);
            if (!LampGlows.TryGetValue(step, out var glow) || !glow)
            {
                float k = Mathf.InverseLerp(Catalog.BranchLevel + Lamps, LampsWhite, step);
                LampGlows[step] = glow = BossVfx.Glow(Color.Lerp(new Color(1f, 0.72f, 0.2f), new Color(1f, 0.97f, 0.9f), k), Mathf.Round(5f + 3f * k));
            }
            WeaponFx.Piece(m.lit, glow, look).name = "LampsLit";
            // A glowing arc on the brass rail shows how far round the ring has filled: the bulbs count, the arc reads at a glance.
            if (!LampArcs.TryGetValue(lit, out var arc)) LampArcs[lit] = arc = LampArc(lit);
            WeaponFx.Piece(arc, glow, look).name = "LampArc";
        }

        // The progress arc on the foot rail, from the back clockwise for `lit` of the 17 lamps.
        private static Mesh LampArc(int lit)
        {
            float frac = Mathf.Clamp01(lit / (float)Lamps);
            int segs = Mathf.Max(1, Mathf.CeilToInt(36f * frac));
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= segs; i++)
            {
                float a = Mathf.PI + frac * TAU * i / segs;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts.Add(new Vector3(c * 1.04f, 0.075f, s * 1.04f));
                verts.Add(new Vector3(c * 1.14f, 0.075f, s * 1.14f));
            }
            for (int i = 0; i < segs; i++)
            {
                int k = i * 2;
                tris.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 });
            }
            var mesh = new Mesh { name = "FortressLampArc" + lit };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // Lamp i round the foot: 0 at the back, then left and right in turn, the last two either side of the front (+z).
        private static Vector3 LampAt(int i)
        {
            int s = (i + 1) / 2;
            float th = Mathf.PI + (i % 2 == 1 ? -1f : 1f) * s * 2f * Mathf.PI / Lamps;
            return new Vector3(Mathf.Sin(th) * LampRing, 0f, Mathf.Cos(th) * LampRing);
        }

        private const float TAU = Mathf.PI * 2f;
        private static Mesh Combine(string name, Mesh part, IEnumerable<Vector3> at, Vector3 size)
        {
            var mesh = new Mesh { name = name };
            mesh.CombineMeshes(at.Select(p => new CombineInstance { mesh = part, transform = Matrix4x4.TRS(p, Quaternion.identity, size) }).ToArray(), true, true);
            return mesh;
        }

        // Past Lv3 every level adds a glowing gold stud (up to 12): along the top of a wall, round the edge of a floor, ramp
        // or spike mat (a turret has its ring of lamps instead, SetLamps).
        private static void SetRankStuds(GameObject go, PieceType type, int level)
        {
            Drop(go.transform.Find("RankStuds"));
            int n = Mathf.Min(12, Catalog.Beyond(level));
            if (n == 0 || Catalog.IsTurret(type)) return;
            var holder = new GameObject("RankStuds").transform;
            holder.SetParent(go.transform, false);
            if (!_studMat) _studMat = Models.Make(new Color(1f, 0.82f, 0.32f), new Color(1f, 0.7f, 0.2f));
            Vector3 size = Catalog.Size(type);
            for (int i = 0; i < n; i++)
            {
                var stud = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(stud.GetComponent<Collider>());
                stud.transform.SetParent(holder, false);
                float a = i * Mathf.PI * 2f / 12f;
                stud.transform.localPosition = type == PieceType.Wall ? new Vector3((i - (n - 1) / 2f) * 0.24f, size.y * 0.5f + 0.07f, 0f)
                                             : new Vector3(Mathf.Cos(a) * size.x * 0.42f, size.y * 0.5f + 0.05f, Mathf.Sin(a) * size.z * 0.42f);
                stud.transform.localScale = Vector3.one * 0.13f;
                stud.transform.localRotation = Quaternion.Euler(45f, a * Mathf.Rad2Deg, 45f);
                stud.GetComponent<Renderer>().sharedMaterial = _studMat;
            }
        }

        // Walls change material with their level: wooden palisade, stone with battlements, spiked iron plates.
        private static void SetWallModel(GameObject go, int level)
        {
            string model = level >= 3 ? "WallMetal" : level == 2 ? "WallStone" : "Wall";
            if (go.transform.Find(model + "Model")) return;
            foreach (Transform c in go.transform) Destroy(c.gameObject);
            Models.Attach(model, go.transform, Catalog.Size(PieceType.Wall));
        }

        // Shows what you're looking at and returns its id (0 = nothing).
        private void UpdateLookInfo(out int id, float reach)
        {
            id = 0;
            _lookId = 0;
            Transform cam = GameInfo.CurCamera.transform;
            if (!Physics.Raycast(cam.position, cam.forward, out var rh, reach, 1 << PieceLayer, QueryTriggerInteraction.Ignore)) return;
            var root = rh.collider.attachedRigidbody ? rh.collider.attachedRigidbody.gameObject : rh.collider.gameObject;
            if (!_pieceIds.TryGetValue(root, out id) || !_states.TryGetValue(id, out var s)) { id = 0; return; }
            // Aiming at a turret's or a wall's foot hits the floor it stands on: the thing standing there is meant (the
            // playtest kept upgrading floors by mistake).
            if (s.Type == PieceType.Floor || s.Type == PieceType.Ramp)
                foreach (var col in Physics.OverlapSphere(rh.point + Vector3.up * 0.9f, 1.1f, 1 << PieceLayer, QueryTriggerInteraction.Ignore))
                {
                    var r2 = col.attachedRigidbody ? col.attachedRigidbody.gameObject : col.gameObject;
                    if (_pieceIds.TryGetValue(r2, out int id2) && _states.TryGetValue(id2, out var s2) && s2.Type != PieceType.Floor && s2.Type != PieceType.Ramp) { id = id2; break; }
                }
            _lookId = id;
        }

        public static string PieceName(PieceType t, int level)
        {
            if (t == PieceType.Wall) return L10n.T(level >= 3 ? "WallMetal" : level == 2 ? "WallStone" : "Wall");
            return L10n.T(t.ToString());
        }

        // ---------- build UI: piece cards above the game's hotbar, a card on the chosen piece, the look card ----------

        // The bar's order (T steps through it the same way): the fort's own pieces, then the turrets.
        public static readonly PieceType[] BarOrder =
        {
            PieceType.Wall, PieceType.Floor, PieceType.Ramp, PieceType.Spikes,
            PieceType.Gun, PieceType.Cannon, PieceType.Harpoon, PieceType.Mortar, PieceType.Tesla, PieceType.Flame, PieceType.Sniper, PieceType.Bubble, PieceType.Mender,
        };

        private RectTransform _cards;
        private readonly List<(UnityEngine.UI.Image box, UnityEngine.UI.Image ring, UnityEngine.UI.Image pic, TextMeshProUGUI name, TextMeshProUGUI cost)> _card =
            new List<(UnityEngine.UI.Image, UnityEngine.UI.Image, UnityEngine.UI.Image, TextMeshProUGUI, TextMeshProUGUI)>();
        private UnityEngine.UI.Image _warnBox, _infoBox, _infoPic, _infoLine, _lookBox, _lookPic;
        private TextMeshProUGUI _warnText;
        // The chosen piece: what it's for (the playtest didn't know what a bubble shield was for), its numbers, price, the keys.
        private TextMeshProUGUI _infoName, _infoPrice, _infoDesc, _infoStats, _infoKeysA, _infoKeysB;
        private (PieceType type, int moving, int coins) _infoDrawn;
        // The piece under the crosshair: name, branch, level, health, what U does and costs (the playtest couldn't see a turret's damage).
        private TextMeshProUGUI _lookName, _lookLv, _lookHp, _lookUp, _lookKeys;
        private Hud.Bar _lookBar;
        private (int id, int level, int branch, int hp, int max, int coins) _lookDrawn;
        private const float InfoH = 132f, LookH = 150f, LookText = 130f; // the two cards' heights; where the look card's text starts

        private bool EnsureBuildUi()
        {
            if (_cards) return true;
            if (!Hud.Font) return false;
            var root = Hud.Root;
            Vector2 C = new Vector2(0.5f, 0.5f), TL = new Vector2(0f, 1f);
            const float w = 98f, h = 116f, gap = 8f, groupGap = 22f; // a wider gap between the fort's pieces and the turrets
            float total = BarOrder.Length * w + (BarOrder.Length - 1) * gap + groupGap;
            var top = new Vector2(0.5f, 1f);
            _cards = Hud.Node(root, "BuildCards", new Vector2(0.5f, 0f), new Vector2(0f, 128f), new Vector2(total, h + 4f));
            for (int i = 0; i < BarOrder.Length; i++)
            {
                // A card per piece: its picture, its name, its price.
                var t = BarOrder[i];
                var box = Hud.Box(_cards, new Vector2(0f, 0f), new Vector2(i * (w + gap) + (Catalog.IsTurret(t) ? groupGap : 0f), 0f), new Vector2(w, h), Hud.Panel, 14f);
                var ring = Hud.Box(box.transform, C, Vector2.zero, new Vector2(w, h), Hud.Accent, 14f, "Ui_outline");
                var pic = Hud.Picture(box.transform, "Piece" + t, top, new Vector2(0f, -5f), new Vector2(56f, 56f));
                var name = Hud.Label(box.transform, "", 15f, top, new Vector2(0f, pic ? -62f : -8f), new Vector2(w - 8f, pic ? 20f : 44f), TextAlignmentOptions.Top);
                name.enableWordWrapping = !pic;
                name.enableAutoSizing = true; name.fontSizeMin = 11f; name.fontSizeMax = 15f;
                var cost = Hud.Label(box.transform, "", 20f, new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(w - 10f, 26f), TextAlignmentOptions.Bottom, Hud.Gold, bold: true);
                _card.Add((box, ring, pic, name, cost));
            }
            int fort = System.Array.FindIndex(BarOrder, Catalog.IsTurret); // the line between the two groups
            Hud.Box(_cards, Vector2.zero, new Vector2(fort * (w + gap) - gap + (gap + groupGap) / 2f, 14f), new Vector2(2f, 88f), new Color(1f, 1f, 1f, 0.22f), 0f, pivot: new Vector2(0.5f, 0f));

            _infoBox = Hud.Box(root, new Vector2(0.5f, 0f), new Vector2(0f, 262f), new Vector2(940f, InfoH), Hud.Panel, 16f);
            var ib = _infoBox.transform;
            _infoPic = Hud.PictureSlot(ib, new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(92f, 92f));
            _infoName = Hud.Label(ib, "", 24f, TL, new Vector2(118f, -12f), new Vector2(330f, 30f), TextAlignmentOptions.Left, bold: true);
            _infoPrice = Hud.Label(ib, "", 24f, TL, new Vector2(440f, -12f), new Vector2(160f, 30f), TextAlignmentOptions.Right, Hud.Gold, bold: true);
            _infoDesc = Hud.Label(ib, "", 17f, TL, new Vector2(118f, -46f), new Vector2(482f, 52f), TextAlignmentOptions.TopLeft);
            _infoDesc.enableAutoSizing = true; _infoDesc.fontSizeMin = 12f; _infoDesc.fontSizeMax = 17f;
            _infoStats = Hud.Label(ib, "", 16f, TL, new Vector2(118f, -102f), new Vector2(482f, 22f), TextAlignmentOptions.Left, Hud.Muted);
            _infoLine = Hud.Box(ib, TL, new Vector2(620f, -14f), new Vector2(1f, InfoH - 28f), new Color(1f, 1f, 1f, 0.12f), 0f, pivot: TL);
            _infoKeysA = Hud.Label(ib, "", 15f, TL, new Vector2(640f, -12f), new Vector2(150f, InfoH - 24f), TextAlignmentOptions.Left, Hud.Muted);
            _infoKeysB = Hud.Label(ib, "", 15f, TL, new Vector2(800f, -12f), new Vector2(130f, InfoH - 24f), TextAlignmentOptions.Left, Hud.Muted);
            foreach (var k in new[] { _infoKeysA, _infoKeysB }) { k.enableWordWrapping = false; k.lineSpacing = 30f; }
            _infoDrawn = ((PieceType)255, -1, -1);
            _warnBox = Hud.Box(root, new Vector2(0.5f, 0f), new Vector2(0f, 404f), new Vector2(400f, 42f), new Color(0.45f, 0.08f, 0.06f, 0.85f), 21f);
            _warnText = Hud.Label(_warnBox.transform, "", 22f, C, Vector2.zero, new Vector2(1200f, 42f), TextAlignmentOptions.Center, bold: true);

            _lookBox = Hud.Box(root, C, new Vector2(0f, -151f), new Vector2(620f, LookH), Hud.Panel, 16f);
            var lb = _lookBox.transform;
            _lookPic = Hud.PictureSlot(lb, new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(108f, 108f));
            _lookName = Wide(Hud.Label(lb, "", 25f, TL, new Vector2(LookText, -11f), new Vector2(0f, 32f), TextAlignmentOptions.Left, bold: true), 120f);
            _lookLv = Hud.Label(lb, "", 25f, new Vector2(1f, 1f), new Vector2(-18f, -11f), new Vector2(120f, 32f), TextAlignmentOptions.Right, Hud.Gold, bold: true);
            _lookBar = Hud.Bar.Make(lb, TL, new Vector2(LookText, -55f), new Vector2(340f, 12f), Hud.Good, TL);
            _lookHp = Hud.Label(lb, "", 16f, TL, new Vector2(LookText + 352f, -49f), new Vector2(200f, 22f), TextAlignmentOptions.Left, Hud.Muted);
            _lookUp = Wide(Hud.Label(lb, "", 19f, TL, new Vector2(LookText, -80f), new Vector2(0f, 28f), TextAlignmentOptions.Left), 18f);
            _lookKeys = Wide(Hud.Label(lb, "", 16f, TL, new Vector2(LookText, -114f), new Vector2(0f, 24f), TextAlignmentOptions.Left, Hud.Muted), 18f);
            return true;
        }

        // A look-card line from where it starts to `right` short of the card's right edge (the card's width follows its text).
        private static TextMeshProUGUI Wide(TextMeshProUGUI t, float right)
        {
            var rt = t.rectTransform;
            rt.anchorMax = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(-(rt.anchoredPosition.x + right), rt.sizeDelta.y);
            return t;
        }

        private static void FitPill(UnityEngine.UI.Image box, TextMeshProUGUI t, string text, float pad = 48f)
        {
            if (t.text == text) return;
            t.text = text;
            box.rectTransform.sizeDelta = new Vector2(t.GetPreferredValues(text).x + pad, box.rectTransform.sizeDelta.y);
        }

        private void LateUpdate()
        {
            HpTags();
            ToastTick();
            bool on = _building && EnsureBuildUi();
            bool lookOnly = !_building && _walkUp && EnsureBuildUi(); // walked up to a piece
            if (_cards) { _cards.gameObject.SetActive(on); _infoBox.gameObject.SetActive(on); }
            if (!on && !lookOnly) { if (_warnBox) { _warnBox.gameObject.SetActive(false); _lookBox.gameObject.SetActive(false); } return; }
            if (lookOnly) _warnBox.gameObject.SetActive(false);
            else BuildCards();
            LookCard();
        }

        private void BuildCards()
        {
            int coins = Siege.Instance.ViewCoins;
            for (int i = 0; i < _card.Count; i++)
            {
                var t = BarOrder[i];
                var (box, ring, pic, name, cost) = _card[i];
                int c = Perks.MyCost(Catalog.BuildCost(t));
                bool locked = !Loot.Instance.ClientVault.CanBuild(t);
                bool sel = t == _type, afford = coins >= c && !locked;
                ring.gameObject.SetActive(sel);
                box.color = sel ? new Color(0.22f, 0.16f, 0.07f, 0.92f) : Hud.Panel;
                box.rectTransform.anchoredPosition = new Vector2(box.rectTransform.anchoredPosition.x, sel ? 10f : 0f);
                name.text = PieceName(t, 1);
                name.color = afford ? Hud.Ink : Hud.Muted;
                name.fontStyle = sel ? FontStyles.Bold : FontStyles.Normal;
                cost.text = locked ? L10n.T("cardLocked") : "$" + c;
                cost.color = locked ? Hud.Muted : afford ? Hud.Gold : Hud.Bad;
                box.color = locked ? new Color(0.05f, 0.06f, 0.08f, 0.55f) : box.color;
                if (pic) pic.color = locked ? new Color(0.3f, 0.3f, 0.3f, 0.45f) : afford ? Color.white : new Color(1f, 1f, 1f, 0.55f);
            }
            InfoCard(coins);
            bool warn = _blockReason.Length > 0;
            _warnBox.gameObject.SetActive(warn);
            if (warn) FitPill(_warnBox, _warnText, _blockReason);
        }

        // The chosen piece: what it's for, its Lv1 numbers and price, the build keys. Carrying a piece: which one, and how to put it down.
        private void InfoCard(int coins)
        {
            var key = (_type, _moveId, coins);
            if (key == _infoDrawn) return;
            _infoDrawn = key;
            string keysA = "", keysB = "";
            if (_moveId != 0 && _states.TryGetValue(_moveId, out var m))
            {
                Hud.ShowPicture(_infoPic, PieceMenu.PictureOf(m));
                _infoName.text = PieceName(m.Type, m.Level) + "  " + PieceMenu.Paint(PieceMenu.Lv(m.Level), Hud.Gold);
                _infoPrice.text = "";
                _infoDesc.text = Hud.Keys(L10n.T("moveKeys"));
                _infoStats.text = "";
            }
            else
            {
                int cost = Perks.MyCost(Catalog.BuildCost(_type));
                Hud.ShowPicture(_infoPic, "Piece" + _type);
                _infoName.text = PieceName(_type, 1);
                _infoPrice.text = "$" + cost;
                _infoPrice.color = coins >= cost ? Hud.Gold : Hud.Bad;
                _infoDesc.text = L10n.T("pieceDesc" + _type);
                var lv1 = new PieceState { Type = _type, Level = 1, MaxHp = Perks.MaxHp(_type, 1) };
                _infoStats.text = string.Join("　", PieceMenu.Rows(lv1, 1, 0).Select(r => r.label + " " + r.now));
                keysA = Hud.Keys(L10n.T("buildKeysA"));
                keysB = Hud.Keys(L10n.T("buildKeysB"));
            }
            _infoKeysA.text = keysA;
            _infoKeysB.text = keysB;
            bool keys = keysA.Length > 0;
            _infoLine.gameObject.SetActive(keys);
            float inf = float.PositiveInfinity;
            float wa = keys ? _infoKeysA.GetPreferredValues(keysA, inf, inf).x : 0f, wb = keys ? _infoKeysB.GetPreferredValues(keysB, inf, inf).x : 0f;
            _infoKeysA.rectTransform.sizeDelta = new Vector2(wa + 4f, InfoH - 24f);
            _infoKeysB.rectTransform.anchoredPosition = new Vector2(640f + wa + 28f, -12f);
            _infoKeysB.rectTransform.sizeDelta = new Vector2(wb + 4f, InfoH - 24f);
            _infoBox.rectTransform.sizeDelta = new Vector2(keys ? 640f + wa + 28f + wb + 22f : 620f, InfoH);
        }

        // What's under the crosshair: picture, name and branch, level, health; what U does and costs; the other keys.
        private void LookCard()
        {
            if (_lookId == 0 || !_states.TryGetValue(_lookId, out var s)) { _lookBox.gameObject.SetActive(false); _lookDrawn = default; return; }
            _lookBox.gameObject.SetActive(true);
            int coins = Siege.Instance ? Siege.Instance.ViewCoins : 0;
            var key = (s.Id, s.Level, s.Branch, s.Hp, s.MaxHp, coins);
            if (key == _lookDrawn) return;
            _lookDrawn = key;
            string name = PieceName(s.Type, s.Level) + (s.Branch > 0 ? "  " + PieceMenu.Chip(PieceMenu.Tag(s.Branch) + " " + Catalog.BranchName(s.Type, s.Branch), Hud.Accent, 68) : "");
            string up = Hud.Keys(UpLine(s, coins)), keys = Hud.Keys(KeysLine(s));
            _lookName.fontSize = 25f; _lookUp.fontSize = 19f; _lookKeys.fontSize = 16f; // full size again (Hud.OneLine shrinks a line that didn't fit)
            _lookName.text = name; _lookUp.text = up; _lookKeys.text = keys;
            _lookLv.text = PieceMenu.Lv(s.Level);
            float f = s.MaxHp > 0 ? (float)s.Hp / s.MaxHp : 1f;
            _lookBar.Set(f, Hud.HealthColor(f));
            _lookHp.text = s.Hp + " / " + s.MaxHp;
            Hud.ShowPicture(_lookPic, PieceMenu.PictureOf(s));
            float inf = float.PositiveInfinity;
            float w = Mathf.Max(_lookName.GetPreferredValues(name, inf, inf).x + 120f, _lookUp.GetPreferredValues(up, inf, inf).x, _lookKeys.GetPreferredValues(keys, inf, inf).x);
            _lookBox.rectTransform.sizeDelta = new Vector2(Mathf.Max(560f, LookText + w + 22f), LookH);
        }

        // What U does next and what it costs: the branch or second split it opens, the perk it brings, or the main number it raises.
        private static string UpLine(PieceState s, int coins)
        {
            int cost = Perks.MyTowerUpgradeCost(Catalog.UpgradeCost(s.Type, s.Level));
            string price = coins >= cost ? PieceMenu.Paint("$" + cost, Hud.Gold) : PieceMenu.Paint(L10n.T("lookShort", cost, cost - coins), Hud.Bad);
            if (Catalog.HasBranches(s.Type) && s.Level == Catalog.BranchLevel - 1)
                return L10n.T("lookBranch", Catalog.BranchName(s.Type, 1), Catalog.BranchName(s.Type, 2)) + "　" + price;
            if (Catalog.HasSecondBranch(s.Type) && s.Level == Catalog.SecondBranchLevel - 1 && s.Branch > 0) // U opens the split there (Upgrade)
            {
                int first = Catalog.FirstOf(s.Branch);
                return L10n.T("lookSplit", Catalog.BranchName(s.Type, Catalog.BranchCode(first, 1)), Catalog.BranchName(s.Type, Catalog.BranchCode(first, 2))) + "　" + price;
            }
            string line = L10n.T("lookUpgrade", PieceMenu.Lv(s.Level + 1)) + "　" + price + "　";
            if (PieceMenu.PerkAt(s.Type, s.Level + 1, s.Branch) is string perk)
                return line + PieceMenu.Paint("· ★ " + L10n.T("lookPerk", perk), Hud.Gold);
            var rows = PieceMenu.Rows(s, s.Level + 1, s.Branch);
            var r = rows.Find(x => x.label == L10n.T("statDamage")); // damage where there is any, else the first number
            if (r.label == null) r = rows[0];
            return line + PieceMenu.Paint($"· {r.label} {r.now} →", Hud.Muted) + " <b>" + PieceMenu.Paint(r.next, Hud.Good) + "</b>";
        }

        private static string KeysLine(PieceState s)
        {
            string keys = L10n.T("lookDetails");
            if (s.Hp < s.MaxHp) keys += "　" + L10n.T("repairHint", Perks.MyCost(Catalog.RepairCost(s.Hp, s.MaxHp)));
            return keys + "　" + L10n.T("moveHint") + "　" + L10n.T("removeHint");
        }

        // ---------- over the field: damaged turrets' health and level; a line when your upgrade lands ----------

        // A tag over each damaged turret in sight, so a wave shows at a glance which one to repair (Owen's list, 5). Not over
        // the one whose look card is up: the card says it all.
        private class HpTag { public RectTransform Root; public CanvasGroup Group; public UnityEngine.UI.Image Back; public TextMeshProUGUI Lv; public Hud.Bar Bar; public int Level; }
        private readonly List<HpTag> _hpTags = new List<HpTag>();
        private const float TagFar = 55f, TagFull = 35f; // gone past TagFar metres, faded in from there to TagFull

        private void HpTags()
        {
            var cam = GameInfo.CurCamera;
            int n = 0;
            if (cam && Hud.Font && !Hud.ModalOpen && !PieceMenu.IsOpen && Siege.Instance && Siege.Instance.ViewActive)
            {
                Vector3 eye = cam.transform.position;
                int carded = _lookBox && _lookBox.gameObject.activeSelf ? _lookId : 0;
                foreach (var kv in _states)
                {
                    var s = kv.Value;
                    if (s.Hp >= s.MaxHp || s.Id == carded || !Catalog.IsTurret(s.Type)) continue;
                    Vector3 at = s.Pos + Vector3.up * (Catalog.MuzzleHeight(s.Type, s.Level) + 0.8f);
                    float dist = Vector3.Distance(eye, at);
                    if (dist > TagFar) continue;
                    Vector3 vp = cam.WorldToViewportPoint(at);
                    if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;
                    var tag = n < _hpTags.Count ? _hpTags[n] : NewHpTag();
                    n++;
                    if (!tag.Root.gameObject.activeSelf) tag.Root.gameObject.SetActive(true);
                    tag.Root.anchorMin = tag.Root.anchorMax = new Vector2(vp.x, vp.y);
                    tag.Root.localScale = Vector3.one * Mathf.Lerp(1f, 0.75f, Mathf.InverseLerp(10f, TagFar, dist));
                    tag.Group.alpha = Mathf.InverseLerp(TagFar, TagFull, dist);
                    if (tag.Level != s.Level)
                    {
                        tag.Level = s.Level;
                        tag.Lv.text = PieceMenu.Lv(s.Level);
                        float w = tag.Lv.GetPreferredValues(tag.Lv.text).x;
                        tag.Lv.rectTransform.sizeDelta = new Vector2(w + 2f, 20f);
                        tag.Back.rectTransform.sizeDelta = new Vector2(8f + w + 6f + 40f + 8f, 22f);
                    }
                    float f = s.MaxHp > 0 ? (float)s.Hp / s.MaxHp : 1f;
                    tag.Bar.Set(f, Hud.HealthColor(f));
                }
            }
            for (int i = n; i < _hpTags.Count; i++) if (_hpTags[i].Root.gameObject.activeSelf) _hpTags[i].Root.gameObject.SetActive(false);
        }

        private HpTag NewHpTag()
        {
            var mid = new Vector2(0.5f, 0.5f);
            var t = new HpTag { Root = Hud.Node(Hud.Root, "TurretHpTag", mid, Vector2.zero, new Vector2(10f, 10f)), Level = -1 };
            t.Root.SetAsFirstSibling(); // under the rest of the HUD
            t.Group = t.Root.gameObject.AddComponent<CanvasGroup>();
            t.Group.blocksRaycasts = false;
            t.Back = Hud.Box(t.Root, mid, Vector2.zero, new Vector2(100f, 22f), new Color(0.07f, 0.09f, 0.12f, 0.8f), 11f);
            t.Lv = Hud.Label(t.Back.transform, "", 13f, new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(40f, 20f), TextAlignmentOptions.Left, Hud.Gold, bold: true, pivot: new Vector2(0f, 0.5f));
            t.Bar = Hud.Bar.Make(t.Back.transform, new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(40f, 7f), Hud.Good, new Vector2(1f, 0.5f));
            _hpTags.Add(t);
            return t;
        }

        // Your upgrade landed: "Machine Gun reached Lv 7  ★ new perk: …", "Machine Gun chose [A Storm]  Lv 3" (Owen's list, 6).
        private readonly Dictionary<int, float> _askedUp = new Dictionary<int, float>(); // piece -> when this player asked to raise it
        private UnityEngine.UI.Image _toast, _toastPic;
        private TextMeshProUGUI _toastText;
        private CanvasGroup _toastGroup;
        private float _toastAt = -10f;
        private const float ToastFor = 2.4f;

        internal void AskedUp(int id) => _askedUp[id] = Time.time;

        private void UpToast(PieceState was, PieceState s)
        {
            if (!Hud.Font) return;
            if (!_toast)
            {
                _toast = Hud.Box(Hud.Root, new Vector2(0.5f, 0.5f), new Vector2(0f, -46f), new Vector2(400f, 52f), Hud.Panel, 26f); // under the crosshair, over the look card
                _toastGroup = _toast.gameObject.AddComponent<CanvasGroup>();
                _toastGroup.blocksRaycasts = false;
                _toastPic = Hud.PictureSlot(_toast.transform, new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(44f, 44f));
                _toastText = Hud.Label(_toast.transform, "", 22f, new Vector2(0f, 0.5f), new Vector2(60f, 0f), new Vector2(1200f, 40f), TextAlignmentOptions.Left, bold: true, pivot: new Vector2(0f, 0.5f));
            }
            string name = PieceName(s.Type, s.Level), lv = PieceMenu.Paint(PieceMenu.Lv(s.Level), Hud.Gold);
            string text = s.Branch != was.Branch && s.Branch > 0
                ? L10n.T("toastBranch", name, PieceMenu.Chip(PieceMenu.Tag(s.Branch) + " " + Catalog.BranchName(s.Type, s.Branch), Hud.Accent, 85)) + "　" + lv
                : L10n.T("toastUp", name, lv);
            for (int l = s.Level; l > was.Level; l--)
                if (PieceMenu.PerkAt(s.Type, l, s.Branch) is string perk) { text += "　" + PieceMenu.Paint("★ " + L10n.T("lookPerk", perk), Hud.Gold); break; }
            _toastText.fontSize = 22f;
            _toastText.text = text;
            Hud.ShowPicture(_toastPic, PieceMenu.PictureOf(s));
            float inf = float.PositiveInfinity;
            _toast.rectTransform.sizeDelta = new Vector2(_toastText.GetPreferredValues(text, inf, inf).x + 60f + 26f, 52f);
            _toastAt = Time.time;
        }

        // Pops in, holds, fades.
        private void ToastTick()
        {
            if (!_toast) return;
            float t = Time.time - _toastAt;
            bool on = t < ToastFor;
            if (_toast.gameObject.activeSelf != on) _toast.gameObject.SetActive(on);
            if (!on) return;
            _toastGroup.alpha = Mathf.Clamp01((ToastFor - t) / 0.4f);
            _toast.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, t / 0.12f);
        }

        // ---------- networking ----------

        private void EnsureRegistered()
        {
            var nm = InstanceFinder.NetworkManager;
            if (!nm || nm == _registeredOn) return;
            _registeredOn = nm;
            nm.ServerManager.RegisterBroadcast(Net.Safe<PiecePlaceRequest>(OnPlaceRequest));
            nm.ServerManager.RegisterBroadcast(Net.Safe<PieceRemoveRequest>(OnRemoveRequest));
            nm.ServerManager.RegisterBroadcast(Net.Safe<PieceUpgradeRequest>(OnUpgradeRequest));
            nm.ServerManager.RegisterBroadcast(Net.Safe<PieceMoveRequest>((c, r, ch) => OnMoveRequest(c, r, ch)));
            nm.ClientManager.RegisterBroadcast<PieceState>(OnPieceState);
            nm.SceneManager.OnClientLoadedStartScenes += OnClientLoaded;
            nm.ServerManager.OnServerConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) { _serverPieces.Clear(); _spent.Clear(); } };
            nm.ClientManager.OnClientConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) ClearLocalPieces(); };
            Log.LogInfo("Build system registered on NetworkManager.");
        }

        private void OnPlaceRequest(NetworkConnection conn, PiecePlaceRequest req, Channel ch)
        {
            var who = PlayerManager.Players.FirstOrDefault(p => p && p.Owner == conn);
            ServerPlace(req.Type, req.Pos, req.RotY, who ? who.ObjectId : -1);
        }

        // Who built each piece (turret kills are credited to them).
        private readonly Dictionary<int, int> _owner = new Dictionary<int, int>();
        public int OwnerOf(int pieceId) => _owner.TryGetValue(pieceId, out int o) ? o : -1;

        public bool ServerCanPlace(PieceType type, Vector3 pos) =>
            Siege.Instance.ServerActive && AllowedAt(pos) && Loot.Instance.Vault.CanBuild(type) && !_serverPieces.Values.Any(p => Overlap(p.Type, p.Pos, type, pos));

        // Host only. Returns false if the slot is taken or the team can't afford it. A finished construction site has paid already.
        public bool ServerPlace(PieceType type, Vector3 pos, float rotY, int ownerId = -1, int prepaid = -1)
        {
            if (!ServerCanPlace(type, pos)) return false;
            if (ownerId < 0 && Player.LocalPlayer) ownerId = Player.LocalPlayer.ObjectId;
            int cost = prepaid >= 0 ? prepaid : Perks.Cost(Catalog.BuildCost(type), PlayerManager.Players.FirstOrDefault(p => p && p.ObjectId == ownerId));
            if (prepaid < 0 && !Siege.Instance.ServerTrySpend(cost)) return false;
            int maxHp = Perks.MaxHp(type, 1);
            var state = new PieceState { Id = _nextId++, Type = type, Pos = pos, RotY = rotY, Hp = maxHp, MaxHp = maxHp, Level = 1 };
            _serverPieces[state.Id] = state;
            _spent[state.Id] = Siege.Instance.ServerActive ? cost : 0;
            InstanceFinder.ServerManager.Broadcast(state);
            RoundBuilt++;
            _owner[state.Id] = ownerId;
            Stats.Built(ownerId);
            AutoTest.Count("placed_" + type);
            return true;
        }

        public void ServerUpgrade(int id, int branch, bool repair = false) =>
            OnUpgradeRequest(null, new PieceUpgradeRequest { Id = id, Branch = branch, Repair = repair, FromLevel = _serverPieces.TryGetValue(id, out var p) ? p.Level : 0 }, Channel.Reliable);

        // The level this client sees a piece at (what an upgrade request says it upgrades from).
        public int LevelOf(int id) => _states.TryGetValue(id, out var s) ? s.Level : 0;

        private void OnUpgradeRequest(NetworkConnection conn, PieceUpgradeRequest req, Channel ch)
        {
            var who = conn == null ? Player.LocalPlayer : PlayerManager.Players.FirstOrDefault(p => p && p.Owner == conn);
            if (req.Repair && req.Id == -1) { ServerRepairAll(who, who ? who.ObjectId : 0); return; }
            if (!_serverPieces.TryGetValue(req.Id, out var s)) return;
            if (req.Repair)
            {
                if (s.Hp >= s.MaxHp || !Siege.Instance.ServerTrySpend(Perks.Cost(Catalog.RepairCost(s.Hp, s.MaxHp), who))) return;
                s.Hp = s.MaxHp;
                _serverPieces[s.Id] = s;
                InstanceFinder.ServerManager.Broadcast(s);
                AutoTest.Count("repairs");
                return;
            }
            if (req.FromLevel != s.Level) return; // someone else's upgrade got there first
            if (req.BranchOnly)
            {
                // A turret already past Lv6 without a second split (an old save) picks it once, free.
                if (s.Level < Catalog.SecondBranchLevel || !Catalog.HasSecondBranch(s.Type) || s.Branch > 2 || req.Branch < 3
                    || (s.Branch > 0 && Catalog.FirstOf(req.Branch) != Catalog.FirstOf(s.Branch))) return; // only the continuation of its own choice
                s.Branch = Catalog.BranchCode(s.Branch > 0 ? Catalog.FirstOf(s.Branch) : Catalog.FirstOf(req.Branch), Catalog.SecondOf(req.Branch));
                _serverPieces[s.Id] = s;
                InstanceFinder.ServerManager.Broadcast(s);
                AutoTest.Count("second_branch");
                return;
            }
            int cost = Perks.TowerUpgradeCost(Catalog.UpgradeCost(s.Type, s.Level), who); // no cap: past Lv3 it just keeps getting dearer
            if (!Siege.Instance.ServerTrySpend(cost)) return;
            if (Siege.Instance.ServerActive) _spent[s.Id] += cost;
            s.Level++;
            if (s.Level == Catalog.BranchLevel && Catalog.HasBranches(s.Type)) s.Branch = req.Branch == 2 ? 2 : 1;
            else if (s.Level == Catalog.SecondBranchLevel && Catalog.HasSecondBranch(s.Type) && req.Branch >= 3)
                s.Branch = Catalog.BranchCode(s.Branch > 0 ? Catalog.FirstOf(s.Branch) : Catalog.FirstOf(req.Branch), Catalog.SecondOf(req.Branch)); // the second split; a missing first branch is filled from the pick
            int newMax = Perks.MaxHp(s.Type, s.Level);
            s.Hp += newMax - s.MaxHp; // upgrading also repairs by the difference
            s.MaxHp = newMax;
            _serverPieces[s.Id] = s;
            InstanceFinder.ServerManager.Broadcast(s);
        }

        // A piece picked up and put down elsewhere: same rules as building there (on the island, in a round, nothing in the
        // way), free, keeps its level, branch and health.
        private bool OnMoveRequest(NetworkConnection conn, PieceMoveRequest req, Channel ch)
        {
            if (!_serverPieces.TryGetValue(req.Id, out var s) || !Siege.Instance.ServerActive) return false;
            if (float.IsNaN(req.Pos.x + req.Pos.y + req.Pos.z) || !AllowedAt(req.Pos)) return false;
            if (_serverPieces.Values.Any(p => p.Id != s.Id && Overlap(p.Type, p.Pos, s.Type, req.Pos))) return false;
            s.Pos = req.Pos;
            s.RotY = req.RotY;
            _serverPieces[s.Id] = s;
            InstanceFinder.ServerManager.Broadcast(s);
            AutoTest.Count("moved_" + s.Type);
            return true;
        }

        private void OnRemoveRequest(NetworkConnection conn, PieceRemoveRequest req, Channel ch)
        {
            if (!_serverPieces.TryGetValue(req.Id, out var state)) return;
            if (_spent.TryGetValue(req.Id, out int spent)) Siege.Instance.ServerAddCoins(spent / 2);
            Remove(state);
        }

        // Host: wipe the field between rounds.
        public void ServerClearAll()
        {
            foreach (var p in _serverPieces.Values.ToList()) Remove(p);
        }

        // Host: rebuild a saved run's fort. Fresh ids, no cost (the saved run already paid), HP/levels/branches preserved.
        public void ServerRestorePieces(IEnumerable<PieceState> saved)
        {
            ServerClearAll();
            int max = 0;
            foreach (var old in saved)
            {
                var s = old;
                s.Id = ++max;
                s.Removed = false;
                _serverPieces[s.Id] = s;
                _spent[s.Id] = 0;
                _owner[s.Id] = -1;
                InstanceFinder.ServerManager.Broadcast(s);
            }
            _nextId = max + 1;
            RoundBuilt = saved.Count();
        }

        // Destroyed by enemies (not taken down): a turret lost is told to everyone.
        private void Lost(PieceState state)
        {
            if (Catalog.IsTurret(state.Type)) Fx.Send(FxKind.PieceLost, state.Pos, state.Pos, 0, (int)state.Type, state.Level);
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
            dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * (1f - Turrets.ShieldAt(state.Pos))));
            state.Hp -= dmg;
            if (state.Hp <= 0) { Lost(state); AutoTest.Count("pieces_destroyed"); return true; }
            _serverPieces[id] = state;
            InstanceFinder.ServerManager.Broadcast(state);
            return true;
        }

        // Host only: damage one piece (a boss's move), or take it away whole (the kraken drags a turret into the sea).
        public void ServerDamagePiece(int id, int dmg)
        {
            if (!_serverPieces.TryGetValue(id, out var s)) return;
            s.Hp -= Mathf.Max(1, Mathf.RoundToInt(dmg * (1f - Turrets.ShieldAt(s.Pos))));
            if (s.Hp <= 0) { Lost(s); AutoTest.Count("pieces_destroyed"); return; }
            _serverPieces[id] = s;
            InstanceFinder.ServerManager.Broadcast(s);
        }
        public void ServerTakePiece(int id) { if (_serverPieces.TryGetValue(id, out var s)) { s.Hp = 0; Remove(s); AutoTest.Count("pieces_destroyed"); } }

        // Host only: damage every piece whose centre is within `radius` of `at` (explosions, water bolts).
        public void ServerDamageArea(Vector3 at, float radius, int dmg)
        {
            foreach (var p in _serverPieces.Values.Where(p => (p.Pos - at).sqrMagnitude <= radius * radius).ToList())
            {
                var s = p;
                s.Hp -= Mathf.Max(1, Mathf.RoundToInt(dmg * (1f - Turrets.ShieldAt(s.Pos))));
                if (s.Hp <= 0) { Lost(s); AutoTest.Count("pieces_destroyed"); continue; }
                _serverPieces[s.Id] = s;
                InstanceFinder.ServerManager.Broadcast(s);
            }
        }

        // Host: the First-aid toolbox. Returns the pieces it reached.
        public List<Vector3> ServerRepairArea(Vector3 at, float radius, float share)
        {
            var hit = new List<Vector3>();
            foreach (var p in _serverPieces.Values.Where(p => p.Hp < p.MaxHp && (p.Pos - at).sqrMagnitude <= radius * radius).ToList())
            {
                var s = p;
                s.Hp = Mathf.Min(s.MaxHp, s.Hp + Mathf.CeilToInt(s.MaxHp * share));
                _serverPieces[s.Id] = s;
                InstanceFinder.ServerManager.Broadcast(s);
                hit.Add(s.Pos);
            }
            return hit;
        }

        // Host: a repair tower's pulse: every damaged piece within `radius` but itself gets `hp` back. Returns where they are.
        // A repair tower's tick on one building: `share` of its own health back (Turrets.Mend picks the strongest tower).
        public bool ServerMend(int id, float share, out Vector3 at)
        {
            at = default;
            if (!_serverPieces.TryGetValue(id, out var s) || s.Hp >= s.MaxHp) return false;
            s.Hp = Mathf.Min(s.MaxHp, s.Hp + Mathf.Max(1, Mathf.CeilToInt(s.MaxHp * share)));
            _serverPieces[id] = s;
            InstanceFinder.ServerManager.Broadcast(s);
            at = s.Pos;
            return true;
        }

        // Pieces a walker has to break through (it walks over floors and ramps).
        public static bool IsWall(Collider col)
        {
            if (!col) return false;
            var parts = col.gameObject.name.Split('_');
            return parts.Length >= 2 && parts[0] == "FortressPiece" && System.Enum.TryParse(parts[1], out PieceType t)
                   && (t == PieceType.Wall || Catalog.IsTurret(t));
        }

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
                if (go)
                {
                    _pieceIds.Remove(go);
                    bool broken = _states.TryGetValue(s.Id, out var last) && last.Hp <= 0 || s.Hp <= 0;
                    StartCoroutine(Shatter(go, broken));
                }
                _pieces.Remove(s.Id);
                _states.Remove(s.Id);
                _askedUp.Remove(s.Id);
                return;
            }
            bool had = _states.TryGetValue(s.Id, out var prev);
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
            if (prev.Level != s.Level || prev.Branch != s.Branch)
            {
                if (Catalog.IsTurret(s.Type)) SetTurretLook(go, s.Type, s.Level, s.Branch); // first: its new barrels get the level's colours too
                if (s.Type == PieceType.Wall) SetWallModel(go, s.Level);
                else Models.SetLevelLook(go, s.Type, s.Level);
                if (s.Type == PieceType.Bubble || s.Type == PieceType.Mender) SetBubbleRing(go, s.Type, s.Level, s.Branch);
                SetRankStuds(go, s.Type, s.Level);
            }
            if (had && (prev.Pos != s.Pos || prev.RotY != s.RotY))
            {
                // Moved: off from the old spot, down on the new one.
                Fx.Send(FxKind.Dust, prev.Pos, prev.Pos);
                go.transform.position = s.Pos;
                go.transform.rotation = s.Type == PieceType.Ramp ? Quaternion.Euler(-45f, s.RotY, 0f) : Quaternion.Euler(0f, s.RotY, 0f);
                StartCoroutine(Pop(go.transform));
                Fx.SoundRandom("WoodItemImpact_V", 1, 3, s.Pos, 0.8f, AudioDistance.Short);
            }
            if (!had) { StartCoroutine(Pop(go.transform)); Fx.SoundRandom("WoodItemImpact_V", 1, 3, s.Pos, 0.8f, AudioDistance.Short); }
            else if (s.Level > prev.Level) { StartCoroutine(Pop(go.transform)); Fx.SoundRandom("MetallicItemImpact_V", 1, 2, s.Pos, 0.9f, AudioDistance.Short); Fx.Sound("Buy_01", s.Pos, 0.6f, AudioDistance.Short); }
            else if (s.Hp < prev.Hp) { StartCoroutine(Shake(go.transform, s.Id)); if (Time.time > _nextHitSound) { _nextHitSound = Time.time + 0.15f; Fx.SoundRandom("KnifeHit_0", 1, 4, s.Pos, 0.6f, AudioDistance.Short); } }
            else if (s.Hp > prev.Hp) Fx.Sound("Buy_02", s.Pos, 0.6f, AudioDistance.Short);
            if (had && (s.Level > prev.Level || s.Branch != prev.Branch) && _askedUp.TryGetValue(s.Id, out float asked))
            {
                _askedUp.Remove(s.Id);
                if (Time.time - asked < 5f) UpToast(prev, s); // (a refused one leaves a stale ask behind)
            }
            _states[s.Id] = s;
            Models.Tint(go, s.MaxHp > 0 ? Mathf.Clamp01((float)s.Hp / s.MaxHp) : 1f);
        }

        private float _nextHitSound;

        // New / upgraded: grow in from 70 %.
        private static System.Collections.IEnumerator Pop(Transform t)
        {
            for (float k = 0f; k < 1f && t; k += Time.deltaTime / 0.18f)
            {
                t.localScale = Vector3.one * Mathf.Lerp(0.7f, 1f, 1f - (1f - k) * (1f - k));
                yield return null;
            }
            if (t) t.localScale = Vector3.one;
        }

        // Hit: a short shudder (round wherever the piece stands now: it may have been moved meanwhile).
        private System.Collections.IEnumerator Shake(Transform t, int id)
        {
            Vector3 Home() => _states.TryGetValue(id, out var st) ? st.Pos : t.position;
            for (float k = 0f; k < 0.15f && t; k += Time.deltaTime)
            {
                t.position = Home() + Random.insideUnitSphere * 0.06f;
                yield return null;
            }
            if (t) t.position = Home();
        }

        // Destroyed: sink and shrink with a crash; removed by hand: just shrink.
        private static System.Collections.IEnumerator Shatter(GameObject go, bool broken)
        {
            if (broken) Fx.SoundRandom("ItemImpactHeavy_V", 1, 3, go.transform.position, 1f);
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
            Vector3 start = go.transform.position;
            for (float k = 0f; k < 1f && go; k += Time.deltaTime / (broken ? 0.4f : 0.2f))
            {
                go.transform.localScale = Vector3.one * (1f - k);
                if (broken) go.transform.position = start + Vector3.down * k * 0.8f + Random.insideUnitSphere * 0.05f;
                yield return null;
            }
            if (go) Destroy(go);
        }

        private void ClearLocalPieces()
        {
            foreach (var p in _pieces.Values) if (p) Destroy(p);
            _pieces.Clear();
            _states.Clear();
            _pieceIds.Clear();
        }
    }

    // Left/right click go to the held item (or bare fists); swallow them while building or with a fortress weapon out,
    // so placing or firing doesn't also swing or shoot.
    [HarmonyLib.HarmonyPatch(typeof(PlayerHolding), "PrimaryInput")]
    internal static class BlockPrimaryWhileBuilding { private static bool Prefix() => !BuildSystem.Building && !Weapons.Drawn && !Hud.ModalOpen; }

    [HarmonyLib.HarmonyPatch(typeof(PlayerHolding), "SecondaryInput")]
    internal static class BlockSecondaryWhileBuilding { private static bool Prefix() => !BuildSystem.Building && !Weapons.Drawn && !Hud.ModalOpen; }

    [HarmonyLib.HarmonyPatch(typeof(PlayerPunching), "PunchInput")]
    internal static class BlockPunchWhileBuilding { private static bool Prefix() => !BuildSystem.Building && !Weapons.Drawn && !Hud.ModalOpen; }

    // Hold-to-attack keeps punching from Update while the button is down (also when it went down before the weapon came out).
    [HarmonyLib.HarmonyPatch(typeof(PlayerPunching), "CanPunch")]
    internal static class NoHeldPunchWhileBuilding
    {
        private static bool Prefix(ref bool __result)
        {
            if (!BuildSystem.Building && !Weapons.Drawn) return true;
            __result = false;
            return false;
        }
    }
}
