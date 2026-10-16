using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Serializing;
using FishNet.Transporting;
using UnityEngine;

namespace HtfFortress
{
    // Host -> everyone: the whole vault (VaultData text).
    public struct VaultState : IBroadcast { public string Data; }

    public enum VaultAction : byte { Merge, EquipFortress, EquipPersonal, UnequipFortress, UnequipPersonal }
    // Client -> host: one vault action.
    public struct VaultRequest : IBroadcast { public VaultAction Action; public Gear Gear; public Rarity Rarity; public byte Slot; }

    public enum LootWhat : byte { Drop, DropGone, Chest, ChestGone, Leftovers, Blueprint }
    // Host -> everyone: gear on the ground, chests, and who got what.
    public struct LootEvent : IBroadcast { public int Id; public LootWhat What; public Vector3 Pos; public Gear Gear; public Rarity Rarity; public string Who; public string Data; public string Item; }

    // Gear that outlives a round: enemies sometimes drop a piece, every 5th wave leaves a chest by the lighthouse,
    // and everything goes into the team vault saved with the host's save. The loadout is set between rounds.
    public class Loot : MonoBehaviour
    {
        public static Loot Instance;
        private const float PickRadius = 2f, ChestRadius = 1.8f, DropLife = 60f;

        // host
        public VaultData Vault { get; private set; } = new VaultData();
        private string _loadedFor;
        private readonly Dictionary<int, (Gear g, Rarity r, Vector3 pos, float until)> _drops = new Dictionary<int, (Gear, Rarity, Vector3, float)>();
        private (int id, Vector3 pos, int wave) _chest;
        private int _nextId = 1;
        private float _nextTick;
        // client
        public VaultData ClientVault { get; private set; } = new VaultData();
        private readonly Dictionary<int, GameObject> _dropViews = new Dictionary<int, GameObject>();
        private GameObject _chestView;
        public bool ClientHasDrops => _dropViews.Count > 0;
        public Vector3? ClientChestPos => _chestView ? _chestView.transform.position : (Vector3?)null;
        private NetworkManager _registeredOn;

        static Loot()
        {
            GenericWriter<VaultState>.SetWrite((w, v) => w.WriteString(v.Data ?? ""));
            GenericReader<VaultState>.SetRead(r => new VaultState { Data = r.ReadString() });
            GenericWriter<VaultRequest>.SetWrite((w, v) => { w.WriteByte((byte)v.Action); w.WriteByte((byte)v.Gear); w.WriteByte((byte)v.Rarity); w.WriteByte(v.Slot); });
            GenericReader<VaultRequest>.SetRead(r => new VaultRequest { Action = (VaultAction)r.ReadByte(), Gear = (Gear)r.ReadByte(), Rarity = (Rarity)r.ReadByte(), Slot = r.ReadByte() });
            GenericWriter<LootEvent>.SetWrite((w, v) =>
            {
                w.WriteInt32(v.Id); w.WriteByte((byte)v.What); w.WriteVector3(v.Pos); w.WriteByte((byte)v.Gear); w.WriteByte((byte)v.Rarity);
                w.WriteString(v.Who ?? ""); w.WriteString(v.Data ?? ""); w.WriteString(v.Item ?? "");
            });
            GenericReader<LootEvent>.SetRead(r => new LootEvent
            {
                Id = r.ReadInt32(), What = (LootWhat)r.ReadByte(), Pos = r.ReadVector3(), Gear = (Gear)r.ReadByte(), Rarity = (Rarity)r.ReadByte(),
                Who = r.ReadString(), Data = r.ReadString(), Item = r.ReadString(),
            });
        }

        private void Awake() => Instance = this;

        public static ulong KeyOf(Player p) => p.SteamID != 0 ? p.SteamID : (ulong)(uint)p.ObjectId;
        private static string NameOf(Player p) => string.IsNullOrEmpty(p.SteamName) ? "Player " + p.ObjectId : p.SteamName;

        private static string Pack(IEnumerable<(Gear, Rarity)> items) => string.Join(",", items.Select(i => (int)i.Item1 + ":" + (int)i.Item2));
        private static IEnumerable<(Gear, Rarity)> Unpack(string s) =>
            (s ?? "").Split(',').Select(x => x.Split(':')).Where(f => f.Length == 2).Select(f => ((Gear)int.Parse(f[0]), (Rarity)int.Parse(f[1])));
        private static string Labels(string packed) => string.Join("、", Unpack(packed).Select(i => GearCatalog.Label(i.Item1, i.Item2)));

