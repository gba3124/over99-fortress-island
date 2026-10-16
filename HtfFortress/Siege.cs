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
    public struct SiegeState : IBroadcast { public bool Active; public Vector3 CorePos; public int CoreHp; public int CoreMax; public int Wave; public int Remaining; public int Coins; public bool Building; public int Countdown; public int ReadyTotal; public int ReadyIn; public int ReadyCountdown; public int BossHp; public int BossMax; }

    // The fortress core, and the walkers that charge it and break walls in the way.
    // All decisions run on the host; clients just draw the core and the HUD from SiegeState.
    public class Siege : MonoBehaviour
    {
        public static Siege Instance;
        public static ConfigEntry<int> CoreMaxHp;
        public static ConfigEntry<int> WalkerDamage;
        public static ConfigEntry<int> StartCoins;

        private const float CoreRadius = 0.8f;
        private const float CoreHitRange = CoreRadius + 1.3f;
        private const float AttackInterval = 1f;

        // host side
        private SiegeState _server;
        private SiegeState _lastSent;
        private float _nextSend;
        private int _bossMax;
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
                w.WriteInt32(v.Wave); w.WriteInt32(v.Remaining); w.WriteInt32(v.Coins); w.WriteBoolean(v.Building); w.WriteInt32(v.Countdown); w.WriteInt32(v.ReadyTotal); w.WriteInt32(v.ReadyIn); w.WriteInt32(v.ReadyCountdown); w.WriteInt32(v.BossHp); w.WriteInt32(v.BossMax);
            });
            GenericReader<SiegeState>.SetRead(r => new SiegeState
            {
                Active = r.ReadBoolean(), CorePos = r.ReadVector3(), CoreHp = r.ReadInt32(), CoreMax = r.ReadInt32(),
                Wave = r.ReadInt32(), Remaining = r.ReadInt32(), Coins = r.ReadInt32(), Building = r.ReadBoolean(), Countdown = r.ReadInt32(), ReadyTotal = r.ReadInt32(), ReadyIn = r.ReadInt32(), ReadyCountdown = r.ReadInt32(), BossHp = r.ReadInt32(), BossMax = r.ReadInt32()
            });
        }

        private void Awake() => Instance = this;

        // ---------- host API (called by FortressPlugin) ----------

        public bool CoreDestroyed => _server.Active && _server.CoreHp <= 0;
        public Vector3 CorePos => _server.CorePos;
        public bool ServerActive => _server.Active;
        public bool ViewActive => _view.Active;
        public int ViewCoins => _view.Coins;
        public int ServerCoins => _server.Coins;
        public SiegeState View => _view;

        public void ServerDamageCoreForTest(int dmg) => _server.CoreHp = Mathf.Max(0, _server.CoreHp - dmg);

        public void ServerReady(int total, int inCircle, int countdown)
        {
            _server.ReadyTotal = total; _server.ReadyIn = inCircle; _server.ReadyCountdown = countdown;
        }
        public int CoreHp => _server.CoreHp;

        // Outside fortress mode building is free (sandbox); inside it costs the team's coins.
        public bool ServerTrySpend(int cost)
        {
            if (!_server.Active) return true;
            if (_server.Coins < cost) return false;
            _server.Coins -= cost;
            Send(true);
            return true;
        }

        public void ServerAddCoins(int amount)
        {
            if (!_server.Active || amount <= 0) return;
            _server.Coins += amount;
        }

        public void ServerStart(Vector3 near)
        {
            // Only snap to ground right under the host's feet; a long ray from a dock or boat would hit the seabed.
            Vector3 pos = near;
            if (Physics.Raycast(near + Vector3.up * 2f, Vector3.down, out var rh, 6f, GameInfo.LevelLayer)) pos = rh.point;
            pos.y = Mathf.Max(pos.y, WaterManager.GetWaterHeight(pos));
            BuildSystem.Log.LogInfo($"Core placed at {pos} (host at {near}).");
            _server = new SiegeState { Active = true, CorePos = pos, CoreHp = CoreMaxHp.Value, CoreMax = CoreMaxHp.Value, Coins = StartCoins.Value, Building = true };
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

        public void ServerProgress(int wave, int remaining, bool building, int countdown)
        {
            _server.Wave = wave;
            _server.Remaining = remaining;
            _server.Building = building;
            _server.Countdown = countdown;
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
            Enemies.Instance.TryGet(fish, out var tag);
            var stats = Enemies.StatsOf(tag);
            Rigidbody rig = fish.Rig;
            if (tag.Kind == EnemyKind.Spitter) { SteerSpitter(fish, rig, stats); return true; }
            if (fish.AttachedRod || rig.isKinematic || !Grounded(fish)) return true; // hold still until it lands

            Vector3 pos = fish.transform.position;
            Vector3 to = _server.CorePos - pos; to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 0.01f ? to / dist : fish.transform.forward;
            fish.transform.forward = dir;

            float reach = CoreHitRange + 0.4f * (stats.Scale - 1f);
            if (dist <= reach) { Halt(rig); TryAttack(fish, null, tag, stats); return true; }

            // Overlap just ahead (a cast would miss a wall the fish is already pressed against).
            int mask = 1 << BuildSystem.PieceLayer;
            float r = 0.45f * Mathf.Max(1f, stats.Scale * 0.8f);
            foreach (var col in Physics.OverlapSphere(pos + dir * (0.6f * stats.Scale) + Vector3.up * 0.4f * stats.Scale, r, mask, QueryTriggerInteraction.Ignore))
            {
                if (!BuildSystem.IsWall(col)) continue;
                Halt(rig);
                TryAttack(fish, col, tag, stats);
                return true;
            }

            Vector3 v = dir * RunSpeed(fish) * stats.Speed * SlowFactor(fish);
            v.y = rig.linearVelocity.y;
            rig.linearVelocity = v;
            rig.angularVelocity = Vector3.zero;
            return true;
        }

        // Harpoons / spikes slow walkers for a while (strongest slow wins).
        private readonly Dictionary<Creature, (float factor, float until)> _slow = new Dictionary<Creature, (float, float)>();
        public void Slow(Creature c, float fraction, float seconds)
        {
            if (fraction <= 0f) return;
            float f = 1f - fraction;
            if (_slow.TryGetValue(c, out var cur) && cur.until > Time.time && cur.factor < f) f = cur.factor;
            _slow[c] = (f, Time.time + seconds);
        }
        private float SlowFactor(Creature c) => _slow.TryGetValue(c, out var s) && s.until > Time.time ? s.factor : 1f;

        private static void Halt(Rigidbody rig) => rig.linearVelocity = new Vector3(0f, rig.linearVelocity.y, 0f);

        private void TryAttack(RunningFish fish, Collider wall, EnemyTag tag, EnemyStats stats)
        {
            if (!InstanceFinder.IsServerStarted) return;
            _nextAttack.TryGetValue(fish, out float next);
            if (Time.time < next) return;
            _nextAttack[fish] = Time.time + AttackInterval;
            int dmg = Mathf.RoundToInt(WalkerDamage.Value * stats.Damage);
            if (tag.Kind == EnemyKind.Bomber) { Explode(fish, dmg); return; }
            if (wall) { BuildSystem.Instance.ServerDamage(wall, Mathf.RoundToInt(dmg * stats.WallMult)); AutoTest.Count("walker_hit_piece"); }
            else { DamageCore(dmg); AutoTest.Count("walker_hit_core"); }
        }

        public void DamageCore(int dmg) => _server.CoreHp = Mathf.Clamp(_server.CoreHp - dmg, 0, _server.CoreMax);

        // Bomber: blows up on whatever stopped it, hurting every piece (and the core) around.
        private void Explode(RunningFish fish, int dmg)
        {
            Vector3 at = fish.transform.position + Vector3.up * 0.5f;
            BuildSystem.Instance.ServerDamageArea(at, 3.5f, dmg);
            if ((at - _server.CorePos).sqrMagnitude < 4.5f * 4.5f) DamageCore(dmg / 2);
            Fx.Send(FxKind.Blast, at, at);
            fish.ServerChangeHp(100000);
            AutoTest.Count("bomber_exploded");
        }

        // Spitter: floats at the surface off the beach, lobs water bolts at the nearest building (or the core), and moves now and then.
        private readonly Dictionary<RunningFish, float> _nextSpit = new Dictionary<RunningFish, float>();
        private readonly Dictionary<RunningFish, float> _nextMove = new Dictionary<RunningFish, float>();

        private void SteerSpitter(RunningFish fish, Rigidbody rig, EnemyStats stats)
        {
            if (!rig.isKinematic) rig.isKinematic = true;
            Vector3 pos = fish.transform.position;
            float water = WaterManager.GetWaterHeight(pos);
            _nextMove.TryGetValue(fish, out float move);
            if (Time.time > move && FortressIsland.Instance.Exists)
            {
                _nextMove[fish] = Time.time + Random.Range(12f, 18f);
                if (move > 0f) pos = FortressIsland.Instance.SpitterSpot();
            }
            pos.y = water + 0.05f + Mathf.Sin(Time.time * 2f + fish.ObjectId) * 0.12f; // head and cannon above the surface
            rig.MovePosition(pos);

            Vector3 target = _server.CorePos + Vector3.up * 1.5f;
            float best = float.MaxValue;
            foreach (var p in BuildSystem.Instance.ServerPieces)
            {
                float d = (p.Pos - pos).sqrMagnitude;
                if (d < best && d < 30f * 30f) { best = d; target = p.Pos; }
            }
            Vector3 face = target - pos; face.y = 0f;
            if (face.sqrMagnitude > 0.01f) fish.transform.rotation = Quaternion.LookRotation(face);

            _nextSpit.TryGetValue(fish, out float spit);
            if (Time.time < spit) return;
            _nextSpit[fish] = Time.time + 3f;
            if ((target - pos).sqrMagnitude > 34f * 34f) return;
            Vector3 mouth = pos + Vector3.up * (0.8f * stats.Scale) + fish.transform.forward * 0.5f;
            Fx.Send(FxKind.Spit, mouth, target);
            StartCoroutine(SpitLands(target, Mathf.RoundToInt(WalkerDamage.Value * stats.Damage)));
            AutoTest.Count("spits");
        }

        private System.Collections.IEnumerator SpitLands(Vector3 at, int dmg)
        {
            yield return new WaitForSeconds(Fx.SpitFlight);
            if (!_server.Active) yield break;
            BuildSystem.Instance.ServerDamageArea(at, 1.6f, dmg);
            if ((at - _server.CorePos).sqrMagnitude < 2.5f * 2.5f) DamageCore(dmg);
        }

        // ---------- per frame ----------

        private void Update()
        {
            EnsureRegistered();
            if (InstanceFinder.IsServerStarted)
            {
                // Boss health for everyone's HUD.
                _server.BossHp = 0; _server.BossMax = 0;
                if (_server.Active)
                    foreach (var kv in Enemies.Instance.ServerAll)
                        if (kv.Value.Kind == EnemyKind.Boss && kv.Key._hp.Value > 0) { _server.BossHp = kv.Key._hp.Value; _server.BossMax = Mathf.Max(_bossMax, kv.Key._hp.Value); _bossMax = _server.BossMax; }
                if (_server.BossMax == 0) _bossMax = 0;
                Send(false);
            }
        }

        private void LateUpdate()
        {
            if (!_view.Active)
            {
                if (_core) Destroy(_core);
                // Between rounds: on the island, explain how to start.
                bool near = FortressIsland.Instance && Player.LocalPlayer && Player.LocalPlayer.Transform
                            && FortressIsland.Instance.OnIsland(Player.LocalPlayer.Transform.position, FortressIsland.IslandRadius + 6f);
                if (!near) { if (_hud) _hud.gameObject.SetActive(false); return; }
                if (!_hud) _hud = Hud.CreateText("SiegeStatus", new Vector2(0.5f, 1f), new Vector2(0f, -40f), 30f, TextAlignmentOptions.Top);
                if (!_hud) return;
                _hud.gameObject.SetActive(true);
                _hud.text = L10n.T("islandTitle") + "\n" + (_view.ReadyCountdown > 0
                    ? L10n.T("readyCountdown", _view.ReadyCountdown)
                    : L10n.T("readyPrompt", _view.ReadyIn, _view.ReadyTotal));
                return;
            }
            if (!_core)
            {
                _core = new GameObject("FortressCore");
                _core.layer = BuildSystem.PieceLayer;
                var col = _core.AddComponent<CapsuleCollider>();
                col.radius = CoreRadius; col.height = 3.3f; col.center = new Vector3(0f, 1.65f, 0f);
                Models.Attach("Core", _core.transform, new Vector3(CoreRadius * 2f, 3f, CoreRadius * 2f), new Vector3(0f, 1.5f, 0f));
                DontDestroyOnLoad(_core);
            }
            _core.transform.position = _view.CorePos;
            Models.Tint(_core, _view.CoreMax > 0 ? (float)_view.CoreHp / _view.CoreMax : 1f);

            if (!_hud) _hud = Hud.CreateText("SiegeStatus", new Vector2(0.5f, 1f), new Vector2(0f, -40f), 30f, TextAlignmentOptions.Top);
            if (_hud)
            {
                _hud.gameObject.SetActive(true);
                string phase = _view.Building ? L10n.T("buildPhase", _view.Countdown) : L10n.T("fightPhase");
                string boss = _view.BossMax > 0 ? "\n<color=#ff6050>" + L10n.T("bossHud", _view.BossHp, _view.BossMax) + "</color>" : "";
                _hud.text = L10n.T("siegeHud", _view.Wave, _view.CoreHp, _view.CoreMax, _view.Remaining, _view.Coins) + "\n" + phase + boss;
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
