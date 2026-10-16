using System.Collections.Generic;
using BepInEx.Configuration;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Serializing;
using FishNet.Transporting;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace HtfFortress
{
    // Host -> everyone: the core and the wave status shown on the HUD.
    public struct SiegeState : IBroadcast { public bool Active; public Vector3 CorePos; public int CoreHp; public int CoreMax; public int Wave; public int Remaining; }

    // The fortress core, and the walkers that charge it and break walls in the way.
    // All decisions run on the host; clients just draw the core and the HUD from SiegeState.
    public class Siege : MonoBehaviour
    {
        public static Siege Instance;
        public static ConfigEntry<int> CoreMaxHp;
        public static ConfigEntry<int> WalkerDamage;

        private const float CoreRadius = 0.8f;
        private const float CoreHitRange = CoreRadius + 1.3f;
        private const float AttackInterval = 1f;

        // host side
        private SiegeState _server;
        private SiegeState _lastSent;
        private float _nextSend;
        private readonly HashSet<RunningFish> _walkers = new HashSet<RunningFish>();
        private readonly Dictionary<RunningFish, float> _nextAttack = new Dictionary<RunningFish, float>();

        // every client
        private SiegeState _view;
        private GameObject _core;
        private TextMeshProUGUI _hud;
        private NetworkManager _registeredOn;

        private static readonly AccessTools.FieldRef<RunningFish, float> RunSpeed = AccessTools.FieldRefAccess<RunningFish, float>("_runSpeed");
        private static readonly AccessTools.FieldRef<Creature, bool> Grounded = AccessTools.FieldRefAccess<Creature, bool>("_isGrounded");

        static Siege()
        {
            GenericWriter<SiegeState>.SetWrite((w, v) =>
            {
                w.WriteBoolean(v.Active); w.WriteVector3(v.CorePos); w.WriteInt32(v.CoreHp); w.WriteInt32(v.CoreMax);
                w.WriteInt32(v.Wave); w.WriteInt32(v.Remaining);
            });
            GenericReader<SiegeState>.SetRead(r => new SiegeState
            {
                Active = r.ReadBoolean(), CorePos = r.ReadVector3(), CoreHp = r.ReadInt32(), CoreMax = r.ReadInt32(),
                Wave = r.ReadInt32(), Remaining = r.ReadInt32()
            });
        }

        private void Awake() => Instance = this;

        // ---------- host API (called by FortressPlugin) ----------

        public bool CoreDestroyed => _server.Active && _server.CoreHp <= 0;
        public Vector3 CorePos => _server.CorePos;

        public void ServerStart(Vector3 near)
        {
            Vector3 pos = near;
            if (Physics.Raycast(near + Vector3.up * 5f, Vector3.down, out var rh, 30f, GameInfo.LevelLayer)) pos = rh.point;
            _server = new SiegeState { Active = true, CorePos = pos, CoreHp = CoreMaxHp.Value, CoreMax = CoreMaxHp.Value };
            _walkers.Clear();
            _nextAttack.Clear();
            Send(true);
        }

        public void ServerStop()
        {
            _server.Active = false;
            _walkers.Clear();
            _nextAttack.Clear();
            Send(true);
        }

        public void ServerProgress(int wave, int remaining)
        {
            _server.Wave = wave;
            _server.Remaining = remaining;
        }

        public void RegisterWalker(Creature c)
        {
            if (c is RunningFish rf) _walkers.Add(rf);
        }

        private void Send(bool force)
        {
            if (!InstanceFinder.IsServerStarted) return;
            if (!force && (Time.time < _nextSend || _server.Equals(_lastSent))) return;
            _nextSend = Time.time + 0.2f;
            _lastSent = _server;
            InstanceFinder.ServerManager.Broadcast(_server);
        }

        // ---------- walker steering (host simulates the creatures) ----------

        // Returns true when we drove the fish this tick, so the game's own wandering is skipped.
        public bool Steer(RunningFish fish)
        {
            if (!_server.Active || !_walkers.Contains(fish)) return false;
            if (fish.IsDead) { _walkers.Remove(fish); return false; }
            Rigidbody rig = fish.Rig;
            if (fish.AttachedRod || rig.isKinematic || !Grounded(fish)) return true; // hold still until it lands

            Vector3 pos = fish.transform.position;
            Vector3 to = _server.CorePos - pos; to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 0.01f ? to / dist : fish.transform.forward;
            fish.transform.forward = dir;

            if (dist <= CoreHitRange) { Halt(rig); TryAttack(fish, null); return true; }

            int mask = 1 << BuildSystem.PieceLayer;
            if (Physics.SphereCast(pos + Vector3.up * 0.4f, 0.3f, dir, out var hit, 0.9f, mask, QueryTriggerInteraction.Ignore) && BuildSystem.IsWall(hit.collider))
            {
                Halt(rig);
                TryAttack(fish, hit.collider);
                return true;
            }

            Vector3 v = dir * RunSpeed(fish);
            v.y = rig.linearVelocity.y;
            rig.linearVelocity = v;
            rig.angularVelocity = Vector3.zero;
            return true;
        }

        private static void Halt(Rigidbody rig) => rig.linearVelocity = new Vector3(0f, rig.linearVelocity.y, 0f);

        private void TryAttack(RunningFish fish, Collider wall)
        {
            if (!InstanceFinder.IsServerStarted) return;
            _nextAttack.TryGetValue(fish, out float next);
            if (Time.time < next) return;
            _nextAttack[fish] = Time.time + AttackInterval;
            if (wall) BuildSystem.Instance.ServerDamage(wall, WalkerDamage.Value);
            else _server.CoreHp = Mathf.Max(0, _server.CoreHp - WalkerDamage.Value);
        }

        // ---------- per frame ----------

        private void Update()
        {
            EnsureRegistered();
            if (InstanceFinder.IsServerStarted) Send(false);
        }

        private void LateUpdate()
        {
            if (!_view.Active)
            {
                if (_core) Destroy(_core);
                if (_hud) _hud.gameObject.SetActive(false);
                return;
            }
            if (!_core)
            {
                _core = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                _core.name = "FortressCore";
                _core.layer = BuildSystem.PieceLayer;
                _core.transform.localScale = new Vector3(CoreRadius * 2f, 1.5f, CoreRadius * 2f);
                DontDestroyOnLoad(_core);
            }
            _core.transform.position = _view.CorePos + Vector3.up * 1.5f;
            float t = _view.CoreMax > 0 ? (float)_view.CoreHp / _view.CoreMax : 1f;
            var m = _core.GetComponent<Renderer>().material;
            var c = Color.Lerp(new Color(0.8f, 0.1f, 0.1f), new Color(1f, 0.8f, 0.2f), t);
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);

            if (!_hud) _hud = Hud.CreateText("SiegeStatus", new Vector2(0.5f, 1f), new Vector2(0f, -40f), 30f, TextAlignmentOptions.Top);
            if (_hud)
            {
                _hud.gameObject.SetActive(true);
                _hud.text = L10n.T("siegeHud", _view.Wave, _view.CoreHp, _view.CoreMax, _view.Remaining);
            }
        }

        // ---------- networking ----------

        private void EnsureRegistered()
        {
            var nm = InstanceFinder.NetworkManager;
            if (!nm || nm == _registeredOn) return;
            _registeredOn = nm;
            nm.ClientManager.RegisterBroadcast<SiegeState>((s, ch) => _view = s);
            nm.SceneManager.OnClientLoadedStartScenes += (conn, asServer) => { if (asServer && _server.Active) InstanceFinder.ServerManager.Broadcast(conn, _server); };
            nm.ClientManager.OnClientConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) _view = default; };
            nm.ServerManager.OnServerConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) { _server = default; _walkers.Clear(); } };
        }
    }

    [HarmonyPatch(typeof(RunningFish), "UpdateMovement")]
    internal static class RunningFishSteerPatch
    {
        private static bool Prefix(RunningFish __instance) => !(Siege.Instance && Siege.Instance.Steer(__instance));
    }
}
