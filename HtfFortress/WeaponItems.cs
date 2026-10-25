using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Managing.Object;
using FishNet.Object;
using HarmonyLib;
using UnityEngine;

namespace HtfFortress
{
    // Marks one of the fortress weapon items (on the prefab, so every copy in the world has it).
    public class FortressWeaponItem : MonoBehaviour
    {
        public Gear Gear;
        public Rarity Rarity;
        public int Stars;
    }

    // Fortress weapons are the game's own items: picked up with E, carried in the hotbar (1-9), dropped with Q, held
    // where friends see them, saved with the world and inventories like any other item. Each weapon x rarity is its own
    // item, built at start-up from the game's pistol with our model on it and registered under item ids 200-255, then
    // 150-177 for the rocket and the game's guns, 190-197 for the absorber and the gatling (the game uses 0-102, the boss trophies 180-189), in the same order on every
    // machine, so the item ids and FishNet's prefab ids agree across the network.
    // Firing is ours (Weapons.cs): the pistol's own shooting, aiming, reloading and inspecting are switched off for them.
    // The game's guns (pistol, shotgun, SMG, sniper, assault rifle, bow) are copies of themselves instead: their own model,
    // shooting and reloading, with the host multiplying what they hit fortress fish for by rarity (GunDamage).
    public static class WeaponItems
    {
        public const byte FirstId = 200, SecondId = 150, ThirdId = 190;
        public static bool Ready { get; private set; }
        private static readonly Dictionary<(Gear, Rarity), Item> Prefabs = new Dictionary<(Gear, Rarity), Item>();
        public static readonly Gear[] Kinds = GearCatalog.All.Where(GearCatalog.IsWeapon).ToArray();

        public static byte IdOf(Gear g, Rarity r) => (byte)(g < Gear.Rocket ? FirstId + (g - Gear.Laser) * 4 + (int)r
                                                         : g < Gear.Absorber ? SecondId + (g - Gear.Rocket) * 4 + (int)r : ThirdId + (g - Gear.Absorber) * 4 + (int)r);

        // The game's gun a fortress one copies.
        public static string GameName(Gear g) => g == Gear.Sniper ? "sniperrifle" : g == Gear.Rifle ? "assaultrifle" : g.ToString().ToLower();
        public static byte GameId(Gear g) => GameInfo.GetSpawnable(GameName(g)) is Item i && i ? i.ID : (byte)0;

        // A fortress weapon, or one of the game's guns as it was (a common of its kind: what it turns into in the vault).
        public static bool AsFortress(Item item, out Gear g, out Rarity r)
        {
            if (Is(item, out g, out r)) return true;
            r = Rarity.Common;
            foreach (var k in Kinds)
                if (GearCatalog.IsGameGun(k) && item && item.ID == GameId(k)) { g = k; return true; }
            return false;
        }

        // Host: what one hit of a game gun on a fortress fish is multiplied by: the rarity times the wave's
        // growth as our weapons have it, cut to the gun's share (the game's guns hit far harder than ours: an assault rifle
        // pours out ~10x a laser's damage) but never below the gun as the game made it.
        public static float GunDamage(Gear g, Rarity r) => GunDamage(GearCatalog.Value(g, r), g, Weapons.Scale);
        public static float GunDamage(float value, Gear g, float scale) => value * Mathf.Max(1f, GunShare(g) * scale);
        // Measured (auto-test "guns"): pistol 25 a shot, shotgun 25 pellets of 3 (two in the magazine), SMG 24 x20/s, sniper
        // 200 a second, assault rifle 40 x14/s, bow 250 an arrow. Shares that bring each to ~45 a second kept up, about our weapons'.
        public static float GunShare(Gear g) => g == Gear.Pistol || g == Gear.Shotgun ? 0.7f : g == Gear.Smg ? 0.22f : g == Gear.Sniper ? 0.34f : g == Gear.Rifle ? 0.16f : 0.24f;
        public const float GunCrit = 0.15f; // a legendary game gun's chance of a triple hit

        // What a weapon sells for (dollars): about the game's own guns, a legendary like its assault rifle.
        public static int Worth(Rarity r) => new[] { 400, 1200, 3500, 9000 }[(int)r];

        public static bool Is(Item item, out Gear g, out Rarity r)
        {
            var tag = item ? item.GetComponent<FortressWeaponItem>() : null;
            g = tag ? tag.Gear : default;
            r = tag ? tag.Rarity : default;
            return tag;
        }

