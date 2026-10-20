using System.Collections;
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
using TMPro;
using UnityEngine;

namespace HtfFortress
{
    // Host -> everyone: the whole vault (VaultData text).
    public struct VaultState : IBroadcast { public string Data; }

    public enum VaultAction : byte { Merge, EquipFortress, EquipPersonal, UnequipFortress, UnequipPersonal, Salvage, Ascend, Enhance, Deposit, Take, Expand, TakeItem, Buy, StoreSlot, Mod, SellAll, BuyItem }
    // Client -> host: one vault action. Take / TakeItem: Slot is the desk it's handed out at (0 the vault's door, 1 the
    // smithy's counter), Item the game item's id, Count how many. Expand / Enhance / Mod: Count is the level the player saw,
    // so two players clicking at once (or a click repeated on a slow link) buy one step, not two. Mod: Slot is the line.
    // Into: Take / TakeItem dragged onto the hotbar: 0 anywhere, n its slot n - 1. StoreSlot: Slot is the hotbar slot dragged
    // onto the vault, whose thing goes in. SellAll: Slot is the tab, Rarity the colour, Count 1 every colour (VaultData.SellAll). BuyItem: Item is the
    // game's item bought at the smithy (Loot.ShopItems).
    public struct VaultRequest : IBroadcast { public VaultAction Action; public Gear Gear; public Rarity Rarity; public byte Slot; public byte Item; public byte Count; public byte Into; }

    public enum LootWhat : byte { Drop, DropGone, Chest, ChestGone, Leftovers, Blueprint, Overflow, Cleared, Scales, ScalesGone, Give }
    // Client -> host: pick up the drop you're looking at (E).
    public struct LootPick : IBroadcast { public int Id; }
    // Host -> everyone: gear on the ground, chests, and who got what.
    public struct LootEvent : IBroadcast { public int Id; public LootWhat What; public Vector3 Pos; public Gear Gear; public Rarity Rarity; public string Who; public string Data; public string Item; }

    // Gear that outlives a round: enemies sometimes drop a piece, every 5th wave leaves a chest by the lighthouse,
    // and everything goes into the team vault saved with the host's save. The loadout is set between rounds.
    public class Loot : MonoBehaviour
    {
        public static Loot Instance;
        private const float PickRadius = 1.2f, ReachRadius = 4f, DropLife = 90f; // walk onto a drop, or E on it from ReachRadius (a chest: E only)

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
        private readonly Dictionary<int, GameObject> _scaleViews = new Dictionary<int, GameObject>();
        private GameObject _chestView;
        public bool ClientHasDrops => _dropViews.Count > 0;
        public bool ClientHasScales => _scaleViews.Count > 0;
        public Vector3? ClientChestPos => _chestView ? _chestView.transform.position : (Vector3?)null;
        private NetworkManager _registeredOn;

