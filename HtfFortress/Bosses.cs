using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FishNet;
using HarmonyLib;
using UnityEngine;

namespace HtfFortress
{
    // The ten bosses (one every tenth wave) are ours from top to bottom: each is one of the game's creatures, blown up and
    // dressed (BossLooks), but none of the game's own behaviour runs it. On the host its brain (BossBrains.cs) holds it
    // kinematic, moves it and casts its skills, each warned on the ground first (BossFx draws the warnings and the hits on
    // every screen). A big move can be broken: hit the boss hard enough while it winds up (or blast it with the conch)
    // and the move is off, it reels and takes half again for a moment. At half health each turns into its second form.
    // One that's still alive after CrashAfter seconds throws itself at the lighthouse.
    public class Bosses : MonoBehaviour
    {
        public static Bosses Instance;
        public const float CrashAfter = 200f;
        // Everything moved by us rather than by the game (bosses and their summons), on every machine: their own movement
        // code stays off (BossPuppetPatches).
        internal static readonly HashSet<Creature> Puppets = new HashSet<Creature>();
        private readonly Dictionary<Creature, BossBrain> _brains = new Dictionary<Creature, BossBrain>();
        private readonly List<KeyValuePair<Creature, BossBrain>> _brainsScratch = new List<KeyValuePair<Creature, BossBrain>>(); // reused in Update: no per-frame garbage
        private readonly Dictionary<int, float> _silenced = new Dictionary<int, float>(); // turret piece id -> until
        private readonly Dictionary<int, float> _scrambled = new Dictionary<int, float>(); // turret piece id -> until
        private float _nextAura;
        private bool _musicOn;

        private void Awake() => Instance = this;

        public static BossBrain Of(Creature c) => Instance && c && Instance._brains.TryGetValue(c, out var b) ? b : null;
        public IEnumerable<BossBrain> All => _brains.Values;
        public static bool IsPuppet(Creature c) => c && Puppets.Contains(c);

        // ---------- host ----------

        // A boss just spawned (FortressPlugin.ServerSpawnBoss): its brain takes over when its entrance is done (`hold` s).
        public void ServerAdd(Creature c, EnemyKind kind, int wave, float hold, Vector3 home)
        {
            var b = BossBrains.Make(kind);
            b.Init(c, kind, wave, home);
            _brains[c] = b;
            Puppets.Add(c);
            if (!_musicOn) { _musicOn = true; Fx.Send(FxKind.BossMusic, c.transform.position, c.transform.position, 0, 1); }
            b.Hold(hold);
        }

        // A summon moved by a little brain of its own (the squadron's fish, the voxel's copies).
        public void ServerAddMinion(Creature c, BossBrain b, EnemyKind kind, int wave)
        {
            b.IsBoss = false;
            b.Init(c, kind, wave, c.transform.position);
            _brains[c] = b;
            Puppets.Add(c);
            b.Hold(0f);
        }

        public void ServerClear()
        {
            if (_musicOn) { _musicOn = false; Fx.Send(FxKind.BossMusic, Vector3.zero, Vector3.zero, 0, 0); }
            foreach (var b in _brains.Values) b.Stop();
            _brains.Clear();
            _silenced.Clear();
            _scrambled.Clear();
            Dark = false;
            BossFx.Send("dark", null, Vector3.zero, Vector3.zero, 0);
        }

        // Auto-test: pretend the bosses have been around for a while.
        public void DebugAge(float seconds) { foreach (var b in _brains.Values) b.Age(seconds); }

        // The conch: a blast of sound breaks the move a boss is winding up.
        public void Interrupt(Creature c) { if (Of(c) is BossBrain b && b.TryBreak(true)) AutoTest.Count("boss_interrupted"); }

        // Turrets a boss has shut down (chained, turned to stone, inked over).
        public void Silence(int pieceId, float seconds) => _silenced[pieceId] = Mathf.Max(_silenced.TryGetValue(pieceId, out float u) ? u : 0f, Time.time + seconds);
        public bool IsSilenced(int pieceId) => _silenced.TryGetValue(pieceId, out float u) && Time.time < u;

        // Turrets standing on the voxel fish's glitch tiles: they still fire, at whatever in range comes up (Turrets.Fire).
        public void Scramble(int pieceId, float seconds) => _scrambled[pieceId] = Mathf.Max(_scrambled.TryGetValue(pieceId, out float u) ? u : 0f, Time.time + seconds);
        public bool IsScrambled(int pieceId) => _scrambled.TryGetValue(pieceId, out float u) && Time.time < u;

        // The anglerfish's darkness: turrets see half as far, except in the lighthouse's light.
        public static bool Dark;
        public static float RangeAt(Vector3 turret) => Dark && Siege.Instance && (turret - Siege.Instance.CorePos).Flat().sqrMagnitude > LightReach * LightReach ? 0.5f : 1f;
        public const float LightReach = 10f;

        // A boss still making its entrance can't be hurt, and the turrets and weapons don't aim at it (Owen: invincible while
        // its intro plays). TestKill: the test keys' "clear the field" still clears it.
        public static bool Entering(Creature c) => Of(c) is BossBrain b && b.IsBoss && !b.IsLive;
        internal static bool TestKill;