        public static Item Prefab(Gear g, Rarity r) => Prefabs.TryGetValue((g, r), out var p) ? p : null;

        // Every machine, as soon as the game's item list and the network manager exist (before any session): build and
        // register the 32 items. Returns whether it's done.
        public static bool TryRegister()
        {
            if (Ready) return true;
            var pistol = GameInfo.GetSpawnable("pistol");
            var nm = InstanceFinder.NetworkManager;
            if (!pistol || !nm || !(nm.SpawnablePrefabs is SinglePrefabObjects network)) return false;
            var names = (Dictionary<string, Item>)AccessTools.Field(typeof(GameInfo), "_nameToSpawnable").GetValue(null);
            var byId = (Dictionary<byte, Item>)AccessTools.Field(typeof(GameInfo), "_idToSpawnable").GetValue(null);
            var all = (Dictionary<byte, Item>)AccessTools.Field(typeof(GameInfo), "_allItems").GetValue(null);
            var idField = AccessTools.Field(typeof(Item), "_id");
            var worthField = AccessTools.Field(typeof(Item), "_worth");
            if (Kinds.Any(g => GearCatalog.Rarities.Any(r => byId.ContainsKey(IdOf(g, r)) && !Is(byId[IdOf(g, r)], out _, out _))))
            {
                BuildSystem.Log.LogError("Item ids 150-177, 190-197 or 200-255 are taken by the game: fortress weapons can't be items.");
                Ready = true; // don't try again every frame
                return true;
            }
            // Templates live under an inactive holder: nothing on them wakes up; the copies the game spawns do.
            var holder = new GameObject("FortressWeaponItems");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            foreach (var g in Kinds)
                foreach (var r in GearCatalog.Rarities)
                {
                    var from = GearCatalog.IsGameGun(g) ? GameInfo.GetSpawnable(GameName(g)) : pistol;
                    if (!from) { BuildSystem.Log.LogError($"No {GameName(g)} in the game: fortress {g} can't be an item."); continue; }
                    var go = Object.Instantiate(from.gameObject, holder.transform);
                    go.name = $"Fortress{g}{r}";
                    var item = go.GetComponent<Item>();
                    byte id = IdOf(g, r);
                    idField.SetValue(item, id);
                    worthField.SetValue(item, Worth(r));
                    var tag = go.AddComponent<FortressWeaponItem>();
                    tag.Gear = g;
                    tag.Rarity = r;
                    if (GearCatalog.IsGameGun(g)) go.AddComponent<RarityGem>().Rarity = r; // its own look; the gem says what it is
                    else Dress(go, item, g, r, pistol);
                    network.AddObject(go.GetComponent<NetworkObject>(), checkForDuplicates: true);
                    names[go.name.ToLower()] = item;
                    byId[id] = item;
                    all[id] = item;
                    Prefabs[(g, r)] = item;
                }
            Ready = true;
            BuildSystem.Log.LogInfo($"Fortress weapons registered as items {FirstId}-{IdOf(Gear.LobsterClaw, Rarity.Legendary)} and {SecondId}-{IdOf(Gear.Bow, Rarity.Legendary)}, {ThirdId}-{IdOf(Kinds.Last(), Rarity.Legendary)} (network prefabs up to {network.GetObjectCount() - 1}).");
            return true;
        }

        // Our model instead of the pistol's (the pistol's colliders stay: they are what you pick it up by). The pistol is an
        // animated hand-and-gun rig: our model rides its gun bone (so the game's hands hold it and it moves with them), its grip
        // in the right hand, about the pistol's size (the models are built half a metre long: at full size the back end
        // reaches the camera).
        private static void Dress(GameObject go, Item item, Gear g, Rarity r, Item pistol)
        {
            foreach (var rend in go.GetComponentsInChildren<Renderer>(true)) rend.enabled = false;
            var hand = item.HandModelRight;
            var model = Models.Attach(Weapons.ModelName(g), hand && hand.parent ? hand.parent : go.transform, new Vector3(0.12f, 0.12f, 0.45f));
            model.name = ModelName;
            RarityLook(model, r);
            Place(model.transform, hand, go.transform, InHand);
            if (g == Gear.Gatling) model.transform.localScale *= 1.5f; // Owen: "加特林好小把，我以為會很大"
            Icon(item, model, pistol, g);
            if (g == Gear.Gatling) Gatling(model);
            go.AddComponent<RarityGem>().Rarity = r;
        }

