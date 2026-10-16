using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using FishNet;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HtfFortress
{
    // Fortress mode: host presses a key, a core appears where they stand, and waves of the game's own creatures attack.
    // Walkers (RunningFish) charge the core and break walls; the rest spawn near the host. Each wave is bigger and tougher.
    [BepInPlugin("owen.htf.fortress", "HTF Fortress", "0.5.0")]
    public class FortressPlugin : BaseUnityPlugin
    {
        private ConfigEntry<Key> _toggleKey;
        private ConfigEntry<bool> _debugKeys;
        private int _kills;
        private ConfigEntry<string> _pool;
        private ConfigEntry<int> _baseCount;
        private ConfigEntry<int> _countPerWave;
        private ConfigEntry<float> _hpPerWave;
        private ConfigEntry<float> _radius;
        private ConfigEntry<float> _breakSeconds;
        private ConfigEntry<int> _bestWave;
        private ConfigEntry<float> _walkerShare;
        private ConfigEntry<float> _walkerRadius;
        private ConfigEntry<Key> _readyKey;
        private ConfigEntry<float> _firstBuildSeconds;
        private ConfigEntry<int> _killReward;
        private ConfigEntry<int> _waveBonus;

        // Wave creatures still alive; turrets pick targets from here.
        public static readonly List<Creature> Alive = new List<Creature>();
        private List<Creature> _alive => Alive;
        // Enemies that must die to clear a wave (land attackers); sea creatures are extras and get cleaned up.
        private readonly HashSet<Creature> _required = new HashSet<Creature>();
        private float _waveStarted;
        private bool _requiredSpawned;
        private bool _spawning;
        private List<Item> _poolPrefabs = new List<Item>();
        private List<Item> _walkerPrefabs = new List<Item>();
        private bool _running;
        private int _wave;
        private int _cleared;
        private float _nextWaveAt;
        private bool _warnedNoLand;
        private bool _hadAlivePlayers;

        private void Awake()
        {
            _instance = this;
            _toggleKey = Config.Bind("Debug", "ToggleKey", Key.F9, "Test key: host starts / ends a round (needs DebugKeys).");
            _debugKeys = Config.Bind("Debug", "DebugKeys", false, "Test keys for the host: F4 teleport to the fortress, F5 +500 coins, F6 god mode, F7 kill all, F8 skip build time (Shift: jump 5 waves), F9 start/end a round.");
            FortressIsland.Unlock = Config.Bind("Island", "Unlock", IslandUnlock.AfterFinalBoss, "When the fortress island appears: after beating the final boss, or always.");
            _pool = Config.Bind("Waves", "CreaturePool", "", "Comma-separated creature names to spawn. Empty = hostile creatures only. Names are listed in the BepInEx log on start.");
            _baseCount = Config.Bind("Waves", "BaseCount", 3, new ConfigDescription("Creatures in wave 1.", new AcceptableValueRange<int>(1, 50)));
            _countPerWave = Config.Bind("Waves", "CountPerWave", 2, new ConfigDescription("Extra creatures added each wave.", new AcceptableValueRange<int>(0, 20)));
            _hpPerWave = Config.Bind("Waves", "HpPerWave", 0.4f, new ConfigDescription("Extra HP multiplier per wave (0.4 = +40% each wave).", new AcceptableValueRange<float>(0f, 2f)));
            _radius = Config.Bind("Waves", "SpawnRadius", 6f, new ConfigDescription("Distance from the host where creatures appear.", new AcceptableValueRange<float>(3f, 80f)));
            _breakSeconds = Config.Bind("Waves", "BreakSeconds", 25f, new ConfigDescription("Build time between waves.", new AcceptableValueRange<float>(0f, 60f)));
            _bestWave = Config.Bind("Stats", "BestWave", 0, "Most waves cleared in one run (saved automatically).");
            _walkerShare = Config.Bind("Siege", "WalkerShare", 0.5f, new ConfigDescription("Share of each wave that are walkers charging the core.", new AcceptableValueRange<float>(0f, 1f)));
            _walkerRadius = Config.Bind("Siege", "WalkerSpawnRadius", 25f, new ConfigDescription("How far from the core walkers appear (on land).", new AcceptableValueRange<float>(8f, 80f)));
            Siege.CoreMaxHp = Config.Bind("Siege", "CoreMaxHp", 1000, new ConfigDescription("Core health.", new AcceptableValueRange<int>(50, 10000)));
            Siege.WalkerDamage = Config.Bind("Siege", "WalkerDamage", 10, new ConfigDescription("Damage a walker deals per hit to walls and the core.", new AcceptableValueRange<int>(1, 500)));
            _readyKey = Config.Bind("Waves", "ReadyKey", Key.N, "Host presses this during build time to start the next wave right away.");
            _firstBuildSeconds = Config.Bind("Waves", "FirstBuildSeconds", 45f, new ConfigDescription("Build time before wave 1.", new AcceptableValueRange<float>(0f, 120f)));
            Siege.StartCoins = Config.Bind("Economy", "StartCoins", 150, new ConfigDescription("Team coins at the start of a run.", new AcceptableValueRange<int>(0, 10000)));
            _killReward = Config.Bind("Economy", "KillReward", 3, new ConfigDescription("Coins per kill (plus the wave number).", new AcceptableValueRange<int>(0, 100)));
            _waveBonus = Config.Bind("Economy", "WaveBonus", 25, new ConfigDescription("Coins for clearing a wave (plus 6 x wave).", new AcceptableValueRange<int>(0, 500)));
            Turrets.DamageMultiplier = Config.Bind("Economy", "TurretDamage", 1f, new ConfigDescription("Turret damage multiplier.", new AcceptableValueRange<float>(0.1f, 10f)));
            L10n.Setting = Config.Bind("General", "Language", LanguageSetting.Auto, "Language for Fortress messages. Auto follows the game.");
            BuildSystem.BuildKey = Config.Bind("Build", "BuildKey", Key.G, "Toggle build mode.");
            BuildSystem.Log = Logger;
            Models.Dir = Path.Combine(Path.GetDirectoryName(Info.Location), "Models");
            gameObject.AddComponent<BuildSystem>();
            gameObject.AddComponent<Siege>();
            gameObject.AddComponent<Turrets>();
            gameObject.AddComponent<FortressIsland>();
            gameObject.AddComponent<Enemies>();
            gameObject.AddComponent<Fx>();
            gameObject.AddComponent<FortressNet>();
            gameObject.AddComponent<Loot>();
            gameObject.AddComponent<VaultUI>();
            gameObject.AddComponent<Construction>();
            gameObject.AddComponent<PieceMenu>();
            gameObject.AddComponent<Actives>();
            gameObject.AddComponent<AutoTest>();
            // One class at a time: a patch that breaks after a game update must not take the others down with it.
            var harmony = new Harmony("owen.htf.fortress");
            foreach (var t in AccessTools.GetTypesFromAssembly(typeof(FortressPlugin).Assembly))
                try { harmony.CreateClassProcessor(t).Patch(); }
                catch (System.Exception e) { Logger.LogError($"Patch {t.Name} failed: {e.GetBaseException().Message}"); }
            Logger.LogInfo("HTF Fortress loaded.");
        }

        private static bool IsHost => Server.Instance && Server.Instance.IsServerInitialized;

        // Screenshots land in BepInEx/fortress-shots so they can be checked without watching the screen.
        public static void Shot(string tag, float delay = 0f) => _instance.StartCoroutine(_instance.ShotAfter(tag, delay));
        private static FortressPlugin _instance;
        public static FortressPlugin Instance => _instance;
        public bool Running => _running;
        public string ReadyKeyName => _readyKey.Value.ToString();
        public int Wave => _wave;
        public bool InBuildTime => _running && _nextWaveAt > 0f;
        public void SkipBuildTime() { if (InBuildTime) _nextWaveAt = Time.time; }
        public void DebugExtendBuild(float seconds) { if (InBuildTime) _nextWaveAt = Time.time + seconds; }
        public void DebugJumpToWave(int wave) { _wave = wave - 1; _nextWaveAt = Time.time; }
        // Test gallery: a frozen, tagged enemy at `pos` facing `yaw` (not part of any wave).
        public Creature DebugShowcase(EnemyKind kind, bool elite, Vector3 pos, float yaw)
        {
            var prefab = _walkerPrefabs.FirstOrDefault() ?? BuildPool().FirstOrDefault(p => p.GetComponent<Creature>() is RunningFish);
            if (!prefab) return null;
            Item item = Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f));
            item.Rig.isKinematic = true;
            Server.Instance.Spawn(item.gameObject);
            Enemies.Instance.ServerTag(item.Creature, kind, elite);
            Showcase.Add(item.Creature);
            return item.Creature;
        }

        public static readonly HashSet<Creature> Showcase = new HashSet<Creature>();

        public void DebugKillAll() { foreach (var c in _alive.Where(c => c && c._hp.Value > 0).ToList()) c.ServerChangeHp(1000000); }

        // ---------- test keys (Debug / DebugKeys, host only; Mac keyboards need Fn for the F keys) ----------

        private UnityEngine.UI.Image _testHelp;

        private void UpdateTestKeys(Keyboard kb)
        {
            if (!_testHelp && _debugKeys.Value && Hud.Font)
            {
                _testHelp = Hud.Box(Hud.Root, new Vector2(1f, 1f), new Vector2(-18f, -18f), new Vector2(330f, 290f), Hud.Panel, 14f);
                var t = Hud.Label(_testHelp.transform, Hud.Keys(L10n.T("testKeys")), 19f, new Vector2(0f, 1f), new Vector2(16f, -12f), new Vector2(300f, 270f), TMPro.TextAlignmentOptions.TopLeft, pivot: new Vector2(0f, 1f));
                t.lineSpacing = 18f;
                _testHelp.rectTransform.sizeDelta = new Vector2(330f, t.GetPreferredValues(t.text, 300f, 0f).y + 26f);
            }
            if (_testHelp) _testHelp.gameObject.SetActive(_debugKeys.Value);
            if (!_debugKeys.Value || kb == null || ChatManager.IsTyping || Hud.ModalOpen) return;
            foreach (var key in new[] { Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, _toggleKey.Value })
                if (kb[key].wasPressedThisFrame) TestKey(key, kb.shiftKey.isPressed);
        }

        public void TestKey(Key key, bool shift)
        {
            if (!Player.LocalPlayer) return;
            if (!IsHost) { Say(L10n.T("testHostOnly")); return; }
            var island = FortressIsland.Instance;
            if (key == _toggleKey.Value)
            {
                if (_running) Stop(L10n.T("stopped"));
                else StartRound(island != null && island.Exists ? island.CorePos : Player.LocalPlayer.Transform.position);
                return;
            }
            switch (key)
            {
                case Key.F4: // to the ready circle, boat to the berth
                    if (island == null || !island.Exists) { Say(L10n.T("testNoIsland")); break; }
                    Player.LocalPlayer.LocalTeleport(island.Center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true);
                    island.ServerMoveBoatToBerth();
                    break;
                case Key.F5:
                    if (_running) Siege.Instance.ServerAddCoins(500); else Say(L10n.T("testNotRunning"));
                    break;
                case Key.F6:
                    PlayerManager.ToggleGodMode();
                    Say(L10n.T(PlayerManager.InGodMode ? "testGodOn" : "testGodOff"));
                    break;
                case Key.F7:
                    DebugKillAll();
                    break;
                case Key.F8:
                    if (!_running) { Say(L10n.T("testNotRunning")); break; }
                    if (shift) { DebugKillAll(); DebugJumpToWave(_wave + 5); Say(L10n.T("testJump", _wave + 1)); }
                    else SkipBuildTime();
                    break;
            }
        }

        private IEnumerator ShotAfter(string tag, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            string dir = Path.Combine(Paths.BepInExRootPath, "fortress-shots");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, DateTime.Now.ToString("HHmmss") + "_" + tag + ".png");
            ScreenCapture.CaptureScreenshot(file);
            Logger.LogInfo("Screenshot: " + file);
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f10Key.wasPressedThisFrame) Shot("manual");
            UpdateTestKeys(kb);
            if (!_running) return;
            if (!IsHost || !Player.LocalPlayer) { Stop(L10n.T("notHost")); return; }
            // Same squad-wipe check the game uses (PlayerDying.OnPlayerAmountChange).
            // Only once someone has been alive this round (a fresh save starts in a 'drowning' intro with nobody alive).
            if (PlayerManager.AlivePlayers.Count > 0) _hadAlivePlayers = true;
            else if (_hadAlivePlayers && _wave > 0) { Stop(L10n.T("wipe")); return; }
            if (Siege.Instance.CoreDestroyed) { Stop(L10n.T("coreDown")); return; }
            Stats.TrackDowns();

            // Creature.Hp is only filled in OnStartClient; the synced _hp is valid from spawn.
            // Anything that wandered far from the fight (or a sea creature that swam off) is removed.
            Vector3 corePos = Siege.Instance.CorePos;
            foreach (var c in _alive.Where(c => c && c._hp.Value > 0 && c.IsSpawned && (c.transform.position - corePos).sqrMagnitude > 80f * 80f).ToList())
            { _alive.Remove(c); _required.Remove(c); c.DestroyItem(); AutoTest.Count("escaped"); }
            // Dead this frame: pay out per enemy type, then forget them.
            var dead = _alive.Where(c => !c || c._hp.Value <= 0).ToList();
            int killed = dead.Count, reward = 0;
            var splits = new List<Vector3>();
            foreach (var d in dead)
            {
                if (!d) { killed--; continue; } // despawned (retreated / cleaned up), not killed: no reward
                int killer = Stats.Killed(d);
                bool tagged = Enemies.Instance.TryGet(d, out var tag);
                if (tagged) Loot.Instance.ServerOnKill(d.transform.position, tag.Kind, tag.Elite, killer, _wave);
                int r = tagged ? Enemies.StatsOf(tag).Reward + _wave / 4 : _killReward.Value;
                reward += r;
                FortressNet.Instance.ServerAddCorpse(d, 2 + r + Siege.Instance.ServerTakeCarried(d)); // most of the money is in the bodies (a thief drops what it stole)
                if (tagged && tag.Kind == EnemyKind.Splitter) splits.Add(d.transform.position);
                Enemies.Instance.ServerForget(d);
            }
            _alive.RemoveAll(c => !c || c._hp.Value <= 0);
            foreach (var at in splits)
                for (int i = 0; i < 3; i++) ServerSpawnMinion(EnemyKind.Mini, at + Quaternion.Euler(0f, i * 120f, 0f) * Vector3.forward * 0.8f);
            _required.RemoveWhere(c => !c || c._hp.Value <= 0);
            // Wave is over when the land attackers are dead (or, if a wave had none, everything), or after 3 minutes for extras.
            bool waveDone = _wave > 0 && _nextWaveAt == 0f && !_spawning && (_requiredSpawned ? _required.Count == 0 : _alive.Count == 0);
            if (_wave > 0 && _nextWaveAt == 0f && Time.time - _waveStarted > 180f && _required.Count == 0) waveDone = true;
            // Hard cap: a wave never lasts more than 4 minutes (stragglers leave, no reward).
            if (_wave > 0 && _nextWaveAt == 0f && Time.time - _waveStarted > 240f)
            {
                foreach (var c in _required.Where(c => c).ToList())
                    BuildSystem.Log.LogInfo($"wave cap: removing {(Enemies.Instance.TryGet(c, out var t) ? t.Kind.ToString() : "?")} at {c.transform.position} (core {Siege.Instance.CorePos})");
                waveDone = true;
                AutoTest.Count("wave_capped");
            }
            if (waveDone && _alive.Count > 0)
            {
                foreach (var c in _alive.Where(c => c && c.IsSpawned).ToList()) c.DestroyItem();
                _alive.Clear();
            }
            if (killed > 0) { Siege.Instance.ServerAddCoins(reward); _kills += killed; AutoTest.Count("kills", killed); }

            if ((_wave == 0 || waveDone) && _alive.Count == 0 && _nextWaveAt == 0f) // not between bursts
            {
                if (_wave > 0)
                {
                    _cleared = _wave;
                    int bonus = _waveBonus.Value + 6 * _wave;
                    if (Perks.TeamLegend(Gear.WarChest)) bonus += Mathf.Min(100, Siege.Instance.ServerCoins / 10); // legendary: 10% interest
                    Siege.Instance.ServerAddCoins(bonus);
                    Say(L10n.T("cleared", _wave, bonus, _breakSeconds.Value.ToString("0"), _readyKey.Value));
                    Fx.Announce("clearedBanner", _wave);
                    if (_wave % 5 == 0) Loot.Instance.ServerSpawnChest(_wave);
                }
                _nextWaveAt = Time.time + (_wave == 0 ? _firstBuildSeconds.Value : _breakSeconds.Value);
            }
            bool building = _nextWaveAt > 0f;
            if (building && kb != null && kb[_readyKey.Value].wasPressedThisFrame && !ChatManager.IsTyping) _nextWaveAt = Time.time;
            Siege.Instance.ServerProgress(_wave, _alive.Count, building, building ? Mathf.CeilToInt(_nextWaveAt - Time.time) : 0);
            if (_nextWaveAt > 0f && Time.time >= _nextWaveAt)
            {
                _nextWaveAt = 0f;
                SpawnWave(++_wave);
            }
        }

        public void StartRound(Vector3 corePos)
        {
            if (!IsHost) { ChatManager.ChatMessage(L10n.T("prefix") + " " + L10n.T("onlyHost")); return; }
            var pool = BuildPool();
            _walkerPrefabs = pool.Where(p => p.GetComponent<Creature>() is RunningFish).ToList();
            _poolPrefabs = pool.Except(_walkerPrefabs).ToList();
            if (pool.Count == 0) { ChatManager.ChatMessage(L10n.T("prefix") + " " + L10n.T("noCreatures")); return; }
            _running = true;
            _wave = 0;
            _cleared = 0;
            _warnedNoLand = false;
            _hadAlivePlayers = false;
            _kills = 0;
            Stats.Reset();
            BuildSystem.Instance.RoundBuilt = 0;
            _nextWaveAt = 0f;
            _alive.Clear();
            Siege.Instance.ServerStart(corePos);
            Say(L10n.T("start", _bestWave.Value));
            Say(L10n.T("startHint", BuildSystem.BuildKey.Value, _readyKey.Value));
            Say(L10n.T("fishTip"));
            Shot("start", 2f);
        }

        private void Stop(string why)
        {
            Stats.TrackDowns(); // the wipe that ends the round is a down too
            _running = false;
            bool coreLost = Siege.Instance.CoreDestroyed;
            foreach (var c in _alive.Where(c => c && c.IsSpawned)) c.DestroyItem();
            _alive.Clear();
            _required.Clear();
            _spawning = false;
            Enemies.Instance.ServerClear();
            FortressNet.Instance.ServerReset();
            Siege.Instance.ServerStop();
            Loot.Instance.ServerCollectAll();
            bool record = _cleared > _bestWave.Value;
            string recordText = record ? L10n.T("newRecord") : L10n.T("best", _bestWave.Value);
            if (record) _bestWave.Value = _cleared;
            Say(L10n.T("result", why, _cleared) + recordText);
            // Everyone sees the results panel; the field is cleared for the next round.
            InstanceFinder.ServerManager.Broadcast(new RoundResult { Waves = _cleared, Kills = _kills, Built = BuildSystem.Instance.RoundBuilt, Best = _bestWave.Value, NewRecord = record, CoreLost = coreLost, Table = Stats.Table() });
            BuildSystem.Instance.ServerClearAll();
        }

        private List<Item> BuildPool()
        {
            var all = (Dictionary<string, Item>)AccessTools.Field(typeof(GameInfo), "_nameToSpawnable").GetValue(null);
            // Item.Creature is only set in Awake, so prefabs need GetComponent.
            var creatures = all.Where(kv => kv.Value && kv.Value.GetComponent<Creature>() is Creature c && c.BossType == BossType.None
                                            && !kv.Key.Contains("meat"))
                               .ToDictionary(kv => kv.Key, kv => kv.Value);
            Logger.LogInfo("Spawnable creatures: " + string.Join(", ", creatures.Keys.OrderBy(k => k)));

            var wanted = _pool.Value.Split(',').Select(s => s.Replace(" ", "").ToLower()).Where(s => s.Length > 0).ToList();
            if (wanted.Count == 0)
            {
                var hostile = creatures.Where(kv => IsHostile(kv.Value.GetComponent<Creature>())).ToList();
                Logger.LogInfo("Default pool (hostile): " + string.Join(", ", hostile.Select(kv => kv.Key + "(" + kv.Value.GetComponent<Creature>().GetType().Name + ")")));
                return hostile.Select(kv => kv.Value).ToList();
            }
            foreach (var w in wanted.Where(w => !creatures.ContainsKey(w)))
                Logger.LogWarning("CreaturePool: unknown creature '" + w + "'");
            return wanted.Where(creatures.ContainsKey).Select(w => creatures[w]).ToList();
        }

        // Land attackers by wave: new kinds join as waves go up, elites every 5th wave, a boss every 10th.
        private static readonly (EnemyKind kind, float weight)[] Mix =
            { (EnemyKind.Runner, 5f), (EnemyKind.Charger, 3f), (EnemyKind.Spitter, 2f), (EnemyKind.Bomber, 2f), (EnemyKind.Tank, 1.5f),
              (EnemyKind.Thief, 1.5f), (EnemyKind.Splitter, 1.5f), (EnemyKind.Healer, 1f), (EnemyKind.Shield, 1.5f), (EnemyKind.Digger, 1.2f) };

        private static EnemyKind PickKind(int wave)
        {
            var open = Mix.Where(m => EnemyCatalog.Of(m.kind).FirstWave <= wave).ToArray();
            float roll = UnityEngine.Random.Range(0f, open.Sum(m => m.weight));
            foreach (var m in open) { roll -= m.weight; if (roll <= 0f) return m.kind; }
            return EnemyKind.Runner;
        }

        private void SpawnWave(int wave)
        {
            _waveStarted = Time.time;
            _requiredSpawned = false;
            _required.Clear();
            int players = Mathf.Max(1, PlayerManager.Players.Count);
            float crowd = 1f + 0.4f * (players - 1);
            int land = Mathf.RoundToInt((_baseCount.Value + _countPerWave.Value * (wave - 1) + 2 * Mathf.Max(0, wave - 8)) * crowd);
            // Linear growth plus a gentle square term so late waves really bite.
            float hpMult = 1f + _hpPerWave.Value * (wave - 1) + 0.05f * (wave - 1) * (wave - 1);
            _hpMult = hpMult;
            bool bossWave = wave % 10 == 0, eliteWave = !bossWave && (wave % 5 == 0 || (wave > 10 && wave % 3 == 0));
            int elites = eliteWave ? 2 + wave / 10 : 0;

            var plan = new List<(EnemyKind kind, bool elite)>();
            if (bossWave) { plan.Add((EnemyCatalog.BossFor(wave), false)); land /= 2; }
            for (int i = 0; i < land; i++) plan.Add((PickKind(wave), i < elites));
            int extras = _poolPrefabs.Count > 0 ? 1 + wave / 3 : 0;

            Vector3 host = Player.LocalPlayer.Transform.position;
            Vector3 core = Siege.Instance.CorePos;
            bool island = FortressIsland.Instance.Exists && FortressIsland.Instance.OnIsland(core);
            Item walkerPrefab = _walkerPrefabs.FirstOrDefault();

            // Three bursts, 3.5 s apart, spread evenly over the three beaches so the team has to split up.
            int gapStart = UnityEngine.Random.Range(0, 3);
            var shuffled = plan.OrderBy(_ => UnityEngine.Random.value).ToList();
            _spawning = true;
            _requiredSpawned = shuffled.Count > 0;
            StartCoroutine(SpawnBursts(shuffled, gapStart, hpMult, island, core, walkerPrefab, extras, host, wave));
            Say(L10n.T("wave", wave, plan.Count + extras, hpMult.ToString("0.##")));
            Fx.Announce(bossWave ? "bossBanner" + EnemyCatalog.BossFor(wave) : eliteWave ? "eliteBanner" : "waveBanner", wave);
            Shot("wave" + wave, 4f);
        }

        // hp > 0 sets an absolute health; otherwise the game's own max health times hpMult.
        private IEnumerator SpawnBursts(List<(EnemyKind kind, bool elite)> plan, int gapStart, float hpMult, bool island, Vector3 core, Item walkerPrefab, int extras, Vector3 host, int wave)
        {
            for (int burst = 0; burst < 3; burst++)
            {
                if (!_running || _wave != wave) break;
                for (int i = burst; i < plan.Count; i += 3)
                {
                    var (kind, elite) = plan[i];
                    if (!walkerPrefab) break;
                    int gap = (gapStart + i) % 3;
                    Vector3 pos;
                    if (kind == EnemyKind.Spitter && island) pos = FortressIsland.Instance.SpitterSpot();
                    else if (island) { if (!FortressIsland.Instance.WalkerSpawn(gap, out pos)) continue; }
                    else if (!FindLandAround(core, _walkerRadius.Value, out pos)) { if (!_warnedNoLand) { _warnedNoLand = true; Say(L10n.T("noLand")); } continue; }
                    var c = SpawnCreature(walkerPrefab, pos, 0f, Enemies.StatsOf(new EnemyTag { Kind = kind, Elite = elite }).BaseHp * hpMult);
                    Enemies.Instance.ServerTag(c, kind, elite);
                    Siege.Instance.RegisterWalker(c);
                    if (kind != EnemyKind.Spitter) _required.Add(c); // spitters only harass: they dive once the landing party is dead
                    AutoTest.Count("spawned_" + kind + (elite ? "_elite" : ""));
                }
                if (burst == 0)
                    for (int i = 0; i < extras; i++)
                    {
                        Item prefab = _poolPrefabs[UnityEngine.Random.Range(0, _poolPrefabs.Count)];
                        Vector3 pos = island ? FortressIsland.Instance.SeaSpawn() : FindSpawnPos(host, _radius.Value, WalksOnLand(prefab.GetComponent<Creature>()));
                        SpawnCreature(prefab, pos, 0f, 25f * hpMult); // harassers: about a walker's health
                        AutoTest.Count("sea_spawned");
                    }
                if (burst < 2) yield return new WaitForSeconds(3.5f);
            }
            _spawning = false;
        }

        private Creature SpawnCreature(Item prefab, Vector3 pos, float hpMult, float hp = 0f)
        {
            Item item = Instantiate(prefab, pos, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            Creature c = item.Creature;
            c._hp.Value = Mathf.Max(1, Mathf.RoundToInt(hp > 0f ? hp : c.MaxHp * hpMult));
            MaxHpOf[c] = c._hp.Value;
            Server.Instance.Spawn(item.gameObject);
            _alive.Add(c);
            return c;
        }

        // Health each enemy spawned with (healers never top a fish past it).
        public readonly Dictionary<Creature, int> MaxHpOf = new Dictionary<Creature, int>();
        private float _hpMult = 1f;

        // Host: an extra walker mid-wave (splitter minis, the Queen's guards). It counts toward clearing the wave.
        public Creature ServerSpawnMinion(EnemyKind kind, Vector3 pos)
        {
            var prefab = _walkerPrefabs.FirstOrDefault();
            if (!_running || !prefab) return null;
            if (Physics.Raycast(pos + Vector3.up * 4f, Vector3.down, out var g, 10f, GameInfo.LevelLayer)) pos = g.point + Vector3.up * 0.5f;
            var c = SpawnCreature(prefab, pos, 0f, EnemyCatalog.Of(kind).BaseHp * _hpMult);
            Enemies.Instance.ServerTag(c, kind, false);
            Siege.Instance.RegisterWalker(c);
            _required.Add(c);
            AutoTest.Count("spawned_" + kind);
            return c;
        }

        // Walkers need ground: try the full radius first, then closer rings, so small islands still work.
        private static bool FindLandAround(Vector3 center, float radius, out Vector3 pos)
        {
            for (float r = radius; r >= 8f; r -= 4f)
                for (int i = 0; i < 8; i++)
                {
                    float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    Vector3 p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    if (Physics.Raycast(new Vector3(p.x, center.y + 40f, p.z), Vector3.down, out var rh, 120f, GameInfo.LevelLayer)
                        && rh.point.y > WaterManager.GetWaterHeight(rh.point) + 0.3f)
                    {
                        pos = rh.point + Vector3.up;
                        return true;
                    }
                }
            pos = default;
            return false;
        }

        private static bool WalksOnLand(Creature c) => c is RunningFish || c is Crab;

        private static bool IsHostile(Creature c) => c is AttackingFish || c is RunningFish || c is Crab;

        // ponytail: random ring sampling, upgrade to authored spawn points once the fortress island exists.
        private static Vector3 FindSpawnPos(Vector3 center, float radius, bool wantLand)
        {
            Vector3 fallback = center + Vector3.forward * radius;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                Vector3 p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                float water = WaterManager.GetWaterHeight(p);
                bool hit = Physics.Raycast(new Vector3(p.x, center.y + 40f, p.z), Vector3.down, out var rh, 120f, GameInfo.LevelLayer);
                bool isLand = hit && rh.point.y > water + 0.3f;
                Vector3 spot = isLand ? rh.point + Vector3.up * 1f : new Vector3(p.x, water - 0.5f, p.z);
                if (attempt == 0) fallback = spot;
                if (isLand == wantLand) return spot;
            }
            return fallback;
        }

        private void Say(string msg)
        {
            Logger.LogInfo(msg);
            try { Server.Instance.SendChatMessage(L10n.T("prefix") + " " + msg); }
            catch (Exception e) { Logger.LogWarning("Chat broadcast failed: " + e.Message); ChatManager.ChatMessage(L10n.T("prefix") + " " + msg); }
        }
    }
}