        // Damage on its way into a boss (Creature.ServerChangeHp): none while it makes its entrance, turrets' share against
        // its guard, everyone's while it's open, and what players deal counts toward breaking the move it's winding up.
        internal static int OnHurt(Creature c, int dmg)
        {
            if (dmg <= 0 || !(Of(c) is BossBrain b)) return dmg;
            if (b.IsBoss && !b.IsLive && !TestKill) { AutoTest.Count("boss_hit_in_entrance"); return 0; }
            // The change is a set piece, not a window: nothing lands while it plays. Owen: too many bosses died before
            // anyone saw the second form; this is the guarantee (Transforming covers the freeze and every SecondForm beat).
            if (b.IsBoss && b.Transforming && !TestKill) { AutoTest.Count("boss_hit_in_transition"); return 0; }
            float m = b.TakeMult * (Turrets.Firing ? b.TurretMult : 1f);
            int d = Mathf.Max(1, Mathf.RoundToInt(dmg * m));
            // A boss that has not changed yet cannot be deleted by one late burst: leave it at 1, so the next Tick plays
            // the change. Without this a crit from 60% to 0 removes the second form entirely.
            if (b.IsBoss && !b.Phase2 && b.Body && b.Body._hp.Value > 0 && b.Body._hp.Value - d <= 0)
                d = b.Body._hp.Value - 1;
            if (d <= 0) return 0;
            if (Turrets.Firing) b.ByTurrets += d;
            else { b.ByPlayers += d; b.PlayerDamage(d); }
            return d;
        }

        private void Update()
        {
            if (!InstanceFinder.IsServerStarted || _brains.Count == 0) return;
            if (FortressPlugin.Frozen) { foreach (var c in _brains.Keys) if (c) Siege.Synced(c); return; } // test key F11
            bool aura = Time.time >= _nextAura;
            if (aura) _nextAura = Time.time + 0.5f;
            _brainsScratch.Clear();
            foreach (var kv in _brains) _brainsScratch.Add(kv); // (not AddRange: Mono's copies through a temporary array every frame)
            foreach (var kv in _brainsScratch)
            {
                var b = kv.Value;
                if (!kv.Key || kv.Key._hp.Value <= 0 || !Siege.Instance.ServerActive)
                {
                    b.Stop();
                    _brains.Remove(kv.Key);
                    if (kv.Key && kv.Key._hp.Value <= 0)
                    {
                        if (b.IsBoss) { b.Report(true); b.Died(); BossTrophies.ServerDrop(b.Kind, kv.Key.transform.position, b.Wave);
                            if (b.Kind == EnemyKind.Eye && b.Wave >= EnemyCatalog.FinalWave) { Vector3 pos = kv.Key.transform.position; Fx.Send(FxKind.Crash, pos, pos); Fx.Send(FxKind.FinalBoss, pos, pos); } }
                        StartCoroutine(Gone(kv.Key, b.IsBoss ? 1.8f : 0.5f));
                    }
                    continue;
                }
                b.Tick();
                if (aura && b.IsBoss) b.Aura();
                if (AutoTest.Running && Time.time >= b.NextTrace) { b.NextTrace = Time.time + 3f; BuildSystem.Log.LogInfo("boss trace " + b.Trace()); }
            }
            bool anyBoss = false; // (a loop: Values.Any boxed an enumerator every frame)
            foreach (var b in _brains.Values) if (b.IsBoss) { anyBoss = true; break; }
            if (!anyBoss)
            {
                if (Dark) { Dark = false; BossFx.Send("dark", null, Vector3.zero, Vector3.zero, 0); }
                if (_musicOn) { _musicOn = false; Fx.Send(FxKind.BossMusic, Vector3.zero, Vector3.zero, 0, 0); }
            }
        }

        // A dead one goes down (into the ground, the sea, out of the sky) and is gone: its body's worth was paid already.
        private static IEnumerator Gone(Creature c, float seconds)
        {
            Vector3 from = c.transform.position, to = from + Vector3.down * Mathf.Max(3f, from.y - BossFx.Ground(from) + 3f);
            for (float t = 0f; t < seconds && c; t += Time.deltaTime)
            {
                float k = t / seconds;
                if (c.Rig) c.Rig.MovePosition(Vector3.Lerp(from, to, k * k));
                Siege.Synced(c);
                yield return null;
            }
            if (c && c.IsSpawned && !c.IsDestroying) c.DestroyItem();
        }

        // Clients forget puppets that are gone.
        private float _nextPrune;
        private void LateUpdate()
        {
            if (Time.time < _nextPrune) return;
            _nextPrune = Time.time + 3f;
            Puppets.RemoveWhere(c => !c);
        }
    }

    // A brain's coroutines, run by hand: one stack per brain, so a broken move, a phase change or death can drop
    // whatever it was doing at once. Yield null for the next frame, a float to wait that long, an IEnumerator to run it.
    internal class Runner
    {
        private readonly Stack<IEnumerator> _stack = new Stack<IEnumerator>();
        private float _until;
        public bool Idle => _stack.Count == 0;
        public string Doing => string.Join(" < ", _stack.Select(e => e.GetType().Name.Split('>')[0].TrimStart('<')));

        public void Start(IEnumerator e) { _stack.Clear(); if (e != null) _stack.Push(e); _until = 0f; }
        public void Stop() => _stack.Clear();

        public void Tick()
        {
            if (Time.time < _until) return;
            for (int guard = 0; _stack.Count > 0 && guard < 64; guard++)
            {
                var top = _stack.Peek();
                bool more;
                try { more = top.MoveNext(); }
                catch (Exception e) { BuildSystem.Log.LogError("boss brain: " + e); _stack.Clear(); return; }
                if (_stack.Count == 0 || _stack.Peek() != top) { if (Time.time < _until) return; continue; } // it was stopped or restarted meanwhile
                if (!more) { _stack.Pop(); continue; }
                switch (top.Current)
                {
                    case IEnumerator child: _stack.Push(child); continue;
                    case float s: _until = Time.time + s; return;
                    default: return;
                }
            }
        }
    }

