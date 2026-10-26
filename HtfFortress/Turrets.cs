using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using FishNet;
using UnityEngine;

namespace HtfFortress
{
    // Turrets and traps, all decided on the host; damage goes through the game's own Creature.ServerChangeHp and
    // visuals through Fx so every client sees them.
    //   Gun: single target, fast.        Cannon: lobbed ball, splash.     Harpoon: pierces a line, slows.
    //   Mortar: long range, delayed splash (no close targets).            Tesla: chains between enemies, x2 on swimmers.
    //   Spikes: hurt walkers standing on them.
    public class Turrets : MonoBehaviour
    {
        public static ConfigEntry<float> DamageMultiplier;
        public static float OverclockUntil;

        private readonly Dictionary<int, float> _nextFire = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _nova = new Dictionary<int, float>(); // Lv20 overdrive: a ring of detonations
        // A storm gun fires ~13 times a second (27 overclocked): one tracer, carrying the damage since the last, every 0.15 s is
        // all anyone can see, and keeps every client from building a cube, a sound and a damage number per bullet.
        private readonly Dictionary<int, (float next, int dmg)> _tracer = new Dictionary<int, (float, int)>();
        private float _nextSpikeTick;

        // True while turrets (and spikes, and the fire they light) deal their damage: a boss's guard knows them apart.
        public static bool Firing { get; private set; }

        private void Update()
        {
            if (!InstanceFinder.IsServerStarted || !Siege.Instance.ServerActive || FortressPlugin.Frozen) return;
            if (Time.time >= _nextMend) { _nextMend = Time.time + 1f; Mend(); } // between waves too
            var alive = FortressPlugin.Alive;
            if (alive.Count == 0) return;
            Firing = true;
            try
            {
                foreach (var p in BuildSystem.Instance.ServerPieces)
                    if (Catalog.IsTurret(p.Type) && !Bosses.Instance.IsSilenced(p.Id)) Fire(p, alive);
                if (Time.time >= _nextSpikeTick) { _nextSpikeTick = Time.time + 0.5f; Spikes(alive); BurnTick(); }
            }
            finally { Firing = false; }
        }

        private static bool Alive(Creature c) => c && c._hp.Value > 0 && !Siege.Instance.IsHidden(c) && !Bosses.Entering(c);

        private Vector3 _from; // the turret firing now (shield bearers block hits from their front)

        private int _owner = -1;

        private static float SoulTurretMult => Loot.Instance && Loot.Instance.Vault != null ? Loot.Instance.Vault.TurretDamageMult : 1f;
        private int Dmg(int baseDmg, Creature c, Catalog.TurretStats s)
        {
            Stats.TurretHit(c, _owner);
            var sv = Loot.Instance ? Loot.Instance.Vault : null;
            float d = baseDmg * DamageMultiplier.Value * (sv != null ? sv.TurretDamageMult : 1f) * (1f + Perks.Team(Gear.Gunpowder));
            if (sv != null && sv.CritChance > 0f && Random.value < sv.CritChance) d *= 2f;
            if (Perks.TeamLegend(Gear.Scope) && Random.value < 0.1f) { d *= 3f; AutoTest.Count("legend_crits"); Perks.Proc(Gear.Scope, c.transform.position); } // legendary: 10% crits
            if (Perks.TeamLegend(Gear.FrostCore) && Random.value < 0.06f && Siege.Instance.TryFreeze(c)) { AutoTest.Count("legend_freezes"); Perks.Proc(Gear.FrostCore, c.transform.position); } // legendary: freeze (capped per target, bosses only slowed: Siege.TryFreeze)
            if (Enemies.Instance.TryGet(c, out var st) && st.Kind == EnemyKind.Shield)
            {
                Vector3 toTurret = _from - c.transform.position; toTurret.y = 0f;
                if (toTurret.sqrMagnitude > 0.01f && Vector3.Angle(c.transform.forward, toTurret) < 70f)
                {
                    if (Perks.TeamLegend(Gear.ApRounds)) Perks.Proc(Gear.ApRounds, c.transform.position); // legendary AP goes straight through
                    else { d *= 0.2f; AutoTest.Count("shield_blocks"); }
                }
            }
            if (Enemies.Instance.TryGet(c, out var tag) && (tag.Kind == EnemyKind.Tank || EnemyCatalog.IsBoss(tag.Kind))) d *= s.BossMult * (1f + Perks.Team(Gear.ApRounds));
            Siege.Instance.Slow(c, Perks.Team(Gear.FrostCore), 1.5f);
            Siege.Instance.Slow(c, s.Slow, s.SlowTime); // a branch's slow (hail, shockwave, conduction, blue flame, harpoons)
            Ignite(c); // Incendiary Rounds (fortress gear): every turret hit sets the fish burning
            if (s.WaterMult != 1f && c.transform.position.y < WaterManager.GetWaterHeight(c.transform.position) + 0.3f) d *= s.WaterMult;
            if (Siege.Instance.IsBubbled(c)) d *= 1.5f; // bubble gun
            if (Weapons.IsNetted(c)) d *= Weapons.NettedMult; // a legendary net
            return Mathf.Max(1, Mathf.RoundToInt(d));
        }

