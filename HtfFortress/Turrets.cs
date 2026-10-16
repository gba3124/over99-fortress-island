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
            if (Enemies.Instance.TryGet(c, out var st) && st.Kind == EnemyKind.Shield)
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
            _nextFire.TryGetValue(p.Id, out float next);
            if (Time.time < next) return;
            var s = Catalog.Turret(p.Type, p.Level, p.Branch);
            s.Range *= 1f + Perks.Team(Gear.Scope);
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
            if (!target) return;
            _nextFire[p.Id] = Time.time + s.Interval;
            Vector3 hit = target.transform.position;
            AutoTest.Count("shots_" + p.Type);

            switch (p.Type)
            {
                case PieceType.Gun:
                {
                    int dmg = Dmg(s.Damage, target, s);
                    target.ServerChangeHp(dmg);
                    Ignite(target);
                    Fx.Send(FxKind.Tracer, muzzle, hit, p.Id, dmg);
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
                                c.ServerChangeHp(Dmg(s.Damage, c, s));
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
                        cur.ServerChangeHp(zd);
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
                if ((c.transform.position - at).sqrMagnitude <= r2) { c.ServerChangeHp(Dmg(dmg, c, s)); if (ignite) Ignite(c); }
        }

        // Incendiary rounds (fortress gear): gun and cannon hits set the target burning for 3 s.
        private readonly Dictionary<Creature, (float dps, float until, int owner)> _burning = new Dictionary<Creature, (float, float, int)>();
        private int _burnTicks;

        private void Ignite(Creature c)
        {
            float dps = Perks.Team(Gear.Incendiary);
            if (dps > 0f && Alive(c)) _burning[c] = (dps, Time.time + 3f, _owner);
        }

        private void BurnTick()
        {
            bool fx = (++_burnTicks & 1) == 0; // flames once a second, damage every half second
            foreach (var kv in _burning.ToList())
            {
                var c = kv.Key;
                if (!Alive(c) || Time.time > kv.Value.until) { _burning.Remove(c); continue; }
                Stats.TurretHit(c, kv.Value.owner);
                c.ServerChangeHp(Mathf.Max(1, Mathf.RoundToInt(kv.Value.dps * 0.5f * DamageMultiplier.Value)));
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
