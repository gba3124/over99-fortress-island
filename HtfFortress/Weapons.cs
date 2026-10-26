using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using FishNet;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Serializing;
using FishNet.Transporting;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace HtfFortress
{
    // Client -> host: where you aim your drawn fortress weapon. Phase 0 = pressed, 1 = still held (about ten a second), 2 = let go;
    // the absorber adds the right button and R (Weapons.RightDown...).
    public struct WeaponFire : IBroadcast { public byte Phase; public Vector3 Origin; public Vector3 Dir; }
    // Host -> everyone: one player's laser heat (0..1) and the seconds until their weapon is ready again.
    public struct WeaponState : IBroadcast { public int PlayerId; public Gear Gear; public float Heat; public float ReadyIn; }
    // Host -> the hook's shooter: a missed hook yanks them to where it bit (a grapple, Owen: "鉤子把自己拉過去").
    public struct HookYank : IBroadcast { public Vector3 To; }

    // Fortress weapons: game items (WeaponItems) fired with the left mouse button anywhere, round or not, at the fortress's
    // enemies and the game's own creatures alike. The client only sends where it aims; the host finds the fish, deals the
    // damage and sends the effects. In a round damage is wave-1 strength times the wave's fish health, so a weapon kills a
    // runner about as fast at wave 20 as at wave 2; outside one it is the wave-1 numbers. Shots pass through the team's own buildings; only the island stops them.
    public partial class Weapons : MonoBehaviour
    {
        public static Weapons Instance;
        public static ConfigEntry<Key> NetKey;
        public static ConfigEntry<float> DamageMultiplier;
        public static bool Drawn => Instance && Instance._drawn;            // the hand net (F) is out
        public static bool NetOut => Instance && Instance._drawn && Instance._net;

        // ---------- tuning (the per-rarity headline numbers are in GearCatalog) ----------
        // Reach before the range mod (RangeOf; Owen: "射距…很多沒設計好"): long shots 30-40 m, aimed shots 25, everything lobbed
        // comes down within 24 (where it will is drawn on the ground, Indicators), close work 10-15, the claw's line 12; the
        // shark hunts 14 m round where it surfaces, the absorber sucks in from 8. Aimed shots take a fish within 0.8 m of the
        // aim, the claw 1.1, a rocket or a lob 1.2.
        public const float LaserRange = 30f, LaserRadius = 0.8f, LaserRampSeconds = 2f, LaserMaxRamp = 3f, OverheatLock = 2f, HeatCoolPerSecond = 0.35f;
        public static readonly float[] LaserHeatSeconds = { 4.5f, 5.5f, 6.5f, 8f };        // seconds of beam before it overheats
        public const float GasRange = 24f, GasRadius = 3f, GasLife = 6f, PoisonLinger = 4f, LobFlight = 0.6f;
        public const int MaxPoisonStacks = 5;
        public const float BoomerangRange = 18f, BoomerangLeg = 0.55f, BoomerangHitRadius = 1.3f, BoomerangSlow = 0.4f;
        public static readonly float[] SharkLife = { 15f, 17f, 20f, 25f };
        public const float SharkSpeed = 7f, SharkBiteEvery = 1.2f, SharkReach = 2.6f, SharkSight = 14f, SeaRing = 27.5f, SharkLeap = 0.55f;
        public const float ShotRange = 25f, ShotRadius = 0.8f, LobRadius = 1.2f, WhirlRange = 24f, WhirlRadius = 6f, WhirlDps = 5f, WhirlBurst = 40f;
        public const float ConchRange = 10f, ConchHalfAngle = 30f, ConchDamage = 10f, BubblePop = 20f;
        public const float NetRange = 24f, NetRadius = 3.5f, NetDamage = 8f, NettedMult = 1.3f;
        public const float JellyRange = 24f, JellyRadius = 3f, JellyLife = 6f, JellySlow = 0.35f, JellyHover = 1.3f;
        public const float WaterRange = 15f, WaterRadius = 1.3f, WaterHurt = 0.6f, WaterPushMax = 25f;
        public static readonly float[] WaterTankSeconds = { 3.5f, 4f, 4.5f, 5.5f };   // seconds of spray before the tank is empty
        public const float PufferRange = 24f, PufferRadius = 3.5f, PufferFuse = 1.2f;
        public static readonly int[] StarCount = { 3, 3, 4, 5 };
        public const float StarRange = 25f, StarSpread = 9f;
        public const float ClawRange = 12f, ClawAim = 1.1f, ClawClose = 3.5f, ClawCone = 60f, ClawSpeed = 60f, ClawMiss = 0.5f, ClawThrow = 8f, ClawFlight = 0.5f, ClawSplash = 2.5f;
        public const float RocketRange = 40f, RocketRadius = 3f, RocketSpeed = 32f, RocketSpread = 14f;
        // The gatling: its top rate (rounds a second), seconds for the barrels to spin up and to run down, the share of the top
        // rate as they start turning, and how far a legendary's bullet glances on to the next fish.
        public const float GatlingRange = 30f, GatlingRate = 12f, GatlingSpinUp = 2f, GatlingSpinDown = 1.5f, GatlingFloor = 0.25f, GatlingBounce = 6f;
        // Owen: "同時射出的子彈至少 *10，蜂窩圓筒狀，有點隨機不是同時". Ten barrels at common, ten more at each rarity, packed in a
        // hex (honeycomb) cylinder round the aim; the bullets leave in a short random order, not all on one frame.
        public const int GatlingBarrelBase = 10;
        public const float GatlingSpread = 0.6f;
        public static int GatlingBarrels(Rarity r) => GatlingBarrelBase * (1 + (int)r);

        // Where barrel i of `barrels` sits in the honeycomb cylinder round direction d: an axial hex spiral, scaled to fit.
        public static Vector3 GatlingOffset(int i, int barrels, Vector3 d)
        {
            Vector2 ax = HexAxial(i);
            int ring = Mathf.CeilToInt(Mathf.Sqrt(barrels / 3f));            // the outermost ring for this many barrels
            float spacing = GatlingSpread / Mathf.Max(1f, ring + 0.5f);
            Vector3 f = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            Vector3 u = Vector3.Cross(Vector3.up, f);
            u = u.sqrMagnitude > 1e-4f ? u.normalized : Vector3.right;
            Vector3 w = Vector3.Cross(f, u).normalized;
            return (u * (ax.x + ax.y * 0.5f) + w * (ax.y * 0.8660254f)) * spacing;
        }

        private static Vector2 HexAxial(int i)
        {
            int r = 0;
            while (3 * r * (r + 1) + 1 <= i) r++;
            if (r == 0) return Vector2.zero;
            int start = 3 * r * (r - 1) + 1;
            int k = i - start, side = k / r, step = k % r;
            switch (side)
            {
                case 0: return new Vector2(r - step, step);
                case 1: return new Vector2(-step, r);
                case 2: return new Vector2(-r, r - step);
                case 3: return new Vector2(-r + step, -step);
                case 4: return new Vector2(step, -r);
                default: return new Vector2(r, -r + step);
            }
        }

        // One more boomerang each rarity up (playtest): 1, 2, 3 side by side; a legendary throws four in a wider fan (it was
        // +-21 degrees: the outer two flew wide of any crowd ahead, and it hit a crowd no harder than an epic).
        public static float[] BoomerangFan(Rarity r) => r == Rarity.Legendary ? new[] { -15f, -5f, 5f, 15f } : r == Rarity.Epic ? new[] { -10f, 0f, 10f } : r == Rarity.Rare ? new[] { -6f, 6f } : new[] { 0f };

        public static float RangeOf(Gear g)
        {
            switch (g)
            {
                case Gear.Laser: return LaserRange;
                case Gear.Rocket: return RocketRange;
                case Gear.BubbleGun: case Gear.Hook: return ShotRange;
                case Gear.StarThrower: return StarRange;
                case Gear.PoisonGas: return GasRange;
                case Gear.Whirlpool: return WhirlRange;
                case Gear.NetLauncher: return NetRange;
                case Gear.JellyGun: return JellyRange;
                case Gear.PufferBomb: return PufferRange;
                case Gear.Boomerang: return BoomerangRange;
                case Gear.Conch: return ConchRange;
                case Gear.WaterCannon: return WaterRange;
                case Gear.LobsterClaw: return ClawRange;
                case Gear.SharkWhistle: return SharkSight;
                case Gear.Absorber: return SuckRange;
                case Gear.Gatling: return GatlingRange;
            }
            return 0f;
        }

        public static string ModelName(Gear g)
        {
            switch (g)
            {
                case Gear.Laser: return "LaserGun";
                case Gear.PoisonGas: return "GasGun";
                case Gear.Boomerang: return "Boomerang";
                case Gear.SharkWhistle: return "SharkWhistle";
                case Gear.BubbleGun: return "BubbleGun";
                case Gear.Hook: return "HookGun";
                case Gear.Whirlpool: return "WhirlCannon";
                case Gear.Conch: return "Conch";
                default: return g.ToString(); // the second set: the model is named after the gear
            }
        }

        // ---------- pure helpers (also used by the headless tests) ----------

        // How hard the laser bites after staying on one fish for `seconds`: x1 at first, x3 (`max`) after LaserRampSeconds.
        public static float LaserRamp(float seconds, float max = LaserMaxRamp) => Mathf.Min(max, 1f + Mathf.Max(0f, seconds) / LaserRampSeconds * (max - 1f));

        // The gatling's rounds a second with its barrels `spin` of the way up (0..1), at a top `rate`.
        public static float GatlingShots(float spin, float rate = GatlingRate) => rate * Mathf.Lerp(GatlingFloor, 1f, Mathf.Clamp01(spin));

        // Where a boomerang is `t` seconds after leaving `a` toward `b` (slows to a stop at b, speeds back to a).
        public static Vector3 BoomerangAt(Vector3 a, Vector3 b, float t)
        {
            if (t <= BoomerangLeg) return Vector3.Lerp(a, b, Mathf.Sin(Mathf.Clamp01(t / BoomerangLeg) * Mathf.PI * 0.5f));
            return Vector3.Lerp(b, a, 1f - Mathf.Cos(Mathf.Clamp01((t - BoomerangLeg) / BoomerangLeg) * Mathf.PI * 0.5f));
        }

        // The lobster claw's catch among fish bodies: the nearest one along the aim from `o` to `end` (within ClawAim of it),
        // or failing that the nearest one right in front of you (ClawClose, within ClawCone of the aim, flat). -1: none.
        public static int ClawPick(Vector3 o, Vector3 end, IList<Vector3> bodies)
        {
            int best = -1;
            float bestT = float.MaxValue;
            Vector3 d = (end - o).normalized;
            for (int i = 0; i < bodies.Count; i++)
            {
                float t = Vector3.Dot(bodies[i] - o, d);
                if (t > 0f && t < bestT && DistToSegment(bodies[i], o, end) <= ClawAim) { bestT = t; best = i; }
            }
            if (best >= 0) return best;
            Vector3 fwd = d.Flat().normalized;
            float near = ClawClose * ClawClose;
            for (int i = 0; i < bodies.Count; i++)
            {
                Vector3 to = (bodies[i] - o).Flat();
                if (to.sqrMagnitude <= near && Vector3.Angle(fwd, to) < ClawCone) { near = to.sqrMagnitude; best = i; }
            }
            return best;
        }

        public static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return (a + ab * t - p).magnitude;
        }

        private void Awake() => Instance = this;

        private NetworkManager _registeredOn;

        static Weapons()
        {
            GenericWriter<WeaponFire>.SetWrite((w, v) => { w.WriteByte(v.Phase); w.WriteVector3(v.Origin); w.WriteVector3(v.Dir); });
            GenericReader<WeaponFire>.SetRead(r => new WeaponFire { Phase = r.ReadByte(), Origin = r.ReadVector3(), Dir = r.ReadVector3() });
            GenericWriter<WeaponState>.SetWrite((w, v) => { w.WriteInt32(v.PlayerId); w.WriteByte((byte)v.Gear); w.WriteSingle(v.Heat); w.WriteSingle(v.ReadyIn); });
            GenericReader<WeaponState>.SetRead(r => new WeaponState { PlayerId = r.ReadInt32(), Gear = (Gear)r.ReadByte(), Heat = r.ReadSingle(), ReadyIn = r.ReadSingle() });
            GenericWriter<HookYank>.SetWrite((w, v) => w.WriteVector3(v.To));
            GenericReader<HookYank>.SetRead(r => new HookYank { To = r.ReadVector3() });
        }

        private void Update()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm && nm != _registeredOn)
            {
                _registeredOn = nm;
                nm.ServerManager.RegisterBroadcast(Net.Safe<WeaponFire>((conn, f, ch) =>
                {
                    var who = PlayerManager.Players.FirstOrDefault(p => p && p.Owner == conn);
                    if (who) ServerFire(who, f);
                }));
                nm.ClientManager.RegisterBroadcast<WeaponState>((s, ch) => OnState(s));
                nm.ClientManager.RegisterBroadcast<HookYank>((h, ch) =>
                {
                    var me = Player.LocalPlayer;
                    if (!me || !me.Transform) return;
                    me.LocalTeleport(h.To, me.Transform.eulerAngles.y, true);
                    Fx.SoundRandom("PickUp_V", 1, 3, h.To, 0.8f, AudioDistance.Short);
                    AutoTest.Count("hook_yank");
                });
                nm.ClientManager.OnClientConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) ClearViews(); };
            }
            if (InstanceFinder.IsServerStarted) ServerTick();
            ClientTick();
        }

        // =====================================================================================================
        // host
        // =====================================================================================================

        private class Gun { public Gear Gear; public float ReadyAt, Heat, LastTick = -10f, RampStart, NextState, NumberDmg, NextNumber, Rounds; public bool Overheated; }
        private class Cloud { public Vector3 At; public float Radius, Until, Dps; public int Owner; public bool Legend; }
        private class Poison { public int Stacks, Owner; public float Until, NextStack, Dps, NextNumber; public bool Legend; }
        private class Vortex { public Vector3 At; public float Until, Dps, NextNumber, NumberDmg; public int Owner; public bool Legend; }
        private class Shark { public int Id, Owner; public Vector3 Home, Pos, Face; public float Until, Landed, NextBite, Dmg, Sight = SharkSight, Size = 1.85f; }
        private class Jelly { public Vector3 At; public float Radius, Until, Dps, NextNumber, NumberDmg; public int Owner; public bool Legend; }

        private readonly Dictionary<(int, Gear), Gun> _guns = new Dictionary<(int, Gear), Gun>(); // each weapon keeps its own heat and cooldown
        private readonly List<Cloud> _clouds = new List<Cloud>();
        private readonly Dictionary<Creature, float> _bubbleDamage = new Dictionary<Creature, float>(); // what a rising bubble has collected
        private readonly Dictionary<Creature, Poison> _poison = new Dictionary<Creature, Poison>();
        private readonly List<Vortex> _vortices = new List<Vortex>();
        private readonly List<Shark> _sharks = new List<Shark>();
        private readonly List<Jelly> _jellies = new List<Jelly>();
        private readonly Dictionary<Creature, float> _netted = new Dictionary<Creature, float>(); // legendary net: until when
        // The hook takes anything lying about (Owen): it comes back on the line and hangs by the gun, and the next shot flings
        // it, as hard as it's worth; it lands wherever it stops, to be hooked again.
        private readonly Dictionary<int, Item> _hooked = new Dictionary<int, Item>(); // player -> what hangs on their hook
        private class Fling { public Item Item; public Player By; public float Dmg, Until; public int Left; public readonly HashSet<Creature> Hit = new HashSet<Creature>(); }
        private readonly List<Fling> _flung = new List<Fling>();
        public const float FlingSpeed = 30f, FlingRadius = 1.2f;
        // A thrown thing hits for the hook's damage times this: $10 of fish x1.4, a $400 gun x3.8, a $9,000 legendary x14, a
        // boss's $20,000 trophy x21. Heavier things knock the fish further back.
        public static float FlingMult(int worth) => 1f + Mathf.Sqrt(Mathf.Max(0, worth) / 50f);
        public static bool OnLine(Item it) => Instance && it && (Instance._hooked.ContainsValue(it) || Instance._flung.Any(f => f.Item == it));
        private int _nextShark = 1;
        private float _nextHalfSecond, _nextSharkSend;
        private bool _wasActive;

        // In a round damage keeps up with that wave's fish; outside one it's the wave-1 numbers.
        internal static float Scale
        {
            get
            {
                var v = Loot.Instance ? (InstanceFinder.IsServerStarted ? Loot.Instance.Vault : Loot.Instance.ClientVault) : null;
                return (DamageMultiplier?.Value ?? 1f) * (v != null ? v.DamageMult : 1f); // souls' permanent damage tree
            }
        } // Owen: weapons are fixed; growth comes from mods, rarity, later weapons and the keeper's tree, not the wave
        private static Player PlayerById(int id) => PlayerManager.Players.FirstOrDefault(p => p && p.ObjectId == id);
        // Everything alive the weapons can hit: the fortress's enemies and, round or not, the game's own creatures (not one
        // somebody is carrying).
        private static readonly AccessTools.FieldRef<CreatureManager, List<Creature>> Wild = AccessTools.FieldRefAccess<CreatureManager, List<Creature>>("_aliveCreatures");
        // Rebuilt at most ten times a second and shared by every weapon, cloud, jelly and shark: the old one made a new list
        // and ran a handful of LINQ lambdas for every shot (thousands of allocations a second on a late wave).
        private static readonly List<Creature> _targets = new List<Creature>(256);
        private static readonly HashSet<Creature> _targetSeen = new HashSet<Creature>();
        private static float _nextTargets;
        private static List<Creature> Targets()
        {
            if (Time.time < _nextTargets) return _targets;
            _nextTargets = Time.time + 0.1f;
            _targets.Clear(); _targetSeen.Clear();
            foreach (var c in FortressPlugin.Alive)
                if (c && c.IsSpawned && c._hp.Value > 0 && !c.Holder && !Siege.Instance.IsHidden(c) && !Bosses.Entering(c) && _targetSeen.Add(c)) _targets.Add(c);
            if (CreatureManager.Instance)
                foreach (var c in Wild(CreatureManager.Instance))
                    if (c && c.IsSpawned && c._hp.Value > 0 && !c.Holder && !Siege.Instance.IsHidden(c) && !Bosses.Entering(c) && _targetSeen.Add(c)) _targets.Add(c);
            return _targets;
        }
        private static Vector3 Body(Creature c) => c.transform.position + Vector3.up * 0.5f;
        // How big a fortress fish is drawn (a tank 1.8, an elite 1.3 times more, a boss far more): a thrown thing that reaches
        // its body has hit it, even while it's still a way from its middle.
        private static float SizeOf(Creature c) => Enemies.Instance && Enemies.Instance.TryGet(c, out var t) ? Mathf.Max(1f, Enemies.StatsOf(t).Scale) : 1f;
        private static bool IsBoss(Creature c) => Enemies.Instance.TryGet(c, out var t) && EnemyCatalog.IsBoss(t.Kind);
        private static bool IsSpitter(Creature c) => Enemies.Instance.TryGet(c, out var t) && t.Kind == EnemyKind.Spitter;

        // Fish along a shot from a to b, nearest first.
        private static List<Creature> Along(Vector3 a, Vector3 b, float radius) =>
            Targets().Where(c => Touches(c, a, b, radius)).OrderBy(c => (Body(c) - a).sqrMagnitude).ToList();

        // A shot that touches a fish's body hits it, not only one that passes its middle (Owen agreed: aimed shots missed
        // tanks and bosses they visibly touched). A fortress fish reaches past its middle by its drawn size; a boss by the box
        // round its meshes (measured once a second).
        private static readonly Dictionary<Creature, (float until, Bounds box)> BossBoxes = new Dictionary<Creature, (float, Bounds)>();
        private static float Girth(Creature c) => 0.45f * (SizeOf(c) - 1f);
        private static Bounds BoxOf(Creature c, float radius)
        {
            if (!BossBoxes.TryGetValue(c, out var k) || Time.time > k.until)
            {
                if (BossBoxes.Count > 20) BossBoxes.Clear(); // bosses gone by
                BossBoxes[c] = k = (Time.time + 1f, BossLooks.WorldBounds(c.transform));
            }
            var box = k.box;
            box.Expand(radius * 2f);
            return box;
        }
        private static bool Touches(Creature c, Vector3 a, Vector3 b, float radius)
        {
            if (!IsBoss(c)) return DistToSegment(Body(c), a, b) <= radius + Girth(c);
            var box = BoxOf(c, radius);
            Vector3 d = b - a;
            float len = d.magnitude;
            return box.Contains(a) || (len > 1e-4f && box.IntersectRay(new Ray(a, d / len), out float at) && at <= len);
        }
        private static bool Touches(Creature c, Vector3 p, float radius) =>
            IsBoss(c) ? BoxOf(c, radius).Contains(p) : (Body(c) - p).sqrMagnitude <= (radius + Girth(c)) * (radius + Girth(c));

        // Where a shot from `o` stops: the island's ground. The team's own buildings, the lighthouse and anything lying about
        // (a trophy, a gun, a fish's body) don't block it.
        public static Vector3 Reach(Vector3 o, Vector3 d, float range)
        {
            foreach (var h in Physics.RaycastAll(o, d, range, GameInfo.LevelLayer).OrderBy(h => h.distance))
            {
                string n = h.collider.gameObject.name;
                if (n.StartsWith("FortressPiece") || n == "FortressCore" || ItemManager.Get(h.collider)) continue;
                return h.point;
            }
            return o + d * range;
        }

        // Where a lobbed shot comes down: on the first fish in its way (aiming at a fish hits it, not the ground behind it),
        // or where the aim meets the ground / sea.
        private static Vector3 Land(Vector3 o, Vector3 d, float range)
        {
            Vector3 p = Reach(o, d, range);
            var first = Along(o, p, LobRadius).FirstOrDefault();
            if (first) p = first.transform.position;
            if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out var g, 40f, GameInfo.LevelLayer)) p = g.point;
            p.y = Mathf.Max(p.y, WaterManager.GetWaterHeight(p));
            return p;
        }

        // Where a shot leaves: the muzzle of the weapon in the shooter's hand as this machine sees it (WeaponItems.Spec), or,
        // with no model to go by (or one that's far from where they shoot from), a little ahead of and below their eyes.
        private static Vector3 Muzzle(Player who, Vector3 o, Vector3 d) => HeldMuzzle(who, o, 2.5f) ?? o + d * 0.6f + Vector3.down * 0.25f;

        internal static Vector3? HeldMuzzle(Player who, Vector3 near, float within)
        {
            var held = who && who.Holding ? who.Holding.HeldItem : null;
            if (!WeaponItems.Is(held, out var g, out _) || GearCatalog.IsGameGun(g)) return null;
            var m = WeaponItems.ModelOf(held);
            if (!m || !m.gameObject.activeInHierarchy) return null;
            var p = WeaponItems.MuzzleOf(m, g);
            return Finite(p) && (p - near).sqrMagnitude <= within * within ? p : (Vector3?)null;
        }

        // Every weapon hit: the kill and the stats are the shooter's, the gauntlet counts, a bubbled fish takes half again.
        private static int Hurt(Creature c, float dmg, Player by)
        {
            if (!c || c._hp.Value <= 0) return 0;
            if (by) Stats.HandHit(c, by);
            dmg *= 1f + Perks.Of(by, Gear.Gauntlet);
            if (!IsBoss(c) && HoldsAbsorber(by)) dmg *= AbsorberBoost; // the final gun into fortress fish (a boss takes it as it is)
            if (Siege.Instance.IsBubbled(c)) dmg *= 1.5f;
            if (IsNetted(c)) dmg *= NettedMult;
            var sv = Loot.Instance ? (InstanceFinder.IsServerStarted ? Loot.Instance.Vault : Loot.Instance.ClientVault) : null;
            if (sv != null && sv.CritChance > 0f && Random.value < sv.CritChance) dmg *= 2f; // the keeper's crit tree
            int d = Mathf.Max(1, Mathf.RoundToInt(dmg));
            c.ServerChangeHp(d);
            return d;
        }

        private static void HitNumber(Creature c, int dmg) { if (c && dmg > 0) Fx.Send(FxKind.WeaponHit, Body(c), Body(c), 0, dmg); }

        public void ServerFire(Player who, WeaponFire f)
        {
            if (!who || !who.Transform || !PlayerManager.AlivePlayers.Contains(who)) return; // any time, not only in a round
            if (!WeaponItems.Held(who, out var g, out var r) || GearCatalog.IsGameGun(g)) return; // the fortress weapon item in their hand (the game's guns fire themselves)
            if (!Finite(f.Origin) || !Finite(f.Dir)) return;
            Vector3 o = f.Origin, d = f.Dir.sqrMagnitude > 0.01f ? f.Dir.normalized : who.Transform.forward;
            Vector3 eye = who.CamObject ? who.CamObject.position : who.Transform.position + Vector3.up * 1.6f; // a friend's camera as the host sees it
            if ((o - eye).sqrMagnitude > 3f * 3f) o = eye; // shots start at the shooter
            if (g == Gear.Absorber) { AbsorberFire(who, f.Phase, o, d, r); return; }
            if (f.Phase <= 2) FireAs(who, f.Phase, o, d, g, r);
        }

        // `who` fires `g` at rarity `r` (in hand, or copied by their absorber), with their own mods on that kind: power in the
        // number, speed in the cooldown, the trait where it acts.
        private void FireAs(Player who, byte phase, Vector3 o, Vector3 d, Gear g, Rarity r)
        {
            var vault = Loot.Instance.Vault;
            ulong key = Loot.KeyOf(who);
            int starItem = WeaponItems.StarsOf(who && who.Holding ? who.Holding.HeldItem : null);
            int stars = starItem > 0 ? starItem : vault.BestStar(key, g, r);
            float v = vault.WeaponValue(key, g, r, stars);
            int rate = vault.ModOf(key, g, WeaponMods.Rate), tr = vault.ModOf(key, g, WeaponMods.Trait);
            float reach = WeaponMods.ReachMult(vault.ModOf(key, g, WeaponMods.Reach));
            bool legend = r == Rarity.Legendary;
            var gun = GunOf(who, g);
            if (g == Gear.Laser) { LaserTick(who, gun, phase, o, d, v, legend, r, rate, tr, reach); return; }
            if (g == Gear.WaterCannon) { WaterTick(who, gun, phase, o, d, v, legend, r, rate, tr, reach); return; }
            if (g == Gear.Gatling) { GatlingTick(who, gun, phase, o, d, v, legend, r, rate, tr, reach); return; }
            if (phase == 2 || Time.time < gun.ReadyAt) return;
            if (WeaponMods.Lingers(g)) v /= WeaponMods.TimeMult(rate); // fire rate: a cloud or a jellyfish stings faster (more of them wouldn't add: stacks and overlaps cap out)
            float cd = Fire(g, who, o, d, v, legend, r, WeaponMods.TraitAdd(g, tr), reach);
            if (cd < 0f) return;
            gun.ReadyAt = Time.time + cd * WeaponMods.TimeMult(rate);
            SendState(who, gun);
            AutoTest.Count("weapon_" + g);
        }

        private static bool Finite(Vector3 v) => !float.IsNaN(v.x + v.y + v.z) && !float.IsInfinity(v.x + v.y + v.z);

        private static void SendState(Player who, Gun gun) =>
            InstanceFinder.ServerManager.Broadcast(who.Owner, new WeaponState { PlayerId = who.ObjectId, Gear = gun.Gear, Heat = gun.Heat, ReadyIn = Mathf.Max(0f, gun.ReadyAt - Time.time) });

        // A shot that isn't the laser. `trait`: what the shooter's trait mod adds (WeaponMods.TraitStep says to what); `reach`:
        // their range mod, times RangeOf. Returns how long until the weapon is ready again (negative: it didn't go off).
        private float Fire(Gear g, Player who, Vector3 o, Vector3 d, float v, bool legend, Rarity r, float trait, float reach)
        {
            float s = Scale;
            Vector3 muzzle = Muzzle(who, o, d);
            switch (g)
            {
                case Gear.PoisonGas:
                {
                    Vector3 at = Land(o, d, GasRange * reach);
                    float life = GasLife + trait;
                    Fx.Send(FxKind.Gas, muzzle, at, 0, Mathf.RoundToInt(life * 1000f), who.ObjectId, "PoisonGas");
                    StartCoroutine(After(LobFlight, () => _clouds.Add(new Cloud { At = at, Radius = GasRadius, Until = Time.time + life, Dps = v, Owner = who.ObjectId, Legend = legend })));
                    return GearCatalog.Cooldown(g, r);
                }
                case Gear.Boomerang:
                {
                    float range = BoomerangRange * reach;
                    Vector3 start = muzzle;
                    foreach (float yaw in BoomerangFan(r))
                    {
                        Vector3 end = Reach(start, Quaternion.Euler(0f, yaw, 0f) * d, range); // not into the ground
                        Fx.Send(FxKind.Boomerang, start, end, 0, 0, who.ObjectId, "Boomerang");
                        StartCoroutine(BoomerangFlight(start, end, v, who, trait));
                    }
                    return BoomerangLeg * 2f + 0.15f; // back in your hand
                }
                case Gear.SharkWhistle:
                {
                    // Owen: "打到的地方直接一灘水，鯊魚從下面跳上來咬". A pool opens where it lands; the shark leaps out of it
                    // and then stays, hunting round its pool and biting (SharkTick) till its time is up.
                    Vector3 at = Land(o, d, 30f);
                    Fx.Send(FxKind.Water, at, at); // the pool
                    // The shark grows with the gun's own power/trait upgrades and rarity: a maxed shark is a real sea monster,
                    // not the same little fish every time.
                    var sharkVault = Loot.Instance ? Loot.Instance.Vault : null;
                    ulong sharkKey = Loot.KeyOf(who);
                    int pl = sharkVault != null ? sharkVault.ModOf(sharkKey, Gear.SharkWhistle, WeaponMods.Power) : 0;
                    int tl = sharkVault != null ? sharkVault.ModOf(sharkKey, Gear.SharkWhistle, WeaponMods.Trait) : 0;
                    float size = Mathf.Clamp(1.70f + 0.075f * pl + 0.05f * tl + 0.12f * (int)r, 1.7f, 3.4f);
                    string sizeKey = size.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    foreach (var spot in SharkSpots(at, r))
                    {
                        var sh = new Shark { Id = _nextShark++, Owner = who.ObjectId, Home = spot, Pos = spot, Face = spot + d.Flat(), Dmg = v, Size = size,
                                             Until = Time.time + SharkLife[(int)r] + trait, Landed = Time.time + SharkLeap, NextBite = Time.time + SharkLeap, Sight = SharkSight * reach };
                        _sharks.Add(sh);
                        Fx.Send(FxKind.Shark, spot + Vector3.down * 3.2f, spot, sh.Id, 1, who.ObjectId, sizeKey); // the leap
                        Fx.Send(FxKind.Water, spot, spot);
                        AutoTest.Count("shark_jump");
                    }
                    return GearCatalog.Cooldown(g, r);
                }
                case Gear.BubbleGun:
                {
                    Vector3 end = Reach(o, d, ShotRange * reach);
                    var c = Along(o, end, ShotRadius).FirstOrDefault();
                    Fx.Send(FxKind.Bubble, muzzle, c ? Body(c) : end, c && !IsBoss(c) ? c.ObjectId : 0, Mathf.RoundToInt(v * 1000f), who.ObjectId, "BubbleGun");
                    float pop = (legend ? BubblePop : 0f) + trait; // a legendary's burst; the trait makes any bubble burst
                    if (c)
                    {
                        // Stacked shots: keep the fish up, float it higher, and remember everything the bubble has taken.
                        _bubbleDamage.TryGetValue(c, out float stored);
                        stored += v * s;
                        _bubbleDamage[c] = stored;
                        float rise = Mathf.Min(9f, Siege.Instance.BubbleRiseOf(c) + 1.2f);
                        var fish = c; var dmg = stored;
                        Siege.Instance.ServerBubble(c, v, rise, at =>
                        {
                            _bubbleDamage.Remove(fish);
                            Pop(at, dmg + pop * s, who, legend);
                        });
                    }
                    return GearCatalog.Cooldown(g, r);
                }
                case Gear.Hook:
                {
                    // Something on the hook already: it's flung, as hard as it's worth.
                    if (_hooked.TryGetValue(who.ObjectId, out var load))
                    {
                        _hooked.Remove(who.ObjectId);
                        if (load && Hookable(load, who)) { FlingOut(who, load, muzzle, d, v * s, legend); return GearCatalog.Cooldown(g, r); }
                    }
                    Vector3 end = Reach(o, d, ShotRange * reach);
                    var c = Along(o, end, ShotRadius).FirstOrDefault();
                    // Anything else lying nearer than the first fish: it comes back on the line.
                    var thing = ItemAlong(o, d, c ? (Body(c) - o).magnitude : (end - o).magnitude, who);
                    if (thing)
                    {
                        Fx.Send(FxKind.Hook, muzzle, thing.transform.position, thing.ObjectId, 1, who.ObjectId);
                        StartCoroutine(ReelIn(who, thing));
                        AutoTest.Count("hooked_item");
                        return 0.6f;
                    }
                    Fx.Send(FxKind.Hook, muzzle, c ? Body(c) : end, c ? c.ObjectId : 0, 0, who.ObjectId, "Hook");
                    if (!c)
                    {
                        // Nothing to grab: the hook holds the wall and pulls the shooter to it (up to 9 m).
                        Vector3 to = o + d * Mathf.Min(9f, (end - o).magnitude);
                        if (Loot.Ground(to + Vector3.up * 3f, 20f, out var gnd)) to = gnd + Vector3.up * 0.2f;
                        if ((to - o).sqrMagnitude > 4f) InstanceFinder.ServerManager.Broadcast(who.Owner, new HookYank { To = to });
                        return 0.6f;
                    }
                    HookFish(c, who, v * s, who.Transform.position + d.Flat().normalized * 2.5f, trait);
                    if (legend)
                    {
                        var next = Targets().Where(x => x != c && (x.transform.position - c.transform.position).sqrMagnitude < 6f * 6f).OrderBy(x => (x.transform.position - c.transform.position).sqrMagnitude).FirstOrDefault();
                        if (next)
                        {
                            Fx.Send(FxKind.Hook, Body(c), Body(next), next.ObjectId, 0, who.ObjectId);
                            HookFish(next, who, v * s, who.Transform.position + d.Flat().normalized * 3.5f, trait);
                            Perks.Proc(Gear.Hook, next.transform.position);
                        }
                    }
                    return GearCatalog.Cooldown(g, r);
                }
                case Gear.Whirlpool:
                {
                    Vector3 at = Land(o, d, WhirlRange * reach);
                    Fx.Send(FxKind.Whirl, muzzle, at, 0, Mathf.RoundToInt(v * 1000f), who.ObjectId, "Whirlpool");
                    StartCoroutine(After(LobFlight, () => _vortices.Add(new Vortex { At = at, Until = Time.time + v, Dps = WhirlDps + trait, Owner = who.ObjectId, Legend = legend })));
                    return GearCatalog.Cooldown(g, r);
                }
                case Gear.NetLauncher:
                {
                    // A net shot out in an arc: everything under it where it comes down is held fast (a boss only slowed); a
                    // legendary net is half as big again and the fish under it take 30% more from everything while held.
                    Vector3 at = Land(o, d, NetRange * reach);
                    float radius = (NetRadius + trait) * (legend ? 1.5f : 1f);
                    Fx.Send(FxKind.Net, muzzle, at, Mathf.RoundToInt(radius * 100f), Mathf.RoundToInt(v * 1000f) * (legend ? -1 : 1), who.ObjectId, "NetLauncher");
                    StartCoroutine(After(LobFlight, () =>
                    {
                        foreach (var c in Targets().Where(x => (x.transform.position - at).Flat().sqrMagnitude <= radius * radius))
                        {
                            HitNumber(c, Hurt(c, NetDamage * s, who));
                            Siege.Instance.Slow(c, IsBoss(c) ? 0.5f : 1f, v);
                            if (legend) { _netted[c] = Time.time + v; Perks.Proc(Gear.NetLauncher, c.transform.position); }
                        }
                        AutoTest.Count("netted");
                    }));
                    return GearCatalog.Cooldown(g, r);
                }
                case Gear.JellyGun:
                {
                    // A jellyfish lobbed out: it hangs over where it lands for 6 s, stinging and slowing every fish under it.
                    Vector3 at = Land(o, d, JellyRange * reach);
                    float life = JellyLife + trait;
                    Fx.Send(FxKind.Jelly, muzzle, at, 0, Mathf.RoundToInt(life * 1000f), who.ObjectId, "JellyGun");
                    StartCoroutine(After(LobFlight, () => _jellies.Add(new Jelly { At = at, Radius = JellyRadius, Until = Time.time + life, Dps = v, Owner = who.ObjectId, Legend = legend })));
                    return GearCatalog.Cooldown(g, r);
                }
                case Gear.PufferBomb:
                {
                    // Thrown; it lands, puffs up, and bursts: spines into everything round it and poison (a legendary bursts twice).
                    Vector3 at = Land(o, d, PufferRange * reach);
                    Fx.Send(FxKind.Puffer, muzzle, at, 0, Mathf.RoundToInt(PufferFuse * 1000f), who.ObjectId, "PufferBomb");
                    StartCoroutine(After(LobFlight + PufferFuse, () => PufferBurst(at, v * s, who, legend ? 1 : 0, PufferRadius + trait)));
                    return GearCatalog.Cooldown(g, r);
                }
                case Gear.StarThrower:
                {
                    // A fan of starfish, each cutting through two fish (three for a legendary).
                    int n = StarCount[(int)r], pierce = (legend ? 3 : 2) + Mathf.RoundToInt(trait);
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 dir = Quaternion.AngleAxis((i - (n - 1) * 0.5f) * StarSpread, Vector3.up) * d;
                        // A thrown starfish bounces off walls (Owen: "反彈"): three legs at most, each one still cuts fish.
                        int left = pierce;
                        var seen = new HashSet<Creature>();
                        Vector3 from = o;
                        for (int bounce = 0; bounce < 3 && left > 0; bounce++)
                        {
                            Vector3 end = Reach(from, dir, StarRange * reach);
                            var hit = Along(from, end, ShotRadius).Where(c => seen.Add(c)).Take(left).ToList();
                            foreach (var c in hit) { HitNumber(c, Hurt(c, v * s, who)); left--; }
                            if (legend && hit.Count > 1) Perks.Proc(Gear.StarThrower, hit[hit.Count - 1].transform.position);
                            Fx.Send(FxKind.Star, bounce == 0 ? muzzle : from, hit.Count > 0 ? Body(hit[hit.Count - 1]) : end, 0, 0, who.ObjectId, "StarThrower");
                            if (left <= 0) break;
                            if (!WallBounce(from, end, out var normal)) break;
                            dir = Vector3.Reflect(dir, normal);
                            from = end + dir * 0.1f;
                        }
                    }
                    return GearCatalog.Cooldown(g, r);
                }
                case Gear.LobsterClaw:
                {
                    // The claw shoots out on its line (Owen: "龍蝦槍…很難用": it was a grab at arm's length, and with nothing that
                    // close nothing came out): the first fish along the aim (ClawPick: or, aimed just off one right in front of
                    // you, that one), and a legendary's two more beside it, are flung on ahead when it gets there; each is hurt
                    // where it lands, and so (half) are the fish it lands on. A boss is too heavy: pinched and slowed.
                    var all = Targets();
                    Vector3 end = Reach(o, d, ClawRange * reach), fwd = d.Flat().normalized;
                    int pick = ClawPick(o, end, all.Select(Body).ToList());
                    var first = pick >= 0 ? all[pick] : null;
                    Vector3 to = first ? Body(first) : end;
                    Fx.Send(FxKind.Claw, muzzle, to, first ? first.ObjectId : 0, 0, who.ObjectId, "LobsterClaw");
                    if (!first) return ClawMiss;
                    var grabbed = new List<Creature> { first };
                    if (legend) grabbed.AddRange(all.Where(x => x != first && (x.transform.position - first.transform.position).sqrMagnitude < 4f * 4f)
                                                    .OrderBy(x => (x.transform.position - first.transform.position).sqrMagnitude).Take(2));
                    float dmg = v * s, splash = ClawSplash + trait;
                    StartCoroutine(After((to - muzzle).magnitude / ClawSpeed, () => { foreach (var c in grabbed) ClawFling(c, fwd, dmg, who, splash); }));
                    if (legend && grabbed.Count > 1) Perks.Proc(Gear.LobsterClaw, first.transform.position);
                    return GearCatalog.Cooldown(g, r);
                }
                case Gear.Rocket:
                {
                    // Straight out, bursting on the first fish in its way or where it meets the ground or the sea; a
                    // legendary fires three side by side.
                    foreach (float yaw in legend ? new[] { -RocketSpread, 0f, RocketSpread } : new[] { 0f })
                    {
                        Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * d;
                        Vector3 end = Reach(o, dir, RocketRange * reach);
                        var first = Along(o, end, LobRadius).FirstOrDefault(); // a fat rocket: as forgiving as a lobbed shot
                        float sea = WaterManager.GetWaterHeight(o);
                        if (first) end = Body(first);
                        else if (end.y < sea && dir.y < -0.01f) end = o + dir * ((o.y - sea) / -dir.y); // into the sea: it bursts on the water
                        float flight = (end - muzzle).magnitude / RocketSpeed;
                        Fx.Send(FxKind.Rocket, muzzle, end, 0, Mathf.RoundToInt(flight * 1000f), who.ObjectId, "Rocket");
                        Vector3 at = end;
                        StartCoroutine(After(flight, () => RocketBlast(at, v * s, who, RocketRadius + trait)));
                    }
                    if (legend) Perks.Proc(Gear.Rocket, o + d * 6f);
                    return GearCatalog.Cooldown(g, r);
                }
                case Gear.Conch:
                {
                    Vector3 fwd = d.Flat().normalized;
                    float far = ConchRange * reach, half = ConchHalfAngle + trait;
                    Fx.Send(FxKind.Sonic, muzzle, muzzle + fwd * far, 0, Mathf.RoundToInt(half), who.ObjectId, "Conch");
                    foreach (var c in Targets())
                    {
                        Vector3 to = (c.transform.position - o).Flat();
                        if (to.sqrMagnitude > far * far || Vector3.Angle(fwd, to) > half) continue;
                        HitNumber(c, Hurt(c, ConchDamage * s, who));
                        Siege.Instance.ServerPush(c, (to.sqrMagnitude > 0.01f ? to.normalized : fwd) * (IsBoss(c) ? v * 0.3f : v) * Perks.Knockback);
                        Siege.Instance.Interrupt(c);
                        if (legend) { Siege.Instance.Slow(c, 1f, 1.5f); Perks.Proc(Gear.Conch, c.transform.position); }
                    }
                    return GearCatalog.Cooldown(g, r);
                }
            }
            return -1f;
        }

        // The claw's catch, flung on ahead (a boss only pinched and slowed): hurt where it lands, and (half) the fish it lands on.
        private void ClawFling(Creature c, Vector3 fwd, float dmg, Player who, float splash)
        {
            if (!c || c._hp.Value <= 0) return;
            if (IsBoss(c)) { HitNumber(c, Hurt(c, dmg, who)); Siege.Instance.Slow(c, 0.5f, 1.5f); return; }
            Vector3 land = Reach(c.transform.position + Vector3.up * 1.5f, fwd, ClawThrow);
            if (Physics.Raycast(land + Vector3.up, Vector3.down, out var gh, 30f, GameInfo.LevelLayer)) land = gh.point;
            land.y = Mathf.Max(land.y, WaterManager.GetWaterHeight(land));
            Siege.Instance.ServerPull(c, land, ClawFlight);
            StartCoroutine(After(ClawFlight, () =>
            {
                if (c) HitNumber(c, Hurt(c, dmg, who));
                foreach (var x in Targets().Where(x => x != c && (x.transform.position - land).sqrMagnitude <= splash * splash)) HitNumber(x, Hurt(x, dmg * 0.5f, who));
                Fx.Send(FxKind.Dust, land, land, 0, Mathf.RoundToInt(splash * 100f)); // the claw's landing splash grows with its radius
            }));
        }

        // ---------- the hook's catch: anything lying about, reeled in, then flung ----------

        // What the hook can take: an item lying about, not held or carried (Loot.Storable's rules), not on somebody else's line.
        private bool Hookable(Item it, Player who) =>
            it && it.Rig && it.RigidbodySync && !it.Holder && !it.SyncedHolder && !it.IsInInventory && it.IsSpawned && !it.IsDestroying && !it.IsDeinitializing
            && !it.BirdHolder && !it.AttachedRod && !it.DeadPlayer && !it.IsQuestItem && (!it.Creature || it.Creature.IsDead) && !Swallow.IsGoing(it)
            && !_hooked.Any(kv => kv.Key != who.ObjectId && kv.Value == it) && !_flung.Any(f => f.Item == it);

        // The first item along the aim, nearer than `max`.
        private Item ItemAlong(Vector3 o, Vector3 d, float max, Player who)
        {
            foreach (var h in Physics.SphereCastAll(o, 0.35f, d, max, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                var it = ItemManager.Get(h.collider);
                if (it && Hookable(it, who)) return it;
            }
            return null;
        }

        // Auto-test: what the hook's sweep meets along a line.
        public string DebugHookScan(Vector3 o, Vector3 d, Player who) => string.Join("; ", Physics.SphereCastAll(o, 0.35f, d, ShotRange, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance)
            .Select(h => { var it = ItemManager.Get(h.collider); return $"{h.collider.name} (layer {h.collider.gameObject.layer}, {h.distance:0.0} m{(it ? $", item {it.name} hookable {Hookable(it, who)}: holder {(bool)it.Holder} synced {(bool)it.SyncedHolder} inv {it.IsInInventory} spawned {it.IsSpawned} destroying {it.IsDestroying}/{it.IsDeinitializing} bird {(bool)it.BirdHolder} rod {(bool)it.AttachedRod} dead {(bool)it.DeadPlayer} quest {it.IsQuestItem} creature {(bool)it.Creature} swallow {Swallow.IsGoing(it)}" : "")})"; }));

        // Where a catch hangs: by the gun, a little out in front of the shooter's view.
        private static Vector3 HookHold(Player who)
        {
            var cam = who.CamObject ? who.CamObject : who.Transform;
            return cam.position + cam.forward * 1.1f + cam.right * 0.45f + Vector3.down * 0.35f;
        }

        private IEnumerator ReelIn(Player who, Item it)
        {
            it.RigidbodySync.StartSimulateLocal(); // the host moves it now
            it.RigidbodySync.SetKinematic(true);
            Vector3 from = it.transform.position;
            for (float t = 0f; t < 0.35f && it && who && who.Transform; t += Time.deltaTime)
            {
                it.transform.position = Vector3.Lerp(from, HookHold(who), t * t / (0.35f * 0.35f));
                Siege.Synced(it);
                yield return null;
            }
            if (it && who) _hooked[who.ObjectId] = it;
            else if (it) it.RigidbodySync.SetKinematic(false);
        }

        private void FlingOut(Player who, Item it, Vector3 from, Vector3 d, float dmg, bool legend)
        {
            int worth = it.TotalWorth;
            it.transform.position = from + d * 0.5f;
            it.RigidbodySync.SetKinematic(false);
            it.Rig.linearVelocity = d * FlingSpeed + Vector3.up * 1.5f;
            it.Rig.angularVelocity = Random.insideUnitSphere * 12f;
            Siege.Synced(it);
            // A legendary hook's throw goes through three fish.
            _flung.Add(new Fling { Item = it, By = who, Dmg = dmg * FlingMult(worth), Until = Time.time + 3f, Left = legend ? 3 : 1 });
            Fx.Send(FxKind.Hook, from, from + d, it.ObjectId, 2, who.ObjectId); // 2: flung
            AutoTest.Count("hook_flung");
        }

        // Every frame: what hangs on a hook stays by its gun (let go once the gun isn't in hand), what's flung hits the fish it
        // flies into.
        private void CatchTick()
        {
            foreach (var kv in _hooked.ToList())
            {
                var who = PlayerById(kv.Key);
                var it = kv.Value;
                bool keep = it && who && who.Transform && PlayerManager.AlivePlayers.Contains(who) && Wields(who, Gear.Hook) && Hookable(it, who);
                if (!keep)
                {
                    _hooked.Remove(kv.Key);
                    if (it && it.RigidbodySync) it.RigidbodySync.SetKinematic(false); // dropped where it hung
                    continue;
                }
                it.transform.position = HookHold(who);
                Siege.Synced(it);
            }
            foreach (var f in _flung.ToList())
            {
                if (!f.Item || Time.time > f.Until || f.Left <= 0) { _flung.Remove(f); continue; }
                Vector3 p = f.Item.transform.position;
                foreach (var c in Targets().Where(c => !f.Hit.Contains(c) && (Body(c) - p).magnitude <= FlingRadius * SizeOf(c)).Take(f.Left))
                {
                    f.Hit.Add(c);
                    f.Left--;
                    HitNumber(c, Hurt(c, f.Dmg, f.By));
                    float push = Mathf.Clamp(f.Item.Rig ? f.Item.Rig.mass * 1.5f : 3f, 2f, 9f);
                    if (!IsBoss(c)) Siege.Instance.ServerPush(c, f.Item.Rig ? f.Item.Rig.linearVelocity.Flat().normalized * push : Vector3.zero);
                    Fx.Send(FxKind.Dust, p, p);
                    AutoTest.Count("hook_fling_hit");
                }
            }
        }

        // A rocket bursts: everything within `radius` takes the blast (the effect is the rocket's own event, at its end).
        // Fire/explosions meeting a poison cloud burn it off: a burst in the cloud's place (Owen: elemental reactions).
        // The normal of the first ground/wall a shot meets (fortress pieces, the lighthouse and items don't count: Reach's rule).
        private static bool WallBounce(Vector3 a, Vector3 b, out Vector3 normal)
        {
            normal = Vector3.up;
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f) return false;
            foreach (var h in Physics.RaycastAll(a, d / len, len, GameInfo.LevelLayer).OrderBy(h => h.distance))
            {
                string n = h.collider.gameObject.name;
                if (n.StartsWith("FortressPiece") || n == "FortressCore" || ItemManager.Get(h.collider)) continue;
                normal = h.normal;
                return true;
            }
            return false;
        }

        private void IgnitePoison(Vector3 at, float radius, Player who)
        {
            List<Cloud> lit = null;
            foreach (var cl in _clouds)
                if ((cl.At - at).Flat().sqrMagnitude <= (radius + cl.Radius) * (radius + cl.Radius)) (lit ??= new List<Cloud>()).Add(cl);
            if (lit == null) return;
            foreach (var cl in lit)
            {
                _clouds.Remove(cl);
                foreach (var c in Targets().Where(x => (Body(x) - cl.At).Flat().sqrMagnitude <= cl.Radius * cl.Radius).ToList())
                    HitNumber(c, Hurt(c, cl.Dps * 2.5f, who));
                Fx.Send(FxKind.Blast, cl.At, cl.At);
                AutoTest.Count("poison_ignited");
            }
        }

        private void RocketBlast(Vector3 at, float dmg, Player who, float radius)
        {
            IgnitePoison(at, radius, who);
            foreach (var c in Targets().Where(x => (Body(x) - at).sqrMagnitude <= radius * radius))
            {
                // Frozen fish shatter (Owen: elemental reactions).
                HitNumber(c, Hurt(c, dmg * (Siege.Instance.IsFrozen(c) ? 1.5f : 1f), who));
                // Dynamite throws things; the rocket's blast does too (Owen): further out, less of a shove.
                if (IsBoss(c)) continue; // a boss holds its ground
                Vector3 d = Body(c) - at;
                float fall = 1f - Mathf.Clamp01(Mathf.Sqrt(d.sqrMagnitude) / Mathf.Max(0.01f, radius));
                Vector3 dir = d.Flat().sqrMagnitude > 0.01f ? d.Flat().normalized : Vector3.forward;
                // The blast's knockback grows with its modded radius (Owen: the shove was stuck at the old distance).
                float knock = Mathf.Clamp(radius / RocketRadius, 1f, 6f);
                Siege.Instance.ServerPush(c, dir * (2f + 4f * fall) * knock * Perks.Knockback); // 2 m at the rim, 6 m at the middle, times radius and soul knockback
                Siege.Instance.Slow(c, 0.3f * fall, 0.5f);
            }
            Fx.Send(FxKind.Blast, at, at, 0, Mathf.RoundToInt(radius * 100f)); // the explosion grows with the modded radius
            AutoTest.Count("rocket_blast");
        }

        // The pufferfish bursts: spines into everything round it, and two stacks of poison (which then works like the gas's).
        private void PufferBurst(Vector3 at, float dmg, Player who, int again, float radius)
        {
            IgnitePoison(at, radius, who);
            foreach (var c in Targets().Where(x => (x.transform.position - at).sqrMagnitude <= radius * radius))
            {
                HitNumber(c, Hurt(c, dmg * (Siege.Instance.IsFrozen(c) ? 1.5f : 1f), who));
                Envenom(c, 2, dmg, who);
                if (!IsBoss(c))
                {
                    Vector3 d = Body(c) - at; d.y = 0f;
                    float fall = 1f - Mathf.Clamp01(d.magnitude / Mathf.Max(0.01f, radius));
                    Vector3 dir = d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward;
                    Siege.Instance.ServerPush(c, dir * (1f + 2f * fall) * Mathf.Clamp(radius / PufferRadius, 1f, 6f) * Perks.Knockback);
                }
            }
            Fx.Send(FxKind.Puffer, at, at, 0, Mathf.RoundToInt(radius * 100f), who ? who.ObjectId : 0, "Burst"); // real burst radius
            AutoTest.Count("puffer_burst");
            if (again > 0) { Perks.Proc(Gear.PufferBomb, at); StartCoroutine(After(1f, () => PufferBurst(at, dmg, who, again - 1, radius))); }
        }

        // ---------- water cannon: spray while you hold the button; it pushes fish back until the tank runs dry ----------

        private void WaterTick(Player who, Gun gun, byte phase, Vector3 o, Vector3 d, float v, bool legend, Rarity r, int rate, int trait, float reach)
        {
            float now = Time.time;
            if (phase == 2) { SendState(who, gun); return; }
            if (phase == 0) AutoTest.Count("weapon_WaterCannon");
            float dt = now - gun.LastTick;
            gun.LastTick = now;
            if (phase == 1 && dt < 0.05f) { gun.LastTick = now - dt; return; }
            if (phase == 0 || dt > 1f) { gun.Heat = Mathf.Max(0f, gun.Heat - HeatCoolPerSecond * Mathf.Min(dt, 10f)); dt = 0.1f; } // the tank refills while it rests
            if (gun.Overheated) { if (now < gun.ReadyAt) return; gun.Overheated = false; gun.Heat = 0.3f; gun.RampStart = now; }
            dt = Mathf.Min(dt, 0.5f);
            gun.Heat += dt / (WaterTankSeconds[(int)r] + WeaponMods.TraitAdd(Gear.WaterCannon, trait)); // the trait: a bigger tank
            if (gun.Heat >= 1f) { gun.Heat = 1f; gun.Overheated = true; gun.ReadyAt = now + OverheatLock; SendState(who, gun); return; }
            Vector3 end = Reach(o, d, WaterRange * reach), fwd = d.Flat().normalized;
            float s = Scale, pour = 1f / WeaponMods.TimeMult(rate); // its fire rate: it pours out faster
            float push = Mathf.Min(WaterPushMax, v * pour * (legend ? 1.5f : 1f));
            Creature first = null;
            foreach (var c in Along(o, end, WaterRadius))
            {
                if (!first) first = c;
                _wet[c] = now + WetSeconds;
                gun.NumberDmg += Hurt(c, v * pour * WaterHurt * s * dt, who);
                if (IsBoss(c)) { Siege.Instance.Slow(c, 0.3f, 0.4f); continue; }
                Siege.Instance.ServerPush(c, fwd * push * dt * 2.5f * Perks.Knockback); // the push lands over 0.25 s
                if (legend) Siege.Instance.Slow(c, 0.5f, 0.5f);
            }
            Fx.Send(FxKind.Water, Muzzle(who, o, d), end, 0, 0, who.ObjectId, "WaterCannon");
            // The spray adds up: show it on the first fish hit, a few times a second (the numbers were never flushed).
            if (now >= gun.NextNumber && gun.NumberDmg > 0f && first)
            {
                HitNumber(first, Mathf.RoundToInt(gun.NumberDmg));
                gun.NumberDmg = 0f; gun.NextNumber = now + 0.4f;
            }
            if (now >= gun.NextState) { gun.NextState = now + 0.25f; SendState(who, gun); }
        }

        // A bubble bursting (a legendary's, or one with the trait mod) splashes the fish around.
        private static void Pop(Vector3 at, float dmg, Player who, bool legend)
        {
            foreach (var c in Targets().Where(x => (x.transform.position - at).sqrMagnitude <= 3f * 3f)) HitNumber(c, Hurt(c, dmg, who));
            Fx.Send(FxKind.Bubble, at, at, 0, 0, who ? who.ObjectId : 0);
            if (legend) Perks.Proc(Gear.BubbleGun, at);
        }

        // ---------- laser: a beam while you hold the button; it heats up, and bites harder the longer it stays on one fish ----------

        // Held legendary at these levels on every line: the beam's colour is white-hot (BeamHeat's top).
        public const int BeamTop = 20;
        public static float BeamMaxBite => WeaponMods.PowerMult(BeamTop) * (LaserMaxRamp + WeaponMods.TraitAdd(Gear.Laser, BeamTop)) / WeaponMods.TimeMult(BeamTop);

        private void LaserTick(Player who, Gun gun, byte phase, Vector3 o, Vector3 d, float v, bool legend, Rarity r, int rate, int trait, float reach)
        {
            float now = Time.time;
            if (phase == 2) { SendState(who, gun); return; }
            if (phase == 0) AutoTest.Count("weapon_Laser");
            float dt = now - gun.LastTick;
            gun.LastTick = now;
            if (phase == 1 && dt < 0.05f) { gun.LastTick = now - dt; return; } // a flood of messages: the time still counts at the next one
            if (phase == 0 || dt > 1f) { gun.Heat = Mathf.Max(0f, gun.Heat - HeatCoolPerSecond * Mathf.Min(dt, 10f)); gun.RampStart = now; dt = 0.1f; } // cooled off since the last burst: it heats up from x1
            if (gun.Overheated) { if (now < gun.ReadyAt) return; gun.Overheated = false; gun.Heat = 0.3f; }
            dt = Mathf.Min(dt, 1f); // up to a second of a friend's beam held up on the way still lands
            gun.Heat += dt / LaserHeatSeconds[(int)r];
            if (gun.Heat >= 1f)
            {
                gun.Heat = 1f; gun.Overheated = true; gun.ReadyAt = now + OverheatLock;
                SendState(who, gun);
                return;
            }
            Vector3 end = Reach(o, d, LaserRange * reach);
            var hits = Along(o, end, LaserRadius);
            var first = hits.FirstOrDefault();
            // The longer the beam's been on (on whatever it sweeps over), the harder it bites. It used to start over on every
            // new fish in front, which in a crowd was all the time: a maxed legendary did a fraction of its paper damage (Owen).
            float maxRamp = LaserMaxRamp + WeaponMods.TraitAdd(Gear.Laser, trait), ramp = LaserRamp(now - gun.RampStart, maxRamp);
            float pour = 1f / WeaponMods.TimeMult(rate); // its fire rate: the beam pulses faster
            float dmg = v * Scale * ramp * pour * dt;
            foreach (var c in legend ? hits : hits.Take(1))
            {
                gun.NumberDmg += Hurt(c, dmg, who);
                if (legend && hits.Count > 1) Perks.Proc(Gear.Laser, c.transform.position);
            }
            float bite = v / GearCatalog.Value(Gear.Laser, r) * ramp * pour;
            Fx.Send(FxKind.Beam, Muzzle(who, o, d), first && !legend ? Body(first) : end, Mathf.RoundToInt(BeamShown(r, bite, BeamMaxBite, ramp >= maxRamp) * 100f), Mathf.RoundToInt(ramp * 100f), who.ObjectId); // PieceId: its heat x100 (the colour)
            // Damage is continuous, but the floating number should pulse with the beam, not sit on the screen for half a second.
            float pulse = Mathf.Clamp(0.22f * WeaponMods.TimeMult(rate), 0.08f, 0.22f);
            if (now >= gun.NextNumber && gun.NumberDmg > 0f && first) { HitNumber(first, Mathf.RoundToInt(gun.NumberDmg)); gun.NumberDmg = 0f; gun.NextNumber = now + pulse; }
            if (now >= gun.NextState) { gun.NextState = now + 0.25f; SendState(who, gun); }
        }

        // ---------- gatling: hold it and the barrels spin up, firing faster and faster at the first fish in its way ----------

        // Owen: "加特林…嘟嘟嘟嘟嘟嘟 一直按住". Its Heat is how far the barrels are spun up (the HUD's bar): a quarter of the
        // top rate as they start, all of it once they're up (its trait mod: sooner); let go and they run down over
        // GatlingSpinDown, so the next burst starts where that left them. A legendary's bullets glance off into the nearest
        // other fish for half.
        private void GatlingTick(Player who, Gun gun, byte phase, Vector3 o, Vector3 d, float v, bool legend, Rarity rarity, int rate, int trait, float reach)
        {
            float now = Time.time, dt = now - gun.LastTick;
            if (phase == 2) { gun.LastTick = now; SendState(who, gun); return; } // let go: they run down from here
            if (phase == 1 && dt < 0.05f) return; // a flood of messages: the time still counts at the next one
            gun.LastTick = now;
            if (phase == 0 || dt > 1f)
            {
                gun.Heat = Mathf.Max(0f, gun.Heat - Mathf.Min(dt, 10f) / GatlingSpinDown);
                if (dt > 0.3f) gun.Rounds = Mathf.Max(gun.Rounds, 1f); // the first bullet on the click (not one a click faster than it spins)
                dt = 0.1f;
                AutoTest.Count("weapon_Gatling");
            }
            dt = Mathf.Min(dt, 0.5f);
            gun.Heat = Mathf.Min(1f, gun.Heat + dt / Mathf.Max(0.15f, GatlingSpinUp - WeaponMods.TraitAdd(Gear.Gatling, trait)));
            gun.Rounds += GatlingShots(gun.Heat) / WeaponMods.TimeMult(rate) * dt;
            int shots = (int)gun.Rounds; // the barrels fire together; the fraction carries to the next tick
            gun.Rounds -= shots;
            shots *= GatlingBarrels(rarity);
            Vector3 end = Reach(o, d, GatlingRange * reach);
            Creature first = null, lastGlance = null;
            bool glanced = false;
            if (shots > 0)
            {
                var hits = Along(o, end, ShotRadius + GatlingSpread + 1f); // everything a cylinder can reach, once
                float dmg = v * Scale;
                int barrels = GatlingBarrels(rarity), bounces = 0;
                for (int i = 0; i < shots; i++)
                {
                    // Each barrel fires along the aim from its own place in the honeycomb, with a little jitter.
                    Vector3 off = GatlingOffset(i % barrels, barrels, d) + Random.insideUnitSphere * 0.03f;
                    Vector3 bo = o + off, be = bo + (end - o);
                    Creature c = null;
                    for (int j = 0; j < hits.Count; j++)
                        if (hits[j] && hits[j]._hp.Value > 0 && Touches(hits[j], bo, be, ShotRadius)) { c = hits[j]; break; }
                    if (!c) continue;
                    if (c != first) first = c;
                    gun.NumberDmg += Hurt(c, dmg, who);
                    if (legend && bounces < 8 / Mathf.Max(1, (int)rarity) && Random.value < 0.35f)
                    {
                        var glance = Targets().Where(x => x != c && x._hp.Value > 0 && (x.transform.position - c.transform.position).sqrMagnitude < GatlingBounce * GatlingBounce)
                                              .OrderBy(x => (x.transform.position - c.transform.position).sqrMagnitude).FirstOrDefault();
                        if (glance) { gun.NumberDmg += Hurt(glance, dmg * 0.5f, who); bounces++; glanced = true; lastGlance = glance; }
                    }
                }
            }
            // PieceId: the bullets (0 while it's still spinning up), Arg: how far it's spun up (%), for everyone's view of it.
            Fx.Send(FxKind.Gatling, Muzzle(who, o, d), first ? Body(first) : end, shots, Mathf.RoundToInt(gun.Heat * 100f), who.ObjectId);
            if (glanced && first && lastGlance) { Fx.Send(FxKind.Gatling, Body(first), Body(lastGlance), -1, 0, who.ObjectId); Perks.Proc(Gear.Gatling, lastGlance.transform.position); }
            if (now >= gun.NextNumber && gun.NumberDmg > 0f && first) { HitNumber(first, Mathf.RoundToInt(gun.NumberDmg)); gun.NumberDmg = 0f; gun.NextNumber = now + 0.5f; }
            if (now >= gun.NextState) { gun.NextState = now + 0.25f; SendState(who, gun); }
        }

        // ---------- boomerang ----------

        private IEnumerator BoomerangFlight(Vector3 a, Vector3 b, float v, Player who, float slow)
        {
            var hit = new HashSet<Creature>();
            bool back = false;
            for (float t = 0f; t < BoomerangLeg * 2f; t += Time.deltaTime)
            {
                if (!back && t > BoomerangLeg) { back = true; hit.Clear(); } // each fish can be hit again on the way back
                Vector3 p = BoomerangAt(a, b, t);
                if (Time.time >= _nextBoomerangPick) { _nextBoomerangPick = Time.time + 0.12f; if (Loot.Instance) Loot.Instance.ServerBoomerangPick(p, 1.8f); }
                foreach (var c in Targets())
                    if (!hit.Contains(c) && Touches(c, p, BoomerangHitRadius))
                    {
                        hit.Add(c);
                        HitNumber(c, Hurt(c, v * Scale, who));
                        if (slow > 0f) Siege.Instance.Slow(c, BoomerangSlow, slow); // the trait
                    }
                yield return null;
            }
        }

        // ---------- shark ----------

        // A spot in the sea near `p`: at least SeaRing from the island's middle, on the water.
        private static Vector3 OnSea(Vector3 p)
        {
            var island = FortressIsland.Instance;
            if (island && island.Exists)
            {
                Vector3 c = island.Center, off = (p - c).Flat();
                if (off.sqrMagnitude < 0.01f) off = Vector3.forward;
                p = c + off.normalized * Mathf.Clamp(off.magnitude, SeaRing, SeaRing + 8f);
            }
            p.y = WaterManager.GetWaterHeight(p);
            return p;
        }

        private static void SendShark(Shark sh, bool alive) => Fx.Send(FxKind.Shark, sh.Pos, sh.Face, sh.Id, alive ? 1 : 0, sh.Owner, sh.Size.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

        // Where a whistle's sharks come up: on the pool, a legendary's second one beside it.
        public static Vector3[] SharkSpots(Vector3 at, Rarity r) =>
            r == Rarity.Legendary ? new[] { at, at + new Vector3(1.8f * 0.8660254f, 0f, -1.8f * 0.5f) } : new[] { at };

        // A shark's next spot: toward `goal`, never further than `leash` from the pool it came up in.
        public static Vector3 SharkStep(Vector3 pos, Vector3 home, Vector3 goal, float leash, float step)
        {
            Vector3 next = Vector3.MoveTowards(pos, goal, step), off = (next - home).Flat();
            if (off.sqrMagnitude > leash * leash) next = home + off.normalized * leash + Vector3.up * (next.y - home.y);
            return next;
        }

        // Its surface: the sea, or the ground its pool has opened in.
        private static float SharkSurface(Vector3 p) => Mathf.Max(WaterManager.GetWaterHeight(p), Loot.Ground(p + Vector3.up * 3f, 20f, out var g) ? g.y : float.MinValue);

        private void SharkTick(float now)
        {
            foreach (var sh in _sharks.ToList())
            {
                if (now > sh.Until) { _sharks.Remove(sh); SendShark(sh, false); continue; }
                // Any fish in its pool's reach, on the sand or in the sea; nothing about: back to the pool.
                var prey = Targets().Where(c => (c.transform.position - sh.Home).Flat().sqrMagnitude < sh.Sight * sh.Sight)
                                    .OrderBy(c => (c.transform.position - sh.Pos).Flat().sqrMagnitude).FirstOrDefault();
                if (now >= sh.Landed) // not while it's still in the air
                {
                    sh.Pos = SharkStep(sh.Pos, sh.Home, prey ? prey.transform.position : sh.Home, sh.Sight, SharkSpeed * Time.deltaTime);
                    sh.Pos.y = SharkSurface(sh.Pos);
                }
                if (!prey) continue;
                sh.Face = prey.transform.position;
                if ((prey.transform.position - sh.Pos).Flat().magnitude <= SharkReach && now >= sh.NextBite)
                {
                    sh.NextBite = now + SharkBiteEvery;
                    var owner = PlayerById(sh.Owner);
                    HitNumber(prey, Hurt(prey, sh.Dmg * Scale * (IsSpitter(prey) ? 3f : 1f), owner));
                    Fx.Send(FxKind.WeaponHit, sh.Pos, prey.transform.position, sh.Id, 0, sh.Owner); // the chomp (no number)
                    AutoTest.Count("shark_bite");
                }
            }
            if (now >= _nextSharkSend) { _nextSharkSend = now + 0.2f; foreach (var sh in _sharks) SendShark(sh, true); }
        }

        // ---------- hook ----------

        // `stun`: seconds it's held still once it's pulled in (the trait mod).
        private static void HookFish(Creature c, Player who, float dmg, Vector3 to, float stun)
        {
            HitNumber(c, Hurt(c, dmg, who));
            int loot = Siege.Instance.ServerTakeCarried(c); // a thief lets go of what it stole
            if (loot > 0) { Siege.Instance.ServerAddCoins(loot); Fx.Announce("thiefHooked", loot); AutoTest.Count("thief_hooked"); }
            if (IsBoss(c)) { Siege.Instance.Slow(c, 0.5f, 1f); return; } // too heavy to pull
            bool spitter = IsSpitter(c);
            if (spitter)
            {
                // Out of the deep water toward the beach, but it stays in the water; stunned so it can't spit for a bit.
                Vector3 from = c.transform.position, step = (to - from).Flat();
                to = OnSea(from + step.normalized * Mathf.Min(8f, step.magnitude));
                Siege.Instance.Slow(c, 1f, 2f);
            }
            Siege.Instance.ServerPull(c, to, 0.35f, spitter);
            if (stun > 0f) Siege.Instance.Slow(c, 1f, 0.35f + stun);
        }

        // ---------- clouds, whirlpools: half-second ticks ----------

        private void HalfSecondTick(float now)
        {
            float s = Scale;
            // Poison: every second in a cloud adds a stack (up to 5); a poisoned fish keeps taking damage a while after leaving.
            _clouds.RemoveAll(c => now > c.Until);
            if (_clouds.Count > 0)
                foreach (var c in Targets())
                {
                    // A cloud is a volume, not a floor circle: a fish flying over it stays clean (Owen: "毒的判定範圍").
                    var cl = _clouds.FirstOrDefault(x => InCloud(c, x));
                    if (cl == null) continue;
                    if (!_poison.TryGetValue(c, out var p)) _poison[c] = p = new Poison();
                    if (now >= p.NextStack) { p.Stacks = Mathf.Min(MaxPoisonStacks, p.Stacks + 1); p.NextStack = now + 1f; }
                    p.Until = now + PoisonLinger; p.Dps = Mathf.Max(p.Dps, cl.Dps); p.Owner = cl.Owner; p.Legend |= cl.Legend;
                    Siege.Instance.Slow(c, Mathf.Min(0.35f, 0.07f * p.Stacks), 0.7f); // each stack a little slower
                }
            foreach (var kv in _poison.ToList())
            {
                var c = kv.Key; var p = kv.Value;
                if (!c || now > p.Until) { _poison.Remove(c); continue; }
                if (c._hp.Value > 0)
                {
                    int dmg = Mathf.Max(1, Mathf.RoundToInt(p.Dps * p.Stacks * s * 0.5f));
                    Hurt(c, dmg, PlayerById(p.Owner));
                    if (now >= p.NextNumber) { p.NextNumber = now + 1f; HitNumber(c, Mathf.RoundToInt(p.Dps * p.Stacks * s)); }
                }
                if (c._hp.Value <= 0)
                {
                    _poison.Remove(c);
                    if (p.Legend) // legendary: the body bursts into a fresh cloud (or tops up the burst one it fell in: they piled up by the dozen)
                    {
                        Vector3 at = c.transform.position;
                        var burst = _clouds.FirstOrDefault(x => !x.Legend && (at - x.At).Flat().sqrMagnitude <= x.Radius * x.Radius);
                        if (burst != null) { burst.Until = Mathf.Max(burst.Until, now + 3f); burst.Dps = Mathf.Max(burst.Dps, p.Dps); }
                        else _clouds.Add(new Cloud { At = at, Radius = 2f, Until = now + 3f, Dps = p.Dps, Owner = p.Owner, Legend = false });
                        Fx.Send(FxKind.Gas, at, at, 0, 3000, p.Owner);
                        Perks.Proc(Gear.PoisonGas, at);
                    }
                }
            }
            // Jellyfish: sting and slow what's under them; a legendary one splits into three small ones when it's done.
            foreach (var j in _jellies.ToList())
            {
                var owner = PlayerById(j.Owner);
                if (now > j.Until)
                {
                    _jellies.Remove(j);
                    if (j.Legend)
                        for (int i = 0; i < 3; i++)
                        {
                            Vector3 at = j.At + Quaternion.Euler(0f, i * 120f, 0f) * Vector3.forward * 2.2f;
                            _jellies.Add(new Jelly { At = at, Radius = JellyRadius * 0.65f, Until = now + 3f, Dps = j.Dps * 0.5f, Owner = j.Owner });
                            Fx.Send(FxKind.Jelly, j.At + Vector3.up * JellyHover, at, 0, 3000, j.Owner);
                            Perks.Proc(Gear.JellyGun, at);
                        }
                    continue;
                }
                Creature first = null;
                foreach (var c in Targets().Where(x => (x.transform.position - j.At).Flat().sqrMagnitude <= j.Radius * j.Radius))
                {
                    if (!first) first = c;
                    j.NumberDmg += Hurt(c, j.Dps * s * 0.5f * (IsWet(c) ? WetMult : 1f), owner);
                    Siege.Instance.Slow(c, JellySlow, 0.7f);
                }
                if (first && now >= j.NextNumber && j.NumberDmg > 0f) { HitNumber(first, Mathf.RoundToInt(j.NumberDmg)); j.NumberDmg = 0f; j.NextNumber = now + 0.5f; }
            }
            foreach (var kv in _netted.Where(kv => !kv.Key || now > kv.Value).ToList()) _netted.Remove(kv.Key);
            foreach (var kv in _wet.Where(kv => !kv.Key || now > kv.Value).ToList()) _wet.Remove(kv.Key);
            foreach (var c in _wet.Keys) Fx.Send(FxKind.Drip, Body(c), Body(c));
            // Whirlpools: light damage to everything they hold; a legendary one bursts when it ends.
            foreach (var w in _vortices.ToList())
            {
                var inside = Targets().Where(c => (c.transform.position - w.At).Flat().sqrMagnitude <= WhirlRadius * WhirlRadius).ToList();
                var owner = PlayerById(w.Owner);
                if (now > w.Until)
                {
                    _vortices.Remove(w);
                    if (w.Legend)
                    {
                        foreach (var c in inside) HitNumber(c, Hurt(c, WhirlBurst * s, owner));
                        Fx.Send(FxKind.Blast, w.At, w.At);
                        Perks.Proc(Gear.Whirlpool, w.At);
                    }
                    continue;
                }
                Creature first = null;
                foreach (var c in inside)
                {
                    if (!first) first = c;
                    w.NumberDmg += Hurt(c, w.Dps * s * 0.5f, owner);
                    if (IsBoss(c)) Siege.Instance.Slow(c, 0.5f, 0.6f);
                }
                if (first && now >= w.NextNumber && w.NumberDmg > 0f) { HitNumber(first, Mathf.RoundToInt(w.NumberDmg)); w.NumberDmg = 0f; w.NextNumber = now + 0.5f; }
            }
        }

        // Host, from Siege.Steer: a fish caught in a whirlpool is dragged to its middle and does nothing else (bosses are only slowed).
        public bool ServerPull(RunningFish fish, Rigidbody rig)
        {
            if (_vortices.Count == 0) return false;
            Vector3 pos = fish.transform.position;
            var w = _vortices.FirstOrDefault(x => (pos - x.At).Flat().sqrMagnitude <= WhirlRadius * WhirlRadius);
            if (w == null) return false;
            Vector3 to = (w.At - pos).Flat();
            float speed = to.magnitude < 0.8f ? 0f : 4f;
            Vector3 vel = to.sqrMagnitude > 0.01f ? to.normalized * speed : Vector3.zero;
            vel.y = rig.linearVelocity.y;
            rig.linearVelocity = vel;
            rig.angularVelocity = Vector3.zero;
            fish.transform.Rotate(0f, 360f * Time.deltaTime, 0f); // spun round
            Siege.Synced(fish);
            return true;
        }

        // Wet, from the water cannon (Owen agreed: it and the conch both just pushed fish back): for WetSeconds the tesla
        // tower's zaps and the jellyfish's stings bite half again. Wet fish drip (FxKind.Drip).
        public const float WetSeconds = 5f, WetMult = 1.5f;
        private readonly Dictionary<Creature, float> _wet = new Dictionary<Creature, float>();
        public static bool IsWet(Creature c) => Instance && c && Instance._wet.TryGetValue(c, out float until) && Time.time <= until;

        public static bool IsNetted(Creature c) => Instance && c && Instance._netted.TryGetValue(c, out float until) && Time.time <= until;

        private static bool InCloud(Creature c, Cloud x)
        {
            Vector3 d = c.transform.position - x.At;
            return d.Flat().sqrMagnitude <= x.Radius * x.Radius && Mathf.Abs(d.y) <= 2.4f; // the dome over the ground
        }
        public static bool IsPoisoned(Creature c) => Instance && c && Instance._poison.TryGetValue(c, out var p) && p.Stacks > 0 && Time.time <= p.Until;

        private void ServerTick()
        {
            bool active = Siege.Instance && Siege.Instance.ServerActive;
            if (!active && _wasActive) // round over: what was out in it goes (the weapons themselves work round or not)
            {
                foreach (var sh in _sharks) SendShark(sh, false);
                _sharks.Clear(); _clouds.Clear(); _poison.Clear(); _bubbleDamage.Clear(); _vortices.Clear(); _guns.Clear(); _jellies.Clear(); _netted.Clear();
            }
            _wasActive = active;
            float now = Time.time;
            if (_sharks.Count > 0) SharkTick(now);
            if (_hooked.Count > 0 || _flung.Count > 0) CatchTick();
            if (now >= _nextHalfSecond) { _nextHalfSecond = now + 0.5f; HalfSecondTick(now); }
        }

        private static IEnumerator After(float seconds, System.Action then)
        {
            yield return new WaitForSeconds(seconds);
            then();
        }

        // =====================================================================================================
        // client
        // =====================================================================================================

        private bool _drawn, _wasHolding, _handEmptied;
        private Item _heldAtDraw; // what was in hand when the net came out (it went into the hotbar; taking anything out puts the net away)

        private static readonly System.Reflection.MethodInfo OpenSlotMethod = AccessTools.Method(typeof(PlayerInventory), "GetOpenSlot");
        private static int OpenSlot(PlayerInventory inv)
        {
            try { return OpenSlotMethod != null ? (int)OpenSlotMethod.Invoke(inv, new object[] { -1 }) : -1; } catch { return -1; }
        }
        private float _kick, _nextSend;
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private Item _hiddenItem;

        public bool ClientHasWeapon => WeaponItems.Carries(Player.LocalPlayer);

        private void ClientTick()
        {
            var me = Player.LocalPlayer;
            bool alive = me && PlayerManager.AlivePlayers.Contains(me);
            var island = FortressIsland.Instance;
            bool ashore = me && me.Transform && island && island.Exists && island.OnIsland(me.Transform.position, FortressIsland.IslandRadius + 6f);
            // The hand net (F) goes away when you leave the island, go down, open a menu, or take something out of the hotbar.
            var inHand = me && me.Holding ? me.Holding.HeldItem : null;
            if (_drawn && !inHand) _handEmptied = true;
            if (_drawn && (!ashore || !alive || Hud.ModalOpen || (inHand && (inHand != _heldAtDraw || _handEmptied)))) Holster();
            var kb = Keyboard.current;
            bool keys = kb != null && !ChatManager.IsTyping && !Hud.ModalOpen && InstanceFinder.IsClientStarted;
            if (keys && ashore && NetKey != null && kb[NetKey.Value].wasPressedThisFrame) // the net works on the island, round or not
            {
                if (_drawn) Holster(true);
                else if (alive) Draw(true);
            }
            // A fortress weapon is an item in your hand (E, 1-9, Q, like the game's guns); it fires anywhere, round or not.
            Gear hg = default; Rarity hr = default;
            bool armed = !_drawn && InstanceFinder.IsClientStarted && alive && WeaponItems.Held(me, out hg, out hr);
            if (_drawn)
            {
                HideHeld();
                if (me.Hands) me.Hands.ToggleHandMeshes(false); // the net floats in view on its own: no empty game hands under it
                UpdateNetView();
                HandleNetMouse();
            }
            else if (armed && !GearCatalog.IsGameGun(hg)) { HandleMouse(hg == Gear.Absorber); KickHeld(); } // the game's guns shoot themselves
            else if (_wasHolding) { Send(2); _wasHolding = false; }
            GatlingViews(armed && hg == Gear.Gatling && _wasHolding);
            if (armed && GearCatalog.IsGameGun(hg) && inHand is Weapon gun) WeaponMods.TopUp(gun); // a fresh one: its full, bigger magazine
            DrawHud(armed ? (hg, hr) : ((Gear, Rarity)?)null);
            CastIndicator(armed ? hg : (Gear?)null, hr);
            AbsorberViews();
            AnimateViews();
        }

        public void Draw(bool net)
        {
            if (_drawn) Stow();
            _net = true; // only the hand net is drawn now; weapons are items
            _drawn = true;
            var me = Player.LocalPlayer;
            _heldAtDraw = me && me.Holding ? me.Holding.HeldItem : null;
            _handEmptied = false;
            // Like switching hotbar slots: what's in your hand goes back into the hotbar (with no room it stays in hand, out of
            // sight, rather than being dropped), so both hands are free for the net.
            if (_heldAtDraw && me.Inventory && (me.Inventory.HasItemInInventory(_heldAtDraw) || OpenSlot(me.Inventory) >= 0))
                me.Inventory.LocalTrySelectSlot(-1);
            if (BuildSystem.Instance) BuildSystem.Instance.StopBuilding();
            Fx.Sound("EquipItem", Player.LocalPlayer ? Player.LocalPlayer.Transform.position : Vector3.zero, 0.7f, AudioDistance.Short);
            AutoTest.Count("weapon_drawn");
        }

        // `back`: F again, so what you had in hand comes back out of the hotbar.
        public void Holster(bool back = false)
        {
            if (!_drawn) return;
            Stow();
            foreach (var r in _hidden) if (r) r.forceRenderingOff = false;
            _hidden.Clear();
            _hiddenItem = null;
            RestoreHands();
            var me = Player.LocalPlayer;
            if (back && _handEmptied && _heldAtDraw && me && me.Holding && !me.Holding.HeldItem && me.Inventory)
                foreach (var kv in me.Inventory._items)
                    if (kv.Value == _heldAtDraw) { me.Inventory.LocalTrySelectSlot(kv.Key); break; }
            Fx.Sound("UnequipItem", Player.LocalPlayer ? Player.LocalPlayer.Transform.position : Vector3.zero, 0.7f, AudioDistance.Short);
        }

        // The game's hands come back the way the game would show them for what you hold (its own tool hands, or none while dead).
        private static System.Reflection.MethodInfo _toolHands;
        private static void RestoreHands()
        {
            var me = Player.LocalPlayer;
            if (!me || !me.Hands || !PlayerManager.AlivePlayers.Contains(me)) return;
            if (_toolHands == null) _toolHands = typeof(PlayerHands).GetMethod("ToggleToolHands", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            try { if (_toolHands != null) { _toolHands.Invoke(me.Hands, new object[] { true }); return; } } catch { }
            me.Hands.ToggleHandMeshes(true);
        }

        // Put the tool in hand away (to holster, or to take the other one out).
        private void Stow()
        {
            _drawn = false;
            if (_netView) _netView.SetActive(false);
        }

        // The game item in your hand is put out of sight while the weapon is out (only on your screen; it can't be used meanwhile).
        private void HideHeld()
        {
            var me = Player.LocalPlayer;
            var held = me && me.Holding ? me.Holding.HeldItem : null;
            if (held == _hiddenItem) return;
            _hiddenItem = held;
            if (!held) return;
            foreach (var r in held.GetComponentsInChildren<Renderer>())
                if (!r.forceRenderingOff) { r.forceRenderingOff = true; _hidden.Add(r); }
        }

        private void Send(byte phase)
        {
            var cam = GameInfo.CurCamera;
            if (!cam || !InstanceFinder.IsClientStarted) return;
            InstanceFinder.ClientManager.Broadcast(new WeaponFire { Phase = phase, Origin = cam.transform.position, Dir = cam.transform.forward });
        }

        private void HandleMouse(bool absorber)
        {
            var mouse = Mouse.current;
            bool can = mouse != null && Cursor.lockState == CursorLockMode.Locked && !ChatManager.IsTyping && !BuildSystem.Building && !Hud.ModalOpen;
            if (absorber) AbsorberKeys(can);
            bool down = can && mouse.leftButton.isPressed;
            if (down && (!_wasHolding || Time.time >= _nextSend))
            {
                Send(_wasHolding ? (byte)1 : (byte)0);
                _nextSend = Time.time + 0.1f;
            }
            else if (!down && _wasHolding) Send(2);
            _wasHolding = down;
        }

        // The fortress weapon item in your hand: our model on it, kicked back a little when it fires.
        private Item _heldModelOf;
        private Transform _heldModel;
        private Vector3 _heldModelHome;

        private Transform HeldModel()
        {
            var held = Player.LocalPlayer && Player.LocalPlayer.Holding ? Player.LocalPlayer.Holding.HeldItem : null;
            if (held != _heldModelOf)
            {
                _heldModelOf = held;
                _heldModel = WeaponItems.ModelOf(held);
                if (_heldModel) _heldModelHome = _heldModel.localPosition;
            }
            return _heldModel;
        }

        private void KickHeld()
        {
            var m = HeldModel();
            _kick = Mathf.MoveTowards(_kick, 0f, Time.deltaTime * 5f);
            if (m) m.position = m.parent.TransformPoint(_heldModelHome) - m.forward * 0.06f * _kick; // 6 cm back (the bone is scaled down)
        }

        // Where my own shots start on my screen: the muzzle of the weapon in my hand.
        private Vector3 MyMuzzle(Vector3 fallback)
        {
            var m = HeldModel();
            return m && WeaponItems.Is(_heldModelOf, out var g, out _) ? WeaponItems.MuzzleOf(m, g) : fallback;
        }

        // Heat and cooldown, weapon by weapon (you can carry several).
        private readonly Dictionary<Gear, (float heat, float readyAt, float cooldown)> _mine = new Dictionary<Gear, (float, float, float)>();

        private void OnState(WeaponState s)
        {
            if (!Player.LocalPlayer || s.PlayerId != Player.LocalPlayer.ObjectId) return;
            _mine.TryGetValue(s.Gear, out var st);
            st.heat = s.Heat;
            if (s.ReadyIn > 0.05f) { st.readyAt = Time.time + s.ReadyIn; st.cooldown = s.ReadyIn; }
            _mine[s.Gear] = st;
        }

        // ---------- HUD: name, keys and a bar (laser heat, or the cooldown) while the weapon is out ----------

        private UnityEngine.UI.Image _hud, _hudPic;
        private TextMeshProUGUI _hudText;
        private Hud.Bar _hudBar;
        private float _hudShownAt = -10f;
        private (Gear, Rarity)? _hudShownGear;
        private bool _hudCompact;
        private const float HudPic = 64f; // room for the picture

        // What a weapon actually does a second with this player's mods and this wave (the card used to show the bare value:
        // Owen "DPS 計算好像每把槍顯示的都不太準").
        public static int DpsNow(ulong key, Gear g, Rarity r, bool atFull = false)
        {
            var v = Loot.Instance ? (InstanceFinder.IsServerStarted ? Loot.Instance.Vault : Loot.Instance.ClientVault) : null;
            if (v == null) return 0;
            float val = v.WeaponValue(key, g, r, v.BestStar(key, g, r)) * (DamageMultiplier != null ? DamageMultiplier.Value : 1f);
            float pour = 1f / WeaponMods.TimeMult(v.ModOf(key, g, WeaponMods.Rate));
            if (g == Gear.Gatling) return Mathf.RoundToInt(val * GatlingRate * GatlingBarrels(r) * pour);
            if (g == Gear.Laser) return Mathf.RoundToInt(val * pour * (atFull ? 3f : 2f)); // mid ramp, or the top of it
            float cd = GearCatalog.Cooldown(g, r);
            if (cd <= 0.01f || g == Gear.WaterCannon) return 0; // held sprays and lingering clouds say their own numbers
            return Mathf.RoundToInt(val / (cd / pour));
        }

        private void DrawHud((Gear g, Rarity r)? eq)
        {
            bool show = (_drawn || eq != null) && Hud.Font && !Hud.ModalOpen && !BuildSystem.Building;
            if (!show || _drawn || eq.Value.g != Gear.Absorber || !Loot.Instance) AbsorberTop(null);
            if (!show) { if (_hud) _hud.gameObject.SetActive(false); return; }
            if (!_hud)
            {
                // The tool's picture on the left; its name, keys and bar beside it.
                _hud = Hud.Box(Hud.Root, new Vector2(0.5f, 0f), new Vector2(0f, 232f), new Vector2(560f, 70f), Hud.Panel, 18f);
                _hudPic = Hud.PictureSlot(_hud.transform, new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(58f, 58f));
                _hudText = Hud.Label(_hud.transform, "", 21f, new Vector2(0.5f, 1f), new Vector2(HudPic / 2f, -8f), new Vector2(1200f, 32f), TextAlignmentOptions.Top);
                _hudBar = Hud.Bar.Make(_hud.transform, new Vector2(0.5f, 0f), new Vector2(HudPic / 2f, 12f), new Vector2(400f, 10f), Hud.Accent);
            }
            _hud.gameObject.SetActive(true);
            if (_drawn) { Hud.ShowPicture(_hudPic, "GearBigNet"); NetHud(); return; }
            var (g, r) = eq.Value;
            if (!_hudShownGear.HasValue || _hudShownGear.Value != (g, r)) { _hudShownGear = (g, r); _hudShownAt = Time.time; }
            bool compact = Time.time - _hudShownAt > 3f; // the card is for the moment you switch, not a forever overlay
            if (_hudCompact != compact) { _hudCompact = compact; if (!compact) _hudText.text = ""; }
            _hudText.gameObject.SetActive(!compact);
            _hudPic.gameObject.SetActive(!compact);
            if (compact) _hud.rectTransform.sizeDelta = new Vector2(470f, 32f);
            Hud.ShowPicture(_hudPic, "Gear" + g);
            // Its name and keys, then what it does at this rarity (with your mods), and a legendary's special.
            var vault = Loot.Instance ? Loot.Instance.ClientVault : null;
            string banner = null;
            string text = Hud.Keys($"<color=#{GearCatalog.Hex(r)}><b>{GearCatalog.Name(g)}</b></color>　{L10n.T(g == Gear.Absorber ? "absorberKeys" : "weaponKeys")}")
                          + $"\n<size=78%><color=#c9d1db>{(g == Gear.Absorber && vault != null ? AbsorberLines(r, out banner) : vault != null ? vault.EffectFor(Loot.MyKey, g, r) : GearCatalog.Effect(g, r))}</color></size>"
                          + (vault != null && WeaponMods.Summary(vault, Loot.MyKey, g) is string mods && mods.Length > 0 ? $"\n<size=78%><color={Hud.Hex(Hud.Gold)}>{L10n.T("modCard", mods)}</color></size>" : "")
                          + (r == Rarity.Legendary ? $"\n<size=78%><color=#{GearCatalog.Hex(r)}>★ {L10n.T("gearLegend" + g)}</color></size>" : "");
            if (GearCatalog.IsWeapon(g) && g != Gear.WaterCannon && g != Gear.Laser && g != Gear.Gatling)
            {
                float cd0 = GearCatalog.Cooldown(g, r);
                if (cd0 > 0.01f && vault != null)
                {
                    float per = 1f / (cd0 / (1f / WeaponMods.TimeMult(vault.ModOf(Loot.MyKey, g, WeaponMods.Rate))));
                    text += $"\n<size=78%><color=#9aa4b0>{L10n.T("weaponRate", per.ToString("0.##"))}</color></size>";
                }
            }
            if (GearCatalog.IsWeapon(g) && g != Gear.WaterCannon)
            {
                if (g == Gear.Laser) text += $"\n<size=78%><color=#9aa4b0>{L10n.T("weaponDpsLaser", DpsNow(Loot.MyKey, g, r), DpsNow(Loot.MyKey, g, r, atFull: true))}</color></size>";
                else { int dps = DpsNow(Loot.MyKey, g, r); if (dps > 0) text += $"\n<size=78%><color=#9aa4b0>{L10n.T("weaponDpsNow", dps)}</color></size>"; }
            }
            // The hook with something hanging on it: what it is, what it's worth and how hard it'll hit when it's flung.
            if (g == Gear.Hook && _onHook != 0)
            {
                var caught = FishView(_onHook) is Transform ct && ct ? ct.GetComponent<Item>() : null;
                var cam = GameInfo.CurCamera;
                if (!caught || !cam || (caught.transform.position - cam.transform.position).sqrMagnitude > 4f * 4f) _onHook = 0; // let go of
                else text += $"\n<size=78%><color={Hud.Hex(Hud.Gold)}>{L10n.T("hookHolding", caught.GetName(), caught.TotalWorth.ToString("N0"), FlingMult(caught.TotalWorth).ToString("0.#"))}</color></size>";
            }
            // One of the game's guns: what a hit does to a fortress fish right now (its own damage, the rarity, this wave).
            if (GearCatalog.IsGameGun(g) && Player.LocalPlayer.Holding.HeldItem is Weapon w)
            {
                var siege = Siege.Instance;
                float now = WeaponMods.ShotDamage(w) * WeaponItems.GunDamage(GearCatalog.Value(g, r), g, Scale);
                text += $"\n<size=78%><color=#c9d1db>{L10n.T("gunNow", Mathf.RoundToInt(now))}</color></size>";
            }
            if (g == Gear.Absorber) AbsorberTop(banner);
            if (_hudText.text != text)
            {
                _hudText.text = text;
                var size = _hudText.GetPreferredValues(text);
                _hudText.rectTransform.sizeDelta = new Vector2(1200f, size.y + 4f);
                _hud.rectTransform.sizeDelta = new Vector2(Mathf.Max(460f, size.x + 48f) + HudPic, Mathf.Max(70f, size.y + 36f));
            }
            if (compact) _hud.rectTransform.sizeDelta = new Vector2(470f, 32f);
            g = BarGear(g); // an absorber copying a weapon: that weapon's heat and cooldown
            _mine.TryGetValue(g, out var st);
            float left = st.readyAt - Time.time;
            if (g == Gear.Laser || g == Gear.WaterCannon || g == Gear.Gatling) // heat, the tank, how far the barrels are spun up
            {
                bool hot = left > 0f;
                _hudBar.Set(hot ? 1f : st.heat, hot ? Hud.Bad : Color.Lerp(Hud.Good, Hud.Gold, st.heat));
            }
            else _hudBar.Set(left > 0f ? 1f - left / Mathf.Max(0.1f, st.cooldown) : 1f, left > 0f ? Hud.Accent : Hud.Good);
        }

        // ---------- effects (every client) ----------

        // ---------- effects (every client), in the game's own effect language (see WeaponFx) ----------

        private class BeamView { public Transform Beam, Core, Hit, HaloIn, HaloOut; public Transform[] Streaks, Bands; public Material Sheath, Glow, HaloInMat, HaloOutMat; public Material[] BandMats; public Vector3 A, B; public float Width, Until, NextFlash, NextSmoke, Heat; }
        private readonly Dictionary<int, BeamView> _beams = new Dictionary<int, BeamView>();
        private float _nextBoomerangPick;
        private class SharkView
        {
            public Transform T; public Vector3 Pos, Face, JumpFrom, JumpTo;
            public float Born, Seen, BiteUntil;
        }
        private readonly Dictionary<int, SharkView> _sharkViews = new Dictionary<int, SharkView>();
        private float _nextBeamSound, _nextWake;

        internal static Transform FishView(int objectId)
        {
            var cm = InstanceFinder.ClientManager;
            return objectId != 0 && cm != null && cm.Objects.Spawned.TryGetValue(objectId, out NetworkObject nob) && nob ? nob.transform : null;
        }

        public void PlayFx(FxEvent e)
        {
            // My own shot: drawn from the gun in my view, which kicks back (a chained hook or a burst starts elsewhere).
            var cam = GameInfo.CurCamera;
            bool fromMe = Player.LocalPlayer && e.Who == Player.LocalPlayer.ObjectId && e.Kind != FxKind.Shark && e.Kind != FxKind.WeaponHit
                          && cam && (e.From - cam.transform.position).sqrMagnitude < 2f * 2f;
            if (fromMe) _kick = e.Kind == FxKind.Beam || e.Kind == FxKind.Gatling ? Mathf.Max(_kick, 0.3f) : 1f;
            // A friend's shot: from their gun as I see it, when what the host sent is near it (a chained hook, a bounce or a
            // burst starts somewhere else, and keeps that).
            bool theirs = !fromMe && e.Kind != FxKind.Shark && e.Kind != FxKind.WeaponHit && e.Kind != FxKind.Soul;
            var shooter = theirs ? PlayerById(e.Who) : null;
            Vector3 from = fromMe ? MyMuzzle(e.From) : shooter ? HeldMuzzle(shooter, e.From, 1.5f) ?? e.From : e.From;
            System.Func<Vector3> muzzle = () => fromMe ? MyMuzzle(from) : shooter ? HeldMuzzle(shooter, from, 1.5f) ?? from : from; // follows the gun while it moves
            switch (e.Kind)
            {
                case FxKind.Beam:
                    Beam(e.Who, from, e.To, 0.07f + 0.04f * Mathf.Clamp(e.Arg / 100f - 1f, 0f, 2f), e.PieceId / 100f);
                    if (Time.time >= _nextBeamSound) { _nextBeamSound = Time.time + 0.3f; Fx.Sound("Ignite", from, 0.25f, AudioDistance.Short); }
                    break;
                case FxKind.Gas:
                    Fx.SoundRandom("PiranhaVomit_V", 1, 3, from, 0.7f);
                    StartCoroutine(GasView(from, e.To, e.Arg / 1000f));
                    break;
                case FxKind.Boomerang:
                    Fx.Sound("PunchSwoosh", from, 0.9f);
                    StartCoroutine(BoomerangView(from, e.To));
                    break;
                case FxKind.Soul: SoulView(e.From, e.To, Mathf.Clamp(e.Arg, 1, 12)); break;
                case FxKind.Shark:
                    OnSharkFx(e);
                    break;
                case FxKind.Bubble:
                    if (e.Arg == 0) { Fx.SoundRandom("ItemHitWaterLight_V", 1, 3, e.To, 1f); Pop(e.To, 2f); break; } // a legendary bubble bursting
                    Fx.SoundRandom("BaitLeaveWater_V", 1, 3, from, 0.7f);
                    StartCoroutine(BubbleView(from, e.To, e.PieceId, e.Arg / 1000f));
                    break;
                case FxKind.Hook:
                    bool mine = Player.LocalPlayer && e.Who == Player.LocalPlayer.ObjectId;
                    if (e.Arg == 2) { Fx.Sound("PunchSwoosh", from, 1f); if (mine) _onHook = 0; break; } // flung
                    if (e.Arg == 1 && mine) _onHook = e.PieceId; // something hangs on my hook now
                    Fx.SoundRandom("FishingrodCast_V", 1, 2, from, 0.8f);
                    StartCoroutine(HookView(muzzle, e.To, e.PieceId, e.Arg == 1));
                    break;
                case FxKind.Whirl:
                    Fx.Sound("BoatFire", from, 0.5f);
                    StartCoroutine(WhirlView(from, e.To, e.Arg / 1000f));
                    break;
                case FxKind.Sonic:
                    Fx.Sound("WhaleNoise_03", from, 0.55f, AudioDistance.Long);
                    StartCoroutine(SonicView(from, e.To, e.Arg > 0 ? e.Arg : ConchHalfAngle));
                    break;
                case FxKind.Net:
                    Fx.Sound("BoatFire", from, 0.4f);
                    StartCoroutine(NetView(from, e.To, Mathf.Abs(e.Arg) / 1000f, e.PieceId / 100f));
                    break;
                case FxKind.Jelly:
                    Fx.SoundRandom("BaitLeaveWater_V", 1, 3, from, 0.7f);
                    StartCoroutine(JellyView(from, e.To, e.Arg / 1000f));
                    break;
                case FxKind.Water:
                    WaterView(from, e.To);
                    break;
                case FxKind.Drip: // a wet fish
                    WeaponFx.Emit(WeaponFx.Drops, from + Random.insideUnitSphere * 0.3f, 3, Vector3.down);
                    break;
                case FxKind.Gatling:
                {
                    if (e.PieceId < 0) { Bullet(e.From, e.To); break; } // a legendary's bullet glancing on into the next fish
                    _spins[e.Who] = (e.Arg / 100f, Time.time);
                    if (e.PieceId == 0) break; // still spinning up
                    Vector3 aim = e.To - from;
                    StartCoroutine(GatlingVolley(from, e.To, e.PieceId));
                    if (Time.time >= _nextGatlingSound) { _nextGatlingSound = Time.time + 0.07f; Fx.SoundRandom("SMGFireOutside_V", 1, 4, from, 0.5f); }
                    WeaponFx.Game("PistolFire", from, aim.normalized);
                    WeaponFx.Emit(WeaponFx.Sparks("Gold"), e.To, Mathf.Min(e.PieceId, 8), -aim.normalized + Vector3.up * 0.5f);
                    break;
                }
                case FxKind.Rocket:
                    Fx.Sound("BoatFire", from, 0.55f);
                    StartCoroutine(RocketView(from, e.To, e.Arg / 1000f));
                    break;
                case FxKind.Puffer:
                    if (e.Key == "Burst") { PufferBurstView(e.To, e.Arg / 100f); break; }
                    if (e.Arg == 0) { PufferBurstView(e.To, PufferRadius); break; }
                    Fx.Sound("PunchSwoosh", from, 0.7f);
                    StartCoroutine(PufferView(from, e.To, e.Arg / 1000f));
                    break;
                case FxKind.Star:
                    if (Time.time >= _nextStarSound) { _nextStarSound = Time.time + 0.05f; Fx.Sound("PunchSwoosh", from, 0.5f); }
                    StartCoroutine(StarView(from, e.To));
                    break;
                case FxKind.Claw:
                    Fx.Sound("PunchSwoosh", from, 0.8f);
                    StartCoroutine(ClawView(muzzle, e.To, e.PieceId));
                    break;
                case FxKind.WeaponHit:
                    if (e.Arg > 0)
                    {
                        try { PlayerUI.AddDamageNumber(e.To + Vector3.up * 0.8f, e.Arg, false); } catch { }
                        WeaponFx.Emit(WeaponFx.Sparks("Gold"), e.To, 8, Vector3.up);
                        WeaponFx.Game("BulletHit", e.To, Vector3.up);
                    }
                    else { SharkBite(e.To); Fx.SoundRandom("FishBite_V", 1, 3, e.To, 1f); WeaponFx.Game("WaterSplash", e.To); WeaponFx.Game("BloodSmall", e.To, Vector3.up); } // a shark's bite
                    break;
            }
        }

        // ---------- laser: a glowing beam with bolts racing down it, the game's muzzle flash, sparks, a hot chunk and smoke where it burns ----------

        // The beam's colour comes from how hard it bites (Owen: red, then green, blue, purple, and the strongest a "dazzling,
        // many-coloured white"). BeamHeat: the gun's rarity sets where it starts (common red, rare green, epic blue,
        // legendary purple) and its bite now (the power mod times the ramp on one fish) carries it up toward the next, so
        // a beam held on a fish visibly heats up; only a legendary with its power up, held on, goes white. The wave's
        // strength doesn't count: the colour says what the gun does, not how far the run is.
        public static float BeamHeat(Rarity r, float bite, float maxBite) => (int)r + Mathf.Clamp01(Mathf.Log(Mathf.Max(1f, bite)) / Mathf.Log(Mathf.Max(1.01f, maxBite)));
        // Owen: "雷射超滿輸出變彩虹色" ("他極限輸出的時候是彩虹色"): a legendary held on till it bites its hardest (the ramp
        // full) goes past white into a rainbow: RainbowHeat and up, the fraction past it how hard it bites (its mods), which
        // makes the rainbow brighter and run faster (AnimateBeams).
        public const float RainbowHeat = 5f;
        public static float BeamShown(Rarity r, float bite, float maxBite, bool full) =>
            full && r == Rarity.Legendary ? RainbowHeat + 0.99f * (BeamHeat(r, bite, maxBite) - 3f) : BeamHeat(r, bite, maxBite);
        private static readonly Color[] HeatStops = { new Color(1f, 0.22f, 0.18f), new Color(0.3f, 1f, 0.35f), new Color(0.3f, 0.55f, 1f), new Color(0.75f, 0.35f, 1f), Color.white };
        private static Color HeatColour(float heat)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt(heat), 0, 3);
            return Color.Lerp(HeatStops[i], HeatStops[i + 1], Mathf.Clamp01(heat - i));
        }
        private static Material LaserMat => WeaponFx.Emissive("Laser", new Color(0.35f, 0.9f, 1f), 5f);
        // Its own copy: each beam's colour moves on its own. The game's particle material adds the vertex colour (white on our
        // meshes) to its own, which turned every beam white whatever its heat (Owen: "雷射光沒有…很明顯的…顏色不同"):
        // weighted by _BaseColorAddSubDiff.x, none (as BossVfx.Glow).
        private static Material BeamMat()
        {
            var m = new Material(LaserMat);
            if (m.HasProperty("_BaseColorAddSubDiff")) m.SetVector("_BaseColorAddSubDiff", Vector4.zero);
            return m;
        }

        // Owen: "雷射的顏色不夠螢光沒發光那個線條 然後雷射子彈速度太慢了 可以很快但是線條長": a neon glow round the beam
        // that doesn't wait on the game's bloom (two see-through sleeves in its colour), and long streaks racing down it.
        public const int BeamStreaks = 3;
        public const float StreakSpeed = 160f, StreakLength = 3f; // m/s, m (at most half the beam)
        public const int RainbowBands = 12; // the sheath of a rainbow beam, cut in bands of the spectrum racing down it
        private static IEnumerable<Transform> Parts(BeamView v) => new[] { v.Beam, v.Core, v.Hit, v.HaloIn, v.HaloOut }.Concat(v.Streaks);

        private void Beam(int who, Vector3 a, Vector3 b, float width, float heat)
        {
            if (!_beams.TryGetValue(who, out var v) || !v.Beam)
            {
                v = new BeamView { Sheath = BeamMat(), Glow = BeamMat(), HaloInMat = BossVfx.Wash(Color.white), HaloOutMat = BossVfx.Wash(Color.white) };
                v.Beam = WeaponFx.Piece(WeaponFx.Cyl, v.Sheath); v.Core = WeaponFx.Piece(WeaponFx.Cyl, v.Glow); v.Hit = WeaponFx.Piece(WeaponFx.Cube, v.Glow);
                v.HaloIn = WeaponFx.Piece(WeaponFx.Cyl, v.HaloInMat); v.HaloOut = WeaponFx.Piece(WeaponFx.Cyl, v.HaloOutMat);
                v.Streaks = Enumerable.Range(0, BeamStreaks).Select(_ => WeaponFx.Piece(WeaponFx.Cyl, v.Glow)).ToArray();
                foreach (var t in Parts(v)) DontDestroyOnLoad(t.gameObject);
                _beams[who] = v;
            }
            v.A = a; v.B = b; v.Width = width; v.Heat = heat; v.Until = Time.time + 0.22f;
        }

        private void AnimateBeams(float now)
        {
            foreach (var v in _beams.Values)
            {
                if (!v.Beam) continue;
                bool on = now <= v.Until;
                float rainbow = v.Heat - RainbowHeat; // 0 and up: past white, how hard it bites (BeamShown)
                bool bands = on && rainbow >= 0f;
                if (bands && v.Bands == null)
                {
                    v.BandMats = Enumerable.Range(0, RainbowBands).Select(_ => BeamMat()).ToArray();
                    v.Bands = v.BandMats.Select(m => WeaponFx.Piece(WeaponFx.Cyl, m)).ToArray();
                    foreach (var t in v.Bands) DontDestroyOnLoad(t.gameObject);
                }
                foreach (var t in Parts(v)) t.gameObject.SetActive(on && !(bands && t == v.Beam)); // the bands in place of the sheath
                if (v.Bands != null) foreach (var t in v.Bands) t.gameObject.SetActive(bands);
                if (!on) continue;
                Vector3 d = v.B - v.A;
                var rot = Quaternion.FromToRotation(Vector3.up, d.sqrMagnitude > 1e-4f ? d : Vector3.up);
                float pulse = 1f + 0.3f * Mathf.Sin(now * 47f) + 0.15f * Mathf.Sin(now * 83f);
                // Seen from the side the beam shows its glowing sheath; the white core pokes out in front of it near the gun.
                v.Beam.SetPositionAndRotation(v.A, rot);
                v.Beam.localScale = new Vector3(v.Width * pulse, d.magnitude, v.Width * pulse);
                v.Core.SetPositionAndRotation(v.A + d.normalized * 0.02f, rot);
                v.Core.localScale = new Vector3(v.Width * 0.35f, d.magnitude, v.Width * 0.35f);
                v.Hit.SetPositionAndRotation(v.B, Quaternion.Euler(now * 900f, now * 700f, 0f));
                v.Hit.localScale = Vector3.one * v.Width * 2.4f * pulse;
                v.HaloIn.SetPositionAndRotation(v.A, rot);
                v.HaloIn.localScale = new Vector3(v.Width * 2.2f * pulse, d.magnitude, v.Width * 2.2f * pulse);
                v.HaloOut.SetPositionAndRotation(v.A, rot);
                v.HaloOut.localScale = new Vector3(v.Width * 4f * pulse, d.magnitude, v.Width * 4f * pulse);
                // Neon: a saturated sheath bright enough for the bloom (with the white the game's material used to add gone, a
                // strong glow keeps its colour), a near-white core, and the sleeves' soft light in its colour.
                Color c = bands ? Color.HSVToRGB(Mathf.Repeat(now * 0.5f, 1f), 0.8f, 1f) : HeatColour(v.Heat), core = bands ? Color.white : Color.Lerp(c, Color.white, 0.5f);
                v.Sheath.SetColor("_BaseColor", c * 0.15f); v.Sheath.SetColor("_EmissionColor", c * 3.2f);
                v.Glow.SetColor("_BaseColor", core * 0.15f); v.Glow.SetColor("_EmissionColor", core * 4.5f);
                v.HaloInMat.color = new Color(c.r, c.g, c.b, 0.28f);
                Color far = bands ? Color.HSVToRGB(Mathf.Repeat(now * 0.5f + 0.5f, 1f), 0.8f, 1f) : c; // the outer glow the other side of the wheel
                v.HaloOutMat.color = new Color(far.r, far.g, far.b, 0.1f);
                Vector3 dir = d.normalized;
                if (bands)
                {
                    // The spectrum once down the beam, sliding toward what it burns: paler and slower for a plain legendary,
                    // full colour and racing for one with everything.
                    float seg = d.magnitude / RainbowBands, sat = 0.55f + 0.4f * rainbow, flow = 0.6f + 1.4f * rainbow, glow = 3.2f + 1.5f * rainbow;
                    for (int i = 0; i < RainbowBands; i++)
                    {
                        v.Bands[i].SetPositionAndRotation(v.A + dir * seg * i, rot);
                        v.Bands[i].localScale = new Vector3(v.Width * pulse, seg, v.Width * pulse);
                        Color h = Color.HSVToRGB(Mathf.Repeat(i / (float)RainbowBands - now * flow, 1f), sat, 1f);
                        v.BandMats[i].SetColor("_BaseColor", h * 0.15f); v.BandMats[i].SetColor("_EmissionColor", h * glow);
                    }
                }
                // Streaks: StreakLength long (at most half the beam), StreakSpeed fast, spaced evenly, clipped to the beam.
                float len = d.magnitude, sl = Mathf.Min(StreakLength, len * 0.5f), lap = len + sl;
                for (int i = 0; i < v.Streaks.Length; i++)
                {
                    float head = Mathf.Repeat(now * StreakSpeed + i * lap / v.Streaks.Length, lap), from = Mathf.Max(0f, head - sl), to = Mathf.Min(len, head);
                    var st = v.Streaks[i];
                    st.gameObject.SetActive(to - from > 0.02f);
                    st.SetPositionAndRotation(v.A + dir * from, rot);
                    st.localScale = new Vector3(v.Width * 0.5f, to - from, v.Width * 0.5f);
                }
                string[] sparks = { "Red", "Green", "Blue", "Violet" };
                WeaponFx.Emit(WeaponFx.Sparks(v.Heat >= 3.5f ? new[] { "Red", "Gold", "Green", "Cyan", "Violet" }[(int)(now * 20f) % 5] : sparks[Mathf.Clamp(Mathf.RoundToInt(v.Heat), 0, 3)]), v.B, 2, -dir + Vector3.up * 0.5f);
                if (now >= v.NextFlash) { v.NextFlash = now + 0.07f; WeaponFx.Game("PistolFire", v.A, dir); }
                if (now >= v.NextSmoke) { v.NextSmoke = now + 0.3f; WeaponFx.Game("SmallSmoke", v.B); WeaponFx.Emit(WeaponFx.Dust, v.B, 1, Vector3.up); }
            }
        }

        // ---------- shared pieces ----------

        private static void Pop(Vector3 at, float size)
        {
            for (int i = 0; i < 18; i++)
                WeaponFx.Emit(WeaponFx.Bubbles, at, 1, default, null, Random.Range(0.12f, 0.28f) * size, Random.onUnitSphere * Random.Range(1.5f, 3.5f) * size, Random.Range(0.4f, 0.8f));
            WeaponFx.Emit(WeaponFx.Drops, at, Mathf.RoundToInt(10 * size), Vector3.up);
            WeaponFx.Game("WaterSplash", at);
        }

        // An arc from a to b over LobFlight, tumbling; `trail` puffs out behind it.
        private IEnumerator Lob(Transform shell, Vector3 a, Vector3 b, System.Action<Vector3> trail, bool tumble = true)
        {
            float next = 0f;
            Vector3 spinAxis = Vector3.Cross(b - a, Vector3.up).normalized;
            for (float t = 0f; t < LobFlight && shell; t += Time.deltaTime)
            {
                float k = t / LobFlight;
                shell.position = Vector3.Lerp(a, b, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 2.5f;
                if (tumble) shell.Rotate(spinAxis, -900f * Time.deltaTime, Space.World);
                if (t >= next) { next = t + 0.02f; trail(shell.position); }
                yield return null;
            }
            if (shell) Destroy(shell.gameObject);
        }

        // ---------- poison gas: a hazard canister tumbles over trailing gas, bursts, and the cloud churns (now and then a skull) ----------

        private class GasShown { public Vector3 At; public float Radius, Until; public bool Burst; }
        private readonly List<GasShown> _gasShown = new List<GasShown>();

        private IEnumerator GasView(Vector3 a, Vector3 b, float life)
        {
            if ((a - b).sqrMagnitude > 0.01f)
            {
                var can = new GameObject("FortressGasCan").transform;
                Models.Attach("GasCanister", can, new Vector3(0.18f, 0.36f, 0.18f)).transform.localScale = Vector3.one * 1.4f;
                yield return Lob(can, a, b, p => WeaponFx.Emit(WeaponFx.Gas, p, 1, default, null, Random.Range(0.35f, 0.6f), Random.insideUnitSphere * 0.4f, 0.7f));
            }
            Fx.SoundRandom("BaitHitWater_V", 1, 3, b, 0.6f);
            float radius = life > 4f ? GasRadius : 2f, k = radius / GasRadius;
            // One coming down inside a cloud already drawn tops that one up instead of churning on top of it (the legendary's
            // cloud from every fish that dies in it piled up by the dozen: 「劇毒槍是不是很耗顯卡」), and only so many churn.
            _gasShown.RemoveAll(x => Time.time > x.Until);
            bool burst = life <= 4f; // a legendary's cloud from a body: the host tops up one of those it falls in too (HalfSecondTick)
            var into = burst ? _gasShown.FirstOrDefault(x => x.Burst && (b - x.At).Flat().sqrMagnitude <= x.Radius * x.Radius) : null;
            bool crowded = _gasShown.Count >= (ClientSettings.Reduced ? 3 : 8);
            for (int i = 0, n = into != null || crowded ? 8 : 24; i < n; i++) // the burst
                WeaponFx.Emit(WeaponFx.Gas, b + Vector3.up * 0.3f, 1, default, null, Random.Range(0.8f, 1.5f) * k,
                              (Random.onUnitSphere.Flat().normalized * Random.Range(1.5f, 3.5f) + Vector3.up * Random.Range(0.3f, 1.2f)) * k, Random.Range(0.9f, 1.4f));
            WeaponFx.Emit(WeaponFx.Sparks("Green"), b + Vector3.up * 0.2f, 12, Vector3.up);
            WeaponFx.Emit(WeaponFx.Dust, b, 6, Vector3.up);
            if (into != null) { into.Until = Mathf.Max(into.Until, Time.time + life); yield break; }
            if (crowded) yield break;
            var shown = new GasShown { At = b, Radius = radius, Until = Time.time + life, Burst = burst };
            _gasShown.Add(shown);
            float nextPuff = 0f, nextBubble = 0f, nextSkull = 0.8f;
            for (float t = 0f; Time.time < shown.Until; t += Time.deltaTime)
            {
                if (shown.Until - Time.time < 1.2f) { yield return null; continue; } // the last puffs fade out by themselves
                if (t >= nextPuff)
                {
                    nextPuff = t + 0.05f;
                    Vector3 at = b + (Random.insideUnitCircle * radius * 0.85f).X0Y() + Vector3.up * Random.Range(0.3f, 1.3f);
                    WeaponFx.Emit(WeaponFx.Gas, at, 1, default, null, Random.Range(1.2f, 2.3f) * k, Random.insideUnitSphere * 0.25f, Random.Range(1.4f, 2.4f));
                }
                if (t >= nextBubble)
                {
                    nextBubble = t + 0.15f;
                    WeaponFx.Emit(WeaponFx.Bubbles, b + (Random.insideUnitCircle * radius * 0.7f).X0Y() + Vector3.up * 0.1f, 1, default, new Color(0.7f, 1f, 0.45f), Random.Range(0.15f, 0.3f), Vector3.up * Random.Range(0.6f, 1.4f), 1.2f);
                }
                if (t >= nextSkull) { nextSkull = t + 1.6f; WeaponFx.Game("Skull", b + (Random.insideUnitCircle * radius * 0.5f).X0Y() + Vector3.up * 0.8f); }
                yield return null;
            }
        }

        // ---------- the second set ----------

        private float _nextStarSound, _nextSpraySound;

        // Net launcher: a balled net arcs over, spreads open on the ground with its floats, holds, then is reeled off.
        private IEnumerator NetView(Vector3 a, Vector3 b, float hold, float radius)
        {
            var ball = new GameObject("FortressNetBall").transform;
            Models.Attach("NetBall", ball, Vector3.one * 0.5f);
            yield return Lob(ball, a, b, p => WeaponFx.Emit(WeaponFx.Dust, p, 1, default, new Color(0.8f, 0.7f, 0.5f), 0.15f, Vector3.zero, 0.4f));
            Fx.SoundRandom("BaitHitWater_V", 1, 3, b, 0.7f);
            WeaponFx.Emit(WeaponFx.Dust, b, 10, Vector3.up);
            float across = 2f * (radius > 0f ? radius : NetRadius);
            var net = new GameObject("FortressNet").transform;
            Models.Attach("NetSpread", net, new Vector3(1f, 0.05f, 1f));
            net.position = b + Vector3.up * 0.06f;
            net.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            for (float t = 0f; t < 0.2f && net; t += Time.deltaTime) { net.localScale = Vector3.one * across * Mathf.Lerp(0.2f, 1f, t / 0.2f); yield return null; }
            if (net) net.localScale = Vector3.one * across;
            yield return new WaitForSeconds(Mathf.Max(0f, hold - 0.6f));
            for (float t = 0f; t < 0.4f && net; t += Time.deltaTime) { net.localScale = Vector3.one * across * (1f - t / 0.4f); yield return null; }
            if (net) Destroy(net.gameObject);
        }

        // Jellyfish gun: a jellyfish sails over and hangs there pulsing, little pink zaps dropping onto what's under it.
        private IEnumerator JellyView(Vector3 a, Vector3 b, float life)
        {
            var jelly = new GameObject("FortressJelly").transform;
            float size = life > 4f ? 1.3f : 0.85f;
            Models.Attach("Jellyfish", jelly, Vector3.one * 0.5f).transform.localScale = Vector3.one * size;
            Vector3 hang = b + Vector3.up * JellyHover;
            yield return LobKeep(jelly, a, hang, p => WeaponFx.Emit(WeaponFx.Bubbles, p, 1, default, new Color(1f, 0.45f, 0.85f), Random.Range(0.12f, 0.22f), Random.insideUnitSphere * 0.4f, 0.8f)); // a pink bioluminescent trail
            float nextZap = 0f;
            for (float t = 0f; t < life && jelly; t += Time.deltaTime)
            {
                float pulse = 1f + 0.12f * Mathf.Sin(t * 5f);
                jelly.localScale = new Vector3(pulse, 2f - pulse, pulse);
                jelly.position = hang + Vector3.up * 0.15f * Mathf.Sin(t * 1.7f);
                jelly.Rotate(0f, 25f * Time.deltaTime, 0f);
                if (t >= nextZap)
                {
                    nextZap = t + 0.3f;
                    Vector3 under = b + (Random.insideUnitCircle * JellyRadius * size * 0.75f).X0Y() + Vector3.up * 0.2f;
                    WeaponFx.Emit(WeaponFx.Bolts("Pink"), Vector3.Lerp(jelly.position, under, 0.5f), 3, (under - jelly.position).normalized);
                    WeaponFx.Emit(WeaponFx.Sparks("Pink"), under, 3, Vector3.up);
                }
                if (t > life - 0.4f) jelly.localScale *= Mathf.Max(0.01f, (life - t) / 0.4f);
                yield return null;
            }
            if (jelly) Destroy(jelly.gameObject);
        }

        // A lob that leaves the thing where it lands (the jellyfish stays to hang there).
        private IEnumerator LobKeep(Transform t, Vector3 a, Vector3 b, System.Action<Vector3> trail = null)
        {
            float next = 0f;
            for (float k = 0f; k < 1f && t; k += Time.deltaTime / LobFlight)
            {
                t.position = Vector3.Lerp(a, b, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.6f;
                if (trail != null && Time.time >= next) { next = Time.time + 0.04f; trail(t.position); }
                yield return null;
            }
            if (t) t.position = b;
        }

        // Water cannon (sent ten times a second while spraying): a stream of drops, foam and a splash where it hits.
        private void WaterView(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            for (int i = 0; i < 6; i++)
            {
                float k = Random.value;
                WeaponFx.Emit(WeaponFx.Drops, a + d * k + Random.insideUnitSphere * 0.08f, 1, default, null, Random.Range(0.08f, 0.16f), d.normalized * Random.Range(8f, 14f) + Random.insideUnitSphere, 0.35f);
            }
            WeaponFx.Emit(WeaponFx.Foam, b + Vector3.up * 0.1f, 3, Vector3.up);
            if (Random.value < 0.3f) WeaponFx.Game("WaterSplash", b);
            if (Time.time >= _nextSpraySound) { _nextSpraySound = Time.time + 0.35f; Fx.SoundRandom("ItemHitWaterLight_V", 1, 3, b, 0.6f); }
        }

        // Pufferfish bomb: thrown, lands, swells up shaking for the fuse (the burst comes as its own event).
        private IEnumerator PufferView(Vector3 a, Vector3 b, float fuse)
        {
            var puff = new GameObject("FortressPuffer").transform;
            Models.Attach("PufferBomb", puff, Vector3.one * 0.25f).transform.localScale = Vector3.one * 1.3f;
            yield return LobKeep(puff, a, b + Vector3.up * 0.3f, p => WeaponFx.Emit(WeaponFx.Sparks("Gold"), p, 1, Vector3.up));
            Fx.SoundRandom("ItemImpactHeavy_V", 1, 3, b, 0.6f);
            for (float t = 0f; t < fuse && puff; t += Time.deltaTime)
            {
                float k = t / fuse;
                puff.localScale = Vector3.one * Mathf.Lerp(1f, 3.2f, k * k);
                puff.position = b + Vector3.up * (0.3f + 0.4f * k) + Random.insideUnitSphere * 0.04f * k;
                puff.Rotate(0f, 200f * Time.deltaTime, 0f);
                yield return null;
            }
            if (puff) Destroy(puff.gameObject);
        }

        private static void PufferBurstView(Vector3 at, float radius = PufferRadius)
        {
            float k = Mathf.Clamp(radius / PufferRadius, 0.5f, 6f);
            WeaponFx.Game("SmallExplosion", at + Vector3.up * 0.5f, Vector3.up);
            Fx.Sound("Explosion", at, 0.6f, AudioDistance.Long);
            for (int i = 0; i < 24; i++)
                WeaponFx.Emit(WeaponFx.Sparks("Gold"), at + Vector3.up * 0.6f, 1, default, null, 0.12f, Random.onUnitSphere.Flat().normalized * Random.Range(8f, 13f) * k + Vector3.up * Random.Range(0f, 3f), 0.35f);
            for (int i = 0; i < 14; i++)
                WeaponFx.Emit(WeaponFx.Gas, at + Vector3.up * 0.4f, 1, default, null, Random.Range(0.6f, 1.1f), Random.onUnitSphere.Flat().normalized * Random.Range(1f, 3f) * k + Vector3.up * 0.6f, 1f);
        }

        // Rocket: a little rocket flying straight with fire and smoke behind it, bursting where it ends.
        private IEnumerator RocketView(Vector3 a, Vector3 b, float flight)
        {
            var rocket = new GameObject("FortressRocket").transform;
            Models.Attach("RocketShell", rocket, new Vector3(0.12f, 0.12f, 0.5f));
            Vector3 dir = (b - a).sqrMagnitude > 0.01f ? (b - a).normalized : Vector3.forward;
            rocket.rotation = Quaternion.LookRotation(dir);
            float nextPuff = 0f;
            for (float t = 0f; t < flight && rocket; t += Time.deltaTime)
            {
                rocket.position = Vector3.Lerp(a, b, t / flight);
                rocket.Rotate(0f, 0f, 720f * Time.deltaTime, Space.Self);
                if (t >= nextPuff)
                {
                    nextPuff = t + 0.025f;
                    Vector3 tail = rocket.position - dir * 0.3f;
                    WeaponFx.Emit(WeaponFx.Sparks("Orange"), tail, 2, -dir);
                    WeaponFx.Emit(WeaponFx.Dust, tail, 1, -dir, new Color(0.75f, 0.75f, 0.75f), Random.Range(0.35f, 0.6f), -dir * 0.6f + Random.insideUnitSphere * 0.4f, 0.9f);
                }
                yield return null;
            }
            if (rocket) Destroy(rocket.gameObject);
            WeaponFx.Game("SmallExplosion", b + Vector3.up * 0.3f, Vector3.up);
            Fx.Sound("Explosion", b, 0.55f, AudioDistance.Long);
            for (int i = 0; i < 16; i++)
                WeaponFx.Emit(WeaponFx.Sparks("Orange"), b + Vector3.up * 0.4f, 1, default, null, 0.12f, Random.onUnitSphere * Random.Range(6f, 11f) + Vector3.up * 2f, 0.3f);
            if (b.y <= WaterManager.GetWaterHeight(b) + 0.2f) WeaponFx.Game("WaterSplash", b);
        }

        // Starfish: one spins out fast and flat, sparks where it ends.
        private IEnumerator StarView(Vector3 a, Vector3 b)
        {
            var star = new GameObject("FortressStar").transform;
            Models.Attach("Starfish", star, Vector3.one * 0.3f).transform.localScale = Vector3.one * 1.4f;
            float time = Mathf.Clamp((b - a).magnitude / 40f, 0.08f, 0.4f);
            for (float t = 0f; t < time && star; t += Time.deltaTime)
            {
                star.position = Vector3.Lerp(a, b, t / time);
                star.rotation = Quaternion.LookRotation((b - a).normalized) * Quaternion.Euler(90f, 0f, t * 2400f);
                if (Random.value < 0.5f) WeaponFx.Emit(WeaponFx.Sparks("Cyan"), star.position, 1, Vector3.up); // a glittering trail
                yield return null;
            }
            if (star) Destroy(star.gameObject);
            WeaponFx.Emit(WeaponFx.Sparks("Orange"), b, 6, Vector3.up);
        }

        // Lobster claw: the claw shoots out on its line, snaps shut on the fish (or on nothing, at the end of its reach) and
        // comes back; the fish it caught goes flying.
        private IEnumerator ClawView(System.Func<Vector3> muzzle, Vector3 b, int fishId)
        {
            Vector3 a = muzzle();
            var claw = new GameObject("FortressClawShot").transform;
            Models.Attach("LobsterClaw", claw, new Vector3(0.12f, 0.12f, 0.45f)).transform.localScale = Vector3.one * 1.6f;
            var rope = new WeaponFx.Rope(18, 0.05f);
            rope.Start(a, a);
            float outTime = Mathf.Clamp((b - a).magnitude / ClawSpeed, 0.05f, 0.45f); // as long as the host waits before the throw
            for (float t = 0f; t < outTime && claw; t += Time.deltaTime)
            {
                Vector3 p = Vector3.Lerp(a, b, t / outTime);
                claw.SetPositionAndRotation(p, Quaternion.LookRotation(b - a));
                rope.Step(muzzle(), p - claw.forward * 0.25f, 0.1f);
                yield return null;
            }
            Fx.SoundRandom("KnifeHit_0", 1, 4, b, 0.9f);
            WeaponFx.Emit(WeaponFx.Sparks("Orange"), b, fishId != 0 ? 10 : 4, Vector3.up);
            if (fishId != 0) { WeaponFx.Game("BloodSmall", b, Vector3.up); StartCoroutine(FlungView(fishId)); }
            else WeaponFx.Emit(WeaponFx.Dust, b, 3, Vector3.up);
            for (float t = 0f; t < 0.2f && claw; t += Time.deltaTime) // and back on its line
            {
                Vector3 p = Vector3.Lerp(b, muzzle(), t * t / 0.04f);
                claw.position = p;
                rope.Step(muzzle(), p - claw.forward * 0.25f, 0.2f);
                yield return null;
            }
            if (claw) Destroy(claw.gameObject);
            rope.Destroy();
        }

        // The fish the claw threw leaves a trail of drops while it flies.
        private IEnumerator FlungView(int fishId)
        {
            for (float t = 0f; t < ClawFlight + 0.1f; t += 0.03f)
            {
                var f = FishView(fishId);
                if (f) WeaponFx.Emit(WeaponFx.Drops, f.position + Vector3.up * 0.5f, 2, Vector3.up);
                yield return new WaitForSeconds(0.03f);
            }
        }

        // ---------- tuna boomerang: spins out and back flinging sea water ----------

        private IEnumerator BoomerangView(Vector3 a, Vector3 b)
        {
            var root = new GameObject("FortressBoomerang");
            Models.Attach("Boomerang", root.transform, new Vector3(0.6f, 0.08f, 0.2f));
            float next = 0f;
            for (float t = 0f; t < BoomerangLeg * 2f && root; t += Time.deltaTime)
            {
                Vector3 p = BoomerangAt(a, b, t);
                if (Time.time >= _nextBoomerangPick) { _nextBoomerangPick = Time.time + 0.12f; if (Loot.Instance) Loot.Instance.ServerBoomerangPick(p, 1.8f); }
                root.transform.position = p;
                root.transform.rotation = Quaternion.Euler(0f, t * 1300f, 0f);
                if (t >= next)
                {
                    next = t + 0.015f;
                    Vector3 tip = p + root.transform.right * 0.3f, fling = root.transform.forward; // off the spinning tips
                    WeaponFx.Emit(WeaponFx.Drops, tip, 1, default, null, -1f, fling * 3f + Vector3.up, 0.5f);
                    WeaponFx.Emit(WeaponFx.Foam, p, 1, default, null, Random.Range(0.18f, 0.3f), Vector3.zero, 0.25f);
                }
                yield return null;
            }
            if (root) { Fx.Sound("PickupWhoosh", root.transform.position, 0.7f); WeaponFx.Emit(WeaponFx.Drops, root.transform.position, 10, Vector3.up); Destroy(root); }
        }

        // ---------- bubble gun: a pixel bubble wobbling out, a stream of little ones; the fish floats inside a big one ----------

        private IEnumerator BubbleView(Vector3 a, Vector3 b, int fishId, float seconds)
        {
            var shot = WeaponFx.Billboard(WeaponFx.BubbleTex, "Bubble", Color.white);
            float next = 0f;
            for (float t = 0f; t < 0.3f && shot; t += Time.deltaTime)
            {
                shot.position = Vector3.Lerp(a, b, t / 0.3f) + Vector3.up * Mathf.Sin(t * 26f) * 0.06f;
                WeaponFx.Face(shot);
                float w = Mathf.Sin(t * 38f) * 0.12f;
                shot.localScale = new Vector3(1f + w, 1f - w, 1f) * 0.55f;
                if (t >= next) { next = t + 0.02f; WeaponFx.Emit(WeaponFx.Bubbles, shot.position, 1, default, null, Random.Range(0.1f, 0.2f), Random.insideUnitSphere * 0.6f + Vector3.up * 0.4f, 0.6f); }
                yield return null;
            }
            if (shot) Destroy(shot.gameObject);
            var fish = FishView(fishId);
            if (!fish) { Pop(b, 0.6f); yield break; }
            float size = 2f * Mathf.Max(0.6f, fish.localScale.y);
            var bubble = WeaponFx.Billboard(WeaponFx.BubbleTex, "Bubble", Color.white);
            next = 0f;
            for (float t = 0f; t < seconds && fish && bubble; t += Time.deltaTime)
            {
                bubble.position = fish.position + Vector3.up * 0.6f * fish.localScale.y;
                WeaponFx.Face(bubble);
                float wob = Mathf.Sin(t * 5f) * 0.05f;
                bubble.localScale = new Vector3(1f + wob, 1f - wob, 1f) * size * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t * 5f));
                if (t >= next) { next = t + 0.2f; WeaponFx.Emit(WeaponFx.Bubbles, bubble.position + Random.insideUnitSphere * size * 0.4f, 1, default, null, Random.Range(0.12f, 0.22f), Vector3.up * Random.Range(0.5f, 1f), 1.2f); }
                yield return null;
            }
            if (bubble) { Fx.SoundRandom("ItemHitWaterLight_V", 1, 3, bubble.position, 0.8f); Pop(bubble.position, size * 0.6f); Destroy(bubble.gameObject); }
        }

        // ---------- hook: the game's muzzle flash, a three-pronged claw on a real rope (slack sags, a bite snaps it taut) ----------

        private int _onHook; // the item hanging on my hook (its object id), for the HUD
        private IEnumerator HookView(System.Func<Vector3> muzzle, Vector3 b, int fishId, bool item = false)
        {
            Vector3 a = muzzle();
            Vector3 dir = (b - a).sqrMagnitude > 1e-4f ? (b - a).normalized : Vector3.forward;
            WeaponFx.Game("PistolFire", a, dir);
            var claw = new GameObject("FortressClaw").transform;
            Models.Attach("GrappleClaw", claw, new Vector3(0.12f, 0.12f, 0.4f)).transform.localScale = Vector3.one * 1.5f;
            var rope = new WeaponFx.Rope(18, 0.04f);
            rope.Start(a, a);
            var fish = FishView(fishId);
            Vector3 aim = fish ? fish.position + Vector3.up * 0.5f : b;
            float outTime = Mathf.Clamp((aim - a).magnitude / 60f, 0.1f, 0.35f);
            for (float t = 0f; t < outTime && claw; t += Time.deltaTime) // flying out, the line paying out behind it
            {
                Vector3 p = Vector3.Lerp(a, aim, t / outTime);
                claw.SetPositionAndRotation(p, Quaternion.LookRotation(aim - a));
                rope.Step(muzzle(), p - claw.forward * 0.3f, 0.12f);
                yield return null;
            }
            if (fish)
            {
                Fx.Sound("CrabFishingRodReelIn", a, 0.8f);
                WeaponFx.Game("BulletHit", aim, -dir);
                if (!item) WeaponFx.Game("BloodSmall", aim, Vector3.up);
                WeaponFx.Emit(WeaponFx.Sparks("Gold"), aim, 10, -dir);
                for (float t = 0f; t < 0.75f && fish && claw; t += Time.deltaTime) // bitten in: the line taut, reeling
                {
                    Vector3 p = fish.position + Vector3.up * 0.5f;
                    claw.SetPositionAndRotation(p, Quaternion.LookRotation(p - muzzle()));
                    rope.Step(muzzle(), p - claw.forward * 0.3f, 0f);
                    yield return null;
                }
            }
            else
            {
                WeaponFx.Game("BulletHit", aim, -dir);
                WeaponFx.Emit(WeaponFx.Sparks("Gold"), aim, 6, -dir);
                WeaponFx.Emit(WeaponFx.Dust, aim, 3, Vector3.up);
                for (float t = 0f; t < 0.12f; t += Time.deltaTime) { rope.Step(muzzle(), aim - claw.forward * 0.3f, 0.3f); yield return null; } // it drops slack...
                for (float t = 0f; t < 0.35f && claw; t += Time.deltaTime) // ...and is reeled back in
                {
                    Vector3 p = Vector3.Lerp(aim, muzzle(), t * t / (0.35f * 0.35f));
                    claw.position = p;
                    rope.Step(muzzle(), p - claw.forward * 0.3f, 0.3f * (1f - t / 0.35f));
                    yield return null;
                }
            }
            if (claw) Destroy(claw.gameObject);
            rope.Destroy();
        }

        // ---------- whirlpool: a water orb, the game's water explosion, a spinning pixel-foam funnel with foam racing round ----------

        private IEnumerator WhirlView(Vector3 a, Vector3 b, float seconds)
        {
            Vector3 dir = (b - a).normalized;
            WeaponFx.Game("ShotgunFire", a, dir);
            WeaponFx.Emit(WeaponFx.Drops, a, 10, dir + Vector3.up * 0.3f);
            var orb = new GameObject("FortressWhirlOrb").transform;
            WeaponFx.Piece(WeaponFx.Ball, WeaponFx.Water("Orb", new Color(0.3f, 0.6f, 1f, 0.85f)), orb).localScale = Vector3.one * 0.45f;
            WeaponFx.Piece(WeaponFx.Cube, WeaponFx.Emissive("OrbCore", new Color(0.4f, 0.75f, 1f), 4f), orb).localScale = Vector3.one * 0.18f;
            yield return Lob(orb, a, b, p => { WeaponFx.Emit(WeaponFx.Drops, p, 1, default, null, -1f, Random.insideUnitSphere * 1.5f); WeaponFx.Emit(WeaponFx.Foam, p, 1, default, null, 0.25f, Vector3.zero, 0.3f); });
            Fx.SoundRandom("ItemHitWaterHeavy_V", 1, 3, b, 0.9f);
            WeaponFx.Game("WaterExplosion", b);
            WeaponFx.Game("WaterSplash", b);
            // A shallow spiral of water just above the ground (or dipping into the sea), turning; foam circling it.
            var funnel = WeaponFx.Piece(WeaponFx.Funnel, WeaponFx.Water("Whirl", new Color(0.7f, 0.88f, 1f, 1f), WeaponFx.SpiralTex));
            var inner = WeaponFx.Piece(WeaponFx.Funnel, WeaponFx.Water("WhirlInner", new Color(0.45f, 0.65f, 1f, 1f), WeaponFx.SpiralTex));
            var foam = WeaponFx.Orbit(b + Vector3.up * 0.32f, WhirlRadius * 0.9f);
            float nextDrop = 0f, spin = 0f;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float grow = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t * 2.5f)) * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, (seconds - t) * 2f));
                spin += Time.deltaTime * 150f;
                if (funnel) { funnel.SetPositionAndRotation(b + Vector3.up * 0.3f, Quaternion.Euler(0f, -spin, 0f)); funnel.localScale = new Vector3(WhirlRadius, WhirlRadius * 0.12f, WhirlRadius) * grow; }
                if (inner) { inner.SetPositionAndRotation(b + Vector3.up * 0.26f, Quaternion.Euler(0f, -spin * 1.7f + 40f, 0f)); inner.localScale = new Vector3(WhirlRadius * 0.55f, WhirlRadius * 0.1f, WhirlRadius * 0.55f) * grow; }
                if (foam) foam.transform.localScale = Vector3.one * Mathf.Max(0.05f, grow);
                if (t > seconds - 0.6f && foam) { var em = foam.emission; em.enabled = false; }
                if (t >= nextDrop && grow > 0.5f)
                {
                    nextDrop = t + 0.1f;
                    float ang = Random.value * Mathf.PI * 2f;
                    Vector3 rad = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    WeaponFx.Emit(WeaponFx.Drops, b + rad * WhirlRadius * grow + Vector3.up * 0.3f, 3, rad + Vector3.up * 1.5f);
                }
                yield return null;
            }
            if (funnel) Destroy(funnel.gameObject);
            if (inner) Destroy(inner.gameObject);
            if (foam) Destroy(foam.gameObject, 1.8f);
            WeaponFx.Game("WaterSplash", b);
        }

        // ---------- conch: glowing rings of sound roll out in its cone, music notes tumble along, dust skips over the ground ----------

        private IEnumerator SonicView(Vector3 a, Vector3 b, float half)
        {
            Vector3 dir = (b - a).Flat().normalized;
            if (dir.sqrMagnitude < 0.01f) yield break;
            float reach = Mathf.Max(1f, (b - a).Flat().magnitude), end = 2f * reach * Mathf.Tan(Mathf.Min(80f, half) * Mathf.Deg2Rad);
            WeaponFx.Emit(WeaponFx.Notes, a + dir * 0.4f, 12, dir);
            if (Physics.Raycast(a, Vector3.down, out var ground, 3f, GameInfo.LevelLayer))
                for (int i = 1; i <= 4; i++) WeaponFx.Emit(WeaponFx.Dust, ground.point + dir * i * 1.8f, 3, dir + Vector3.up);
            for (int i = 0; i < 4; i++)
            {
                WeaponFx.Emit(WeaponFx.Rings, a + dir * 0.4f, 1, dir, null, end, dir * (reach / 0.48f), 0.48f);
                yield return new WaitForSeconds(0.07f);
            }
        }

        // ---------- shark: bursts out of the sea (the game's water explosion), a foaming V of a wake, a splash as it dives ----------

        private void SoulView(Vector3 from, Vector3 to, int n)
        {
            for (int i = 0; i < n; i++)
            {
                var root = new GameObject("FortressSoul").transform;
                DontDestroyOnLoad(root.gameObject);
                Models.Attach("SoulOrb", root, Vector3.one * 0.2f);
                var t = root;
                t.position = from + Quaternion.Euler(0f, i * 37f, 0f) * Vector3.forward * Random.Range(0.3f, 1.4f);
                StartCoroutine(SoulFloat(t, to, 1.2f + i * 0.08f));
            }
        }
        private System.Collections.IEnumerator SoulFloat(Transform t, Vector3 to, float seconds)
        {
            Vector3 start = t.position;
            for (float k = 0f; k < 1f && t; k += Time.deltaTime / seconds)
            {
                t.position = Vector3.Lerp(start, to, k * k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.4f;
                t.Rotate(0f, 180f * Time.deltaTime, 0f);
                yield return null;
            }
            if (t) Destroy(t.gameObject);
        }

        private void OnSharkFx(FxEvent e)
        {
            if (e.Arg == 0)
            {
                if (_sharkViews.TryGetValue(e.PieceId, out var gone) && gone.T)
                {
                    WeaponFx.Game("WaterSplash", gone.T.position);
                    WeaponFx.Emit(WeaponFx.Foam, gone.T.position, 10, Vector3.up);
                    StartCoroutine(Dive(gone.T));
                }
                _sharkViews.Remove(e.PieceId);
                return;
            }
            if (!_sharkViews.TryGetValue(e.PieceId, out var v) || !v.T)
            {
                var root = new GameObject("FortressShark").transform;
                DontDestroyOnLoad(root.gameObject);
                Models.Attach("Shark", root, new Vector3(0.5f, 0.6f, 2f));
                float size = 1.85f;
                if (!string.IsNullOrEmpty(e.Key)) float.TryParse(e.Key, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out size);
                root.localScale = Vector3.one * Mathf.Clamp(size, 1.5f, 3.4f); // the game's shark is about 2 m; upgrades make it a sea monster
                root.position = e.From;
                Vector3 look = (e.To - e.From).Flat();
                if (look.sqrMagnitude > 0.01f) root.rotation = Quaternion.LookRotation(look);
                v = new SharkView { T = root, Born = Time.time, Seen = Time.time, JumpFrom = e.From, JumpTo = e.To, Pos = e.To, Face = e.To };
                Fx.Sound("FishCaught_Splash", e.From, 0.9f, AudioDistance.Long);
                WeaponFx.Game("WaterExplosion", e.From);
                WeaponFx.Game("WaterSplash", e.From);
            }
            else
            {
                v.Pos = e.From; v.Face = e.To; v.Seen = Time.time;
                if (!string.IsNullOrEmpty(e.Key) && float.TryParse(e.Key, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s)) v.T.localScale = Vector3.one * Mathf.Clamp(s, 1.5f, 3.4f);
            }
            _sharkViews[e.PieceId] = v;
        }

        private void SharkBite(Vector3 at)
        {
            foreach (var v in _sharkViews.Values)
                if (v.T && (v.T.position - at).sqrMagnitude < 5f * 5f) v.BiteUntil = Time.time + 0.35f;
        }

        private static IEnumerator Dive(Transform t)
        {
            for (float k = 0f; k < 1f && t; k += Time.deltaTime / 0.6f) { t.position += Vector3.down * Time.deltaTime * 2.5f; yield return null; }
            if (t) Destroy(t.gameObject);
        }

        // ---------- gatling: its bullets (a few pooled bars streaking out, not an object or a particle each) and its barrels ----------

        public const float BulletSpeed = 240f, BulletLength = 2.5f; // m/s, m
        private const int MaxBullets = 40;
        private class Shot { public Transform T; public Vector3 A, B; public float Start; }
        private readonly List<Shot> _shots = new List<Shot>();
        private int _nextShot;
        private float _nextGatlingSound, _mySpin;
        private readonly Dictionary<int, (float spin, float at)> _spins = new Dictionary<int, (float, float)>(); // a friend's barrels, from their bullets

        private void Bullet(Vector3 a, Vector3 b)
        {
            Shot s;
            if (_shots.Count < MaxBullets)
            {
                s = new Shot { T = WeaponFx.Piece(WeaponFx.Cyl, WeaponFx.Emissive("GatlingBullet", new Color(1f, 0.82f, 0.4f), 6f)) };
                DontDestroyOnLoad(s.T.gameObject);
                _shots.Add(s);
            }
            else s = _shots[_nextShot = (_nextShot + 1) % MaxBullets]; // the oldest
            s.A = a; s.B = b; s.Start = Time.time;
            s.T.gameObject.SetActive(true);
        }

        private void AnimateBullets(float now)
        {
            foreach (var s in _shots)
            {
                if (!s.T || !s.T.gameObject.activeSelf) continue;
                Vector3 d = s.B - s.A;
                float len = d.magnitude, head = (now - s.Start) * BulletSpeed, from = Mathf.Max(0f, head - BulletLength);
                if (from >= len) { s.T.gameObject.SetActive(false); continue; }
                s.T.SetPositionAndRotation(s.A + d / Mathf.Max(1e-4f, len) * from, Quaternion.FromToRotation(Vector3.up, len > 1e-4f ? d : Vector3.up));
                s.T.localScale = new Vector3(0.035f, Mathf.Max(0.01f, Mathf.Min(len, head) - from), 0.035f);
            }
        }

        // Every gatling in someone's hand turns its barrels (WeaponItems.Gatling) as fast as they're spun up: mine while I hold
        // the button, a friend's as their bullets say, running down once they stop.
        // The volley's tracers: one per barrel (capped: it's a look, not the damage), honeycomb round the aim, leaving in a
        // short random order instead of every one on the same frame (Owen: "有點隨機發射不是同時").
        private System.Collections.IEnumerator GatlingVolley(Vector3 from, Vector3 to, int count)
        {
            int n = Mathf.Clamp(count, 1, 12), barrels = Mathf.Max(1, count);
            for (int i = 0; i < n; i++)
            {
                Vector3 off = GatlingOffset(i, barrels, to - from);
                Bullet(from + off, to + Random.insideUnitSphere * 0.35f);
                if (Random.value < 0.5f) yield return null; // stagger
                else yield return new WaitForSeconds(Random.value * 0.05f);
            }
        }

        private void GatlingViews(bool firing)
        {
            float dt = Time.deltaTime;
            var vault = Loot.Instance ? Loot.Instance.ClientVault : null;
            float up = Mathf.Max(0.15f, GatlingSpinUp - WeaponMods.TraitAdd(Gear.Gatling, vault != null ? vault.ModOf(Loot.MyKey, Gear.Gatling, WeaponMods.Trait) : 0));
            _mySpin = firing ? Mathf.Min(1f, _mySpin + dt / up) : Mathf.Max(0f, _mySpin - dt / GatlingSpinDown);
            foreach (var p in PlayerManager.Players)
            {
                var held = p && p.Holding ? p.Holding.HeldItem : null;
                if (!WeaponItems.Is(held, out var g, out _) || g != Gear.Gatling || !(WeaponItems.ModelOf(held) is Transform model) || !(model.Find(WeaponItems.GatlingSpin) is Transform barrels)) continue;
                float spin = p == Player.LocalPlayer ? _mySpin : _spins.TryGetValue(p.ObjectId, out var s) ? Mathf.Max(0f, s.spin - (Time.time - s.at) / GatlingSpinDown) : 0f;
                if (spin > 0f) barrels.Rotate(0f, 0f, spin * 1100f * dt, Space.Self);
            }
        }

        private void AnimateViews()
        {
            float now = Time.time;
            AnimateBeams(now);
            AnimateBullets(now);
            bool wake = now >= _nextWake;
            if (wake) _nextWake = now + 0.04f;
            foreach (var kv in _sharkViews.ToList())
            {
                var v = kv.Value;
                var t = v.T;
                if (!t || now - v.Seen > 2f) { if (t) Destroy(t.gameObject); _sharkViews.Remove(kv.Key); continue; } // lost track: gone
                bool leaping = now - v.Born < 0.55f;
                bool biting = now < v.BiteUntil;
                Vector3 p;
                if (leaping)
                {
                    float k = Mathf.Clamp01((now - v.Born) / 0.55f);
                    p = Vector3.Lerp(v.JumpFrom, v.JumpTo, k);
                    p.y = v.JumpTo.y + Mathf.Sin(k * Mathf.PI) * 2.6f; // up out of the pool, then down on the bite
                    t.position = p;
                    Vector3 jumpLook = (v.JumpTo - v.JumpFrom).Flat();
                    if (jumpLook.sqrMagnitude > 0.01f) t.rotation = Quaternion.LookRotation(jumpLook) * Quaternion.Euler(-55f * (1f - k), 0f, 0f);
                    if (wake) WeaponFx.Emit(WeaponFx.Foam, p + Random.insideUnitSphere * 0.4f, 3, Vector3.up);
                    continue;
                }
                p = Vector3.MoveTowards(t.position, v.Pos, Time.deltaTime * SharkSpeed * 1.5f);
                p.y = Mathf.Max(WaterManager.GetWaterHeight(p), v.Pos.y) + 0.1f + Mathf.Sin(now * 3f + kv.Key) * 0.06f; // teeth above the surface (the sea, or its pool in the sand)
                if (biting)
                {
                    Vector3 toFace = (v.Face - p).Flat();
                    if (toFace.sqrMagnitude > 0.01f) p += toFace.normalized * 0.55f;
                    p.y += 0.35f;
                }
                bool moving = (p - t.position).sqrMagnitude > 1e-5f;
                t.position = p;
                Vector3 look = (v.Face - p).Flat();
                if (look.sqrMagnitude > 0.01f) t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(look) * (biting ? Quaternion.Euler(-22f, 0f, 0f) : Quaternion.identity), Time.deltaTime * (biting ? 14f : 6f));
                if (wake && (moving || biting))
                {
                    Vector3 back = -t.forward, side = t.right;
                    for (int s = -1; s <= 1; s += 2) // a V of foam peeling off the snout
                        WeaponFx.Emit(WeaponFx.Foam, p + t.forward * 0.6f + side * s * 0.3f, 1, default, null, Random.Range(0.4f, 0.7f), (back * 0.8f + side * s) * 1.6f, 1.1f);
                    if (biting) WeaponFx.Emit(WeaponFx.Drops, p + t.forward * 1.2f, 6, Vector3.up);
                    if (Random.value < 0.35f) WeaponFx.Emit(WeaponFx.Drops, p + t.forward * 0.9f, 2, Vector3.up + side * Random.Range(-1f, 1f));
                }
            }
        }

        private void ClearViews()
        {
            foreach (var b in _beams.Values) foreach (var t in new[] { b.Beam, b.Core, b.Hit }) if (t) Destroy(t.gameObject);
            _beams.Clear();
            foreach (var s in _sharkViews.Values) if (s.T) Destroy(s.T.gameObject);
            _sharkViews.Clear();
            if (WeaponFx.Instance) WeaponFx.Instance.Clear();
            Holster();
        }

        // ---------- the hand net (F): swing it at the bodies in front of you; they pile up in the bag ----------

        private bool _net;
        private GameObject _netView;
        private Transform _bag, _contents;
        private readonly List<(GameObject go, Mesh baked)> _catch = new List<(GameObject, Mesh)>();
        private float _swing, _nextSwing;
        private const int MaxShownFish = 12;
        private static readonly Vector3 NetHoop = new Vector3(0f, -0.12f, 0.962f), NetHang = new Vector3(0f, -0.954f, -0.301f).normalized;

        private void UpdateNetView()
        {
            var cam = GameInfo.CurCamera;
            if (!cam) return;
            if (!_netView) BuildNetView();
            if (_netView.transform.parent != cam.transform) _netView.transform.SetParent(cam.transform, false);
            _netView.SetActive(true);
            var (count, cap, _) = FortressNet.Instance ? FortressNet.Instance.ClientNet : (0, 5, 0);
            // A bigger net is a bigger net: hoop and bag grow with each team upgrade; the bag bulges as it fills.
            int size = cap >= FortressNet.Capacity[2] ? 2 : cap >= FortressNet.Capacity[1] ? 1 : 0;
            float fill = cap > 0 ? Mathf.Clamp01((float)count / cap) : 0f;
            _netView.transform.localScale = Vector3.one * (1f + 0.2f * size);
            _bag.localScale = new Vector3(1f + 0.08f * fill, 1f + 0.4f * fill, 1f + 0.08f * fill); // sags deeper, the rim stays in the hoop
            while (_catch.Count > count) DropCatch(_catch.Count - 1);                 // sold, or a new round
            while (_catch.Count < Mathf.Min(count, MaxShownFish)) AddCatch(null);    // caught while the net was away
            _swing = Mathf.MoveTowards(_swing, 0f, Time.deltaTime * 3.2f);
            float s = Mathf.Sin(_swing * Mathf.PI); // a scoop: forward and down, then back up
            float bob = Mathf.Sin(Time.time * 2.2f) * 0.006f;
            _netView.transform.localPosition = new Vector3(0.3f, -0.3f + bob - s * 0.12f, 0.45f + s * 0.25f);
            _netView.transform.localRotation = Quaternion.Euler(-10f + s * 55f, -12f, 0f);
        }

        private void BuildNetView()
        {
            _netView = new GameObject("FortressNetView");
            DontDestroyOnLoad(_netView);
            Models.Attach("HandNet", _netView.transform, new Vector3(0.05f, 0.05f, 0.8f), new Vector3(0f, 0f, 0.4f));
            var bagModel = Models.Attach("HandNetBag", _netView.transform, new Vector3(0.4f, 0.4f, 0.35f), new Vector3(0f, -0.3f, 0.9f));
            // The bag hangs from the hoop (Models/HandNet: hoop centre and the bag's hanging direction), so it stretches from
            // there: its local -Y is the way it hangs. The catch sits halfway down, unstretched.
            _bag = new GameObject("Bag").transform;
            _bag.SetParent(_netView.transform, false);
            _bag.localPosition = NetHoop;
            _bag.localRotation = Quaternion.FromToRotation(Vector3.down, NetHang);
            bagModel.transform.SetParent(_bag, true);
            _contents = new GameObject("Catch").transform;
            _contents.SetParent(_netView.transform, false);
            _contents.localPosition = NetHoop + NetHang * 0.22f;
            _contents.localRotation = _bag.localRotation;
            foreach (var r in _netView.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // A fish in the bag: a small copy of the body you scooped (or a stand-in), settling from the bottom up.
        private void AddCatch(Transform source)
        {
            if (!_contents || _catch.Count >= MaxShownFish) return;
            var go = new GameObject("Fish");
            go.transform.SetParent(_contents, false);
            Mesh baked = null;
            var smr = source ? source.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            var mf = source && !smr ? source.GetComponentInChildren<MeshFilter>() : null;
            if (smr) { baked = new Mesh(); smr.BakeMesh(baked); Mount(go, baked, smr.sharedMaterials); }
            else if (mf && mf.sharedMesh && mf.GetComponent<MeshRenderer>()) Mount(go, mf.sharedMesh, mf.GetComponent<MeshRenderer>().sharedMaterials);
            else Models.Attach("NetFish", go.transform, new Vector3(0.06f, 0.05f, 0.2f));
            int i = _catch.Count;
            float a = i * 137.5f * Mathf.Deg2Rad, rad = 0.02f + 0.01f * i; // the bag narrows toward the bottom: the first fish sit in the middle
            go.transform.localPosition = new Vector3(Mathf.Cos(a) * rad, -0.09f + 0.022f * i, Mathf.Sin(a) * rad);
            go.transform.localRotation = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(70f, 110f));
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            _catch.Add((go, baked));
        }

        private static void Mount(GameObject go, Mesh mesh, Material[] mats)
        {
            var holder = new GameObject("Mesh");
            holder.transform.SetParent(go.transform, false);
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            holder.AddComponent<MeshRenderer>().sharedMaterials = mats;
            var b = mesh.bounds;
            float k = 0.18f / Mathf.Max(0.01f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z))); // about 18 cm, whatever the fish
            holder.transform.localScale = Vector3.one * k;
            holder.transform.localPosition = -b.center * k;
        }

        private void DropCatch(int i)
        {
            var (go, baked) = _catch[i];
            if (go) Destroy(go);
            if (baked) Destroy(baked);
            _catch.RemoveAt(i);
        }

        private void HandleNetMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || Cursor.lockState != CursorLockMode.Locked || ChatManager.IsTyping || Time.time < _nextSwing) return;
            _nextSwing = Time.time + 0.45f;
            _swing = 1f;
            Fx.Sound("PunchSwoosh", Player.LocalPlayer.Transform.position, 0.6f, AudioDistance.Short);
            var cam = GameInfo.CurCamera;
            if (cam && InstanceFinder.IsClientStarted)
                InstanceFinder.ClientManager.Broadcast(new NetSwing { Origin = cam.transform.position, Dir = cam.transform.forward });
        }

        // Every client, from the host's scoop effect: my own catch goes into my bag (copied before the body is gone).
        public bool OnScoop(FxEvent e)
        {
            if (!Player.LocalPlayer || e.Who != Player.LocalPlayer.ObjectId || !NetOut || !_contents) return false;
            AddCatch(FishView(e.PieceId));
            return true;
        }

        private void NetHud()
        {
            var (count, cap, value) = FortressNet.Instance ? FortressNet.Instance.ClientNet : (0, 5, 0);
            string text = Hud.Keys($"<b>{L10n.T("netTitle")}</b> {count}/{cap}  <color={Hud.Hex(Hud.Gold)}>${value}</color>　{L10n.T("netKeys", NetKey.Value)}");
            if (_hudText.text != text)
            {
                _hudText.text = text;
                _hud.rectTransform.sizeDelta = new Vector2(Mathf.Max(460f, _hudText.GetPreferredValues(text).x + 48f) + HudPic, 70f);
            }
            float f = cap > 0 ? (float)count / cap : 0f;
            _hudBar.Set(f, count >= cap ? Hud.Bad : Hud.Accent);
        }

        // Auto-test: what's going on around `at` on the host.
        public string DebugState(Vector3 at) =>
            $"clouds {_clouds.Count} [{string.Join(", ", _clouds.Select(c => $"{(c.At - at).Flat().magnitude:F1}m"))}], poisoned {_poison.Count} [{string.Join(", ", _poison.Values.Select(p => p.Stacks))}], " +
            $"sharks {_sharks.Count} [{string.Join(", ", _sharks.Select(s => $"{(s.Pos - at).Flat().magnitude:F1}m from, {(s.Pos - (FortressIsland.Instance ? FortressIsland.Instance.Center : Vector3.zero)).Flat().magnitude:F1}m out"))}], vortices {_vortices.Count}";

        // Auto-test hooks.
        public void DebugNet(bool on) { if (on) Draw(true); else Holster(true); }
        public void DebugForgetHeld() => _heldModelOf = null;
    }
}