        private void Fire(PieceState p, List<Creature> alive)
        {
            if (!Catalog.Shoots(p.Type)) return; // the bubble shields, the repair tower mends
            _nextFire.TryGetValue(p.Id, out float next);
            if (Time.time < next) return;
            var s = Catalog.Turret(p.Type, p.Level, p.Branch);
            s.Range *= (1f + Perks.Team(Gear.Scope)) * Bosses.RangeAt(p.Pos);
            if (Time.time < OverclockUntil) s.Interval *= 0.5f; // an Overclock chip was used
            if (p.Type == PieceType.Tesla) s.Chain += Mathf.RoundToInt(Perks.Team(Gear.TeslaCoil));
            _owner = BuildSystem.Instance.OwnerOf(p.Id);
            _from = p.Pos;
            Vector3 muzzle = p.Pos + Vector3.up * Catalog.MuzzleHeight(p.Type, p.Level); // where its model's muzzle is

            // Range is measured flat (a cylinder): a turret up on a rise reaches the beach below it, and one on the ground the
            // flyers over it (the playtest's high turret couldn't hit what was under it).
            Creature target = null;
            float best = float.MaxValue, min = s.MinRange * s.MinRange, range2 = s.Range * s.Range;
            bool IsBoss(Creature c) => Enemies.Instance.TryGet(c, out var bt) && EnemyCatalog.IsBoss(bt.Kind);
            // A boss is a big target: ordinary towers get a longer reach against it, so it isn't sniper-only.
            float Limit(Creature c) => IsBoss(c) ? range2 * 2.56f : range2;
            // On one of the voxel fish's glitch tiles its lock jumps: any fish in range, a new one at random each shot.
            bool scrambled = Bosses.Instance.IsScrambled(p.Id);
            int seen = 0;
            foreach (var c in alive)
            {
                if (!Alive(c)) continue;
                float d = Flat2(c.transform.position - muzzle);
                if (scrambled) { if (d < Limit(c) && d >= min && Random.Range(0, ++seen) == 0) target = c; continue; } // one in `seen`: each in range equally likely
                if (d < Limit(c) && d >= min && d < best) { best = d; target = c; }
            }
            // The sniper picks the toughest fish in range instead of the nearest.
            if (p.Type == PieceType.Sniper && !scrambled)
            {
                int hp = -1;
                for (int i = 0; i < alive.Count; i++)
                {
                    var c = alive[i];
                    if (!Alive(c)) continue;
                    if (Flat2(c.transform.position - muzzle) >= Limit(c)) continue;
                    if (c._hp.Value > hp) { hp = c._hp.Value; target = c; }
                }
            }
            if (!target) return;
            _nextFire[p.Id] = Time.time + s.Interval;
            Vector3 hit = target.transform.position;
            if (AutoTest.Running) AutoTest.Count("shots_" + p.Type);

            switch (p.Type)
            {
                case PieceType.Gun:
                {
                    int dmg = Dmg(s.Damage, target, s);
                    Hit(target, dmg);
                    Pierce(alive, muzzle, target, s);
                    _tracer.TryGetValue(p.Id, out var tr);
                    if (Time.time - tr.next > 0.5f) tr.dmg = 0; // left over from when it last stopped firing
                    tr.dmg += dmg;
                    bool show = Time.time >= tr.next;
                    if (show) { Fx.Send(FxKind.Tracer, muzzle, hit, p.Id, tr.dmg, 0, Key(p.Type)); tr = (Time.time + 0.15f, 0); }
                    _tracer[p.Id] = tr;
                    var extraGun = Others(alive, target, muzzle, s, false);
                    if (extraGun.Count > 0)
                    {
                        // A loop, not a lambda over `s`: the lambda's closure was built at the top of Fire, on every call of
                        // every turret every frame, reloading or not.
                        var shots = new List<(Creature c, int dmg)>(extraGun.Count);
                        foreach (var c in extraGun) shots.Add((c, Dmg(s.Damage, c, s)));
                        StartCoroutine(GunBurst(muzzle, shots, show, Key(p.Type)));
                    }
                    break;
                }

                case PieceType.Flame:
                {
                    // Everything in a cone toward the target burns.
                    Vector3 aim = (hit - muzzle).Flat().normalized;
                    var living = Living(alive);
                    foreach (var c in living)
                    {
                        Vector3 to = (c.transform.position - muzzle).Flat();
                        float reach = Enemies.Instance.TryGet(c, out var ft) && EnemyCatalog.IsBoss(ft.Kind) ? s.Range * 1.6f : s.Range;
                        if (to.sqrMagnitude > reach * reach || Vector3.Angle(aim, to) > s.Cone) continue;
                        Hit(c, Dmg(s.Damage, c, s));
                        SetBurn(c, s.Burn);
                    }
                    Done(living);
                    Fx.Send(FxKind.Flame, muzzle, muzzle + aim * s.Range, p.Id, 0, 0, Key(p.Type)); // the visual ends at the real range
                    break;
                }

                case PieceType.Sniper:
                {
                    int dmg = Dmg(s.Damage, target, s);
                    Hit(target, dmg);
                    Pierce(alive, muzzle, target, s);
                    Fx.Send(FxKind.Snipe, muzzle, s.Pierce ? muzzle + (hit - muzzle).Flat().normalized * s.Range + Vector3.up * (hit.y - muzzle.y) : hit, p.Id, dmg, 0, Key(p.Type));
                    // Three targets (Lv5+ barrels) are a burst, not a shotgun: `啪、啪、啪`, each round separately aimed.
                    var extras = Others(alive, target, muzzle, s, true);
                    if (extras.Count > 0)
                    {
                        var shots = new List<(Creature c, int dmg)>(extras.Count);
                        foreach (var extra in extras) shots.Add((extra, Dmg(s.Damage, extra, s)));
                        StartCoroutine(SniperBurst(muzzle, shots, p.Id, Key(p.Type)));
                    }
                    break;
                }

                case PieceType.Cannon:
                    for (int i = 0; i < s.Shots; i++)
                    {
                        Vector3 at = i == 0 ? hit : hit + Random.insideUnitSphere.Flat() * 3f;
                        Splash(alive, at, s.Splash, s.Damage, s);
                        if (s.Burn > 0f) StartCoroutine(GroundFire(at, s, _owner, _from));
                        Fx.Send(FxKind.Cannonball, muzzle, at, i == 0 ? p.Id : 0, Mathf.RoundToInt(s.Damage * DamageMultiplier.Value * SoulTurretMult), 0, Key(p.Type));
                    }
                    break;

                case PieceType.Harpoon:
                {
                    Vector3 dir = (hit - muzzle).Flat().normalized;
                    for (int i = 0; i < s.Shots; i++)
                    {
                        Vector3 d = Quaternion.Euler(0f, (i - (s.Shots - 1) * 0.5f) * 12f, 0f) * dir;
                        Vector3 end = muzzle + d * s.Range;
                        var living = Living(alive);
                        Creature last = null; float far = -1f;
                        foreach (var c in living)
                            if (DistToSegment(c.transform.position, muzzle, end) < 1f)
                            {
                                Hit(c, Dmg(s.Damage, c, s));
                                float along = Vector3.Dot((c.transform.position - muzzle).Flat(), d);
                                if (along > far) { far = along; last = c; }
                            }
                        Done(living);
                        Fx.Send(FxKind.Harpoon, muzzle, end, i == 0 ? p.Id : 0, 0, 0, Key(p.Type));
                        if (s.Chain > 0 && last) ChainHarpoon(alive, last, s, Key(p.Type));
                    }
                    break;
                }

                case PieceType.Mortar:
                    for (int i = 0; i < s.Shots; i++)
                    {
                        Vector3 at = i == 0 ? hit : hit + Random.insideUnitSphere.Flat() * 2.5f;
                        Fx.Send(FxKind.Mortar, muzzle, at, i == 0 ? p.Id : 0, 0, 0, Key(p.Type));
                        StartCoroutine(MortarLands(at, s, _owner, _from));
                    }
                    break;

                case PieceType.Tesla:
                {
                    var hitSet = new HashSet<Creature>();
                    Creature cur = target; Vector3 from = muzzle; // the electrode on top (Catalog.MuzzleHeight)
                    for (int n = 0; n < s.Chain && cur; n++)
                    {
                        hitSet.Add(cur);
                        int zd = Dmg(s.Damage, cur, s);
                        if (Weapons.IsWet(cur)) zd = Mathf.RoundToInt(zd * Weapons.WetMult); // the water cannon's wet fish
                        Hit(cur, zd);
                        if (s.Splash > 0f) ArcBlast(alive, cur, s, n == 0 ? p.Id : 0);
                        if (Perks.TeamLegend(Gear.TeslaCoil)) { Siege.Instance.Slow(cur, 1f, 0.35f); Perks.Proc(Gear.TeslaCoil, cur.transform.position); } // legendary: every zap stuns
                        Vector3 to = cur.transform.position + Vector3.up * 0.4f;
                        Fx.Send(FxKind.Zap, from, to, n == 0 ? p.Id : 0, zd, 0, Key(p.Type));
                        from = to;
                        Creature nextC = null; float nb = 6f * 6f;
                        foreach (var c in alive)
                        {
                            if (!Alive(c) || hitSet.Contains(c)) continue;
                            float d = (c.transform.position - from).sqrMagnitude;
                            if (d < nb) { nb = d; nextC = c; }
                        }
                        cur = nextC;
                    }
                    break;
                }
            }
            // Lv20 overdrive: every few seconds the tower does not just shoot, it detonates the ground around it. This is the
            // incremental-game "it went crazy" moment, and it is deliberately very visible.
            if (p.Level >= Catalog.OverdriveLevel && (!_nova.TryGetValue(p.Id, out float nt) || Time.time >= nt))
            {
                _nova[p.Id] = Time.time + 4.5f;
                Nova(p, s, muzzle, alive);
            }        }