    // What every boss brain stands on: moving the body, warning, hitting, picking targets.
    public abstract class BossBrain
    {
        public Creature Body;
        public EnemyKind Kind;
        public int Wave, Tier, MaxHp;
        public bool Phase2;
        public bool IsBoss = true;
        public float TakeMult = 1f, TurretMult = 1f; // >1 while it reels, TurretMult < 1 behind a guard
        protected float Born, Foot, Reach, Height; // spawned at; its origin above its lowest point; its size round; how tall
        protected Vector3 Home; // where it stands at the end of its entrance
        internal readonly Runner Run = new Runner();
        private bool _warned, _stunned;
        // the move being wound up, if it can be broken: broken when the players' damage since it began reaches _breakAt
        private int _breakDealt, _breakAt;
        private bool _breakable;
        protected bool Broken { get; private set; }
        public bool Winding => _breakable; // winding up a move that can be broken (auto-test)
        internal float NextTrace;
        internal string Trace() => $"{Kind} at {Pos:F1} (home {Home:F1}), kinematic {(Rig ? Rig.isKinematic.ToString() : "-")}, alive {Alive}, live {_live}, hp {Body._hp.Value}/{MaxHp}, doing {Run.Doing}";
        public void DebugBreak() { _breakable = true; TryBreak(true); } // test key: reel now, whatever it's doing
        internal void Age(float seconds) => Born -= seconds;

        protected Transform T => Body.transform;
        protected Rigidbody Rig => Body.Rig;
        public Vector3 Pos => T.position;
        protected static FortressIsland Isle => FortressIsland.Instance;
        protected static Vector3 Center => FortressIsland.Instance.Center;
        protected static Vector3 Core => Siege.Instance.CorePos;
        protected bool Alive => Body && Body._hp.Value > 0 && Siege.Instance.ServerActive;
        protected float Hp01 => MaxHp > 0 ? (float)Body._hp.Value / MaxHp : 1f;
        protected float SlowFactor => Siege.Instance.SlowOf(Body);
        // Its body's facing: most of the game's fish look down +z; a few are modelled turned (BossLooks.Turn).
        protected Quaternion Facing(Vector3 dir) => Quaternion.LookRotation(dir.Flat().sqrMagnitude > 1e-4f ? dir.Flat() : Forward.Flat()) * BossLooks.Turn(Kind);
        protected Vector3 Forward => T.rotation * Quaternion.Inverse(BossLooks.Turn(Kind)) * Vector3.forward;

        public void Init(Creature c, EnemyKind kind, int wave, Vector3 home)
        {
            Body = c; Kind = kind; Wave = wave; Tier = Mathf.Clamp(wave / 10, 1, 10); Home = home;
            MaxHp = FortressPlugin.Instance.MaxHpOf.TryGetValue(c, out int m) ? m : c._hp.Value;
            Born = Time.time;
            foreach (var rb in c.GetComponentsInChildren<Rigidbody>()) { rb.isKinematic = true; }
            var b = BossLooks.WorldBounds(T);
            Foot = T.position.y - b.min.y;
            Reach = Mathf.Max(0.5f, Mathf.Max(b.extents.x, b.extents.z));
            Height = Mathf.Max(0.5f, b.size.y);
        }

        // Its entrance (it comes up, lands, rises out of the sea... and stands for its roar), then it lives.
        public void Hold(float seconds) => Run.Start(HoldThen(seconds));
        private IEnumerator HoldThen(float seconds)
        {
            float t0 = Time.time;
            yield return Entrance(seconds);
            float left = seconds - (Time.time - t0);
            if (left > 0f) yield return left;
            if (IsBoss) Fx.Announce("bossHint" + Kind); // how to fight it, once the entrance is over and the screen is back
            Run.Start(Live());
        }
        private bool _live; // past its entrance
        public bool IsLive => _live;
        private IEnumerator Live()
        {
            _live = true; _liveAt = Time.time;
            while (true)
            {
                // CastNew (auto-test): the second form's new move next, so the run can check it without waiting on the dice.
                if (CastNew && Phase2) { CastNew = false; yield return NewMove(); yield return Breath; }
                else yield return Act();
                yield return null;
            }
        }
        internal bool CastNew;
        // The move only its second form has (BOSS-FORMS.md §2, 新招); Act also picks it by weight once Phase2 is on.
        protected virtual IEnumerator NewMove() => Act();

        // What its fight came to, from the end of its entrance: how long, and the players' and turrets' share of the damage
        // (Owen, on its health: 「說不定會覺得怎麼樣好調整」). In everyone's chat when it's down or gone, and in the host's log.
        private float _liveAt;
        internal long ByPlayers, ByTurrets;
        internal void Report(bool down)
        {
            if (!IsBoss || !_live) return;
            int seconds = Mathf.RoundToInt(Time.time - _liveAt);
            long dealt = ByPlayers + ByTurrets;
            int players = dealt > 0 ? (int)(ByPlayers * 100 / dealt) : 0;
            int taken = down ? 100 : MaxHp > 0 && Body ? Mathf.Clamp(Mathf.RoundToInt(100f * (MaxHp - Body._hp.Value) / MaxHp), 0, 100) : 0;
            BuildSystem.Log.LogInfo($"Boss fight: {Kind}, wave {Wave}, {PlayerManager.Players.Count} players, {MaxHp} health: {(down ? "down" : "rammed the lighthouse")} after {seconds} s, " +
                                    $"{taken}% taken (players {ByPlayers}, turrets {ByTurrets}).");
            Fx.BossFight(Kind, down, seconds, players, taken);
        }

        // Whatever it was in the middle of is dropped (broken, second form): undo what that move had changed.
        protected virtual void Interrupted() { TurretMult = 1f; }

