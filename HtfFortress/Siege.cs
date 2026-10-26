using System.Collections.Generic;
using System.Linq;
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
using UnityEngine.UI;

namespace HtfFortress
{
    // Host -> everyone: the core and the wave status shown on the HUD.
    public struct SiegeState : IBroadcast { public bool Active; public Vector3 CorePos; public int CoreHp; public int CoreMax; public int Wave; public int Remaining; public int Coins; public bool Building; public int Countdown; public int ReadyTotal; public int ReadyIn; public int ReadyCountdown; public int BossHp; public int BossMax; public byte BossKind; public int BossCrashIn; public bool IslandOpen; public bool TestKeys; }
    // Client -> host: a friend pressed a test key (the host has them on).
    public struct TestKeyRequest : IBroadcast { public int Key; public bool Shift, Ctrl; }
    // Client -> host: start the next wave now (the N build-time skip works for every player, not just the host).
    public struct SkipBuildRequest : IBroadcast { public int By; }

    // The fortress core, and the walkers that charge it and break walls in the way.
    // All decisions run on the host; clients just draw the core and the HUD from SiegeState.
    public class Siege : MonoBehaviour
    {
        public static Siege Instance;
        public static ConfigEntry<int> CoreMaxHp;
        public static ConfigEntry<int> WalkerDamage;
        // Fortress prices and rewards are tuned in "coins"; they cost and pay the game's own dollars at this rate.
        public static ConfigEntry<float> MoneyScaleEntry;
        // 30 dollars a coin (it was 10): priced for players who have beaten the game's last boss (Owen: a team that walks in
        // with the main game's money mustn't buy the fort out at wave 1). A wallet from the end of the main game (about
        // $50,000-$100,000) is the first few waves' takings; everything the fort pays scales the same, so its own balance holds.
        public const float DefaultMoneyScale = 30f;
        public static float MoneyScale => MoneyScaleEntry?.Value ?? DefaultMoneyScale;
        public static int Money(int coins) => (int)System.Math.Round(System.Math.Min((double)coins * MoneyScale, int.MaxValue)); // the dearest levels stop at the money's ceiling

        private const float CoreRadius = 0.8f;
        private const float CoreHitRange = CoreRadius + 1.3f;
        private const float AttackInterval = 1f;

        // host side
        private SiegeState _server;
        private SiegeState _lastSent;
        private float _nextSend;
        private int _bossMax;
        private int _lastCoins; private bool _lastCoinsKnown; private string _coinPop = ""; private float _coinPopUntil;
        private int _lastReady; private bool _wasActive;
        private readonly HashSet<RunningFish> _walkers = new HashSet<RunningFish>();
        private readonly Dictionary<RunningFish, float> _nextAttack = new Dictionary<RunningFish, float>();
        // The one wall every walker breaks through (see UpdateBreach): a walled-in fort then comes down at one point.
        private int _breachId;
        private Vector3 _breachPos;
        private float _nextBreach;

        // every client
        private SiegeState _view;
        private GameObject _core;
        // top bar
        private RectTransform _top;
        private GameObject _roundGroup;
        private TextMeshProUGUI _waveText, _hpText, _enemyText, _coinText, _phaseText, _titleText, _bossText;
        private Hud.Bar _hpBar, _bossBar, _bossUnder;
        private TextMeshProUGUI _bossCount;
        private int _bossBars, _bossBarsOf;
        private float _barFlashUntil;
        // A boss's bars, the last one red: each a different colour from the one under it.
        private static readonly Color[] BarColours = { Hud.Bad, new Color(1f, 0.62f, 0.25f), new Color(0.95f, 0.42f, 0.78f), new Color(0.62f, 0.47f, 1f) };
        private Image _phaseBox, _bossBox;
        private int _lastCoreHp; private float _hpFlashUntil;
        private NetworkManager _registeredOn;

        private static readonly AccessTools.FieldRef<RunningFish, float> RunSpeed = AccessTools.FieldRefAccess<RunningFish, float>("_runSpeed");
        private static readonly AccessTools.FieldRef<Creature, bool> Grounded = AccessTools.FieldRefAccess<Creature, bool>("_isGrounded");

        static Siege()
        {
            GenericWriter<SiegeState>.SetWrite((w, v) =>
            {
                w.WriteBoolean(v.Active); w.WriteVector3(v.CorePos); w.WriteInt32(v.CoreHp); w.WriteInt32(v.CoreMax);
                w.WriteInt32(v.Wave); w.WriteInt32(v.Remaining); w.WriteInt32(v.Coins); w.WriteBoolean(v.Building); w.WriteInt32(v.Countdown); w.WriteInt32(v.ReadyTotal); w.WriteInt32(v.ReadyIn); w.WriteInt32(v.ReadyCountdown); w.WriteInt32(v.BossHp); w.WriteInt32(v.BossMax); w.WriteByte(v.BossKind); w.WriteInt32(v.BossCrashIn); w.WriteBoolean(v.IslandOpen); w.WriteBoolean(v.TestKeys);
            });
            GenericReader<SiegeState>.SetRead(r => new SiegeState
            {
                Active = r.ReadBoolean(), CorePos = r.ReadVector3(), CoreHp = r.ReadInt32(), CoreMax = r.ReadInt32(),
                Wave = r.ReadInt32(), Remaining = r.ReadInt32(), Coins = r.ReadInt32(), Building = r.ReadBoolean(), Countdown = r.ReadInt32(), ReadyTotal = r.ReadInt32(), ReadyIn = r.ReadInt32(), ReadyCountdown = r.ReadInt32(), BossHp = r.ReadInt32(), BossMax = r.ReadInt32(), BossKind = r.ReadByte(), BossCrashIn = r.ReadInt32(), IslandOpen = r.ReadBoolean(), TestKeys = r.ReadBoolean()
            });
            GenericWriter<TestKeyRequest>.SetWrite((w, v) => { w.WriteInt32(v.Key); w.WriteBoolean(v.Shift); w.WriteBoolean(v.Ctrl); });
            GenericReader<TestKeyRequest>.SetRead(r => new TestKeyRequest { Key = r.ReadInt32(), Shift = r.ReadBoolean(), Ctrl = r.ReadBoolean() });
            GenericWriter<SkipBuildRequest>.SetWrite((w, v) => w.WriteInt32(v.By));
            GenericReader<SkipBuildRequest>.SetRead(r => new SkipBuildRequest { By = r.ReadInt32() });
        }

        private void Awake() => Instance = this;

        // ---------- host API (called by FortressPlugin) ----------

        public bool CoreDestroyed => _server.Active && _server.CoreHp <= 0;
        public Vector3 CorePos => _server.CorePos;
        public bool ServerActive => _server.Active;
        public bool ViewActive => _view.Active;
        // The crew's money: the game's own (MoneyManager syncs it to everyone and saves it with the host's save).
        public int ViewCoins => MoneyManager.Instance ? MoneyManager.Money : _view.Coins;
        public int ServerCoins => MoneyManager.Instance ? MoneyManager.Instance._money.Value : _server.Coins;
        public SiegeState View => _view;
        public bool HasView { get; private set; } // a SiegeState has arrived since connecting

        public void ServerDamageCoreForTest(int dmg) => _server.CoreHp = Mathf.Max(0, _server.CoreHp - dmg);

        public void ServerReady(int total, int inCircle, int countdown)
        {
            _server.ReadyTotal = total; _server.ReadyIn = inCircle; _server.ReadyCountdown = countdown;
        }
        public int CoreHp => _server.CoreHp;
        public int CoreMax => _server.CoreMax;

        // Outside fortress mode building is free (sandbox); inside it costs the crew's money. The shops' goods (`always`) cost
        // it round or not (Owen: the stall "隨時都能購").
        public bool ServerTrySpend(int cost, bool always = false)
        {
            if (!_server.Active && !always) return true;
            if (ServerCoins < cost) return false;
            if (MoneyManager.Instance) MoneyManager.Instance._money.Value -= cost;
            else _server.Coins -= cost;
            Send(true);
            return true;
        }

        // Money from selling fish: real income, paid whether or not a round is on (ServerAddCoins only pays during one, so a
        // free sandbox build taken down outside a round can't refund money that was never spent).
        public void ServerPay(int amount)
        {
            if (amount <= 0) return;
            if (MoneyManager.Instance) MoneyManager.Instance._money.Value += amount;
            else if (_server.Active) _server.Coins += amount;
        }

        public void ServerAddCoins(int amount)
        {
            if (!_server.Active || amount <= 0) return;
            if (MoneyManager.Instance) MoneyManager.Instance._money.Value += amount;
            else _server.Coins += amount;
        }

        public void ServerStart(Vector3 near)
        {
            // Only snap to ground right under the host's feet; a long ray from a dock or boat would hit the seabed.
            Vector3 pos = near;
            if (Physics.Raycast(near + Vector3.up * 2f, Vector3.down, out var rh, 6f, GameInfo.LevelLayer)) pos = rh.point;
            pos.y = Mathf.Max(pos.y, WaterManager.GetWaterHeight(pos));
            BuildSystem.Log.LogInfo($"Core placed at {pos} (host at {near}).");
            _server = new SiegeState { Active = true, CorePos = pos, CoreHp = CoreShielded, CoreMax = CoreShielded, Building = true }; // no free money: you build with what you have
            ClearFish();
            Send(true);
        }

        // Restore a saved mid-run checkpoint (host only): the lighthouse keeps the HP it had when the wave checkpointed.
        public void ServerRestoreCore(int hp, int max)
        {
            _server.CoreMax = Mathf.Max(1, max);
            _server.CoreHp = Mathf.Clamp(hp, 1, _server.CoreMax);
            Send(true);
        }

        public void ServerSetCoins(int coins)
        {
            coins = Mathf.Max(0, coins);
            if (MoneyManager.Instance) MoneyManager.Instance._money.Value = coins;
            else _server.Coins = coins;
            Send(true);
        }

        public void ServerStop()
        {
            Bosses.Instance.ServerClear();
            _server.Active = false;
            ClearFish();
            Send(true);
        }

