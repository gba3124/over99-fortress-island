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
    public struct NetState : IBroadcast { public int PlayerId; public int Count; public int Capacity; public int Value; public int Backup; }
    // Host -> everyone: how many backup fish one player has hanging on their belt (0 = none).
    public struct FoodPack : IBroadcast { public int PlayerId; public int Count; }
    // Client -> host at the fish stall: Sell = sell my net, else buy the team a bigger net.
    // Level (buying a bigger net): the net size the player saw, so two at once buy one size, not two.
    public struct NetRequest : IBroadcast { public bool Sell; public int Level; public bool Food; } // Food: buy a grilled fish at the stall
    // Client -> host: a swing of the hand net, from the camera.
    public struct NetSwing : IBroadcast { public Vector3 Origin; public Vector3 Dir; }
    // Client -> host only: this player's own auto-eat rules (never rebroadcast: each player chooses when they eat).
    public struct EatRule : IBroadcast { public bool Hungry; public bool LowHealth; }

    // The fishing net: walk over dead fish to scoop them up (up to the net's size), then sell the whole net
    // at the fish stall by the dock for team coins. The stall also sells bigger nets (a team upgrade).
    // Corpses nobody collects vanish after two and a half minutes so the island never fills up.
    public class FortressNet : MonoBehaviour
    {
        public static FortressNet Instance;
        public static readonly int[] Capacity = { 5, 10, 20 };
        public static readonly int[] UpgradeCost = { 60, 150 };
        private const float CounterRadius = 3f, UseRadius = CounterRadius + 1.5f, CorpseLife = 150f; // (a minute was too short in the playtest)

        // host
        private readonly Dictionary<Creature, (int value, float until)> _corpses = new Dictionary<Creature, (int, float)>();
        private readonly Queue<Creature> _corpseOrder = new Queue<Creature>();
        // A late wave is about 200 fish (wave 100); 160 leaves a whole wave's bodies plus a backlog for the net and helpers.
        private static int CorpseCap => FortressPlugin.Instance != null ? FortressPlugin.Instance.CorpseCapValue : 160;
        private readonly Dictionary<int, (int count, int value)> _nets = new Dictionary<int, (int, int)>();
        private readonly Dictionary<int, int> _backup = new Dictionary<int, int>(); // player id -> grilled fish on the belt
        private readonly Dictionary<int, (GameObject root, int shown)> _waistFish = new Dictionary<int, (GameObject, int)>(); // client models
        private readonly Dictionary<int, int> _pack = new Dictionary<int, int>(); // client copy of the belt counts
        private readonly Dictionary<int, (bool hungry, bool hurt)> _eatRules = new Dictionary<int, (bool, bool)>(); // host copy, by player id
        private int _lastRuleBits = -1;
        public const int BackupPack = 5, BackupCost = 100, BackupFullness = 40, BackupHeal = 10;
        private int _netLevel;
        private float _nextTick;
        // client
        private NetState _mine;
        public bool ClientNetFull => _mine.Capacity > 0 && _mine.Count >= _mine.Capacity;
        public int ClientBackup => _mine.Backup;
        public void PreviewBackup(int add) { _mine.Backup = Mathf.Max(0, _mine.Backup + add); }
        // The team net's size step as this player sees it (their own Big Net gear adds on top of it).
        public int ClientNetLevel
        {
            get
            {
                var v = Loot.Instance ? Loot.Instance.ClientVault : null;
                return System.Array.IndexOf(Capacity, _mine.Capacity);
            }
        }
        private NetworkManager _registeredOn;

        static FortressNet()
        {
            GenericWriter<NetState>.SetWrite((w, v) => { w.WriteInt32(v.PlayerId); w.WriteInt32(v.Count); w.WriteInt32(v.Capacity); w.WriteInt32(v.Value); w.WriteInt32(v.Backup); });
            GenericReader<NetState>.SetRead(r => new NetState { PlayerId = r.ReadInt32(), Count = r.ReadInt32(), Capacity = r.ReadInt32(), Value = r.ReadInt32(), Backup = r.ReadInt32() });
            GenericWriter<FoodPack>.SetWrite((w, v) => { w.WriteInt32(v.PlayerId); w.WriteInt32(v.Count); });
            GenericReader<FoodPack>.SetRead(r => new FoodPack { PlayerId = r.ReadInt32(), Count = r.ReadInt32() });
            GenericWriter<NetRequest>.SetWrite((w, v) => { w.WriteBoolean(v.Sell); w.WriteInt32(v.Level); w.WriteBoolean(v.Food); });
            GenericReader<NetRequest>.SetRead(r => new NetRequest { Sell = r.ReadBoolean(), Level = r.ReadInt32(), Food = r.ReadBoolean() });
            GenericWriter<NetSwing>.SetWrite((w, v) => { w.WriteVector3(v.Origin); w.WriteVector3(v.Dir); });
            GenericReader<NetSwing>.SetRead(r => new NetSwing { Origin = r.ReadVector3(), Dir = r.ReadVector3() });
            GenericWriter<EatRule>.SetWrite((w, v) => { w.WriteBoolean(v.Hungry); w.WriteBoolean(v.LowHealth); });
            GenericReader<EatRule>.SetRead(r => new EatRule { Hungry = r.ReadBoolean(), LowHealth = r.ReadBoolean() });
        }

        private void Awake() => Instance = this;

        private int Cap => Capacity[Mathf.Clamp(_netLevel, 0, Capacity.Length - 1)];
        private int CapOf(int playerId) => Cap;

        // ---------- host API ----------

        public void ServerAddCorpse(Creature c, int value)
        {
            if (!c || !c.IsSpawned) return;
            _corpses[c] = (value, Time.time + CorpseLife);
            _corpseOrder.Enqueue(c);
            while (_corpses.Count > CorpseCap && _corpseOrder.Count > 0)
            {
                var old = _corpseOrder.Dequeue();
                if (!old || !_corpses.ContainsKey(old)) continue;
                _corpses.Remove(old);
                if (old.IsSpawned) old.DestroyItem();
                AutoTest.Count("corpses_swept");
            }
        }

        // Thieves: the nearest corpse, and taking it away (returns what it was worth).
        public Creature ServerNearestCorpse(Vector3 pos, float within) =>
            _corpses.Keys.Where(c => c && c.IsSpawned && (c.transform.position - pos).sqrMagnitude < within * within)
                    .OrderBy(c => (c.transform.position - pos).sqrMagnitude).FirstOrDefault();

        public int ServerTakeCorpse(Creature c)
        {
            if (!c || !_corpses.TryGetValue(c, out var v)) return 0;
            _corpses.Remove(c);
            if (c.IsSpawned) c.DestroyItem();
            return v.value;
        }

        public IEnumerable<Creature> ServerCorpses() => _corpses.Keys.Where(c => c && c.IsSpawned);
        public IEnumerable<Vector3> ServerCorpsePositions() => _corpses.Keys.Where(c => c && c.IsSpawned).Select(c => c.transform.position);
        public int ServerCapOf(int playerId) => CapOf(playerId);

        // The fish cannon: every corpse within `radius` flies into this player's net (as far as it holds).
        public int ServerScoopAround(Player p, float radius)
        {
            if (!p || !p.Transform) return 0;
            Vector3 pos = p.Transform.position;
            _nets.TryGetValue(p.ObjectId, out var net);
            int cap = CapOf(p.ObjectId), got = 0;
            foreach (var kv in _corpses.Where(kv => kv.Key && (kv.Key.transform.position - pos).sqrMagnitude < radius * radius).OrderBy(kv => (kv.Key.transform.position - pos).sqrMagnitude).ToList())
            {
                if (net.count >= cap) break;
                net.count++; net.value += kv.Value.value; got++;
                if (Perks.Legend(p, Gear.FishGloves)) { Siege.Instance.ServerAddCoins(Siege.Money(1)); Stats.Fished(p.ObjectId, Siege.Money(1)); } // like a scoop by hand
                Fx.Send(FxKind.Scoop, kv.Key.transform.position, pos);
                kv.Key.DestroyItem();
                _corpses.Remove(kv.Key);
            }
            _nets[p.ObjectId] = net;
            Send(p.ObjectId);
            return got;
        }
        public int ServerNetCount(int playerId) => _nets.TryGetValue(playerId, out var n) ? n.count : 0;
        public (int count, int capacity, int value) ClientNet => (_mine.Count, _mine.Capacity > 0 ? _mine.Capacity : Capacity[0], _mine.Value);

        // keepSize: at a round's start, a bigger net bought before it lasts through it.
        public void ServerReset(bool keepSize = false)
        {
            foreach (var c in _corpses.Keys.Where(c => c && c.IsSpawned).ToList()) c.DestroyItem();
            _corpses.Clear();
            _nets.Clear();
            if (!keepSize) _netLevel = 0;
            foreach (var p in PlayerManager.Players) if (p) Send(p.ObjectId);
        }

        private void Send(int playerId)
        {
            _nets.TryGetValue(playerId, out var n);
            _backup.TryGetValue(playerId, out int back);
            InstanceFinder.ServerManager.Broadcast(new NetState { PlayerId = playerId, Count = n.count, Capacity = CapOf(playerId), Value = n.value, Backup = back });
        }

        private void Update()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm && nm != _registeredOn)
            {
                _registeredOn = nm;
                nm.ClientManager.RegisterBroadcast<NetState>((s, ch) => { if (Player.LocalPlayer && s.PlayerId == Player.LocalPlayer.ObjectId) _mine = s; });
                nm.ClientManager.RegisterBroadcast<FoodPack>((s, ch) => { _pack[s.PlayerId] = s.Count; RefreshWaist(); });
                nm.ServerManager.RegisterBroadcast(Net.Safe<NetRequest>((conn, r, ch) =>
                {
                    var who = PlayerManager.Players.FirstOrDefault(p => p && p.Owner == conn);
                    if (r.Food) { if (who) { if (r.Level > 0) ServerBuyBackup(who); else ServerBuyFood(who); } return; }
                    if (!r.Sell) { if (r.Level == _netLevel) ServerUpgrade(); return; }
                    if (who) ServerSell(who);
                }));
                nm.ServerManager.RegisterBroadcast(Net.Safe<NetSwing>((conn, s, ch) =>
                {
                    var who = PlayerManager.Players.FirstOrDefault(p => p && p.Owner == conn);
                    if (who) ServerSwing(who, s.Origin, s.Dir);
                }));
                nm.ServerManager.RegisterBroadcast(Net.Safe<EatRule>((conn, r, ch) =>
                {
                    var who = PlayerManager.Players.FirstOrDefault(p => p && p.Owner == conn);
                    if (who) _eatRules[who.ObjectId] = (r.Hungry, r.LowHealth);
                }));
                nm.ClientManager.OnClientConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) _mine = default; };
                nm.SceneManager.OnClientLoadedStartScenes += (conn, asServer) =>
                {
                    if (!asServer) return;
                    // A late joiner needs the belts that are already on other players, not just its own NetState.
                    foreach (var kv in _backup) InstanceFinder.ServerManager.Broadcast(conn, new FoodPack { PlayerId = kv.Key, Count = kv.Value });
                };
            }
            // Tell the host when this player's own rule changes (it is not a team-wide setting).
            if (InstanceFinder.IsClientStarted && Player.LocalPlayer && Player.LocalPlayer.Vitals)
            {
                int bits = (ClientSettings.AutoEatHungry != null && ClientSettings.AutoEatHungry.Value ? 1 : 0)
                         | (ClientSettings.AutoEatLowHealth != null && ClientSettings.AutoEatLowHealth.Value ? 2 : 0);
                if (bits != _lastRuleBits)
                {
                    _lastRuleBits = bits;
                    if (InstanceFinder.IsServerStarted && Player.LocalPlayer) _eatRules[Player.LocalPlayer.ObjectId] = ((bits & 1) != 0, (bits & 2) != 0);
                    if (InstanceFinder.IsClientStarted) InstanceFinder.ClientManager.Broadcast(new EatRule { Hungry = (bits & 1) != 0, LowHealth = (bits & 2) != 0 });
                }
            }
            if (InstanceFinder.IsServerStarted && Time.time >= _nextTick) { _nextTick = Time.time + 0.25f; ServerTick(); }
            RefreshWaist();
            // The hand net is gone (Owen): the helpers pick the fish up. Only the corpse pool and the stall's helpers remain.
        }

        private void ServerTick()
        {
            AutoEatBackup();
            var island = FortressIsland.Instance;
            if (!island || !island.Exists) return;
            // Expire old corpses.
            foreach (var kv in _corpses.Where(kv => !kv.Key || !kv.Key.IsSpawned || Time.time > kv.Value.until).ToList())
            {
                if (kv.Key && kv.Key.IsSpawned) kv.Key.DestroyItem();
                _corpses.Remove(kv.Key);
            }
            Vector3 counter = island.CounterPos;
            foreach (var p in PlayerManager.AlivePlayers)
            {
                if (!p || !p.Transform) continue;
                Vector3 pos = p.Transform.position;
                bool changed = !_nets.TryGetValue(p.ObjectId, out var net); // a new face: tell them their net size (with their Big Net)
                int cap = CapOf(p.ObjectId);
                // Legendary big net: a full net sells itself (at 80%) wherever you are.
                // (the old legendary Big Net auto-sell is retired with the hand net: helpers carry the fish now)
                _nets[p.ObjectId] = net;
                if (changed) Send(p.ObjectId);
            }
        }

        // The hand net (host): a swing scoops the bodies in front of you (as many as the net still holds), or, aimed at the
        // stall's counter, empties the net onto it (sold).
        public const float SwingReach = 3.2f, SwingHalfAngle = 55f;

        public void ServerSwing(Player p, Vector3 origin, Vector3 dir)
        {
            var island = FortressIsland.Instance;
            if (!p || !p.Transform || !island || !island.Exists || !PlayerManager.AlivePlayers.Contains(p) || dir.sqrMagnitude < 0.01f) return;
            Vector3 pos = p.Transform.position;
            if (!island.OnIsland(pos, FortressIsland.IslandRadius + 6f)) return;
            if ((origin - pos).sqrMagnitude > 3f * 3f) origin = pos + Vector3.up * 1.6f; // swings start at the swinger
            Vector3 flat = new Vector3(dir.x, 0f, dir.z).normalized;
            _nets.TryGetValue(p.ObjectId, out var net);
            if (island && net.count > 0 && (island.CounterPos - pos).sqrMagnitude < UseRadius * UseRadius
                && Vector3.Angle(flat, new Vector3(island.CounterPos.x - pos.x, 0f, island.CounterPos.z - pos.z)) < 60f) { ServerSell(p); return; }
            int cap = CapOf(p.ObjectId);
            var reach = _corpses.Keys.Where(c => c && c.IsSpawned)
                .Select(c => (c, d: c.transform.position - pos))
                .Where(x => x.d.sqrMagnitude < SwingReach * SwingReach && Mathf.Abs(x.d.y) < 2.5f && (new Vector2(x.d.x, x.d.z).sqrMagnitude < 0.8f || Vector3.Angle(flat, new Vector3(x.d.x, 0f, x.d.z)) < SwingHalfAngle))
                .OrderBy(x => x.d.sqrMagnitude).Select(x => x.c).ToList();
            foreach (var c in reach)
            {
                if (net.count >= cap) break;
                int v = _corpses[c].value;
                _corpses.Remove(c);
                net.count++; net.value += v;
                if (Perks.Legend(p, Gear.FishGloves)) { Siege.Instance.ServerAddCoins(Siege.Money(1)); Stats.Fished(p.ObjectId, Siege.Money(1)); Perks.Proc(Gear.FishGloves, pos); } // legendary: paid on the spot
                Fx.Send(FxKind.Scoop, c.transform.position, pos, c.ObjectId, 0, p.ObjectId);
                StartCoroutine(Remove(c)); // a moment later, so every screen can copy the fish into the net first
                AutoTest.Count("net_scooped");
            }
            // Any other fish in reach, dead or flopping (your own catch, the game's fish, crabs): worth the game's price.
            // Not the fortress's fish (those fight back) and not bosses.
            var seen = new HashSet<Item>();
            foreach (var col in Physics.OverlapSphere(pos + flat * (SwingReach * 0.5f) + Vector3.up * 0.6f, SwingReach, ~0, QueryTriggerInteraction.Ignore))
            {
                if (net.count >= cap) break;
                var item = ItemManager.Get(col);
                if (!item || !seen.Add(item) || item.Holder || item.IsDestroying || item.IsDeinitializing || item.DeadPlayer) continue;
                var c = item.Creature;
                if (!c || c.BossType != BossType.None || _corpses.ContainsKey(c) || FortressPlugin.Alive.Contains(c) || item.TotalWorth <= 0) continue;
                Vector3 d = item.transform.position - pos;
                if (d.sqrMagnitude > SwingReach * SwingReach || Mathf.Abs(d.y) > 2.5f || (new Vector2(d.x, d.z).sqrMagnitude > 0.8f && Vector3.Angle(flat, new Vector3(d.x, 0f, d.z)) > SwingHalfAngle)) continue;
                net.count++; net.value += item.TotalWorth;
                Fx.Send(FxKind.Scoop, item.transform.position, pos, item.ObjectId, 0, p.ObjectId);
                StartCoroutine(Remove(item));
                AutoTest.Count("net_scooped_fish");
            }
            _nets[p.ObjectId] = net;
            Send(p.ObjectId);
        }

        private static System.Collections.IEnumerator Remove(Item c)
        {
            yield return new WaitForSeconds(0.15f);
            if (c && c.IsSpawned) c.DestroyItem();
        }

        // Anything thrown onto the stall's counter is sold on the spot, like at the shopkeeper: a fortress body for what it's worth to the
        // fortress (during a round), anything else (your catch from fishing, junk) for the game's own price.
        public void ServerSellThrown(Item item, Vector3 into)
        {
            if (Swallow.IsGoing(item) || _food.Contains(item)) return; // a grilled fish just bought is to eat, not to sell back
            if (item is Creature c && _corpses.TryGetValue(c, out var v))
            {
                if (!Siege.Instance.ServerActive) return;
                _corpses.Remove(c);
                Siege.Instance.ServerAddCoins(v.value);
                if (item.LastHolder) Stats.Fished(item.LastHolder.ObjectId, v.value);
                Fx.Send(FxKind.Sell, item.transform.position, item.transform.position, 0, v.value);
                Swallow.Server(c, into, Gulp.Crate);
                AutoTest.Count("crate_sold_body");
                return;
            }
            if (item.TotalWorth <= 0) return;
            MoneyManager.SellItem(item);
            Swallow.Server(item, into, Gulp.Crate);
            AutoTest.Count("crate_sold_item");
        }

        // At the stall, on the player's key: the net's worth goes to the crew's money.
        public void ServerSell(Player p)
        {
            var island = FortressIsland.Instance;
            if (!p || !p.Transform || !island || !island.Exists || !_nets.TryGetValue(p.ObjectId, out var net) || net.count == 0) return;
            if ((p.Transform.position - island.CounterPos).sqrMagnitude > UseRadius * UseRadius) return;
            int value = Mathf.RoundToInt(net.value * (1f + Perks.Of(p, Gear.FishGloves))); // + Fishmonger's Gloves
            Siege.Instance.ServerPay(value);
            Stats.Fished(p.ObjectId, value);
            Fx.Send(FxKind.Sell, island.CounterPos, island.CounterPos, 0, value);
            AutoTest.Count("net_sold", net.count);
            _nets[p.ObjectId] = default;
            Send(p.ObjectId);
        }

        // The fishmonger's grilled fish, paid from the crew's money: it goes straight into the buyer's hand and they eat it
        // there and then, the game's own way (to the mouth, chewing; it fills you up and heals a little). Owen: "烤魚點了直接吃".
        public const int FoodCost = 25;
        public const float FoodGrace = 6f; // the hand-over and the eating, even on a slow connection
        private readonly HashSet<Item> _food = new HashSet<Item>();
        private void ServerBuyFood(Player who)
        {
            var island = FortressIsland.Instance;
            var prefab = GameInfo.GetSpawnable("bass");
            if (!island || !island.Exists || !prefab || !who || !who.Transform || !PlayerManager.AlivePlayers.Contains(who) // nobody pays for a fish they can't eat
                || !Siege.Instance.ServerTrySpend(Siege.Money(FoodCost), always: true)) return;
            var fish = Instantiate(prefab, who.Transform.position + Vector3.up * 1.2f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), Server.Instance.DynamicObjectsHolder);
            if (fish.Creature) fish.Creature.ServerKillOnSpawn();
            Server.Instance.Spawn(fish.gameObject);
            fish._cookness.Value = 1f; // grilled
            _food.Add(fish);
            Loot.HandToEat(who, fish);
            StartCoroutine(EatenAnyway(who, fish));
            AutoTest.Count("stall_food");
        }

        // Paid is eaten (the playtest: 「有時候有花錢但沒有吃到魚」). Bought at the counter, the fish could fall onto it before it
        // reached the buyer's hand and be sold straight back (ServerSellThrown skips it now); a click or a number key stops the
        // game's eating halfway, the fish left in hand or dropped; a hand-over can fail. Whatever is left of it once they've
        // stopped eating is eaten for them, the game's way (its fill-up and heal, and gone as if eaten). One they put away in
        // the hotbar (a number key with a free slot) stays theirs, to eat later.
        private System.Collections.IEnumerator EatenAnyway(Player who, Item fish)
        {
            for (float t = 0.5f; ; t += 0.5f)
            {
                yield return new WaitForSeconds(0.5f);
                if (!fish || !fish.IsSpawned || fish.IsDestroying || fish.IsDeinitializing || fish.IsInInventory) break; // eaten, or put away
                bool eating = who && who.Eating && who.Eating._isEating.Value;
                if (!LeftOver(t, eating)) continue;
                if (fish is Creature c && who && who.Vitals && PlayerManager.AlivePlayers.Contains(who))
                {
                    float k = GameInfo.CooknessEatCurve.Evaluate(c.Cookness); // as Server.FinishEatingCreature
                    who.Vitals.RestoreFullness((int)(c.FullnessToRestore * k));
                    who.Vitals.Heal((int)(c.HpToRestore * k));
                    fish.DestroyItem(3); // DestroyReason.Eating
                    AutoTest.Count("stall_food_finished");
                }
                break; // (the buyer gone or down: it stays where it is, anyone's to eat)
            }
            _food.Remove(fish);
        }

        // Past the grace and not being eaten now (someone eating keeps their fish, however long it takes; never past 20 s).
        public static bool LeftOver(float waited, bool eating) => waited >= FoodGrace && (!eating || waited >= 20f);

        // Buying a stringer of grilled fish: they hang on the belt and are eaten by themselves when hunger drops below 10%.
        private void ServerBuyBackup(Player who)
        {
            var island = FortressIsland.Instance;
            if (!island || !island.Exists || !who || !who.Transform || !PlayerManager.AlivePlayers.Contains(who)
                || !Siege.Instance.ServerTrySpend(Siege.Money(BackupCost), always: true)) return;
            _backup.TryGetValue(who.ObjectId, out int n);
            _backup[who.ObjectId] = n + BackupPack;
            BroadcastPack(who.ObjectId);
            Send(who.ObjectId);
            AutoTest.Count("stall_backup_fish", BackupPack);
        }

        private void BroadcastPack(int playerId)
        {
            _backup.TryGetValue(playerId, out int n);
            if (Player.LocalPlayer && Player.LocalPlayer.ObjectId == playerId) { _pack[playerId] = n; RefreshWaist(); }
            if (InstanceFinder.ServerManager) InstanceFinder.ServerManager.Broadcast(new FoodPack { PlayerId = playerId, Count = n });
        }

        private void AutoEatBackup()
        {
            foreach (var p in PlayerManager.Players)
            {
                if (!p || !p.Transform || !p.Vitals || p.Dying && p.Dying.IsDead) continue;
                if (!_backup.TryGetValue(p.ObjectId, out int n) || n <= 0) continue;
                bool hungry = ClientSettings.AutoEatHungry == null || ClientSettings.AutoEatHungry.Value;
                bool hurt = ClientSettings.AutoEatLowHealth != null && ClientSettings.AutoEatLowHealth.Value;
                if (_eatRules.TryGetValue(p.ObjectId, out var rule)) { hungry = rule.hungry; hurt = rule.hurt; }
                if (!(hungry && p.Vitals.Fullness < 10) && !(hurt && p.Vitals.Health < 50)) continue;
                _backup[p.ObjectId] = --n;
                p.Vitals.RestoreFullness(BackupFullness);
                p.Vitals.Heal(BackupHeal);
                Fx.Send(FxKind.Heal, p.Transform.position, p.Transform.position + Vector3.up);
                BroadcastPack(p.ObjectId); Send(p.ObjectId);
                AutoTest.Count("backup_fish_eaten");
            }
        }

        private void RefreshWaist()
        {
            // Client: one small fish model per belt, attached to the player's hip.
            foreach (var id in new List<int>(_pack.Keys))
            {
                _pack.TryGetValue(id, out int n);
                var p = PlayerManager.Players.FirstOrDefault(x => x && x.ObjectId == id);
                // Visual tiers: a belt cannot show 100 fish, so it grows a handful and then a basket; the exact number is on
                // the stall's backup-fish line.
                int want = n >= 100 ? 6 : n >= 50 ? 5 : n >= 20 ? 5 : n >= 15 ? 4 : n >= 10 ? 3 : n >= 5 ? 2 : 1;
                bool basket = n >= 50;
                if (n <= 0 || !p || !p.Transform)
                {
                    if (_waistFish.TryGetValue(id, out var old) && old.root) Object.Destroy(old.root);
                    _waistFish.Remove(id); _pack.Remove(id);
                    continue;
                }
                // Hide your own belt BEFORE the unchanged-model early-out below: otherwise an old local model (a hot reload,
                // or Player.LocalPlayer being assigned after the first tick) keeps sitting in the first-person camera.
                bool own = p == Player.LocalPlayer;
                _waistFish.TryGetValue(id, out var have);
                if (own)
                {
                    if (have.root) Object.Destroy(have.root);
                    _waistFish.Remove(id);
                    continue; // teammates still see it on you; you never see it in your own camera
                }
                if (have.root && have.shown == want + (basket ? 100 : 0)) continue;
                if (have.root) Object.Destroy(have.root);
                var holder = new GameObject("BackupFish");
                holder.transform.SetParent(p.Transform, false);
                holder.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                holder.transform.localRotation = Quaternion.identity;
                // A small belt ring, not a wall of fish in front of the camera: left, right, back, all clearly separated.
                var offsets = new[] { new Vector3(0.28f, 0f, 0.02f), new Vector3(-0.28f, 0f, 0.02f),
                                      new Vector3(0.16f, -0.02f, -0.26f), new Vector3(-0.16f, -0.02f, -0.26f),
                                      new Vector3(0.30f, 0.02f, -0.16f), new Vector3(-0.30f, 0.02f, -0.16f) };
                var yaws = new[] { 90f, -90f, 180f, 180f, 120f, -120f };
                int first = basket ? 2 : 0;
                for (int i = 0; i < want; i++)
                {
                    var one = Models.Attach("GrilledFish", holder.transform, Vector3.one * 0.45f);
                    one.transform.localScale = Vector3.one * (basket ? 0.18f : 0.20f); // the grilled model is ~62 cm long: 11-12 cm on the belt
                    one.transform.localPosition = offsets[(first + i) % offsets.Length];
                    one.transform.localRotation = Quaternion.Euler(0f, yaws[(first + i) % yaws.Length], 0f);
                }
                if (basket)
                {
                    var crate = Models.Attach("HelperBasket", holder.transform, Vector3.one * 0.4f, share: true);
                    crate.transform.localScale = Vector3.one * 0.42f;
                    crate.transform.localPosition = new Vector3(0f, -0.08f, -0.18f);
                    crate.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                }
                _waistFish[id] = (holder, want + (basket ? 100 : 0));
            }
        }

        private void ServerUpgrade()
        {
            if (_netLevel >= UpgradeCost.Length || !Siege.Instance.ServerTrySpend(Siege.Money(UpgradeCost[_netLevel]), always: true)) return;
            _netLevel++;
            foreach (var p in PlayerManager.Players) if (p) Send(p.ObjectId);
            Fx.Announce("netUpgraded", Cap);
        }

        // ---------- client ----------

        private bool NearCounter()
        {
            var island = FortressIsland.Instance;
            return island && island.Exists && Player.LocalPlayer && Player.LocalPlayer.Transform
                   && (Player.LocalPlayer.Transform.position - island.CounterPos).sqrMagnitude < (UseRadius - 0.3f) * (UseRadius - 0.3f);
        }

        private void ClientInput()
        {
            var kb = Keyboard.current;
            if (kb == null || ChatManager.IsTyping || BuildSystem.Building || Hud.ModalOpen) return;
            if (kb.uKey.wasPressedThisFrame && NearCounter() && !BuildSystem.AimingAtPiece && Siege.Instance.ViewActive && InstanceFinder.IsClientStarted)
            {
                bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                if (shift || _mine.Count > 0) InstanceFinder.ClientManager.Broadcast(new NetRequest { Sell = !shift, Level = ClientNetLevel }); // U sells, Shift+U buys a bigger net
            }
        }

        private UnityEngine.UI.Image _panel;
        private TextMeshProUGUI _netTitle, _netCount, _netFull;
        private Hud.Bar _netBar;

        // Bottom right: your net (count / size, value); a hint when it's full; the upgrade offer at the stall.
        private void LateUpdate()
        {
            // The bottom-right net HUD is gone with the hand net.
            if (_panel) _panel.gameObject.SetActive(false);
        }
    }

    // Over the stall's counter (host): sells what's thrown onto it, the way the game's own sell box does.
    internal class SellCounter : MonoBehaviour
    {
        private void OnTriggerStay(Collider other)
        {
            if (!InstanceFinder.IsServerStarted || !FortressNet.Instance) return;
            var item = ItemManager.Get(other);
            if (!item || item.Holder || item.IsDeinitializing || item.IsDestroying || item.DeadPlayer || (item.Creature && !item.Creature.IsDead)) return;
            FortressNet.Instance.ServerSellThrown(item, transform.position + Vector3.up * 0.9f); // onto the counter top
        }
    }
}