        private void Nova(PieceState p, Catalog.TurretStats s, Vector3 muzzle, List<Creature> alive)
        {
            Vector3 at = p.Pos + Vector3.up * 0.6f;
            float r = Catalog.NovaRadius(s.Range);
            foreach (var c in alive.Where(c => Alive(c) && (c.transform.position - at).Flat().sqrMagnitude <= r * r).ToList())
            {
                Hit(c, Dmg(Mathf.RoundToInt(s.Damage * 1.6f), c, s));
                Fx.Send(FxKind.Blast, c.transform.position + Vector3.up * 0.3f, c.transform.position, p.Id);
            }
            Fx.Send(FxKind.Blast, at, at, p.Id);
            AutoTest.Count("turret_nova");
        }

        private IEnumerator GunBurst(Vector3 muzzle, List<(Creature c, int dmg)> shots, bool show, string key)
        {
            foreach (var (c, dmg) in shots)
            {
                yield return new WaitForSeconds(0.035f); // extra barrels answer one after another, not in one frame
                if (!Alive(c) || c._hp.Value <= 0) continue;
                Hit(c, dmg);
                if (show) Fx.Send(FxKind.Tracer, muzzle, c.transform.position, 0, 0, 0, key);
            }
        }

        private IEnumerator SniperBurst(Vector3 muzzle, List<(Creature c, int dmg)> shots, int pieceId, string key)
        {
            foreach (var (c, dmg) in shots)
            {
                yield return new WaitForSeconds(0.13f);
                if (!Alive(c) || c._hp.Value <= 0) continue;
                Hit(c, dmg);
                Fx.Send(FxKind.Snipe, muzzle, c.transform.position, 0, dmg, 0, key);
            }
        }