        // Where it's spawned, off its stand position (`at` is where its feet go: under the sand, high up, far out at sea),
        // for its Entrance to bring it in.
        public static Vector3 ArrivalOffset(EnemyKind k, Vector3 at, Vector3 center)
        {
            Vector3 outward = (at - center).Flat().normalized, side = Vector3.Cross(Vector3.up, outward);
            switch (k)
            {
                case EnemyKind.Warden: return Vector3.down * 7f;
                case EnemyKind.Urchin: return Vector3.up * 22f;
                case EnemyKind.Angler: return outward * 7f + Vector3.down * 10f; // up out of the sea off the beach
                case EnemyKind.Kraken: return Vector3.down * 10f;
                case EnemyKind.Stonefish: return Vector3.down * 8f;
                case EnemyKind.Eye: return Vector3.up * 35f;
                case EnemyKind.Koi: return outward * 6f + Vector3.down * 9f;
                case EnemyKind.Voxel: return Vector3.down * 40f; // nowhere to be seen until it has loaded
                case EnemyKind.Steed: { Vector3 sea = at + outward * 30f; return outward * 30f + Vector3.up * (WaterManager.GetWaterHeight(sea) - at.y); }
                case EnemyKind.Skyfish: return outward * 45f + side * 12f + Vector3.up * 9f;
                default: return Vector3.zero;
            }
        }

        // By default it comes straight to its stand position in the first 0.8 s.
        protected virtual IEnumerator Entrance(float seconds)
        {
            Vector3 from = Pos;
            Quaternion r = T.rotation;
            for (float t = 0f; t < 0.8f && Alive; t += Time.deltaTime) { float k = t / 0.8f; Put(Vector3.Lerp(from, Home, 1f - (1f - k) * (1f - k)), r); yield return null; }
            Put(Home, r);
        }

        public void Stop() { Run.Stop(); OnStop(); }
        protected virtual void OnStop() { }

        // The boss's loop: one move after another (BossBrains.cs). Phase2 flips at half health (SecondForm plays first).
        protected abstract IEnumerator Act();
        protected virtual IEnumerator SecondForm() { Shout("bossPhase2"); Send("phase2", Pos, Pos); yield return 1.5f; }

        // True from the moment the change starts until its show is over (OnHurt lets nothing through meanwhile).
        public bool Transforming { get; private set; }
        // The freeze before the boss's own SecondForm (which plays its own 1.5-2.5 s roar and effects).
        protected virtual float TransformSeconds => 0.9f;

        // The change: freeze, colour swap on every screen, then the boss's own SecondForm. Damage cannot cut it short, so
        // the second form is always seen at least once.
        private IEnumerator Transform()
        {
            Transforming = true;
            _breakable = false; // a wind-up left over from the last move cannot be broken mid-change
            TakeMult = 1f; TurretMult = 1f;
            Send("secondform", Pos, Pos);
            for (float t = 0f; t < TransformSeconds && Alive; t += Time.deltaTime) yield return null;
            yield return SecondForm();
            Transforming = false;
        }

        public virtual void Tick()
        {
            if (_breakable && _breakDealt >= _breakAt) TryBreak(false);
            // Bosses only: a summon worn to half (a glitch copy) would otherwise stop and announce a change it doesn't have.
            if (IsBoss && !Phase2 && Hp01 <= 0.5f && !_stunned && _live)
            {
                Phase2 = true;
                AutoTest.Count("boss_phase2_" + Kind);
                Interrupted();
                Run.Start(Then(Transform(), Live()));
            }
            if (Time.time - Born > Bosses.CrashAfter - 30f && !_warned) { _warned = true; Fx.Announce("bossCrashSoon", 30); }
            if (Time.time - Born > Bosses.CrashAfter) { Crash(); return; }
            Run.Tick();
            Hazards();
            Siege.Synced(Body);
        }
        private static IEnumerator Then(IEnumerator a, IEnumerator b) { yield return a; yield return b; }

        // ---------- breaking ----------

        internal void PlayerDamage(int d) { if (_breakable) _breakDealt += d; }

        // `byConch`: the conch breaks any move that can be broken, damage only once enough is dealt.
        internal bool TryBreak(bool byConch)
        {
            if (!_breakable || (!byConch && _breakDealt < _breakAt)) return false;
            _breakable = false;
            Broken = true;
            AutoTest.Count("boss_broken_" + Kind);
            Interrupted();
            Run.Start(Then(Reel(), Live()));
            return true;
        }

        // It reels: dizzy, open to everything (x1.5) for a few seconds. One up in the air drops to the ground first.
        protected virtual IEnumerator Reel()
        {
            _stunned = true;
            Fx.Announce("bossBroken");
            Send("broken", Pos + Vector3.up * (Reach + 0.5f), Pos, 3000);
            TakeMult = 1.5f;
            yield return Fall();
            yield return 3f;
            TakeMult = 1f;
            _stunned = false;
            yield return Recover();
        }
        protected virtual IEnumerator Fall() { yield break; }
        protected virtual IEnumerator Recover() { yield break; }

        // ---------- moving (kinematic; the network carries it to everyone) ----------

        protected void Put(Vector3 p, Quaternion r)
        {
            if (!Rig) { T.SetPositionAndRotation(p, r); return; }
            Rig.MovePosition(p);
            Rig.MoveRotation(r);
        }
        protected void Put(Vector3 p) => Put(p, T.rotation);

