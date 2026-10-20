using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HtfFortress
{
    // The ten bosses' moves (DESIGN.md §24 has the whole cast). Each brain's Act is one turn of its loop: get into place,
    // pick a move, do it, breathe. Damage numbers are per piece / player / the lighthouse at wave 1 (Scaled grows them with
    // the wave); warnings are seconds on the ground before the hit lands. Breakable moves can be stopped (Bosses).
    public static class BossBrains
    {
        public static BossBrain Make(EnemyKind k)
        {
            switch (k)
            {
                case EnemyKind.Warden: return new Warden();
                case EnemyKind.Urchin: return new Urchin();
                case EnemyKind.Angler: return new Angler();
                case EnemyKind.Steed: return new Steed();
                case EnemyKind.Stonefish: return new Stonefish();
                case EnemyKind.Skyfish: return new Skyfish();
                case EnemyKind.Kraken: return new Kraken();
                case EnemyKind.Voxel: return new Voxel();
                case EnemyKind.Koi: return new Koi();
                default: return new Eye();
            }
        }

        // Spots for a volley: the buildings near `from` first (turrets most), then players, then anywhere round it.
        internal static List<Vector3> Spots(Vector3 from, int n, float range, float spread = 16f)
        {
            var spots = BuildSystem.Instance.ServerPieces.Where(p => (p.Pos - from).Flat().magnitude < range)
                .OrderByDescending(p => (Catalog.IsTurret(p.Type) ? 2f : 0f) + Random.value * 2f).Take(n * 2 / 3).Select(p => p.Pos).ToList();
            foreach (var p in PlayerManager.AlivePlayers.Where(p => p && p.Transform && !AutoTest.IgnorePlayers && (p.Transform.position - from).Flat().magnitude < range).Take(2))
                spots.Add(p.Transform.position);
            for (int i = 0; spots.Count < n && i < n * 4; i++)
            {
                Vector3 s = from + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(4f, spread);
                if ((s - FortressIsland.Instance.Center).Flat().magnitude < 28f) spots.Add(s);
            }
            for (int i = 0; i < spots.Count; i++) { var s = spots[i]; s.y = BossFx.Ground(s); spots[i] = s; }
            return spots.Take(n).ToList();
        }
    }

    // ---------- 1. wave 10: the Iron-Claw Warden (a lobster) ----------
    // Walks in on the fort. Guillotine: claws up over the toughest thing in front of it, then down (breakable). Shackles:
    // chains thrown onto turrets hold them silent. Shell: turrets do a quarter while it pushes on (players' weapons as ever).
    // Second form: the guillotine falls twice.
    internal class Warden : BossBrain
    {
        protected override IEnumerator Entrance(float seconds)
        {
            Send("warden.rise", Home, Home, 0, Mathf.RoundToInt(Reach * 100f));
            Vector3 from = Pos;
            Quaternion r = T.rotation;
            for (float t = 0f; t < 1.1f && Alive; t += Time.deltaTime)
            {
                float k = t / 1.1f;
                Put(Vector3.Lerp(from, Home, 1f - (1f - k) * (1f - k)), r * Quaternion.Euler(-30f * (1f - k), 0f, 0f));
                yield return null;
            }
            Put(Home, r);
        }

        protected override IEnumerator Act()
        {
            yield return WalkTo(Heart(), 2.4f, 6.5f, Phase2 ? 2.5f : 4f);
            switch (Pick(4f, 3f, Phase2 ? 1.5f : 2.2f))
            {
                case 0: yield return Guillotine(Phase2 ? 2 : 1); break;
                case 1: yield return Shackles(Phase2 ? 3 : 2); break;
                default: yield return Shell(Phase2 ? 5f : 4f); break;
            }
            yield return Breath;
        }

        private IEnumerator Guillotine(int times)
        {
            for (int i = 0; i < times && Alive; i++)
            {
                Vector3 at = Target();
                yield return Turn(at, 0.35f);
                Quaternion r = Facing(at - Pos);
                Shout("wardenGuillotine");
                yield return Warn("circle", at, at, 2.6f, 1.3f, breakable: true, during: k => Put(Pos, r * Quaternion.Euler(-22f * Mathf.Sin(k * Mathf.PI * 0.5f), 0f, 0f)));
                Put(Pos, r * Quaternion.Euler(6f, 0f, 0f));
                Hit(Circle(at, 2.6f), 170f, 30f, 45f, at, 9f);
                Send("warden.snap", at, Pos);
                AutoTest.Count("warden_guillotine");
                yield return 0.45f;
                Put(Pos, r);
            }
        }

        private Vector3 Target()
        {
            var p = Best(x => (x.Pos - Pos).Flat().magnitude < 11f, x => x.MaxHp + (Catalog.IsTurret(x.Type) ? 200f : 0f) - (x.Pos - Pos).Flat().magnitude * 10f);
            if ((Core - Pos).Flat().magnitude < 11f && (!p.HasValue || Random.value < 0.4f)) return Core;
            if (p.HasValue) return p.Value.Pos;
            Vector3 f = Pos + Forward * (Reach + 3f);
            f.y = GroundY(f);
            return f;
        }

        private IEnumerator Shackles(int n)
        {
            var turrets = TurretsNear(18f).OrderBy(p => (p.Pos - Pos).sqrMagnitude).Take(n).ToList();
            if (turrets.Count == 0) { yield return Guillotine(1); yield break; }
            foreach (var t in turrets) WarnOnly("lane", Pos, t.Pos, 0.9f, 0.9f);
            Send("warden.swing", Pos, Pos, 900);
            yield return 0.9f;
            float hold = 6f + Tier * 0.3f;
            foreach (var t in turrets)
            {
                Bosses.Instance.Silence(t.Id, hold);
                BuildSystem.Instance.ServerDamagePiece(t.Id, Scaled(35f));
                Send("warden.chain", Pos, t.Pos, Mathf.RoundToInt(hold * 1000f), t.Id);
            }
            Shout("wardenChains");
            AutoTest.Count("warden_shackles", turrets.Count);
            yield return 0.5f;
        }

        private IEnumerator Shell(float seconds)
        {
            TurretMult = 0.25f;
            Send("warden.shell", Pos, Pos, Mathf.RoundToInt(seconds * 1000f));
            Shout("wardenShell");
            AutoTest.Count("warden_shell");
            yield return WalkTo(Core, 1.6f, 4f, seconds);
            TurretMult = 1f;
        }
    }

    // ---------- 2. wave 20: the Thousand-Needle Urchin (a sea urchin) ----------
    // Rolls. Crush: a lane through the fort, it revs up spinning, then rolls down it (breakable). Volley: spikes arc onto
    // the buildings and stay stuck in the ground (caltrops). Bloom: its spikes burst out round it, pulse after pulse.
    // Second form: the crush bounces back, more spikes, more pulses.
    internal class Urchin : BossBrain
    {
        private Quaternion _spin = Quaternion.identity;
        private float Radius => Mathf.Max(1f, Height * 0.5f);

        // Rolling: turned about the axis across the way it goes, by the distance over its radius.
        private void RollTo(Vector3 p)
        {
            Vector3 d = (p - Pos).Flat();
            if (d.sqrMagnitude > 1e-6f) _spin = Quaternion.AngleAxis(d.magnitude / Radius * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, d.normalized)) * _spin;
            Put(p, _spin);
        }
        private void Spin(Vector3 dir, float degPerSec) { _spin = Quaternion.AngleAxis(degPerSec * Time.deltaTime, Vector3.Cross(Vector3.up, dir)) * _spin; Put(Pos, _spin); }

        protected override IEnumerator Entrance(float seconds)
        {
            _spin = T.rotation;
            Vector3 from = Pos;
            for (float t = 0f; t < 0.75f && Alive; t += Time.deltaTime) { float k = t / 0.75f; Spin(Vector3.forward, 500f); Put(Vector3.Lerp(from, Home, k * k), _spin); yield return null; }
            Put(Home, _spin);
            Send("urchin.land", Home, Home, 0, Mathf.RoundToInt(Reach * 130f));
            for (float t = 0f; t < 0.5f && Alive; t += Time.deltaTime) { float k = t / 0.5f; Put(Home + Vector3.up * 10f * k * (1f - k), _spin); yield return null; }
            Put(Home, _spin);
            Send("urchin.land", Home, Home, 0, Mathf.RoundToInt(Reach * 80f));
        }

        protected override IEnumerator Act()
        {
            Vector3 goal = Heart();
            for (float t = 0f; t < (Phase2 ? 2f : 3f) && Alive && (goal - Pos).Flat().magnitude > 9f; t += Time.deltaTime)
            {
                Vector3 dir = (goal - Pos).Flat().normalized;
                var wall = WallAhead(dir);
                if (wall != null) { BuildSystem.Instance.ServerDamagePiece(wall.Value.Id, Scaled(40f)); Send("stomp", wall.Value.Pos, wall.Value.Pos); yield return 0.5f; continue; }
                RollTo(Stand(Pos + dir * 3.5f * SlowFactor * Time.deltaTime));
                yield return null;
            }
            switch (Pick(4f, 3.5f, 2.5f))
            {
                case 0: yield return Crush(Phase2 ? 2 : 1); break;
                case 1: yield return Volley(Phase2 ? 20 : 11 + Tier / 2); break;
                default: yield return Bloom(Phase2 ? 4 : 3); break;
            }
            yield return Breath;
        }

        private IEnumerator Crush(int lanes)
        {
            Vector3 dir = (Heart() - Pos).Flat().normalized;
            for (int i = 0; i < lanes && Alive; i++)
            {
                Vector3 a = Pos, b = Across(a, dir, 32f);
                dir = (b - a).Flat().normalized;
                Shout("urchinCrush");
                Vector3 d0 = dir;
                yield return Warn("lane", new Vector3(a.x, GroundY(a), a.z), b, Radius * 2f + 1.2f, 1.4f, breakable: true, during: k => Spin(d0, 200f + 900f * k));
                var hit = new HashSet<int>();
                var hurt = new HashSet<Player>();
                float len = (b - a).Flat().magnitude;
                Send("urchin.roll", a, b, Mathf.RoundToInt(len / 16f * 1000f));
                for (float d = 0f; d < len && Alive; d += 16f * SlowFactor * Time.deltaTime)
                {
                    Vector3 p = Stand(a + dir * d);
                    RollTo(p);
                    HitOnce(hit, hurt, Circle(p, Radius + 0.8f), 95f, 28f, 45f, p, 12f);
                    yield return null;
                }
                AutoTest.Count("urchin_crush");
                dir = Quaternion.Euler(0f, Random.value < 0.5f ? 50f : -50f, 0f) * -dir;
                yield return 0.3f;
            }
        }

        private IEnumerator Volley(int n)
        {
            var spots = BossBrains.Spots(Pos, n, 20f);
            Send("urchin.bristle", Pos, Pos, 1100);
            foreach (var s in spots) WarnOnly("circle", s, s, 1.3f, 1.1f);
            yield return 1.1f;
            Vector3 top = Pos + Vector3.up * Radius;
            foreach (var s in spots) Send("urchin.spike", top, s);
            yield return 0.45f;
            foreach (var s in spots)
            {
                Hit(Circle(s, 1.3f), 45f, 16f, 20f, s, 5f);
                Send("urchin.caltrop", s, s, 9000);
                Hazard(s, 1.1f, 9f, Scaled(4f), 0);
            }
            AutoTest.Count("urchin_volley");
        }

        private IEnumerator Bloom(int pulses)
        {
            Shout("urchinBloom");
            for (int i = 0; i < pulses && Alive; i++)
            {
                float r = Radius + 4f + i * 0.9f;
                yield return Warn("ring", Pos, Pos, r, i == 0 ? 1.1f : 0.65f, breakable: i == 0, extra: Mathf.RoundToInt(Radius * 100f));
                var inside = Circle(Pos, r);
                Hit(p => inside(p) && (p - Pos).Flat().magnitude >= Radius * 0.8f, 50f, 22f, 25f, Pos, 10f);
                Send("urchin.bloom", Pos, Pos, 0, Mathf.RoundToInt(r * 100f));
                AutoTest.Count("urchin_bloom");
            }
        }
    }

    // ---------- 3. wave 30: the Lantern of the Abyss (an anglerfish) ----------
    // Hangs in the air off the shore. Veil: night falls; turrets see half as far, except in the lighthouse's light. Lure:
    // its lantern swells and flashes; whoever is looking at it then is drawn in and bitten (look away!). Devour: a lunge
    // down a lane, jaws open: buildings bitten, fish swallowed (it heals on them). Second form: the night stays.
    internal class Angler : BossBrain
    {
        private const float Hover = 3.5f;
        private float _veilUntil;
        private Vector3 Lantern => Pos + Forward * Reach * 0.85f + Vector3.up * Height * 0.55f;

        protected override IEnumerator Entrance(float seconds)
        {
            Send("angler.lure", Home, Home, 2200);
            Vector3 from = Pos;
            for (float t = 0f; t < 1.8f && Alive; t += Time.deltaTime) { Put(Vector3.Lerp(from, Home, Mathf.SmoothStep(0f, 1f, t / 1.8f))); yield return null; }
            Put(Home);
        }

        public override void Tick()
        {
            base.Tick();
            if (_veilUntil > 0f && Time.time > _veilUntil && !Phase2) { _veilUntil = 0f; Lights(true); }
        }
        protected override void OnStop() { if (Bosses.Dark) Lights(true); }
        private void Lights(bool on) { Bosses.Dark = !on; Send("dark", Pos, Pos, on ? 0 : 1); }

        protected override IEnumerator Act()
        {
            float a = Mathf.Atan2(Pos.z - Center.z, Pos.x - Center.x) + Random.Range(-0.9f, 0.9f);
            Vector3 to = Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(20f, 25f);
            to.y = GroundY(to) + Foot + Hover;
            yield return Glide(to, 3.5f);
            yield return Turn(Core, 0.5f);
            switch (Pick(Bosses.Dark ? 0f : 3f, 4f, 3.5f))
            {
                case 0: yield return Veil(); break;
                case 1: yield return Lure(); break;
                default: yield return Devour(Phase2 ? 2 : 1); break;
            }
            yield return Breath;
        }

        private IEnumerator Veil()
        {
            Send("angler.lure", Pos, Pos, 1200);
            yield return 1.2f;
            Lights(false);
            _veilUntil = Time.time + 15f;
            Shout("anglerVeil");
            AutoTest.Count("angler_veil");
        }

        private IEnumerator Lure()
        {
            Shout("anglerLure", true);
            Send("angler.lure", Pos, Pos, 1700);
            yield return Warn("circle", new Vector3(Pos.x, GroundY(Pos), Pos.z), Pos, Reach + 1f, 1.7f, breakable: true);
            Vector3 lantern = Lantern;
            Send("angler.flash", lantern, Pos);
            int n = 0;
            foreach (var p in Targets.ToList())
            {
                Vector3 eye = p.CamObject ? p.CamObject.position : p.Transform.position + Vector3.up * 1.6f;
                Vector3 look = p.CamObject ? p.CamObject.forward : p.Transform.forward;
                Vector3 to = lantern - eye;
                if (to.magnitude > 45f || Vector3.Angle(look, to) > 50f || Physics.Linecast(eye, lantern, GameInfo.LevelLayer)) continue;
                Siege.HurtPlayer(p, Mathf.Min(70, Scaled(18f)), (Pos - p.Transform.position).Flat().normalized * 16f + Vector3.up * 5f);
                n++;
            }
            AutoTest.Count("angler_lure");
            AutoTest.Count("angler_charmed", n);
            yield return 0.6f;
        }

        private IEnumerator Devour(int times)
        {
            for (int i = 0; i < times && Alive; i++)
            {
                Vector3 target = Heart(), dir = (target - Pos).Flat().normalized;
                Vector3 a = new Vector3(Pos.x, GroundY(Pos), Pos.z), b = a + dir * Mathf.Min(26f, (target - Pos).Flat().magnitude + 6f);
                b.y = GroundY(b);
                yield return Turn(target, 0.4f);
                yield return Warn("lane", a, b, 5f, 1.3f, breakable: true);
                var hit = new HashSet<int>();
                var hurt = new HashSet<Player>();
                Vector3 from = Pos, to = new Vector3(b.x, GroundY(b) + Foot + 0.8f, b.z);
                Send("angler.lunge", from, to, 700);
                for (float t = 0f; t < 0.7f && Alive; t += Time.deltaTime)
                {
                    Vector3 p = Vector3.Lerp(from, to, t / 0.7f);
                    Put(p, Facing(dir));
                    HitOnce(hit, hurt, Circle(p, 3f), 110f, 32f, 50f, p, 10f);
                    foreach (var f in FortressPlugin.Alive.Where(c => c && c != Body && c._hp.Value > 0 && !EnemyCatalog.IsPuppet(KindOf(c)) && (c.transform.position - p).Flat().magnitude < 3.2f).ToList())
                    {
                        Send("angler.gulp", f.transform.position, p);
                        FortressPlugin.Alive.Remove(f);
                        f.DestroyItem();
                        Body.ServerChangeHp(-Mathf.CeilToInt(MaxHp * 0.015f));
                        AutoTest.Count("angler_ate");
                    }
                    yield return null;
                }
                AutoTest.Count("angler_devour");
                yield return Glide(new Vector3(Pos.x, GroundY(Pos) + Foot + Hover, Pos.z), 6f);
            }
        }

        protected override IEnumerator SecondForm()
        {
            Shout("anglerPhase2");
            Send("phase2", Pos, Pos);
            Send("angler.lure", Pos, Pos, 1500);
            yield return 1.5f;
            Lights(false);
            _veilUntil = 0f;
        }
    }

    // ---------- 4. wave 40: the Silver Steed (the game's "seahorse", which is a horse) ----------
    // Leaps about the fort like a chess knight. Charge: a lane right through the fort (breakable). Stomp: it rears and
    // brings its hooves down (breakable). Phantom cavalry: ghost riders gallop parallel lanes across the whole island.
    // Second form, the golden spin: it charges there and back; five phantoms.
    internal class Steed : BossBrain
    {
        protected override IEnumerator Entrance(float seconds)
        {
            Vector3 from = Pos, dir = (Home - from).Flat().normalized;
            for (float t = 0f; t < 1.3f && Alive; t += Time.deltaTime)
            {
                float k = t / 1.3f;
                Vector3 p = Vector3.Lerp(from, Home, k);
                p.y = GroundY(p) + Foot; // (it gallops: BossSteed)
                Put(p, Facing(dir));
                if (Random.value < 0.25f) Send("steed.splash", p, p);
                yield return null;
            }
            Put(Home, Facing(dir));
            Send("steed.rear", Pos, Pos);
            yield return Rear(1.1f, 38f);
        }

        // On its hind legs (turned about its back hooves).
        private IEnumerator Rear(float seconds, float deg)
        {
            Vector3 p0 = Pos;
            Quaternion r = T.rotation;
            for (float t = 0f; t < seconds && Alive; t += Time.deltaTime) { Pose(p0, r, deg * Mathf.Sin(t / seconds * Mathf.PI)); yield return null; }
            Put(p0, r);
        }
        private void Pose(Vector3 p0, Quaternion r, float deg)
        {
            Vector3 fwd = (r * Quaternion.Inverse(BossLooks.Turn(Kind)) * Vector3.forward).Flat().normalized;
            Vector3 hooves = p0 - fwd * Reach * 0.55f - Vector3.up * Foot, right = Vector3.Cross(Vector3.up, fwd);
            var q = Quaternion.AngleAxis(-deg, right);
            Put(hooves + q * (p0 - hooves), q * r);
        }

        protected override IEnumerator Act()
        {
            Vector3 to = SpotNear(Heart(), 9f, 15f);
            yield return Leap(Stand(to), 5f, 0.9f);
            Send("steed.land", Pos, Pos);
            Hit(Circle(Pos, Reach + 1f), 30f, 12f, 0f, Pos, 6f);
            switch (Pick(4f, 3f, 3f))
            {
                case 0: yield return Charge(Phase2 ? 2 : 1); break;
                case 1: yield return Stomp(); break;
                default: yield return Cavalry(Phase2 ? 5 : 3); break;
            }
            yield return Breath;
        }

        private IEnumerator Charge(int times)
        {
            Vector3 dir = (Heart() - Pos).Flat().normalized;
            for (int i = 0; i < times && Alive; i++)
            {
                Vector3 a = Pos, b = Across(a, dir, 34f);
                dir = (b - a).Flat().normalized;
                yield return Turn(b, 0.3f);
                Vector3 p0 = Pos;
                Quaternion r = T.rotation;
                Shout("steedCharge");
                yield return Warn("lane", new Vector3(a.x, GroundY(a), a.z), b, Mathf.Max(3.4f, 2f * (Reach * 0.6f + 1f)), i == 0 ? 1.2f : 0.8f, breakable: true, // as wide as what it hits
                                  during: k => Pose(p0, r, 24f * Mathf.Sin(Mathf.Min(1f, k * 1.4f) * Mathf.PI)));
                Put(p0, r);
                float len = (b - a).Flat().magnitude;
                Send("steed.charge", a, b, Mathf.RoundToInt(len / 24f * 1000f));
                var hit = new HashSet<int>();
                var hurt = new HashSet<Player>();
                for (float d = 0f; d < len && Alive; d += 24f * SlowFactor * Time.deltaTime)
                {
                    Vector3 p = Stand(a + dir * d);
                    Put(p, Facing(dir)); // (it gallops: BossSteed)
                    HitOnce(hit, hurt, Circle(p, Reach * 0.6f + 1f), 100f, 34f, 50f, p, 14f);
                    yield return null;
                }
                AutoTest.Count("steed_charge");
                dir = -dir;
            }
        }

        private IEnumerator Stomp()
        {
            float r = 7.5f + Tier * 0.15f;
            Vector3 p0 = Pos;
            Quaternion rot = T.rotation;
            Send("steed.rear", Pos, Pos);
            yield return Warn("circle", new Vector3(p0.x, GroundY(p0), p0.z), p0, r, 1.4f, breakable: true, during: k => Pose(p0, rot, 42f * Mathf.Sin(Mathf.Min(1f, k * 1.1f) * Mathf.PI * 0.5f)));
            Put(p0, rot);
            Hit(Circle(p0, r), 80f, 30f, 40f, p0, 12f);
            Send("steed.stomp", p0, p0, 0, Mathf.RoundToInt(r * 100f));
            AutoTest.Count("steed_stomp");
            yield return 0.5f;
        }

        private IEnumerator Cavalry(int n)
        {
            Vector3 along = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward, side = Vector3.Cross(Vector3.up, along);
            var lanes = new List<(Vector3 a, Vector3 b)>();
            for (int i = 0; i < n; i++)
            {
                Vector3 mid = Center + side * ((i - (n - 1) * 0.5f) * 6.5f + Random.Range(-1f, 1f));
                Vector3 a = mid - along * 30f, b = mid + along * 30f;
                a.y = GroundY(a); b.y = GroundY(b);
                lanes.Add((a, b));
            }
            Shout("steedCavalry", true);
            Send("steed.rear", Pos, Pos);
            foreach (var l in lanes) WarnOnly("lane", l.a, l.b, 3.2f, 1.7f);
            yield return Rear(1.7f, 32f);
            foreach (var l in lanes) Send("steed.phantom", l.a, l.b, 1100);
            var hit = new HashSet<int>();
            var hurt = new HashSet<Player>();
            for (float t = 0f; t < 1.1f; t += Time.deltaTime)
            {
                float k = t / 1.1f;
                foreach (var l in lanes) { Vector3 p = Vector3.Lerp(l.a, l.b, k); HitOnce(hit, hurt, Circle(p, 2f), 70f, 26f, 35f, p, 10f); }
                yield return null;
            }
            AutoTest.Count("steed_cavalry");
        }

        protected override IEnumerator SecondForm()
        {
            Shout("steedPhase2");
            Send("phase2", Pos, Pos);
            Send("steed.golden", Pos, Pos);
            yield return Rear(1.5f, 45f);
        }
    }

    // ---------- 5. wave 50: the Venom Rock Emperor (a stonefish) ----------
    // Burrows: under the ground (turrets can't see it) to a new spot, and bursts up. Spires: rock spires burst up along
    // lines out from it (breakable). Venom: globs of poison on the buildings leave pools that eat walls and hurt players.
    // Petrify: its eyes glow; turrets it looks at turn to stone for a while (breakable). Second form: more lines, bigger pools.
    internal class Stonefish : BossBrain
    {
        protected override IEnumerator Entrance(float seconds)
        {
            Send("stone.rise", Home, Home, 0, Mathf.RoundToInt(Reach * 100f));
            yield return Surface(Home, 0.7f);
        }

        private IEnumerator Surface(Vector3 to, float seconds)
        {
            Vector3 from = Pos, over = to + Vector3.up * 1.5f;
            for (float t = 0f; t < seconds && Alive; t += Time.deltaTime) { Put(Vector3.Lerp(from, over, t / seconds)); yield return null; }
            for (float t = 0f; t < 0.3f && Alive; t += Time.deltaTime) { Put(Vector3.Lerp(over, to, t / 0.3f)); yield return null; }
            Put(to);
        }

        protected override IEnumerator Act()
        {
            if (Random.value < 0.75f) yield return Burrow(SpotNear(Heart(), 7f, 14f));
            switch (Pick(4f, 3.5f, 2.5f))
            {
                case 0: yield return Spires(Phase2 ? 5 : 3); break;
                case 1: yield return Venom(Phase2 ? 8 : 5); break;
                default: yield return Gaze(); break;
            }
            yield return Breath;
        }

        private IEnumerator Burrow(Vector3 to)
        {
            to = Stand(to);
            Vector3 from = Pos, below = Pos + Vector3.down * (Height + 1.5f), under = to + Vector3.down * (Height + 1.5f);
            Send("stone.dig", new Vector3(from.x, GroundY(from), from.z), from);
            for (float t = 0f; t < 0.6f && Alive; t += Time.deltaTime) { Put(Vector3.Lerp(from, below, t / 0.6f)); yield return null; }
            Siege.Instance.Hide(Body, true);
            float time = Mathf.Max(0.6f, (to - from).Flat().magnitude / 9f);
            Send("stone.tunnel", from, to, Mathf.RoundToInt(time * 1000f));
            for (float t = 0f; t < time && Alive; t += Time.deltaTime) { Put(Vector3.Lerp(below, under, t / time)); yield return null; }
            yield return Warn("circle", new Vector3(to.x, GroundY(to), to.z), to, Reach + 1.5f, 0.8f);
            Siege.Instance.Hide(Body, false);
            Send("stone.rise", to, to, 0, Mathf.RoundToInt(Reach * 100f));
            yield return Surface(to, 0.35f);
            Hit(Circle(to, Reach + 1.5f), 70f, 28f, 30f, to, 10f);
            AutoTest.Count("stone_burrow");
        }

        protected override void Interrupted() { base.Interrupted(); if (Siege.Instance.IsHidden(Body)) { Siege.Instance.Hide(Body, false); Put(Stand(Pos)); } }

        private IEnumerator Spires(int lines)
        {
            Vector3 main = (Heart() - Pos).Flat().normalized, at = new Vector3(Pos.x, GroundY(Pos), Pos.z);
            var ends = new List<Vector3>();
            for (int i = 0; i < lines; i++)
            {
                Vector3 e = at + Quaternion.Euler(0f, (i - (lines - 1) * 0.5f) * (lines > 3 ? 34f : 30f), 0f) * main * 18f;
                e.y = GroundY(e);
                ends.Add(e);
                WarnOnly("lane", at, e, 2.4f, 1.3f);
            }
            Shout("stoneSpires");
            Send("stone.rumble", at, at, 1300);
            yield return Warn("circle", at, at, Reach + 0.5f, 1.3f, breakable: true);
            foreach (var e in ends)
            {
                Send("stone.spires", at, e);
                Hit(Lane(at, e, 2.4f), 85f, 30f, 40f, at, 10f);
            }
            AutoTest.Count("stone_spires");
            yield return 0.6f;
        }

        private IEnumerator Venom(int n)
        {
            var spots = BossBrains.Spots(Pos, n, 22f);
            float r = Phase2 ? 3.2f : 2.4f;
            Send("stone.spines", Pos, Pos, 1000);
            foreach (var s in spots) WarnOnly("circle", s, s, r, 1.0f);
            yield return 1.0f;
            Vector3 mouth = Pos + Vector3.up * Height * 0.4f + Forward * Reach * 0.5f;
            foreach (var s in spots) Send("stone.spit", mouth, s);
            yield return 0.5f;
            foreach (var s in spots)
            {
                Hit(Circle(s, r), 40f, 14f, 18f, s, 4f);
                Send("stone.pool", s, s, 7000, Mathf.RoundToInt(r * 100f));
                Hazard(s, r, 7f, Scaled(4f), Scaled(5f));
            }
            AutoTest.Count("stone_venom");
        }

        private IEnumerator Gaze()
        {
            Vector3 dir = (Heart() - Pos).Flat().normalized, at = new Vector3(Pos.x, GroundY(Pos), Pos.z);
            yield return Turn(Pos + dir, 0.4f);
            Shout("stoneGaze");
            Send("stone.eyes", Pos, Pos, 1500);
            yield return Warn("cone", at, at + dir * 22f, 0f, 1.5f, breakable: true, extra: 35);
            Send("stone.gaze", Pos + Vector3.up * Height * 0.5f, at + dir * 22f, 0, 35);
            var cone = Cone(at, dir, 22f, 35f);
            float hold = 7f + Tier * 0.3f;
            foreach (var t in Pieces.Where(p => Catalog.IsTurret(p.Type) && cone(p.Pos)).ToList())
            {
                Bosses.Instance.Silence(t.Id, hold);
                Send("stone.petrify", t.Pos, Pos, Mathf.RoundToInt(hold * 1000f), t.Id);
                AutoTest.Count("stone_petrified");
            }
            HurtPlayers(cone, Mathf.Min(70, Scaled(24f)), Pos, 5f);
            AutoTest.Count("stone_gaze");
            yield return 0.5f;
        }
    }

    // ---------- 6. wave 60: the Sky Torpedo Admiral (a flying fish) ----------
    // Flies round the island, high. Dive: a bombing run along a lane through the fort (breakable: shoot it down!). Squadron:
    // small flying fish that circle, then dive on turrets and blow up (shoot them first). Sonic boom: a pass so fast the
    // air shatters walls along a wide lane (breakable). Broken, it falls out of the sky and lies there a moment.
    internal class Skyfish : BossBrain
    {
        private const float Ring = 30f, Alt = 15f;
        private float _a;
        private static float Sea => WaterManager.GetWaterHeight(Center);
        private Vector3 OrbitPoint(float a) => Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Ring + Vector3.up * (Sea + Alt - Center.y);

        protected override IEnumerator Entrance(float seconds)
        {
            Vector3 dir = (Center - Home).Flat().normalized, side = Vector3.Cross(Vector3.up, dir);
            Vector3 a = Pos, low = Home + dir * 6f + Vector3.up * 1f, up = Home + dir * 2f + Vector3.up * 9f;
            for (float t = 0f; t < 0.9f && Alive; t += Time.deltaTime) { Vector3 p = Vector3.Lerp(a, low, t / 0.9f); Put(p, Facing(low - a) * Quaternion.Euler(10f, 0f, 30f * Mathf.Sin(t * 4f))); yield return null; }
            Send("sky.pass", low, low);
            for (float t = 0f; t < 0.7f && Alive; t += Time.deltaTime) { float k = t / 0.7f; Put(Vector3.Lerp(low, up, k), Facing(dir) * Quaternion.Euler(-60f * (1f - k), 0f, 0f)); yield return null; }
            for (float t = 0f; t < 0.6f && Alive; t += Time.deltaTime) { Put(Vector3.Lerp(up, Home, t / 0.6f), Quaternion.Slerp(T.rotation, Facing(dir), t / 0.6f)); yield return null; }
            Put(Home, Facing(Center - Home));
        }

        protected override IEnumerator Act()
        {
            yield return Orbit(Random.Range(2f, 3.5f));
            int squad = Bosses.Instance.All.Count(b => !b.IsBoss && b.Kind == EnemyKind.Squadron);
            switch (Pick(4f, squad < 3 ? 3f : 0f, 3f))
            {
                case 0: yield return Torpedo(); break;
                case 1: yield return Squadron(Phase2 ? 6 : 4); break;
                default: yield return Boom(); break;
            }
            yield return Breath * 0.6f;
        }

        private IEnumerator Orbit(float seconds)
        {
            _a = Mathf.Atan2(Pos.z - Center.z, Pos.x - Center.x);
            yield return Glide(OrbitPoint(_a), 16f);
            for (float t = 0f; t < seconds && Alive; t += Time.deltaTime)
            {
                _a += 14f / Ring * Time.deltaTime * SlowFactor;
                Vector3 tangent = new Vector3(-Mathf.Sin(_a), 0f, Mathf.Cos(_a));
                Put(OrbitPoint(_a) + Vector3.up * Mathf.Sin(t * 1.3f) * 1.2f, Facing(tangent) * Quaternion.Euler(0f, 0f, 28f));
                yield return null;
            }
        }

        private IEnumerator Torpedo()
        {
            Vector3 target = Heart(), run = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * (target - Pos).Flat().normalized;
            Vector3 a = target - run * 20f, b = target + run * 20f;
            a.y = GroundY(a); b.y = GroundY(b);
            yield return Glide(a - run * 10f + Vector3.up * 14f, 24f);
            Shout("skyfishDive");
            yield return Warn("lane", a, b, 4.5f, 1.4f, breakable: true, during: k => Put(Pos, Facing(run) * Quaternion.Euler(18f * k, 0f, 0f)));
            float len = (b - a).Flat().magnitude;
            Vector3 last = a - run * 99f;
            for (float d = -10f; d < len + 10f && Alive; d += 28f * SlowFactor * Time.deltaTime)
            {
                Vector3 g = a + run * d;
                float h = d < 0f ? 3f - d * 1.1f : d > len ? 3f + (d - len) * 1.1f : 3f;
                Put(new Vector3(g.x, GroundY(g) + Foot + h, g.z), Facing(run) * Quaternion.Euler(d < 0f ? 25f : d > len ? -25f : 0f, 0f, 0f));
                if (d >= 0f && d <= len && (g - last).Flat().magnitude >= (Phase2 ? 2.2f : 3.2f))
                {
                    last = g;
                    g.y = GroundY(g);
                    Hit(Circle(g, 2.6f), 60f, 20f, 30f, g, 8f);
                    Send("sky.bomb", g, g);
                }
                yield return null;
            }
            AutoTest.Count("skyfish_dive");
        }

        private IEnumerator Squadron(int n)
        {
            Shout("skyfishSquadron");
            Send("sky.call", Pos, Pos);
            for (int i = 0; i < n && Alive; i++)
            {
                Summon(EnemyKind.Squadron, Pos + Quaternion.Euler(0f, i * 360f / n, 0f) * Vector3.forward * 5f - Vector3.up * 1f, new Kamikaze(), 1f);
                yield return 0.15f;
            }
            AutoTest.Count("skyfish_squadron", n);
        }

        private IEnumerator Boom()
        {
            Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            Vector3 a = Center - dir * 30f, b = Center + dir * 30f;
            a.y = GroundY(a); b.y = GroundY(b);
            yield return Glide(a - dir * 12f + Vector3.up * 24f, 26f);
            Shout("skyfishBoom");
            yield return Warn("lane", a, b, 7f, 1.8f, breakable: true);
            float len = (b - a).Flat().magnitude;
            Send("sky.boom", a + Vector3.up * 7f, b + Vector3.up * 7f, Mathf.RoundToInt(len / 50f * 1000f));
            var hit = new HashSet<int>();
            var hurt = new HashSet<Player>();
            for (float d = -8f; d < len + 8f && Alive; d += 50f * Time.deltaTime)
            {
                Vector3 g = a + dir * d;
                Put(new Vector3(g.x, GroundY(g) + Foot + 7f, g.z), Facing(dir));
                if (d >= 0f && d <= len) HitOnce(hit, hurt, Circle(g, 3.5f), 140f, 30f, 60f, g, 14f);
                yield return null;
            }
            AutoTest.Count("skyfish_boom");
        }

        protected override IEnumerator Fall()
        {
            Vector3 from = Pos, to = Stand(Pos);
            Send("sky.fall", from, to);
            for (float t = 0f; t < 0.7f && Body; t += Time.deltaTime) { float k = t / 0.7f; Put(Vector3.Lerp(from, to, k * k), T.rotation * Quaternion.Euler(0f, 0f, 400f * Time.deltaTime)); yield return null; }
            Put(to, Facing(Forward) * Quaternion.Euler(0f, 0f, 90f));
            Send("stomp", to, to);
        }
        protected override IEnumerator Recover() { yield return Glide(Pos + Vector3.up * 10f, 8f); }

        protected override IEnumerator SecondForm()
        {
            Shout("skyfishPhase2");
            Send("phase2", Pos, Pos);
            yield return Glide(Pos + Vector3.up * 8f, 10f);
        }
    }

    // A flying fish of the squadron: wheels round a moment, picks a turret, dives on it and blows up.
    internal class Kamikaze : BossBrain
    {
        protected override IEnumerator Act()
        {
            Vector3 c = Pos;
            float a = Random.value * 6.28f;
            for (float t = 0f, end = Random.Range(1.2f, 2.4f); t < end && Alive; t += Time.deltaTime)
            {
                a += 2.4f * Time.deltaTime;
                Put(c + new Vector3(Mathf.Cos(a) * 3f, Mathf.Sin(t * 3f) * 0.3f, Mathf.Sin(a) * 3f), Facing(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a))));
                yield return null;
            }
            var turret = Best(p => Catalog.IsTurret(p.Type), p => -(p.Pos - Pos).Flat().magnitude + Random.value * 12f);
            Vector3 at = turret.HasValue ? turret.Value.Pos : Core;
            yield return Warn("circle", at, at, 2.2f, 1.0f);
            yield return Glide(at + Vector3.up * 0.6f, 24f);
            Hit(Circle(at, 2.2f), 80f, 20f, 25f, at, 8f);
            Send("sky.bomb", at, at);
            AutoTest.Count("squadron_hit");
            FortressPlugin.Alive.Remove(Body); // blown up with it: no reward
            Stop();
            Body.DestroyItem();
        }
    }

    // ---------- 7. wave 70: the Crimson Kraken (a squid) ----------
    // In the sea off a beach, its head out of the water; moves between the three beaches under the sea. Sweep: tentacles
    // come up out of the surf and sweep the beach (breakable). Grab: a tentacle wraps round a turret and drags it into the
    // sea, gone (breakable: save it!). Ink: a cloud of ink over the beach: turrets in it can't see. Second form: a whirlpool
    // that drags players toward it; wider sweeps; it grabs two.
    internal class Kraken : BossBrain
    {
        private int _gap, _moves;
        private Vector3 SeaSpot(int gap) { var p = Center + GapDir(gap) * 31f; p.y = WaterManager.GetWaterHeight(p) - Height * 0.3f + Foot; return p; }

        protected override IEnumerator Entrance(float seconds)
        {
            _gap = NearestGap(Home);
            Vector3 to = SeaSpot(_gap);
            Send("kraken.rise", to, to, 1600);
            yield return 0.5f;
            Vector3 from = Pos;
            for (float t = 0f; t < 1.3f && Alive; t += Time.deltaTime) { Put(Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / 1.3f)), Facing(Center - to)); yield return null; }
            Put(to, Facing(Center - to));
        }

        protected override IEnumerator Act()
        {
            if (++_moves % 3 == 0) yield return Move((_gap + (Random.value < 0.5f ? 1 : 2)) % 3);
            switch (Pick(4f, 3f, 3f, Phase2 ? 2.5f : 0f))
            {
                case 0: yield return Sweep(); break;
                case 1: yield return Grab(Phase2 ? 2 : 1); break;
                case 2: yield return Ink(); break;
                default: yield return Whirl(); break;
            }
            yield return Breath;
        }

        private IEnumerator Move(int gap)
        {
            Vector3 from = Pos, down = Pos + Vector3.down * (Height + 2f);
            Send("kraken.dive", from, from);
            for (float t = 0f; t < 0.8f && Alive; t += Time.deltaTime) { Put(Vector3.Lerp(from, down, t / 0.8f)); yield return null; }
            Siege.Instance.Hide(Body, true);
            _gap = gap;
            Vector3 to = SeaSpot(gap), under = to + Vector3.down * (Height + 2f);
            Put(under, Facing(Center - to));
            yield return 1.0f;
            Send("kraken.rise", to, to, 900);
            yield return 0.4f;
            Siege.Instance.Hide(Body, false);
            for (float t = 0f; t < 0.9f && Alive; t += Time.deltaTime) { Put(Vector3.Lerp(under, to, Mathf.SmoothStep(0f, 1f, t / 0.9f))); yield return null; }
            AutoTest.Count("kraken_moved");
        }

        protected override void Interrupted() { base.Interrupted(); if (Siege.Instance.IsHidden(Body)) { Siege.Instance.Hide(Body, false); Put(SeaSpot(_gap)); } }

        private Vector3 Shore => new Vector3(Pos.x, WaterManager.GetWaterHeight(Pos), Pos.z);

        private IEnumerator Sweep()
        {
            Vector3 dir = (Center - Pos).Flat().normalized;
            float range = 21f, half = Phase2 ? 50f : 38f;
            Shout("krakenSweep");
            Send("kraken.tentacles", Shore, Shore + dir * range, 1400, Phase2 ? 5 : 3);
            yield return Warn("cone", Shore, Shore + dir * range, 0f, 1.4f, breakable: true, extra: Mathf.RoundToInt(half));
            Hit(Cone(Shore, dir, range, half), 95f, 30f, 45f, Shore, 12f);
            Send("kraken.sweep", Shore, Shore + dir * range, 0, Mathf.RoundToInt(half));
            AutoTest.Count("kraken_sweep");
            yield return 0.6f;
        }

        private IEnumerator Grab(int n)
        {
            var turrets = Pieces.Where(p => Catalog.IsTurret(p.Type) && (p.Pos - Pos).Flat().magnitude < 26f).OrderBy(p => (p.Pos - Pos).sqrMagnitude).Take(n).ToList();
            if (turrets.Count == 0) { yield return Sweep(); yield break; }
            Shout("krakenGrab", true);
            foreach (var t in turrets) Send("kraken.reach", t.Pos, Shore, 2000, t.Id);
            foreach (var t in turrets.Skip(1)) WarnOnly("circle", t.Pos, t.Pos, 2.2f, 2f);
            yield return Warn("circle", turrets[0].Pos, turrets[0].Pos, 2.2f, 2.0f, breakable: true, breakShare: 0.05f);
            var ids = new HashSet<int>(Pieces.Select(p => p.Id));
            foreach (var t in turrets)
            {
                if (!ids.Contains(t.Id)) continue; // gone already
                Send("kraken.grab", t.Pos, Shore, 0, t.Id); // (before it goes: every screen takes a copy of it to drag under)
                BuildSystem.Instance.ServerTakePiece(t.Id);
                AutoTest.Count("kraken_grab");
            }
            yield return 0.8f;
        }

        private IEnumerator Ink()
        {
            Vector3 at = Pos + (Center - Pos).Flat().normalized * 14f;
            at.y = GroundY(at);
            float r = 7f + Tier * 0.1f;
            yield return Warn("circle", at, at, r, 1.0f);
            Send("kraken.ink", at, Shore, 6000, Mathf.RoundToInt(r * 100f));
            foreach (var t in Pieces.Where(p => Catalog.IsTurret(p.Type) && (p.Pos - at).Flat().magnitude <= r).ToList()) Bosses.Instance.Silence(t.Id, 6f);
            Hazard(at, r, 6f, Scaled(2f), 0);
            Shout("krakenInk");
            AutoTest.Count("kraken_ink");
        }

        private IEnumerator Whirl()
        {
            Vector3 at = Shore + (Center - Pos).Flat().normalized * 5f;
            at.y = WaterManager.GetWaterHeight(at);
            Shout("krakenWhirl");
            Send("kraken.whirl", at, at, 7000, 1500);
            for (float t = 0f; t < 7f && Alive; t += 0.5f)
            {
                foreach (var p in Targets.ToList())
                {
                    Vector3 d = (at - p.Transform.position).Flat();
                    if (d.magnitude > 16f) continue;
                    if (d.magnitude < 4f) Siege.HurtPlayer(p, Mathf.Min(70, Scaled(6f)), Vector3.up * 4f);
                    else Siege.HurtPlayer(p, 1, d.normalized * 7f + Vector3.up);
                }
                yield return 0.5f;
            }
            AutoTest.Count("kraken_whirl");
        }
    }

    // ---------- 8. wave 80: the Shattered Pixel King (a voxel fish) ----------
    // Hangs low in the air and blinks about (it breaks into cubes and comes together elsewhere). Copy-paste: copies of itself
    // that shoot (it isn't one of them). Delete: a selection box over the fort, then nearly everything in it is gone
    // (breakable). Undo: it rewinds its own health unless it's stopped (breakable). Bolts: a barrage of pixels. Second
    // form, FATAL ERROR: the screens glitch, two boxes, more copies.
    internal class Voxel : BossBrain
    {
        private const float Hover = 2.5f;
        private int _undos;

        protected override IEnumerator Entrance(float seconds)
        {
            Send("voxel.load", Home, Home, 900);
            yield return 0.9f;
            Put(Home, Facing(Center - Home));
            Send("voxel.in", Home, Home);
        }

        private IEnumerator Blink(Vector3 to)
        {
            Send("voxel.out", Pos, Pos);
            yield return 0.15f;
            Put(to, Facing(Core - to));
            Send("voxel.in", to, to);
            yield return 0.3f;
        }

        private Vector3 HoverSpot()
        {
            Vector3 p = SpotNear(Center, 12f, 22f);
            p.y = GroundY(p) + Foot + Hover;
            return p;
        }

        protected override IEnumerator Act()
        {
            yield return Blink(HoverSpot());
            int copies = Bosses.Instance.All.Count(b => !b.IsBoss && b.Kind == EnemyKind.Glitch);
            switch (Pick(copies < (Phase2 ? 4 : 2) ? 3f : 0f, 4f, Hp01 < 0.85f && _undos < 3 ? 2f : 0f, 3f))
            {
                case 0: yield return Copy(Phase2 ? 4 : 2); break;
                case 1: yield return Delete(Phase2 ? 2 : 1); break;
                case 2: yield return Undo(); break;
                default: yield return Bolts(Phase2 ? 14 : 9); break;
            }
            yield return Breath;
        }

        private IEnumerator Copy(int n)
        {
            Shout("voxelCopy");
            Send("voxel.copy", Pos + Vector3.up * (Height + 1f), Pos, 900);
            yield return 0.9f;
            for (int i = 0; i < n; i++)
            {
                Vector3 at = HoverSpot();
                if (Summon(EnemyKind.Glitch, at - Vector3.up * Hover, new GlitchCopy(), 1f)) Send("voxel.in", at, at);
            }
            AutoTest.Count("voxel_copies", n);
            yield return Blink(HoverSpot());
        }

        private IEnumerator Delete(int boxes)
        {
            const float half = 4f;
            var centres = new List<(Vector3 c, Vector3 to)>();
            var pieces = Pieces.ToList();
            for (int i = 0; i < boxes; i++)
            {
                var best = pieces.OrderByDescending(p => pieces.Count(q => (q.Pos - p.Pos).Flat().magnitude < half) + Random.value).Select(p => p.Pos).FirstOrDefault();
                Vector3 c = best != Vector3.zero && centres.All(x => (x.c - best).Flat().magnitude > half * 2f) ? best : SpotNear(Core, 3f, 12f);
                c.y = GroundY(c);
                Vector3 to = c + Quaternion.Euler(0f, Random.Range(0f, 90f), 0f) * Vector3.forward;
                centres.Add((c, to));
                WarnOnly("box", c, to, half, 1.6f);
                Send("voxel.select", c, to, 1600, Mathf.RoundToInt(half * 100f));
            }
            Shout("voxelDelete", true);
            yield return Warn("circle", new Vector3(Pos.x, GroundY(Pos), Pos.z), Pos, 2f, 1.6f, breakable: true);
            foreach (var (c, to) in centres)
            {
                Vector3 f = (to - c).Flat().normalized;
                var inBox = Lane(c - f * half, c + f * half, half * 2f);
                foreach (var p in Pieces.Where(p => inBox(p.Pos)).ToList()) BuildSystem.Instance.ServerDamagePiece(p.Id, Mathf.Max(Scaled(140f), Mathf.RoundToInt(p.MaxHp * 0.45f)));
                if (inBox(Core)) Siege.Instance.DamageCore(Scaled(60f));
                HurtPlayers(inBox, Mathf.Min(70, Scaled(25f)), c, 6f);
                Send("voxel.delete", c, to, 0, Mathf.RoundToInt(half * 100f));
            }
            AutoTest.Count("voxel_delete");
            yield return 0.5f;
        }

        private IEnumerator Undo()
        {
            _undos++;
            Shout("voxelUndo", true);
            Send("voxel.undo", Pos, Pos, 3000);
            Vector3 g = new Vector3(Pos.x, GroundY(Pos), Pos.z);
            yield return Warn("ring", g, g, Reach + 3f, 3f, breakable: true, breakShare: 0.03f, extra: Mathf.RoundToInt((Reach + 1f) * 100f));
            Body.ServerChangeHp(-Mathf.RoundToInt(MaxHp * 0.12f));
            Send("voxel.restored", Pos, Pos);
            AutoTest.Count("voxel_undo");
        }

        private IEnumerator Bolts(int n)
        {
            var spots = BossBrains.Spots(Pos, n, 24f);
            foreach (var s in spots) WarnOnly("circle", s, s, 1.3f, 0.8f);
            yield return 0.8f;
            foreach (var s in spots) Send("voxel.bolt", Pos + Forward * Reach * 0.7f, s);
            yield return 0.35f;
            foreach (var s in spots) Hit(Circle(s, 1.3f), 35f, 14f, 18f, s, 4f);
            AutoTest.Count("voxel_bolts");
        }

        protected override IEnumerator SecondForm()
        {
            Shout("voxelPhase2");
            Send("voxel.fatal", Pos, Pos, 2500);
            yield return 2.5f;
        }
    }

    // One of the voxel fish's copies: blinks about and shoots a pixel at the buildings now and then.
    internal class GlitchCopy : BossBrain
    {
        protected override IEnumerator Act()
        {
            yield return Random.Range(1.5f, 3f);
            var spot = BossBrains.Spots(Pos, 1, 20f).FirstOrDefault();
            if (spot == Vector3.zero) yield break;
            yield return Warn("circle", spot, spot, 1.3f, 0.8f);
            Send("voxel.bolt", Pos, spot);
            yield return 0.35f;
            Hit(Circle(spot, 1.3f), 30f, 12f, 15f, spot, 4f);
            if (Random.value < 0.35f)
            {
                Vector3 to = SpotNear(Center, 12f, 22f);
                to.y = GroundY(to) + Foot + 2.5f;
                Send("voxel.out", Pos, Pos);
                Put(to, Facing(Core - to));
                Send("voxel.in", to, to);
            }
        }
    }

    // ---------- 9. wave 90: the Koi Who Leapt the Dragon Gate (a koi; a dragon in its second form) ----------
    // Swims through the air round the island. Jet: a beam of water swept across the fort (breakable). Gate leap: up high,
    // then it plunges onto the fort (breakable). Rain: drops the size of boulders. Second form: it leaps the dragon gate and
    // becomes a dragon: fire breath over the fort (breakable; what it hits burns), heaven's thunder on marked spots.
    internal class Koi : BossBrain
    {
        private float _a, _t;
        private static float Sea => WaterManager.GetWaterHeight(Center);
        private Vector3 SwimPoint(float a, float t) => Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (23f + 3f * Mathf.Sin(t * 0.7f)) + Vector3.up * (Sea + 8f + 2.5f * Mathf.Sin(t * 1.3f) - Center.y);
        private Vector3 Mouth => Pos + Forward * Reach * 0.9f;

        protected override IEnumerator Entrance(float seconds)
        {
            Vector3 gate = new Vector3(Pos.x, WaterManager.GetWaterHeight(Pos), Pos.z);
            Send("koi.gate", gate, gate, 2200);
            yield return 0.5f;
            yield return Leap(Home, 10f, 1.3f);
        }

        protected override IEnumerator Act()
        {
            yield return Swim(Random.Range(2f, 3.5f));
            if (!Phase2)
                switch (Pick(4f, 3f, 3f))
                {
                    case 0: yield return Jet(); break;
                    case 1: yield return GateLeap(); break;
                    default: yield return Rain(10 + Tier / 2); break;
                }
            else
                switch (Pick(4f, 3f, 3f))
                {
                    case 0: yield return Fire(); break;
                    case 1: yield return Thunder(8); break;
                    default: yield return GateLeap(); break;
                }
            yield return Breath;
        }

        private IEnumerator Swim(float seconds)
        {
            _a = Mathf.Atan2(Pos.z - Center.z, Pos.x - Center.x);
            yield return Glide(SwimPoint(_a, _t), 12f);
            for (float t = 0f; t < seconds && Alive; t += Time.deltaTime)
            {
                _a += 9f / 23f * Time.deltaTime * SlowFactor;
                _t += Time.deltaTime;
                Vector3 p = SwimPoint(_a, _t), d = SwimPoint(_a + 0.05f, _t + 0.05f) - p;
                Put(p, Quaternion.LookRotation(d.normalized) * Quaternion.Euler(0f, 0f, Mathf.Sin(_t * 2f) * 12f) * BossLooks.Turn(Kind));
                yield return null;
            }
        }

        private IEnumerator Jet()
        {
            Vector3 aim = Heart();
            yield return Turn(aim, 0.4f);
            Vector3 dir = (aim - Pos).Flat().normalized, g = new Vector3(Pos.x, GroundY(Pos), Pos.z);
            const float len = 24f, sweep = 28f;
            Shout("koiJet");
            yield return Warn("cone", g, g + dir * len, 0f, 1.2f, breakable: true, extra: Mathf.RoundToInt(sweep));
            Send("koi.jet", Mouth, g + dir * len, 1300, Mathf.RoundToInt(sweep));
            float next = 0f;
            for (float t = 0f; t < 1.3f && Alive; t += Time.deltaTime)
            {
                if (t >= next)
                {
                    next = t + 0.15f;
                    Vector3 end = g + Quaternion.Euler(0f, Mathf.Lerp(-sweep, sweep, t / 1.3f), 0f) * dir * len;
                    end.y = GroundY(end);
                    Hit(Lane(g, end, 1.8f), 30f, 10f, 12f, g, 5f);
                }
                yield return null;
            }
            AutoTest.Count("koi_jet");
        }

        private IEnumerator GateLeap()
        {
            Vector3 at = Heart();
            at.y = GroundY(at);
            float r = Phase2 ? 6.5f : 5.5f;
            yield return Glide(Vector3.Lerp(Pos, at, 0.4f) + Vector3.up * 18f, 16f);
            if (Phase2) Shout("koiDragonDive"); else Shout("koiLeap");
            yield return Warn("circle", at, at, r, 1.5f, breakable: true, during: k => Put(Pos, Quaternion.LookRotation((at - Pos).normalized) * BossLooks.Turn(Kind)));
            Vector3 from = Pos, to = at + Vector3.up * Foot * 0.4f;
            for (float t = 0f; t < 0.45f && Alive; t += Time.deltaTime) { float k = t / 0.45f; Put(Vector3.Lerp(from, to, k * k)); yield return null; }
            Hit(Circle(at, r), 120f, 35f, 55f, at, 14f);
            Send("koi.splash", at, at, 0, Mathf.RoundToInt(r * 100f));
            AutoTest.Count("koi_leap");
            yield return 0.4f;
            yield return Glide(at + Vector3.up * 9f, 10f);
        }

        private IEnumerator Rain(int n)
        {
            var spots = BossBrains.Spots(Core, n, 24f, 20f);
            foreach (var s in spots) WarnOnly("circle", s, s, 1.8f, 1.4f);
            Send("koi.call", Pos, Pos, 1400);
            yield return 1.4f;
            foreach (var s in spots) { Send("koi.drop", s, s); Hit(Circle(s, 1.8f), 40f, 14f, 18f, s, 5f); }
            AutoTest.Count("koi_rain");
        }

        private IEnumerator Fire()
        {
            Vector3 aim = Heart();
            yield return Turn(aim, 0.4f);
            Vector3 dir = (aim - Pos).Flat().normalized, g = new Vector3(Pos.x, GroundY(Pos), Pos.z);
            const float range = 21f, half = 30f;
            Shout("koiFire");
            yield return Warn("cone", g, g + dir * range, 0f, 1.4f, breakable: true, extra: Mathf.RoundToInt(half));
            Send("koi.breath", Mouth, g + dir * range, 1200, Mathf.RoundToInt(half));
            var cone = Cone(g, dir, range, half);
            Hit(cone, 110f, 35f, 50f, g, 8f);
            foreach (var p in Pieces.Where(x => cone(x.Pos)).ToList()) { Hazard(p.Pos, 1.2f, 4f, Scaled(4f), Scaled(10f)); Send("koi.burn", p.Pos, p.Pos, 4000); }
            AutoTest.Count("koi_fire");
            yield return 0.5f;
        }

        private IEnumerator Thunder(int n)
        {
            var spots = BossBrains.Spots(Core, n, 24f, 18f);
            foreach (var s in spots) WarnOnly("circle", s, s, 2.6f, 1.5f);
            Shout("koiThunder");
            Send("koi.charge", Pos, Pos, 1500);
            yield return 1.5f;
            foreach (var s in spots) { Send("koi.thunder", s, s); Hit(Circle(s, 2.6f), 90f, 30f, 40f, s, 8f); }
            AutoTest.Count("koi_thunder");
            yield return 0.4f;
        }

        protected override IEnumerator SecondForm()
        {
            Shout("koiDragon");
            yield return Glide(Center + (Pos - Center).Flat().normalized * 16f + Vector3.up * (Sea + 20f - Center.y), 12f);
            Send("koi.dragon", Pos, Pos, 2600);
            yield return 2.6f;
        }
    }

    // ---------- 10. wave 100: OVER 99, the Eye at the End (a black shell with a red slit; the game's foot snail) ----------
    // Hangs over the sea at the island's edge, its eye on the lighthouse. Gaze: a beam swept across the fort (breakable).
    // Ninety-nine spears: rain all over the island. Echoes: shadows of the nine bosses before it do their moves again.
    // Second form, OVER 99: the sky turns to blood; at a quarter health the Last Judgment: it hangs over the lighthouse for
    // eight seconds; if nobody breaks it, the lighthouse loses a third and everything round it is struck.
    internal class Eye : BossBrain
    {
        private const float Alt = 13f;
        private bool _judged;
        private static float Sea => WaterManager.GetWaterHeight(Center);

        protected override IEnumerator Entrance(float seconds)
        {
            Send("eye.descend", Home, Pos, 2600);
            Vector3 from = Pos;
            for (float t = 0f; t < 2.6f && Alive; t += Time.deltaTime) { Put(Vector3.Lerp(from, Home, Mathf.SmoothStep(0f, 1f, t / 2.6f)), Facing(Center - Home) * Quaternion.Euler(0f, 90f * (1f - t / 2.6f), 0f)); yield return null; }
            Put(Home, Facing(Center - Home));
        }

        protected override void OnStop() { Send("eye.red", Pos, Pos, 0); }

        protected override IEnumerator Act()
        {
            float a = Mathf.Atan2(Pos.z - Center.z, Pos.x - Center.x) + Random.Range(-0.8f, 0.8f);
            Vector3 to = Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 25f;
            to.y = Sea + Alt;
            yield return Glide(to, 5f);
            yield return Turn(Core, 0.6f);
            if (Phase2 && !_judged && Hp01 <= 0.25f) { yield return Judgment(); yield break; }
            switch (Pick(4f, 3.5f, 3.5f))
            {
                case 0: yield return Beam(); break;
                case 1: yield return Spears(Phase2 ? 3 : 2); break;
                default: yield return Echoes(Phase2 ? 3 : 2); break;
            }
            yield return Breath;
        }

        private Vector3 Under => new Vector3(Pos.x, GroundY(Pos), Pos.z);

        private IEnumerator Beam()
        {
            Vector3 dir = (Core - Pos).Flat().normalized, g = Under;
            const float range = 36f, sweep = 32f;
            Shout("eyeGaze");
            Send("eye.open", Pos, Pos, 1600);
            yield return Warn("cone", g, g + dir * range, 0f, 1.6f, breakable: true, breakShare: 0.035f, extra: Mathf.RoundToInt(sweep));
            Send("eye.beam", Pos, g + dir * range, 2400, Mathf.RoundToInt(sweep));
            float next = 0f;
            for (float t = 0f; t < 2.4f && Alive; t += Time.deltaTime)
            {
                if (t >= next)
                {
                    next = t + 0.15f;
                    Vector3 end = g + Quaternion.Euler(0f, Mathf.Lerp(-sweep, sweep, t / 2.4f), 0f) * dir * range;
                    Hit(Lane(g, end, 2.4f), 26f, 12f, 15f, g, 4f);
                }
                yield return null;
            }
            AutoTest.Count("eye_beam");
        }

        private IEnumerator Spears(int batches)
        {
            Shout("eyeSpears");
            for (int b = 0; b < batches && Alive; b++)
            {
                var spots = BossBrains.Spots(Core, 33, 30f, 26f);
                foreach (var s in spots) WarnOnly("circle", s, s, 1.5f, 1.6f);
                Send("eye.spears", Pos, Pos, 1600);
                yield return 1.6f;
                foreach (var s in spots) { Send("eye.spear", s, s); Hit(Circle(s, 1.5f), 50f, 18f, 25f, s, 5f); }
                AutoTest.Count("eye_spears");
                yield return 0.5f;
            }
        }

        // The bosses before it, as shadows: the warden's guillotine, the urchin's roll, the steed's cavalry, the stonefish's
        // spires, the admiral's bombing run, the kraken's sweep.
        private IEnumerator Echoes(int n)
        {
            Shout("eyeEchoes");
            var picks = Enumerable.Range(0, 6).OrderBy(_ => Random.value).Take(n).ToList();
            var hits = new List<System.Action>();
            foreach (int k in picks)
            {
                switch (k)
                {
                    case 0: // guillotines on the three toughest things
                        foreach (var p in Pieces.OrderByDescending(p => p.MaxHp + Random.value * 100f).Take(3).ToList())
                        { var at = p.Pos; WarnOnly("circle", at, at, 2.6f, 1.6f); hits.Add(() => { Hit(Circle(at, 2.6f), 150f, 30f, 45f, at, 9f); Send("eye.echo", at, at, 0, 260); }); }
                        break;
                    case 1: // a roll right across
                    {
                        Vector3 d = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward, a = Center - d * 26f, b = Center + d * 26f;
                        a.y = GroundY(a); b.y = GroundY(b);
                        WarnOnly("lane", a, b, 5f, 1.6f);
                        hits.Add(() => { Hit(Lane(a, b, 5f), 90f, 28f, 45f, Center, 12f); Send("eye.echoLane", a, b, 0, 500); });
                        break;
                    }
                    case 2: // three phantom riders
                    {
                        Vector3 along = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward, side = Vector3.Cross(Vector3.up, along);
                        for (int i = -1; i <= 1; i++)
                        {
                            Vector3 a = Center + side * i * 7f - along * 28f, b = Center + side * i * 7f + along * 28f;
                            a.y = GroundY(a); b.y = GroundY(b);
                            WarnOnly("lane", a, b, 3f, 1.6f);
                            hits.Add(() => { Hit(Lane(a, b, 3f), 70f, 26f, 35f, a, 10f); Send("eye.echoLane", a, b, 0, 300); });
                        }
                        break;
                    }
                    case 3: // spires out from a spot by the lighthouse
                    {
                        Vector3 o = SpotNear(Core, 4f, 10f);
                        for (int i = 0; i < 4; i++)
                        {
                            Vector3 e = o + Quaternion.Euler(0f, i * 90f + 45f, 0f) * Vector3.forward * 14f;
                            e.y = GroundY(e);
                            WarnOnly("lane", o, e, 2.4f, 1.6f);
                            hits.Add(() => { Hit(Lane(o, e, 2.4f), 85f, 30f, 40f, o, 10f); Send("eye.echoLane", o, e, 0, 240); });
                        }
                        break;
                    }
                    case 4: // a bombing run
                    {
                        Vector3 d = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward, a = Core - d * 20f, b = Core + d * 20f;
                        a.y = GroundY(a); b.y = GroundY(b);
                        WarnOnly("lane", a, b, 4.5f, 1.6f);
                        hits.Add(() => { for (float s = 0f; s <= 40f; s += 3.2f) { Vector3 g = Vector3.Lerp(a, b, s / 40f); Hit(Circle(g, 2.6f), 60f, 20f, 30f, g, 8f); Send("eye.echo", g, g, 0, 260); } });
                        break;
                    }
                    default: // a sweep in from the sea
                    {
                        int gap = Random.Range(0, 3);
                        Vector3 sea = Center + GapDir(gap) * 31f, dir = -GapDir(gap);
                        sea.y = WaterManager.GetWaterHeight(sea);
                        WarnOnly("cone", sea, sea + dir * 21f, 0f, 1.6f, 40);
                        hits.Add(() => { Hit(Cone(sea, dir, 21f, 40f), 95f, 30f, 45f, sea, 12f); Send("eye.echoCone", sea, sea + dir * 21f, 0, 40); });
                        break;
                    }
                }
            }
            Send("eye.open", Pos, Pos, 1600);
            yield return 1.6f;
            foreach (var h in hits) h();
            AutoTest.Count("eye_echoes");
            yield return 0.6f;
        }

        private IEnumerator Judgment()
        {
            _judged = true;
            Shout("eyeJudgment", true);
            yield return Glide(Core + Vector3.up * (Alt + 4f), 10f);
            Send("eye.judgment", Pos, Core, 8000);
            yield return Warn("circle", Core, Core, 12f, 8f, breakable: true, breakShare: 0.06f);
            Siege.Instance.DamageCore(Mathf.RoundToInt(Siege.Instance.CoreMax * 0.3f));
            Hit(Circle(Core, 12f), 200f, 45f, 0f, Core, 16f);
            Send("eye.verdict", Core, Core);
            AutoTest.Count("eye_judgment");
            yield return 1f;
        }

        protected override IEnumerator Fall()
        {
            Vector3 from = Pos, to = Pos - Vector3.up * 6f;
            for (float t = 0f; t < 0.5f && Body; t += Time.deltaTime) { Put(Vector3.Lerp(from, to, t / 0.5f)); yield return null; }
        }
        protected override IEnumerator Recover() { yield return Glide(Pos + Vector3.up * 6f, 6f); }

        protected override IEnumerator SecondForm()
        {
            Shout("eyePhase2");
            Send("eye.red", Pos, Pos, 1);
            Send("phase2", Pos, Pos);
            Send("eye.open", Pos, Pos, 2000);
            yield return 2f;
        }
    }
}
