using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Managing.Object;
using FishNet.Object;
using HarmonyLib;
using UnityEngine;

namespace HtfFortress
{
    // Marks a boss trophy item (on the prefab, so every copy in the world has it).
    public class BossTrophyItem : MonoBehaviour { public EnemyKind Boss; }

    // What a boss leaves behind: its signature part (the warden's claw, the urchin's spike, the lantern... the Eye's eye),
    // a real item like the fortress weapons (picked up with E, carried, kept in the vault, saved with the world), sold at
    // the fish stall's counter (or any of the game's shops) for a small fortune. Built at start-up from the game's own spider crab
    // shell (a plain trophy item) with our model on it, registered as items 180-189 in boss order on every machine (the
    // game's own run 0-102, the fortress weapons 200-255).
    public static class BossTrophies
    {
        public const byte FirstId = 180;
        public static bool Ready { get; private set; }
        private static readonly Dictionary<EnemyKind, Item> Prefabs = new Dictionary<EnemyKind, Item>();

        public static byte IdOf(EnemyKind k) => (byte)(FirstId + System.Array.IndexOf(EnemyCatalog.BossOrder, k));
        public static bool IsItemId(byte id) => id >= FirstId && id < FirstId + EnemyCatalog.BossOrder.Length; // a legendary trophy
        // What it sells for (dollars): twice the boss's kill reward, $2,400 for the warden's claw up to $20,000 for the Eye.
        // What one sells for: the first boss's (wave 10) as much as the main game's last boss ($15,000), each one after 40% more,
        // the tenth's (wave 100) $310,000; in fort coins, so they move with its prices.
        public static int Worth(EnemyKind k) => Siege.Money(Mathf.RoundToInt(500f * Mathf.Pow(1.4f, System.Array.IndexOf(EnemyCatalog.BossOrder, k))));
        public static string ModelOf(EnemyKind k) => "Trophy" + k;
        public static bool Is(Item item, out EnemyKind k)
        {
            var tag = item ? item.GetComponent<BossTrophyItem>() : null;
            k = tag ? tag.Boss : default;
            return tag;
        }

        public static bool TryRegister()
        {
            if (Ready) return true;
            var shell = GameInfo.GetSpawnable("spidercrabshell");
            var nm = InstanceFinder.NetworkManager;
            if (!shell || !nm || !(nm.SpawnablePrefabs is SinglePrefabObjects network)) return false;
            var names = (Dictionary<string, Item>)AccessTools.Field(typeof(GameInfo), "_nameToSpawnable").GetValue(null);
            var byId = (Dictionary<byte, Item>)AccessTools.Field(typeof(GameInfo), "_idToSpawnable").GetValue(null);
            var all = (Dictionary<byte, Item>)AccessTools.Field(typeof(GameInfo), "_allItems").GetValue(null);
            if (EnemyCatalog.BossOrder.Any(k => byId.ContainsKey(IdOf(k)) && !Is(byId[IdOf(k)], out _)))
            {
                BuildSystem.Log.LogError("Item ids 180-189 are taken by the game: boss trophies can't be items.");
                Ready = true;
                return true;
            }
            var holder = new GameObject("FortressBossTrophies");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            foreach (var k in EnemyCatalog.BossOrder)
            {
                var go = Object.Instantiate(shell.gameObject, holder.transform);
                go.name = "FortressTrophy" + k;
                var item = go.GetComponent<Item>();
                AccessTools.Field(typeof(Item), "_id").SetValue(item, IdOf(k));
                AccessTools.Field(typeof(Item), "_worth").SetValue(item, Worth(k));
                // The shell is one of the game's quest items: a trophy isn't (so it's saved, stored, sold, hooked like anything).
                AccessTools.Property(typeof(Item), nameof(Item.IsQuestItem)).SetValue(item, false);
                go.AddComponent<BossTrophyItem>().Boss = k;
                Dress(go, item, k, shell);
                network.AddObject(go.GetComponent<NetworkObject>(), checkForDuplicates: true);
                names[go.name.ToLower()] = item;
                byId[IdOf(k)] = item;
                all[IdOf(k)] = item;
                Prefabs[k] = item;
            }
            Ready = true;
            BuildSystem.Log.LogInfo($"Boss trophies registered as items {FirstId}-{FirstId + EnemyCatalog.BossOrder.Length - 1}.");
            return true;
        }