        // The ground (or the sea, whichever is higher) under `p`.
        protected static float GroundY(Vector3 p)
        {
            float y = WaterManager.GetWaterHeight(p);
            if (Physics.Raycast(new Vector3(p.x, p.y + 30f, p.z), Vector3.down, out var h, 80f, GameInfo.LevelLayer)) y = Mathf.Max(y, h.point.y);
            return y;
        }
        protected static float LandY(Vector3 p) => Physics.Raycast(new Vector3(p.x, p.y + 30f, p.z), Vector3.down, out var h, 80f, GameInfo.LevelLayer) ? h.point.y : WaterManager.GetWaterHeight(p);
        protected Vector3 Stand(Vector3 p)
        {
            // Measure the current mesh's bottom, not the one cached at spawn: a boss whose legs/claws animate (the Warden)
            // would otherwise stand on the cached box and float. Translation does not change the origin-to-bottom offset.
            var ground = new Vector3(p.x, GroundY(p), p.z);
            var b = BossLooks.WorldBounds(T);
            float bottom = b.min.y - T.position.y;
            return ground + Vector3.up * (0f - bottom);
        }

        // Walk (on the ground, turning as it goes) toward `goal` until within `stopAt`, `seconds` at most. Walls in its way
        // are trampled: it stops at one and breaks it.
        // Going round instead of standing still in front of every turret (Owen: "每隻都很笨重"): probe a few sides for a
        // clear way, remember it for a moment, and only trample when the way really is walled off.
        private Vector3 _detourDir;
        private float _detourUntil;
        private bool TryDetour(Vector3 dir, out Vector3 move)
        {
            move = Vector3.zero;
            if (Time.time < _detourUntil && _detourDir.sqrMagnitude > 0.01f && WallAhead(_detourDir) == null)
            {
                move = _detourDir;
                return true;
            }
            for (int i = 0; i < 8; i++)
            {
                float ang = (i % 2 == 0 ? 1f : -1f) * (40f + 25f * (i / 2));
                Vector3 side = (Quaternion.Euler(0f, ang, 0f) * dir).Flat().normalized;
                if (WallAhead(side) != null) continue;
                Vector3 next = Pos + side * 1.4f;
                if (Mathf.Abs(LandY(next) - LandY(Pos)) > 2.5f) continue;
                _detourDir = side; _detourUntil = Time.time + 0.8f; move = side;
                return true;
            }
            return false;
        }

        protected IEnumerator WalkTo(Vector3 goal, float speed, float stopAt, float seconds)
        {
            for (float t = 0f; t < seconds && Alive; t += Time.deltaTime)
            {
                Vector3 to = (goal - Pos).Flat();
                if (to.magnitude <= stopAt) yield break;
                Vector3 dir = to.normalized;
                Quaternion face = Quaternion.RotateTowards(T.rotation, Facing(dir), 220f * Time.deltaTime);
                var wall = WallAhead(dir);
                if (wall != null)
                {
                    // A turret or a short wall in the way: step around it if there is a way; otherwise break through it.
                    if (TryDetour(dir, out var side))
                    {
                        Vector3 step = Pos + side * speed * SlowFactor * Time.deltaTime;
                        Put(Stand(step), Quaternion.RotateTowards(T.rotation, Facing(side), 260f * Time.deltaTime));
                        yield return null;
                        continue;
                    }
                    Put(Pos, face);
                    yield return Trample(wall.Value);
                    continue;
                }
                Vector3 next = Pos + dir * speed * SlowFactor * Time.deltaTime;
                if (Mathf.Abs(LandY(next) - LandY(Pos)) > 2.5f) yield break; // a cliff
                Put(Stand(next), face);
                yield return null;
            }
        }

        private float _nextTrample;
        private IEnumerator Trample(PieceState wall)
        {
            if (Time.time >= _nextTrample)
            {
                _nextTrample = Time.time + 0.55f;
                BuildSystem.Instance.ServerDamagePiece(wall.Id, Scaled(55f));
                Send("stomp", wall.Pos, wall.Pos);
            }
            yield return null;
        }

        protected PieceState? WallAhead(Vector3 dir)
        {
            Vector3 ahead = Pos.Flat() + dir * (Reach * 0.8f + 0.8f);
            foreach (var p in BuildSystem.Instance.ServerPieces)
                if ((p.Type == PieceType.Wall || Catalog.IsTurret(p.Type)) && (p.Pos.Flat() - ahead).sqrMagnitude < 1.6f * 1.6f) return p;
            return null;
        }

        // A jump along an arc to `to` (it lands standing).
        protected IEnumerator Leap(Vector3 to, float height, float seconds, bool face = true)
        {
            Vector3 from = Pos;
            Quaternion r0 = T.rotation, r1 = face && (to - from).Flat().sqrMagnitude > 0.01f ? Facing(to - from) : r0;
            for (float t = 0f; t < seconds && Alive; t += Time.deltaTime)
            {
                float k = t / seconds;
                Put(Vector3.Lerp(from, to, k) + Vector3.up * height * 4f * k * (1f - k), Quaternion.Slerp(r0, r1, Mathf.Clamp01(k * 3f)));
                yield return null;
            }
            Put(to, r1);
        }

        // Glide to `to` (in the air or on the water) at `speed`, turning to face the way it goes.
        protected IEnumerator Glide(Vector3 to, float speed, float bank = 0f)
        {
            while (Alive)
            {
                Vector3 d = to - Pos;
                float step = speed * SlowFactor * Time.deltaTime;
                if (d.magnitude <= step) { Put(to); yield break; }
                Quaternion face = d.Flat().sqrMagnitude > 0.01f ? Facing(d) * Quaternion.Euler(0f, 0f, bank) : T.rotation;
                Put(Pos + d.normalized * step, Quaternion.RotateTowards(T.rotation, face, 120f * Time.deltaTime));
                yield return null;
            }
        }

        protected IEnumerator Turn(Vector3 toward, float seconds)
        {
            Quaternion r0 = T.rotation, r1 = Facing(toward - Pos);
            for (float t = 0f; t < seconds && Alive; t += Time.deltaTime) { Put(Pos, Quaternion.Slerp(r0, r1, t / seconds)); yield return null; }
            Put(Pos, r1);
        }

