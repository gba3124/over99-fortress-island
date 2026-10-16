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
        private TextMeshProUGUI _hud;
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

        // ---------- host API ----------

        public void ServerAddCorpse(Creature c, int value)
        {
            if (c && c.IsSpawned) _corpses[c] = (value, Time.time + CorpseLife);
        }

        public IEnumerable<Vector3> ServerCorpsePositions() => _corpses.Keys.Where(c => c && c.IsSpawned).Select(c => c.transform.position);
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
            InstanceFinder.ServerManager.Broadcast(new NetState { PlayerId = playerId, Count = n.count, Capacity = Cap, Value = n.value });
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
                if (net.count < Cap)
                    foreach (var kv in _corpses.Where(kv => kv.Key && (kv.Key.transform.position - pos).sqrMagnitude < ScoopRadius * ScoopRadius).ToList())
                    {
                        if (net.count >= Cap) break;
                        net.count++; net.value += kv.Value.value; changed = true;
                        Fx.Send(FxKind.Scoop, kv.Key.transform.position, pos);
                        kv.Key.DestroyItem();
                        _corpses.Remove(kv.Key);
                        AutoTest.Count("net_scooped");
                    }
                // Sell at the crate.
                if (net.count > 0 && (pos - crate).sqrMagnitude < CrateRadius * CrateRadius)
                {
                    Siege.Instance.ServerAddCoins(net.value);
                    Stats.Fished(p.ObjectId, net.value);
                    Fx.Send(FxKind.Sell, crate, crate, 0, net.value);
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
            if (kb == null || ChatManager.IsTyping || BuildSystem.Building) return;
            if (kb.uKey.wasPressedThisFrame && NearCrate() && Siege.Instance.ViewActive && InstanceFinder.IsClientStarted)
                InstanceFinder.ClientManager.Broadcast(new NetUpgradeRequest());
        }

        private void LateUpdate()
        {
            bool show = Siege.Instance && Siege.Instance.ViewActive;
            if (!show) { if (_hud) _hud.gameObject.SetActive(false); return; }
            if (!_hud) _hud = Hud.CreateText("NetHud", new Vector2(1f, 0f), new Vector2(-30f, 150f), 26f, TextAlignmentOptions.BottomRight);
            if (!_hud) return;
            _hud.rectTransform.pivot = new Vector2(1f, 0f);
            _hud.rectTransform.sizeDelta = new Vector2(700f, 120f);
            _hud.gameObject.SetActive(true);
            int cap = _mine.Capacity > 0 ? _mine.Capacity : Capacity[0];
            string full = _mine.Count >= cap ? " <color=#ff7060>" + L10n.T("netFull") + "</color>" : "";
            string line = L10n.T("netHud", _mine.Count, cap, _mine.Value) + full;
            if (NearCrate())
            {
                int lvl = System.Array.IndexOf(Capacity, cap);
                line += "\n" + (lvl >= 0 && lvl < UpgradeCost.Length ? L10n.T("netUpgradeHint", Capacity[lvl + 1], UpgradeCost[lvl]) : L10n.T("netMax"));
            }
            _hud.text = line;
        }
    }
}