        private void Update()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm && nm != _registeredOn)
            {
                _registeredOn = nm;
                nm.ClientManager.RegisterBroadcast<VaultState>((s, ch) => ClientVault = VaultData.Read(s.Data));
                nm.ClientManager.RegisterBroadcast<LootEvent>((e, ch) => OnLoot(e));
                nm.ServerManager.RegisterBroadcast<VaultRequest>((conn, req, ch) =>
                {
                    var who = PlayerManager.Players.FirstOrDefault(p => p && p.Owner == conn);
                    if (who) ServerRequest(who, req);
                });
                nm.SceneManager.OnClientLoadedStartScenes += (conn, asServer) =>
                {
                    if (!asServer) return;
                    SendVault(conn);
                    foreach (var kv in _drops) InstanceFinder.ServerManager.Broadcast(conn, new LootEvent { Id = kv.Key, What = LootWhat.Drop, Pos = kv.Value.pos, Gear = kv.Value.g, Rarity = kv.Value.r });
                    if (_chest.id != 0) InstanceFinder.ServerManager.Broadcast(conn, new LootEvent { Id = _chest.id, What = LootWhat.Chest, Pos = _chest.pos });
                };
                nm.ServerManager.OnServerConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) { _drops.Clear(); _chest = default; _loadedFor = null; } };
                nm.ClientManager.OnClientConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) ClearViews(); };
            }
            if (InstanceFinder.IsServerStarted && Time.time >= _nextTick) { _nextTick = Time.time + 0.2f; ServerTick(); }
            AnimateViews();
        }

        // ---------- host: the vault file ----------

        private static string FilePath(string save) =>
            Path.Combine(Paths.BepInExRootPath, "fortress-vault", string.Concat(save.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".txt");

        private void EnsureLoaded()
        {
            string save = SaveManager.CurServerSave?.Name;
            if (string.IsNullOrEmpty(save) || save == _loadedFor) return;
            _loadedFor = save;
            string path = FilePath(save);
            Vault = File.Exists(path) ? VaultData.Read(File.ReadAllText(path)) : new VaultData();
            BuildSystem.Log.LogInfo($"Vault for save '{save}': {Vault.Owned.Values.Sum()} items stored, {Vault.Fortress.Count} fortress slots in use.");
            SendVault(null);
        }

        private void SaveVault()
        {
            if (string.IsNullOrEmpty(_loadedFor)) return;
            string path = FilePath(_loadedFor);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Vault.Write());
            SendVault(null);
        }

        private void SendVault(NetworkConnection to)
        {
            if (!InstanceFinder.IsServerStarted) return;
            var s = new VaultState { Data = Vault.Write() };
            if (to != null) InstanceFinder.ServerManager.Broadcast(to, s); else InstanceFinder.ServerManager.Broadcast(s);
        }

        private static void Broadcast(LootEvent e) { if (InstanceFinder.IsServerStarted) InstanceFinder.ServerManager.Broadcast(e); }

        // ---------- host: drops and chests ----------

        // An enemy died: maybe it leaves a piece of gear where it fell (the killer's lucky charm raises the odds).
        public void ServerOnKill(Vector3 pos, EnemyKind kind, bool elite, int killerId, int wave)
        {
            float chance = EnemyCatalog.IsBoss(kind) ? 1f : elite ? 0.25f : 0.03f;
            var killer = PlayerManager.Players.FirstOrDefault(p => p && p.ObjectId == killerId);
            if (killer) chance *= Mathf.Max(1f, Vault.PersonalValue(KeyOf(killer), Gear.LuckyCharm));
            if (EnemyCatalog.IsBoss(kind)) ServerUnlockRandom(killer); // every boss leaves a blueprint while any are left
            if (Random.value >= chance) return;
            Rarity r = GearCatalog.Roll(wave, EnemyCatalog.IsBoss(kind) ? Rarity.Epic : Rarity.Common);
            if (killer && Perks.Legend(killer, Gear.LuckyCharm) && r < Rarity.Legendary) r++; // legendary: one rarity better
            Drop(GearCatalog.RandomGear(), r, pos);
        }

        private void Drop(Gear g, Rarity r, Vector3 pos)
        {
            // Big or flung bodies die above the ground: put the drop on the ground (or on the water) below.
            if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out var hit, 40f, GameInfo.LevelLayer)) pos = hit.point + Vector3.up * 0.5f;
            pos.y = Mathf.Max(pos.y, WaterManager.GetWaterHeight(pos) + 0.3f);
            int id = _nextId++;
            _drops[id] = (g, r, pos, Time.time + DropLife);
            Broadcast(new LootEvent { Id = id, What = LootWhat.Drop, Pos = pos, Gear = g, Rarity = r });
            AutoTest.Count("loot_dropped");
        }

        // A blueprint for a turret the team doesn't have yet (none left: nothing happens).
        public bool ServerUnlockRandom(Player who)
        {
            EnsureLoaded();
            var locked = System.Enum.GetValues(typeof(PieceType)).Cast<PieceType>().Where(t => !Vault.CanBuild(t)).ToList();
            if (locked.Count == 0) return false;
            var t = locked[Random.Range(0, locked.Count)];
            Vault.Unlocked.Add(t);
            SaveVault();
            Broadcast(new LootEvent { What = LootWhat.Blueprint, Who = who ? NameOf(who) : "", Data = t.ToString() });
            Fx.Announce("blueprintBanner");
            AutoTest.Count("blueprints");
            return true;
        }

        // After every 5th wave: a chest by the lighthouse; the first player to step on it opens it for the team.
        public void ServerSpawnChest(int wave)
        {
            var island = FortressIsland.Instance;
            if (island == null || !island.Exists) return;
            if (_chest.id != 0) OpenChest(null);
            Vector3 p = island.Center + new Vector3(3.5f, 4f, -3f);
            if (Physics.Raycast(p, Vector3.down, out var hit, 10f, GameInfo.LevelLayer)) p = hit.point;
            _chest = (_nextId++, p, wave);
            Broadcast(new LootEvent { Id = _chest.id, What = LootWhat.Chest, Pos = p });
            Fx.Announce("chestBanner");
            AutoTest.Count("chests");
        }

        // The game's own items a chest can throw out, by rarity (they land beside the chest and are yours to keep).
        private static readonly string[][] GameItems =
        {
            new[] { "knife", "brassknuckles", "bow", "dynamite", "disc", "moneyinabottle" },
            new[] { "pistol", "shotgun" },
            new[] { "smg", "sniperrifle" },
            new[] { "assaultrifle" },
        };
        public const float GameItemChance = 0.3f;

        private void OpenChest(Player who)
        {
            int n = 2 + (_chest.wave >= 10 ? 1 : 0) + (_chest.wave >= 20 ? 1 : 0);
            var got = new List<(Gear, Rarity)>();
            for (int i = 0; i < n; i++) got.Add((GearCatalog.RandomGear(), GearCatalog.Roll(_chest.wave + 5)));
            foreach (var g in got) Vault.Add(g.Item1, g.Item2);
            SaveVault();
            if (_chest.wave >= 10 && (Random.value < 0.6f || AutoTest.ForceGameItem)) ServerUnlockRandom(who);
            string item = "";
            if (Random.value < GameItemChance || AutoTest.ForceGameItem)
            {
                Rarity ir = GearCatalog.Roll(_chest.wave + 5);
                var pool = GameItems[(int)ir];
                string name = pool[Random.Range(0, pool.Length)];
                var prefab = GameInfo.GetSpawnable(name);
                if (prefab)
                {
                    var it = Instantiate(prefab, _chest.pos + Vector3.up * 1.2f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                    Server.Instance.Spawn(it.gameObject);
                    item = name + ":" + (int)ir;
                    AutoTest.Count("game_item_" + name);
                }
            }
            Broadcast(new LootEvent { Id = _chest.id, What = LootWhat.ChestGone, Pos = _chest.pos, Who = who ? NameOf(who) : "", Data = Pack(got), Item = item });
            _chest = default;
            AutoTest.Count("chests_opened");
        }

        // Round over: whatever is still lying around (and an unopened chest) goes into the vault anyway.
        public void ServerCollectAll()
        {
            var got = _drops.Values.Select(d => (d.g, d.r)).ToList();
            foreach (var id in _drops.Keys.ToList()) Broadcast(new LootEvent { Id = id, What = LootWhat.DropGone });
            _drops.Clear();
            if (got.Count > 0)
            {
                foreach (var g in got) Vault.Add(g.Item1, g.Item2);
                SaveVault();
                Broadcast(new LootEvent { What = LootWhat.Leftovers, Data = Pack(got) });
            }
            if (_chest.id != 0) OpenChest(null);
        }

        private void ServerTick()
        {
            EnsureLoaded();
            foreach (var kv in _drops.Where(kv => Time.time > kv.Value.until).ToList())
            {
                _drops.Remove(kv.Key);
                Broadcast(new LootEvent { Id = kv.Key, What = LootWhat.DropGone });
            }
            bool changed = false;
            foreach (var p in PlayerManager.AlivePlayers)
            {
                if (!p || !p.Transform) continue;
                Vector3 pos = p.Transform.position;
                foreach (var kv in _drops.Where(kv => (kv.Value.pos - pos).sqrMagnitude < PickRadius * PickRadius).ToList())
                {
                    _drops.Remove(kv.Key);
                    Vault.Add(kv.Value.g, kv.Value.r);
                    Broadcast(new LootEvent { Id = kv.Key, What = LootWhat.DropGone, Pos = kv.Value.pos, Gear = kv.Value.g, Rarity = kv.Value.r, Who = NameOf(p) });
                    AutoTest.Count("loot_picked");
                    changed = true;
                }
                if (_chest.id != 0 && (_chest.pos - pos).sqrMagnitude < ChestRadius * ChestRadius) OpenChest(p);
            }
            if (changed) SaveVault();
        }

        // ---------- host: vault actions (locked while a round is running) ----------

        public void ServerRequest(Player who, VaultRequest req)
        {
            if (!who || (FortressPlugin.Instance && FortressPlugin.Instance.Running)) return;
            EnsureLoaded();
            ulong key = KeyOf(who);
            Gear g = req.Gear;
            Rarity r = req.Rarity;
            switch (req.Action)
            {
                case VaultAction.Merge:
                    if (r == Rarity.Legendary || Vault.Count(g, r) < 3) return;
                    Vault.Add(g, r, -3);
                    Vault.Add(g, r + 1);
                    AutoTest.Count("merged");
                    break;
                case VaultAction.EquipFortress:
                    if (!GearCatalog.IsFortress(g) || Vault.Count(g, r) < 1 || Vault.Fortress.Count >= GearCatalog.FortressSlots || Vault.Fortress.Any(f => f.Item1 == g)) return;
                    Vault.Add(g, r, -1);
                    Vault.Fortress.Add((g, r));
                    break;
                case VaultAction.EquipPersonal:
                    var slots = Vault.Slots(key);
                    bool active = GearCatalog.IsActive(g);
                    int sameKind = slots.Count(s => GearCatalog.IsActive(s.Item1) == active);
                    if (GearCatalog.IsFortress(g) || Vault.Count(g, r) < 1 || sameKind >= (active ? GearCatalog.ActiveSlots : GearCatalog.PersonalSlots) || slots.Any(s => s.Item1 == g)) return;
                    Vault.Add(g, r, -1);
                    slots.Add((g, r));
                    Vault.Names[key] = NameOf(who);
                    break;
                case VaultAction.UnequipFortress:
                    if (req.Slot >= Vault.Fortress.Count) return;
                    var f0 = Vault.Fortress[req.Slot];
                    Vault.Fortress.RemoveAt(req.Slot);
                    Vault.Add(f0.Item1, f0.Item2);
                    break;
                case VaultAction.UnequipPersonal:
                    var mine = Vault.Slots(key);
                    if (req.Slot >= mine.Count) return;
                    var s0 = mine[req.Slot];
                    mine.RemoveAt(req.Slot);
                    Vault.Add(s0.Item1, s0.Item2);
                    break;
            }
            SaveVault();
        }

        // Auto-test: start from an empty vault.
        public void ServerWipeVaultForTest()
        {
            string save = SaveManager.CurServerSave?.Name;
            if (!string.IsNullOrEmpty(save) && File.Exists(FilePath(save))) File.Delete(FilePath(save));
            _loadedFor = null;
            EnsureLoaded();
        }

        public string ServerVaultFile => string.IsNullOrEmpty(_loadedFor) ? null : FilePath(_loadedFor);

        // Auto-test: put a piece of gear on a player even mid-round (the real menu refuses that).
        public void DebugEquip(Player who, Gear g, Rarity r)
        {
            EnsureLoaded();
            var slots = Vault.Slots(KeyOf(who));
            if (GearCatalog.IsFortress(g)) { Vault.Fortress.RemoveAll(f => f.Item1 == g); Vault.Fortress.Add((g, r)); }
            else { slots.RemoveAll(s => GearCatalog.IsActive(s.Item1) == GearCatalog.IsActive(g)); slots.Add((g, r)); }
            SaveVault();
        }

        // Auto-test: put gear straight into the vault.
        public void ServerGive(Gear g, Rarity r, int n = 1) { EnsureLoaded(); Vault.Add(g, r, n); SaveVault(); }

        // ---------- client: what you see ----------

        private void OnLoot(LootEvent e)
        {
            switch (e.What)
            {
                case LootWhat.Drop:
                    if (!_dropViews.ContainsKey(e.Id)) _dropViews[e.Id] = MakeDropView(e.Pos, e.Rarity);
                    break;
                case LootWhat.DropGone:
                    if (_dropViews.TryGetValue(e.Id, out var go)) { Destroy(go); _dropViews.Remove(e.Id); }
                    if (!string.IsNullOrEmpty(e.Who))
                    {
                        ChatManager.ChatMessage(L10n.T("lootPicked", e.Who, GearCatalog.Label(e.Gear, e.Rarity)));
                        Fx.Sound(e.Rarity >= Rarity.Epic ? "QuestReceived" : "PickupGrab", e.Pos);
                    }
                    break;
                case LootWhat.Chest:
                    if (_chestView) Destroy(_chestView);
                    _chestView = MakeChestView(e.Pos);
                    break;
                case LootWhat.ChestGone:
                    if (_chestView) Destroy(_chestView);
                    ChatManager.ChatMessage(string.IsNullOrEmpty(e.Who) ? L10n.T("chestAuto", Labels(e.Data)) : L10n.T("chestOpened", e.Who, Labels(e.Data)));
                    var f = (e.Item ?? "").Split(':');
                    if (f.Length == 2 && int.TryParse(f[1], out int ir))
                        ChatManager.ChatMessage(L10n.T("chestGameItem", $"<color=#{GearCatalog.Hex((Rarity)ir)}>[{GearCatalog.RarityName((Rarity)ir)}] {L10n.T("item_" + f[0])}</color>"));
                    Fx.Sound("CasinoWinEpic", e.Pos, 1f, AudioDistance.Long);
                    break;
                case LootWhat.Blueprint:
                    if (System.Enum.TryParse(e.Data, out PieceType bp))
                        ChatManager.ChatMessage(L10n.T(string.IsNullOrEmpty(e.Who) ? "blueprintFoundTeam" : "blueprintFound", e.Who, BuildSystem.PieceName(bp, 1)));
                    break;
                case LootWhat.Leftovers:
                    ChatManager.ChatMessage(L10n.T("lootLeftovers", Labels(e.Data)));
                    break;
            }
        }

        // A glowing crystal in the rarity's colour, bobbing and turning so it reads as "pick me up".
        private static GameObject MakeDropView(Vector3 pos, Rarity r)
        {
            var go = new GameObject("FortressLoot");
            go.transform.position = pos;
            var gem = Models.Attach("LootGem", go.transform, Vector3.one);
            gem.transform.localScale = Vector3.one * (r >= Rarity.Epic ? 0.6f : 0.45f);
            Models.Paint(gem, GearCatalog.Color(r));
            return go;
        }

        private static GameObject MakeChestView(Vector3 pos)
        {
            var go = new GameObject("FortressChest");
            go.transform.position = pos;
            Models.Attach("Chest", go.transform, new Vector3(1f, 0.7f, 0.7f), new Vector3(0f, 0.35f, 0f));
            return go;
        }

        private void AnimateViews()
        {
            float t = Time.time;
            foreach (var kv in _dropViews)
            {
                if (!kv.Value) continue;
                var c = kv.Value.transform.GetChild(0);
                c.localPosition = Vector3.up * (0.15f * Mathf.Sin(t * 2.5f + kv.Key));
                c.localRotation = Quaternion.Euler(20f, t * 90f + kv.Key * 37f, 20f);
            }
        }

        private void ClearViews()
        {
            foreach (var go in _dropViews.Values) if (go) Destroy(go);
            _dropViews.Clear();
            if (_chestView) Destroy(_chestView);
            ClientVault = new VaultData();
        }

        // Test / debug: the host's current loot on the ground.
        public int ServerDropCount => _drops.Count;
        public IEnumerable<Vector3> ServerDropPositions => _drops.Values.Select(d => d.pos);
        public Vector3? ServerChestPos => _chest.id != 0 ? _chest.pos : (Vector3?)null;
    }
}