        // The other fish a multi-barrel turret shoots at once: the nearest (or, for the sniper, the toughest) in range.
        private List<Creature> Others(List<Creature> alive, Creature first, Vector3 muzzle, Catalog.TurretStats s, bool toughest) =>
            s.Targets <= 1 ? NoOthers : OthersInRange(alive, first, muzzle, s, toughest);
        private static readonly List<Creature> NoOthers = new List<Creature>(); // read only

        private List<Creature> OthersInRange(List<Creature> alive, Creature first, Vector3 muzzle, Catalog.TurretStats s, bool toughest)
        {
            var inRange = alive.Where(c => c != first && Alive(c) && Flat2(c.transform.position - muzzle) < s.Range * s.Range
                                           && Flat2(c.transform.position - muzzle) >= s.MinRange * s.MinRange);
            var sorted = toughest ? inRange.OrderByDescending(c => c._hp.Value) : inRange.OrderBy(c => Flat2(c.transform.position - muzzle));
            var result = new List<Creature>(s.Targets - 1);
            foreach (var c in sorted) { if (result.Count >= s.Targets - 1) break; result.Add(c); }
            return result;
        }

        private static float Flat2(Vector3 v) => v.x * v.x + v.z * v.z;

        private void Splash(List<Creature> alive, Vector3 at, float radius, int dmg, Catalog.TurretStats s)
        {
            float r2 = radius * radius;
            var living = Living(alive);
            foreach (var c in living)
                if ((c.transform.position - at).sqrMagnitude <= r2) Hit(c, Dmg(dmg, c, s));
            Done(living);
        }

