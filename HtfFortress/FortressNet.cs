using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Serializing;
using FishNet.Transporting;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HtfFortress
{
    // Host -> everyone: one player's net. Client -> host: buy the next net size.
    public struct NetState : IBroadcast { public int PlayerId; public int Count; public int Capacity; public int Value; }
    public struct NetUpgradeRequest : IBroadcast { }

    // The fishing net: walk over dead fish to scoop them up (up to the net's size), then sell the whole net
    // at the fish crate by the dock for team coins. The crate also sells bigger nets (a team upgrade).
    // Corpses nobody collects vanish after a minute so the island never fills up.
    public class FortressNet : MonoBehaviour
    {
        public static FortressNet Instance;
        public static readonly int[] Capacity = { 5, 10, 20 };
        public static readonly int[] UpgradeCost = { 60, 150 };
        private const float ScoopRadius = 2.5f, CrateRadius = 3f, CorpseLife = 60f;

        // host
        private readonly Dictionary<Creature, (int value, float until)> _corpses = new Dictionary<Creature, (int, float)>();
        private readonly Dictionary<int, (int count, int value)> _nets = new Dictionary<int, (int, int)>();
        private int _netLevel;
        private float _nextTick;
        // client
        private NetState _mine;
        private NetworkManager _registeredOn;

        static FortressNet()
        {
            GenericWriter<NetState>.SetWrite((w, v) => { w.WriteInt32(v.PlayerId); w.WriteInt32(v.Count); w.WriteInt32(v.Capacity); w.WriteInt32(v.Value); });
            GenericReader<NetState>.SetRead(r => new NetState { PlayerId = r.ReadInt32(), Count = r.ReadInt32(), Capacity = r.ReadInt32(), Value = r.ReadInt32() });
            GenericWriter<NetUpgradeRequest>.SetWrite((w, v) => w.WriteBoolean(true));
            GenericReader<NetUpgradeRequest>.SetRead(r => { r.ReadBoolean(); return new NetUpgradeRequest(); });
        }

        private void Awake() => Instance = this;

        private int Cap => Capacity[Mathf.Clamp(_netLevel, 0, Capacity.Length - 1)];
        private int CapOf(int playerId) => Cap + Mathf.RoundToInt(Perks.Of(playerId, Gear.BigNet)); // + the Big Net gear

        // ---------- host API ----------

        public void ServerAddCorpse(Creature c, int value)
        {
            if (c && c.IsSpawned) _corpses[c] = (value, Time.time + CorpseLife);
        }

        public IEnumerable<Vector3> ServerCorpsePositions() => _corpses.Keys.Where(c => c && c.IsSpawned).Select(c => c.transform.position);
        public int ServerCapOf(int playerId) => CapOf(playerId);
        public int ServerNetCount(int playerId) => _nets.TryGetValue(playerId, out var n) ? n.count : 0;

        public void ServerReset()
        {
            foreach (var c in _corpses.Keys.Where(c => c && c.IsSpawned).ToList()) c.DestroyItem();
            _corpses.Clear();
            _nets.Clear();
            _netLevel = 0;
            foreach (var p in PlayerManager.Players) if (p) Send(p.ObjectId);
        }

        private void Send(int playerId)
        {
            _nets.TryGetValue(playerId, out var n);
            InstanceFinder.ServerManager.Broadcast(new NetState { PlayerId = playerId, Count = n.count, Capacity = CapOf(playerId), Value = n.value });
        }

        private void Update()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm && nm != _registeredOn)
            {
                _registeredOn = nm;
                nm.ClientManager.RegisterBroadcast<NetState>((s, ch) => { if (Player.LocalPlayer && s.PlayerId == Player.LocalPlayer.ObjectId) _mine = s; });
                nm.ServerManager.RegisterBroadcast<NetUpgradeRequest>((conn, r, ch) => ServerUpgrade());
                nm.ClientManager.OnClientConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) _mine = default; };
            }
            if (InstanceFinder.IsServerStarted && Time.time >= _nextTick) { _nextTick = Time.time + 0.25f; ServerTick(); }
            ClientInput();
        }

        private void ServerTick()
        {
            var island = FortressIsland.Instance;
            if (!island || !island.Exists) return;
            // Expire old corpses.
            foreach (var kv in _corpses.Where(kv => !kv.Key || !kv.Key.IsSpawned || Time.time > kv.Value.until).ToList())
            {
                if (kv.Key && kv.Key.IsSpawned) kv.Key.DestroyItem();
                _corpses.Remove(kv.Key);
            }
            Vector3 crate = island.CratePos;
            foreach (var p in PlayerManager.AlivePlayers)
            {
                if (!p || !p.Transform) continue;
                Vector3 pos = p.Transform.position;
                _nets.TryGetValue(p.ObjectId, out var net);
                bool changed = false;
                // Scoop.
                int cap = CapOf(p.ObjectId);
                if (net.count < cap)
                    foreach (var kv in _corpses.Where(kv => kv.Key && (kv.Key.transform.position - pos).sqrMagnitude < ScoopRadius * ScoopRadius).ToList())
                    {
                        if (net.count >= cap) break;
                        net.count++; net.value += kv.Value.value; changed = true;
                        Fx.Send(FxKind.Scoop, kv.Key.transform.position, pos);
                        kv.Key.DestroyItem();
                        _corpses.Remove(kv.Key);
                        AutoTest.Count("net_scooped");
                    }
                // Sell at the crate.
                if (net.count > 0 && (pos - crate).sqrMagnitude < CrateRadius * CrateRadius)
                {
                    int value = Mathf.RoundToInt(net.value * (1f + Perks.Of(p, Gear.FishGloves))); // + Fishmonger's Gloves
                    Siege.Instance.ServerAddCoins(value);
                    Stats.Fished(p.ObjectId, value);
                    Fx.Send(FxKind.Sell, crate, crate, 0, value);
                    AutoTest.Count("net_sold", net.count);
                    net = default; changed = true;
                }
                _nets[p.ObjectId] = net;
                if (changed) Send(p.ObjectId);
            }
        }

        private void ServerUpgrade()
        {
            if (_netLevel >= UpgradeCost.Length || !Siege.Instance.ServerTrySpend(UpgradeCost[_netLevel])) return;
            _netLevel++;
            foreach (var p in PlayerManager.Players) if (p) Send(p.ObjectId);
            Fx.Announce("netUpgraded", Cap);
        }

        // ---------- client ----------

        private bool NearCrate()
        {
            var island = FortressIsland.Instance;
            return island && island.Exists && Player.LocalPlayer && Player.LocalPlayer.Transform
                   && (Player.LocalPlayer.Transform.position - island.CratePos).sqrMagnitude < (CrateRadius + 1.5f) * (CrateRadius + 1.5f);
        }

        private void ClientInput()
        {
            var kb = Keyboard.current;
            if (kb == null || ChatManager.IsTyping || BuildSystem.Building || Hud.ModalOpen) return;
            if (kb.uKey.wasPressedThisFrame && NearCrate() && Siege.Instance.ViewActive && InstanceFinder.IsClientStarted)
                InstanceFinder.ClientManager.Broadcast(new NetUpgradeRequest());
        }

        private UnityEngine.UI.Image _panel;
        private TextMeshProUGUI _netTitle, _netCount, _netFull;
        private Hud.Bar _netBar;

        // Bottom right: your net (count / size, value); a hint when it's full; the upgrade offer at the crate.
        private void LateUpdate()
        {
            bool show = Siege.Instance && Siege.Instance.ViewActive && Hud.Font;
            if (!show) { if (_panel) _panel.gameObject.SetActive(false); return; }
            if (!_panel)
            {
                _panel = Hud.Box(Hud.Root, new Vector2(1f, 0f), new Vector2(-24f, 150f), new Vector2(320f, 74f), Hud.Panel, 16f);
                _netTitle = Hud.Label(_panel.transform, "", 21f, new Vector2(0f, 1f), new Vector2(16f, -8f), new Vector2(200f, 30f), TextAlignmentOptions.TopLeft);
                _netCount = Hud.Label(_panel.transform, "", 24f, new Vector2(1f, 1f), new Vector2(-16f, -6f), new Vector2(120f, 30f), TextAlignmentOptions.TopRight, bold: true);
                _netBar = Hud.Bar.Make(_panel.transform, new Vector2(0f, 0f), new Vector2(16f, 14f), new Vector2(288f, 14f), Hud.Accent);
                _netFull = Hud.Label(_panel.transform, "", 20f, new Vector2(1f, 1f), new Vector2(0f, 30f), new Vector2(400f, 28f), TextAlignmentOptions.BottomRight, Hud.Bad, bold: true, outline: true);
            }
            _panel.gameObject.SetActive(true);
            int cap = _mine.Capacity > 0 ? _mine.Capacity : Capacity[0];
            bool full = _mine.Count >= cap;
            _netTitle.text = $"{L10n.T("netTitle")}  <size=85%><color={Hud.Hex(Hud.Gold)}>{L10n.T("netWorth", _mine.Value)}</color></size>";
            _netCount.text = $"{_mine.Count}<size=70%><color={Hud.Hex(Hud.Muted)}> / {cap}</color></size>";
            _netCount.color = full ? Hud.Bad : Hud.Ink;
            _netBar.Set((float)_mine.Count / cap, full ? Hud.Bad : Hud.Accent);
            _netFull.gameObject.SetActive(full);
            _netFull.text = L10n.T("netFullShort");
            if (NearCrate())
            {
                int bonus = Mathf.RoundToInt(Loot.Instance.ClientVault.PersonalValue(Loot.KeyOf(Player.LocalPlayer), Gear.BigNet));
                int lvl = System.Array.IndexOf(Capacity, cap - bonus);
                Hud.Prompt(lvl >= 0 && lvl < UpgradeCost.Length ? L10n.T("netUpgradeHint", Capacity[lvl + 1], UpgradeCost[lvl]) : L10n.T("netMax"));
            }
        }
    }
}
