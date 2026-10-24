using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HtfFortress
{
    // Each player's own upgrades to each kind of weapon, bought at the smithy with fish scales (the playtest: everyone trains
    // their own guns). Four lines a kind, kept for good and counted for every weapon of that kind they hold, any rarity, with
    // no top level (Owen: "可以都升級到很誇張，只是指數價格上升"): each level costs more than the last (Cost), and what a
    // level adds tapers off wherever more would break the game (Soft, TimeMult, ReachMult). Fortress weapons: power (its
    // number), fire rate (the time between shots; the laser and the water cannon pour out faster: Owen "每把槍改造都要多一
    // 個射速"), range (how far it reaches: "還有射距") and a trait of its own. The game's guns: bullets (the game's own
    // bullet upgrades, then 10% a level past its last one), fire rate (the bow: how fast it draws), range (straighter,
    // faster shots) and magazine & reload (not the bow: it holds one arrow). Weapons used to have the team's legendary
    // enhancement; VaultData.Read turns that into everyone's power, and moves the range traits of before (boomerang, conch,
    // water cannon, absorber) onto the range line.
    public static class WeaponMods
    {
        public const int Tracks = 4;
        public const int Power = 0, Rate = 1, Trait = 2, Reach = 3; // fortress weapons
        public const int Bullets = 0, FireRate = 1, Magazine = 2;  // the game's guns (their range is Reach too)
        public static readonly int[] Shown = { 0, 1, 3, 2 };       // the smithy's order: power, fire rate, range, trait

        // Fish scales for the next level up from `lvl`: 8, 15, 25, 40, 60 as before, then 22% dearer a level (a line to Lv10
        // ~700 in all, Lv20 ~6,400, Lv30 ~48,000; Sim prints what a run brings in). Past int it can't be bought.
        private static readonly int[] FirstCosts = { 8, 15, 25, 40, 60 };
        public const double CostGrowth = 1.12;
        public static int Cost(int lvl) => lvl < 0 ? 0 : lvl < FirstCosts.Length ? FirstCosts[lvl]
            : (int)System.Math.Min(int.MaxValue, System.Math.Round(FirstCosts[FirstCosts.Length - 1] * System.Math.Pow(CostGrowth, lvl - FirstCosts.Length + 1)));

        public static bool Has(Gear g, int track) => GearCatalog.IsWeapon(g) && track >= 0 && track < Tracks && !(g == Gear.Bow && track == Magazine);

        // Levels as an effect that mustn't run away counts them: one each to Lv5, then fewer and fewer, never past 15.
        public static float Soft(int lvl) => lvl <= 5 ? Mathf.Max(0, lvl) : 5f + 10f * (lvl - 5) / (lvl + 5f);
        // A weapon whose number is a hold, a pull or a push (seconds, metres), not damage.
        public static bool IsControl(Gear g) => g == Gear.BubbleGun || g == Gear.Whirlpool || g == Gear.Conch || g == Gear.NetLauncher || g == Gear.WaterCannon;
        // Power: damage +10% a level for good; a control weapon's seconds or metres the same to Lv5, then tapering (x2.5 at most).
        public static float PowerMult(int lvl, Gear g = Gear.Laser) => 1f + 0.1f * (IsControl(g) ? Soft(lvl) : Mathf.Max(0, lvl));
        // Fire rate: the time between shots (and reloading, the bow's draw): -13% at Lv1, -40% at Lv5, never under a fifth.
        public static float TimeMult(int lvl) => 0.2f + 0.8f / (1f + 0.2f * Mathf.Max(0, lvl));
        // Range: +9% at Lv1, +33% at Lv5, +50% at Lv10, never twice as far.
        public static float ReachMult(int lvl) => 1f + Mathf.Max(0, lvl) / (Mathf.Max(0, lvl) + 10f);
        public const float BulletStep = 0.1f;                         // a game gun's damage a level past its last bullet upgrade
        public static int MagAdd(int mag, int lvl) => Mathf.RoundToInt(Mathf.Max(1, Mathf.RoundToInt(mag * 0.2f)) * Soft(lvl));

        // A fortress weapon's trait, per level (Soft levels).
        public static float TraitStep(Gear g)
        {
            switch (g)
            {
                case Gear.Laser: return 0.2f;       // the most it ramps up to on one fish (x3)
                case Gear.PoisonGas: return 1.2f;   // seconds the cloud hangs (6)
                case Gear.Boomerang: return 0.3f;   // seconds a fish it hits is slowed
                case Gear.SharkWhistle: return 3f;  // seconds the shark hunts
                case Gear.BubbleGun: return 3f;     // the burst when a bubble pops (a legendary's 20 and this; the others only this)
                case Gear.Hook: return 0.3f;        // seconds the fish is stunned after the pull
                case Gear.Whirlpool: return 4f;     // damage a second to what it holds (5)
                case Gear.Conch: return 3f;         // degrees wider either side (30)
                case Gear.NetLauncher: return 0.5f; // the net's radius (3.5)
                case Gear.JellyGun: return 1.2f;    // seconds the jellyfish hangs (6)
                case Gear.WaterCannon: return 0.5f; // seconds more in the tank
                case Gear.PufferBomb: return 0.5f;  // the burst's radius (3.5)
                case Gear.StarThrower: return 1f;   // more fish each star cuts through (2)
                case Gear.LobsterClaw: return 0.5f; // the splash's radius where the thrown fish lands (2.5)
                case Gear.Rocket: return 0.4f;      // the blast's radius (3)
                case Gear.Absorber: return 3f;      // degrees wider it sucks in (30)
                case Gear.Gatling: return 0.1f;     // seconds sooner its barrels are spun up (2.0)
            }
            return 0f;
        }
        public static float TraitAdd(Gear g, int lvl) => TraitStep(g) * Soft(lvl);
        // What the trait's number counts: seconds, metres, damage, times, fish, degrees.
        private static string TraitUnit(Gear g) =>
            g == Gear.Laser ? "modUnitTimes" : g == Gear.BubbleGun || g == Gear.Whirlpool ? "modUnitDamage" : g == Gear.StarThrower ? "modUnitFish"
          : g == Gear.Conch || g == Gear.Absorber ? "modUnitDegrees" : g == Gear.Gatling ? "modUnitSecondsLess"
          : g == Gear.PoisonGas || g == Gear.SharkWhistle || g == Gear.Hook || g == Gear.JellyGun || g == Gear.Boomerang || g == Gear.WaterCannon ? "modUnitSeconds" : "modUnitMetres";
        // The laser and the water cannon pour out rather than shoot, gas and jellyfish linger and sting: their fire rate is
        // more a second (Weapons.FireAs, LaserTick, WaterTick).
        public static bool Pours(Gear g) => g == Gear.Laser || g == Gear.WaterCannon || Lingers(g);
        public static bool Lingers(Gear g) => g == Gear.PoisonGas || g == Gear.JellyGun;
        private static bool Lobs(Gear g) => g == Gear.PoisonGas || g == Gear.Whirlpool || g == Gear.NetLauncher || g == Gear.JellyGun || g == Gear.PufferBomb;

        // ---------- what the smithy shows ----------

        public static string TrackName(Gear g, int t) => L10n.T(t == Reach ? "modRange" : GearCatalog.IsGameGun(g) ? new[] { "modBullets", "modFireRate", "modMagazine" }[t] : new[] { "modPower", "modFireRate", "modTrait" }[t]);

        // What a line does, in a few words.
        public static string TrackWhat(Gear g, int t)
        {
            if (t == Reach) return L10n.T(GearCatalog.IsGameGun(g) ? "modRangeWhatGun" : g == Gear.SharkWhistle ? "modRangeWhatShark" : g == Gear.Absorber ? "modRangeWhatAbsorber" : Lobs(g) ? "modRangeWhatLob" : "modRangeWhat");
            if (GearCatalog.IsGameGun(g)) return L10n.T(t == Bullets ? "modBulletsWhat" : t == FireRate ? (g == Gear.Bow ? "modDrawWhat" : "modFireRateWhat") : "modMagazineWhat");
            if (t == Power) return L10n.T("modPowerWhat");
            if (t == Rate) return L10n.T(g == Gear.Laser ? "modRateWhatLaser" : g == Gear.WaterCannon ? "modRateWhatWater" : Lingers(g) ? "modRateWhatLinger" : "modFireRateWhat");
            return L10n.T("modTrait" + g);
        }

        // A line's number at a level.
        public static string TrackValue(Gear g, int t, int lvl)
        {
            int pct(float f) => Mathf.RoundToInt(f * 100f);
            string less = L10n.T(g == Gear.Bow ? "modDrawValue" : "modRateValue", pct(1f - TimeMult(lvl)));
            if (t == Reach) return GearCatalog.IsGameGun(g) ? L10n.T("modRangeGunValue", pct(1f - 1f / ReachMult(lvl)), pct(ReachMult(lvl) - 1f))
                                                            : L10n.T("modRangeValue", (Weapons.RangeOf(g) * ReachMult(lvl)).ToString("0.#"));
            if (GearCatalog.IsGameGun(g))
            {
                var att = PrefabAttachments(g);
                if (t == Bullets) return L10n.T("modBulletsValue", att ? BulletDamage(att, att.AmmoType, lvl) : 0);
                if (t == FireRate) return less;
                int mag = att ? DefaultMag(att) : 0;
                return L10n.T("modMagazineValue", mag + MagAdd(mag, lvl), pct(1f - TimeMult(lvl)));
            }
            if (t == Power) return "+" + pct(PowerMult(lvl, g) - 1f) + "%";
            if (t == Rate) return Pours(g) ? L10n.T("modRatePourValue", pct(1f / TimeMult(lvl) - 1f)) : less;
            return L10n.T(TraitUnit(g), TraitAdd(g, lvl).ToString("0.#"));
        }

        // "Power Lv2 · Fire rate Lv1" (the lines above 0), for cards.
        public static string Summary(VaultData v, ulong player, Gear g)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (int t in Shown) if (Has(g, t) && v.ModOf(player, g, t) > 0) parts.Add($"{TrackName(g, t)} Lv{v.ModOf(player, g, t)}");
            return string.Join(" · ", parts);
        }

        // ---------- the game's guns ----------

        private static readonly AccessTools.FieldRef<Attachments, BulletUpgrade[]> Tiers = AccessTools.FieldRefAccess<Attachments, BulletUpgrade[]>("_bulletUpgrades");
        private static readonly AccessTools.FieldRef<Attachments, int> DefaultMagRef = AccessTools.FieldRefAccess<Attachments, int>("_defaultAmmoPerMag");
        private static readonly AccessTools.FieldRef<Attachments, int> ExtendedMagRef = AccessTools.FieldRefAccess<Attachments, int>("_extendedAmmoPerMag");
        private static System.Reflection.MethodInfo _setAmmo; // looked up on first use, not at load (the headless tests can't)
        internal static readonly AccessTools.FieldRef<Weapon, float> Interval = AccessTools.FieldRefAccess<Weapon, float>("_timeBetweenShots");
        internal static readonly AccessTools.FieldRef<Weapon, float> Spread = AccessTools.FieldRefAccess<Weapon, float>("_spread");
        internal static readonly AccessTools.FieldRef<Weapon, float> ShotSpeed = AccessTools.FieldRefAccess<Weapon, float>("_projSpeed");
        internal static readonly AccessTools.FieldRef<Weapon, bool> AnimGated = AccessTools.FieldRefAccess<Weapon, bool>("_noShootingDuringShootAnim");
        internal static readonly AccessTools.FieldRef<Weapon, string> ReloadAnim = AccessTools.FieldRefAccess<Weapon, string>("_activeReloadAnimName");
        internal static readonly AccessTools.FieldRef<Tool, Animation> Anim = AccessTools.FieldRefAccess<Tool, Animation>("_anim");

        public static int DefaultMag(Attachments a) => DefaultMagRef(a);
        public static Attachments PrefabAttachments(Gear g) => WeaponItems.Prefab(g, Rarity.Common) is Item p && p ? p.GetComponentInChildren<Attachments>(true) : null;

        // One shot's damage (the shotgun's: one pellet's) with bullets at `lvl`: up the game's own bullet upgrades first (or
        // from the one the gun already has, bought at the game's gun shop), then 10% a level past the last.
        public static int BulletDamage(Attachments a, int have, int lvl)
        {
            var tiers = Tiers(a);
            if (tiers == null || tiers.Length == 0) return 0;
            int last = tiers.Length - 1, i = Mathf.Clamp(Mathf.Max(have, lvl), 0, last);
            return Mathf.RoundToInt(tiers[i].Damage * (1f + BulletStep * Mathf.Max(0, lvl - last)));
        }

        // The holder's level on a line for one of our game guns (-1: not one, or nobody holds it). The vault every machine
        // has: the holder's magazine and reload come out the same on everyone's screen.
        internal static int Level(Weapon w, int track, out Gear g)
        {
            g = default;
            if (!w || !w.Holder || !Loot.Instance || !WeaponItems.Is(w, out g, out _) || !GearCatalog.IsGameGun(g)) return -1;
            return Loot.Instance.ClientVault.ModOf(Loot.KeyOf(w.Holder), g, track);
        }

        // A gun just taken in hand still holds the magazine it was made with (the game fills it when it spawns, before anyone
        // holds it): filled to its holder's once, so a magazine mod shows straight away rather than after the first reload.
        private static readonly System.Collections.Generic.HashSet<Weapon> Filled = new System.Collections.Generic.HashSet<Weapon>();
        public static void TopUp(Weapon w)
        {
            if (!w || !w.Holder || !Filled.Add(w)) return; // the holder's mods: only once it has one
            Filled.RemoveWhere(x => !x);
            int made = w.Attachments.ExtendedMag ? ExtendedMagRef(w.Attachments) : DefaultMagRef(w.Attachments);
            if (w.Ammo == made && w.Attachments.AmmoPerMag > made)
                (_setAmmo ?? (_setAmmo = AccessTools.PropertySetter(typeof(Weapon), nameof(Weapon.Ammo)))).Invoke(w, new object[] { w.Attachments.AmmoPerMag });
        }

        // What one shot of the gun in someone's hand does before the host's rarity and wave (the HUD's number).
        public static int ShotDamage(Weapon w)
        {
            int lvl = Level(w, Bullets, out _);
            return lvl > 0 ? BulletDamage(w.Attachments, w.Attachments.AmmoType, lvl) : w.Damage;
        }
    }

    // The hotbar (1-n): each fortress weapon's slot ringed in its rarity's colour, with "+N" in the corner for your mods on
    // its kind (Owen: the quality, the upgrades and the +n should show where the weapons are too).
    public class HotbarBadges : MonoBehaviour
    {
        private static readonly AccessTools.FieldRef<PlayerInventory, List<InventorySlot>> Slots = AccessTools.FieldRefAccess<PlayerInventory, List<InventorySlot>>("_itemSlots");
        private static readonly AccessTools.FieldRef<InventorySlot, RectTransform> Frame = AccessTools.FieldRefAccess<InventorySlot, RectTransform>("_slot");
        private class Badge { public GameObject Root; public Image Ring; public TextMeshProUGUI Plus; public string Shown; }
        private readonly Dictionary<InventorySlot, Badge> _badges = new Dictionary<InventorySlot, Badge>();
        private float _next;

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.2f;
            var me = Player.LocalPlayer;
            var slots = me && me.Inventory && Hud.Font && Loot.Instance ? Slots(me.Inventory) : null;
            if (slots == null) return;
            var v = Loot.Instance.ClientVault;
            foreach (var slot in slots)
            {
                if (!slot) continue;
                Gear g = default; Rarity r = default;
                bool ours = slot.Item && WeaponItems.Is(slot.Item, out g, out r);
                int n = ours ? v.ModTotal(Loot.MyKey, g) : 0;
                string want = ours ? $"{r} {n}" : null;
                if (!_badges.TryGetValue(slot, out var b) || !b.Root) _badges[slot] = b = Make(slot);
                if (want == b.Shown) continue;
                b.Shown = want;
                b.Root.SetActive(ours);
                if (!ours) continue;
                b.Ring.color = GearCatalog.Color(r) * new Color(1f, 1f, 1f, r == Rarity.Common ? 0.45f : 0.95f);
                b.Plus.text = n > 0 ? "+" + n : "";
            }
        }

        private static Badge Make(InventorySlot slot)
        {
            var frame = Frame(slot) ? Frame(slot) : (RectTransform)slot.transform;
            var rt = new GameObject("FortressBadge", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(frame, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(3f, 3f); rt.offsetMax = new Vector2(-3f, -3f); // inside the game's own selection outline
            var ring = rt.gameObject.AddComponent<Image>();
            ring.raycastTarget = false;
            Hud.Round(ring, 10f, "Ui_outline");
            var plus = Hud.Label(rt, "", 17f, new Vector2(1f, 1f), new Vector2(-3f, -1f), new Vector2(60f, 22f), TextAlignmentOptions.TopRight, Hud.Gold, bold: true, outline: true, pivot: new Vector2(1f, 1f));
            rt.gameObject.SetActive(false); // until there's a weapon in the slot
            return new Badge { Root = rt.gameObject, Ring = ring, Plus = plus };
        }
    }

    // Bullets: the shooter's machine works out a shot's damage (the host then multiplies it by rarity and wave). The bow
    // calls this for its arrow and multiplies by the draw.
    [HarmonyPatch(typeof(Weapon), "GetShotDamage")]
    internal static class ModBullets
    {
        private static void Postfix(Weapon __instance, ref int __result)
        {
            int lvl = WeaponMods.Level(__instance, WeaponMods.Bullets, out _);
            if (lvl > 0) __result = WeaponMods.BulletDamage(__instance.Attachments, __instance.Attachments.AmmoType, lvl);
        }
    }

    // Rate of fire: the time between shots from the gun as it was made, and the shot's animation as much faster where the
    // gun waits for it (a pump, a bolt). Range: its spread narrowed and its bullets (the bow's arrows) faster, by as much.
    // Only the shooter's machine shoots.
    [HarmonyPatch(typeof(Weapon), "Shoot")]
    internal static class ModFireRate
    {
        private static void Prefix(Weapon __instance)
        {
            int lvl = WeaponMods.Level(__instance, WeaponMods.FireRate, out var g);
            if (lvl < 0 || !(WeaponItems.Prefab(g, Rarity.Common) is Weapon made)) return;
            float m = WeaponMods.ReachMult(WeaponMods.Level(__instance, WeaponMods.Reach, out _));
            WeaponMods.Spread(__instance) = WeaponMods.Spread(made) / m;
            WeaponMods.ShotSpeed(__instance) = WeaponMods.ShotSpeed(made) * m;
            if (g == Gear.Bow) return;
            float k = WeaponMods.TimeMult(lvl);
            WeaponMods.Interval(__instance) = WeaponMods.Interval(made) * k;
            var anim = WeaponMods.Anim(__instance);
            if (!anim || !WeaponMods.AnimGated(__instance)) return;
            foreach (var name in new[] { "Fire", "FireLast" }) { var st = anim[name]; if (st) st.speed = 1f / k; }
        }
    }

    // Magazine: more rounds a load.
    [HarmonyPatch(typeof(Attachments), nameof(Attachments.AmmoPerMag), MethodType.Getter)]
    internal static class ModMagazine
    {
        private static void Postfix(Attachments __instance, ref int __result)
        {
            int lvl = WeaponMods.Level(__instance.Weapon, WeaponMods.Magazine, out _);
            if (lvl > 0) __result += WeaponMods.MagAdd(__result, lvl);
        }
    }

    // Reload: the reload's animation (it refills when it's that far through) and its sounds, faster. The bow's goes with its draw.
    [HarmonyPatch(typeof(Weapon), "Reload")]
    internal static class ModReload
    {
        private static void Postfix(Weapon __instance)
        {
            int lvl = WeaponMods.Level(__instance, WeaponMods.Magazine, out var g);
            var anim = WeaponMods.Anim(__instance);
            string clip = WeaponMods.ReloadAnim(__instance);
            if (lvl < 0 || g == Gear.Bow || !anim || string.IsNullOrEmpty(clip)) return;
            var st = anim[clip];
            if (st) st.speed = 1f / WeaponMods.TimeMult(lvl);
        }
    }

    [HarmonyPatch(typeof(Weapon), "GetReloadAudioSequenceSpeedMultiplier")]
    internal static class ModReloadSound
    {
        private static void Postfix(Weapon __instance, ref float __result)
        {
            int lvl = WeaponMods.Level(__instance, WeaponMods.Magazine, out var g);
            if (lvl > 0 && g != Gear.Bow) __result /= WeaponMods.TimeMult(lvl);
        }
    }

    // The bow's rate of fire: it draws (and nocks the next arrow) faster.
    [HarmonyPatch(typeof(Attachments), nameof(Attachments.ChargeSpeed), MethodType.Getter)]
    internal static class ModDraw
    {
        private static void Postfix(Attachments __instance, ref float __result)
        {
            int lvl = WeaponMods.Level(__instance.Weapon, WeaponMods.FireRate, out var g);
            if (lvl > 0 && g == Gear.Bow) __result /= WeaponMods.TimeMult(lvl);
        }
    }
}