        // Everything remembered about this round's fish.
        private void ClearFish()
        {
            _walkers.Clear(); _nextAttack.Clear(); _progress.Clear(); _stuckTries.Clear(); _blockedSince.Clear(); _flank.Clear(); _slow.Clear(); _airborne.Clear(); _freezeNext.Clear();
            _carry.Clear(); _nextHop.Clear(); _nextHeal.Clear(); _hidden.Clear(); _dug.Clear();
            _nextSpit.Clear(); _nextMove.Clear(); _spitterBorn.Clear(); _decoys.Clear(); _bubbled.Clear(); _reeled.Clear();
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
            if (Time.time >= _nextFlow) { _nextFlow = Time.time + 0.75f; RebuildFlow(); }
            _server.IslandOpen = FortressIsland.HostUnlocked; // clients show the island when the host has it, whatever their own setting
            _server.TestKeys = FortressPlugin.Instance && FortressPlugin.Instance.TestKeysOn; // and the test keys when the host has them on
            if (!force && (Time.time < _nextSend || _server.Equals(_lastSent))) return;
            _nextSend = Time.time + 0.2f;
            _lastSent = _server;
            InstanceFinder.ServerManager.Broadcast(_server);
        }

        // ---------- walker steering (host simulates the creatures) ----------

        // Owen's playtest: the friend walled the whole island in and the fish "didn't want to come in" -- each hit whichever
        // wall it met, so the fort was nibbled at everywhere and broken nowhere. Now the host picks ONE wall for the whole
        // horde: the wall nearest any walker (shortest walk for the fish that get there first, and the one the others drift
        // to), lowest health among near-equals. Refreshed twice a second, or at once when that wall is gone.
        private bool _anyWalls;

        // A coarse flow field toward the lighthouse, rebuilt a few times a second when there are walls. Fish sample one
        // direction instead of each doing their own wall/target search; a fully walled fort falls back to attacking.
        private const float FlowCell = 2f, FlowRange = 46f;
        private Vector3 _flowOrigin;
        private int _flowN;
        private float[] _flowDist;
        private Vector3[] _flowDir;
        private bool _flowValid;
        private float _nextFlow;