        static Loot()
        {
            GenericWriter<LootPick>.SetWrite((w, v) => w.WriteInt32(v.Id));
            GenericReader<LootPick>.SetRead(r => new LootPick { Id = r.ReadInt32() });
            GenericWriter<VaultState>.SetWrite((w, v) => w.WriteString(v.Data ?? ""));
            GenericReader<VaultState>.SetRead(r => new VaultState { Data = r.ReadString() });
            GenericWriter<VaultRequest>.SetWrite((w, v) => { w.WriteByte((byte)v.Action); w.WriteByte((byte)v.Gear); w.WriteByte((byte)v.Rarity); w.WriteByte(v.Slot); w.WriteByte(v.Item); w.WriteByte(v.Count); w.WriteByte(v.Into); });
            GenericReader<VaultRequest>.SetRead(r => new VaultRequest { Action = (VaultAction)r.ReadByte(), Gear = (Gear)r.ReadByte(), Rarity = (Rarity)r.ReadByte(), Slot = r.ReadByte(), Item = r.ReadByte(), Count = r.ReadByte(), Into = r.ReadByte() });
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
        public static ulong MyKey => Player.LocalPlayer ? KeyOf(Player.LocalPlayer) : 0;
        internal static string NameOf(Player p) => string.IsNullOrEmpty(p.SteamName) ? "Player " + p.ObjectId : p.SteamName;

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
                nm.ServerManager.RegisterBroadcast(Net.Safe<VaultRequest>((conn, req, ch) =>
                {
                    var who = PlayerManager.Players.FirstOrDefault(p => p && p.Owner == conn);
                    if (who) ServerRequest(who, req);
                }));
                nm.ServerManager.RegisterBroadcast(Net.Safe<LootPick>((conn, req, ch) =>
                {
                    var who = PlayerManager.Players.FirstOrDefault(p => p && p.Owner == conn);
                    if (who && who.Transform && _drops.TryGetValue(req.Id, out var d) && (d.pos - who.Transform.position).sqrMagnitude < ReachRadius * ReachRadius)
                    { ServerPick(req.Id, who); SaveVault(); }
                    // The chest opens with E (walking past it opened it before, and what flew out looked like your own gear falling).
                    else if (who && who.Transform && _chest.id != 0 && req.Id == _chest.id && (_chest.pos - who.Transform.position).sqrMagnitude < (ReachRadius + 1f) * (ReachRadius + 1f))
                        OpenChest(who);
                }));
                nm.SceneManager.OnClientLoadedStartScenes += (conn, asServer) =>
                {
                    if (!asServer) return;
                    SendVault(conn);
                    foreach (var kv in _drops) InstanceFinder.ServerManager.Broadcast(conn, new LootEvent { Id = kv.Key, What = LootWhat.Drop, Pos = kv.Value.pos, Gear = kv.Value.g, Rarity = kv.Value.r });
                    if (_chest.id != 0) InstanceFinder.ServerManager.Broadcast(conn, new LootEvent { Id = _chest.id, What = LootWhat.Chest, Pos = _chest.pos });
                    foreach (var kv in _scales) InstanceFinder.ServerManager.Broadcast(conn, new LootEvent { Id = kv.Key, What = LootWhat.Scales, Pos = kv.Value.pos, Data = kv.Value.n.ToString() });
                };
                // _loadedFor stays: quitting mid-round, Stop() runs a frame later (the menu has cleared the save by then) and must still save the record.
                nm.ServerManager.OnServerConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) { _drops.Clear(); _scales.Clear(); _chest = default; } };
                nm.ClientManager.OnClientConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) ClearViews(); };
            }
            if (InstanceFinder.IsServerStarted && Time.time >= _nextTick) { _nextTick = Time.time + 0.2f; ServerTick(); }
            if (_unsent && Time.time >= _nextSend) SendChange();
            AnimateViews();
            DrawScalesHud();
        }

        // ---------- host: the vault file ----------

        // A copy of the game's own save file (and this vault's) the first time a save is played with the mod in a session, the
        // last five kept, in BepInEx/fortress-backups: a new version of the mod can't cost anyone their world (the playtest worried).
        private static readonly HashSet<string> BackedUp = new HashSet<string>();
        private static void BackupSave(string save)
        {
            if (!BackedUp.Add(save)) return;
            try
            {
                string dir = Path.Combine(Paths.BepInExRootPath, "fortress-backups");
                Directory.CreateDirectory(dir);
                string safe = string.Concat(save.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)), stamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string saves = Path.Combine(Application.persistentDataPath, "Saves");
                foreach (var f in Directory.Exists(saves) ? Directory.GetFiles(saves, save + ".txt", SearchOption.AllDirectories) : new string[0])
                    File.Copy(f, Path.Combine(dir, $"{safe}-{stamp}-{Path.GetFileName(Path.GetDirectoryName(f))}.txt"), true);
                if (File.Exists(FilePath(save))) File.Copy(FilePath(save), Path.Combine(dir, $"{safe}-{stamp}-vault.txt"), true);
                foreach (var old in Directory.GetFiles(dir, safe + "-*").OrderByDescending(f => f).Skip(10)) File.Delete(old); // five sessions (save + vault)
            }
            catch (System.Exception e) { BuildSystem.Log.LogWarning("Save backup failed: " + e.Message); }
        }

        private static string FilePath(string save) => Path.Combine(Paths.BepInExRootPath, "fortress-vault", Safe(save) + ".txt");
        private static string Safe(string save) => string.Concat(save.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

        private void EnsureLoaded()
        {
            string save = SaveManager.CurServerSave?.Name;
            if (string.IsNullOrEmpty(save) || save == _loadedFor) return;
            _loadedFor = save;
            BackupSave(save);
            string text = ReadSafely(FilePath(save));
            Vault = text != null ? VaultData.Read(text) : new VaultData();
            BuildSystem.Log.LogInfo($"Vault for save '{save}': {Vault.Owned.Values.Sum()} gear and weapons, {Vault.Items.Count} of the game's items, {Vault.SlotsUsed}/{Vault.StorageSlots} slots, {Vault.Fortress.Count} fortress slots in use.");
            SendVault(null);
        }

        // The vault in memory is the truth. Its file is written when the game writes its own save (VaultSaves: the autosave
        // every 5 minutes, the save button, going to the menu, quitting), so a crash takes both back to the same moment (Owen
        // asked; a weapon taken out between the two saves used to vanish, one put in came back twice). With no world running
        // (a round wound up after its host left) nothing of the game's is left to keep in step: written at once.
        // Everyone hears of a change at once, then at most twice a second (the helpers bring things in one after another).
        private bool _unsent;
        private float _nextSend;
        internal void SaveVault()
        {
            if (string.IsNullOrEmpty(_loadedFor)) return;
            if (!InstanceFinder.IsServerStarted) { FlushVault(); return; }
            _unsent = true;
            if (Time.time >= _nextSend) SendChange();
        }
        private void SendChange() { _unsent = false; _nextSend = Time.time + 0.5f; SendVault(null); }

        internal bool FlushVault()
        {
            if (string.IsNullOrEmpty(_loadedFor)) return false;
            bool ok = WriteSafely(FilePath(_loadedFor), Vault.Write());
            if (!ok) BuildSystem.Log.LogError($"The vault of '{_loadedFor}' couldn't be written: the last good one is kept.");
            return ok;
        }

        // Written the way the game writes its saves: to a temporary file, read back, then swapped in with the old one kept
        // as .backup (if it was whole); read back from the .backup when the file itself is missing or empty.
        public static bool WriteSafely(string path, string text)
        {
            string tmp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(tmp, text);
                if (File.ReadAllText(tmp) != text) return false;
                if (!File.Exists(path)) File.Move(tmp, path);
                else File.Replace(tmp, path, string.IsNullOrWhiteSpace(File.ReadAllText(path)) ? null : path + ".backup");
                return true;
            }
            catch (System.Exception e) { BuildSystem.Log?.LogWarning("Writing " + path + ": " + e.Message); return false; }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch (System.Exception) { } }
        }

        public static string ReadSafely(string path)
        {
            foreach (string p in new[] { path, path + ".backup" })
                try
                {
                    string text = File.Exists(p) ? File.ReadAllText(p) : null;
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    if (p != path) BuildSystem.Log?.LogWarning($"{path} is missing or empty: read its backup instead.");
                    return text;
                }
                catch (System.Exception e) { BuildSystem.Log?.LogWarning("Reading " + p + ": " + e.Message); }
            return null;
        }

        // The game deleted a save: its vault goes to the backups, so a new save of the same name starts with an empty one
        // (it used to inherit the old one's legendaries and scales).
        internal static void ArchiveVault(string save)
        {
            try
            {
                string path = FilePath(save), dir = Path.Combine(Paths.BepInExRootPath, "fortress-backups");
                if (Instance && Instance._loadedFor == save) { Instance._loadedFor = null; Instance.Vault = new VaultData(); }
                if (!File.Exists(path)) return;
                Directory.CreateDirectory(dir);
                File.Move(path, Path.Combine(dir, $"deleted-{Safe(save)}-{System.DateTime.Now:yyyyMMdd-HHmmss}-vault.txt"));
                if (File.Exists(path + ".backup")) File.Delete(path + ".backup");
                BuildSystem.Log.LogInfo($"Save '{save}' deleted: its vault moved to {dir}.");
            }
            catch (System.Exception e) { BuildSystem.Log.LogWarning($"Archiving the vault of '{save}': {e.Message}"); }
        }

        private void SendVault(NetworkConnection to)
        {
            if (!InstanceFinder.IsServerStarted) return;
            var s = new VaultState { Data = Vault.Write(forClients: true) };
            if (to != null) InstanceFinder.ServerManager.Broadcast(to, s); else InstanceFinder.ServerManager.Broadcast(s);
        }

        private static void Broadcast(LootEvent e) { if (InstanceFinder.IsServerStarted) InstanceFinder.ServerManager.Broadcast(e); }

        // ---------- host: drops and chests ----------

        // An enemy died: maybe it leaves a piece of gear where it fell (the killer's lucky charm raises the odds).
        // From wave 2 on, the first kill of a round that hasn't dropped anything yet always does.
        private int _roundDrops;
        public void ServerNewRound()
        {
            _roundDrops = 0;
            EnsureLoaded();
            var island = FortressIsland.Instance;
            if (!WeaponItems.Ready || !island || !island.Exists) return;
            Vector3 circle = island.Center + FortressIsland.ReadyLocal;
            bool changed = false, gave = false;
            int n = 0;
            Vector3 Spot() => circle + Quaternion.Euler(0f, 40f * n++, 0f) * Vector3.forward * 3f + Vector3.up * 0.8f;
            // Weapons equipped from before they were items come out by the start circle (the ones in the vault are the
            // smithy's rack now).
            foreach (var slots in Vault.Personal.Values)
                foreach (var w in slots.Where(x => GearCatalog.IsWeapon(x.Item1)).ToList()) { Wait(WeaponItems.ServerSpawn(w.Item1, w.Item2, Spot())); slots.Remove(w); changed = true; }
            // A first weapon for each player, once per save: a common rocket launcher on the ground by the circle.
            foreach (var p in PlayerManager.Players.Where(p => p))
            {
                ulong key = KeyOf(p);
                if (Vault.Starter.Contains(key) || WeaponItems.Carries(p)) continue;
                Vault.Starter.Add(key);
                Wait(WeaponItems.ServerSpawn(Gear.Rocket, Rarity.Common, Spot()));
                changed = gave = true;
                AutoTest.Count("starter_weapon");
            }
            if (changed) SaveVault();
            if (gave) Fx.Announce("starterWeapon");
        }

        // Chance that a kill drops gear; `luck` is the killer's lucky charm (0 = none).
        public static float NormalDrop = 0.06f, EliteDrop = 0.35f;

        public static float DropChance(EnemyKind kind, bool elite, int wave, int roundDrops, float luck)
        {
            float chance = EnemyCatalog.IsBoss(kind) ? 1f : elite ? EliteDrop : NormalDrop;
            if (roundDrops == 0 && wave >= 2) chance = 1f;
            return chance * Mathf.Max(1f, luck);
        }

        public void ServerOnKill(Vector3 pos, EnemyKind kind, bool elite, int killerId, int wave)
        {
            var killer = PlayerManager.Players.FirstOrDefault(p => p && p.ObjectId == killerId);
            float chance = DropChance(kind, elite, wave, _roundDrops, killer ? Vault.PersonalValue(KeyOf(killer), Gear.LuckyCharm) : 0f);
            if (EnemyCatalog.IsBoss(kind)) ServerUnlockRandom(killer); // every boss leaves a blueprint while any are left
            if (elite && !EnemyCatalog.IsBoss(kind) && Random.value < EliteScaleChance) ServerDropScales(pos, 1, 1, 0f);
            if (Random.value >= chance) return;
            Rarity r = GearCatalog.Roll(wave, EnemyCatalog.IsBoss(kind) ? Rarity.Epic : Rarity.Common);
            if (killer && Perks.Legend(killer, Gear.LuckyCharm) && r < Rarity.Legendary) { r++; Perks.Proc(Gear.LuckyCharm, pos); } // legendary: one rarity better
            Drop(GearCatalog.RandomGear(), r, pos);
            _roundDrops++;
        }

        // The surface under `from`, looking through walls, turrets and the lighthouse: loot on top of those can't be reached
        // (floors and ramps are fine, you can stand on them).
        internal static bool Ground(Vector3 from, float depth, out Vector3 at)
        {
            foreach (var h in Physics.RaycastAll(from, Vector3.down, depth, GameInfo.LevelLayer).OrderBy(h => h.distance))
            {
                if (BuildSystem.IsWall(h.collider) || h.collider.gameObject.name == "FortressCore") continue;
                at = h.point;
                return true;
            }
            at = from;
            return false;
        }

        private void Drop(Gear g, Rarity r, Vector3 pos)
        {
            // Big or flung bodies die above the ground: put the drop on the ground (or on the water) below.
            if (Ground(pos + Vector3.up * 2f, 40f, out var ground)) pos = ground + Vector3.up * 0.5f;
            pos.y = Mathf.Max(pos.y, WaterManager.GetWaterHeight(pos) + 0.3f);
            if (GearCatalog.IsWeapon(g) && WeaponItems.ServerSpawn(g, r, pos + Vector3.up * 0.5f) is Item wi && wi) { _lootWeapons.Add(wi); AutoTest.Count("loot_dropped"); return; } // a real weapon on the ground (E, or walk over it)
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
            Ground(island.Center + new Vector3(3.5f, 4f, -3f), 10f, out var p);
            _chest = (_nextId++, p, wave);
            Broadcast(new LootEvent { Id = _chest.id, What = LootWhat.Chest, Pos = p });
            Fx.Announce("chestBanner");
            AutoTest.Count("chests");
        }

        // The game's own things a chest can throw out (they land beside the chest and are yours to keep); half the time it's
        // one of the game's guns instead, as a fortress weapon of a rolled rarity.
        private static readonly string[] GameItems = { "knife", "brassknuckles", "dynamite", "disc", "moneyinabottle" };
        private static readonly Gear[] GameGuns = GearCatalog.WeaponKinds.Where(GearCatalog.IsGameGun).ToArray();
        public const float GameItemChance = 0.3f;

        // A chest's gear: 2 pieces, 3 from wave 10, 4 from wave 20, rolled as if five waves later.
        public static List<(Gear, Rarity)> ChestRoll(int wave)
        {
            int n = 2 + (wave >= 10 ? 1 : 0) + (wave >= 20 ? 1 : 0);
            var got = new List<(Gear, Rarity)>();
            for (int i = 0; i < n; i++) got.Add((GearCatalog.RandomGear(), GearCatalog.Roll(wave + 5)));
            return got;
        }

        private void OpenChest(Player who)
        {
            var got = ChestRoll(_chest.wave);
            bool extra = Random.value < GameItemChance || AutoTest.ForceGameItem;
            if (extra && !AutoTest.ForceGameItem && Random.value < 0.5f) { got.Add((GameGuns[Random.Range(0, GameGuns.Length)], GearCatalog.Roll(_chest.wave + 5))); extra = false; }
            int pop = 0;
            foreach (var g in got)
            {
                // Weapons come out as real items, flung around the chest; the rest goes into the vault.
                if (GearCatalog.IsWeapon(g.Item1) && WeaponItems.ServerSpawn(g.Item1, g.Item2, _chest.pos + Quaternion.Euler(0f, 90f * pop++, 0f) * Vector3.forward * 0.8f + Vector3.up * 1.2f) is Item ci && ci) { _lootWeapons.Add(ci); continue; }
                ServerStore(g.Item1, g.Item2);
            }
            ServerDropScales(_chest.pos, 2 + Mathf.Clamp(_chest.wave / 10, 0, 6), 3, 1.6f);
            SaveVault();
            if (_chest.wave >= 10 && (Random.value < 0.6f || AutoTest.ForceGameItem)) ServerUnlockRandom(who);
            string item = "";
            if (extra)
            {
                string name = GameItems[Random.Range(0, GameItems.Length)];
                var prefab = GameInfo.GetSpawnable(name);
                if (prefab)
                {
                    var it = Instantiate(prefab, _chest.pos + Vector3.up * 1.2f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                    Server.Instance.Spawn(it.gameObject);
                    item = name + ":0";
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
            int left = _scales.Values.Sum(x => x.n);
            foreach (var id in _scales.Keys.ToList()) ServerPickScales(id, null);
            if (left > 0) Broadcast(new LootEvent { What = LootWhat.ScalesGone, Data = left.ToString(), Who = "*" });
            var got = _drops.Values.Select(d => (d.g, d.r)).ToList();
            foreach (var id in _drops.Keys.ToList()) Broadcast(new LootEvent { Id = id, What = LootWhat.DropGone });
            _drops.Clear();
            if (got.Count > 0)
            {
                foreach (var g in got) ServerStore(g.Item1, g.Item2);
                SaveVault();
                Broadcast(new LootEvent { What = LootWhat.Leftovers, Data = Pack(got) });
            }
            if (_chest.id != 0) OpenChest(null);
        }

        // Into the vault, or, when the vault is full, straight into fish scales (nothing is lost; the team sees it happen).
        // Returns false when it was dismantled.
        private bool ServerStore(Gear g, Rarity r)
        {
            if (Vault.Fits(g, r)) { Vault.Add(g, r); return true; }
            Vault.Scales += GearCatalog.SalvageScales[(int)r];
            Broadcast(new LootEvent { What = LootWhat.Overflow, Gear = g, Rarity = r, Data = Vault.StorageSlots.ToString() });
            AutoTest.Count("overflow_salvaged");
            return false;
        }

        // ---------- fish scales on the ground ----------
        // Shiny blue scales where they fell (a boss bursts into a ring of them, a chest has a few, an elite now and then):
        // walk over them and the team's scales go up. Left lying, they go into the vault when the round ends.
        private readonly Dictionary<int, (Vector3 pos, int n)> _scales = new Dictionary<int, (Vector3, int)>();
        private readonly HashSet<Item> _lootWeapons = new HashSet<Item>(); // weapons that dropped (kills, the chest): walked over, into the vault
        public const float ScaleRadius = 1.5f, EliteScaleChance = 0.25f;
        public static int BossScales(int wave) => 6 + 3 * Mathf.Clamp(wave / 10, 1, 10); // 9 at wave 10, 36 at wave 100

        public void ServerDropScales(Vector3 at, int total, int pieces, float spread)
        {
            if (total <= 0) return;
            pieces = Mathf.Clamp(pieces, 1, total);
            for (int i = 0; i < pieces; i++)
            {
                int n = total / pieces + (i < total % pieces ? 1 : 0);
                Vector3 p = at + Quaternion.Euler(0f, i * 360f / pieces + Random.Range(-20f, 20f), 0f) * Vector3.forward * (pieces == 1 ? 0f : Random.Range(spread * 0.4f, spread));
                if (Ground(p + Vector3.up * 3f, 40f, out var g)) p = g + Vector3.up * 0.35f;
                p.y = Mathf.Max(p.y, WaterManager.GetWaterHeight(p) + 0.3f);
                int id = _nextId++;
                _scales[id] = (p, n);
                Broadcast(new LootEvent { Id = id, What = LootWhat.Scales, Pos = p, Data = n.ToString() });
            }
            AutoTest.Count("scales_dropped", total);
        }

        // Picked up (by `p`, or `who`: a helper), or swept into the vault at the end of the round (neither).
        private void ServerPickScales(int id, Player p, string who = "")
        {
            if (!_scales.TryGetValue(id, out var s)) return;
            _scales.Remove(id);
            Vault.Scales += s.n;
            Broadcast(new LootEvent { Id = id, What = LootWhat.ScalesGone, Pos = s.pos, Data = s.n.ToString(), Who = p ? NameOf(p) : who });
            if (p) AutoTest.Count("scales_picked", s.n);
        }

        private void ServerPick(int id, Player p)
        {
            if (!_drops.TryGetValue(id, out var d)) return;
            _drops.Remove(id);
            ServerStore(d.g, d.r);
            Broadcast(new LootEvent { Id = id, What = LootWhat.DropGone, Pos = d.pos, Gear = d.g, Rarity = d.r, Who = NameOf(p) });
            AutoTest.Count("loot_picked");
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
                    ServerPick(kv.Key, p);
                    changed = true;
                }
                foreach (var kv in _scales.Where(kv => (kv.Value.pos - pos).sqrMagnitude < ScaleRadius * ScaleRadius).ToList())
                {
                    ServerPickScales(kv.Key, p);
                    changed = true;
                }
                // A dropped weapon walked over goes straight into the vault (E still takes it in hand): no carrying it to the door.
                foreach (var w in _lootWeapons.ToList())
                {
                    if (!w || w.IsDestroying || !w.IsSpawned || w.Holder || w.SyncedHolder || w.IsInInventory) { _lootWeapons.Remove(w); continue; }
                    if ((w.transform.position - pos).sqrMagnitude >= PickRadius * PickRadius || !WeaponItems.Is(w, out var wg, out var wr) || !Vault.Fits(wg, wr)) continue;
                    _lootWeapons.Remove(w);
                    Vault.Add(wg, wr);
                    Broadcast(new LootEvent { What = LootWhat.DropGone, Pos = w.transform.position, Gear = wg, Rarity = wr, Who = NameOf(p) });
                    w.DestroyItem();
                    AutoTest.Count("weapon_auto_stored");
                    changed = true;
                }
            }
            if (changed) SaveVault();
        }

        // ---------- host: what the stall's helpers pick up besides the bodies (Owen: "小幫手…應該要什麼都撿") ----------

        // Something lying on the island: a gear drop or a pile of scales (Id), or an item (a weapon, one of the game's things).
        public struct Lying { public int Id; public Item Item; public Vector3 Pos; public object Key => (object)Item ?? Id; }
        public const string HelperWho = "\u0001helper"; // LootEvent.Who for a helper (each screen says it in its own language)
        // An item waits this long lying still (and only if it fits in the vault): what you drop for a friend stays for them.
        public const float LieStill = 15f;
        private Dictionary<Item, (float since, Vector3 at)> _still = new Dictionary<Item, (float, Vector3)>();
        private readonly List<Lying> _lying = new List<Lying>();
        private float _nextLying;

        // The weapons by the start circle are left for their owners a few minutes (a friend new to the mode is slow to find theirs).
        private readonly Dictionary<Item, float> _helpersWait = new Dictionary<Item, float>();
        private void Wait(Item it) { if (it) _helpersWait[it] = Time.time + 180f; }

        private bool FitsVault(Item it) => WeaponItems.AsFortress(it, out var g, out var r) ? Vault.Fits(g, r) : Vault.FitsItem(it.ID);

        // Looked over twice a second: not on a boat, not a fish's body (those are the baskets'), on the island or in its shallows.
        public List<Lying> ServerLying()
        {
            var island = FortressIsland.Instance;
            if (Time.time < _nextLying || !island || !island.Exists) return _lying;
            _nextLying = Time.time + 0.5f;
            _lying.Clear();
            foreach (var kv in _drops) _lying.Add(new Lying { Id = kv.Key, Pos = kv.Value.pos });
            foreach (var kv in _scales) _lying.Add(new Lying { Id = kv.Key, Pos = kv.Value.pos });
            var bodies = new HashSet<Creature>(FortressNet.Instance.ServerCorpses());
            foreach (var k in _helpersWait.Where(kv => !kv.Key || Time.time >= kv.Value).Select(kv => kv.Key).ToList()) _helpersWait.Remove(k);
            var still = new Dictionary<Item, (float since, Vector3 at)>();
            foreach (var it in ItemManager.Items.Values)
            {
                if (!it || !island.OnIsland(it.transform.position, FortressIsland.IslandRadius + 4f) || !Storable(it)
                    || (it.RigidbodySync && it.RigidbodySync.OnBoat) || (it.Creature && bodies.Contains(it.Creature)) || _helpersWait.ContainsKey(it) || !FitsVault(it)) continue;
                Vector3 p = it.transform.position;
                var s = _still.TryGetValue(it, out var was) && (was.at - p).sqrMagnitude < 0.25f ? was : (since: Time.time, at: p);
                still[it] = s;
                if (Time.time - s.since >= LieStill) _lying.Add(new Lying { Item = it, Pos = p });
            }
            _still = still;
            return _lying;
        }

        // A helper reached it (`basket`: where it goes): into the vault as if a player had walked over it (gear that doesn't
        // fit, as scales), scales to the team's. False when it's gone or no longer fits.
        public bool ServerHelperPick(object key, Vector3 basket)
        {
            EnsureLoaded();
            if (key is int id && _drops.TryGetValue(id, out var d))
            {
                _drops.Remove(id);
                ServerStore(d.g, d.r);
                Broadcast(new LootEvent { Id = id, What = LootWhat.DropGone, Pos = d.pos, Gear = d.g, Rarity = d.r, Who = HelperWho });
                Fx.Send(FxKind.Scoop, d.pos, basket);
            }
            else if (key is int sid && _scales.TryGetValue(sid, out var s))
            {
                ServerPickScales(sid, null, HelperWho);
                Fx.Send(FxKind.Scoop, s.pos, basket);
            }
            else if (key is Item it && Storable(it) && FitsVault(it))
            {
                Vector3 at = it.transform.position;
                if (WeaponItems.AsFortress(it, out var g, out var r))
                {
                    Vault.Add(g, r);
                    Broadcast(new LootEvent { What = LootWhat.DropGone, Pos = at, Gear = g, Rarity = r, Who = HelperWho });
                }
                else if (SavedOf(it) is SavedItem saved) Vault.AddItem(new StoredItem { Saved = saved, Dead = it.Creature && it.Creature.IsDead });
                else return false;
                Fx.Send(FxKind.Scoop, at, basket);
                it.DestroyItem();
            }
            else return false;
            SaveVault();
            return true;
        }

        // ---------- host: vault actions (locked while a round is running) ----------

        // Everything, the loadout too, works during a round (the playtest wanted to change gear mid-run).
        public void ServerRequest(Player who, VaultRequest req)
        {
            if (!who) return;
            EnsureLoaded();
            if (req.Action == VaultAction.Expand && req.Count != Vault.BoughtOf((Expansion)req.Slot)) return;
            if (req.Action == VaultAction.Enhance && req.Count != Vault.EnhanceOf(req.Gear)) return;
            ulong key = KeyOf(who);
            if (req.Action == VaultAction.Mod && req.Count != (byte)Vault.ModOf(key, req.Gear, req.Slot)) return; // the level it was bought at (its low byte: levels go past 255)
            if (req.Action == VaultAction.Deposit) { if (!ServerDeposit(who, req.Gear, req.Rarity)) return; }
            else if (req.Action == VaultAction.Take) { if (!ServerTake(who, req.Gear, req.Rarity, Mathf.Max(1, (int)req.Count), req.Into - 1)) return; }
            else if (req.Action == VaultAction.TakeItem) { if (!ServerTakeItem(who, req.Item, Mathf.Max(1, (int)req.Count), req.Into - 1)) return; }
            else if (req.Action == VaultAction.StoreSlot) { if (!ServerStoreSlot(who, req.Slot)) return; }
            else if (req.Action == VaultAction.Buy) { if (!ServerBuy(who, req.Gear)) return; }
            else if (req.Action == VaultAction.BuyItem) { if (!ServerBuyItem(who, req.Item)) return; }
            else if (req.Action == VaultAction.SellAll) { if (!ServerSellAll(req.Slot, req.Count == 1 ? (Rarity?)null : req.Rarity)) return; }
            else if (!Vault.Apply(req.Action, req.Gear, req.Rarity, req.Slot, key)) return;
            if (req.Action == VaultAction.EquipPersonal || req.Action == VaultAction.Mod) Vault.Names[key] = NameOf(who);
            SaveVault();
        }

        // A weapon you carry goes into the vault (the one in your hand last, so you keep holding it if you can).
        private bool ServerDeposit(Player who, Gear g, Rarity r)
        {
            if (!GearCatalog.IsWeapon(g) || !who.Inventory) return false;
            var held = who.Holding ? who.Holding.HeldItem : null;
            bool Match(Item i) => i && !i.IsDestroying && WeaponItems.Is(i, out var ig, out var ir) && ig == g && ir == r;
            var item = who.Inventory._items.Values.FirstOrDefault(i => Match(i) && i != held) ?? (Match(held) ? held : null);
            if (!item || !Vault.Fits(g, r)) return false;
            who.Inventory.RemoveItem(item);
            item.DestroyItem();
            Vault.Add(g, r);
            AutoTest.Count("weapon_racked");
            return true;
        }

        // Dropped or thrown on the smithy's anvil: into the vault for the bench (sparks and forge smoke).
        public void ServerDepositThrown(Item item, Gear g, Rarity r, Vector3 into)
        {
            if (!item || item.IsDestroying || Swallow.IsGoing(item)) return;
            EnsureLoaded();
            if (!Vault.Fits(g, r)) { VaultFullNotice(); return; } // stays on the anvil
            Vault.Add(g, r);
            Swallow.Server(item, into, Gulp.Forge);
            AutoTest.Count("weapon_racked");
            SaveVault();
        }

        // Anything thrown at the vault's door goes in: a fortress weapon as one more of its kind, the game's own items each
        // as it is (its weight, cookness, skin, attachments: the game's own save of an item). The same things the game
        // would keep lying in the world (no live fish, nothing held, no player's body), and no quest items.
        public void ServerStoreThrown(Item item, Vector3 into)
        {
            if (!Storable(item)) return;
            EnsureLoaded();
            if (WeaponItems.AsFortress(item, out var g, out var r)) // one of the game's guns becomes a common of its kind
            {
                if (!Vault.Fits(g, r)) { VaultFullNotice(); return; }
                Vault.Add(g, r);
            }
            else
            {
                var saved = SavedOf(item);
                if (saved == null) return;
                if (!Vault.FitsItem(item.ID)) { VaultFullNotice(); return; }
                Vault.AddItem(new StoredItem { Saved = saved, Dead = item.Creature && item.Creature.IsDead });
            }
            Swallow.Server(item, into, Gulp.Vault);
            AutoTest.Count("vault_stored");
            SaveVault();
        }

        internal static bool Storable(Item item) =>
            item && item.IsSpawned && !item.IsDestroying && !item.IsDeinitializing && !Swallow.IsGoing(item) && !item.Holder && !item.SyncedHolder && !item.IsInInventory
            && !item.BirdHolder && !item.AttachedRod && !item.DeadPlayer && !item.IsQuestItem && (!item.Creature || item.Creature.IsDead)
            && !(HandedOut.TryGetValue(item, out float until) && Time.time < until) && !Weapons.OnLine(item);

        // Just taken out at the vault's door: a fish rolling back into it mustn't go straight back in.
        private static readonly Dictionary<Item, float> HandedOut = new Dictionary<Item, float>();
        private static void JustOut(Item item)
        {
            if (!item) return;
            foreach (var k in HandedOut.Where(kv => !kv.Key || Time.time > kv.Value).Select(kv => kv.Key).ToList()) HandedOut.Remove(k);
            HandedOut[item] = Time.time + 4f;
        }

        private static readonly System.Reflection.MethodInfo ToSaved = HarmonyLib.AccessTools.Method(typeof(SaveManager), "ItemToSavedItem");
        internal static SavedItem SavedOf(Item item)
        {
            try { return ToSaved?.Invoke(null, new object[] { (byte)0, item }) as SavedItem; }
            catch (System.Exception e) { BuildSystem.Log.LogWarning("Can't store " + item.name + ": " + e.GetBaseException().Message); return null; }
        }

        private float _nextFull;
        private void VaultFullNotice()
        {
            if (Time.time < _nextFull) return;
            _nextFull = Time.time + 5f;
            Fx.Announce("vaultFullNotice", Vault.StorageSlots);
        }

        // Taken out of the vault: at the feet of whoever took it, and into their hands if they have room (their own screen picks
        // it up, as if they'd pressed E on it) — the playtest wanted it on them, not on the floor by the door.
        private static Vector3 AtFeet(Player who) =>
            who && who.Transform ? who.Transform.position + Vector3.up * 1.2f + new Vector3(Random.Range(-0.2f, 0.2f), 0f, Random.Range(-0.2f, 0.2f)) : HandOut(0);
        // `slot`: the hotbar slot it was dragged onto (-1: any).
        private static void GiveTo(Player who, Item it, int slot = -1)
        {
            if (who && it && InstanceFinder.IsServerStarted) InstanceFinder.ServerManager.Broadcast(who.Owner, new LootEvent { What = LootWhat.Give, Id = it.ObjectId, Data = slot >= 0 ? slot.ToString() : "" });
        }
        // ...and straight to their mouth: they eat it at once (the stall's grilled fish).
        internal static void HandToEat(Player who, Item it)
        {
            if (who && it && InstanceFinder.IsServerStarted) InstanceFinder.ServerManager.Broadcast(who.Owner, new LootEvent { What = LootWhat.Give, Id = it.ObjectId, Data = "eat" });
        }

        // Dragged from a hotbar slot onto the vault: that slot's thing goes in (one of the game's guns as a common of its
        // kind), the same things the vault's door takes (no quest items, no one's body, nothing alive).
        private bool ServerStoreSlot(Player who, byte slot)
        {
            if (!who.Inventory || !who.Inventory._items.TryGetValue(slot, out var item) || !item || item.IsDestroying || item.IsQuestItem || item.DeadPlayer
                || (item.Creature && !item.Creature.IsDead)) return false;
            EnsureLoaded();
            if (WeaponItems.AsFortress(item, out var g, out var r))
            {
                if (!Vault.Fits(g, r)) { VaultFullNotice(); return false; }
                Vault.Add(g, r);
            }
            else
            {
                var saved = SavedOf(item);
                if (saved == null) return false;
                if (!Vault.FitsItem(item.ID)) { VaultFullNotice(); return false; }
                Vault.AddItem(new StoredItem { Saved = saved, Dead = item.Creature && item.Creature.IsDead });
            }
            who.Inventory.RemoveItem(item);
            item.DestroyItem();
            AutoTest.Count("hotbar_stored");
            return true;
        }

        // Where things handed out land: in front of the vault's door, or on the ground before the smithy's counter.
        private static Vector3 HandOut(int desk)
        {
            var island = FortressIsland.Instance;
            Vector3 at = desk == 1 ? island.SmithyCounter : island.VaultDoor + Vector3.up * 0.9f;
            return at + new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(0f, 0.3f), Random.Range(-0.35f, 0.35f));
        }

        // Out of the vault: weapons (n of them) land at the desk, to pick up with E.
        private bool ServerTake(Player who, Gear g, Rarity r, int n, int slot = -1)
        {
            var island = FortressIsland.Instance;
            if (!GearCatalog.IsWeapon(g) || Vault.Count(g, r) < 1 || !island || !island.Exists) return false;
            n = Mathf.Min(n, Vault.Count(g, r));
            int taken = 0;
            for (int i = 0; i < n; i++) if (WeaponItems.ServerSpawn(g, r, AtFeet(who)) is Item it && it) { JustOut(it); GiveTo(who, it, i == 0 ? slot : -1); taken++; }
            Vault.Add(g, r, -taken);
            AutoTest.Count("weapon_taken", taken);
            return taken > 0;
        }

        // The smith's shop: a common weapon of any kind, for the crew's money (round or not), handed to the buyer like
        // anything taken out. A special weapon (the absorber, the gatling) is dearer, and only
        // for sale once the save's best round has beaten the first boss (the gatling: the wave 80 boss, Owen "應該要過80關才能解鎖";
        // till then the shop shows "$???").
        public const int WeaponPrice = 300, SpecialPrice = 5000, SpecialBest = 10, GatlingBest = 80;
        public static int ShopPrice(Gear g) => Siege.Money(GearCatalog.IsSpecial(g) ? SpecialPrice : WeaponPrice);
        public static int UnlockBest(Gear g) => g == Gear.Gatling ? GatlingBest : SpecialBest;
        public static bool ForSale(Gear g, VaultData v) => !GearCatalog.IsSpecial(g) || v.Best >= UnlockBest(g);

        // Active gear (K) too (Owen: 「鐵匠台那邊應該賣更多東西」): a common one, straight into the vault, used up like any
        // (Actives). Dearer the further this save has got (its record), as what they do grows with the wave: $4,500 on a new
        // save, $9,000 at 10, $27,000 at 50, $49,500 at 100, a third to half of a wave's takings then (Sim's economy), so a
        // few for a boss fight, not one for every press.
        public const int ActivePrice = 150;
        public static int ActiveShopPrice(int best) => Siege.Money(ActivePrice * (10 + Mathf.Max(0, best)) / 10);
        public static int ShopPrice(Gear g, VaultData v) => GearCatalog.IsActive(g) ? ActiveShopPrice(v.Best) : ShopPrice(g);
        // What the shop hands over: a weapon once it's for sale, active gear while the vault has room for it.
        public static bool Buyable(Gear g, VaultData v) => GearCatalog.IsWeapon(g) ? ForSale(g, v) : GearCatalog.IsActive(g) && v.Fits(g, Rarity.Common);

        private bool ServerBuy(Player who, Gear g)
        {
            var island = FortressIsland.Instance;
            if (!Buyable(g, Vault) || !island || !island.Exists || !Siege.Instance.ServerTrySpend(ShopPrice(g, Vault), always: true)) return false;
            if (GearCatalog.IsActive(g)) Vault.Add(g, Rarity.Common); // K uses it
            else if (WeaponItems.ServerSpawn(g, Rarity.Common, AtFeet(who)) is Item it && it) { JustOut(it); GiveTo(who, it); }
            AutoTest.Count("weapon_bought");
            return true;
        }

        // ...and the game's own explosives (Owen: 「怎麼沒賣炸藥」; the playtest wanted to blast their boat free): every item
        // of the game's that is an Explosive, found when the shop is first drawn (none found: none sold), at the game's own
        // shop price (Item.Cost, what its shops ask; one that no shop sells: twice what it sells for), handed over like a weapon.
        private static List<byte> _explosives;
        public static List<byte> ShopItems => _explosives != null && _explosives.Count > 0 ? _explosives : (_explosives = FindExplosives());
        private static List<byte> FindExplosives()
        {
            var all = typeof(GameInfo).GetField("_allItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null) as Dictionary<byte, Item>;
            var found = all == null ? new List<byte>() : all.Values.Where(i => i is Explosive).Select(i => i.ID).Distinct().OrderBy(id => id).ToList();
            if (found.Count > 0) BuildSystem.Log.LogInfo("The smithy sells the game's explosives: " + string.Join(", ", found.Select(id => $"{id} {all[id].name} ${ItemPrice(id)}")));
            else if (!_noneLogged && all != null && all.Count > 0) { _noneLogged = true; BuildSystem.Log.LogWarning($"No explosive among the game's {all.Count} items: the smithy sells none."); }
            return found;
        }
        private static bool _noneLogged;
        public static int ItemPrice(byte id) => GameInfo.GetSpawnable(id) is Item p && p ? (p.Currency == Currency.Dollar && p.Cost > 0 ? p.Cost : Mathf.Max(Siege.Money(10), 2 * p.DefaultWorth)) : 0;

        private bool ServerBuyItem(Player who, byte id)
        {
            var island = FortressIsland.Instance;
            var prefab = GameInfo.GetSpawnable(id);
            if (!ShopItems.Contains(id) || !prefab || !island || !island.Exists || !Siege.Instance.ServerTrySpend(ItemPrice(id), always: true)) return false;
            var it = Instantiate(prefab, AtFeet(who), Quaternion.identity, Server.Instance.DynamicObjectsHolder);
            Server.Instance.Spawn(it.gameObject);
            JustOut(it);
            GiveTo(who, it);
            AutoTest.Count("item_bought");
            return true;
        }

        // The quartermaster's one-click sale: the money is the crew's (round or not), as if carried to the stall's counter.
        private bool ServerSellAll(int tab, Rarity? colour)
        {
            var island = FortressIsland.Instance;
            if (!island || !island.Exists) return false;
            var (money, _, n) = Vault.SellAll(tab, colour, ItemWorth);
            if (n == 0) return false;
            Siege.Instance.ServerPay(money);
            Fx.Send(FxKind.Sell, island.VaultDoor, island.VaultDoor, 0, money);
            AutoTest.Count("vault_sold", n);
            return true;
        }

        // What the stall's counter pays for one of the game's items (the game's Item.TotalWorth, from what was saved of it).
        private static readonly System.Reflection.FieldInfo WorthField = HarmonyLib.AccessTools.Field(typeof(Item), "_worth");
        private static int ItemWorth(StoredItem s)
        {
            var prefab = GameInfo.GetSpawnable(s.Id);
            if (!prefab || WorthField == null) return 0;
            var x = s.Saved;
            float weight = x.Weight > 0f ? x.Weight : 1f;
            return (int)((int)((int)WorthField.GetValue(prefab) * weight * GameInfo.CooknessWorthCurve.Evaluate(x.Cookness) * x.BettingMultiplier) * x.KillScoreMultiplier);
        }

        // ...and the game's own items, each as it went in (a fish is dead again, as the game loads one from a save).
        private bool ServerTakeItem(Player who, byte id, int n, int slot = -1)
        {
            var island = FortressIsland.Instance;
            if (Vault.CountItem(id) < 1 || !island || !island.Exists) return false;
            int taken = 0;
            for (int i = 0; i < n; i++)
            {
                var s = Vault.TakeItem(id);
                if (s == null) break;
                var it = Spawn(s, AtFeet(who));
                if (it) { JustOut(it); GiveTo(who, it, i == 0 ? slot : -1); taken++; }
                else { Vault.AddItem(s); break; }
            }
            AutoTest.Count("item_taken", taken);
            return taken > 0;
        }

        internal static Item Spawn(StoredItem s, Vector3 at)
        {
            var prefab = GameInfo.GetSpawnable(s.Id);
            if (!prefab) return null;
            var item = Instantiate(prefab, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), Server.Instance.DynamicObjectsHolder);
            item.LoadFromSave(s.Saved);
            if (item.Creature)
            {
                if (s.Dead) item.Creature.ServerKillOnSpawn();
                if (s.Saved.IsDripCreature) item.Creature.SetDrip();
            }
            Server.Instance.Spawn(item.gameObject);
            return item;
        }

        // Shift+F7 (test key): gear drops and the chest gone, without going into the vault.
        public int ServerClearDrops()
        {
            int n = _drops.Count + _scales.Count + (_chest.id != 0 ? 1 : 0);
            _drops.Clear();
            _scales.Clear();
            _chest = default;
            Broadcast(new LootEvent { What = LootWhat.Cleared });
            return n;
        }

        // Host: the record lives in the save's vault, so test saves and practice rounds don't touch the real one.
        public bool ServerRecord(int waves)
        {
            EnsureLoaded();
            if (waves <= Vault.Best) return false;
            Vault.Best = waves;
            SaveVault();
            return true;
        }

        // OVER 99 on this save: the day and who held the lighthouse (it turns gold; the record board lists them).
        public void ServerWin(string team)
        {
            EnsureLoaded();
            Vault.Wins.Add((System.DateTime.Now.ToString("yyyy-MM-dd"), team));
            SaveVault();
        }

        // Auto-test: start from an empty vault.
        public void ServerWipeVaultForTest()
        {
            string save = SaveManager.CurServerSave?.Name;
            if (!string.IsNullOrEmpty(save)) foreach (string p in new[] { FilePath(save), FilePath(save) + ".backup" }) if (File.Exists(p)) File.Delete(p);
            _loadedFor = null;
            EnsureLoaded();
        }

        public string ServerVaultFile => string.IsNullOrEmpty(_loadedFor) ? null : FilePath(_loadedFor);

        // Auto-test: a drop right here.
        public void DebugDrop(Gear g, Rarity r, Vector3 pos) => Drop(g, r, pos);
        public List<Vector3> DebugScales => _scales.Values.Select(v => v.pos).ToList();
        public (int id, Vector3 pos) DebugChest => (_chest.id, _chest.pos);

        // Auto-test: put a piece of gear on a player even mid-round (the real menu refuses that).
        public void DebugEquip(Player who, Gear g, Rarity r)
        {
            EnsureLoaded();
            var slots = Vault.Slots(KeyOf(who));
            if (GearCatalog.IsActive(g)) Vault.Add(g, r); // nothing to wear: the team having it is enough
            else if (GearCatalog.IsFortress(g)) { Vault.Fortress.RemoveAll(f => f.Item1 == g); Vault.Fortress.Add((g, r)); }
            else { slots.RemoveAll(s => GearCatalog.SlotKind(s.Item1) == GearCatalog.SlotKind(g)); slots.Add((g, r)); }
            SaveVault();
        }

        // Auto-test: the best record on this save (expansions wait for it).
        public void DebugBest(int waves) { EnsureLoaded(); Vault.Best = waves; SaveVault(); }

        // Auto-test: put gear straight into the vault.
        public void ServerGive(Gear g, Rarity r, int n = 1) { EnsureLoaded(); Vault.Add(g, r, n); SaveVault(); }

        // ---------- client: what you see ----------

        private void OnLoot(LootEvent e)
        {
            switch (e.What)
            {
                case LootWhat.Drop:
                    if (!_dropViews.ContainsKey(e.Id)) _dropViews[e.Id] = MakeDropView(e.Id, e.Pos, e.Gear, e.Rarity);
                    break;
                case LootWhat.DropGone:
                    if (_dropViews.TryGetValue(e.Id, out var go)) { Destroy(go); _dropViews.Remove(e.Id); }
                    if (!string.IsNullOrEmpty(e.Who))
                    {
                        ChatManager.ChatMessage(L10n.T("lootPicked", e.Who == HelperWho ? L10n.T("helperWho") : e.Who, GearCatalog.Label(e.Gear, e.Rarity)));
                        Fx.Sound(e.Rarity >= Rarity.Epic ? "QuestReceived" : "PickupGrab", e.Pos);
                        if (Player.LocalPlayer && e.Who == NameOf(Player.LocalPlayer)) GearCards.Instance.ShowGear(e.Gear, e.Rarity);
                    }
                    break;
                case LootWhat.Chest:
                    if (_chestView) Destroy(_chestView);
                    _chestView = MakeChestView(e.Id, e.Pos);
                    break;
                case LootWhat.ChestGone:
                    if (_chestView) Destroy(_chestView);
                    ChatManager.ChatMessage(string.IsNullOrEmpty(e.Who) ? L10n.T("chestAuto", Labels(e.Data)) : L10n.T("chestOpened", e.Who, Labels(e.Data)));
                    var f = (e.Item ?? "").Split(':');
                    if (f.Length == 2 && int.TryParse(f[1], out int ir))
                        ChatManager.ChatMessage(L10n.T("chestGameItem", $"<color=#{GearCatalog.Hex((Rarity)ir)}>[{GearCatalog.RarityName((Rarity)ir)}] {L10n.T("item_" + f[0])}</color>"));
                    Fx.Sound("CasinoWinEpic", e.Pos, 1f, AudioDistance.Long);
                    GearCards.Instance.ShowChest(Unpack(e.Data).ToList(), f.Length == 2 && int.TryParse(f[1], out int gr) ? L10n.T("item_" + f[0]) + "|" + gr : null);
                    break;
                case LootWhat.Blueprint:
                    if (System.Enum.TryParse(e.Data, out PieceType bp))
                        ChatManager.ChatMessage(L10n.T(string.IsNullOrEmpty(e.Who) ? "blueprintFoundTeam" : "blueprintFound", e.Who, BuildSystem.PieceName(bp, 1)));
                    break;
                case LootWhat.Give:
                    StartCoroutine(TakeInHand(e.Id, int.TryParse(e.Data, out int into) ? into : -1, e.Data == "eat"));
                    break;
                case LootWhat.Scales:
                    if (!_scaleViews.ContainsKey(e.Id)) _scaleViews[e.Id] = MakeScaleView(e.Pos, int.TryParse(e.Data, out int sn) ? sn : 1);
                    break;
                case LootWhat.ScalesGone:
                    if (_scaleViews.TryGetValue(e.Id, out var sv)) { Destroy(sv); _scaleViews.Remove(e.Id); }
                    if (e.Who == "*") ChatManager.ChatMessage(L10n.T("scalesSwept", e.Data));
                    else if (!string.IsNullOrEmpty(e.Who))
                    {
                        StartCoroutine(FloatText(e.Pos + Vector3.up * 0.6f, $"<color=#7fd0ff>+{e.Data} ◆</color>"));
                        Fx.Sound("PickupGrab", e.Pos);
                    }
                    break;
                case LootWhat.Cleared:
                    foreach (var view in _dropViews.Values) if (view) Destroy(view);
                    _dropViews.Clear();
                    foreach (var view in _scaleViews.Values) if (view) Destroy(view);
                    _scaleViews.Clear();
                    if (_chestView) Destroy(_chestView);
                    break;
                case LootWhat.Leftovers:
                    ChatManager.ChatMessage(L10n.T("lootLeftovers", Labels(e.Data)));
                    break;
                case LootWhat.Overflow:
                    ChatManager.ChatMessage(L10n.T("lootOverflow", e.Data, GearCatalog.Label(e.Gear, e.Rarity), GearCatalog.SalvageScales[(int)e.Rarity]));
                    break;
            }
        }

        // The piece itself (its own model, the one its icon is drawn from), turning and bobbing over a ring in its rarity's
        // colour, with a glowing gem of that colour over it from rare up (RarityGem), so it's seen across the island.
        // Walk onto it or look at it and press E (the game's outline and prompt).
        public static string DropModel(Gear g)
        {
            if (GearCatalog.IsWeapon(g)) return Weapons.ModelName(g);
            switch (g)
            {
                case Gear.WarChest: return "Chest";
                case Gear.Decoy: return "Decoy";
                case Gear.BigNet: return "HandNetBag";
                default: return "Gear" + g;
            }
        }

        private static readonly Dictionary<Rarity, Material> BeamMats = new Dictionary<Rarity, Material>();
        private static GameObject MakeDropView(int id, Vector3 pos, Gear g, Rarity r)
        {
            var go = new GameObject("FortressLoot");
            go.transform.position = pos;
            var spin = new GameObject("Spin").transform; // AnimateViews turns child 0
            spin.SetParent(go.transform, false);
            var model = Models.Attach(DropModel(g), spin, Vector3.one * 0.4f);
            var mf = model.GetComponent<MeshFilter>();
            if (mf && mf.sharedMesh)
            {
                var b = mf.sharedMesh.bounds;
                float k = (r >= Rarity.Epic ? 0.75f : 0.6f) / Mathf.Max(0.05f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
                model.transform.localScale = Vector3.one * k;
                model.transform.localPosition = -b.center * k;
            }
            else Models.Paint(model, GearCatalog.Color(r));
            if (!BeamMats.TryGetValue(r, out var bm)) BeamMats[r] = bm = Models.Make(GearCatalog.Color(r), GearCatalog.Color(r) * 1.2f);
            Part(PrimitiveType.Cylinder, go.transform, new Vector3(0f, -0.42f, 0f), new Vector3(0.7f, 0.01f, 0.7f), bm);
            var gem = go.AddComponent<RarityGem>(); // over the bobbing piece
            gem.Rarity = r;
            gem.Height = 0.75f;
            // The game's own pick-up: outline and "[E]" on the piece, through our Interactable.
            var grab = new GameObject("Grab");
            grab.SetActive(false);
            grab.transform.SetParent(go.transform, false);
            grab.layer = LayerMask.NameToLayer("Interactable");
            grab.tag = "Interactable";
            var col = grab.AddComponent<SphereCollider>();
            col.radius = 0.55f;
            var it = grab.AddComponent<LootGrab>();
            it.Id = id; it.Gear = g; it.Rarity = r;
            HarmonyLib.AccessTools.Field(typeof(Interactable), "_interactCol").SetValue(it, col);
            HarmonyLib.AccessTools.Field(typeof(Interactable), "_textTarget").SetValue(it, go.transform);
            HarmonyLib.AccessTools.Field(typeof(Interactable), "_modelsToOutline").SetValue(it, new[] { model });
            grab.SetActive(true);
            return go;
        }

        private static void Part(PrimitiveType type, Transform parent, Vector3 at, Vector3 scale, Material m)
        {
            var x = GameObject.CreatePrimitive(type);
            Destroy(x.GetComponent<Collider>());
            x.transform.SetParent(parent, false);
            x.transform.localPosition = at;
            x.transform.localScale = scale;
            var rend = x.GetComponent<Renderer>();
            rend.sharedMaterial = m;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static GameObject MakeChestView(int id, Vector3 pos)
        {
            var go = new GameObject("FortressChest");
            go.transform.position = pos;
            var model = Models.Attach("Chest", go.transform, new Vector3(1f, 0.7f, 0.7f), new Vector3(0f, 0.35f, 0f));
            // E opens it (the game's outline and "[E]" on it, like a drop).
            var grab = new GameObject("Grab");
            grab.SetActive(false);
            grab.transform.SetParent(go.transform, false);
            grab.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            grab.layer = LayerMask.NameToLayer("Interactable");
            grab.tag = "Interactable";
            var col = grab.AddComponent<BoxCollider>();
            col.size = new Vector3(1.2f, 0.9f, 0.9f);
            var it = grab.AddComponent<LootGrab>();
            it.Id = id; it.Chest = true;
            HarmonyLib.AccessTools.Field(typeof(Interactable), "_interactCol").SetValue(it, col);
            HarmonyLib.AccessTools.Field(typeof(Interactable), "_textTarget").SetValue(it, go.transform);
            HarmonyLib.AccessTools.Field(typeof(Interactable), "_modelsToOutline").SetValue(it, new[] { model.gameObject });
            grab.SetActive(true);
            return go;
        }

        // A scale (or a little pile of them, by how many it's worth): flat, blue, glowing, turning slowly over the ground.
        private static Material _scaleMat;
        private static GameObject MakeScaleView(Vector3 pos, int n)
        {
            var go = new GameObject("FortressScales");
            go.transform.position = pos;
            var spin = new GameObject("Spin").transform; // AnimateViews turns child 0
            spin.SetParent(go.transform, false);
            if (!_scaleMat) _scaleMat = BossVfx.Glow(new Color(0.25f, 0.62f, 1f), 6f);
            int pieces = Mathf.Clamp(n, 1, 3);
            for (int i = 0; i < pieces; i++)
            {
                var s = WeaponFx.Piece(WeaponFx.Ball, _scaleMat, spin);
                s.localPosition = new Vector3((i - (pieces - 1) * 0.5f) * 0.16f, i * 0.04f, (i % 2) * 0.08f);
                s.localRotation = Quaternion.Euler(25f + 15f * i, 40f * i, 10f);
                s.localScale = new Vector3(0.4f, 0.05f, 0.29f);
            }
            return go;
        }

        // What the vault just handed this player: picked up as soon as it exists on this screen (if there's room for it).
        // `slot`: the hotbar slot it was dragged onto. The game's own rule puts it there: a held thing that isn't in the hotbar
        // goes into the slot you select, when that slot is empty. So: hands emptied (what's held goes back to its slot),
        // picked up, and once the pickup has landed, that slot selected (it goes in, hands empty again).
        private static IEnumerator TakeInHand(int objectId, int slot = -1, bool eat = false)
        {
            for (float t = 0f; t < 3f; t += 0.1f)
            {
                var me = Player.LocalPlayer;
                var cm = InstanceFinder.ClientManager;
                if (me && cm != null && cm.Objects.Spawned.TryGetValue(objectId, out var nob) && nob && nob.GetComponent<Item>() is Item it && it && !it.Holder && !it.IsInInventory)
                {
                    bool into = slot >= 0 && me.Inventory && SlotEmpty(me, slot);
                    var before = me.Holding ? me.Holding.HeldItem : null;
                    if ((into || eat) && me.Holding && me.Holding.HeldItem) { me.Inventory.LocalTrySelectSlot(-1); yield return null; }
                    try { it.PickUp(me, calledFromLocal: true, sendToServer: true); } catch (System.Exception e) { BuildSystem.Log.LogWarning("Couldn't hand over " + it.name + ": " + e.Message); yield break; }
                    if (eat) { yield return EatHeld(me, it, before); yield break; }
                    if (!into) yield break;
                    for (float w = 0f; w < 2f && !(me.Holding.HeldItem == it && !me.Holding.UninitializedHeldItem); w += Time.deltaTime) yield return null;
                    yield return new WaitForSeconds(0.15f); // the host has it as held too
                    if (me.Holding.HeldItem == it && SlotEmpty(me, slot)) { me.Inventory.LocalTrySelectSlot(slot); AutoTest.Count("hotbar_into"); }
                    yield break;
                }
                yield return new WaitForSeconds(0.1f);
            }
        }

        // The game's own eating, started for you (as if you held the left button with it in hand): it goes to your mouth,
        // you chew, the host fills you up and heals you. Then back to what you had in hand.
        private static readonly System.Reflection.MethodInfo StartEatingMethod = HarmonyLib.AccessTools.Method(typeof(PlayerEating), "StartEating");
        private static IEnumerator EatHeld(Player me, Item food, Item before)
        {
            for (float w = 0f; w < 2f && me && !(me.Holding.HeldItem == food && !me.Holding.UninitializedHeldItem); w += Time.deltaTime) yield return null;
            var eating = me ? me.GetComponentInChildren<PlayerEating>(true) : null;
            if (!eating || !food || me.Holding.HeldItem != food) yield break;
            yield return new WaitForSeconds(0.2f); // in hand on the host too
            try { StartEatingMethod?.Invoke(eating, null); } catch (System.Exception e) { BuildSystem.Log.LogWarning("Couldn't start eating: " + e.Message); yield break; }
            AutoTest.Count("food_eating");
            for (float w = 0f; w < 8f && food && !food.IsDestroying; w += Time.deltaTime) yield return null;
            if (!before || !me || !me.Inventory || me.Holding.HeldItem) yield break;
            foreach (var kv in me.Inventory._items) if (kv.Value == before) { me.Inventory.LocalTrySelectSlot(kv.Key); break; }
        }

        private static bool SlotEmpty(Player me, int slot) => !me.Inventory._items.TryGetValue((byte)slot, out var there) || !there;

        // "+3 ◆" rising off where it was picked up, facing the camera.
        // A few at a time (each its own text mesh): helpers sweeping a field of scales put up dozens at once.
        private static readonly List<GameObject> Floating = new List<GameObject>();
        private static IEnumerator FloatText(Vector3 at, string text)
        {
            Floating.RemoveAll(x => !x);
            if (!Hud.Font || Floating.Count >= (ClientSettings.Reduced ? 3 : 8)) yield break;
            var go = new GameObject("FortressFloatText");
            Floating.Add(go);
            var t = go.AddComponent<TextMeshPro>();
            t.font = Hud.Font; t.text = text; t.fontSize = 6f; t.alignment = TextAlignmentOptions.Center; t.fontStyle = FontStyles.Bold;
            t.outlineWidth = 0.2f; t.outlineColor = new Color32(0, 0, 0, 255);
            for (float s = 0f; s < 1.2f && go; s += Time.deltaTime)
            {
                go.transform.position = at + Vector3.up * s * 1.2f;
                var cam = GameInfo.CurCamera;
                if (cam) go.transform.rotation = Quaternion.LookRotation(go.transform.position - cam.transform.position);
                t.alpha = Mathf.Clamp01((1.2f - s) / 0.4f);
                yield return null;
            }
            Destroy(go);
        }

        // The team's scales, always on screen (bottom left, over the money), bumping up when they grow.
        private TextMeshProUGUI _scalesHud;
        private int _scalesShown = -1;
        private float _scalesBump;
        private void DrawScalesHud()
        {
            bool show = Hud.Font && FortressIsland.Instance && FortressIsland.Instance.Exists && Player.LocalPlayer && !Hud.ModalOpen;
            if (!show) { if (_scalesHud) _scalesHud.gameObject.SetActive(false); return; }
            if (!_scalesHud) _scalesHud = Hud.Label(Hud.Root, "", 26f, Vector2.zero, new Vector2(48f, 182f), new Vector2(300f, 36f), TextAlignmentOptions.Left, Color.white, bold: true, outline: true, pivot: Vector2.zero); // over the game's money box
            _scalesHud.gameObject.SetActive(true);
            int n = ClientVault.Scales;
            if (n != _scalesShown) { if (_scalesShown >= 0 && n > _scalesShown) _scalesBump = 1f; _scalesShown = n; _scalesHud.text = $"<color=#7fd0ff>◆</color> {n} <size=70%>{L10n.T("scalesName")}</size>"; }
            _scalesBump = Mathf.MoveTowards(_scalesBump, 0f, Time.deltaTime * 2.5f);
            _scalesHud.transform.localScale = Vector3.one * (1f + 0.25f * _scalesBump);
        }

        private void AnimateViews()
        {
            float t = Time.time;
            foreach (var kv in _scaleViews)
            {
                if (!kv.Value) continue;
                var c = kv.Value.transform.GetChild(0);
                c.localPosition = Vector3.up * (0.1f * Mathf.Sin(t * 3f + kv.Key));
                c.localRotation = Quaternion.Euler(0f, t * 90f + kv.Key * 53f, 0f);
            }
            foreach (var kv in _dropViews)
            {
                if (!kv.Value) continue;
                var c = kv.Value.transform.GetChild(0);
                c.localPosition = Vector3.up * (0.15f * Mathf.Sin(t * 2.5f + kv.Key));
                c.localRotation = Quaternion.Euler(0f, t * 70f + kv.Key * 37f, 0f);
            }
        }

        private void ClearViews()
        {
            foreach (var go in _dropViews.Values) if (go) Destroy(go);
            _dropViews.Clear();
            foreach (var go in _scaleViews.Values) if (go) Destroy(go);
            _scaleViews.Clear();
            if (_chestView) Destroy(_chestView);
            ClientVault = new VaultData();
        }

        // Test / debug: the host's current loot on the ground.
        public int ServerDropCount => _drops.Count;
        public IEnumerable<Vector3> ServerDropPositions => _drops.Values.Select(d => d.pos);
        public Vector3? ServerChestPos => _chest.id != 0 ? _chest.pos : (Vector3?)null;
    }

    // E on a piece of gear lying on the ground: "[Rare] Gunpowder / Pick up [E]", the outline, then the host stores it.
    public class LootGrab : Interactable
    {
        public int Id;
        public Gear Gear;
        public Rarity Rarity;
        public bool Chest; // the round's chest, not a drop
        private string _text;

        public override void Hover()
        {
            if (!_isHovering)
            {
                _text = Chest ? $"{L10n.T("chestName")}\n{L10n.T("chestOpen")} {SpriteManager.GetPickUpInput()}"
                              : $"{GearCatalog.Label(Gear, Rarity)}\n{L10n.T("lootPickUp")} {SpriteManager.GetPickUpInput()}";
                PlayerUI.SetLookAtText(_text, TextTarget);
            }
            base.Hover();
        }

        public override void UnHover()
        {
            PlayerUI.HideLookAtText(_text);
            base.UnHover();
        }

        public override void Interact(Player player)
        {
            base.Interact(player);
            if (player != Player.LocalPlayer || !InstanceFinder.IsClientStarted) return;
            UnHover();
            InstanceFinder.ClientManager.Broadcast(new LootPick { Id = Id });
        }

        protected override void OnDestroy()
        {
            PlayerUI.HideLookAtText(_text);
            base.OnDestroy();
        }
    }
}

namespace HtfFortress
{
    // The vault is written with the game's own save (Loot.FlushVault) and leaves with it when the game deletes a save.
    [HarmonyLib.HarmonyPatch]
    internal static class VaultSaves
    {
        [HarmonyLib.HarmonyPostfix, HarmonyLib.HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveServer), typeof(bool))]
        private static void Saved(bool autoSave)
        {
            if (Loot.Instance && Loot.Instance.FlushVault() && !autoSave) ChatManager.ChatMessage("<i>" + L10n.T("vaultSaved") + "</i>"); // under the game's own "saved" line
        }

        [HarmonyLib.HarmonyPostfix, HarmonyLib.HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.DeleteServer))]
        private static void Deleted(string name) => Loot.ArchiveVault(name);
    }
}