        // The fish a shot can hit, copied before the hits as `alive.Where(Alive).ToList()` did (a hit may change `alive`),
        // into a reused list: a new one per shell, flame or harpoon (hundreds of fish late on) was the host's largest
        // steady garbage in a big fight. A stack, so a hit that ends in another shot gets a list of its own.
        private readonly Stack<List<Creature>> _spareLists = new Stack<List<Creature>>();
        private List<Creature> Living(List<Creature> alive)
        {
            var l = _spareLists.Count > 0 ? _spareLists.Pop() : new List<Creature>();
            foreach (var c in alive) if (Alive(c)) l.Add(c);
            return l;
        }
        private void Done(List<Creature> l) { l.Clear(); _spareLists.Push(l); }

        // PieceType names for the effects' key, made once (an enum's ToString is a new string each shot).
        private static readonly Dictionary<PieceType, string> KeyNames = new Dictionary<PieceType, string>();
        private static string Key(PieceType t)
        {
            if (!KeyNames.TryGetValue(t, out var k)) KeyNames[t] = k = t.ToString();
            return k;
        }

        // Incendiary rounds (fortress gear): turret hits set the target burning for 3 s.
        private readonly Dictionary<Creature, (float dps, float until, int owner)> _burning = new Dictionary<Creature, (float, float, int)>();
        private int _burnTicks;