        // ---------- warnings ----------

        // Shows the warning on every screen (shape: circle, lane, cone, ring, box; see BossFx) and waits it out. A breakable
        // one is broken by `breakShare` of its health in players' damage meanwhile, or by the conch: then the move is off
        // (Broken; the brain's runner has already moved on to reeling, so code after the yield doesn't run).
        protected IEnumerator Warn(string shape, Vector3 a, Vector3 b, float size, float seconds, bool breakable = false, float breakShare = 0.04f, int extra = 0, Action<float> during = null)
        {
            Broken = false;
            Send("w." + shape, a, b, Mathf.RoundToInt(seconds * 1000f), Mathf.RoundToInt(size * 100f) + extra * 100000);
            if (breakable)
            {
                _breakable = true;
                _breakDealt = 0;
                _breakAt = Mathf.Max(1, Mathf.RoundToInt(MaxHp * breakShare));
                Send("winding", Pos + Vector3.up * (Reach + 0.3f), Pos, Mathf.RoundToInt(seconds * 1000f));
            }
            if (during == null) yield return seconds;
            else for (float t = 0f; t < seconds; t += Time.deltaTime) { during(t / seconds); yield return null; }
            _breakable = false;
        }

        // Several warnings at once (the brain casts them together, then waits once).
        protected void WarnOnly(string shape, Vector3 a, Vector3 b, float size, float seconds, int extra = 0) =>
            Send("w." + shape, a, b, Mathf.RoundToInt(seconds * 1000f), Mathf.RoundToInt(size * 100f) + extra * 100000);

        // ---------- hitting ----------

        // Damage that grows with the wave, like the fish's bites.
        protected int Scaled(float baseDmg) => Mathf.Max(1, Mathf.RoundToInt(baseDmg * (1f + 0.06f * (Wave - 1))));

        protected static IEnumerable<PieceState> Pieces => BuildSystem.Instance.ServerPieces;

        // Pieces, the lighthouse and players inside `inside` (a point test on the ground plane) take damage; players are thrown.
        protected int Hit(Func<Vector3, bool> inside, float pieceDmg, float playerDmg, float coreDmg, Vector3 from, float knock = 6f)
        {
            int n = 0;
            if (pieceDmg > 0f)
                foreach (var p in Pieces.Where(p => inside(p.Pos)).ToList()) { BuildSystem.Instance.ServerDamagePiece(p.Id, Scaled(pieceDmg)); n++; }
            if (coreDmg > 0f && inside(Core)) Siege.Instance.DamageCore(Scaled(coreDmg));
            if (playerDmg > 0f) HurtPlayers(inside, Scaled(playerDmg), from, knock);
            return n;
        }