        private int FlowCellOf(Vector3 p)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt((p.x - _flowOrigin.x) / FlowCell), 0, _flowN - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((p.z - _flowOrigin.z) / FlowCell), 0, _flowN - 1);
            return z * _flowN + x;
        }

        private void RebuildFlow()
        {
            var island = FortressIsland.Instance;
            if (island == null || !island.Exists) { _flowValid = false; return; }
            _flowValid = false;
            _flowOrigin = island.Center - new Vector3(FlowRange * 0.5f, 0f, FlowRange * 0.5f);
            _flowN = Mathf.CeilToInt(FlowRange / FlowCell) + 1;
            int n = _flowN * _flowN;
            if (_flowDist == null || _flowDist.Length != n) { _flowDist = new float[n]; _flowDir = new Vector3[n]; }
            for (int i = 0; i < n; i++) { _flowDist[i] = float.MaxValue; _flowDir[i] = Vector3.zero; }
            var blocked = new bool[n];
            var build = BuildSystem.Instance;
            if (build != null)
                foreach (var p in build.ServerPieces)
                {
                    if (p.Hp <= 0 || p.Type != PieceType.Wall) continue;
                    int cx = Mathf.Clamp(Mathf.RoundToInt((p.Pos.x - _flowOrigin.x) / FlowCell), 0, _flowN - 1);
                    int cz = Mathf.Clamp(Mathf.RoundToInt((p.Pos.z - _flowOrigin.z) / FlowCell), 0, _flowN - 1);
                    for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if (x >= 0 && x < _flowN && z >= 0 && z < _flowN) blocked[z * _flowN + x] = true;
                    }
                }
            int start = FlowCellOf(island.Center);
            blocked[start] = false;
            var queue = new Queue<int>();
            _flowDist[start] = 0; queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue(); int cx = cur % _flowN, cz = cur / _flowN;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = cz + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (nx < 0 || nx >= _flowN || nz < 0 || nz >= _flowN) continue;
                    int ni = nz * _flowN + nx;
                    if (blocked[ni] || _flowDist[ni] <= _flowDist[cur] + 1f) continue;
                    _flowDist[ni] = _flowDist[cur] + 1f; queue.Enqueue(ni);
                }
            }
            for (int z = 0; z < _flowN; z++) for (int x = 0; x < _flowN; x++)
            {
                int i = z * _flowN + x;
                if (_flowDist[i] == float.MaxValue) continue;
                float best = _flowDist[i]; Vector3 bd = Vector3.zero;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nx >= _flowN || nz < 0 || nz >= _flowN) continue;
                    int ni = nz * _flowN + nx;
                    if (_flowDist[ni] < best) { best = _flowDist[ni]; bd = new Vector3(dx, 0f, dz); }
                }
                _flowDir[i] = bd.sqrMagnitude > 0.01f ? bd.normalized : Vector3.zero;
            }
            _flowValid = true;
        }

        private Vector3 FlowDir(Vector3 pos)
        {
            if (!_flowValid || _flowDir == null) return Vector3.zero;
            return _flowDir[FlowCellOf(pos)];
        }
        private void UpdateBreach()
        {
            if (Time.time < _nextBreach && _breachId != 0) return;
            _nextBreach = Time.time + 0.5f;
            var build = BuildSystem.Instance;
            if (!build || _walkers.Count == 0) { _breachId = 0; _anyWalls = false; return; }
            _anyWalls = false;
            int best = 0; float bestD = float.MaxValue; int bestHp = int.MaxValue;
            Vector3 bestPos = default;
            foreach (var p in build.ServerPieces)
            {
                if (p.Hp <= 0 || p.Type != PieceType.Wall) continue;
                _anyWalls = true;
                float d = float.MaxValue;
                int stride = Mathf.Max(1, _walkers.Count / 64); // 400 walkers: sample 64, the nearest wall is still the same
                int seen = 0;
                foreach (var f in _walkers)
                {
                    if ((seen++ % stride) != 0 || !f || f.IsDead) continue;
                    float dd = (f.transform.position - p.Pos).sqrMagnitude;
                    if (dd < d) d = dd;
                }
                if (d == float.MaxValue) continue;
                if (d < bestD - 0.04f || (Mathf.Abs(d - bestD) <= 0.04f && p.Hp < bestHp))
                { bestD = d; bestHp = p.Hp; best = p.Id; bestPos = p.Pos; }
            }
            if (best != _breachId && FortressPlugin.Instance != null && FortressPlugin.Instance.VerboseLogs)
                BuildSystem.Log.LogInfo($"breach: walkers break wall {best} at {bestPos}");
            _breachId = best; _breachPos = bestPos;
        }

        private bool IsBreach(Collider col)
        {
            if (_breachId == 0 || !col) return false;
            var parts = col.gameObject.name.Split('_');
            return parts.Length >= 3 && int.TryParse(parts[2], out int id) && id == _breachId;
        }

        // Returns true when we drove the fish this tick, so the game's own wandering is skipped.
        public bool Steer(RunningFish fish)
        {
            if (FortressPlugin.Showcase.Contains(fish)) { fish.Rig.isKinematic = true; return true; } // test gallery: stand still
            if (!_server.Active || !_walkers.Contains(fish)) return false;
            if (FortressPlugin.Frozen) { Halt(fish.Rig); return true; } // test key F11
            if (fish.IsDead) { _walkers.Remove(fish); return false; }
            if (IsBubbled(fish) || _reeled.Contains(fish)) return true; // floating in a bubble, or on a hook line
            Enemies.Instance.TryGet(fish, out var tag);
            var stats = Enemies.StatsOf(tag);
            Rigidbody rig = fish.Rig;
            if (tag.Kind == EnemyKind.Spitter) { SteerSpitter(fish, rig, stats); return true; }
            if (_hidden.Contains(fish)) return true; // a digger underground
            // These don't need solid ground under them.
            if (tag.Kind == EnemyKind.Thief && ThiefEscaped(fish)) return true;
            if (tag.Kind == EnemyKind.Healer) HealAround(fish);
            if (!Grounded(fish) && !rig.isKinematic && !fish.AttachedRod)
            {
                // Afloat or caught on something for 8 s (never landing, so never "stuck" below): put back on the beach toward the lighthouse.
                if (!_airborne.TryGetValue(fish, out float since)) _airborne[fish] = since = Time.time;
                if (Time.time - since > 8f)
                {
                    Vector3 at = fish.transform.position, toCore = _server.CorePos - at; toCore.y = 0f;
                    Vector3 hop = at + toCore.normalized * Mathf.Min(6f, toCore.magnitude * 0.5f);
                    if (Physics.Raycast(hop + Vector3.up * 20f, Vector3.down, out var land, 40f, GameInfo.LevelLayer)) hop = land.point + Vector3.up * (0.3f + 0.3f * stats.Scale);
                    rig.position = hop; fish.transform.position = hop; rig.velocity = Vector3.zero;
                    _airborne.Remove(fish);
                    AutoTest.Count("unstuck_afloat");
                }
                return true;
            }
            _airborne.Remove(fish);
            if (fish.AttachedRod || rig.isKinematic) return true; // hold still until it lands
            if (!EnemyCatalog.IsBoss(tag.Kind) && Weapons.Instance && Weapons.Instance.ServerPull(fish, rig)) return true; // in a whirlpool
            if (tag.Kind == EnemyKind.Thief) { SteerThief(fish, rig, stats); return true; }

            Vector3 pos = fish.transform.position;
            // Simulation LOD: 15 Hz near the camera, 7.5 Hz at 35 m, 3.75 Hz at 70 m. Between ticks the rigidbody keeps
            // the last velocity, so movement stays smooth while 400 fish stop running a full steering query every frame.
            var lodCam = GameInfo.CurCamera;
            Vector3 lodEye = lodCam ? lodCam.transform.position : _server.CorePos;
            float lodD2 = (pos - lodEye).sqrMagnitude;
            int lodStep = lodD2 > 70f * 70f ? 16 : lodD2 > 35f * 35f ? 8 : 4;
            if (((Time.frameCount + fish.ObjectId) & (lodStep - 1)) != 0) return true;
            // Stuck on a step, rock or tree (no progress and no attack for 6 s): hop it 2.5 m on toward the lighthouse.
            if (!_progress.TryGetValue(fish, out var prog) || (pos - prog.pos).sqrMagnitude > 0.5f * 0.5f || SlowFactor(fish) <= 0f)
            {
                _progress[fish] = prog = (pos, Time.time);
                _stuckTries[fish] = 0;
            }
            else if (Time.time - prog.time > 2.5f)
            {
                // Stuck on a wreck, a rock, a wall corner or another fish. The first tries sidestep around it; a wreck is
                // wider than a fish, so later tries move farther sideways instead of only trying the same centre line.
                _stuckTries.TryGetValue(fish, out int tries); _stuckTries[fish] = ++tries;
                Vector3 toCore = _server.CorePos - pos; toCore.y = 0f;
                if (toCore.sqrMagnitude < 0.01f) toCore = fish.transform.forward;
                Vector3 side = Vector3.Cross(Vector3.up, toCore.normalized);
                float lateral = tries <= 1 ? 2.5f : tries == 2 ? 4.5f : 6.5f;
                Vector3 hop = pos;
                bool landed = false;
                foreach (int sign in new[] { 1, -1 })
                {
                    foreach (float step in new[] { 2.5f, 4.5f, 7f })
                    {
                        Vector3 p = pos + toCore.normalized * Mathf.Min(step, toCore.magnitude * 0.7f) + side * (sign * lateral);
                        if (!Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out var g, 40f, GameInfo.LevelLayer)) continue;
                        if (g.point.y < WaterManager.GetWaterHeight(g.point) + 0.2f) continue;
                        hop = g.point + Vector3.up * (0.3f + 0.3f * stats.Scale);
                        landed = true;
                        break;
                    }
                    if (landed) break;
                }
                if (!landed) return true; // no ground anywhere nearby: leave it to the airborne recovery
                rig.position = hop; fish.transform.position = hop; rig.linearVelocity = Vector3.zero;
                _progress[fish] = (hop, Time.time);
                _blockedSince.Remove(fish); _flank.Remove(fish);
                if (FortressPlugin.Instance != null && FortressPlugin.Instance.VerboseLogs)
                    BuildSystem.Log.LogInfo($"unstuck {tag.Kind} from {pos} to {hop} (2.5 s, try {tries}, sideways {lateral})");
                AutoTest.Count("unstuck");
                return true;
            }
            // Players who come close get chased and bitten (bombers just blow up on them).
            Player prey = Prey(pos, tag.Kind);
            if (prey && tag.Kind == EnemyKind.Bomber && (prey.Transform.position - pos).sqrMagnitude < 1.8f * 1.8f)
            {
                Explode(fish, Mathf.RoundToInt(WalkerDamage.Value * stats.Damage), atBuilding: false);
                return true;
            }
            bool decoyed = DecoyNear(pos, out var duck);
            Vector3 coreTo = _server.CorePos - pos; coreTo.y = 0f;
            float coreDist = coreTo.magnitude;
            Vector3 target = prey && tag.Kind != EnemyKind.Bomber ? prey.Transform.position : _server.CorePos;
            if (decoyed) { target = duck; prey = null; }
            Vector3 to = target - pos; to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 0.01f ? to / dist : fish.transform.forward;
            if (!prey && !decoyed && _flowValid)
            {
                Vector3 flow = FlowDir(pos);
                if (flow.sqrMagnitude > 0.01f) dir = flow;
            }
            fish.transform.forward = dir;
            if (decoyed && dist <= 1.5f + 0.5f * (stats.Scale - 1f))
            {
                // Pecking at a rubber duck. A bomber blows itself up on it.
                Halt(rig);
                if (tag.Kind == EnemyKind.Bomber) Explode(fish, Mathf.RoundToInt(WalkerDamage.Value * stats.Damage), atBuilding: false);
                AutoTest.Count("decoyed");
                return true;
            }

            if (prey && tag.Kind != EnemyKind.Bomber)
            {
                if (dist <= 1.3f + 0.5f * (stats.Scale - 1f)) { Halt(rig); Bite(fish, prey, stats); return true; }
            }
            else
            {
                float reach = CoreHitRange + 0.4f * (stats.Scale - 1f);
                if (coreDist <= reach) { Halt(rig); TryAttack(fish, null, tag, stats); return true; }
            }

            // Flanking: after being held at a wall for 3 s, walk sideways along it for a while looking for a way round.
            bool flanking = _flank.TryGetValue(fish, out var fl) && Time.time < fl.until;
            if (flanking) dir = fl.dir;
            fish.transform.forward = dir;

            Collider wall = _anyWalls ? WallAhead(pos, dir, stats) : null;
            if (wall && tag.Kind == EnemyKind.Digger && !_dug.Contains(fish)) { StartCoroutine(Burrow(fish, rig, dir)); return true; }
            // A walled-in fort used to leave fish hammering whatever wall each of them happened to meet (or nothing at all):
            // they now all break the one wall UpdateBreach picked. If the way to it is clear, walk there; at it, hit it.
            if (wall && _breachId != 0 && !IsBreach(wall) && !decoyed && prey == null)
            {
                Vector3 bd = _breachPos - pos; bd.y = 0f;
                if (bd.sqrMagnitude > 0.04f)
                {
                    Vector3 bdir = bd.normalized;
                    Collider bw = WallAhead(pos, bdir, stats);
                    if (bw && IsBreach(bw))
                    {
                        fish.transform.forward = bdir;
                        Halt(rig);
                        TryAttack(fish, bw, tag, stats);
                        AutoTest.Count("breach_hit");
                        return true;
                    }
                    if (!bw)
                    {
                        fish.transform.forward = bdir;
                        Vector3 bv = bdir * RunSpeed(fish) * stats.Speed * SlowFactor(fish);
                        bv.y = rig.linearVelocity.y;
                        rig.linearVelocity = bv;
                        rig.angularVelocity = Vector3.zero;
                        return true;
                    }
                }
            }
            if (wall)
            {
                if (flanking) { _flank.Remove(fish); _blockedSince.Remove(fish); } // flank blocked too: just smash through
                else
                {
                    if (!_blockedSince.TryGetValue(fish, out float since)) _blockedSince[fish] = since = Time.time;
                    if (Time.time - since > 3f && tag.Kind != EnemyKind.Bomber && tag.Kind != EnemyKind.Tank && !EnemyCatalog.IsBoss(tag.Kind))
                    {
                        float side = (fish.ObjectId & 1) == 0 ? 1f : -1f;
                        _flank[fish] = (Quaternion.Euler(0f, 80f * side, 0f) * dir, Time.time + 2.5f);
                        _blockedSince.Remove(fish);
                        AutoTest.Count("flanks");
                        return true;
                    }
                }
                Halt(rig);
                TryAttack(fish, wall, tag, stats);
                return true;
            }
            if (!flanking) _blockedSince.Remove(fish);

            Vector3 v = dir * RunSpeed(fish) * stats.Speed * SlowFactor(fish);
            v.y = rig.linearVelocity.y;
            rig.linearVelocity = v;
            rig.angularVelocity = Vector3.zero;
            return true;
        }

        // ---------- active gear on the host: decoy duck, shockwave ----------

        private readonly List<(Vector3 pos, float until)> _decoys = new List<(Vector3, float)>();

        public void ServerDecoy(Vector3 pos, float seconds)
        {
            if (Physics.Raycast(pos + Vector3.up * 4f, Vector3.down, out var g, 10f, GameInfo.LevelLayer)) pos = g.point;
            _decoys.Add((pos, Time.time + seconds));
            Fx.Send(FxKind.Decoy, pos, pos, 0, Mathf.RoundToInt(seconds));
        }

        private bool DecoyNear(Vector3 pos, out Vector3 at)
        {
            _decoys.RemoveAll(d => Time.time > d.until);
            at = default;
            float best = 15f * 15f;
            bool found = false;
            foreach (var d in _decoys)
            {
                float dd = (d.pos - pos).sqrMagnitude;
                if (dd < best) { best = dd; at = d.pos; found = true; }
            }
            return found;
        }

        public int ServerShockwave(Vector3 at, float radius, float stun, int dmg, Player by = null)
        {
            int n = 0;
            foreach (var fish in _walkers.ToList())
            {
                if (!fish || fish.IsDead) continue;
                Vector3 d = fish.transform.position - at; d.y = 0f;
                if (d.sqrMagnitude > radius * radius) continue;
                Vector3 to = fish.transform.position + (d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward) * 3f;
                if (Physics.Raycast(to + Vector3.up * 5f, Vector3.down, out var g, 10f, GameInfo.LevelLayer)) to = g.point + Vector3.up * 0.4f;
                if (!fish.Rig.isKinematic) { fish.Rig.position = to; fish.transform.position = to; }
                Slow(fish, 1f, stun);
                if (by) Stats.HandHit(fish, by); // a kill by your hammer is yours
                fish.ServerChangeHp(dmg);
                n++;
            }
            if (n > 0) Fx.Send(FxKind.Blast, at, at);
            return n;
        }

        // ---------- fortress weapons on the host: bubble, hook line, shove, interrupt ----------

        // Bubble gun: the fish floats helpless for a few seconds and takes half again from everything. Bosses are only slowed.
        private class Bubble { public float Until, Rise; public System.Action<Vector3> Pop; }
        private readonly Dictionary<Creature, Bubble> _bubbled = new Dictionary<Creature, Bubble>();
        public bool IsBubbled(Creature c) => c && _bubbled.TryGetValue(c, out var b) && Time.time < b.Until;
        public float BubbleRiseOf(Creature c) => c && _bubbled.TryGetValue(c, out var b) ? b.Rise : 0f;

        // Owen: "一直射到泡泡越浮越高，刷新破掉的時間，傷害也會添加". Each shot on a bubbled fish keeps it up, floats it
        // higher and stacks its burst; the bubble pops for everything that was put in it.
        public void ServerBubble(Creature c, float seconds, float rise, System.Action<Vector3> onPop)
        {
            if (!(c is Creature fish) || fish.IsDead || !fish.Rig || _reeled.Contains(c)) return;
            if (Enemies.Instance.TryGet(c, out var tag) && EnemyCatalog.IsBoss(tag.Kind)) { Slow(c, 0.5f, seconds); return; }
            if (_bubbled.TryGetValue(c, out var b))
            {
                b.Until = Time.time + seconds;
                b.Rise = Mathf.Max(b.Rise, rise);
                if (onPop != null) b.Pop = onPop;
                AutoTest.Count("bubbled");
                return;
            }
            _bubbled[c] = new Bubble { Until = Time.time + seconds, Rise = Mathf.Max(1.4f, rise), Pop = onPop };
            StartCoroutine(Float(fish));
            AutoTest.Count("bubbled");
        }

        private System.Collections.IEnumerator Float(Creature fish)
        {
            var rig = fish.Rig;
            bool wasKinematic = rig.isKinematic;
            rig.isKinematic = true;
            Vector3 start = fish.transform.position;
            for (float t = 0f; fish && !fish.IsDead && IsBubbled(fish); t += Time.deltaTime) // a round ending clears the bubbles
            {
                float h = _bubbled.TryGetValue(fish, out var b) ? b.Rise : 1.4f;
                rig.MovePosition(start + Vector3.up * (Mathf.Min(1f, t * 2f) * h + Mathf.Sin(t * 3f) * 0.12f));
                Synced(fish);
                yield return null;
            }
            _bubbled.TryGetValue(fish, out var bubble);
            _bubbled.Remove(fish);
            if (!fish) yield break;
            if (!wasKinematic) { rig.isKinematic = false; rig.linearVelocity = Vector3.zero; } // a fish killed up there drops too
            if (!fish.IsDead) bubble?.Pop?.Invoke(fish.transform.position);
        }

        // Hook (and the conch's shove): drag a fish to `to` over `seconds`; it does nothing else meanwhile. It stops short of
        // the first wall or turret on the way, so nobody reels fish into the fort. `onWater`: a spitter stays on the surface.
        private readonly HashSet<Creature> _reeled = new HashSet<Creature>();

        public void ServerPull(Creature c, Vector3 to, float seconds, bool onWater = false)
        {
            if (!(c is Creature fish) || fish.IsDead || !fish.Rig || _reeled.Contains(c) || IsBubbled(c)) return;
            Vector3 from = fish.transform.position;
            to = StopAtWall(from, to);
            if (onWater) to.y = WaterManager.GetWaterHeight(to);
            else if (Loot.Ground(to + Vector3.up * 4f, 12f, out var g)) to = g + Vector3.up * 0.4f;
            StartCoroutine(Reel(fish, from, to, seconds));
        }

        private System.Collections.IEnumerator Reel(Creature fish, Vector3 from, Vector3 to, float seconds)
        {
            _reeled.Add(fish);
            var rig = fish.Rig;
            bool wasKinematic = rig.isKinematic;
            rig.isKinematic = true;
            for (float t = 0f; t < seconds && fish && !fish.IsDead; t += Time.deltaTime)
            {
                rig.MovePosition(Vector3.Lerp(from, to, t / seconds) + Vector3.up * Mathf.Sin(t / seconds * Mathf.PI) * 1.2f);
                Synced(fish);
                yield return null;
            }
            _reeled.Remove(fish);
            if (!fish) yield break;
            if (!fish.IsDead) { rig.position = to; fish.transform.position = to; Synced(fish); }
            if (!wasKinematic) { rig.isKinematic = false; rig.linearVelocity = Vector3.zero; }
        }

        // The game sends a creature's position only while its physics velocity is over 0.1 m/s, so a fish carried kinematically
        // (bubble, hook line, burrow, spitter, whirlpool spin) could look frozen to friends: flag it as moving each frame.
        private static System.Action<RigidbodySync, bool> _setSending;
        internal static void Synced(Item fish)
        {
            if (!fish || !fish.RigidbodySync) return;
            if (_setSending == null)
            {
                var set = typeof(RigidbodySync).GetProperty("IsSendingUpdates")?.GetSetMethod(true);
                _setSending = set != null ? (System.Action<RigidbodySync, bool>)System.Delegate.CreateDelegate(typeof(System.Action<RigidbodySync, bool>), set) : (s, v) => { };
            }
            _setSending(fish.RigidbodySync, true);
        }

        private static Vector3 StopAtWall(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.01f) return to;
            foreach (var h in Physics.RaycastAll(from + Vector3.up * 0.5f, d / len, len, 1 << BuildSystem.PieceLayer, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                if (BuildSystem.IsWall(h.collider)) return from + d / len * Mathf.Max(0f, h.distance - 0.7f);
            return to;
        }

        // Conch: shove a fish along the ground (not a spitter out in the deep).
        public void ServerPush(Creature c, Vector3 push)
        {
            if (!c || push.sqrMagnitude < 0.01f || (Enemies.Instance.TryGet(c, out var tag) && tag.Kind == EnemyKind.Spitter)) return;
            ServerPull(c, c.transform.position + new Vector3(push.x, 0f, push.z), 0.25f);
        }

        // Conch: a blast of sound breaks the move a boss is winding up (Bosses).
        public void Interrupt(Creature c) => Bosses.Instance.Interrupt(c);

        // ---------- the newer kinds ----------

        // Thief: no fighting. Grab the nearest corpse (or coins from the fish stall if there are none), then run for the sea.
        // Walls don't stop it, it hops them. Killed on the way, it drops what it carried.
        private readonly Dictionary<Creature, int> _carry = new Dictionary<Creature, int>();
        private readonly Dictionary<RunningFish, float> _nextHop = new Dictionary<RunningFish, float>();

        // Off the beach or in the water with its loot: gone, loot and all.
        private bool ThiefEscaped(RunningFish fish)
        {
            if (!_carry.TryGetValue(fish, out int lost)) return false;
            Vector3 pos = fish.transform.position, outward = pos - FortressIsland.Instance.Center; outward.y = 0f;
            if (outward.magnitude < 26f && pos.y > WaterManager.GetWaterHeight(pos) + 0.05f) return false;
            _carry.Remove(fish);
            _walkers.Remove(fish);
            fish.DestroyItem();
            if (lost > 0) Fx.Announce("thiefEscaped", lost);
            AutoTest.Count("thief_escaped");
            return true;
        }

        public int ServerTakeCarried(Creature c) { if (!c || !_carry.TryGetValue(c, out int v)) return 0; _carry.Remove(c); return v; }

        private void SteerThief(RunningFish fish, Rigidbody rig, EnemyStats stats)
        {
            var island = FortressIsland.Instance;
            Vector3 pos = fish.transform.position, target;
            if (_carry.ContainsKey(fish))
            {
                // Out through the nearest beach gap (the way they came in), not over the rocks.
                Vector3 outward = pos - island.Center; outward.y = 0f;
                float best = float.MaxValue; Vector3 exit = outward.normalized;
                foreach (float g in FortressIsland.GapAngles)
                {
                    Vector3 d = new Vector3(Mathf.Cos(g * Mathf.Deg2Rad), 0f, Mathf.Sin(g * Mathf.Deg2Rad));
                    float ang = Vector3.Angle(d, outward);
                    if (ang < best) { best = ang; exit = d; }
                }
                // Head for the gap's mouth first, then straight out to sea.
                Vector3 mouth = island.Center + exit * 17f;
                target = Vector3.Dot(outward, exit) > 16f && Vector3.Angle(outward, exit) < 20f ? island.Center + exit * 34f : mouth;
            }
            else
            {
                var corpse = FortressNet.Instance.ServerNearestCorpse(pos, 45f);
                target = corpse ? corpse.transform.position : island.CounterPos;
                if (corpse && (target - pos).sqrMagnitude < 1.5f * 1.5f)
                {
                    _carry[fish] = FortressNet.Instance.ServerTakeCorpse(corpse);
                    Fx.Send(FxKind.Scoop, target, pos);
                    Fx.Announce("thiefStole", _carry[fish]);
                    AutoTest.Count("thief_stole");
                    return;
                }
                if (!corpse && (target - pos).sqrMagnitude < 2.4f * 2.4f)
                {
                    int steal = Mathf.Min(ServerCoins, Money(20 + 2 * _server.Wave));
                    _carry[fish] = steal > 0 && ServerTrySpend(steal) ? steal : 0;
                    if (_carry[fish] > 0) { Fx.Announce("thiefCoins", steal); AutoTest.Count("thief_stole"); }
                    return;
                }
            }
            Vector3 to = target - pos; to.y = 0f;
            Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : fish.transform.forward;
            fish.transform.forward = dir;
            _nextHop.TryGetValue(fish, out float hopAt);
            if (Time.time >= hopAt && WallAhead(pos, dir, stats))
            {
                _nextHop[fish] = Time.time + 1.2f;
                Vector3 over = pos + dir * 2.6f;
                if (Physics.Raycast(over + Vector3.up * 6f, Vector3.down, out var g, 12f, GameInfo.LevelLayer)) over = g.point + Vector3.up * 0.4f;
                rig.position = over; fish.transform.position = over;
                Fx.Send(FxKind.Dust, pos, pos);
                AutoTest.Count("thief_hops");
                return;
            }
            float load = _carry.ContainsKey(fish) ? 0.6f : 1f; // weighed down by the loot: catchable
            Vector3 v = dir * RunSpeed(fish) * stats.Speed * SlowFactor(fish) * load;
            v.y = rig.linearVelocity.y;
            rig.linearVelocity = v;
            rig.angularVelocity = Vector3.zero;
        }

        // Healer: every 2.5 s, every hurt fish within 6 m gets 12% of its health back.
        private readonly Dictionary<RunningFish, float> _nextHeal = new Dictionary<RunningFish, float>();

        private void HealAround(RunningFish fish)
        {
            _nextHeal.TryGetValue(fish, out float next);
            if (Time.time < next) return;
            _nextHeal[fish] = Time.time + 2.5f;
            Vector3 at = fish.transform.position;
            foreach (var c in FortressPlugin.Alive)
            {
                if (!c || c == fish || c._hp.Value <= 0 || (c.transform.position - at).sqrMagnitude > 6f * 6f || Weapons.IsPoisoned(c)) continue; // poison stops healing
                if (!FortressPlugin.Instance.MaxHpOf.TryGetValue(c, out int max) || c._hp.Value >= max) continue;
                c.ServerChangeHp(-Mathf.Min(max - c._hp.Value, Mathf.CeilToInt(max * 0.12f)));
                Fx.Send(FxKind.Heal, at + Vector3.up * 0.8f, c.transform.position + Vector3.up * 0.6f);
                AutoTest.Count("healed");
            }
        }

        // Digger: meets a wall, goes under it (turrets can't see it down there) and comes up on the other side.
        private readonly HashSet<Creature> _hidden = new HashSet<Creature>();
        private readonly HashSet<RunningFish> _dug = new HashSet<RunningFish>();
        public bool IsHidden(Creature c) => _hidden.Contains(c);
        public void Hide(Creature c, bool hidden) { if (hidden) _hidden.Add(c); else _hidden.Remove(c); } // a boss under the ground or the sea: turrets can't see it

        private System.Collections.IEnumerator Burrow(RunningFish fish, Rigidbody rig, Vector3 dir)
        {
            _dug.Add(fish);
            _hidden.Add(fish);
            rig.isKinematic = true;
            Vector3 start = fish.transform.position;
            Fx.Send(FxKind.Dust, start, start);
            AutoTest.Count("digger_burrowed");
            Vector3 down = start + Vector3.down * 1.8f, end = start + dir * 5.5f;
            if (Physics.Raycast(end + Vector3.up * 6f, Vector3.down, out var g, 12f, GameInfo.LevelLayer)) end = g.point + Vector3.up * 0.4f;
            for (float t = 0f; t < 2.4f && fish && !fish.IsDead; t += Time.deltaTime)
            {
                float k = t / 2.4f;
                Vector3 p = k < 0.2f ? Vector3.Lerp(start, down, k / 0.2f) : k < 0.85f ? Vector3.Lerp(down, end + Vector3.down * 1.8f, (k - 0.2f) / 0.65f) : Vector3.Lerp(end + Vector3.down * 1.8f, end, (k - 0.85f) / 0.15f);
                rig.MovePosition(p);
                Synced(fish);
                yield return null;
            }
            _hidden.Remove(fish);
            if (!fish || fish.IsDead) yield break;
            Fx.Send(FxKind.Dust, end, end);
            rig.isKinematic = false;
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

        // Legendary Frost Core: 6 % a turret hit, but a wall of turrets rolled that hundreds of times a second and bosses then
        // never moved again. At most one full freeze per target every 4 s; bosses are slowed to half speed instead (never a
        // full stop). Other slows (bubbles, harpoons, the Frost Core gear itself) are unchanged.
        public const float FreezeCooldown = 4f;
        private readonly Dictionary<Creature, float> _freezeNext = new Dictionary<Creature, float>();
        public bool TryFreeze(Creature c)
        {
            if (!c || c.IsDead) return false;
            if (_freezeNext.TryGetValue(c, out float next) && Time.time < next) return false;
            _freezeNext[c] = Time.time + FreezeCooldown;
            bool boss = Enemies.Instance.TryGet(c, out var tag) && EnemyCatalog.IsBoss(tag.Kind);
            bool wet = Weapons.IsWet(c);
            Slow(c, boss ? 0.5f : 1f, wet ? 2.6f : 1.5f); // wet fish freeze much longer (Owen: elemental reactions)
            return true;
        }
        public float SlowOf(Creature c) => SlowFactor(c);
        public bool IsFrozen(Creature c) => c && SlowOf(c) <= 0.05f; // held still by Frost Core / a bubble
        private float SlowFactor(Creature c) =>
            (_slow.TryGetValue(c, out var s) && s.until > Time.time ? s.factor : 1f)
            * (FortressIsland.Instance && FortressIsland.Instance.InPool(c.transform.position) ? PoolSlow : 1f); // knee-deep in a tide pool
        public const float PoolSlow = 0.55f;

        private readonly Dictionary<RunningFish, float> _blockedSince = new Dictionary<RunningFish, float>();
        private readonly Dictionary<RunningFish, (Vector3 dir, float until)> _flank = new Dictionary<RunningFish, (Vector3, float)>();

        // Overlap just ahead (a cast would miss a wall the fish is already pressed against). NonAlloc: this runs for every
        // walker every physics frame, and an allocating OverlapSphere there was the host's steadiest source of garbage
        // (Mono's GC is stop-the-world, so the whole server -- and so every client's world -- hitched when it ran).
        private static readonly Collider[] WallAheadHits = new Collider[8];
        private static Collider WallAhead(Vector3 pos, Vector3 dir, EnemyStats stats)
        {
            int mask = 1 << BuildSystem.PieceLayer;
            float r = 0.45f * Mathf.Max(1f, stats.Scale * 0.8f);
            int n = Physics.OverlapSphereNonAlloc(pos + dir * (0.6f * stats.Scale) + Vector3.up * 0.4f * stats.Scale, r, WallAheadHits, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (BuildSystem.IsWall(WallAheadHits[i])) return WallAheadHits[i];
            return null;
        }

        private readonly Dictionary<RunningFish, (Vector3 pos, float time)> _progress = new Dictionary<RunningFish, (Vector3, float)>();
        private readonly Dictionary<RunningFish, int> _stuckTries = new Dictionary<RunningFish, int>();
        private readonly Dictionary<RunningFish, float> _airborne = new Dictionary<RunningFish, float>(); // since when it hasn't touched ground

        private static void Halt(Rigidbody rig) => rig.linearVelocity = new Vector3(0f, rig.linearVelocity.y, 0f);

        // Bites on buildings also keep up with the fish's health from wave 20 on (Owen: 怪打建築加上後期係數): towers grow
        // x1.25 health a level, and a bite that grew only +8% a wave left a tower of the wave's level standing through 41 bites
        // at wave 10 but 963 at wave 100. With this it's 55-90 from wave 20 on (x1 to 20, x3.2 at 50, x17 at 100). The
        // lighthouse and players keep the plain bite: neither grows with the wave.
        public static float Late(int wave) => Mathf.Pow(Mathf.Max(1f, FortressPlugin.HpMult(wave) / FortressPlugin.HpMult(20)), 0.6f);

        private void TryAttack(RunningFish fish, Collider wall, EnemyTag tag, EnemyStats stats)
        {
            if (!InstanceFinder.IsServerStarted) return;
            _nextAttack.TryGetValue(fish, out float next);
            if (Time.time < next) return;
            _nextAttack[fish] = Time.time + AttackInterval;
            _progress[fish] = (fish.transform.position, Time.time); // attacking counts as progress
            int dmg = Mathf.RoundToInt(WalkerDamage.Value * stats.Damage * (1f + 0.08f * Mathf.Max(0, _server.Wave - 1))); // hits harder each wave
            if (tag.Kind == EnemyKind.Bomber) { Explode(fish, dmg); return; }
            if (wall)
            {
                int bite = Mathf.RoundToInt(dmg * stats.WallMult * Late(_server.Wave));
                BuildSystem.Instance.ServerDamage(wall, bite);
                AutoTest.Count("walker_hit_piece");
                if (Perks.TeamLegend(Gear.Rebar)) { fish.ServerChangeHp(Mathf.Max(1, Mathf.RoundToInt(bite * 0.3f))); AutoTest.Count("legend_thorns"); Perks.Proc(Gear.Rebar, fish.transform.position); } // legendary: walls bite back
            }
            else { DamageCore(Mathf.Max(1, dmg / 2)); AutoTest.Count("walker_hit_core"); } // the lighthouse is sturdier than a wall
        }

        // ---------- going after players ----------

        private static float AggroRange(EnemyKind k) => k == EnemyKind.Charger || EnemyCatalog.IsBoss(k) ? 7f : k == EnemyKind.Tank ? 4f : 5f;

        // Players enemies can go after (the balance test turns this off to measure turrets against the core alone).
        private static IEnumerable<Player> Targets => AutoTest.IgnorePlayers ? Enumerable.Empty<Player>() : PlayerManager.AlivePlayers;

        private static Player Prey(Vector3 pos, EnemyKind kind)
        {
            float r = AggroRange(kind), best = r * r;
            Player prey = null;
            foreach (var p in Targets)
            {
                if (!p || !p.Transform) continue;
                float d = (p.Transform.position - pos).sqrMagnitude;
                if (d < best && Mathf.Abs(p.Transform.position.y - pos.y) < 3f) { best = d; prey = p; }
            }
            return prey;
        }

        private void Bite(RunningFish fish, Player prey, EnemyStats stats)
        {
            _nextAttack.TryGetValue(fish, out float next);
            if (Time.time < next) return;
            _nextAttack[fish] = Time.time + 1.2f;
            _progress[fish] = (fish.transform.position, Time.time);
            int dmg = PlayerDamage(Mathf.RoundToInt(12f * stats.Damage));
            HurtPlayer(prey, dmg, fish.transform.forward * 4f + Vector3.up * 2f);
            if (Perks.Legend(prey, Gear.SharkArmor)) { fish.ServerChangeHp(25); AutoTest.Count("legend_biteback"); Perks.Proc(Gear.SharkArmor, fish.transform.position); } // legendary: bite back
        }

        // Player damage scales with the wave like the enemies do, but stays under their (fixed) 100 HP: a late bite hurts.
        private int PlayerDamage(int baseDmg) => Mathf.Min(80, Mathf.RoundToInt(baseDmg * (1f + 0.08f * Mathf.Max(0, _server.Wave - 1))));

        public static void HurtPlayer(Player p, int dmg, Vector3 force)
        {
            if (!p || !Server.Instance) return;
            dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * (1f - Perks.Of(p, Gear.SharkArmor)) * (Loot.Instance && Loot.Instance.Vault != null ? Loot.Instance.Vault.HealthTakenMult : 1f)));
            Server.Instance.HitPlayer(p, dmg, force, p.Transform.position, 3);
            AutoTest.Count("player_hits");
        }

        private static void HurtPlayersAround(Vector3 at, float radius, int dmg)
        {
            foreach (var p in Targets.ToList())
                if (p && p.Transform && (p.Transform.position - at).sqrMagnitude <= radius * radius)
                    HurtPlayer(p, dmg, (p.Transform.position - at).normalized * 6f + Vector3.up * 3f);
        }

        private static int CoreShielded => Mathf.RoundToInt(CoreMaxHp.Value * (1f + Perks.Team(Gear.CoreShield)) * (Loot.Instance && Loot.Instance.Vault != null ? Loot.Instance.Vault.CoreMult : 1f));

        // A bubble shield by the lighthouse covers it like any other building. (Negative = healing, used by the auto-test.)
        public void DamageCore(int dmg)
        {
            if (dmg > 0) dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * (1f - Turrets.ShieldAt(_server.CorePos))));
            _server.CoreHp = Mathf.Clamp(_server.CoreHp - dmg, 0, _server.CoreMax);
            _lastCoreHit = Time.time;
        }
        private float _lastCoreHit, _nextRegen;

        // A tenth of the lighthouse back, at its door: dearer per point than a wall (1 coin per 4 health).
        public static int CoreRepairCost(int coreMax) => Money(Mathf.Max(1, Mathf.CeilToInt(coreMax * 0.1f / 4f)));
        public void ServerRepairCore(Player who)
        {
            if (!_server.Active || _server.CoreHp >= _server.CoreMax || !ServerTrySpend(Perks.Cost(CoreRepairCost(_server.CoreMax), who))) return;
            ServerHealCore(0.1f);
            Fx.Send(FxKind.Heal, _server.CorePos + Vector3.up * 6f, _server.CorePos + Vector3.up * 2f);
            AutoTest.Count("core_repaired");
        }

        // Host: the lighthouse mended by `share` of its health (the field repair kit, the repair at its door); how much.
        public int ServerHealCore(float share)
        {
            if (!_server.Active || _server.CoreHp >= _server.CoreMax) return 0;
            int before = _server.CoreHp;
            _server.CoreHp = Mathf.Min(_server.CoreMax, _server.CoreHp + Mathf.CeilToInt(_server.CoreMax * share));
            return _server.CoreHp - before;
        }

        // Legendary lighthouse shield: left alone for 5 s, the lighthouse mends 1% a second.
        private void CoreRegen()
        {
            if (!_server.Active || Time.time < _nextRegen || Time.time - _lastCoreHit < 5f || _server.CoreHp >= _server.CoreMax || !Perks.TeamLegend(Gear.CoreShield)) return;
            _nextRegen = Time.time + 1f;
            _server.CoreHp = Mathf.Min(_server.CoreMax, _server.CoreHp + Mathf.CeilToInt(_server.CoreMax * 0.01f));
            AutoTest.Count("legend_regen");
            Perks.Proc(Gear.CoreShield, _server.CorePos + Vector3.up * 4f);
        }

        // Bomber: blows up on whatever stopped it. On a wall or the lighthouse every piece (and the core) around takes it; on a
        // person or a decoy duck only the people round it do (Owen: 「塔和地板卡到人」: one that ran into a player on the fort
        // blew up the floor and the tower they stood by).
        private void Explode(RunningFish fish, int dmg, bool atBuilding = true)
        {
            Vector3 at = fish.transform.position + Vector3.up * 0.5f;
            if (atBuilding)
            {
                BuildSystem.Instance.ServerDamageArea(at, 3.5f, Mathf.RoundToInt(dmg * Late(_server.Wave)));
                if ((at - _server.CorePos).sqrMagnitude < 4.5f * 4.5f) DamageCore(dmg / 2);
            }
            HurtPlayersAround(at, 3.5f, PlayerDamage(35));
            Fx.Send(FxKind.Blast, at, at);
            // Gone in the blast: no coins, no body, no loot (killing it before it gets there is what pays).
            FortressPlugin.Alive.Remove(fish);
            _walkers.Remove(fish);
            fish.DestroyItem();
            AutoTest.Count("bomber_exploded");
        }

        // Spitter: floats at the surface off the beach, lobs water bolts at the nearest building (or the core), and moves now and then.
        private readonly Dictionary<RunningFish, float> _nextSpit = new Dictionary<RunningFish, float>();
        private readonly Dictionary<RunningFish, float> _nextMove = new Dictionary<RunningFish, float>();

        private readonly Dictionary<RunningFish, float> _spitterBorn = new Dictionary<RunningFish, float>();
        private const float SpitterStay = 75f;

        private void SteerSpitter(RunningFish fish, Rigidbody rig, EnemyStats stats)
        {
            if (!rig.isKinematic) rig.isKinematic = true;
            // After a while it dives back into the deep (no reward), so one unreachable spitter can't stall a wave forever.
            if (!_spitterBorn.TryGetValue(fish, out float born)) _spitterBorn[fish] = born = Time.time;
            if (Time.time - born > SpitterStay) { _walkers.Remove(fish); _spitterBorn.Remove(fish); fish.DestroyItem(); AutoTest.Count("spitter_retreated"); return; }
            Vector3 pos = fish.transform.position;
            float water = WaterManager.GetWaterHeight(pos);
            bool stunned = SlowFactor(fish) <= 0f; // hooked or blasted: no moving, no spitting for a moment
            _nextMove.TryGetValue(fish, out float move);
            if (Time.time > move && FortressIsland.Instance.Exists && !stunned)
            {
                _nextMove[fish] = Time.time + Random.Range(12f, 18f);
                if (move > 0f) pos = FortressIsland.Instance.SpitterSpot();
            }
            pos.y = water + 0.05f + Mathf.Sin(Time.time * 2f + fish.ObjectId) * 0.12f; // head and cannon above the surface
            rig.MovePosition(pos);
            Synced(fish);
            if (stunned) return;

            Vector3 target = _server.CorePos + Vector3.up * 1.5f;
            float best = float.MaxValue;
            foreach (var p in BuildSystem.Instance.ServerPieces)
            {
                float d = (p.Pos - pos).sqrMagnitude;
                if (d < best && d < 30f * 30f) { best = d; target = p.Pos; }
            }
            // Sometimes it aims at a player in range instead of a building.
            var mark = Targets.Where(p => p && p.Transform && (p.Transform.position - pos).sqrMagnitude < 25f * 25f).OrderBy(_ => Random.value).FirstOrDefault();
            if (mark && Random.value < 0.4f) target = mark.Transform.position;
            Vector3 face = target - pos; face.y = 0f;
            if (face.sqrMagnitude > 0.01f) fish.transform.rotation = Quaternion.LookRotation(face);

            _nextSpit.TryGetValue(fish, out float spit);
            if (Time.time < spit) return;
            _nextSpit[fish] = Time.time + 4f;
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
            BuildSystem.Instance.ServerDamageArea(at, 1.6f, Mathf.RoundToInt(dmg * Late(_server.Wave)));
            if ((at - _server.CorePos).sqrMagnitude < 2.5f * 2.5f) DamageCore(dmg);
            HurtPlayersAround(at, 2f, PlayerDamage(15));
        }

        // ---------- per frame ----------

        private void Update()
        {
            EnsureRegistered();
            if (InstanceFinder.IsServerStarted)
            {
                CoreRegen();
                // Boss health for everyone's HUD: the biggest boss on the field (the frenzy's piranha over its big fish).
                _server.BossHp = 0; _server.BossMax = 0; _server.BossCrashIn = 0;
                int shown = 0;
                if (_server.Active)
                {
                    UpdateBreach(); // the one wall the horde is breaking (see Steer)
                    foreach (var kv in Enemies.Instance.ServerAll)
                    {
                        if (!EnemyCatalog.IsBoss(kv.Value.Kind) || kv.Key._hp.Value <= 0) continue;
                        int max = FortressPlugin.Instance.MaxHpOf.TryGetValue(kv.Key, out int m) ? m : Mathf.Max(_bossMax, kv.Key._hp.Value);
                        if (max < shown) continue;
                        shown = max;
                        _server.BossKind = (byte)kv.Value.Kind; _server.BossHp = kv.Key._hp.Value; _server.BossMax = max; _bossMax = max;
                        _server.BossCrashIn = Bosses.Of(kv.Key) is BossBrain bb ? Mathf.CeilToInt(bb.CrashIn) : 0;
                    }
                }
                if (_server.BossMax == 0) _bossMax = 0;
                Send(false);
            }
        }

        // The lighthouse (the round's core), with a lamp in its lantern; gold once this save has gone OVER 99. Its porch holds the
        // pearl in its clam, what the fish come for, behind a gate; a ladder goes up its east side to the gallery (Ladder.cs).
        public Transform Lighthouse => _core ? _core.transform : null;
        public const float LanternHeight = 4.86f; // the lantern's middle above the lighthouse's foot (tools/make_fortress_kit.py)
        private bool _coreGold;

        private void ShowCore(Vector3 pos, float health)
        {
            bool gold = Loot.Instance && Loot.Instance.ClientVault != null && Loot.Instance.ClientVault.Wins.Count > 0;
            if (_core && _coreGold != gold) Destroy(_core); // a win just came in (or another save's vault): made again in its colours
            bool open = _view.Active || _view.ReadyCountdown > 0;
            if (!_core)
            {
                _core = new GameObject("FortressCore");
                _core.layer = BuildSystem.PieceLayer;
                var col = _core.AddComponent<CapsuleCollider>();
                col.radius = CoreRadius; col.height = 3.3f; col.center = new Vector3(0f, 1.65f, 0f);
                var model = Models.Attach("Core", _core.transform, new Vector3(CoreRadius * 2f, 3f, CoreRadius * 2f), new Vector3(0f, 1.5f, 0f));
                _core.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // its door (modelled on +z) to the plaza and the dock road
                var lamp = new GameObject("Lantern");
                lamp.transform.SetParent(_core.transform, false);
                lamp.transform.localPosition = Vector3.up * LanternHeight;
                _lamp = lamp.AddComponent<Light>();
                _lamp.type = LightType.Point; _lamp.color = new Color(1f, 0.86f, 0.6f); _lamp.range = 9f; _lamp.intensity = 1.2f; _lamp.shadows = LightShadows.None;
                DontDestroyOnLoad(_core);
                _coreGold = gold;
                if (gold) Gild(model.GetComponent<Renderer>(), _lamp);
                _lampColor = _lamp.color; _lampIntensity = _lamp.intensity;
                Door(_core.transform, model);
                Solids(_core.transform);
                Shrine(_core.transform);
                _gate = Gate(_core.transform);
                _gateOpen = open;
                _gate.localPosition = open ? GateOpen : GateShut;
            }
            _core.transform.position = pos;
            Models.Tint(_core, health);

            // The gate shuts the pearl in between rounds. E at the door opens it: it sinks slowly into the porch's step while
            // the countdown runs, the clam opening and the pearl's light coming out (what brings the fish); open all round,
            // back up after. The lantern flashes red while the countdown runs.
            Vector3 slot = _core.transform.TransformPoint(GateShut);
            Vector3 to = open ? GateOpen : GateShut;
            if (open != _gateOpen) { _gateOpen = open; _gateNoise = 0f; }
            if (_gate.localPosition != to)
            {
                _gate.localPosition = Vector3.MoveTowards(_gate.localPosition, to, Time.deltaTime * GateSpeed);
                if (Time.time >= _gateNoise) { _gateNoise = Time.time + 1.4f; Fx.Sound("CrabFishingRodReelIn", slot, 0.9f); WeaponFx.Emit(WeaponFx.Dust, slot, 4, Vector3.up); }
                if (_gate.localPosition == to && open) { Fx.Sound("ShinyCreatureCaught_0" + Random.Range(1, 4), slot, 0.9f, AudioDistance.Long); BossFx.Burst(_pearl.position, "Pink", 24, 0.08f); }
                else if (_gate.localPosition == to) Fx.SoundRandom("MetallicItemImpact_V", 1, 3, slot, 0.8f);
            }
            float k = Mathf.InverseLerp(GateShut.y, GateOpen.y, _gate.localPosition.y); // 0 shut .. 1 open
            _lid.localRotation = Quaternion.Euler(-Mathf.Lerp(40f, 70f, k), 0f, 0f); // ajar behind the bars, wide open in a round
            _pearl.localPosition = PearlAt + Vector3.up * (0.02f * k * Mathf.Sin(Time.time * 2f));
            _pearl.localRotation = Quaternion.Euler(0f, Time.time * 25f, 0f);
            _pearlLight.intensity = Mathf.Lerp(0.6f, 2.6f, k) * (0.3f + 0.7f * health) * (1f + 0.08f * Mathf.Sin(Time.time * 3f)); // dims as the lighthouse is hurt
            Models.Tint(_pearl.gameObject, 1f); // the pearl keeps its colour while the lighthouse reddens
            bool counting = !_view.Active && _view.ReadyCountdown > 0;
            _lamp.color = counting ? new Color(1f, 0.22f, 0.12f) : _lampColor;
            _lamp.intensity = counting ? 2.5f + 1.5f * Mathf.Sin(Time.time * 12f) : _lampIntensity;
        }

        private Light _lamp, _pearlLight;
        private Color _lampColor;
        private float _lampIntensity, _gateNoise;
        private Transform _gate, _lid, _pearl;
        private bool _gateOpen;
        // In the lighthouse's own frame (its door on +z; tools/make_fortress_kit.py's Blender (x, y, z) is (-x, z, -y) here):
        // the gate standing in the porch's groove on its step, and sunk into the step; it goes down in about the countdown.
        private static readonly Vector3 GateShut = new Vector3(0f, 0.45f, 1.42f), GateOpen = new Vector3(0f, -1.27f, 1.42f);
        private const float GateSpeed = 0.38f;
        // The clam's hinge (its lips' plane) above the pedestal (its top at y 0.95), the pearl lying in its middle valley.
        private static readonly Vector3 Hinge = new Vector3(0f, 1.06f, 1.03f), PearlAt = new Vector3(0f, 1.093f, 1.25f);

        // The door: the game's own look-at and [E] (LighthouseDoor) at the porch, the whole lighthouse outlined.
        private static void Door(Transform core, GameObject model)
        {
            var door = new GameObject("LighthouseDoor");
            door.SetActive(false);
            door.transform.SetParent(core, false);
            door.transform.localPosition = new Vector3(0f, 0.95f, 1.62f);
            door.layer = LayerMask.NameToLayer("Interactable");
            door.tag = "Interactable";
            var col = door.AddComponent<BoxCollider>();
            col.size = new Vector3(1.3f, 1.9f, 0.3f);
            var text = new GameObject("TextTarget").transform;
            text.SetParent(core, false);
            text.localPosition = new Vector3(0f, 2.2f, 1.62f);
            var it = door.AddComponent<LighthouseDoor>();
            AccessTools.Field(typeof(Interactable), "_interactCol").SetValue(it, col);
            AccessTools.Field(typeof(Interactable), "_textTarget").SetValue(it, text);
            AccessTools.Field(typeof(Interactable), "_modelsToOutline").SetValue(it, new[] { model });
            door.SetActive(true);
        }

        // What you stand on and bump into besides the tower: the porch, the gallery's floor and railing (open where the ladder
        // comes up), the lantern. Named like the lighthouse, so shots, drops and helpers treat them as it.
        private static void Solids(Transform core)
        {
            GameObject Part(Vector3 at, Quaternion rot)
            {
                var go = new GameObject("FortressCore");
                go.layer = core.gameObject.layer;
                go.transform.SetParent(core, false);
                go.transform.localPosition = at;
                go.transform.localRotation = rot;
                return go;
            }
            Part(new Vector3(0f, 1.4f, 1.1f), Quaternion.identity).AddComponent<BoxCollider>().size = new Vector3(1.3f, 2.8f, 0.9f);
            var deck = Part(new Vector3(0f, Ladder.DeckY - 0.06f, 0f), Quaternion.identity);
            deck.transform.localScale = new Vector3(Ladder.DeckR * 2f, 0.06f, Ladder.DeckR * 2f); // the cylinder mesh: 1 across, 2 high
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var mc = deck.AddComponent<MeshCollider>();
            mc.sharedMesh = cyl.GetComponent<MeshFilter>().sharedMesh;
            mc.convex = true;
            Destroy(cyl);
            var lantern = Part(new Vector3(0f, 5.15f, 0f), Quaternion.identity).AddComponent<CapsuleCollider>();
            lantern.radius = 0.52f; lantern.height = 1.6f;
            for (int i = 1; i < 15; i++) // posts every 22.5 degrees at 1.5 m round; none at 0, where the ladder is (local -x)
            {
                Vector3 a = RailPost(i), b = RailPost(i + 1);
                Part((a + b) / 2f + Vector3.up * (Ladder.DeckY + 0.5f), Quaternion.LookRotation(b - a)).AddComponent<BoxCollider>().size = new Vector3(0.1f, 1f, (b - a).magnitude + 0.05f);
            }
        }

        private static Vector3 RailPost(int i) => new Vector3(-1.5f * Mathf.Cos(i * Mathf.PI / 8f), 0f, -1.5f * Mathf.Sin(i * Mathf.PI / 8f));

        // The pearl in its clam on the porch's pedestal, a pink light on it; the lid opens with the gate (ShowCore).
        private void Shrine(Transform core)
        {
            Models.Attach("ClamBase", core, Vector3.one * 0.3f).transform.localPosition = Hinge;
            _lid = new GameObject("ClamHinge").transform;
            _lid.SetParent(core, false);
            _lid.localPosition = Hinge;
            Models.Attach("ClamLid", _lid, Vector3.one * 0.3f);
            _pearl = Models.Attach("Pearl", core, Vector3.one * 0.24f).transform;
            _pearl.localPosition = PearlAt;
            _pearlLight = _pearl.gameObject.AddComponent<Light>();
            _pearlLight.type = LightType.Point; _pearlLight.color = new Color(1f, 0.84f, 0.95f); _pearlLight.range = 4f; _pearlLight.shadows = LightShadows.None;
        }

        // A portcullis of iron bars filling the porch's arch, its local origin at its bottom middle.
        private static Transform Gate(Transform core)
        {
            var gate = new GameObject("Gate").transform;
            gate.SetParent(core, false);
            var iron = Models.Make(new Color(0.24f, 0.24f, 0.27f));
            gate.gameObject.AddComponent<Models.OwnMaterials>().Mats.Add(iron);
            void Bar(Vector3 at, Vector3 size)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(b.GetComponent<Collider>());
                b.transform.SetParent(gate, false);
                b.transform.localPosition = at;
                b.transform.localScale = size;
                b.GetComponent<Renderer>().sharedMaterial = iron;
            }
            for (int i = 0; i < 7; i++)
            {
                float x = -0.36f + 0.12f * i, top = 1.2f + Mathf.Sqrt(0.43f * 0.43f - x * x) - 0.03f; // up into the arch
                Bar(new Vector3(x, top / 2f, 0f), new Vector3(0.035f, top, 0.035f));
            }
            for (int i = 0; i < 4; i++) Bar(new Vector3(0f, 0.25f + 0.35f * i, 0.02f), new Vector3(0.84f, 0.035f, 0.035f));
            return gate;
        }

        // The white bands go gold and the lantern burns warm.
        private static void Gild(Renderer r, Light lamp)
        {
            if (!r) return;
            var cols = Models.ColorsOf(r);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i].b > cols[i].r + 0.3f) Models.Recolor(r, i, new Color(1f, 0.82f, 0.4f));          // the crystal lantern
                else if (cols[i].grayscale > 0.7f) Models.Recolor(r, i, new Color(0.9f, 0.66f, 0.24f)); // the white bands
            }
            lamp.color = new Color(1f, 0.78f, 0.35f);
            lamp.intensity = 2f;
        }

        private float _nextHud; // the HUD text is rebuilt ten times a second, not every frame: the strings were the largest
                                // steady allocation both the host and the clients made (each one is garbage for the GC)

        private void LateUpdate()
        {
            // Sounds for the ready countdown and the round starting.
            if (_view.ReadyCountdown != _lastReady) { if (_view.ReadyCountdown > 0) Fx.Sound("Click", _view.CorePos, 1f, AudioDistance.Long); _lastReady = _view.ReadyCountdown; }
            if (_view.Active && !_wasActive) { Fx.Sound("QuestFinished", _view.CorePos, 1f, AudioDistance.Long); _lastCoinsKnown = false; }
            _wasActive = _view.Active;

            if (!_view.Active)
            {
                // Between rounds the lighthouse still stands on the island, whole again; away from the island it isn't there.
                var isl = FortressIsland.Instance;
                if (isl && isl.Exists) ShowCore(isl.LighthousePos, 1f);
                else if (_core) Destroy(_core);
                // Between rounds: on the island, explain how to start.
                bool near = FortressIsland.Instance && Player.LocalPlayer && Player.LocalPlayer.Transform
                            && FortressIsland.Instance.OnIsland(Player.LocalPlayer.Transform.position, FortressIsland.IslandRadius + 6f);
                if (!near || !EnsureHud()) { ShowHud(false); return; }
                ShowHud(true);
                _roundGroup.SetActive(false);
                _bossBox.gameObject.SetActive(false);
                _titleText.gameObject.SetActive(true);
                if (Time.time >= _nextHud)
                {
                    _nextHud = Time.time + 0.1f;
                    _titleText.text = L10n.T("islandName");
                    SetPhase(_view.ReadyCountdown > 0 ? $"<color={Hud.Hex(Hud.Accent)}><b>{L10n.T("readyCountdown", _view.ReadyCountdown)}</b></color>　" + Hud.Keys(L10n.T("gateCancelHint"))
                             : Hud.Keys(L10n.T("readyPrompt")) + "　" + L10n.T("helpHint"));
                }
                return;
            }
            ShowCore(_view.CorePos, _view.CoreMax > 0 ? (float)_view.CoreHp / _view.CoreMax : 1f);

            if (!EnsureHud()) return;
            ShowHud(true);
            _roundGroup.SetActive(true);
            _titleText.gameObject.SetActive(false);
            if (ViewCoins > _lastCoins && _lastCoinsKnown) { _coinPop = "+$" + (ViewCoins - _lastCoins); _coinPopUntil = Time.time + 1.2f; }
            _lastCoins = ViewCoins; _lastCoinsKnown = true;
            if (_view.CoreHp < _lastCoreHp) _hpFlashUntil = Time.time + 0.25f;
            _lastCoreHp = _view.CoreHp;

            if (Time.time >= _nextHud)
            {
                _nextHud = Time.time + 0.1f;
                float hp = _view.CoreMax > 0 ? (float)_view.CoreHp / _view.CoreMax : 1f;
                _waveText.text = L10n.T("hudWave", _view.Wave);
                _hpBar.Set(hp, Time.time < _hpFlashUntil ? Color.white : Hud.HealthColor(hp));
                _hpText.text = $"{_view.CoreHp}<size=70%><color={Hud.Hex(Hud.Muted)}> / {_view.CoreMax}</color></size>";
                _enemyText.text = _view.Remaining.ToString();
                _coinText.text = "$" + ViewCoins + (Time.time < _coinPopUntil ? $" <size=72%><color={Hud.Hex(Hud.Good)}>{_coinPop}</color></size>" : "");
                bool host = InstanceFinder.IsServerStarted;
                int mend = _view.Building && BuildSystem.Instance ? BuildSystem.Instance.ClientRepairAllCost : 0;
                SetPhase(_view.Building ? L10n.T("buildPhase", _view.Countdown) + "　" + L10n.T("phaseSkip", FortressPlugin.Instance.ReadyKeyName)
                                          + "　" + L10n.T("phaseBuildKey", BuildSystem.BuildKey.Value) + (mend > 0 ? "　" + L10n.T("phaseRepairAll", mend) : "")
                                        : $"<color={Hud.Hex(Hud.Accent)}>{L10n.T("fightPhase")}</color>"
                                          + (host && FortressPlugin.Instance.CanCallNext ? "　" + L10n.T("phaseCallNext", FortressPlugin.Instance.ReadyKeyName) : ""));
                bool boss = _view.BossMax > 0;
                _bossBox.gameObject.SetActive(boss);
                if (boss)
                {
                    // The game's bosses crash into the lighthouse if they live too long: the countdown goes red in the last 30 s.
                    int left = _view.BossCrashIn;
                    string crash = left > 0 ? $"  <size=75%><color={Hud.Hex(left <= 30 ? Hud.Bad : Hud.Muted)}>{L10n.T("bossCrashIn", left / 60, (left % 60).ToString("00"))}</color></size>" : "";
                    _bossText.text = L10n.T("bossName" + (EnemyKind)_view.BossKind) + crash;
                    // Its health in Bosses.Bars bars (Owen: 「王會是血量xN 然後一條一條打掉」): the top one drains over the next
                    // one's colour, the count of bars left beside it; a bar broken flashes white.
                    float f = (float)_view.BossHp / _view.BossMax;
                    int bars = Bosses.BarsLeft(f);
                    if (bars < _bossBars && _view.BossMax == _bossBarsOf) _barFlashUntil = Time.time + 0.18f;
                    _bossBars = bars; _bossBarsOf = _view.BossMax;
                    _bossUnder.Set(bars > 1 ? 1f : 0f, BarColours[(bars + 2) % BarColours.Length] * 0.7f);
                    _bossBar.Set(bars > 0 ? f * Bosses.Bars - (bars - 1) : 0f, Time.time < _barFlashUntil ? Color.white : BarColours[(bars + 3) % BarColours.Length]);
                    _bossCount.text = bars > 1 ? "×" + bars : "";
                }
            }
            else if (_view.BossMax <= 0 && _bossBox.gameObject.activeSelf) _bossBox.gameObject.SetActive(false);
        }

        private void ShowHud(bool on)
        {
            if (_top) _top.gameObject.SetActive(on);
            if (_phaseBox) _phaseBox.gameObject.SetActive(on);
            if (_bossBox && !on) _bossBox.gameObject.SetActive(false);
        }

        private void SetPhase(string text)
        {
            text = Hud.Keys(text);
            if (_phaseText.text == text) return;
            _phaseText.text = text;
            _phaseBox.rectTransform.sizeDelta = new Vector2(_phaseText.GetPreferredValues(text).x + 48f, 42f);
        }

        // Top of the screen: wave, lighthouse health, enemies left, team coins; under it the phase, then the boss.
        private bool EnsureHud()
        {
            if (_top) return true;
            if (!Hud.Font) return false;
            var root = Hud.Root;
            Vector2 L = new Vector2(0f, 0.5f), C = new Vector2(0.5f, 0.5f);
            _top = Hud.Box(root, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(890f, 62f), Hud.Panel, 18f).rectTransform;
            _roundGroup = Hud.Node(_top, "Round", Vector2.zero, Vector2.zero, Vector2.zero).gameObject;
            var g = (RectTransform)_roundGroup.transform;
            g.anchorMin = Vector2.zero; g.anchorMax = Vector2.one;
            var pill = Hud.Box(g, L, new Vector2(9f, 0f), new Vector2(150f, 46f), Hud.Accent, 14f);
            _waveText = Hud.Label(pill.transform, "", 26f, C, Vector2.zero, new Vector2(150f, 46f), TextAlignmentOptions.Center, new Color(0.13f, 0.08f, 0.02f), bold: true);
            Hud.Label(g, L10n.T("hudLighthouse"), 22f, L, new Vector2(176f, 0f), new Vector2(70f, 40f), TextAlignmentOptions.Left, Hud.Muted);
            _hpBar = Hud.Bar.Make(g, L, new Vector2(248f, 0f), new Vector2(220f, 16f), Hud.Good);
            _hpText = Hud.Label(g, "", 24f, L, new Vector2(480f, 0f), new Vector2(130f, 40f), TextAlignmentOptions.Left, bold: true);
            Hud.Icon(g, "Skull", L, new Vector2(612f, 0f), new Vector2(28f, 28f), Hud.Bad);
            _enemyText = Hud.Label(g, "", 24f, L, new Vector2(648f, 0f), new Vector2(70f, 40f), TextAlignmentOptions.Left, bold: true);
            Hud.Icon(g, "Money", L, new Vector2(716f, 0f), new Vector2(32f, 20f), Hud.Gold);
            _coinText = Hud.Label(g, "", 24f, L, new Vector2(756f, 0f), new Vector2(130f, 40f), TextAlignmentOptions.Left, Hud.Gold, bold: true);
            _titleText = Hud.Label(_top, "", 28f, C, Vector2.zero, new Vector2(860f, 60f), TextAlignmentOptions.Center, bold: true);
            _phaseBox = Hud.Box(root, new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(400f, 42f), Hud.Panel, 21f);
            _phaseText = Hud.Label(_phaseBox.transform, "", 22f, C, Vector2.zero, new Vector2(1400f, 42f), TextAlignmentOptions.Center);
            _bossBox = Hud.Box(root, new Vector2(0.5f, 1f), new Vector2(0f, -136f), new Vector2(660f, 46f), Hud.Panel, 16f);
            _bossText = Hud.Label(_bossBox.transform, "", 22f, L, new Vector2(18f, 0f), new Vector2(190f, 40f), TextAlignmentOptions.Left, Hud.Bad, bold: true);
            _bossUnder = Hud.Bar.Make(_bossBox.transform, L, new Vector2(206f, 0f), new Vector2(386f, 18f), Hud.Bad);
            _bossBar = Hud.Bar.Make(_bossBox.transform, L, new Vector2(206f, 0f), new Vector2(386f, 18f), Hud.Bad);
            _bossBar.Back.color = Color.clear; // the bar under it shows through
            _bossCount = Hud.Label(_bossBox.transform, "", 22f, L, new Vector2(598f, 0f), new Vector2(56f, 40f), TextAlignmentOptions.Left, Hud.Ink, bold: true);
            return true;
        }

        // ---------- networking ----------

        private void EnsureRegistered()
        {
            var nm = InstanceFinder.NetworkManager;
            if (!nm || nm == _registeredOn) return;
            _registeredOn = nm;
            nm.ClientManager.RegisterBroadcast<SiegeState>((s, ch) => { _view = s; HasView = true; });
            nm.ServerManager.RegisterBroadcast(Net.Safe<TestKeyRequest>((conn, r, ch) => { if (FortressPlugin.Instance) FortressPlugin.Instance.HostTestKey(r); }));
            nm.ServerManager.RegisterBroadcast(Net.Safe<SkipBuildRequest>((conn, r, ch) => { if (FortressPlugin.Instance) FortressPlugin.Instance.ServerSkipBuild(); }));
            nm.SceneManager.OnClientLoadedStartScenes += (conn, asServer) => { if (asServer) InstanceFinder.ServerManager.Broadcast(conn, _server); };
            nm.ClientManager.OnClientConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) { _view = default; HasView = false; } };
            nm.ServerManager.OnServerConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) { _server = default; _walkers.Clear(); } };
        }
    }

    [HarmonyPatch(typeof(RunningFish), "UpdateMovement")]
    internal static class RunningFishSteerPatch
    {
        private static bool Prefix(RunningFish __instance) => !(Siege.Instance && Siege.Instance.Steer(__instance));
    }

    // The host steers fortress fish. The game hands a creature's physics to whichever client hits or bumps it, and from then on
    // only that client moves it, with the game's own wandering: a friend's shot made a fish forget the lighthouse (and ignore
    // the host's bubble, hook and whirlpool). Clients leave living fortress fish to the host, as the game already does for bosses.
    [HarmonyPatch(typeof(RigidbodySync), nameof(RigidbodySync.StartSimulateLocal))]
    internal static class HostKeepsFortressFish
    {
        private static bool Prefix(RigidbodySync __instance)
        {
            if (InstanceFinder.IsServerStarted || !Enemies.Instance) return true;
            var c = __instance.GetComponentInParent<Creature>();
            return !(c && !c.IsDead && Enemies.Instance.ClientIsFortress(c.ObjectId));
        }
    }

    // During a round the boss bar is ours (the name, the health we gave it, the crash countdown); the game's own would sit
    // on top of it.
    [HarmonyLib.HarmonyPatch(typeof(PlayerUI), nameof(PlayerUI.ToggleBossUI))]
    internal static class OurBossBarInRounds
    {
        private static bool Prefix(bool __0) => !(__0 && Siege.Instance && Siege.Instance.ViewActive);
    }

    // The lighthouse's door: E pulls its gate to start a round (E again during the countdown lets it go); during a round, E
    // mends the lighthouse a tenth at a time (the playtest wanted the lighthouse repairable).
    public class LighthouseDoor : Interactable
    {
        private string _text;

        // Called every frame it's looked at: the words follow the state (pull, stop, nothing during a round).
        public override void Hover()
        {
            var v = Siege.Instance ? Siege.Instance.View : default;
            string want = !v.Active ? L10n.T(v.ReadyCountdown > 0 ? "gateCancel" : "gatePull") + " " + SpriteManager.GetPickUpInput()
                        : v.CoreHp < v.CoreMax ? L10n.T("coreRepair", Perks.MyCost(Siege.CoreRepairCost(v.CoreMax))) + " " + SpriteManager.GetPickUpInput() : L10n.T("coreFull");
            if (want != _text)
            {
                if (_text != null) PlayerUI.HideLookAtText(_text);
                _text = want;
                if (_text != null) PlayerUI.SetLookAtText(_text, TextTarget);
            }
            base.Hover();
        }

        public override void UnHover()
        {
            if (_text != null) PlayerUI.HideLookAtText(_text);
            _text = null;
            base.UnHover();
        }

        public override void Interact(Player player)
        {
            base.Interact(player);
            if (player != Player.LocalPlayer || !Siege.Instance) return;
            UnHover();
            if (InstanceFinder.IsClientStarted) InstanceFinder.ClientManager.Broadcast(new GateRequest { Pull = (byte)(Siege.Instance.View.Active ? 2 : 1) });
            AutoTest.Count("gate_e");
        }
    }
}