        private void Ignite(Creature c, bool spread = true)
        {
            float dps = Perks.Team(Gear.Incendiary);
            if (dps <= 0f || !Alive(c)) return;
            SetBurn(c, dps); // never weakens a flamethrower's burn
            if (spread && Perks.TeamLegend(Gear.Incendiary)) Spread(c);
        }

        // Legendary: the fire jumps to the nearest other fish. (Apart from Ignite: this lambda's closure was built on every hit.)
        private void Spread(Creature c)
        {
            var next = FortressPlugin.Alive.Where(x => x != c && Alive(x) && !_burning.ContainsKey(x) && (x.transform.position - c.transform.position).sqrMagnitude < 2.5f * 2.5f).FirstOrDefault();
            if (next) { Ignite(next, false); Perks.Proc(Gear.Incendiary, next.transform.position); }
        }

        // Keeps the stronger of two burns (flamethrower vs incendiary rounds).
        private void SetBurn(Creature c, float dps)
        {
            if (dps <= 0f || !Alive(c)) return;
            if (_burning.TryGetValue(c, out var b) && b.dps > dps && b.until > Time.time) return;
            _burning[c] = (dps, Time.time + 3f, _owner);
        }

        // Repair towers: once a second every damaged building in reach gets a share of its own health back (not the tower
        // itself: two look after each other), from the strongest tower reaching it only, like bubbles; the lighthouse a part
        // of that share (Catalog.MendCore). A green spark to a few of them every other second.
        private float _nextMend;
        private int _mendPulse;
        private readonly Dictionary<int, (float share, int by)> _mendBest = new Dictionary<int, (float, int)>();
        private void Mend()
        {
            bool fx = (++_mendPulse & 1) == 0;
            var menders = BuildSystem.Instance.ServerPieces.Where(x => x.Type == PieceType.Mender && !Bosses.Instance.IsSilenced(x.Id)).ToList();
            if (menders.Count == 0) return;
            _mendBest.Clear();
            Vector3 core = Siege.Instance.CorePos;
            float coreShare = 0f; int coreBy = 0;
            foreach (var m in menders)
            {
                var s = Catalog.Turret(m.Type, m.Level, m.Branch);
                float share = s.Heal / s.Interval, r2 = s.Range * s.Range;
                foreach (var p in BuildSystem.Instance.ServerPieces)
                    if (p.Id != m.Id && p.Hp < p.MaxHp && (p.Pos - m.Pos).Flat().sqrMagnitude <= r2 && (!_mendBest.TryGetValue(p.Id, out var b) || share > b.share))
                        _mendBest[p.Id] = (share, m.Id);
                if ((core - m.Pos).Flat().sqrMagnitude <= r2 && share * s.CoreMend > coreShare) { coreShare = share * s.CoreMend; coreBy = m.Id; }
            }
            var healed = new Dictionary<int, List<Vector3>>();
            void Mark(int by, Vector3 at) { if (!healed.TryGetValue(by, out var l)) healed[by] = l = new List<Vector3>(); l.Add(at); }
            foreach (var kv in _mendBest) if (BuildSystem.Instance.ServerMend(kv.Key, kv.Value.share, out var at)) Mark(kv.Value.by, at);
            if (coreShare > 0f && Siege.Instance.ServerHealCore(coreShare) > 0) Mark(coreBy, core + Vector3.up * 2f);
            foreach (var m in menders)
            {
                if (!healed.TryGetValue(m.Id, out var list)) continue;
                AutoTest.Count("mended");
                if (fx) foreach (var to in list.Take(3)) Fx.Send(FxKind.Mend, m.Pos + Vector3.up * Catalog.MuzzleHeight(m.Type, m.Level), to + Vector3.up * 0.6f, m.Id);
            }
        }

