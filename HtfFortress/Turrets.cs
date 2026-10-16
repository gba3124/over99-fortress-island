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
        private float _nextSpikeTick;

        private void Update()
        {
            if (!InstanceFinder.IsServerStarted || !Siege.Instance.ServerActive) return;
            var alive = FortressPlugin.Alive;
            if (alive.Count == 0) return;
            foreach (var p in BuildSystem.Instance.ServerPieces.ToList())
                if (Catalog.IsTurret(p.Type)) Fire(p, alive);
            if (Time.time >= _nextSpikeTick) { _nextSpikeTick = Time.time + 0.5f; Spikes(alive); BurnTick(); }
        }

        private static bool Alive(Creature c) => c && c._hp.Value > 0 && !Siege.Instance.IsHidden(c);

        private Vector3 _from; // the turret firing now (shield bearers block hits from their front)

        private int _owner = -1;

        private int Dmg(int baseDmg, Creature c, Catalog.TurretStats s)
        {
            Stats.TurretHit(c, _owner);
            float d = baseDmg * DamageMultiplier.Value * (1f + Perks.Team(Gear.Gunpowder));
            if (Perks.TeamLegend(Gear.Scope) && Random.value < 0.1f) { d *= 3f; AutoTest.Count("legend_crits"); } // legendary: 10% crits
            if (Perks.TeamLegend(Gear.FrostCore) && Random.value < 0.06f) { Siege.Instance.Slow(c, 1f, 1.5f); AutoTest.Count("legend_freezes"); } // legendary: freeze
            if (!Perks.TeamLegend(Gear.ApRounds) && Enemies.Instance.TryGet(c, out var st) && st.Kind == EnemyKind.Shield) // legendary AP ignores shields
            {
                Vector3 toTurret = _from - c.transform.position; toTurret.y = 0f;
                if (toTurret.sqrMagnitude > 0.01f && Vector3.Angle(c.transform.forward, toTurret) < 70f) { d *= 0.2f; AutoTest.Count("shield_blocks"); }
            }
            if (Enemies.Instance.TryGet(c, out var tag) && (tag.Kind == EnemyKind.Tank || EnemyCatalog.IsBoss(tag.Kind))) d *= s.BossMult * (1f + Perks.Team(Gear.ApRounds));
            Siege.Instance.Slow(c, Perks.Team(Gear.FrostCore), 1.5f);
            if (s.WaterMult != 1f && c.transform.position.y < WaterManager.GetWaterHeight(c.transform.position) + 0.3f) d *= s.WaterMult;
            return Mathf.Max(1, Mathf.RoundToInt(d));
        }

        private void Fire(PieceState p, List<Creature> alive)
        {
            if (p.Type == PieceType.Bubble) return; // shields, doesn't shoot
            _nextFire.TryGetValue(p.Id, out float next);
            if (Time.time < next) return;
            var s = Catalog.Turret(p.Type, p.Level, p.Branch);
            s.Range *= 1f + Perks.Team(Gear.Scope);
            if (Time.time < OverclockUntil) s.Interval *= 0.5f; // an Overclock chip was used
            if (p.Type == PieceType.Tesla) s.Chain += Mathf.RoundToInt(Perks.Team(Gear.TeslaCoil));
            _owner = BuildSystem.Instance.OwnerOf(p.Id);
            _from = p.Pos;
            Vector3 muzzle = p.Pos + Vector3.up * 1.1f;

            Creature target = null;
            float best = s.Range * s.Range, min = s.MinRange * s.MinRange;
            foreach (var c in alive)
            {
                if (!Alive(c)) continue;
                float d = (c.transform.position - muzzle).sqrMagnitude;
                if (d < best && d >= min) { best = d; target = c; }
            }
            // The sniper picks the toughest fish in range instead of the nearest.
            if (p.Type == PieceType.Sniper)
                target = alive.Where(c => Alive(c) && (c.transform.position - muzzle).sqrMagnitude < s.Range * s.Range).OrderByDescending(c => c._hp.Value).FirstOrDefault() ?? target;
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
                    Ignite(target);
                    Fx.Send(FxKind.Tracer, muzzle, hit, p.Id, dmg);
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
                    break;
                }

                case PieceType.Cannon:
                    for (int i = 0; i < s.Shots; i++)
                    {
                        Vector3 at = i == 0 ? hit : hit + Random.insideUnitSphere.Flat() * 3f;
                        Splash(alive, at, s.Splash, s.Damage, s, ignite: true);
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
                        StartCoroutine(MortarLands(at, s));
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
                        Hit(cur, zd);
                        if (Perks.TeamLegend(Gear.TeslaCoil)) Siege.Instance.Slow(cur, 1f, 0.35f); // legendary: every zap stuns
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

        private void Splash(List<Creature> alive, Vector3 at, float radius, int dmg, Catalog.TurretStats s, bool ignite = false)
        {
            float r2 = radius * radius;
            foreach (var c in alive.Where(Alive).ToList())
                if ((c.transform.position - at).sqrMagnitude <= r2) { Hit(c, Dmg(dmg, c, s)); if (ignite) Ignite(c); }
        }

        // Incendiary rounds (fortress gear): gun and cannon hits set the target burning for 3 s.
        private readonly Dictionary<Creature, (float dps, float until, int owner)> _burning = new Dictionary<Creature, (float, float, int)>();
        private int _burnTicks;

        private void Ignite(Creature c, bool spread = true)
        {
            float dps = Perks.Team(Gear.Incendiary);
            if (dps <= 0f || !Alive(c)) return;
            _burning[c] = (dps, Time.time + 3f, _owner);
            // Legendary: the fire jumps to the nearest other fish.
            if (spread && Perks.TeamLegend(Gear.Incendiary))
            {
                var next = FortressPlugin.Alive.Where(x => x != c && Alive(x) && !_burning.ContainsKey(x) && (x.transform.position - c.transform.position).sqrMagnitude < 2.5f * 2.5f).FirstOrDefault();
                if (next) Ignite(next, false);
            }
        }

        // Flamethrower burns: keeps the stronger of two burns.
        private void SetBurn(Creature c, float dps)
        {
            if (dps <= 0f || !Alive(c)) return;
            if (_burning.TryGetValue(c, out var b) && b.dps > dps && b.until > Time.time) return;
            _burning[c] = (dps, Time.time + 3f, _owner);
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

        private IEnumerator MortarLands(Vector3 at, Catalog.TurretStats s)
        {
            yield return new WaitForSeconds(Fx.MortarFlight);
            if (!Siege.Instance.ServerActive) yield break;
            Splash(FortressPlugin.Alive, at, s.Splash, s.Damage, s);
            if (s.Burn <= 0f) yield break;
            // Fire branch: the impact keeps burning for 4 s.
            for (float t = 0f; t < 4f && Siege.Instance.ServerActive; t += 0.5f)
            {
                Fx.Send(FxKind.Burn, at, at);
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
    }
}