        // Our model instead of the shell's, held by a box round it (what you pick it up by), and a gold gem over it on the ground.
        private static void Dress(GameObject go, Item item, EnemyKind k, Item shell)
        {
            var old = go.GetComponentsInChildren<Collider>(true).FirstOrDefault(c => !c.isTrigger);
            int layer = old ? old.gameObject.layer : go.layer;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) c.enabled = false;
            var model = Models.Attach(ModelOf(k), go.transform, Vector3.one * 0.4f);
            model.name = WeaponItems.ModelName;
            var mesh = model.GetComponent<MeshFilter>() is MeshFilter f ? f.sharedMesh : null;
            float size = mesh ? Mathf.Max(mesh.bounds.size.x, Mathf.Max(mesh.bounds.size.y, mesh.bounds.size.z)) : 0.5f;
            float s = 0.8f / Mathf.Max(0.1f, size); // about 0.8 m long: big enough to see from across the beach
            var ls = go.transform.lossyScale;
            model.transform.localScale = new Vector3(s / ls.x, s / ls.y, s / ls.z);
            model.transform.localPosition = mesh ? -Vector3.Scale(mesh.bounds.center, model.transform.localScale) : Vector3.zero;
            var box = go.AddComponent<BoxCollider>();
            box.size = mesh ? Vector3.Scale(mesh.bounds.size, model.transform.localScale) : Vector3.one * 0.5f;
            box.center = Vector3.zero;
            if (layer != go.layer) { var hold = new GameObject("TrophyCollider") { layer = layer }; hold.transform.SetParent(go.transform, false); var b2 = hold.AddComponent<BoxCollider>(); b2.size = box.size; Object.Destroy(box); }
            AccessTools.Field(typeof(Item), "_mesh").SetValue(item, mesh);
            if (mesh && shell.Mesh)
            {
                float Big(Bounds b) => Mathf.Max(0.001f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
                float ko = shell.InventoryMeshScale * Big(shell.Mesh.bounds) / Big(mesh.bounds);
                Quaternion ro = Quaternion.Euler(shell.InventoryMeshRot);
                Vector3 middle = shell.InventoryMeshPos + ro * (shell.Mesh.bounds.center * shell.InventoryMeshScale);
                AccessTools.Field(typeof(Item), "_inventoryMeshPos").SetValue(item, middle - ro * (mesh.bounds.center * ko));
                AccessTools.Field(typeof(Item), "_inventoryMeshScale").SetValue(item, ko);
            }
            go.AddComponent<RarityGem>().Rarity = Rarity.Legendary;
        }

        public static Material[] Colours(Item item) =>
            Is(item, out var k) && Prefabs.TryGetValue(k, out var p) && WeaponItems.ModelOf(p) is Transform m && m.GetComponent<Renderer>() is Renderer rd ? rd.sharedMaterials : null;

        // Host: a trophy in the world at `at` (it drops from there).
        public static Item ServerSpawn(EnemyKind k, Vector3 at)
        {
            if (!Prefabs.TryGetValue(k, out var prefab) || !prefab || !ItemManager.Instance) return null;
            var item = ItemManager.Instance.SpawnNewItem(prefab, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            AutoTest.Count("trophy_spawned");
            return item;
        }

        // Host: the boss is dead: its trophy falls where it went down (on the island: a sea or sky boss's lands on the shore
        // nearest it), in a burst of gold, and everyone's told.
        public static void ServerDrop(EnemyKind k, Vector3 died, int wave)
        {
            var island = FortressIsland.Instance;
            Vector3 at = died;
            if (island && island.Exists)
            {
                Vector3 c = island.Center, d = (died - c).Flat();
                at = c + (d.magnitude > 1f ? d.normalized : Vector3.forward) * Mathf.Min(d.magnitude, 20f);
            }
            at.y = BossFx.Ground(at) + 2.5f;
            var item = ServerSpawn(k, at);
            if (Loot.Instance) Loot.Instance.ServerDropScales(at, Loot.BossScales(wave), 8, 3.2f); // and a ring of fish scales round it
            BossFx.Send("trophy", null, at, at, 0, (int)k);
            Fx.Announce("trophyDropped" + k);
            if (item) AutoTest.Count("trophy_dropped_" + k);
        }
    }

    // Registered with the weapons, the moment the game has built its item list.
    [HarmonyPatch(typeof(GameInfo), "Awake")]
    internal static class RegisterBossTrophies
    {
        private static void Postfix() => BossTrophies.TryRegister();
    }

    // Its name: "Iron Claw of the Warden".
    [HarmonyPatch(typeof(Item), nameof(Item.GetName))]
    internal static class BossTrophyName
    {
        private static void Postfix(Item __instance, ref string __result)
        {
            if (BossTrophies.Is(__instance, out var k)) __result = L10n.T("trophy" + k);
        }
    }
}