        // Bubble shields: the strongest one covering `pos` (0 = none). Used by every piece-damage path on the host.
        public static float ShieldAt(Vector3 pos)
        {
            float best = 0f;
            foreach (var p in BuildSystem.Instance.ServerPieces)
            {
                if (p.Type != PieceType.Bubble) continue;
                var s = Catalog.Turret(p.Type, p.Level, p.Branch);
                if ((p.Pos - pos).sqrMagnitude <= s.Range * s.Range) best = Mathf.Max(best, s.Shield);
            }
            return best;
        }

        // Every turret hit lands here. Legendary gunpowder: a turret kill bursts and hurts the fish around it.
        private bool _bursting;
        private void Hit(Creature c, int dmg)
        {
            c.ServerChangeHp(dmg);
            if (c._hp.Value > 0 || _bursting || !Perks.TeamLegend(Gear.Gunpowder)) return;
            Burst(c, dmg);
        }

        // (Apart from Hit: this lambda captures `c`, and its closure was built on every hit.)
        private void Burst(Creature c, int dmg)
        {
            _bursting = true;
            Vector3 at = c.transform.position;
            foreach (var x in FortressPlugin.Alive.Where(x => x != c && Alive(x) && (x.transform.position - at).sqrMagnitude < 2.5f * 2.5f).ToList())
                x.ServerChangeHp(Mathf.Max(1, Mathf.RoundToInt(dmg * 0.3f)));
            Fx.Send(FxKind.Blast, at, at);
            Perks.Proc(Gear.Gunpowder, at);
            AutoTest.Count("legend_bursts");
            _bursting = false;
        }

        private void BurnTick()
        {
            bool fx = (++_burnTicks & 1) == 0; // flames once a second, damage every half second
            foreach (var kv in _burning.ToList())
            {
                var c = kv.Key;
                if (!Alive(c) || Time.time > kv.Value.until) { _burning.Remove(c); continue; }
                Stats.TurretHit(c, kv.Value.owner);
                Hit(c, Mathf.Max(1, Mathf.RoundToInt(kv.Value.dps * 0.5f * DamageMultiplier.Value * SoulTurretMult)));
                if (fx) Fx.Send(FxKind.Burn, c.transform.position, c.transform.position);
                AutoTest.Count("burn_ticks");
            }
        }

        private IEnumerator MortarLands(Vector3 at, Catalog.TurretStats s, int owner, Vector3 from)
        {
            yield return new WaitForSeconds(Fx.MortarFlight);
            if (!Siege.Instance.ServerActive) yield break;
            _owner = owner; _from = from;
            Splash(FortressPlugin.Alive, at, s.Splash, s.Damage, s);
            if (s.Burn > 0f) yield return GroundFire(at, s, owner, from);
        }

        // Fire branches (mortar, napalm cluster): where a shell lands keeps burning for 4 s, s.Burn damage a second.
        private IEnumerator GroundFire(Vector3 at, Catalog.TurretStats s, int owner, Vector3 from)
        {
            for (float t = 0f; t < 4f && Siege.Instance.ServerActive; t += 0.5f)
            {
                Fx.Send(FxKind.Burn, at, at);
                _owner = owner; _from = from;
                Splash(FortressPlugin.Alive, at, s.Splash * 0.8f, Mathf.RoundToInt(s.Burn * 0.5f), s);
                yield return new WaitForSeconds(0.5f);
            }
        }