        // The gatling's barrels turn (Weapons.GatlingViews): they come out of the gun's own mesh (the hotbar keeps the whole
        // gun) and go back on a spinner of their own, on their axis. In the mesh's units, from tools/make_weapon_models4.py:
        // the axis (0, AXIS_Z, 0); the barrels everything past BarrelFrom (between RECEIVER_Y and HUB_Y) within BarrelRadius
        // of it (CLUSTER_R and a little).
        public const string GatlingSpin = "GatlingSpin";
        private static readonly Vector3 GatlingAxis = new Vector3(0f, 0.11f, 0f);
        private const float BarrelFrom = 0.1f, BarrelRadius = 0.056f;
        public static bool GatlingBarrel(Vector3 p) => p.z > BarrelFrom && new Vector2(p.x, p.y - GatlingAxis.y).sqrMagnitude < BarrelRadius * BarrelRadius;
        private static void Gatling(GameObject model)
        {
            var mf = model.GetComponent<MeshFilter>();
            var whole = mf ? mf.sharedMesh : null;
            if (!whole) return;
            var v = whole.vertices;
            bool Barrel(int i) => GatlingBarrel(v[i]);
            var body = Object.Instantiate(whole);
            for (int s = 0; s < whole.subMeshCount; s++)
            {
                var t = whole.GetTriangles(s);
                var keep = new List<int>(t.Length);
                for (int i = 0; i < t.Length; i += 3)
                    if (!(Barrel(t[i]) && Barrel(t[i + 1]) && Barrel(t[i + 2]))) { keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]); }
                body.SetTriangles(keep, s);
            }
            body.bounds = whole.bounds; // its front is still the muzzle (Weapons.MyMuzzle)
            mf.sharedMesh = body;
            int n = 0; // the rarity's bands (RarityLook) round the receiver, not standing still across the turning barrels
            foreach (Transform t in model.transform) if (t.name == "RarityBand") t.localPosition = new Vector3(0f, GatlingAxis.y, -0.02f - 0.07f * n++);
            var barrels = Models.Attach("GatlingBarrels", model.transform, Vector3.zero);
            barrels.name = GatlingSpin;
            barrels.transform.localPosition = GatlingAxis;
        }

        // Each rarity looks its part: its glowing parts take the rarity's colour (blue, purple, gold), rare and up get bands
        // of that colour round the body (one, then two), and a legendary is gilded, with a little crest on top. The hotbar
        // icon uses the same materials, so it shows there too.
        private static void RarityLook(GameObject model, Rarity r)
        {
            if (r == Rarity.Common) return;
            Color c = GearCatalog.Color(r);
            var rend = model.GetComponent<Renderer>();
            var mf = model.GetComponent<MeshFilter>();
            if (!rend || !mf || !mf.sharedMesh) return;
            var gold = new Color(1f, 0.78f, 0.3f);
            var cols = Models.ColorsOf(rend);
            var mats = rend.sharedMaterials;
            for (int i = 0; i < mats.Length && i < cols.Length; i++)
            {
                bool glows = mats[i] && mats[i].IsKeywordEnabled("_EMISSION");
                Color to = glows ? c : r == Rarity.Legendary && cols[i].maxColorComponent > 0.25f ? Color.Lerp(cols[i], gold, 0.4f) : cols[i];
                if (to != cols[i]) Models.Recolor(rend, i, to);
            }
            var b = mf.sharedMesh.bounds;
            var band = Models.Make(c, c);
            model.AddComponent<Models.OwnMaterials>().Mats.Add(band); // goes with the template; copies share it
            float across = Mathf.Min(b.size.x, b.size.y) * 0.95f; // round the body, not the whole silhouette
            foreach (float t in r >= Rarity.Epic ? new[] { 0.32f, 0.58f } : new[] { 0.45f })
            {
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(ring.GetComponent<Collider>());
                ring.name = "RarityBand";
                ring.transform.SetParent(model.transform, false);
                ring.transform.localPosition = new Vector3(b.center.x, b.center.y, b.min.z + b.size.z * t);
                ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // a flat disc across the barrel: only its rim shows
                ring.transform.localScale = new Vector3(across, b.size.z * 0.025f, across);
                ring.GetComponent<Renderer>().sharedMaterial = band;
            }
            if (r == Rarity.Legendary)
            {
                var crest = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(crest.GetComponent<Collider>());
                crest.name = "RarityCrest";
                crest.transform.SetParent(model.transform, false);
                crest.transform.localPosition = new Vector3(b.center.x, b.max.y + b.size.y * 0.08f, b.center.z);
                crest.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
                crest.transform.localScale = Vector3.one * Mathf.Min(b.size.x, b.size.y) * 0.3f;
                crest.GetComponent<Renderer>().sharedMaterial = band;
            }
        }

        public const string ModelName = "FortressModel";
        public static Transform ModelOf(Item item) => item ? item.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == ModelName) : null;

        // How a model sits in the hand: its longest side (metres), and where its grip is from the hand target (item axes:
        // x right, y up, z the barrel). Calibrated by eye against the game's pistol (auto-test "weapons": held_* shots).
        public struct Grip { public float Longest; public Vector3 FromHand; }
        private const float Across = 0.15f;
        public static readonly Grip InHand = new Grip { Longest = 0.22f, FromHand = new Vector3(-0.03f, 0.03f, 0.05f) };

        public static void Place(Transform model, Transform hand, Transform item, Grip grip)
        {
            var mesh = model.GetComponent<MeshFilter>() is MeshFilter f ? f.sharedMesh : null;
            var size = mesh ? mesh.bounds.size : new Vector3(0.12f, 0.12f, 0.45f);
            // The whistle stays its size; nothing round is fatter than a fist, or a ball (the puffer) fills the view.
            float k = Mathf.Min(1f, Mathf.Min(grip.Longest / Mathf.Max(0.01f, Mathf.Max(size.x, Mathf.Max(size.y, size.z))),
                                              Across / Mathf.Max(0.01f, Mathf.Min(size.x, size.y))));
            var s = model.parent.lossyScale;
            model.localScale = new Vector3(k / s.x, k / s.y, k / s.z);
            model.rotation = item.rotation; // the item's forward is the barrel's
            model.position = (hand ? hand.position : item.position) + item.rotation * grip.FromHand;
        }

        // The hotbar shows an item's one mesh (Item.Mesh), placed by the item's own numbers: ours, framed like the pistol's.
        private static void Icon(Item item, GameObject model, Item pistol, Gear g)
        {
            var ours = model.GetComponent<MeshFilter>().sharedMesh;
            var theirs = pistol.Mesh;
            AccessTools.Field(typeof(Item), "_mesh").SetValue(item, ours);
            if (!ours || !theirs) return;
            // The pistol's mesh may sit turned inside the pistol; ours sits square (its forward is the item's forward).
            var shown = pistol.GetComponentsInChildren<Renderer>(true).FirstOrDefault(rd =>
                rd is SkinnedMeshRenderer sk ? sk.sharedMesh == theirs : rd.GetComponent<MeshFilter>() is MeshFilter f && f.sharedMesh == theirs);
            Quaternion inPistol = shown ? Quaternion.Inverse(pistol.transform.rotation) * shown.transform.rotation : Quaternion.identity;
            // The slot looks at the pistol's side; the boomerang lies flat, so it's stood up to show its V, not its edge.
            Quaternion rp = Quaternion.Euler(pistol.InventoryMeshRot), ro = rp * Quaternion.Inverse(inPistol) * (g == Gear.Boomerang ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity);
            float Big(Bounds b) => Mathf.Max(0.001f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
            float kp = pistol.InventoryMeshScale, ko = kp * Big(theirs.bounds) / Big(ours.bounds);
            Vector3 middle = pistol.InventoryMeshPos + rp * (theirs.bounds.center * kp);
            AccessTools.Field(typeof(Item), "_inventoryMeshPos").SetValue(item, middle - ro * (ours.bounds.center * ko));
            AccessTools.Field(typeof(Item), "_inventoryMeshRot").SetValue(item, ro.eulerAngles);
            AccessTools.Field(typeof(Item), "_inventoryMeshScale").SetValue(item, ko);
        }

        // Its colours for the hotbar (the slot has one material of its own).
        public static Material[] Colours(Item item) =>
            Is(item, out var g, out var r) && ModelOf(Prefab(g, r)) is Transform m && m.GetComponent<Renderer>() is Renderer rd ? rd.sharedMaterials : null;

        // Auto-test: what the pistol we build on looks like inside.
        public static string DescribeBase()
        {
            var pistol = GameInfo.GetSpawnable("pistol");
            if (!pistol) return "no pistol";
            var sb = new System.Text.StringBuilder($"pistol scale {pistol.transform.localScale}, mesh {(pistol.Mesh ? $"{pistol.Mesh.name} {pistol.Mesh.bounds.center}/{pistol.Mesh.bounds.size}" : "-")}, slot pos {pistol.InventoryMeshPos} rot {pistol.InventoryMeshRot} scale {pistol.InventoryMeshScale}, hand {(pistol.HandModelRight ? pistol.HandModelRight.name : "-")}");
            foreach (var t in pistol.GetComponentsInChildren<Transform>(true))
                sb.Append($"\n    {t.name}: pos {t.localPosition:F3} rot {t.localEulerAngles:F0} scale {t.localScale:F3} layer {t.gameObject.layer}{(t.GetComponent<Renderer>() is Renderer rd ? $" [{rd.GetType().Name}]" : "")}");
            return sb.ToString();
        }

        // Host: put a weapon in the world (a drop, a chest, a starter).
        public static Item ServerSpawn(Gear g, Rarity r, Vector3 at, int stars = 0)
        {
            var prefab = Prefab(g, r);
            if (!prefab || !ItemManager.Instance) return null;
            var item = ItemManager.Instance.SpawnNewItem(prefab, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            if (item && item.GetComponent<FortressWeaponItem>() is FortressWeaponItem tag) tag.Stars = Mathf.Clamp(stars, 0, GearCatalog.MaxStars);
            AutoTest.Count("weapon_item_spawned");
            return item;
        }
        public static int StarsOf(Item item) => item && item.GetComponent<FortressWeaponItem>() is FortressWeaponItem tag ? tag.Stars : 0;

        // The fortress weapon this player holds right now (the host knows everyone's held item).
        public static bool Held(Player p, out Gear g, out Rarity r)
        {
            var held = p && p.Holding ? p.Holding.HeldItem : null;
            return Is(held, out g, out r);
        }

        public static bool Carries(Player p) =>
            p && p.Inventory && (Is(p.Holding ? p.Holding.HeldItem : null, out _, out _) || p.Inventory._items.Values.Any(i => i && i.GetComponent<FortressWeaponItem>()));
    }

    // Registered the moment the game has built its item list.
    [HarmonyPatch(typeof(GameInfo), "Awake")]
    internal static class RegisterWeaponItems
    {
        private static void Postfix() => WeaponItems.TryRegister();
    }

    // Its name: "[Rare] Laser Gun" instead of the pistol's.
    [HarmonyPatch(typeof(Item), nameof(Item.GetName))]
    internal static class WeaponItemName
    {
        private static void Postfix(Item __instance, ref string __result)
        {
            if (WeaponItems.Is(__instance, out var g, out var r)) __result = GearCatalog.Label(g, r);
        }
    }

    // A hotbar slot holding a fortress weapon draws it in its own colours; the slot's material comes back for anything else
    // (before the game puts that item's skin and cookness on it).
    [HarmonyPatch(typeof(InventorySlot), nameof(InventorySlot.SetItem))]
    internal static class WeaponItemSlotColours
    {
        private static readonly Dictionary<Renderer, Material[]> Plain = new Dictionary<Renderer, Material[]>();

        private static void Prefix(Item item, Renderer ____renderer)
        {
            if (____renderer && !WeaponItems.Is(item, out _, out _) && !BossTrophies.Is(item, out _) && Plain.TryGetValue(____renderer, out var mats))
            {
                ____renderer.sharedMaterials = mats;
                Plain.Remove(____renderer);
            }
        }

        private static void Postfix(Item item, Renderer ____renderer)
        {
            var ours = WeaponItems.Colours(item) ?? BossTrophies.Colours(item);
            if (!____renderer || ours == null) return;
            if (!Plain.ContainsKey(____renderer)) Plain[____renderer] = ____renderer.sharedMaterials;
            ____renderer.sharedMaterials = ours;
        }
    }

    // The pistol's own shooting, reloading and inspecting are off for fortress weapons (Weapons.cs fires them); its
    // aiming (right mouse: the gun comes up to the middle and the view zooms) stays, and carries our model with it. The
    // game's guns keep all of theirs.
    [HarmonyPatch(typeof(Weapon), nameof(Weapon.PrimaryInput))]
    internal static class WeaponItemNoShoot
    {
        internal static bool Ours(Weapon w) => w.GetComponent<FortressWeaponItem>() is FortressWeaponItem t && t && !GearCatalog.IsGameGun(t.Gear);
        private static bool Prefix(Weapon __instance) => !Ours(__instance);
    }

    [HarmonyPatch(typeof(Weapon), nameof(Weapon.ReloadInput))]
    internal static class WeaponItemNoReload { private static bool Prefix(Weapon __instance) => !WeaponItemNoShoot.Ours(__instance); }

    [HarmonyPatch(typeof(Weapon), nameof(Weapon.InspectInput))]
    internal static class WeaponItemNoInspect { private static bool Prefix(Weapon __instance) => !WeaponItemNoShoot.Ours(__instance); }

    // A rare-or-better piece lying on the ground has a gem of its rarity's colour floating over it: turning, bobbing and
    // glowing (the game's bloom makes it a bright point from across the island; it grows a little with distance so it stays
    // seen). Weapon items carry one (it follows the item but is its own object in the world: a child would be skewed by the
    // item's tumbling and the gun bone's scale), and so do loot drops (Loot.MakeDropView).
    public class RarityGem : MonoBehaviour
    {
        public Rarity Rarity;
        public float Height = 0.45f; // over the piece
        private Item _item;
        private Transform _gem;
        private float _seed;

        private void Start()
        {
            if (Rarity < Rarity.Rare) { enabled = false; return; }
            _item = GetComponent<Item>();
            _seed = Random.value * 10f;
            _gem = new GameObject("RarityGem").transform;
            // A closed, hand-built octahedron instead of the old LootGem OBJ: that mesh had half its faces wound inward,
            // so from most angles it looked like a broken half-gem (Owen's screenshots).
            var mesh = new Mesh { name = "RarityGemMesh" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0.5f, 0f), new Vector3(0.3f, 0.08f, 0f), new Vector3(0.15f, 0.08f, -0.26f),
                new Vector3(-0.15f, 0.08f, -0.26f), new Vector3(-0.3f, 0.08f, 0f), new Vector3(-0.15f, 0.08f, 0.26f),
                new Vector3(0.15f, 0.08f, 0.26f), new Vector3(0f, -0.5f, 0f)
            };
            mesh.triangles = new[]
            {
                0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5, 0, 5, 6, 0, 6, 1,
                7, 2, 1, 7, 3, 2, 7, 4, 3, 7, 5, 4, 7, 6, 5, 7, 1, 6
            };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var gemGo = new GameObject("GemMesh");
            gemGo.transform.SetParent(_gem, false);
            gemGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            var gemMat = Models.MakeShared(GearCatalog.Color(Rarity), GearCatalog.Color(Rarity) * 0.8f);
            var mr = gemGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = gemMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _gem.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!_gem) return;
            // A weapon only while it lies there: nobody holds it, it isn't in a hotbar.
            bool show = !Reveal.Playing && (!_item || (_item.IsSpawned && !_item.Holder && !_item.SyncedHolder && !_item.IsInInventory && !_item.IsDestroying));
            if (_gem.gameObject.activeSelf != show) _gem.gameObject.SetActive(show);
            if (!show) return;
            float t = Time.time + _seed;
            var cam = GameInfo.CurCamera;
            float far = cam ? Mathf.Clamp((cam.transform.position - transform.position).magnitude / 10f, 1f, 3f) : 1f;
            // Anchor to the item's renderer, not a fixed world height: a loot that is high up or moves keeps its glow at
            // the item, and a small thing on the ground doesn't get a light floating in the sky.
            float baseY = transform.position.y;
            var body = GetComponentInChildren<Renderer>();
            if (body) baseY = body.bounds.max.y;
            float scale = (0.1f + 0.04f * (int)Rarity) * far; // rare 0.14 m, epic 0.18, legendary 0.22
            _gem.localScale = Vector3.one * scale;
            // Clear the item's whole bounds plus the gem's own half-height: a gem tucked into a barrel or the ground reads
            // as a broken half-gem (and is what the playtest saw every time near a weapon).
            float half = 0.5f * scale;
            _gem.position = new Vector3(transform.position.x, baseY + half + 0.07f + 0.06f * Mathf.Sin(t * 2.2f), transform.position.z);
            _gem.rotation = Quaternion.Euler(0f, t * 90f, 0f);
        }

        // A pooled (despawned) item is only switched off, and then LateUpdate no longer runs to hide the gem.
        private void OnDisable() { if (_gem) _gem.gameObject.SetActive(false); }
        private void OnDestroy() { if (_gem) Destroy(_gem.gameObject); }
    }
}