        protected static void HurtPlayers(Func<Vector3, bool> inside, int dmg, Vector3 from, float knock)
        {
            foreach (var p in Targets.ToList())
                if (inside(p.Transform.position))
                {
                    Vector3 away = (p.Transform.position - from).Flat();
                    Siege.HurtPlayer(p, Mathf.Min(80, dmg), (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * knock + Vector3.up * knock * 0.5f);
                }
        }

        protected static IEnumerable<Player> Targets => AutoTest.IgnorePlayers ? Enumerable.Empty<Player>() : PlayerManager.AlivePlayers.Where(p => p && p.Transform);

        protected static Func<Vector3, bool> Circle(Vector3 c, float r) => p => (p - c).Flat().sqrMagnitude <= r * r;
        protected static Func<Vector3, bool> Lane(Vector3 a, Vector3 b, float width)
        {
            Vector3 ab = (b - a).Flat();
            float len = ab.magnitude;
            Vector3 dir = len > 0.01f ? ab / len : Vector3.forward;
            return p => { Vector3 ap = (p - a).Flat(); float along = Vector3.Dot(ap, dir); return along >= -0.5f && along <= len + 0.5f && (ap - dir * along).sqrMagnitude <= width * width * 0.25f; };
        }
        protected static Func<Vector3, bool> Cone(Vector3 apex, Vector3 dir, float range, float halfAngle)
        {
            Vector3 d = dir.Flat().normalized;
            return p => { Vector3 ap = (p - apex).Flat(); return ap.sqrMagnitude <= range * range && Vector3.Angle(d, ap) <= halfAngle; };
        }

        // Like Hit, but each piece, player and the lighthouse at most once per `done` (a charge, a roll, a sweep).
        protected void HitOnce(HashSet<int> done, HashSet<Player> hurt, Func<Vector3, bool> inside, float pieceDmg, float playerDmg, float coreDmg, Vector3 from, float knock = 6f)
        {
            foreach (var p in Pieces.Where(p => !done.Contains(p.Id) && inside(p.Pos)).ToList()) { done.Add(p.Id); BuildSystem.Instance.ServerDamagePiece(p.Id, Scaled(pieceDmg)); }
            if (coreDmg > 0f && !done.Contains(-1) && inside(Core)) { done.Add(-1); Siege.Instance.DamageCore(Scaled(coreDmg)); }
            foreach (var p in Targets.ToList())
                if (!hurt.Contains(p) && inside(p.Transform.position))
                {
                    hurt.Add(p);
                    Vector3 away = (p.Transform.position - from).Flat();
                    Siege.HurtPlayer(p, Mathf.Min(80, Scaled(playerDmg)), (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * knock + Vector3.up * knock * 0.5f);
                }
        }

        // ---------- picking targets ----------

        // Its moves in turn, weighted, not the same one twice running (a weight of 0 is off).
        private int _lastPick = -1;
        protected int Pick(params float[] w)
        {
            float sum = 0f;
            for (int i = 0; i < w.Length; i++) if (i != _lastPick || w.Count(x => x > 0f) <= 1) sum += Mathf.Max(0f, w[i]);
            float r = UnityEngine.Random.value * sum;
            for (int i = 0; i < w.Length; i++)
            {
                if ((i == _lastPick && w.Count(x => x > 0f) > 1) || w[i] <= 0f) continue;
                r -= w[i];
                if (r <= 0f) return _lastPick = i;
            }
            return _lastPick = Array.FindLastIndex(w, x => x > 0f);
        }

        // A breath between moves: shorter each ten waves, and in its second form.
        protected float Breath => Mathf.Lerp(3.2f, 1.6f, (Tier - 1) / 9f) * (Phase2 ? 0.7f : 1f);

        // From `a` along `dir` to the far side of the island (at most `max`).
        protected static Vector3 Across(Vector3 a, Vector3 dir, float max, float edge = 27f)
        {
            dir = dir.Flat().normalized;
            Vector3 c = (a - Center).Flat();
            float bq = Vector3.Dot(c, dir), cq = c.sqrMagnitude - edge * edge, disc = bq * bq - cq;
            float len = disc > 0f ? -bq + Mathf.Sqrt(disc) : max;
            Vector3 b = a + dir * Mathf.Clamp(len, 6f, max);
            b.y = GroundY(b);
            return b;
        }

        // Somewhere `min`..`max` from `near` on the island's land, toward the island's middle more often than not.
        protected static Vector3 SpotNear(Vector3 near, float min, float max)
        {
            for (int i = 0; i < 12; i++)
            {
                Vector3 p = near + Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward * UnityEngine.Random.Range(min, max);
                if ((p - Center).Flat().magnitude > 25f) continue;
                if (LandY(p) > WaterManager.GetWaterHeight(p) + 0.3f) { p.y = LandY(p); return p; }
            }
            var q = Vector3.Lerp(near, Center, 0.3f); q.y = GroundY(q); return q;
        }

        protected static EnemyKind KindOf(Creature c) => Enemies.Instance.TryGet(c, out var t) ? t.Kind : EnemyKind.Runner;

        protected static PieceState? Best(Func<PieceState, bool> where, Func<PieceState, float> score)
        {
            PieceState? best = null;
            float s = float.MinValue;
            foreach (var p in Pieces)
            {
                if (!where(p)) continue;
                float v = score(p);
                if (v > s) { s = v; best = p; }
            }
            return best;
        }
        protected PieceState? NearestPiece(float range, Func<PieceState, bool> where = null) => Best(p => (where == null || where(p)) && (p.Pos - Pos).Flat().magnitude <= range, p => -(p.Pos - Pos).Flat().sqrMagnitude);
        protected IEnumerable<PieceState> TurretsNear(float range) => Pieces.Where(p => Catalog.IsTurret(p.Type) && (p.Pos - Pos).Flat().magnitude <= range);

        // Owen: a boss's first target is the damage turrets, not the lighthouse or the walls.
        protected Vector3 TowerPoint(float range = 32f)
        {
            PieceState best = default;
            float bd = float.MaxValue;
            foreach (var p in Pieces)
            {
                if (!Catalog.IsTurret(p.Type) || p.Hp <= 0) continue;
                float d = (p.Pos - Pos).Flat().sqrMagnitude;
                if (d > range * range || d >= bd) continue;
                bd = d; best = p;
            }
            return best.Id != 0 ? best.Pos : Heart();
        }
        protected static Player SomePlayer(Vector3 near, float range) => Targets.Where(p => (p.Transform.position - near).Flat().magnitude <= range).OrderBy(_ => UnityEngine.Random.value).FirstOrDefault();

        // Where the fight is: the lighthouse, or the thickest of the buildings round it.
        protected Vector3 Heart()
        {
            var turret = Best(p => Catalog.IsTurret(p.Type), p => -(p.Pos - Pos).Flat().sqrMagnitude * 0.3f - (p.Pos - Core).Flat().sqrMagnitude);
            return turret.HasValue && UnityEngine.Random.value < 0.7f ? turret.Value.Pos : Core;
        }

        // A beach (0..2): its direction out from the middle, a spot on it, and one out at sea off it.
        protected static Vector3 GapDir(int gap) { float a = FortressIsland.GapAngles[((gap % 3) + 3) % 3] * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); }
        protected static int NearestGap(Vector3 p) { var d = (p - Center).Flat(); int best = 0; float b = 999f; for (int i = 0; i < 3; i++) { float a = Vector3.Angle(d, GapDir(i)); if (a < b) { b = a; best = i; } } return best; }

        // ---------- what it leaves behind ----------

        // Patches of ground that hurt (caltrops, venom, fire): players in them every half second, and pieces.
        private readonly List<(Vector3 at, float r, float until, int player, int piece)> _hazards = new List<(Vector3, float, float, int, int)>();
        private float _nextHazard;
        protected void Hazard(Vector3 at, float r, float seconds, int playerDmg, int pieceDmg) => _hazards.Add((at, r, Time.time + seconds, playerDmg, pieceDmg));
        private void Hazards()
        {
            if (_hazards.Count == 0 || Time.time < _nextHazard) return;
            _nextHazard = Time.time + 0.5f;
            _hazards.RemoveAll(h => Time.time > h.until);
            foreach (var h in _hazards)
            {
                if (h.piece > 0) foreach (var p in Pieces.Where(p => (p.Pos - h.at).Flat().sqrMagnitude <= h.r * h.r).ToList()) BuildSystem.Instance.ServerDamagePiece(p.Id, h.piece);
                if (h.player > 0) foreach (var p in Targets.ToList()) if ((p.Transform.position - h.at).Flat().sqrMagnitude <= h.r * h.r && Mathf.Abs(p.Transform.position.y - h.at.y) < 3f) Siege.HurtPlayer(p, h.player, Vector3.up);
            }
        }

        // Something it calls up, moved by its own little brain.
        protected Creature Summon(EnemyKind kind, Vector3 at, BossBrain brain, float hpShare)
        {
            int hp = Mathf.Max(1, Mathf.RoundToInt(EnemyCatalog.Of(kind).BaseHp * FortressPlugin.Instance.WaveHpMult * hpShare));
            var c = FortressPlugin.Instance.ServerSpawnPuppet(kind, at, Quaternion.LookRotation((Core - at).Flat().sqrMagnitude > 0.01f ? (Core - at).Flat() : Vector3.forward), hp);
            if (c) Bosses.Instance.ServerAddMinion(c, brain, kind, Wave);
            return c;
        }

        // ---------- the rest ----------

        protected void Send(string key, Vector3 a, Vector3 b, int arg = 0, int who = 0) => BossFx.Send(key, Body, a, b, arg, who);
        // A move's name across the screen: the first time it's used in the fight (`always`: every time, the ones to react to).
        private readonly HashSet<string> _shouted = new HashSet<string>();
        protected void Shout(string key, bool always = false) { if (_shouted.Add(key) || always) Fx.Announce(key); }

        // Every half second: what it stands on (and the lighthouse, if it's at it) takes a beating.
        public void Aura()
        {
            if (Pos.y - GroundY(Pos) > Foot + 2.5f) return; // up in the air
            float r = Reach + 0.6f;
            BuildSystem.Instance.ServerDamageArea(Pos, r, Mathf.RoundToInt((15 + Wave) * 0.5f));
            if ((Core - Pos).Flat().magnitude < r + 2f) Siege.Instance.DamageCore(Mathf.RoundToInt((20 + Wave) * 0.5f));
        }

        // Too slow: it throws itself at the lighthouse and is gone. A boss that is still nearly full does far more damage
        // than one that was already worn down (Owen: "扣王剩餘血量的比例"): 60 % of the core at full health, 15 % when spent.
        private void Crash()
        {
            float hp = MaxHp > 0 ? Mathf.Clamp01(Body._hp.Value / (float)MaxHp) : 1f;
            bool finalBoss = Kind == EnemyKind.Eye && Wave >= EnemyCatalog.FinalWave;
            if (finalBoss)
            {
                // The last boss must be killed to win (Owen): running out the clock is not a kill, it is the lighthouse
                // being smashed. No partial damage: the tower goes down and the run is lost.
                Siege.Instance.DamageCore(Siege.Instance.CoreMax);
                Fx.Send(FxKind.Crash, Core + Vector3.up * 2f, Core + Vector3.up * 2f);
                Fx.Announce("finalBossCrash");
            }
            else
            {
                Siege.Instance.DamageCore(Mathf.RoundToInt(Siege.Instance.CoreMax * Mathf.Lerp(0.15f, 0.6f, hp)));
                Fx.Send(FxKind.Crash, Core + Vector3.up * 2f, Core + Vector3.up * 2f);
                Fx.Announce("bossCrashed");
            }
            Report(false);
            AutoTest.Count("boss_crashed");
            Stop();
            Body.DestroyItem();
        }

        public virtual void Died()
        {
            Send("died", Pos, Pos, Mathf.RoundToInt(Reach * 100f));
            AutoTest.Count("boss_died_" + Kind);
        }

        public float CrashIn => Mathf.Max(1f, Born + Bosses.CrashAfter - Time.time);
    }

