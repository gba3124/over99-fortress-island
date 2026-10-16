using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HtfFortress
{
    // Prototype: host presses a key, waves of the game's own creatures spawn around them,
    // each wave bigger and tougher than the last.
    [BepInPlugin("owen.htf.fortress", "HTF Fortress", "0.1.0")]
    public class FortressPlugin : BaseUnityPlugin
    {
        private ConfigEntry<Key> _toggleKey;
        private ConfigEntry<string> _pool;
        private ConfigEntry<int> _baseCount;
        private ConfigEntry<int> _countPerWave;
        private ConfigEntry<float> _hpPerWave;
        private ConfigEntry<float> _radius;
        private ConfigEntry<float> _breakSeconds;
        private ConfigEntry<int> _bestWave;

        private readonly List<Creature> _alive = new List<Creature>();
        private List<Item> _poolPrefabs = new List<Item>();
        private bool _running;
        private int _wave;
        private int _cleared;
        private float _nextWaveAt;

        private void Awake()
        {
            _toggleKey = Config.Bind("Waves", "ToggleKey", Key.F9, "Host presses this to start / stop the wave mode.");
            _pool = Config.Bind("Waves", "CreaturePool", "", "Comma-separated creature names to spawn. Empty = hostile creatures only. Names are listed in the BepInEx log on start.");
            _baseCount = Config.Bind("Waves", "BaseCount", 3, new ConfigDescription("Creatures in wave 1.", new AcceptableValueRange<int>(1, 50)));
            _countPerWave = Config.Bind("Waves", "CountPerWave", 2, new ConfigDescription("Extra creatures added each wave.", new AcceptableValueRange<int>(0, 20)));
            _hpPerWave = Config.Bind("Waves", "HpPerWave", 0.25f, new ConfigDescription("Extra HP multiplier per wave (0.25 = +25% each wave).", new AcceptableValueRange<float>(0f, 2f)));
            _radius = Config.Bind("Waves", "SpawnRadius", 6f, new ConfigDescription("Distance from the host where creatures appear.", new AcceptableValueRange<float>(3f, 80f)));
            _breakSeconds = Config.Bind("Waves", "BreakSeconds", 10f, new ConfigDescription("Pause between waves.", new AcceptableValueRange<float>(0f, 60f)));
            _bestWave = Config.Bind("Stats", "BestWave", 0, "Most waves cleared in one run (saved automatically).");
            L10n.Setting = Config.Bind("General", "Language", LanguageSetting.Auto, "Language for Fortress messages. Auto follows the game.");
            BuildSystem.BuildKey = Config.Bind("Build", "BuildKey", Key.B, "Toggle build mode (place walls).");
            BuildSystem.Log = Logger;
            gameObject.AddComponent<BuildSystem>();
            Logger.LogInfo("HTF Fortress loaded. Host: press " + _toggleKey.Value + " in game to start waves.");
        }

        private static bool IsHost => Server.Instance && Server.Instance.IsServerInitialized;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[_toggleKey.Value].wasPressedThisFrame && !ChatManager.IsTyping)
            {
                if (_running) Stop(L10n.T("stopped"));
                else Start_();
            }
            if (!_running) return;
            if (!IsHost || !Player.LocalPlayer) { Stop(L10n.T("notHost")); return; }
            // Same squad-wipe check the game uses (PlayerDying.OnPlayerAmountChange).
            if (_wave > 0 && PlayerManager.AlivePlayers.Count == 0) { Stop(L10n.T("wipe")); return; }

            // Creature.Hp is only filled in OnStartClient; the synced _hp is valid from spawn.
            _alive.RemoveAll(c => !c || c._hp.Value <= 0);
            if (_alive.Count == 0 && _nextWaveAt == 0f)
            {
                if (_wave > 0) { _cleared = _wave; Say(L10n.T("cleared", _wave, _breakSeconds.Value.ToString("0"))); }
                _nextWaveAt = Time.time + (_wave == 0 ? 3f : _breakSeconds.Value);
            }
            if (_nextWaveAt > 0f && Time.time >= _nextWaveAt)
            {
                _nextWaveAt = 0f;
                SpawnWave(++_wave);
            }
        }

        private void Start_()
        {
            if (!IsHost) { ChatManager.ChatMessage(L10n.T("prefix") + " " + L10n.T("onlyHost")); return; }
            _poolPrefabs = BuildPool();
            if (_poolPrefabs.Count == 0) { ChatManager.ChatMessage(L10n.T("prefix") + " " + L10n.T("noCreatures")); return; }
            _running = true;
            _wave = 0;
            _cleared = 0;
            _nextWaveAt = 0f;
            _alive.Clear();
            Say(L10n.T("start", _poolPrefabs.Count, _bestWave.Value, _toggleKey.Value));
        }

        private void Stop(string why)
        {
            _running = false;
            foreach (var c in _alive.Where(c => c && c.IsSpawned)) c.DestroyItem();
            _alive.Clear();
            string record = _cleared > _bestWave.Value ? L10n.T("newRecord") : L10n.T("best", _bestWave.Value);
            if (_cleared > _bestWave.Value) _bestWave.Value = _cleared;
            Say(L10n.T("result", why, _cleared) + record);
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

        private void SpawnWave(int wave)
        {
            int count = _baseCount.Value + _countPerWave.Value * (wave - 1);
            float hpMult = 1f + _hpPerWave.Value * (wave - 1);
            Vector3 center = Player.LocalPlayer.transform.position;
            for (int i = 0; i < count; i++)
            {
                Item prefab = _poolPrefabs[UnityEngine.Random.Range(0, _poolPrefabs.Count)];
                Vector3 pos = FindSpawnPos(center, WalksOnLand(prefab.GetComponent<Creature>()));
                Item item = Instantiate(prefab, pos, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                Creature c = item.Creature;
                c._hp.Value = Mathf.Max(1, Mathf.RoundToInt(c.MaxHp * hpMult));
                Server.Instance.Spawn(item.gameObject);
                _alive.Add(c);
                Logger.LogInfo($"  spawned {prefab.name} at {pos} ({Vector3.Distance(center, pos):0}m)");
            }
            Say(L10n.T("wave", wave, count, hpMult.ToString("0.##")));
        }

        private static bool WalksOnLand(Creature c) => c is RunningFish || c is Crab;

        private static bool IsHostile(Creature c) => c is AttackingFish || c is RunningFish || c is Crab;

        // ponytail: random ring sampling, upgrade to authored spawn points once the fortress island exists.
        private Vector3 FindSpawnPos(Vector3 center, bool wantLand)
        {
            Vector3 fallback = center + Vector3.forward * _radius.Value;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                Vector3 p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * _radius.Value;
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