        private void Spikes(List<Creature> alive)
        {
            foreach (var p in BuildSystem.Instance.ServerPieces)
            {
                if (p.Type != PieceType.Spikes) continue;
                foreach (var c in alive)
                {
                    if (!Alive(c)) continue;
                    float scale = 1f;
                    if (Enemies.Instance.TryGet(c, out var tag))
                    {
                        if (EnemyCatalog.BodyOf(tag.Kind) == EnemyCatalog.Body.Sky) continue; // spikes are on the ground
                        scale = Enemies.StatsOf(tag).Scale;
                    }
                    // The cell is 3 m; a fish's own width counts too, so a tank's body touches the mat (Owen: "地刺的判定範圍").
                    float reach = 1.5f + 0.45f * (Mathf.Max(1f, scale) - 1f);
                    Vector3 d = c.transform.position - p.Pos;
                    if (Mathf.Abs(d.x) <= reach && Mathf.Abs(d.z) <= reach && d.y > -0.6f && d.y < 2.5f)
                    {
                        Stats.TurretHit(c, BuildSystem.Instance.OwnerOf(p.Id));
                        int dmg = Mathf.RoundToInt(Catalog.SpikeDamage(p.Level) * DamageMultiplier.Value * SoulTurretMult);
                        c.ServerChangeHp(dmg);
                        Siege.Instance.Slow(c, 0.45f, 0.9f);
                        Fx.Send(FxKind.WeaponHit, c.transform.position, c.transform.position, 0, dmg); // number, sparks, the game's hit sound
                        AutoTest.Count("spike_hits");
                    }
                }
            }
        }

        // Railgun, piercing shot: the round goes on through `first` to the end of its range, hitting every fish within a metre
        // of the line.
        private void Pierce(List<Creature> alive, Vector3 muzzle, Creature first, Catalog.TurretStats s)
        {
            if (!s.Pierce) return;
            Vector3 end = muzzle + (first.transform.position - muzzle).Flat().normalized * s.Range;
            var living = Living(alive);
            foreach (var c in living)
                if (c != first && DistToSegment(c.transform.position, muzzle, end) < 1f) Hit(c, Dmg(s.Damage, c, s));
            Done(living);
        }

        // Chain harpoon: from the last fish on the harpoon's line it jumps to the nearest fish not yet hit (6 m), s.Chain times.
        private void ChainHarpoon(List<Creature> alive, Creature from, Catalog.TurretStats s, string key)
        {
            var hit = new HashSet<Creature> { from };
            for (int n = 0; n < s.Chain && from; n++)
            {
                Creature next = null; float best = 6f * 6f;
                foreach (var c in alive)
                {
                    if (!Alive(c) || hit.Contains(c)) continue;
                    float d = (c.transform.position - from.transform.position).sqrMagnitude;
                    if (d < best) { best = d; next = c; }
                }
                if (!next) break;
                Fx.Send(FxKind.Harpoon, from.transform.position + Vector3.up * 0.5f, next.transform.position + Vector3.up * 0.5f, 0, 0, 0, key);
                Hit(next, Dmg(s.Damage, next, s));
                hit.Add(next);
                from = next;
            }
        }

        // Arc blast: a zap bursts round the fish it hits, half its damage to every fish within s.Splash (one boom a volley).
        private void ArcBlast(List<Creature> alive, Creature at, Catalog.TurretStats s, int pieceId)
        {
            Vector3 pos = at.transform.position;
            Splash(alive, pos, s.Splash, Mathf.RoundToInt(s.Damage * 0.5f), s);
            if (pieceId != 0) Fx.Send(FxKind.Blast, pos, pos, 0, Mathf.RoundToInt(s.Splash * 100f));
            AutoTest.Count("arc_blasts");
        }

        private static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = (b - a).Flat(), ap = (p - a).Flat();
            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return (ap - ab * t).magnitude;
        }
    }

    internal static class VecExt
    {
        public static Vector3 Flat(this Vector3 v) => new Vector3(v.x, 0f, v.z);
        public static Vector3 X0Y(this Vector2 v) => new Vector3(v.x, 0f, v.y);
    }
}
