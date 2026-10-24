using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FishNet;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace HtfFortress
{
    // The absorber, a special weapon (only the smithy sells it, once the save's best round has beaten the first boss; Owen's
    // idea): it sucks in anything lying about and fires what it holds. A fortress weapon in it is copied (it fires as that
    // weapon; a rarer absorber lifts it to its own rarity), a boss's trophy gives that boss's move, anything else is shot out
    // as a copy of itself, the harder the more it's worth. R spits the real thing back out, flung the way the hook throws;
    // the right button sucks in something new and spits the old one out on the way. What each player's absorber holds is
    // theirs (kept in the vault, like their mods), so it stays through hotbar swaps, dying and saving.
    public partial class Weapons
    {
        public const float SuckRange = 8f, SuckAngle = 30f, SuckEvery = 0.5f;
        // The final gun (Owen: "吸收槍應該傷害要很高，因為他是最終槍"): everything its holder hits with it (what it copies,
        // an item shot, a trophy's move) does this much to a fortress fish. A boss takes it as it is, so a boss stays a fight.
        public const float AbsorberBoost = 2.5f;
        private static bool HoldsAbsorber(Player p) => p && WeaponItems.Held(p, out var g, out _) && g == Gear.Absorber;
        // The absorber's inputs past the left button's 0-2: the right button pressed, held, let go; R.
        public const byte RightDown = 3, RightHeld = 4, RightUp = 5, SpitKey = 6;

        // An item shot for what the item's worth: x2 for a $50 fish, x4 at $350, x8 at $6,350 (the hook's single throw grows
        // faster: FlingMult).
        public static float ShotMult(int worth) => 1f + Mathf.Log(1f + Mathf.Max(0, worth) / 50f, 2f);

        // What a trophy's move takes to come back, in seconds for a common absorber (rarer ones as much quicker as their shots).
        public static float MoveSeconds(EnemyKind boss)
        {
            switch (boss)
            {
                case EnemyKind.Warden: return 2.5f;
                case EnemyKind.Urchin: return 2.6f;
                case EnemyKind.Angler: return 6f;
                case EnemyKind.Steed: return 3.5f;
                case EnemyKind.Stonefish: return 3.5f;
                case EnemyKind.Skyfish: return 5f;
                case EnemyKind.Kraken: return 6f;
                case EnemyKind.Voxel: return 7f;
                case EnemyKind.Koi: return 5f;
                default: return 8f; // the Eye
            }
        }
        // A move's damage, in absorber shots (what the HUD shows, what the host deals).
        public static float MoveMult(EnemyKind boss)
        {
            switch (boss)
            {
                case EnemyKind.Warden: return 6f;     // the one fish clamped (and under a third of its health: cut down)
                case EnemyKind.Urchin: return 1.6f;   // each needle, through two fish
                case EnemyKind.Angler: return 4f;     // the burst, to every fish drawn in
                case EnemyKind.Steed: return 5f;      // the stomp, within 5 m
                case EnemyKind.Stonefish: return 2.5f; // the spires, and three stacks of poison
                case EnemyKind.Skyfish: return 2.5f;  // each of six bombs
                case EnemyKind.Kraken: return 1.5f;   // a second, for 3 s, to everything held
                case EnemyKind.Voxel: return 5f;      // what isn't deleted
                case EnemyKind.Koi: return 1f;        // every quarter second for 1.5 s
                default: return 6f;                   // the beam (the spears: two each)
            }
        }

        private readonly Dictionary<int, float> _nextSuck = new Dictionary<int, float>(), _nextSuckFx = new Dictionary<int, float>();

        private Gun GunOf(Player who, Gear g) => _guns.TryGetValue((who.ObjectId, g), out var gun) ? gun : _guns[(who.ObjectId, g)] = new Gun { Gear = g };

        // Auto-test: every weapon of `who`'s ready to fire again.
        public void DebugReady(Player who) { foreach (var kv in _guns.Where(kv => kv.Key.Item1 == who.ObjectId)) kv.Value.ReadyAt = 0f; _nextSuck.Remove(who.ObjectId); }

        // A fortress weapon (not one of the game's guns, not another special): the absorber fires as it.
        internal static bool Copies(Item prefab, out Gear g, out Rarity r) => WeaponItems.Is(prefab, out g, out r) && !GearCatalog.IsGameGun(g) && !GearCatalog.IsSpecial(g);
        // What `who`'s absorber fires a weapon it copies as: a rarer absorber lifts it to its own rarity.
        public static Rarity CopyRarity(Rarity copied, Rarity absorber) => (Rarity)Mathf.Max((int)copied, (int)absorber);

        // Host: whether `who` fires `g` right now: in hand, or copied by the absorber in hand.
        private static bool Wields(Player who, Gear g)
        {
            if (!WeaponItems.Held(who, out var hg, out _)) return false;
            if (hg == g) return true;
            var held = hg == Gear.Absorber && Loot.Instance ? Loot.Instance.Vault.AbsorbedOf(Loot.KeyOf(who)) : null;
            return held != null && Copies(GameInfo.GetSpawnable(held.Id), out var cg, out _) && cg == g;
        }

        // Host: one input from a player with an absorber in hand.
        private void AbsorberFire(Player who, byte phase, Vector3 o, Vector3 d, Rarity r)
        {
            var vault = Loot.Instance.Vault;
            ulong key = Loot.KeyOf(who);
            var held = vault.AbsorbedOf(key);
            if (phase == SpitKey)
            {
                if (held == null) return;
                vault.Absorbed.Remove(key);
                SpitOut(who, held, Muzzle(o, d), d, r);
                Loot.Instance.SaveVault();
                return;
            }
            if (phase == RightDown || phase == RightHeld || (held == null && phase <= 1)) { Suck(who, key, o, d, r, held); return; }
            if (phase >= RightUp || held == null) return;
            var prefab = GameInfo.GetSpawnable(held.Id);
            if (Copies(prefab, out var cg, out var cr))
            {
                FireAs(who, phase, o, d, cg, CopyRarity(cr, r));
                if (phase == 0) AutoTest.Count("absorber_copy_" + cg);
                return;
            }
            var gun = GunOf(who, Gear.Absorber);
            if (phase == 2 || Time.time < gun.ReadyAt) return;
            float v = vault.WeaponValue(key, Gear.Absorber, r) * Scale;
            float cd = BossTrophies.Is(prefab, out var boss) ? TrophyMove(boss, who, o, d, v) : ItemShot(who, held, o, d, v);
            gun.ReadyAt = Time.time + cd * GearCatalog.Cooldown(Gear.Absorber, r) / GearCatalog.Cooldown(Gear.Absorber, Rarity.Common)
                                         * WeaponMods.TimeMult(vault.ModOf(key, Gear.Absorber, WeaponMods.Rate));
            SendState(who, gun);
            AutoTest.Count(BossTrophies.Is(prefab, out _) ? "absorber_move_" + boss : "absorber_shot");
        }

        // The nearest thing to the aim in a cone ahead (8 m, further with the range mod; wider with the trait) goes in. Holding something already (the
        // right button): the new one comes in and the old one is spat out on the way.
        private void Suck(Player who, ulong key, Vector3 o, Vector3 d, Rarity r, StoredItem held)
        {
            float now = Time.time;
            var vault = Loot.Instance.Vault;
            float reach = SuckReach(vault, key), cone = SuckAngle + WeaponMods.TraitAdd(Gear.Absorber, vault.ModOf(key, Gear.Absorber, WeaponMods.Trait));
            Vector3 muzzle = Muzzle(o, d);
            if (!_nextSuckFx.TryGetValue(who.ObjectId, out float fx) || now >= fx) { _nextSuckFx[who.ObjectId] = now + 0.25f; AbsFx("suck", muzzle, muzzle + d * reach, who); }
            if (_nextSuck.TryGetValue(who.ObjectId, out float next) && now < next) return;
            var it = SuckTarget(o, d, reach, cone, who);
            var saved = it ? Loot.SavedOf(it) : null;
            if (saved == null) return;
            _nextSuck[who.ObjectId] = now + SuckEvery;
            vault.Absorbed[key] = new StoredItem { Saved = saved, Dead = it.Creature && it.Creature.IsDead, Worth = it.TotalWorth };
            Swallow.Server(it, muzzle, Gulp.Absorber);
            if (held != null) SpitOut(who, held, muzzle, d, r);
            Loot.Instance.SaveVault();
            AutoTest.Count("absorbed");
        }

        private static float SuckReach(VaultData vault, ulong key) => SuckRange * WeaponMods.ReachMult(vault.ModOf(key, Gear.Absorber, WeaponMods.Reach));

        // Anything the hook could take (lying about, not held, no live fish), but not another absorber.
        private Item SuckTarget(Vector3 o, Vector3 d, float reach, float cone, Player who)
        {
            Item best = null;
            float bestScore = float.MaxValue;
            foreach (var col in Physics.OverlapSphere(o, reach, ~0, QueryTriggerInteraction.Ignore))
            {
                var it = ItemManager.Get(col);
                if (!it || !Hookable(it, who) || (WeaponItems.Is(it, out var g, out _) && GearCatalog.IsSpecial(g))) continue;
                Vector3 to = it.transform.position - o;
                float angle = Vector3.Angle(d, to);
                if (to.magnitude > reach || angle > cone) continue;
                float score = angle + to.magnitude * 2f;
                if (score < bestScore) { best = it; bestScore = score; }
            }
            return best;
        }

        // The real thing back out at the muzzle, flung ahead as the hook throws: as hard as it's worth.
        private void SpitOut(Player who, StoredItem s, Vector3 muzzle, Vector3 d, Rarity r)
        {
            var it = Loot.Spawn(s, muzzle + d * 0.4f);
            AutoTest.Count("absorber_spat");
            if (!it) return;
            float dmg = Loot.Instance.Vault.WeaponValue(Loot.KeyOf(who), Gear.Absorber, r) * Scale;
            StartCoroutine(SpitNext(who, it, muzzle, d, dmg, r == Rarity.Legendary));
        }

        private IEnumerator SpitNext(Player who, Item it, Vector3 from, Vector3 d, float dmg, bool legend)
        {
            yield return null; // spawned: its body moves from the next frame
            if (!it || !it.Rig || !it.RigidbodySync || !who) yield break;
            it.RigidbodySync.StartSimulateLocal();
            FlingOut(who, it, from, d, dmg, legend);
        }

        // Anything else: a copy of it shot out (the thing stays inside) into the first fish on the line.
        private float ItemShot(Player who, StoredItem held, Vector3 o, Vector3 d, float v)
        {
            Vector3 end = Reach(o, d, ShotRange);
            var c = Along(o, end, ShotRadius * 1.25f).FirstOrDefault();
            AbsFx("shot", Muzzle(o, d), c ? Body(c) : end, who, held.Id);
            if (c)
            {
                HitNumber(c, Hurt(c, v * ShotMult(held.Worth), who));
                if (!IsBoss(c)) Siege.Instance.ServerPush(c, d.Flat().normalized * 2.5f);
            }
            return GearCatalog.Cooldown(Gear.Absorber, Rarity.Common);
        }

        private static int MaxOf(Creature c) => FortressPlugin.Instance && FortressPlugin.Instance.MaxHpOf.TryGetValue(c, out int m) ? m : c.MaxHp;
        private static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out var g, 60f, GameInfo.LevelLayer)) p = g.point;
            p.y = Mathf.Max(p.y, WaterManager.GetWaterHeight(p));
            return p;
        }
        private List<Creature> Within(Vector3 at, float r) => Targets().Where(c => (c.transform.position - at).Flat().sqrMagnitude <= r * r).ToList();

        // Poison stacks onto a fish (they then work like the gas's).
        private void Envenom(Creature c, int stacks, float dmg, Player who)
        {
            float now = Time.time;
            if (!_poison.TryGetValue(c, out var p)) _poison[c] = p = new Poison();
            p.Stacks = Mathf.Min(MaxPoisonStacks, p.Stacks + stacks); p.Until = now + PoisonLinger; p.NextStack = now + 1f;
            p.Dps = Mathf.Max(p.Dps, dmg * 0.04f / Mathf.Max(0.01f, Scale)); p.Owner = who ? who.ObjectId : 0;
        }

        // A boss's trophy in the absorber: that boss's move, at a player's size. `v`: one absorber shot's damage now.
        // Returns its cooldown (seconds, for a common absorber).
        private float TrophyMove(EnemyKind boss, Player who, Vector3 o, Vector3 d, float v)
        {
            Vector3 muzzle = Muzzle(o, d), fwd = d.Flat().sqrMagnitude > 1e-4f ? d.Flat().normalized : who.Transform.forward.Flat().normalized;
            float hit = v * MoveMult(boss);
            switch (boss)
            {
                case EnemyKind.Warden:
                {
                    // The guillotine: the first fish ahead (16 m) is clamped; one (not a boss) left under a third of its health is cut down.
                    Vector3 end = Reach(o, d, 16f);
                    var c = Along(o, end, 1f).FirstOrDefault();
                    AbsFx("warden", muzzle, c ? Body(c) : end, who);
                    if (!c) return 1f;
                    HitNumber(c, Hurt(c, hit, who));
                    if (c && c._hp.Value > 0 && !IsBoss(c) && c._hp.Value * 3 < MaxOf(c)) HitNumber(c, Hurt(c, c._hp.Value, who));
                    break;
                }
                case EnemyKind.Urchin:
                {
                    // Every needle at once: twelve all round, 14 m out over the ground (rises and dips don't stop them), each through two fish.
                    Vector3 at = who.Transform.position + Vector3.up;
                    const int n = 12;
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 dir = Quaternion.AngleAxis(360f * i / n, Vector3.up) * fwd, end = at + dir * 14f;
                        foreach (var c in Targets().Where(c => DistToSegment(c.transform.position.Flat(), at.Flat(), end.Flat()) <= 1f)
                                                   .OrderBy(c => (c.transform.position - at).Flat().sqrMagnitude).Take(2))
                            HitNumber(c, Hurt(c, hit, who));
                    }
                    AbsFx("urchin", at, at + fwd, who, n);
                    break;
                }
                case EnemyKind.Angler:
                {
                    // A lantern thrown out: the fish within 10 m of it are drawn in to it, then its light bursts on them and holds them 1.5 s.
                    Vector3 at = Land(o, d, 20f);
                    AbsFx("angler", muzzle, at, who);
                    // Drawn to a ring round it, each from its own side (piled onto one spot, big fish knock each other away).
                    foreach (var c in Within(at, 10f).Where(c => !IsBoss(c)))
                    {
                        Vector3 off = (c.transform.position - at).Flat();
                        if (off.magnitude > 1.8f) Siege.Instance.ServerPull(c, at + off.normalized * 1.8f, 0.6f);
                    }
                    StartCoroutine(After(0.8f, () =>
                    {
                        foreach (var c in Within(at, 5f)) { HitNumber(c, Hurt(c, hit, who)); Siege.Instance.Slow(c, IsBoss(c) ? 0.5f : 1f, 1.5f); }
                    }));
                    break;
                }
                case EnemyKind.Steed:
                {
                    // A golden stomp where you aim: everything within 5 m is hit and thrown back out of it.
                    Vector3 at = Land(o, d, 18f);
                    AbsFx("steed", at, at, who);
                    foreach (var c in Within(at, 5f))
                    {
                        HitNumber(c, Hurt(c, hit, who));
                        Vector3 away = (c.transform.position - at).Flat();
                        if (!IsBoss(c)) Siege.Instance.ServerPush(c, (away.sqrMagnitude > 0.01f ? away.normalized : fwd) * 7f);
                    }
                    break;
                }
                case EnemyKind.Stonefish:
                {
                    // Venom spires up out of the ground ahead (14 m): every fish by the line is hit and poisoned (three stacks).
                    Vector3 a = Ground(who.Transform.position + fwd * 1.5f), b = Ground(a + fwd * 14f);
                    AbsFx("stone", a, b, who);
                    foreach (var c in Targets().Where(c => DistToSegment(c.transform.position.Flat(), a.Flat(), b.Flat()) <= 1.8f))
                    {
                        HitNumber(c, Hurt(c, hit, who));
                        Envenom(c, 3, hit, who);
                    }
                    break;
                }
                case EnemyKind.Skyfish:
                {
                    // An air raid: a streak overhead down the aim, then six bombs along it one after another, each 3 m round.
                    var drops = Enumerable.Range(0, 6).Select(i => Ground(who.Transform.position + fwd * (5f + 4f * i))).ToList();
                    AbsFx("sky", drops[0] + Vector3.up * 6f, drops[5] + Vector3.up * 6f, who);
                    for (int i = 0; i < drops.Count; i++)
                    {
                        var p = drops[i];
                        StartCoroutine(After(0.35f + 0.12f * i, () =>
                        {
                            AbsFx("bomb", p, p, who);
                            foreach (var c in Targets().Where(c => (Body(c) - p).sqrMagnitude <= 9f)) HitNumber(c, Hurt(c, hit, who));
                        }));
                    }
                    break;
                }
                case EnemyKind.Kraken:
                {
                    // Tentacles up where you aim: for 3 s everything within 5 m is held (a boss slowed), squeezed and pulled in.
                    Vector3 at = Land(o, d, 18f);
                    AbsFx("kraken", at - fwd * 3f, at, who);
                    foreach (var c in Within(at, 5f)) Siege.Instance.Slow(c, IsBoss(c) ? 0.5f : 1f, 3f);
                    _vortices.Add(new Vortex { At = at, Until = Time.time + 3f, Dps = hit / Mathf.Max(0.01f, Scale), Owner = who.ObjectId });
                    break;
                }
                case EnemyKind.Voxel:
                {
                    // Select, delete: an 8 m box where you aim; a moment later every fish in it (not a boss) with less health
                    // than ten shots is deleted outright, and the rest are hit.
                    Vector3 at = Land(o, d, 20f), side = Vector3.Cross(Vector3.up, fwd);
                    const float half = 4f;
                    AbsFx("voxel", at, at + fwd, who, Mathf.RoundToInt(half * 100f));
                    StartCoroutine(After(0.8f, () =>
                    {
                        AbsFx("voxelDelete", at, at + fwd, who, Mathf.RoundToInt(half * 100f));
                        foreach (var c in Targets())
                        {
                            Vector3 rel = (c.transform.position - at).Flat();
                            if (Mathf.Abs(Vector3.Dot(rel, fwd)) > half || Mathf.Abs(Vector3.Dot(rel, side)) > half) continue;
                            if (!IsBoss(c) && c._hp.Value <= v * 10f) HitNumber(c, Hurt(c, c._hp.Value, who));
                            else HitNumber(c, Hurt(c, hit, who));
                        }
                    }));
                    break;
                }
                case EnemyKind.Koi:
                {
                    // Dragon breath: a cone of fire ahead (12 m, 25° each side) for 1.5 s, burning everything in it.
                    Vector3 from = who.Transform.position;
                    AbsFx("koi", muzzle + fwd * 1.5f, muzzle + fwd * 12f, who, 0, 1500); // a step ahead: fire at the camera fills the view
                    StartCoroutine(Breathe(from, fwd, hit, who));
                    break;
                }
                default:
                {
                    // The end's gaze: a beam 45 m down the aim through everything on it, then spears out of the sky on the five
                    // fish nearest where it ends.
                    Vector3 end = Reach(o, d, 45f);
                    foreach (var c in Along(o, end, 1.6f)) HitNumber(c, Hurt(c, hit, who));
                    AbsFx("eye", muzzle, end, who);
                    foreach (var c in Targets().OrderBy(c => (c.transform.position - end).sqrMagnitude).Take(5).ToList())
                    {
                        var t = c;
                        AbsFx("spear", t.transform.position, t.transform.position, who);
                        StartCoroutine(After(0.55f, () => { if (t) HitNumber(t, Hurt(t, v * 2f, who)); }));
                    }
                    break;
                }
            }
            return MoveSeconds(boss);
        }

        private IEnumerator Breathe(Vector3 from, Vector3 fwd, float hit, Player who)
        {
            for (int i = 0; i < 6; i++)
            {
                foreach (var c in Targets())
                {
                    Vector3 to = (c.transform.position - from).Flat();
                    if (to.sqrMagnitude <= 12f * 12f && Vector3.Angle(fwd, to) <= 25f) HitNumber(c, Hurt(c, hit, who));
                }
                yield return new WaitForSeconds(0.25f);
            }
        }

        // The absorber's effects (BossMoves.PlayAbsorber draws them): PieceId and Arg as each move needs.
        private static void AbsFx(string key, Vector3 a, Vector3 b, Player who, int pieceId = 0, int arg = 0)
        {
            Fx.Queue(new FxEvent { Kind = FxKind.Absorber, Key = key, From = a, To = b, PieceId = pieceId, Arg = arg, Who = who ? who.ObjectId : 0 });
        }

        // =====================================================================================================
        // client: what each absorber holds (in the glass on top of it), and on my screen
        // =====================================================================================================

        // The glass on the absorber's model, in its mesh's units (tools/make_weapon_models3.py: DOME_Y, DOME_Z, DOME_R).
        private static readonly Vector3 DomeAt = new Vector3(0f, 0.21f, 0.02f);
        private const float DomeR = 0.055f;
        private static Material _glass;
        private readonly Dictionary<Transform, (byte id, Transform root)> _chambers = new Dictionary<Transform, (byte, Transform)>();

        // Every absorber in someone's hand shows what its holder's absorber holds, turning in the glass; one nobody holds is empty.
        private void AbsorberViews()
        {
            var vault = Loot.Instance ? Loot.Instance.ClientVault : null;
            var seen = new HashSet<Transform>();
            if (vault != null)
                foreach (var p in PlayerManager.Players)
                {
                    var held = p && p.Holding ? p.Holding.HeldItem : null;
                    if (!WeaponItems.Is(held, out var g, out _) || g != Gear.Absorber || !(WeaponItems.ModelOf(held) is Transform model)) continue;
                    seen.Add(model);
                    Show(model, vault.AbsorbedOf(Loot.KeyOf(p))?.Id ?? 0);
                }
            foreach (var model in _chambers.Keys.ToList())
            {
                if (!model) { _chambers.Remove(model); continue; }
                if (!seen.Contains(model)) Show(model, 0);
                if (_chambers[model].root && _chambers[model].root.Find("Look") is Transform look) look.localRotation = Quaternion.Euler(15f, Time.time * 80f, 0f);
            }
        }

        private void Show(Transform model, byte id)
        {
            if (_chambers.TryGetValue(model, out var ch) && ch.id == id && ch.root) return;
            if (ch.root) Destroy(ch.root.gameObject);
            var root = new GameObject("AbsorberChamber").transform;
            root.SetParent(model, false);
            root.localPosition = DomeAt;
            if (!_glass) _glass = BossVfx.Wash(new Color(0.75f, 0.92f, 1f, 0.2f));
            WeaponFx.Piece(WeaponFx.Ball, _glass, root).localScale = Vector3.one * DomeR * 2f;
            if (id != 0 && LookOf(id, DomeR * 1.5f) is Transform look) { look.SetParent(root, false); look.name = "Look"; }
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = model.gameObject.layer;
            _chambers[model] = (id, root);
        }

        // A plain copy of what an item looks like (its meshes in their colours, nothing alive in it), `size` across, at the origin.
        internal static Transform LookOf(byte id, float size)
        {
            var prefab = GameInfo.GetSpawnable(id);
            if (!prefab) return null;
            var root = new GameObject("AbsorbedLook").transform;
            var inner = new GameObject("Inner").transform;
            inner.SetParent(root, false);
            var toRoot = prefab.transform.worldToLocalMatrix;
            var box = new Bounds();
            bool any = false;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                var mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>() is MeshFilter mf ? mf.sharedMesh : null;
                if (!mesh) continue;
                var m = toRoot * r.transform.localToWorldMatrix;
                var part = new GameObject("Part", typeof(MeshFilter), typeof(MeshRenderer)).transform;
                part.SetParent(inner, false);
                part.localPosition = m.GetColumn(3);
                part.localRotation = m.rotation;
                part.localScale = m.lossyScale;
                part.GetComponent<MeshFilter>().sharedMesh = mesh;
                var pr = part.GetComponent<MeshRenderer>();
                pr.sharedMaterials = r.sharedMaterials;
                pr.shadowCastingMode = ShadowCastingMode.Off;
                var b = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (any) box.Encapsulate(p); else { box = new Bounds(p, Vector3.zero); any = true; }
                }
            }
            if (!any) { Destroy(root.gameObject); return null; }
            float k = size / Mathf.Max(0.01f, Mathf.Max(box.size.x, Mathf.Max(box.size.y, box.size.z)));
            inner.localScale = Vector3.one * k;
            inner.localPosition = -box.center * k;
            return root;
        }

        // What my absorber holds and what it does with it: a line over the bottom panel's, and the banner at the top.
        private UnityEngine.UI.Image _absTop;
        private TextMeshProUGUI _absTopText;

        private string AbsorberLines(Rarity r, out string banner)
        {
            var vault = Loot.Instance.ClientVault;
            ulong me = Loot.MyKey;
            var held = vault.AbsorbedOf(me);
            var siege = Siege.Instance;
            float scale = Weapons.Scale; // the wave no longer scales player damage
            int shot = Mathf.RoundToInt(vault.WeaponValue(me, Gear.Absorber, r) * scale);
            if (held == null) { banner = L10n.T("absorberEmpty"); return L10n.T("absorberEmptyHow", SuckReach(vault, me).ToString("0.#")); }
            var prefab = GameInfo.GetSpawnable(held.Id);
            string name = prefab ? prefab.GetName() : "#" + held.Id;
            string what;
            if (Copies(prefab, out var cg, out var cr))
            {
                var rr = CopyRarity(cr, r);
                name = GearCatalog.Label(cg, cr);
                what = L10n.T("absorberCopy", GearCatalog.Label(cg, rr)) + "\n" + vault.EffectFor(me, cg, rr);
            }
            else if (BossTrophies.Is(prefab, out var boss))
                what = $"<b>{L10n.T("absMove" + boss)}</b>：{L10n.T("absMoveFx" + boss, Mathf.RoundToInt(shot * MoveMult(boss)))}";
            else what = L10n.T("absorberShot", Mathf.RoundToInt(shot * ShotMult(held.Worth)), held.Worth.ToString("N0"), ShotMult(held.Worth).ToString("0.#"));
            banner = L10n.T("absorberIn", name) + "　<size=80%>" + what.Split('\n')[0] + "</size>";
            return what + "\n" + L10n.T("absorberBoost", AbsorberBoost.ToString("0.#")) + "\n" + L10n.T("absorberSpit", FlingMult(held.Worth).ToString("0.#"));
        }

        private void AbsorberTop(string banner)
        {
            if (banner == null) { if (_absTop) _absTop.gameObject.SetActive(false); return; }
            if (!_absTop)
            {
                _absTop = Hud.Box(Hud.Root, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(600f, 46f), Hud.Panel, 16f);
                _absTopText = Hud.Label(_absTop.transform, "", 22f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 40f), TextAlignmentOptions.Center);
            }
            _absTop.gameObject.SetActive(true);
            if (_absTopText.text == banner) return;
            _absTopText.text = banner;
            _absTop.rectTransform.sizeDelta = new Vector2(_absTopText.GetPreferredValues(banner).x + 48f, 46f);
        }

        // Whose heat and cooldown the bar shows: the weapon my absorber copies, or the one in hand.
        private static Gear BarGear(Gear g)
        {
            var held = g == Gear.Absorber && Loot.Instance ? Loot.Instance.ClientVault.AbsorbedOf(Loot.MyKey) : null;
            return held != null && Copies(GameInfo.GetSpawnable(held.Id), out var cg, out _) ? cg : g;
        }

        // Right button and R, besides the left (HandleMouse): sent while the absorber is in hand.
        private bool _rightHeld;
        private float _nextRight;
        private void AbsorberKeys(bool can)
        {
            var mouse = Mouse.current;
            bool right = can && mouse != null && mouse.rightButton.isPressed;
            if (right && (!_rightHeld || Time.time >= _nextRight)) { Send(_rightHeld ? RightHeld : RightDown); _nextRight = Time.time + 0.1f; }
            else if (!right && _rightHeld) Send(RightUp);
            _rightHeld = right;
            var kb = Keyboard.current;
            if (can && kb != null && kb.rKey.wasPressedThisFrame) Send(SpitKey);
        }
    }

    // The absorber in hand doesn't aim down its sights: the right button sucks in.
    [HarmonyPatch(typeof(Weapon), nameof(Weapon.SecondaryInput))]
    internal static class AbsorberNoAim
    {
        private static bool Prefix(Weapon __instance) => !(WeaponItems.Is(__instance, out var g, out _) && g == Gear.Absorber);
    }
}