    // The game's own behaviour stays off the bodies we move: no swimming, crawling or biting on its own.
    [HarmonyPatch]
    internal static class BossPuppetPatches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(Fish), "UpdateMovement")]
        private static bool Fish(Fish __instance) => !Bosses.IsPuppet(__instance);

        [HarmonyPrefix, HarmonyPatch(typeof(AttackingFish), "UpdateMovement")]
        private static bool Attacking(AttackingFish __instance) => !Bosses.IsPuppet(__instance);

        [HarmonyPrefix, HarmonyPatch(typeof(AttackingFish), nameof(AttackingFish.OnCollision))]
        private static bool Bite(AttackingFish __instance) => !Bosses.IsPuppet(__instance);

        [HarmonyPrefix, HarmonyPatch(typeof(Crab), "UpdateMovement")]
        private static bool Crab(Crab __instance) => !Bosses.IsPuppet(__instance);

        // The game pops a creature that's sunk under the level back up onto the ground (or takes it away if there's none):
        // ours go under the sand and the sea on purpose (rising out of it, burrowing, diving).
        [HarmonyPrefix, HarmonyPatch(typeof(DazedUtils), nameof(DazedUtils.TryMoveItemFromUnderLevel))]
        private static bool Surface(Item item, ref bool __result)
        {
            if (!(item && item.Creature && Bosses.IsPuppet(item.Creature))) return true;
            __result = false;
            return false;
        }

        // The game's sync turns a body's physics back on whenever it starts simulating it (on spawn, on a bump): ours stay
        // kinematic, or they'd fall out of the sky and sink into the sea between our moves.
        [HarmonyPrefix, HarmonyPatch(typeof(RigidbodySync), nameof(RigidbodySync.SetKinematic))]
        private static void StayKinematic(RigidbodySync __instance, ref bool kinematic)
        {
            if (!kinematic && __instance.GetComponent<Creature>() is Creature c && Bosses.IsPuppet(c)) kinematic = true;
        }

        // Every hit on a creature ends here on the host.
        [HarmonyPrefix, HarmonyPatch(typeof(Creature), nameof(Creature.ServerChangeHp))]
        private static void Hurt(Creature __instance, ref int damage) => damage = Bosses.OnHurt(__instance, damage);
    }
}
