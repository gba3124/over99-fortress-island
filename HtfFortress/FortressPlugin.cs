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
    [BepInPlugin("owen.htf.fortress", "OVER 99", Version)]
    public class FortressPlugin : BaseUnityPlugin
    {
        public const string Version = "0.3.0";
        private ConfigEntry<Key> _toggleKey;
        private ConfigEntry<bool> _debugKeys;
        private ConfigEntry<bool> _verboseLogs;
        private ConfigEntry<Key> _menuKey;
        private ConfigEntry<float> _menuScale;
        private int _kills;
        private ConfigEntry<string> _pool;
        private ConfigEntry<int> _baseCount;
        private ConfigEntry<int> _countPerWave;
        private ConfigEntry<float> _hpPerWave;
        private ConfigEntry<float> _radius;
        private ConfigEntry<float> _breakSeconds;
        private bool _practice; // a test key made this round easier: it can't set a record
        private ConfigEntry<float> _walkerShare;
        private ConfigEntry<float> _walkerRadius;
        private ConfigEntry<Key> _readyKey;
        private ConfigEntry<float> _firstBuildSeconds;
        private ConfigEntry<int> _killReward;
        private ConfigEntry<int> _waveBonus;

        // Wave creatures still alive; turrets pick targets from here.
        public static readonly List<Creature> Alive = new List<Creature>();
        private List<Creature> _alive => Alive;
        // Reused every frame (no garbage): see the round's Update.
        private static readonly List<Creature> _escaped = new List<Creature>(), _dead = new List<Creature>();
        private static readonly List<Vector3> _splits = new List<Vector3>();
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
        private bool _resuming;
        private bool _resumeTried;
        private float _nextBuildSave;
        private int _stackFrom;   // the first wave of the ones on the field now (more than one when called early): all paid when they're cleared
        private int _bestAtStart; // the save's record when this round began (it's raised wave by wave now)
        private float _nextWaveAt;
        private ConfigEntry<float> _spawnGap, _burstGap;
        private bool _warnedNoLand;
        private bool _hadAlivePlayers;

        private void Awake()
        {
            _instance = this;
            _toggleKey = Config.Bind("Debug", "ToggleKey", Key.F9, "Test key: host starts / ends a round (needs DebugKeys).");
            _verboseLogs = Config.Bind("Debug", "VerboseLog", false, "Detailed diagnostics in the BepInEx log: perf lines, per-wall breaches, unstuck teleports and wave screenshots. Leave off for normal play.");
            _debugKeys = Config.Bind("Debug", "DebugKeys", false, "Test keys for the host: F4 teleport to the fortress, F5 +$1,500,000 (Shift: $150M, Ctrl/Alt: $1.5B), F6 god mode, F7 kill all, F8 skip build time (Shift: jump 5 waves), F9 start/end a round. The clickable cheat menu opens with MenuKey.");
            _menuKey = Config.Bind("Debug", "MenuKey", Key.F12, "Open the clickable cheat menu (needs DebugKeys).");
            _showKeyHelp = Config.Bind("Debug", "ShowKeyHelp", false, "Show the test-key list in the top-right corner. Off by default: F12 (MenuKey) still opens the clickable menu.");
            _menuScale = Config.Bind("Debug", "MenuScale", 1f, new ConfigDescription("Cheat menu size.", new AcceptableValueRange<float>(0.6f, 1.8f)));
            // Basic host-facing model preset: the original single-piece turret heads (Owen preferred them), no
            // user-selectable modular art pass. Model options are deliberately few: classic heads, enemy/prop shadows
            // off, corpse cap 160; everything else is automatic.
            Catalog.ClassicHeads = true;
            FortressIsland.Unlock = Config.Bind("Island", "Unlock", IslandUnlock.AfterFinalBoss, "When the fortress island appears: after beating the final boss, or always.");
            Reveal.Seen = Config.Bind("Island", "RevealSeen", false, "The island's entrance (it rises out of the sea) has played on this computer. Set to false to see it again.");
            _pool = Config.Bind("Waves", "CreaturePool", "", "Comma-separated creature names to spawn. Empty = hostile creatures only. Names are listed in the BepInEx log on start.");
            _baseCount = Config.Bind("Waves", "BaseCount", 3, new ConfigDescription("Creatures in wave 1.", new AcceptableValueRange<int>(1, 50)));
            _countPerWave = Config.Bind("Waves", "CountPerWave", 2, new ConfigDescription("Extra creatures added each wave.", new AcceptableValueRange<int>(0, 20)));
            _hpPerWave = Config.Bind("Waves", "HpPerWave", 0.4f, new ConfigDescription("Extra HP multiplier per wave (0.4 = +40% each wave).", new AcceptableValueRange<float>(0f, 2f)));
            _radius = Config.Bind("Waves", "SpawnRadius", 6f, new ConfigDescription("Distance from the host where creatures appear.", new AcceptableValueRange<float>(3f, 80f)));
            // A wave used to arrive in three big bursts that instantiated everything at once (smooth for nobody, and a
            // stutter on every machine). Fish now walk in one after another, and the bursts are closer together.
            _spawnGap = Config.Bind("Waves", "SpawnGap", 0.06f, new ConfigDescription("Seconds between two fish of a wave appearing (0 = all at once).", new AcceptableValueRange<float>(0f, 0.5f)));
            _burstGap = Config.Bind("Waves", "BurstGap", 1.2f, new ConfigDescription("Seconds between the three bursts of a wave (was 3.5).", new AcceptableValueRange<float>(0f, 10f)));
            // New names so the longer defaults reach existing setups (the old BreakSeconds / FirstBuildSeconds were 25 / 45).
            _breakSeconds = Config.Bind("Waves", "BuildSecondsBetweenWaves", 90f, new ConfigDescription("Build time between waves (the host can press N to start the next wave sooner).", new AcceptableValueRange<float>(0f, 300f)));
            _walkerShare = Config.Bind("Siege", "WalkerShare", 0.5f, new ConfigDescription("Share of each wave that are walkers charging the core.", new AcceptableValueRange<float>(0f, 1f)));
            _walkerRadius = Config.Bind("Siege", "WalkerSpawnRadius", 25f, new ConfigDescription("How far from the core walkers appear (on land).", new AcceptableValueRange<float>(8f, 80f)));
            Siege.CoreMaxHp = Config.Bind("Siege", "CoreMaxHp", 1000, new ConfigDescription("Core health.", new AcceptableValueRange<int>(50, 10000)));
            Siege.WalkerDamage = Config.Bind("Siege", "WalkerDamage", 10, new ConfigDescription("Damage a walker deals per hit to walls and the core.", new AcceptableValueRange<int>(1, 500)));
            _readyKey = Config.Bind("Waves", "ReadyKey", Key.N, "Host presses this during build time to start the next wave right away, or holds it mid-wave to bring the next one early.");
            _firstBuildSeconds = Config.Bind("Waves", "BuildSecondsBeforeWave1", 90f, new ConfigDescription("Build time before wave 1 (the host can press N to start sooner).", new AcceptableValueRange<float>(0f, 300f)));
            Siege.MoneyScaleEntry = Config.Bind("Economy", "MoneyScale", Siege.DefaultMoneyScale, new ConfigDescription("The fortress costs and pays the game's own money: every price and reward is this many dollars per fortress coin.", new AcceptableValueRange<float>(0.1f, 100f)));
            // Prices went up (10 -> 30 dollars a coin): a config file still on the old default moves with it, a value set by hand stays.
            var economy = Config.Bind("Economy", "PriceVersion", 1, "Which price list this file was last moved to (don't change).");
            if (economy.Value < 2) { if (Mathf.Approximately(Siege.MoneyScaleEntry.Value, 10f)) Siege.MoneyScaleEntry.Value = Siege.DefaultMoneyScale; economy.Value = 2; }
            _killReward = Config.Bind("Economy", "KillReward", 3, new ConfigDescription("Coins per kill (plus the wave number).", new AcceptableValueRange<int>(0, 100)));
            _waveBonus = Config.Bind("Economy", "WaveBonus", 25, new ConfigDescription("Coins for clearing a wave (plus 6 x wave).", new AcceptableValueRange<int>(0, 500)));
            Turrets.DamageMultiplier = Config.Bind("Economy", "TurretDamage", 1f, new ConfigDescription("Turret damage multiplier.", new AcceptableValueRange<float>(0.1f, 10f)));
            HandDamage = Config.Bind("Economy", "HandDamage", 1f, new ConfigDescription("Multiplier on damage players' own weapons (guns, bow, knife, fists) deal to fortress enemies.", new AcceptableValueRange<float>(0.1f, 10f)));
            SellAllAnyPlayer = Config.Bind("Economy", "VaultSellAllAnyPlayer", false, "Also let friends use the vault's \"sell all\" buttons. Off: only the host can sell everything at once (the host's log records who did).");
            CorpseCap = Config.Bind("Performance", "CorpseCap", 160, new ConfigDescription("Most fish bodies left on the island at once; the oldest vanish beyond it. 110 was too tight for a late wave (about 200 fish), 160 keeps a full wave plus a backlog; raise it if your machines keep up.", new AcceptableValueRange<int>(40, 400)));
            EnemyShadows = Config.Bind("Performance", "EnemyShadows", false, "Let fortress enemies cast shadows. Off is much lighter with hundreds of fish; the models still show.");
            PropShadows = Config.Bind("Performance", "PropShadows", false, "Let the mod's small props (loot, chests, baskets, gulls) cast shadows. Off saves GPU: the shadow pass draws things that are off screen.");
            L10n.Setting = Config.Bind("General", "Language", LanguageSetting.Auto, "Language for Fortress messages. Auto follows the game (all 16 of its locales).");
            BuildSystem.BuildKey = Config.Bind("Build", "BuildKey", Key.G, "Toggle build mode.");
            Weapons.NetKey = Config.Bind("Weapons", "NetKey", Key.F, "Take out / put away the hand net (scoops dead fish; sell them at the fish stall).");
            Weapons.DamageMultiplier = Config.Bind("Economy", "WeaponDamage", 1f, new ConfigDescription("Multiplier on fortress weapon damage (laser gun, gas gun, boomerang, shark, ...). It already grows with the wave.", new AcceptableValueRange<float>(0.1f, 10f)));
            BuildSystem.Log = Logger;
            Models.Dir = Path.Combine(Path.GetDirectoryName(Info.Location), "Models");
            Hud.IconDir = Path.Combine(Path.GetDirectoryName(Info.Location), "Icons");
            gameObject.AddComponent<BuildSystem>();
            gameObject.AddComponent<Siege>();
            gameObject.AddComponent<Turrets>();
            gameObject.AddComponent<FortressIsland>();
            gameObject.AddComponent<Enemies>();
            gameObject.AddComponent<Fx>();
            gameObject.AddComponent<FortressNet>();
            gameObject.AddComponent<Helpers>();
            gameObject.AddComponent<Loot>();
            gameObject.AddComponent<VaultUI>();
            gameObject.AddComponent<StallMenu>();
            gameObject.AddComponent<KeeperMenu>();
            gameObject.AddComponent<CheatMenu>();
            gameObject.AddComponent<Construction>();
            gameObject.AddComponent<PieceMenu>();
            gameObject.AddComponent<Actives>();
            gameObject.AddComponent<Weapons>();
            gameObject.AddComponent<WeaponFx>();
            gameObject.AddComponent<VersionCheck>();
            gameObject.AddComponent<GearCards>();
            gameObject.AddComponent<HotbarBadges>();
            gameObject.AddComponent<Reveal>();
            gameObject.AddComponent<ItemIcons>();
            gameObject.AddComponent<Bosses>();
            gameObject.AddComponent<BossFx>();
            gameObject.AddComponent<PerfLog>(); // long-frame log: what clients see as "everything stops, then jumps"
            Markers.Show = Config.Bind("Players", "TeammateMarkers", true, "Show where your teammates are: a mark over their heads with name and distance (red while they're down and need carrying back), an arrow at the screen's edge when they're off it.");
            Unstuck.UnstuckKey = Config.Bind("Players", "UnstuckKey", Key.O, "Hold to get out when you're stuck in or between pieces (you're also freed by yourself when you're inside one, or push for 2 s and go nowhere).");
            gameObject.AddComponent<Markers>();
            gameObject.AddComponent<Unstuck>();
            Tips.Show = Config.Bind("Tips", "ShowTips", true, "Show a short tip card the first time something new happens.");
            Tips.Reset = Config.Bind("Tips", "ResetTips", false, "Set to true to see every tip card again.");
            ClientSettings.Bind(Config);
            ClientSettings.Add("setTeamMarkers", Markers.Show);
            gameObject.AddComponent<Tips>();
            gameObject.AddComponent<AutoTest>();
            // One class at a time: a patch that breaks after a game update must not take the others down with it.
            var harmony = new Harmony("owen.htf.fortress");
            foreach (var t in AccessTools.GetTypesFromAssembly(typeof(FortressPlugin).Assembly))
                try { harmony.CreateClassProcessor(t).Patch(); }
                catch (System.Exception e) { Logger.LogError($"Patch {t.Name} failed: {e.GetBaseException().Message}"); }
            Logger.LogInfo("OVER 99 (HTF Fortress) loaded.");
        }

        private static bool IsHost => Server.Instance && Server.Instance.IsServerInitialized;

        // Screenshots land in BepInEx/fortress-shots so they can be checked without watching the screen.
        public static void Shot(string tag, float delay = 0f) => _instance.StartCoroutine(_instance.ShotAfter(tag, delay));
        private static FortressPlugin _instance;
        public static FortressPlugin Instance => _instance;
        public bool Running => _running;
        public string ReadyKeyName => _readyKey.Value.ToString();
        public Key ReadyKey => _readyKey != null ? _readyKey.Value : Key.N;
        public void ServerSkipBuild() { if (InBuildTime) _nextWaveAt = Time.time; }
        public int Wave => _wave;
        public int Cleared => _cleared;
        public bool InBuildTime => _running && _nextWaveAt > 0f;
        public int WaveNow => _wave;

        // Host: the lighthouse keeper's soul skip (Owen): one soul for ten waves, only between waves, never past the save's
        // own record, and only once wave 100 has been cleared (souls are earned there and beyond).
        public bool ServerSkipTenWaves()
        {
            if (!InstanceFinder.IsServerStarted || !_running || _nextWaveAt <= 0f) return false;
            var v = Loot.Instance ? Loot.Instance.Vault : null;
            if (v == null || v.Best < 100 || v.Souls < 1) return false;
            int target = (_wave / 10 + 1) * 10;
            int cap = v.Best / 10 * 10;
            if (target > cap) return false;
            v.Souls--;
            _wave = target - 1;
            _nextWaveAt = Time.time;
            Loot.Instance.SaveVault();
            AutoTest.Count("skip_waves");
            Say(L10n.T("skipWaveDone", target, v.Souls));
            BuildSystem.Log.LogInfo($"keeper skip: wave {target - 10} -> {target}, souls left {v.Souls}");
            return true;
        }
        // Mid-wave the host can hold N to bring the next wave on top of this one (Owen asked): not while this one is still
        // landing, not a boss's wave (its entrance wants the stage to itself), not past wave 100.
        public const float CallHold = 0.4f; // a tap-and-hold, not a full second: N should feel like "next wave now"
        public bool CanCallNext => _running && _wave > 0 && _nextWaveAt == 0f && !_spawning && (_wave + 1) % 10 != 0 && _wave < EnemyCatalog.FinalWave;
        private float _callHeld;
        public void SkipBuildTime() { if (InBuildTime) _nextWaveAt = Time.time; }
        public void DebugExtendBuild(float seconds) { if (InBuildTime) _nextWaveAt = Time.time + seconds; }
        public void DebugJumpToWave(int wave) { _wave = wave - 1; _nextWaveAt = Time.time; }

        // Test key F1: a boss turns up as it would at its own wave (its entrance, its health and strength there).
        public void DebugSummonBoss(EnemyKind kind)
        {
            int wave = (System.Array.IndexOf(EnemyCatalog.BossOrder, kind) + 1) * 10, gap = UnityEngine.Random.Range(0, 3);
            var island = FortressIsland.Instance;
            Fx.Send(FxKind.BossIntro, island && island.Exists ? island.Center : Siege.Instance.CorePos, BossSpot(kind, gap), wave, (int)kind);
            StartCoroutine(SummonAfter(kind, gap, wave));
        }
        private IEnumerator SummonAfter(EnemyKind kind, int gap, int wave)
        {
            yield return new WaitForSeconds(Reveal.BossSpawnAt(wave));
            if (_running) ServerSpawnBoss(kind, HpMult(wave, _hpPerWave.Value), gap, wave, Reveal.BossSeconds(wave) - Reveal.BossSpawnAt(wave));
        }
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
        public static ConfigEntry<float> HandDamage;
        // Small mod props (loot, chests, baskets, gulls) casting shadows: the shadow camera draws casters you cannot see, so
        // turning it off is one of the cheapest GPU savings there is (Owen: "沒看的地方不渲染").
        public static ConfigEntry<bool> PropShadows;
        public static ConfigEntry<bool> EnemyShadows;
        public static ConfigEntry<int> CorpseCap;
        // A one-click sale throws a whole tab away: only the host may do it unless the team turns this on (Owen: a friend
        // emptied the stash). The sale itself still leaves a pre-sell snapshot in fortress-backups.
        public static ConfigEntry<bool> SellAllAnyPlayer;
        public float WaveHpMult => _hpMult; // this wave's health multiplier (things that should keep up with the fish use it)

        // Shift+F7: everything lying on the fortress island and in the water round it goes (weapons, fish, the game's items,
        // gear drops, the chest), not into the vault. Live fish and what anyone holds or carries stay.
        public int DebugClearGround()
        {
            var island = FortressIsland.Instance;
            if (island == null || !island.Exists) return 0;
            int n = 0;
            foreach (var it in FindObjectsOfType<Item>())
                if (it && it.IsSpawned && !it.IsDestroying && !it.Holder && !it.SyncedHolder && !it.IsInInventory && !it.DeadPlayer
                    && (!it.Creature || it.Creature.IsDead) && island.OnIsland(it.transform.position, FortressIsland.IslandRadius + 15f))
                { it.DestroyItem(); n++; }
            AutoTest.Count("ground_cleared", n);
            return n + Loot.Instance.ServerClearDrops();
        }

        public void DebugKillAll()
        {
            Bosses.TestKill = true; // a boss still making its entrance too
            try { foreach (var c in _alive.Where(c => c && c._hp.Value > 0).ToList()) c.ServerChangeHp(1000000); }
            finally { Bosses.TestKill = false; }
        }
        public void DebugStopRound() { if (_running) Stop(L10n.T("stopped")); }

        // Cheat menu: the new currencies and vault, so a tester does not have to grind to the wave that pays them.
        // Testing money, without a round: written straight into the game's wallet (the old F5 only paid during a round and
        // only $15,000, useless for a Lv20 turret).
        public void DebugMoney(long dollars)
        {
            if (!IsHost || MoneyManager.Instance == null) return;
            if (dollars > 0) MoneyManager.Instance._money.Value = (int)System.Math.Min(int.MaxValue, (long)MoneyManager.Instance._money.Value + dollars);
            AutoTest.Count("debug_money");
        }
        // Stress test (Owen: "一次叫一堆怪 讓我測試是不是因為怪太多才卡"): spawn a wave's worth of walkers around the
        // fortress without waiting for a wave. Keep it host-only, and use F7 to clear them afterwards.
        public void DebugSpawnHorde(int n)
        {
            if (!IsHost) return;
            if (!_running) { Say(L10n.T("testNotRunning")); return; }
            var island = FortressIsland.Instance;
            Vector3 c = island && island.Exists ? island.Center : (Siege.Instance ? Siege.Instance.CorePos : Player.LocalPlayer.Transform.position);
            n = Mathf.Clamp(n, 1, 500);
            for (int i = 0; i < n; i++)
            {
                float a = UnityEngine.Random.value * Mathf.PI * 2f;
                float r = UnityEngine.Random.Range(12f, 28f);
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (!Physics.Raycast(new Vector3(p.x, c.y + 40f, p.z), Vector3.down, out var hit, 120f, GameInfo.LevelLayer)
                    || hit.point.y < WaterManager.GetWaterHeight(hit.point) + 0.2f) { p = island && island.Exists ? island.RespawnPos : p; }
                else p = hit.point + Vector3.up * 0.6f;
                ServerSpawnMinion(EnemyKind.Runner, p);
            }
            AutoTest.Count("test_horde", n);
        }
        public void DebugSouls(int n) { if (!IsHost) return; var v = Loot.Instance ? Loot.Instance.Vault : null; if (v == null) return; v.Souls = Mathf.Max(0, v.Souls + n); Loot.Instance.SaveVault(); }
        public void DebugScales(int n) { if (!IsHost) return; var v = Loot.Instance ? Loot.Instance.Vault : null; if (v == null) return; v.Scales = Mathf.Max(0, v.Scales + n); Loot.Instance.SaveVault(); }
        public void DebugGear(int n)
        {
            if (!IsHost || Loot.Instance == null) return;
            for (int i = 0; i < n; i++) Loot.Instance.ServerGive(GearCatalog.RandomGear(), GearCatalog.Roll(Mathf.Max(1, _wave), Rarity.Common));
        }
        public void DebugUnlockAll()
        {
            if (!IsHost || Loot.Instance == null) return;
            var v = Loot.Instance.Vault;
            foreach (PieceType t in System.Enum.GetValues(typeof(PieceType))) if (Catalog.NeedsBlueprint(t)) v.Unlocked.Add(t);
            Loot.Instance.SaveVault();
        }

        // ---------- test keys (Debug / DebugKeys, host only; Mac keyboards need Fn for the F keys) ----------

        private UnityEngine.UI.Image _testHelp;
        public bool TestKeysOn => _debugKeys.Value;
        public bool VerboseLogs => _debugKeys.Value || (_verboseLogs != null && _verboseLogs.Value);
        public Key MenuKey => _menuKey != null ? _menuKey.Value : Key.F12;
        private static ConfigEntry<bool> _showKeyHelp;
        public bool ShowKeyHelp => _showKeyHelp != null && _showKeyHelp.Value;
        public Key ToggleKey => _toggleKey.Value;
        public int CorpseCapValue => CorpseCap != null ? CorpseCap.Value : 160;
        public bool EnemyShadowsOn => EnemyShadows != null && EnemyShadows.Value;
        public float MenuScale { get => _menuScale != null ? _menuScale.Value : 1f; set { if (_menuScale != null) _menuScale.Value = Mathf.Clamp(value, 0.6f, 1.8f); } }
        // Mine, or the host's: a friend in a host's world with test keys on has them too (the playtest's F5 did nothing).
        private bool KeysOn => _debugKeys.Value || (!IsHost && Siege.Instance && Siege.Instance.View.TestKeys);

        // Host: a friend's test key, done here (their own teleport is done on their screen).
        public void HostTestKey(TestKeyRequest r)
        {
            if (!_debugKeys.Value || (Key)r.Key == Key.F4) return;
            TestKey((Key)r.Key, r.Shift, r.Ctrl);
        }

        private void UpdateTestKeys(Keyboard kb)
        {
            if ((!_debugKeys.Value || !ShowKeyHelp) && _testHelp)
            {
                Destroy(_testHelp.gameObject);
                _testHelp = null;
            }
            if (!_testHelp && KeysOn && ShowKeyHelp && Hud.Font)
            {
                _testHelp = Hud.Box(Hud.Root, new Vector2(1f, 1f), new Vector2(-18f, -18f), new Vector2(330f, 290f), Hud.Panel, 14f);
                _testHelp.raycastTarget = true;
                var mb = _testHelp.gameObject.AddComponent<UnityEngine.UI.Button>();
                mb.onClick.AddListener(() => { if (CheatMenu.Instance) CheatMenu.Instance.Toggle(); }); // click the list: the clickable menu
                // A key a line, never wrapped (Owen: "很多地方的文字都會換行"): the panel is as wide as its longest line, and past
                // 560 the text shrinks instead.
                var t = Hud.Label(_testHelp.transform, Hud.Keys(L10n.T("testKeys") + "\n" + L10n.T("testKeysClick")), 19f, new Vector2(0f, 1f), new Vector2(16f, -12f), new Vector2(560f, 270f), TMPro.TextAlignmentOptions.TopLeft, pivot: new Vector2(0f, 1f));
                t.lineSpacing = 18f;
                t.enableWordWrapping = false;
                var need = t.GetPreferredValues(t.text, float.PositiveInfinity, float.PositiveInfinity);
                if (need.x > 560f) { t.fontSize *= 560f / need.x; need = t.GetPreferredValues(t.text, float.PositiveInfinity, float.PositiveInfinity); }
                t.rectTransform.sizeDelta = need;
                _testHelp.rectTransform.sizeDelta = need + new Vector2(32f, 26f);
            }
            if (_testHelp) _testHelp.gameObject.SetActive(KeysOn);
            if (!KeysOn || kb == null || ChatManager.IsTyping || Hud.ModalOpen) return;
            foreach (var key in new[] { Key.F1, Key.F2, Key.F3, Key.F5, Key.F6, Key.F7, Key.F8, Key.F10, Key.F11, _toggleKey.Value }) // (F4 is everyone's: Unstick)
                if (kb[key].wasPressedThisFrame) TestKey(key, kb.shiftKey.isPressed, kb.ctrlKey.isPressed || kb.leftAltKey.isPressed || kb.rightAltKey.isPressed);
        }

        // F4, for anyone at any time: back to the ready circle by the lighthouse (stuck in a rock or your own walls: the
        // playtest got stuck twice). Pushing to walk and going nowhere for 2.5 s brings up the hint.
        private readonly HashSet<int> _markedDead = new HashSet<int>();
        private Vector3 _stuckFrom;
        private float _stuckSince, _nextStuckHint;
        private void Unstick(Keyboard kb)
        {
            var me = Player.LocalPlayer;
            var island = FortressIsland.Instance;
            if (kb == null || !me || !me.Transform || island == null || !island.Exists || ChatManager.IsTyping || Hud.ModalOpen) return;
            if (kb.f4Key.wasPressedThisFrame)
            {
                if (!island.TrySafePlayerSpot(island.RespawnPos, out var to))
                {
                    Hud.Banner(L10n.T("islandNotReady"), 2.5f, Hud.Bad);
                    return;
                }
                BuildSystem.Log.LogInfo($"F4 island: root {island.RootPos}, sink {island.Sink:0.0}, to {to}");
                me.LocalTeleport(to, 0f, true);
                if (IsHost && _debugKeys.Value) island.ServerMoveBoatToBerth();
                AutoTest.Count("unstick_key");
                return;
            }
            Vector3 pos = me.Transform.position;
            bool walking = kb.wKey.isPressed || kb.aKey.isPressed || kb.sKey.isPressed || kb.dKey.isPressed;
            if (!walking || (pos - _stuckFrom).sqrMagnitude > 0.3f * 0.3f) { _stuckFrom = pos; _stuckSince = Time.time; return; }
            if (Time.time - _stuckSince > 2.5f && Time.time > _nextStuckHint && island.OnIsland(pos, FortressIsland.IslandRadius + 15f))
            {
                Hud.Banner(Hud.Keys(L10n.T("stuckHint")), 3.5f);
                _nextStuckHint = Time.time + 25f;
            }
        }

        // The battle held still (test key F11): no boss moves, no fish walk, no turret fires.
        public static bool Frozen;
        private int _testBoss = -1;

        public void TestKey(Key key, bool shift, bool ctrl = false)
        {
            if (!Player.LocalPlayer) return;
            if (!IsHost)
            {
                var isle = FortressIsland.Instance;
                if (key == Key.F4) { if (isle != null && isle.Exists) Player.LocalPlayer.LocalTeleport(isle.Center + FortressIsland.ReadyLocal + Vector3.up * 0.5f, 0f, true); return; }
                if (Siege.Instance && Siege.Instance.View.TestKeys) InstanceFinder.ClientManager.Broadcast(new TestKeyRequest { Key = (int)key, Shift = shift, Ctrl = ctrl });
                else Say(L10n.T("testHostOnly"));
                return;
            }
            var island = FortressIsland.Instance;
            if (key == _toggleKey.Value)
            {
                if (_running) Stop(L10n.T("stopped"));
                else StartRound(island != null && island.Exists ? island.CorePos : Player.LocalPlayer.Transform.position);
                return;
            }
            switch (key)
            {
                case Key.F5:
                    DebugMoney(ctrl ? 1_500_000_000L : shift ? 150_000_000L : 1_500_000L); // $1.5M / $150M / $1.5B
                    if (_running) _practice = true;
                    break;
                case Key.F6:
                    PlayerManager.ToggleGodMode();
                    _practice |= _running;
                    Say(L10n.T(PlayerManager.InGodMode ? "testGodOn" : "testGodOff"));
                    break;
                case Key.F7:
                    if (shift) { Say(L10n.T("testCleared", DebugClearGround())); break; }
                    if (ctrl) { DebugSpawnHorde(200); Say(L10n.T("testHorde", 200)); break; }
                    DebugKillAll();
                    _practice |= _running;
                    break;
                case Key.F1: // a boss right now, with its entrance: the next one in order (Shift: the same one again)
                {
                    if (!_running) { Say(L10n.T("testNotRunning")); break; }
                    if (!shift || _testBoss < 0) _testBoss = (_testBoss + 1) % EnemyCatalog.BossOrder.Length;
                    var kind = EnemyCatalog.BossOrder[_testBoss];
                    DebugSummonBoss(kind);
                    _practice = true;
                    Say(L10n.T("testBoss", L10n.T("bossName" + kind), (_testBoss + 1) * 10));
                    break;
                }
                case Key.F2: // the bosses on the field: a quarter of their health off (half is the second form); Shift: their move broken
                    foreach (var b in Bosses.Instance.All.Where(b => b.IsBoss).ToList())
                    {
                        if (shift) b.DebugBreak();
                        else b.Body.ServerChangeHp(Mathf.Max(1, b.MaxHp / 4));
                    }
                    _practice |= _running;
                    break;
                case Key.F3: // the lighthouse and every building mended; Shift: the ten bosses' trophies at your feet
                    if (shift)
                    {
                        Vector3 me = Player.LocalPlayer.Transform.position, fwd = Player.LocalPlayer.Transform.forward.Flat().normalized;
                        for (int i = 0; i < EnemyCatalog.BossOrder.Length; i++)
                            BossTrophies.ServerSpawn(EnemyCatalog.BossOrder[i], me + fwd * 2.5f + Quaternion.Euler(0f, (i - 4.5f) * 14f, 0f) * fwd * 1.5f + Vector3.up * 1.2f);
                        _practice |= _running;
                        break;
                    }
                    if (!_running) { Say(L10n.T("testNotRunning")); break; }
                    Siege.Instance.DamageCore(-Siege.Instance.CoreMax);
                    BuildSystem.Instance.ServerRepairArea(Siege.Instance.CorePos, 1000f, 1f);
                    _practice = true;
                    Say(L10n.T("testMended"));
                    break;
                case Key.F11:
                    if (!shift) { Say(L10n.T("testFreezeShift")); break; } // avoid freezing the whole battle by accident
                    Frozen = !Frozen;
                    Say(L10n.T(Frozen ? "testFrozen" : "testUnfrozen"));
                    break;
                case Key.F10: // the island's entrance again
                    if (_running) { Say(L10n.T("testNotDuringRound")); break; }
                    Reveal.Instance.Replay();
                    break;
                case Key.F8:
                    if (!_running) { Say(L10n.T("testNotRunning")); break; }
                    if (ctrl) { DebugKillAll(); if (FortressNet.Instance) FortressNet.Instance.ServerReset(); DebugJumpToWave((_wave / 10 + 1) * 10); _practice = true; Say(L10n.T("testJump", _wave + 1)); }
                    else if (shift) { DebugKillAll(); if (FortressNet.Instance) FortressNet.Instance.ServerReset(); DebugJumpToWave(_wave + 5); _practice = true; Say(L10n.T("testJump", _wave + 1)); }
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
            Unstick(kb);
            UpdateTestKeys(kb);
            if (!_running)
            {
                TryAutoResume();
                return;
            }
            // Checkpoint the build phase periodically too: a player can add towers after the wave clear before quitting.
            if (_nextWaveAt > 0f && Time.time >= _nextBuildSave)
            {
                _nextBuildSave = Time.time + 5f;
                RunSave.Save(_wave + 1);
            }
            if (!IsHost || !Player.LocalPlayer) { Stop(L10n.T("notHost")); return; }
            // Same squad-wipe check the game uses (PlayerDying.OnPlayerAmountChange).
            // Only once someone has been alive this round (a fresh save starts in a 'drowning' intro with nobody alive).
            // Someone down: mark where (everyone sees a pillar there; what they carried is hard to find otherwise).
            // Plain loops and reused lists: this runs every frame with every enemy, and the LINQ here made several kB of
            // garbage a frame. Mono's GC is stop-the-world, so that garbage showed up as the server freezing for a moment
            // every few seconds -- and every client's world with it.
            var players = PlayerManager.Players;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (!p || !p.Transform || !p.Dying) continue;
                bool down = p.Dying.IsDead;
                if (down && _markedDead.Add(p.ObjectId)) Fx.Send(FxKind.DeathMark, p.Transform.position, p.Transform.position, 0, 0, p.ObjectId);
                else if (!down) _markedDead.Remove(p.ObjectId);
            }
            if (PlayerManager.AlivePlayers.Count > 0) _hadAlivePlayers = true;
            else if (_hadAlivePlayers && _wave > 0) { Stop(L10n.T("wipe")); return; }
            if (Siege.Instance.CoreDestroyed) { Stop(L10n.T("coreDown")); return; }
            Stats.TrackDowns();

            // Creature.Hp is only filled in OnStartClient; the synced _hp is valid from spawn.
            // Anything that wandered far from the fight (or a sea creature that swam off) is removed.
            Vector3 corePos = Siege.Instance.CorePos;
            // (Not a boss: the albatross circles wide, and a boss that stays away crashes into the lighthouse in the end anyway.)
            _escaped.Clear();
            for (int i = 0; i < _alive.Count; i++)
            {
                var c = _alive[i];
                if (!c || c._hp.Value <= 0 || !c.IsSpawned) continue;
                if ((c.transform.position - corePos).sqrMagnitude <= 80f * 80f) continue;
                if (Enemies.Instance.TryGet(c, out var ft) && EnemyCatalog.IsBoss(ft.Kind)) continue;
                _escaped.Add(c);
            }
            for (int i = 0; i < _escaped.Count; i++)
            {
                var c = _escaped[i];
                _alive.Remove(c); _required.Remove(c); c.DestroyItem(); AutoTest.Count("escaped");
            }
            // Dead this frame: pay out per enemy type, then forget them.
            _dead.Clear();
            for (int i = 0; i < _alive.Count; i++) { var c = _alive[i]; if (!c || c._hp.Value <= 0) _dead.Add(c); }
            int killed = _dead.Count, reward = 0;
            _splits.Clear();
            foreach (var d in _dead)
            {
                if (!d) { killed--; continue; } // despawned (retreated / cleaned up), not killed: no reward
                int killer = Stats.Killed(d);
                bool tagged = Enemies.Instance.TryGet(d, out var tag);
                if (tagged) Loot.Instance.ServerOnKill(d.transform.position, tag.Kind, tag.Elite, killer, _wave);
                int r = tagged ? KillReward(tag, _wave) : Siege.Money(_killReward.Value);
                reward += r;
                if (tagged && EnemyCatalog.IsPuppet(tag.Kind)) reward += CorpseValue(r); // a boss (or its summons) blows apart: its body's worth is paid straight away
                else FortressNet.Instance.ServerAddCorpse(d, CorpseValue(r) + Siege.Instance.ServerTakeCarried(d)); // most of the money is in the bodies (a thief drops what it stole)
                if (tagged && tag.Kind == EnemyKind.Splitter) _splits.Add(d.transform.position);
                Enemies.Instance.ServerForget(d);
            }
            _alive.RemoveAll(c => !c || c._hp.Value <= 0);
            foreach (var at in _splits)
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
                    // The record goes up as each wave is held, not at the round's end: what needs "N waves held" (the vault's
                    // expansions) opens mid-round (Owen, at wave 23, couldn't buy the one for 20).
                    if (!_practice && !PlayerManager.InGodMode) Loot.Instance.ServerRecord(_cleared);
                    int bonus = 0; // every wave on the field: one, or more when the next ones were called early
                    for (int w = Mathf.Min(_stackFrom, _wave); w <= _wave; w++) bonus += WaveBonus(w, _waveBonus.Value, Perks.Team(Gear.WarChest));
                    if (Perks.TeamLegend(Gear.WarChest)) { bonus += Mathf.Min(Siege.Money(100), Siege.Instance.ServerCoins / 10); Perks.Proc(Gear.WarChest, Siege.Instance.CorePos + Vector3.up * 4f); } // legendary: 10% interest
                    Siege.Instance.ServerAddCoins(bonus);
                    if (_wave >= EnemyCatalog.FinalWave) { Victory(); return; } // OVER 99: wave 100 held, the run is won
                    Say(L10n.T("cleared", _wave, bonus, _breakSeconds.Value.ToString("0"), _readyKey.Value));
                    Fx.Announce("clearedBanner", _wave);
                    for (int w = Mathf.Min(_stackFrom, _wave); w <= _wave; w++) if (w % 5 == 0) Loot.Instance.ServerSpawnChest(w);
                }
                _nextWaveAt = Time.time + (_wave == 0 ? _firstBuildSeconds.Value : _breakSeconds.Value);
                RunSave.Save(_wave + 1); // cleared N: continue at the build phase before N+1
            }
            bool building = _nextWaveAt > 0f;
            if (building && kb != null && kb[_readyKey.Value].wasPressedThisFrame && !ChatManager.IsTyping) _nextWaveAt = Time.time;
            else if (!building) HoldForNextWave(kb);
            Siege.Instance.ServerProgress(_wave, _alive.Count, building, building ? Mathf.CeilToInt(_nextWaveAt - Time.time) : 0);
            if (_nextWaveAt > 0f && Time.time >= _nextWaveAt)
            {
                _nextWaveAt = 0f;
                SpawnWave(++_wave);
            }
        }

        // Held for CallHold seconds (a ring fills): the next wave comes now. Let go to call another.
        private void HoldForNextWave(Keyboard kb)
        {
            bool held = kb != null && kb[_readyKey.Value].isPressed && !ChatManager.IsTyping && !Hud.ModalOpen;
            if (!held) { _callHeld = 0f; return; }
            if (_callHeld < 0f || !CanCallNext) return; // just called one (let go first), or not now
            _callHeld += Time.deltaTime;
            Hud.Ring(_callHeld / CallHold, L10n.T("callingNext", _wave + 1), Hud.Accent);
            if (_callHeld < CallHold) return;
            _callHeld = -1f;
            DebugCallNext();
        }

        // The next wave now, on top of what's left of this one (they all have to die before it counts as cleared).
        public void DebugCallNext()
        {
            if (!CanCallNext) return;
            Say(L10n.T("calledEarly", _wave + 1));
            AutoTest.Count("wave_called_early");
            SpawnWave(++_wave, onTop: true);
        }

        public void StartRound(Vector3 corePos)
        {
            Loot.Instance.ServerNewRound();
            if (!IsHost) { ChatManager.ChatMessage(L10n.T("prefix") + " " + L10n.T("onlyHost")); return; }
            var pool = BuildPool();
            _walkerPrefabs = pool.Where(p => p.GetComponent<Creature>() is RunningFish).ToList();
            _poolPrefabs = pool.Except(_walkerPrefabs).ToList();
            if (pool.Count == 0) { ChatManager.ChatMessage(L10n.T("prefix") + " " + L10n.T("noCreatures")); return; }
            _running = true;
            _victory = false;
            Frozen = false; // a new round never starts with the test freezer still on
            _wave = 0;
            _cleared = 0;
            _warnedNoLand = false;
            _hadAlivePlayers = false;
            _practice = PlayerManager.InGodMode;
            _kills = 0;
            Stats.Reset();
            BuildSystem.Instance.RoundBuilt = 0;
            _nextWaveAt = 0f;
            _alive.Clear();
            MaxHpOf.Clear();
            _hpMult = 1f;
            FortressNet.Instance.ServerReset(keepSize: true); // everyone's net (and its size with their Big Net) from the first second
            Siege.Instance.ServerStart(corePos);
            _bestAtStart = Loot.Instance.Vault.Best;
            Say(L10n.T("start", Loot.Instance.Vault.Best));
            Say(L10n.T("startHint", BuildSystem.BuildKey.Value, _readyKey.Value));
            Say(L10n.T("fishTip"));
            Shot("start", 2f);
            if (!_resuming) { RunSave.Save(1); _nextBuildSave = Time.time + 5f; }
        }

        // A checkpoint from the last session: restore the fort and put the host back into the build phase for its wave.
        private void TryAutoResume()
        {
            if (_resumeTried || !IsHost || !InstanceFinder.IsServerStarted) return;
            var island = FortressIsland.Instance;
            if (!island || !island.Exists || PlayerManager.AlivePlayers.Count == 0) return;
            _resumeTried = true;
            if (!RunSave.Load(out var snap)) return;
            if (!island.Exists) return;
            _resuming = true;
            StartRound(island.CorePos);
            _resuming = false;
            if (!_running) return;
            _wave = Mathf.Max(1, snap.NextWave - 1);
            _cleared = _wave;
            _stackFrom = snap.NextWave;
            _nextWaveAt = Time.time + _breakSeconds.Value;
            _nextBuildSave = Time.time + 5f;
            BuildSystem.Instance.ServerRestorePieces(snap.Pieces);
            Siege.Instance.ServerRestoreCore(snap.CoreHp, snap.CoreMax);
            Siege.Instance.ServerSetCoins(snap.Coins);
            if (Player.LocalPlayer && Player.LocalPlayer.Transform) Player.LocalPlayer.LocalTeleport(island.RespawnPos, 0f, true);
            RunSave.Save(snap.NextWave);
            Say(L10n.T("runResumed", snap.NextWave));
            Fx.Announce("runResumedShort", snap.NextWave);
            BuildSystem.Log.LogInfo($"Run checkpoint resumed: wave {snap.NextWave}, {snap.Pieces.Count} pieces, {snap.CoreHp}/{snap.CoreMax} core, {snap.Coins} coins.");
        }

        // Wave 100 cleared: the whole run is beaten. Confetti over the lighthouse, then the results (with the win on them).
        private bool _victory;
        private void Victory()
        {
            if (Siege.Instance == null || Siege.Instance.CoreDestroyed) return; // the tower fell first: no win screen
            _victory = true;
            Vector3 top = Siege.Instance.CorePos + Vector3.up * 6f;
            Fx.Send(FxKind.Victory, top, top);
            Fx.Announce("victoryBanner");
            AutoTest.Count("victory");
            Stop(L10n.T("victory"));
        }

        private void Stop(string why)
        {
            Stats.TrackDowns(); // the wipe that ends the round is a down too
            // A quit to the menu keeps the checkpoint; a real loss or win ends the run. The build phase saves next wave, a
            // mid-wave quit saves the wave being replayed.
            bool naturalEnd = _victory || (Siege.Instance && Siege.Instance.CoreDestroyed)
                              || (_hadAlivePlayers && PlayerManager.AlivePlayers.Count == 0);
            if (naturalEnd) RunSave.Clear();
            else RunSave.Save(_nextWaveAt > 0f ? _wave + 1 : Mathf.Max(1, _wave));
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
            bool practice = _practice || PlayerManager.InGodMode;
            int soulsBefore = Loot.Instance.Vault.Souls, oldBest = Loot.Instance.Vault.Best;
            bool record = !practice && (Loot.Instance.ServerRecord(_cleared) || _cleared > _bestAtStart);
            int soulGain = Loot.Instance.Vault.Souls - soulsBefore;
            string soulLines = "";
            if (soulGain > 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int m = Mathf.Max(10, (oldBest / 10 + 1) * 10); m <= _cleared; m += 10) sb.Append(L10n.T("soulSettleBlock", m, Loot.SoulReward(m))).Append('\n');
                soulLines = sb.ToString().TrimEnd('\n');
            }
            if (soulGain > 0) Fx.Send(FxKind.Soul, Siege.Instance.CorePos + Vector3.up * 0.5f, Siege.Instance.CorePos + Vector3.up * 3.5f, Mathf.Min(12, soulGain / 100 + 1));
            if (_victory && !practice) Loot.Instance.ServerWin(string.Join("、", PlayerManager.Players.Where(p => p).Select(Loot.NameOf)));
            // The run's pay-off: money by the waves held, so the next run never starts from nothing (the playtest's idea).
            int settleBase = practice ? 0 : SettleBonus(_cleared);
            int settle = practice ? 0 : Mathf.RoundToInt(settleBase * Loot.Instance.Vault.SettleMult);
            int settleBonus = settle - settleBase;
            int scaleGain = Loot.Instance ? Loot.Instance.RoundScaleIncome : 0;
            int scaleSpent = Loot.Instance ? Loot.Instance.RoundScaleSpent : 0;
            if (settle > 0) { AutoTest.Count("settle_bonus", settle); if (MoneyManager.Instance) MoneyManager.Instance._money.Value += settle; Say(L10n.T("settleBonus", _cleared, settle)); Fx.Announce("settleBonusShort", settle); }
            int best = Loot.Instance.Vault.Best;
            string recordText = practice ? L10n.T("practice") : record ? L10n.T("newRecord") : L10n.T("best", best);
            Say(L10n.T("result", why, _cleared) + recordText);
            // Everyone sees the full-screen end beat, then the results panel; the field is cleared for the next round.
            if (_cleared > 0) Fx.Send(FxKind.RoundEnd, Siege.Instance.CorePos, Siege.Instance.CorePos, 0, _victory ? 1 : 0);
            InstanceFinder.ServerManager.Broadcast(new RoundResult { Waves = _cleared, Kills = _kills, Built = BuildSystem.Instance.RoundBuilt, Best = best, NewRecord = record, CoreLost = coreLost, Victory = _victory, Table = Stats.Table(), Souls = soulGain, SoulsTotal = Loot.Instance.Vault.Souls, SoulsFrom = oldBest, SoulLines = soulLines, Wins = Loot.Instance.Vault.Wins.Count, SettleBase = settleBase, SettleBonus = settleBonus, ScaleGain = scaleGain, ScaleSpent = scaleSpent, Practice = practice });
            _victory = false;
            Frozen = false; // a new round never starts with the test freezer still on
            BuildSystem.Instance.ServerClearAll();
            // If the host was pulled back to the main menu (or the session stopped), do not leave the build bar and menus
            // floating over it: that was the "stuck screen" in the playtest.
            if (BuildSystem.Instance) BuildSystem.Instance.ForceCloseUI();
            if (VaultUI.IsOpen && VaultUI.Instance) VaultUI.Instance.Close();
            if (StallMenu.IsOpen && StallMenu.Instance) StallMenu.Instance.Close();
            if (KeeperMenu.IsOpen && KeeperMenu.Instance) KeeperMenu.Instance.Close();
            if (PieceMenu.IsOpen && PieceMenu.Instance) PieceMenu.Instance.Close();
            if (Tips.Instance && Tips.HelpShown) Tips.Instance.CloseHelp();
            if (Weapons.Instance && Weapons.Drawn) Weapons.Instance.Holster();
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
        // Special fish have to be seen to be learned: Runner was 5 of 19.5 weight (a quarter of every wave), so the guide's
        // Charger/Spitter/Bomber/Thief/Shield/Digger rarely turned up. Runner is still the backbone, at about 2.5 of 18.7.
        private static readonly (EnemyKind kind, float weight)[] Mix =
            { (EnemyKind.Runner, 2.5f), (EnemyKind.Charger, 2.5f), (EnemyKind.Spitter, 2.2f), (EnemyKind.Bomber, 2.2f), (EnemyKind.Tank, 1.8f),
              (EnemyKind.Thief, 1.6f), (EnemyKind.Splitter, 1.6f), (EnemyKind.Healer, 1.3f), (EnemyKind.Shield, 1.6f), (EnemyKind.Digger, 1.4f) };

        private static EnemyKind PickKind(int wave)
        {
            var open = Mix.Where(m => EnemyCatalog.Of(m.kind).FirstWave <= wave).ToArray();
            float roll = UnityEngine.Random.Range(0f, open.Sum(m => m.weight));
            foreach (var m in open) { roll -= m.weight; if (roll <= 0f) return m.kind; }
            return EnemyKind.Runner;
        }

        // Coins for a kill (its body is worth 2 more in the net).
        // In the game's dollars. A body is worth its kill reward and a bit more (most of the money is in the bodies).
        public static int KillReward(EnemyTag tag, int wave)
        {
            // Money has to grow with the wave's health, not just +wave/4: a linear reward against a cubic difficulty curve
            // made late towers unaffordable (Owen's recording: 100 waves never earned an 80M tower).
            float rich = Mathf.Pow(HpMult(wave), 0.55f);
            return Siege.Money(Mathf.RoundToInt((Enemies.StatsOf(tag).Reward + wave / 4) * rich));
        }
        public static int SettleBonus(int waves) => waves > 0 ? Siege.Money(100 * waves) : 0; // $1,000 a wave (wave 40: $40,000)
        public static int CorpseValue(int killReward) => Siege.Money(3) + Mathf.RoundToInt(killReward * 1.5f); // (+50% after the playtest: fish were worth too little)
        // Clearing a wave: the WaveBonus setting plus 6 a wave, and the War Chest's share on top.
        public static int WaveBonus(int wave, int baseBonus = 25, float warChest = 0f) =>
            Siege.Money(Mathf.RoundToInt((baseBonus + 6 * wave) * Mathf.Pow(HpMult(wave), 0.45f) * (1f + warChest)));

        // What a wave sends: land attackers (the boss and its escort first on boss waves), sea harassers, and the health
        // multiplier. OVER 99: a hundred waves. The crowd stops growing at LandCap (that many fish is already a siege, and
        // more would choke the game); after that it's health that climbs (a cube term that makes the last tens brutal) and
        // the share of elites (none before wave 15, up to 60% by wave 75).
        public const int LandCap = 36;
        public static float HpMult(int wave, float hpPerWave = 0.4f)
        {
            float w = wave - 1;
            // Gentler than it was (0.4 / 0.05 / 0.0015): the fish no longer pull player weapons up with them, so the health
            // curve is what the weapon mods have to keep up with.
            return 1f + hpPerWave * w + 0.012f * w * w + 0.0002f * w * w * w + Mathf.Pow(Mathf.Max(0f, w - 30f), 3f) * 0.004f;
        }

        // A boss's health on top of that (Owen: "可能不是武器 是王血太少": a legendary with its mods took the wave-50 boss down
        // in two seconds, before it showed a move). x3.4 at wave 10, x20 at 50, x44 at 100: about half a minute or more
        // for a team kitted out as a team gets by then (Sim's boss lines), still well inside Bosses.CrashAfter.
        public static float BossHpMult(int wave) => Mathf.Max(1f, wave / 3f + wave * wave / 900f + Mathf.Pow(Mathf.Max(0f, wave - 30f), 3f) / 300f);

        public static (List<(EnemyKind kind, bool elite)> plan, int extras, float hpMult) PlanWave(int wave, int players, int baseCount, int perWave, float hpPerWave, bool seaPool)
        {
            float crowd = 1f + 0.4f * (Mathf.Max(1, players) - 1);
            int land = Mathf.RoundToInt(Mathf.Min(LandCap, baseCount + perWave * (wave - 1) + 2 * Mathf.Max(0, wave - 8)) * crowd);
            float hpMult = HpMult(wave, hpPerWave);
            bool bossWave = wave % 10 == 0, eliteWave = !bossWave && (wave % 5 == 0 || (wave > 10 && wave % 3 == 0));

            var plan = new List<(EnemyKind kind, bool elite)>();
            if (bossWave)
            {
                plan.Add((EnemyCatalog.BossFor(wave), false));
                land /= 2;
            }
            int elites = (eliteWave ? 2 + wave / 10 : 0) + Mathf.RoundToInt(land * Mathf.Clamp01((wave - 15) / 100f));
            for (int i = 0; i < land; i++) plan.Add((PickKind(wave), i < elites));
            return (plan, seaPool ? Mathf.Min(12, 1 + wave / 3) : 0, hpMult);
        }

        // `onTop`: called early, while the last wave's fish are still about: they stay ones to kill.
        private void SpawnWave(int wave, bool onTop = false)
        {
            RunSave.Save(wave); // replay this wave from its start if the session dies mid-fight
            _waveStarted = Time.time;
            _requiredSpawned = false;
            if (!onTop) { _required.Clear(); _stackFrom = wave; }
            var (plan, extras, hpMult) = PlanWave(wave, PlayerManager.Players.Count, _baseCount.Value, _countPerWave.Value, _hpPerWave.Value, _poolPrefabs.Count > 0);
            _hpMult = hpMult;
            bool bossWave = plan.Count > 0 && EnemyCatalog.IsBoss(plan[0].kind), eliteWave = plan.Any(p => p.elite);

            Vector3 host = Player.LocalPlayer.Transform.position;
            Vector3 core = Siege.Instance.CorePos;
            bool island = FortressIsland.Instance.Exists && FortressIsland.Instance.OnIsland(core);
            Item walkerPrefab = _walkerPrefabs.FirstOrDefault();

            // Three bursts, 3.5 s apart, spread evenly over the three beaches so the team has to split up.
            int gapStart = UnityEngine.Random.Range(0, 3);
            var shuffled = plan.OrderBy(p => EnemyCatalog.IsBoss(p.kind) ? 0 : 1).ThenBy(_ => UnityEngine.Random.value).ToList(); // the boss in the first burst
            _spawning = true;
            _requiredSpawned = shuffled.Count > 0 || _required.Count > 0;
            if (bossWave && island)
            {
                // The boss's entrance plays where it comes in: it turns up alone, stands for it (and roars), then the rest of
                // the wave comes (the frenzy's big fish with it).
                var kind = EnemyCatalog.BossFor(wave);
                var entry = shuffled.FirstOrDefault(p => p.kind == kind);
                shuffled.Remove(entry);
                Fx.Send(FxKind.BossIntro, FortressIsland.Instance.Center, BossSpot(kind, gapStart), wave, (int)kind);
                StartCoroutine(BossArrives(kind, gapStart, hpMult, core, walkerPrefab, wave, shuffled, extras, host));
            }
            else
            {
                StartCoroutine(SpawnBursts(shuffled, gapStart, hpMult, island, core, walkerPrefab, extras, host, wave));
                Fx.Announce(eliteWave ? "eliteBanner" : "waveBanner", wave);
            }
            Say(L10n.T("wave", wave, plan.Count + extras, hpMult.ToString("0.##")));
            Shot("wave" + wave, 4f);
        }

        private IEnumerator BossArrives(EnemyKind kind, int gap, float hpMult, Vector3 core, Item walkerPrefab, int wave, List<(EnemyKind kind, bool elite)> rest, int extras, Vector3 host)
        {
            yield return new WaitForSeconds(Reveal.BossSpawnAt(wave));
            if (!_running || _wave != wave) { _spawning = false; yield break; }
            ServerSpawnBoss(kind, hpMult, gap, wave, Reveal.BossSeconds(wave) - Reveal.BossSpawnAt(wave));
            yield return new WaitForSeconds(Reveal.BossSeconds(wave) - Reveal.BossSpawnAt(wave));
            if (_running && _wave == wave) yield return SpawnBursts(rest, gap + 1, hpMult, true, core, walkerPrefab, extras, host, wave);
            else _spawning = false;
        }

        // Where a boss comes in at a beach gap: on the sand, out at sea off it, or in the air over it. Its entrance plays there.
        public static Vector3 BossSpot(EnemyKind kind, int gap)
        {
            var island = FortressIsland.Instance;
            Vector3 c = island && island.Exists ? island.Center : Siege.Instance.CorePos;
            float a = FortressIsland.GapAngles[((gap % 3) + 3) % 3] * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            switch (EnemyCatalog.BodyOf(kind))
            {
                case EnemyCatalog.Body.Sea: { var p = c + dir * 31f; p.y = WaterManager.GetWaterHeight(p); return p; }
                case EnemyCatalog.Body.Sky: { var p = c + dir * 25f; p.y = Mathf.Max(WaterManager.GetWaterHeight(p), Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var g, 60f, GameInfo.LevelLayer) ? g.point.y : 0f) + 5f; return p; }
                default: { var p = c + dir * 22.5f + Vector3.up * 10f; return Physics.Raycast(p, Vector3.down, out var h, 20f, GameInfo.LevelLayer) ? h.point : p - Vector3.up * 9f; }
            }
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
                    int gap = (gapStart + i) % 3;
                    if (EnemyCatalog.IsBoss(kind)) { ServerSpawnBoss(kind, hpMult, gap, wave, 0f); continue; }
                    if (!walkerPrefab) break;
                    Vector3 pos;
                    if (kind == EnemyKind.Spitter && island) pos = FortressIsland.Instance.SpitterSpot();
                    else if (island) { if (!FortressIsland.Instance.WalkerSpawn(gap, out pos)) continue; }
                    else if (!FindLandAround(core, _walkerRadius.Value, out pos)) { if (!_warnedNoLand) { _warnedNoLand = true; Say(L10n.T("noLand")); } continue; }
                    var c = SpawnCreature(walkerPrefab, Spread(pos), 0f, Enemies.StatsOf(new EnemyTag { Kind = kind, Elite = elite }).BaseHp * hpMult);
                    Enemies.Instance.ServerTag(c, kind, elite);
                    Siege.Instance.RegisterWalker(c);
                    if (kind != EnemyKind.Spitter) _required.Add(c); // spitters only harass: they dive once the landing party is dead
                    AutoTest.Count("spawned_" + kind + (elite ? "_elite" : ""));
                    if (_spawnGap.Value > 0f) yield return new WaitForSeconds(_spawnGap.Value); // one at a time, not a burst
                }
                if (burst == 0)
                    for (int i = 0; i < extras; i++)
                    {
                        Item prefab = _poolPrefabs[UnityEngine.Random.Range(0, _poolPrefabs.Count)];
                        Vector3 pos = island ? FortressIsland.Instance.SeaSpawn() : FindSpawnPos(host, _radius.Value, WalksOnLand(prefab.GetComponent<Creature>()));
                        SpawnCreature(prefab, Spread(pos), 0f, 25f * hpMult); // harassers: about a walker's health
                        AutoTest.Count("sea_spawned");
                        if (_spawnGap.Value > 0f) yield return new WaitForSeconds(_spawnGap.Value);
                    }
                if (burst < 2) yield return new WaitForSeconds(_burstGap.Value);
            }
            _spawning = false;
        }

        // Something already standing there? Move this one along until it is not inside it (the fish shoved each other apart
        // when a burst spawned several into the same patch of sand).
        private Vector3 Spread(Vector3 pos, float min = 2.6f)
        {
            for (int pass = 0; pass < 6; pass++)
            {
                bool moved = false;
                for (int i = 0; i < _alive.Count; i++)
                {
                    var c = _alive[i];
                    if (!c) continue;
                    Vector3 d = pos - c.transform.position;
                    d.y = 0f;
                    float dist = d.magnitude;
                    if (dist >= min) continue;
                    Vector3 dir = dist > 0.05f ? d / dist : new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f)).normalized;
                    pos += dir * (min - dist + 0.2f);
                    moved = true;
                }
                if (!moved) break;
            }
            return pos;
        }

        private Creature SpawnCreature(Item prefab, Vector3 pos, float hpMult, float hp = 0f, float yaw = float.NaN)
        {
            Item item = Instantiate(prefab, pos, Quaternion.Euler(0f, float.IsNaN(yaw) ? UnityEngine.Random.Range(0f, 360f) : yaw, 0f));
            Creature c = item.Creature;
            c._hp.Value = Mathf.Max(1, Mathf.RoundToInt(hp > 0f ? hp : c.MaxHp * hpMult));
            MaxHpOf[c] = c._hp.Value;
            Server.Instance.Spawn(item.gameObject);
            _alive.Add(c);
            return c;
        }

        // A boss: its creature (BossLooks.BodyOf) at its spot (BossSpot), with our health (the wave's multiplier and a boss's
        // own, plus half again per extra player like the game does), handed to its brain (Bosses), which stands it for its
        // entrance (`hold` s) and then fights.
        public Creature ServerSpawnBoss(EnemyKind kind, float hpMult, int gap, int wave, float hold)
        {
            int hp = Mathf.Max(1, Mathf.RoundToInt(EnemyCatalog.Of(kind).BaseHp * hpMult * BossHpMult(wave) * (1f + 0.5f * (Mathf.Max(1, PlayerManager.Players.Count) - 1))));
            Vector3 at = BossSpot(kind, gap), toward = (Siege.Instance.CorePos - at).Flat();
            var c = ServerSpawnPuppet(kind, at, Quaternion.LookRotation(toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward), hp, BossBrain.ArrivalOffset(kind, at, FortressIsland.Instance && FortressIsland.Instance.Exists ? FortressIsland.Instance.Center : Siege.Instance.CorePos), out var home);
            if (!c) return null;
            _required.Add(c);
            Bosses.Instance.ServerAdd(c, kind, wave, hold, home);
            return c;
        }

        // One of the creatures the bosses are made of (or summon), spawned and tagged; Bosses moves it. Placed by its lowest
        // point: `at` is where its feet (or belly) go.
        public Creature ServerSpawnPuppet(EnemyKind kind, Vector3 at, Quaternion rot, int hp) => ServerSpawnPuppet(kind, at, rot, hp, Vector3.zero, out _);
        // `offset`: spawned that far from where it would stand (`home`), for its entrance to bring it in.
        public Creature ServerSpawnPuppet(EnemyKind kind, Vector3 at, Quaternion rot, int hp, Vector3 offset, out Vector3 home)
        {
            home = at;
            var all = (Dictionary<string, Item>)AccessTools.Field(typeof(GameInfo), "_nameToSpawnable").GetValue(null);
            if (!all.TryGetValue(BossLooks.BodyOf(kind) ?? "", out var prefab) || !prefab) { Logger.LogWarning($"no body for {kind}"); return null; }
            var item = Instantiate(prefab, at, rot * BossLooks.Turn(kind));
            item.transform.localScale = Vector3.one * EnemyCatalog.Of(kind).Scale;
            item.transform.position += Vector3.up * (at.y - BossLooks.WorldBounds(item.transform).min.y);
            home = item.transform.position;
            item.transform.position += offset;
            foreach (var rb in item.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
            var c = item.GetComponent<Creature>();
            c._hp.Value = hp;
            MaxHpOf[c] = hp;
            Bosses.Puppets.Add(c);
            IgnoreWater(c) = true; // under the sea or the sand on purpose: the game would take it for a drowned fish
            Server.Instance.Spawn(item.gameObject);
            _alive.Add(c);
            Enemies.Instance.ServerTag(c, kind, false);
            AutoTest.Count("spawned_" + kind);
            return c;
        }

        private static readonly AccessTools.FieldRef<Creature, bool> IgnoreWater = AccessTools.FieldRefAccess<Creature, bool>("_ignoreDeathByWater");

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
            if (!InstanceFinder.IsServerStarted) return; // the session ended while this was queued (no warning spam)
            try { Server.Instance.SendChatMessage(L10n.T("prefix") + " " + msg); }
            catch (Exception e) { Logger.LogWarning("Chat broadcast failed: " + e.Message); ChatManager.ChatMessage(L10n.T("prefix") + " " + msg); }
        }
    }


    // In a round nobody gets up on their own (Owen: "人不能自己復活吧 要塞模式的時候 要有同伴救 都死了就輸了"): holding the
    // mouse to give up and respawn does nothing while a round runs and someone's still standing; a friend picks up your
    // body and holds it to bring you back (the game's own revive). Everyone down ends the round (FortressPlugin.Update),
    // and then the game's own respawn takes over.
    [HarmonyPatch(typeof(PlayerDying), "MouseClick")]
    internal static class NoSelfRespawn
    {
        private static float _nextHint;
        private static bool Prefix()
        {
            if (!(Siege.Instance && Siege.Instance.ViewActive) || PlayerManager.AlivePlayers.Count == 0) return true;
            if (Time.time >= _nextHint) { _nextHint = Time.time + 4f; ChatManager.ChatMessage(L10n.T("prefix") + " " + L10n.T("downNoGiveUp")); }
            AutoTest.Count("give_up_blocked");
            return false;
        }
    }
}
