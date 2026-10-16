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
        private int _lastCoins; private bool _lastCoinsKnown; private string _coinPop = ""; private float _coinPopUntil;
        private int _lastReady; private bool _wasActive;
        private readonly HashSet<RunningFish> _walkers = new HashSet<RunningFish>();
        private readonly Dictionary<RunningFish, float> _nextAttack = new Dictionary<RunningFish, float>();

        // every client
        private SiegeState _view;
        private GameObject _core;
        // top bar
        private RectTransform _top;
        private GameObject _roundGroup;
        private TextMeshProUGUI _waveText, _hpText, _enemyText, _coinText, _phaseText, _titleText, _bossText;
        private Hud.Bar _hpBar, _bossBar;
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
        public int CoreMax => _server.CoreMax;

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
            _server = new SiegeState { Active = true, CorePos = pos, CoreHp = CoreShielded, CoreMax = CoreShielded, Coins = StartCoins.Value + Mathf.RoundToInt(Perks.Team(Gear.WarChest)), Building = true };
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
            if (FortressPlugin.Showcase.Contains(fish)) { fish.Rig.isKinematic = true; return true; } // test gallery: stand still
            if (!_server.Active || !_walkers.Contains(fish)) return false;
            if (fish.IsDead) { _walkers.Remove(fish); return false; }
            Enemies.Instance.TryGet(fish, out var tag);
            var stats = Enemies.StatsOf(tag);
            Rigidbody rig = fish.Rig;
            if (tag.Kind == EnemyKind.Spitter) { SteerSpitter(fish, rig, stats); return true; }
            if (fish.AttachedRod || rig.isKinematic || !Grounded(fish)) return true; // hold still until it lands

            Vector3 pos = fish.transform.position;
            // Stuck on a step, rock or tree (no progress and no attack for 6 s): hop it 2.5 m on toward the lighthouse.
            if (!_progress.TryGetValue(fish, out var prog) || (pos - prog.pos).sqrMagnitude > 0.5f * 0.5f) _progress[fish] = prog = (pos, Time.time);
            else if (Time.time - prog.time > 6f)
            {
                Vector3 toCore = (_server.CorePos - pos); toCore.y = 0f;
                Vector3 hop = pos + toCore.normalized * Mathf.Min(2.5f, toCore.magnitude * 0.5f);
                if (Physics.Raycast(hop + Vector3.up * 8f, Vector3.down, out var ground, 16f, GameInfo.LevelLayer)) hop = ground.point + Vector3.up * (0.3f + 0.3f * stats.Scale);
                rig.position = hop; fish.transform.position = hop;
                _progress[fish] = (hop, Time.time);
                BuildSystem.Log.LogInfo($"unstuck {tag.Kind} from {pos}");
                AutoTest.Count("unstuck");
                return true;
            }
            // Players who come close get chased and bitten (bombers just blow up on them).
            Player prey = Prey(pos, tag.Kind);
            if (prey && tag.Kind == EnemyKind.Bomber && (prey.Transform.position - pos).sqrMagnitude < 1.8f * 1.8f)
            {
                Explode(fish, Mathf.RoundToInt(WalkerDamage.Value * stats.Damage));
                return true;
            }
            Vector3 target = prey && tag.Kind != EnemyKind.Bomber ? prey.Transform.position : _server.CorePos;
            Vector3 to = target - pos; to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 0.01f ? to / dist : fish.transform.forward;
            fish.transform.forward = dir;

            if (prey && tag.Kind != EnemyKind.Bomber)
            {
                if (dist <= 1.3f + 0.5f * (stats.Scale - 1f)) { Halt(rig); Bite(fish, prey, stats); return true; }
            }
            else
            {
                float reach = CoreHitRange + 0.4f * (stats.Scale - 1f);
                if (dist <= reach) { Halt(rig); TryAttack(fish, null, tag, stats); return true; }
            }

            // Flanking: after being held at a wall for 3 s, walk sideways along it for a while looking for a way round.
            bool flanking = _flank.TryGetValue(fish, out var fl) && Time.time < fl.until;
            if (flanking) dir = fl.dir;
            fish.transform.forward = dir;

            Collider wall = WallAhead(pos, dir, stats);
            if (wall)
            {
                if (flanking) { _flank.Remove(fish); _blockedSince.Remove(fish); } // flank blocked too: just smash through
                else
                {
                    if (!_blockedSince.TryGetValue(fish, out float since)) _blockedSince[fish] = since = Time.time;
                    if (Time.time - since > 3f && tag.Kind != EnemyKind.Bomber && tag.Kind != EnemyKind.Tank && tag.Kind != EnemyKind.Boss)
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

        private readonly Dictionary<RunningFish, float> _blockedSince = new Dictionary<RunningFish, float>();
        private readonly Dictionary<RunningFish, (Vector3 dir, float until)> _flank = new Dictionary<RunningFish, (Vector3, float)>();

        // Overlap just ahead (a cast would miss a wall the fish is already pressed against).
        private static Collider WallAhead(Vector3 pos, Vector3 dir, EnemyStats stats)
        {
            int mask = 1 << BuildSystem.PieceLayer;
            float r = 0.45f * Mathf.Max(1f, stats.Scale * 0.8f);
            foreach (var col in Physics.OverlapSphere(pos + dir * (0.6f * stats.Scale) + Vector3.up * 0.4f * stats.Scale, r, mask, QueryTriggerInteraction.Ignore))
                if (BuildSystem.IsWall(col)) return col;
            return null;
        }

        private readonly Dictionary<RunningFish, (Vector3 pos, float time)> _progress = new Dictionary<RunningFish, (Vector3, float)>();

        private static void Halt(Rigidbody rig) => rig.linearVelocity = new Vector3(0f, rig.linearVelocity.y, 0f);

        private void TryAttack(RunningFish fish, Collider wall, EnemyTag tag, EnemyStats stats)
        {
            if (!InstanceFinder.IsServerStarted) return;
            _nextAttack.TryGetValue(fish, out float next);
            if (Time.time < next) return;
            _nextAttack[fish] = Time.time + AttackInterval;
            _progress[fish] = (fish.transform.position, Time.time); // attacking counts as progress
            int dmg = Mathf.RoundToInt(WalkerDamage.Value * stats.Damage * (1f + 0.08f * Mathf.Max(0, _server.Wave - 1))); // hits harder each wave
            if (tag.Kind == EnemyKind.Bomber) { Explode(fish, dmg); return; }
            if (wall) { BuildSystem.Instance.ServerDamage(wall, Mathf.RoundToInt(dmg * stats.WallMult)); AutoTest.Count("walker_hit_piece"); }
            else { DamageCore(Mathf.Max(1, dmg / 2)); AutoTest.Count("walker_hit_core"); } // the lighthouse is sturdier than a wall
        }

        // ---------- going after players ----------

        private static float AggroRange(EnemyKind k) => k == EnemyKind.Charger || k == EnemyKind.Boss ? 7f : k == EnemyKind.Tank ? 4f : 5f;

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
            int dmg = Mathf.Min(60, Mathf.RoundToInt(12f * stats.Damage * (1f + 0.05f * Mathf.Max(0, _server.Wave - 1))));
            HurtPlayer(prey, dmg, fish.transform.forward * 4f + Vector3.up * 2f);
        }

        public static void HurtPlayer(Player p, int dmg, Vector3 force)
        {
            if (!p || !Server.Instance) return;
            dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * (1f - Perks.Of(p, Gear.SharkArmor))));
            Server.Instance.HitPlayer(p, dmg, force, p.Transform.position, 3);
            AutoTest.Count("player_hits");
        }

        private static void HurtPlayersAround(Vector3 at, float radius, int dmg)
        {
            foreach (var p in Targets.ToList())
                if (p && p.Transform && (p.Transform.position - at).sqrMagnitude <= radius * radius)
                    HurtPlayer(p, dmg, (p.Transform.position - at).normalized * 6f + Vector3.up * 3f);
        }

        private static int CoreShielded => Mathf.RoundToInt(CoreMaxHp.Value * (1f + Perks.Team(Gear.CoreShield)));

        public void DamageCore(int dmg) => _server.CoreHp = Mathf.Clamp(_server.CoreHp - dmg, 0, _server.CoreMax);

        // Bomber: blows up on whatever stopped it, hurting every piece (and the core) around.
        private void Explode(RunningFish fish, int dmg)
        {
            Vector3 at = fish.transform.position + Vector3.up * 0.5f;
            BuildSystem.Instance.ServerDamageArea(at, 3.5f, dmg);
            if ((at - _server.CorePos).sqrMagnitude < 4.5f * 4.5f) DamageCore(dmg / 2);
            HurtPlayersAround(at, 3.5f, 35);
            Fx.Send(FxKind.Blast, at, at);
            fish.ServerChangeHp(100000);
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
            BuildSystem.Instance.ServerDamageArea(at, 1.6f, dmg);
            if ((at - _server.CorePos).sqrMagnitude < 2.5f * 2.5f) DamageCore(dmg);
            HurtPlayersAround(at, 2f, 15);
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
            // Sounds for the ready countdown and the round starting.
            if (_view.ReadyCountdown != _lastReady) { if (_view.ReadyCountdown > 0) Fx.Sound("Click", _view.CorePos, 1f, AudioDistance.Long); _lastReady = _view.ReadyCountdown; }
            if (_view.Active && !_wasActive) { Fx.Sound("QuestFinished", _view.CorePos, 1f, AudioDistance.Long); _lastCoinsKnown = false; }
            _wasActive = _view.Active;

            if (!_view.Active)
            {
                if (_core) Destroy(_core);
                // Between rounds: on the island, explain how to start.
                bool near = FortressIsland.Instance && Player.LocalPlayer && Player.LocalPlayer.Transform
                            && FortressIsland.Instance.OnIsland(Player.LocalPlayer.Transform.position, FortressIsland.IslandRadius + 6f);
                if (!near || !EnsureHud()) { ShowHud(false); return; }
                ShowHud(true);
                _roundGroup.SetActive(false);
                _bossBox.gameObject.SetActive(false);
                _titleText.gameObject.SetActive(true);
                _titleText.text = L10n.T("islandName");
                SetPhase(_view.ReadyCountdown > 0 ? $"<color={Hud.Hex(Hud.Accent)}><b>{L10n.T("readyCountdown", _view.ReadyCountdown)}</b></color>"
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

            if (!EnsureHud()) return;
            ShowHud(true);
            _roundGroup.SetActive(true);
            _titleText.gameObject.SetActive(false);
            if (_view.Coins > _lastCoins && _lastCoinsKnown) { _coinPop = "+" + (_view.Coins - _lastCoins); _coinPopUntil = Time.time + 1.2f; Fx.Sound("Sell_01", _view.CorePos, 0.4f, AudioDistance.Long); }
            _lastCoins = _view.Coins; _lastCoinsKnown = true;
            if (_view.CoreHp < _lastCoreHp) _hpFlashUntil = Time.time + 0.25f;
            _lastCoreHp = _view.CoreHp;

            float hp = _view.CoreMax > 0 ? (float)_view.CoreHp / _view.CoreMax : 1f;
            _waveText.text = L10n.T("hudWave", _view.Wave);
            _hpBar.Set(hp, Time.time < _hpFlashUntil ? Color.white : Hud.HealthColor(hp));
            _hpText.text = $"{_view.CoreHp}<size=70%><color={Hud.Hex(Hud.Muted)}> / {_view.CoreMax}</color></size>";
            _enemyText.text = _view.Remaining.ToString();
            _coinText.text = _view.Coins + (Time.time < _coinPopUntil ? $" <size=72%><color={Hud.Hex(Hud.Good)}>{_coinPop}</color></size>" : "");
            bool host = InstanceFinder.IsServerStarted;
            SetPhase(_view.Building ? L10n.T("buildPhase", _view.Countdown) + (host ? "　" + L10n.T("phaseSkip", FortressPlugin.Instance.ReadyKeyName) : "")
                                    : $"<color={Hud.Hex(Hud.Accent)}>{L10n.T("fightPhase")}</color>");
            bool boss = _view.BossMax > 0;
            _bossBox.gameObject.SetActive(boss);
            if (boss)
            {
                _bossText.text = L10n.T("bossName");
                _bossBar.Set((float)_view.BossHp / _view.BossMax);
            }
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
            _bossBar = Hud.Bar.Make(_bossBox.transform, L, new Vector2(206f, 0f), new Vector2(436f, 18f), Hud.Bad);
            return true;
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
