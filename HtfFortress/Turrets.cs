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
                foreach (var p in BuildSystem.Instance.ServerPieces.ToList())
                    if (Catalog.IsTurret(p.Type) && !Bosses.Instance.IsSilenced(p.Id)) Fire(p, alive);
                if (Time.time >= _nextSpikeTick) { _nextSpikeTick = Time.time + 0.5f; Spikes(alive); BurnTick(); }
            }
            finally { Firing = false; }
        }

        private static bool Alive(Creature c) => c && c._hp.Value > 0 && !Siege.Instance.IsHidden(c) && !Bosses.Entering(c);

        private Vector3 _from; // the turret firing now (shield bearers block hits from their front)

        private int _owner = -1;

        private int Dmg(int baseDmg, Creature c, Catalog.TurretStats s)
        {
            Stats.TurretHit(c, _owner);
            float d = baseDmg * DamageMultiplier.Value * (1f + Perks.Team(Gear.Gunpowder));
            if (Perks.TeamLegend(Gear.Scope) && Random.value < 0.1f) { d *= 3f; AutoTest.Count("legend_crits"); Perks.Proc(Gear.Scope, c.transform.position); } // legendary: 10% crits
            if (Perks.TeamLegend(Gear.FrostCore) && Random.value < 0.06f) { Siege.Instance.Slow(c, 1f, 1.5f); AutoTest.Count("legend_freezes"); Perks.Proc(Gear.FrostCore, c.transform.position); } // legendary: freeze
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
            Vector3 muzzle = p.Pos + Vector3.up * 1.1f;

            // Range is measured flat (a cylinder): a turret up on a rise reaches the beach below it, and one on the ground the
            // flyers over it (the playtest's high turret couldn't hit what was under it).
            Creature target = null;
            float best = s.Range * s.Range, min = s.MinRange * s.MinRange;
            foreach (var c in alive)
            {
                if (!Alive(c)) continue;
                float d = Flat2(c.transform.position - muzzle);
                if (d < best && d >= min) { best = d; target = c; }
            }
            // The sniper picks the toughest fish in range instead of the nearest.
            if (p.Type == PieceType.Sniper)
                target = alive.Where(c => Alive(c) && Flat2(c.transform.position - muzzle) < s.Range * s.Range).OrderByDescending(c => c._hp.Value).FirstOrDefault() ?? target;
            if (!target) return;
            _nextFire[p.Id] = Time.time + s.Interval;
            Vector3 hit = target.transform.position;
            AutoTest.Count("shots_" + p.Type);

            switch (p.Type)
            {
                case PieceType.Gun:
                {
                    int dmg = Dmg(s.Damage, target, s);
                    Hit(target, dmg);
                    _tracer.TryGetValue(p.Id, out var tr);
                    if (Time.time - tr.next > 0.5f) tr.dmg = 0; // left over from when it last stopped firing
                    tr.dmg += dmg;
                    bool show = Time.time >= tr.next;
                    if (show) { Fx.Send(FxKind.Tracer, muzzle, hit, p.Id, tr.dmg); tr = (Time.time + 0.15f, 0); }
                    _tracer[p.Id] = tr;
                    foreach (var extra in Others(alive, target, muzzle, s, false)) // Lv5+: the extra barrels pick their own fish
                    {
                        Hit(extra, Dmg(s.Damage, extra, s));
                        if (show) Fx.Send(FxKind.Tracer, muzzle, extra.transform.position, 0, 0);
                    }
                    break;
                }

                case PieceType.Flame:
                {
                    // Everything in a cone toward the target burns.
                    Vector3 aim = (hit - muzzle).Flat().normalized;
                    foreach (var c in alive.Where(Alive).ToList())
                    {
                        Vector3 to = (c.transform.position - muzzle).Flat();
                        if (to.sqrMagnitude > s.Range * s.Range || Vector3.Angle(aim, to) > s.Cone) continue;
                        Hit(c, Dmg(s.Damage, c, s));
                        SetBurn(c, s.Burn);
                    }
                    Fx.Send(FxKind.Flame, muzzle, muzzle + aim * s.Range * 0.9f, p.Id);
                    break;
                }

                case PieceType.Sniper:
                {
                    int dmg = Dmg(s.Damage, target, s);
                    Hit(target, dmg);
                    Fx.Send(FxKind.Snipe, muzzle, hit, p.Id, dmg);
                    foreach (var extra in Others(alive, target, muzzle, s, true))
                    {
                        int ed = Dmg(s.Damage, extra, s);
                        Hit(extra, ed);
                        Fx.Send(FxKind.Snipe, muzzle, extra.transform.position, 0, ed);
                    }
                    break;
                }

                case PieceType.Cannon:
                    for (int i = 0; i < s.Shots; i++)
                    {
                        Vector3 at = i == 0 ? hit : hit + Random.insideUnitSphere.Flat() * 3f;
                        Splash(alive, at, s.Splash, s.Damage, s);
                        Fx.Send(FxKind.Cannonball, muzzle, at, i == 0 ? p.Id : 0, Mathf.RoundToInt(s.Damage * DamageMultiplier.Value));
                    }
                    break;

                case PieceType.Harpoon:
                {
                    Vector3 dir = (hit - muzzle).Flat().normalized;
                    for (int i = 0; i < s.Shots; i++)
                    {
                        Vector3 d = Quaternion.Euler(0f, (i - (s.Shots - 1) * 0.5f) * 12f, 0f) * dir;
                        Vector3 end = muzzle + d * s.Range;
                        foreach (var c in alive.Where(Alive).ToList())
                            if (DistToSegment(c.transform.position, muzzle, end) < 1f)
                            {
                                Hit(c, Dmg(s.Damage, c, s));
                                Siege.Instance.Slow(c, s.Slow, s.SlowTime);
                            }
                        Fx.Send(FxKind.Harpoon, muzzle, end, i == 0 ? p.Id : 0);
                    }
                    break;
                }

                case PieceType.Mortar:
                    for (int i = 0; i < s.Shots; i++)
                    {
                        Vector3 at = i == 0 ? hit : hit + Random.insideUnitSphere.Flat() * 2.5f;
                        Fx.Send(FxKind.Mortar, muzzle, at, i == 0 ? p.Id : 0);
                        StartCoroutine(MortarLands(at, s, _owner, _from));
                    }
                    break;

                case PieceType.Tesla:
                {
                    var hitSet = new HashSet<Creature>();
                    Creature cur = target; Vector3 from = muzzle + Vector3.up * 0.3f;
                    for (int n = 0; n < s.Chain && cur; n++)
                    {
                        hitSet.Add(cur);
                        int zd = Dmg(s.Damage, cur, s);
                        if (Weapons.IsWet(cur)) zd = Mathf.RoundToInt(zd * Weapons.WetMult); // the water cannon's wet fish
                        Hit(cur, zd);
                        if (Perks.TeamLegend(Gear.TeslaCoil)) { Siege.Instance.Slow(cur, 1f, 0.35f); Perks.Proc(Gear.TeslaCoil, cur.transform.position); } // legendary: every zap stuns
                        Vector3 to = cur.transform.position + Vector3.up * 0.4f;
                        Fx.Send(FxKind.Zap, from, to, n == 0 ? p.Id : 0, zd);
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
        }

        // The other fish a multi-barrel turret shoots at once: the nearest (or, for the sniper, the toughest) in range.
        private IEnumerable<Creature> Others(List<Creature> alive, Creature first, Vector3 muzzle, Catalog.TurretStats s, bool toughest)
        {
            if (s.Targets <= 1) return Enumerable.Empty<Creature>();
            var inRange = alive.Where(c => c != first && Alive(c) && Flat2(c.transform.position - muzzle) < s.Range * s.Range
                                           && Flat2(c.transform.position - muzzle) >= s.MinRange * s.MinRange);
            return (toughest ? inRange.OrderByDescending(c => c._hp.Value) : inRange.OrderBy(c => Flat2(c.transform.position - muzzle))).Take(s.Targets - 1).ToList();
        }

        private static float Flat2(Vector3 v) => v.x * v.x + v.z * v.z;

        private void Splash(List<Creature> alive, Vector3 at, float radius, int dmg, Catalog.TurretStats s)
        {
            float r2 = radius * radius;
            foreach (var c in alive.Where(Alive).ToList())
                if ((c.transform.position - at).sqrMagnitude <= r2) Hit(c, Dmg(dmg, c, s));
        }

        // Incendiary rounds (fortress gear): turret hits set the target burning for 3 s.
        private readonly Dictionary<Creature, (float dps, float until, int owner)> _burning = new Dictionary<Creature, (float, float, int)>();
        private int _burnTicks;

        private void Ignite(Creature c, bool spread = true)
        {
            float dps = Perks.Team(Gear.Incendiary);
            if (dps <= 0f || !Alive(c)) return;
            SetBurn(c, dps); // never weakens a flamethrower's burn
            // Legendary: the fire jumps to the nearest other fish.
            if (spread && Perks.TeamLegend(Gear.Incendiary))
            {
                var next = FortressPlugin.Alive.Where(x => x != c && Alive(x) && !_burning.ContainsKey(x) && (x.transform.position - c.transform.position).sqrMagnitude < 2.5f * 2.5f).FirstOrDefault();
                if (next) { Ignite(next, false); Perks.Proc(Gear.Incendiary, next.transform.position); }
            }
        }

        // Keeps the stronger of two burns (flamethrower vs incendiary rounds).
        private void SetBurn(Creature c, float dps)
        {
            if (dps <= 0f || !Alive(c)) return;
            if (_burning.TryGetValue(c, out var b) && b.dps > dps && b.until > Time.time) return;
            _burning[c] = (dps, Time.time + 3f, _owner);
        }

        // Repair towers: once a second every damaged building in reach gets the tower's heal back (not the tower itself: two
        // look after each other), the lighthouse MendCore of it; a green spark to a few of them every other second.
        private float _nextMend;
        private int _mendPulse;
        private void Mend()
        {
            bool fx = (++_mendPulse & 1) == 0;
            foreach (var p in BuildSystem.Instance.ServerPieces.Where(x => x.Type == PieceType.Mender).ToList())
            {
                if (Bosses.Instance.IsSilenced(p.Id)) continue;
                var s = Catalog.Turret(p.Type, p.Level, p.Branch);
                var healed = BuildSystem.Instance.ServerMendArea(p.Pos, s.Range, s.Damage, p.Id);
                Vector3 core = Siege.Instance.CorePos;
                if ((core - p.Pos).Flat().sqrMagnitude <= s.Range * s.Range && Siege.Instance.ServerHealCore(s.Damage * Catalog.MendCore / Mathf.Max(1, Siege.Instance.CoreMax)) > 0)
                    healed.Add(core + Vector3.up * 2f);
                if (healed.Count == 0) continue;
                AutoTest.Count("mended");
                if (fx) foreach (var to in healed.Take(3)) Fx.Send(FxKind.Mend, p.Pos + Vector3.up * 1.4f, to + Vector3.up * 0.6f, p.Id);
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
                Hit(c, Mathf.Max(1, Mathf.RoundToInt(kv.Value.dps * 0.5f * DamageMultiplier.Value)));
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
            if (s.Burn <= 0f) yield break;
            // Fire branch: the impact keeps burning for 4 s.
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
                    Vector3 d = c.transform.position - p.Pos;
                    if (Mathf.Abs(d.x) < 1.6f && Mathf.Abs(d.z) < 1.6f && d.y > -0.5f && d.y < 2f)
                    {
                        Stats.TurretHit(c, BuildSystem.Instance.OwnerOf(p.Id));
                        c.ServerChangeHp(Mathf.RoundToInt(Catalog.SpikeDamage(p.Level) * DamageMultiplier.Value));
                        Siege.Instance.Slow(c, 0.3f, 0.6f);
                        AutoTest.Count("spike_hits");
                    }
                }
            }
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
