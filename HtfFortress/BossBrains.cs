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
            Put(Stand(Home), r);
        }

        protected override bool Grounded => true;

        protected override IEnumerator Act()
        {
            yield return WalkTo(TowerPoint(34f), 5.0f, 2.8f, Phase2 ? 3.2f : 5f); // a melee boss: get right up to the tower
            switch (Pick(4f, 3f, Phase2 ? 1.5f : 2.2f, Phase2 ? 3f : 0f))
            {
                case 0: yield return Guillotine(Phase2 ? 2 : 1); break;
                case 1: yield return Shackles(Phase2 ? 3 : 2); break;
                case 2: yield return Shell(Phase2 ? 5f : 4f); break;
                default: yield return Anvil(); break;
            }
            yield return Prowl(Breath); // (a lobster: it scuttles sideways round the tower it's after)
        }
        protected override IEnumerator NewMove() => Anvil();

        // Anvil (second form): both claws up, then down together; the shock rolls out all round it. Nothing near it is
        // spared, so the answer is to break it while the claws are up or to be outside the ring when they fall.
        private IEnumerator Anvil()
        {
            float r = Reach + 5.5f, inner = Reach * 0.6f; // the hole is its own footprint (the warning rings its body)
            Vector3 g = new Vector3(Pos.x, GroundY(Pos), Pos.z);
            var claws = Lean(Pos + Forward, 28f);
            Shout("wardenAnvil");
            Send("warden.heat", Pos, Pos, 1400);
            yield return Warn("ring", g, g, r, 1.4f, breakable: true, extra: Mathf.RoundToInt(inner * 100f), during: Coil(claws, 1.4f));
            claws(-Over, 1f);
            var disc = Circle(g, r);
            Hit(p => disc(p) && (p - g).Flat().sqrMagnitude >= inner * inner, 90f, 28f, 40f, g, 11f);
            Send("warden.anvil", g, Pos + Forward * Reach * 0.8f, 0, Mathf.RoundToInt(r * 100f));
            AutoTest.Count("warden_anvil");
            yield return Recoil(claws, 0.5f);
        }

        private IEnumerator Guillotine(int times)
        {
            for (int i = 0; i < times && Alive; i++)
            {
                Vector3 at = Target();
                yield return Turn(at, 0.35f);
                var claws = Lean(at, 22f);
                Shout("wardenGuillotine");
                yield return Warn("circle", at, at, 2.6f, 1.3f, breakable: true, during: Coil(claws, 1.3f));
                claws(-Over, 1f);
                Hit(Circle(at, 2.6f), 170f, 30f, 45f, at, 9f);
                Send("warden.snap", at, Pos);
                AutoTest.Count("warden_guillotine");
                yield return Recoil(claws, 0.45f);
            }
        }

        private Vector3 Target()
        {
            var turret = TurretsNear(11f).OrderBy(x => (x.Pos - Pos).sqrMagnitude).FirstOrDefault();
            if (turret.Id != 0) return turret.Pos;
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
            var throwing = Lean(turrets.Aggregate(Vector3.zero, (s, t) => s + t.Pos) / turrets.Count, 16f, back: 0.4f);
            yield return Posed(0.9f, Coil(throwing, 0.9f));
            throwing(-Over, 1f);
            float hold = 6f + Tier * 0.3f;
            foreach (var t in turrets)
            {
                Bosses.Instance.Silence(t.Id, hold);
                BuildSystem.Instance.ServerDamagePiece(t.Id, ScaledPiece(35f));
                Send("warden.chain", Pos, t.Pos, Mathf.RoundToInt(hold * 1000f), t.Id);
            }
            Shout("wardenChains");
            AutoTest.Count("warden_shackles", turrets.Count);
            yield return Recoil(throwing, 0.5f);
        }

        private IEnumerator Shell(float seconds)
        {
            TurretMult = 0.25f;
            Send("warden.shell", Pos, Pos, Mathf.RoundToInt(seconds * 1000f));
            Shout("wardenShell");
            AutoTest.Count("warden_shell");
            yield return WalkTo(TowerPoint(28f), 3.6f, 2.2f, seconds);
            TurretMult = 1f;
        }

        // The furnace relit: it rears up, claws high and glowing from inside, and brings them down with a clang (1.8 s).
        protected override IEnumerator SecondForm()
        {
            Shout("wardenPhase2");
            Send("phase2", Pos, Pos);
            Send("warden.heat", Pos, Pos, 1300);
            var claws = Lean(Pos + Forward, 30f);
            yield return Posed(1.3f, Coil(claws, 1.3f));
            claws(-Over, 1f);
            Send("warden.snap", Pos + Forward * Reach * 0.8f, Pos);
            yield return Recoil(claws, 0.5f);
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

        protected override bool Grounded => true;
        protected override IEnumerator Prowl(float seconds) => Sidestep(seconds, TowerPoint(30f), 1.6f, 4f, put: (p, r) => RollTo(p));
        protected override IEnumerator Sag(float seconds) { yield return seconds; } // a ball: nothing to sag

        // Its spikes pressed into the ground (x 1, `depth` m down) and sprung up off it (below 0): a squat as it gathers
        // itself, a hop as it lets go.
        private System.Action<float, float> Squat(float depth)
        {
            Vector3 p0 = Pos;
            return (x, _) => Put(p0 - Vector3.up * depth * x, _spin);
        }

        protected override IEnumerator Act()
        {
            Vector3 goal = TowerPoint(30f);
            float v = Vel.Flat().magnitude;
            for (float t = 0f; t < (Phase2 ? 2.5f : 3.5f) && Alive && (goal - Pos).Flat().magnitude > 4.5f; t += Time.deltaTime)
            {
                Vector3 dir = (goal - Pos).Flat().normalized;
                var wall = WallAhead(dir);
                if (wall != null) { BuildSystem.Instance.ServerDamagePiece(wall.Value.Id, ScaledPiece(40f)); Send("stomp", wall.Value.Pos, wall.Value.Pos); v = 0f; yield return 0.5f; continue; }
                v = Mathf.MoveTowards(v, 4.5f, 8f * Time.deltaTime); // it gets rolling
                RollTo(Stand(Pos + dir * v * SlowFactor * Time.deltaTime));
                yield return null;
            }
            switch (Pick(4f, 3.5f, 2.5f, Phase2 ? 3f : 0f))
            {
                case 0: yield return Crush(Phase2 ? 2 : 1); break;
                case 1: yield return Volley(Phase2 ? 20 : 11 + Tier / 2); break;
                case 2: yield return Bloom(Phase2 ? 4 : 3); break;
                default: yield return Nova(); break;
            }
            yield return Prowl(Breath);
        }
        protected override IEnumerator NewMove() => Nova();

        private IEnumerator Crush(int lanes)
        {
            Vector3 dir = (TowerPoint(30f) - Pos).Flat().normalized;
            for (int i = 0; i < lanes && Alive; i++)
            {
                Vector3 a = Pos, b = Across(a, dir, 32f);
                dir = (b - a).Flat().normalized;
                Shout("urchinCrush");
                Vector3 d0 = dir;
                // It revs up spinning, rocking back off its mark, and is let go down the lane from it.
                yield return Warn("lane", new Vector3(a.x, GroundY(a), a.z), b, Radius * 2f + 1.2f, 1.4f, breakable: true,
                                  during: k => { Spin(d0, 200f + 900f * k); Put(Stand(a - d0 * 1.1f * Mathf.Max(0f, Windup(k, 1.4f))), _spin); });
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
            var squat = Squat(0.5f);
            yield return Posed(1.1f, Coil(squat, 1.1f));
            squat(-Over, 1f);
            Vector3 top = Pos + Vector3.up * Radius;
            foreach (var s in spots) Send("urchin.spike", top, s);
            yield return Recoil(squat, 0.45f);
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
                float r = Radius + 4f + i * 0.9f, s = i == 0 ? 1.1f : 0.65f;
                var squat = Squat(0.4f);
                yield return Warn("ring", Pos, Pos, r, s, breakable: i == 0, extra: Mathf.RoundToInt(Radius * 100f), during: Coil(squat, s));
                squat(-Over, 1f);
                var inside = Circle(Pos, r);
                Hit(p => inside(p) && (p - Pos).Flat().magnitude >= Radius * 0.8f, 50f, 22f, 25f, Pos, 10f);
                Send("urchin.bloom", Pos, Pos, 0, Mathf.RoundToInt(r * 100f));
                AutoTest.Count("urchin_bloom");
            }
        }

        // Bristle Nova (second form): it tenses on the spot, then every spike goes at once: one wide burst all round it, and
        // in the same instant a ring of spikes driven into the ground further out, left there as caltrops. Breakable.
        private IEnumerator Nova()
        {
            float r = Radius + 6f;
            Vector3 g = new Vector3(Pos.x, GroundY(Pos), Pos.z);
            var spots = new List<Vector3>();
            float a0 = Random.Range(0f, 360f);
            for (int i = 0; i < 8; i++)
            {
                Vector3 s = g + Quaternion.Euler(0f, a0 + i * 45f, 0f) * Vector3.forward * (r + 1.6f);
                if ((s - Center).Flat().magnitude > 28f) continue; // not out in the sea
                s.y = GroundY(s);
                spots.Add(s);
                WarnOnly("circle", s, s, 1.1f, 1.2f);
            }
            Shout("urchinNova");
            Send("urchin.bristle", Pos, Pos, 1200);
            Vector3 d0 = (Core - Pos).Flat().normalized;
            var squat = Squat(0.6f);
            yield return Warn("ring", g, g, r, 1.2f, breakable: true, extra: Mathf.RoundToInt(Radius * 100f), during: k => { Spin(d0, 60f + 300f * k); squat(Windup(k, 1.2f), 1f); });
            squat(-Over, 1f);
            var inside = Circle(g, r);
            Hit(p => inside(p) && (p - g).Flat().magnitude >= Radius * 0.8f, 65f, 24f, 30f, g, 11f);
            Send("urchin.nova", g, g, 0, Mathf.RoundToInt(r * 100f));
            foreach (var s in spots)
            {
                Hit(Circle(s, 1.1f), 35f, 12f, 15f, s, 5f);
                Send("urchin.caltrop", s, s, 6000);
                Hazard(s, 1.1f, 6f, Scaled(4f), 0);
            }
            AutoTest.Count("urchin_nova");
            yield return Recoil(squat, 0.4f);
        }

        // A thousand needles at once: it spins up on the spot, faster and faster, and every spike flares out (1.9 s, no harm).
        protected override IEnumerator SecondForm()
        {
            Shout("urchinPhase2");
            Send("phase2", Pos, Pos);
            Send("urchin.bristle", Pos, Pos, 1400);
            Vector3 d = (Core - Pos).Flat().normalized;
            var squat = Squat(0.7f);
            for (float t = 0f; t < 1.4f && Alive; t += Time.deltaTime) { Spin(d, 200f + 1200f * t / 1.4f); squat(Windup(t / 1.4f, 1.4f), 1f); yield return null; }
            squat(-Over, 1f);
            Send("urchin.bloom", Pos, Pos, 0, Mathf.RoundToInt((Radius + 5f) * 100f));
            yield return Recoil(squat, 0.5f);
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
            Vector3 to = Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(16f, 20f);
            to.y = GroundY(to) + Foot + Hover;
            yield return Glide(to, 3.5f);
            yield return Turn(Core, 0.5f);
            switch (Pick(Bosses.Dark ? 0f : 3f, 4f, 3.5f, Phase2 ? 3f : 0f))
            {
                case 0: yield return Veil(); break;
                case 1: yield return Lure(); break;
                case 2: yield return Devour(Phase2 ? 2 : 1); break;
                default: yield return Blackout(); break;
            }
            yield return Prowl(Breath);
        }
        protected override IEnumerator NewMove() => Blackout();
        protected override IEnumerator Prowl(float seconds) => Sidestep(seconds, Core, 1.2f, 3.5f); // drifting along the shore

        private IEnumerator Veil()
        {
            Send("angler.lure", Pos, Pos, 1200);
            yield return Posed(1.2f, Coil(Lean(Core, 12f, back: 0.8f, rise: 0.4f), 1.2f));
            Lights(false);
            _veilUntil = Time.time + 15f;
            Shout("anglerVeil");
            AutoTest.Count("angler_veil");
        }

        private IEnumerator Lure()
        {
            Shout("anglerLure", true);
            Send("angler.lure", Pos, Pos, 1700);
            var rear = Lean(Core, 14f, back: 1f);
            yield return Warn("circle", new Vector3(Pos.x, GroundY(Pos), Pos.z), Pos, Reach + 1f, 1.7f, breakable: true, during: Coil(rear, 1.7f));
            rear(-Over, 1f);
            Vector3 lantern = Lantern;
            Send("angler.flash", lantern, Pos);
            int n = 0;
            foreach (var p in Targets.ToList())
            {
                Vector3 eye = p.CamObject ? p.CamObject.position : p.Transform.position + Vector3.up * 1.6f;
                Vector3 look = p.CamObject ? p.CamObject.forward : p.Transform.forward;
                Vector3 to = lantern - eye;
                if (to.magnitude > 45f || Vector3.Angle(look, to) > 50f || Physics.Linecast(eye, lantern, GameInfo.LevelLayer)) continue;
                Siege.HurtPlayer(p, Mathf.Min(80, Scaled(18f)), (Pos - p.Transform.position).Flat().normalized * 16f + Vector3.up * 5f);
                n++;
            }
            AutoTest.Count("angler_lure");
            AutoTest.Count("angler_charmed", n);
            yield return Recoil(rear, 0.6f);
        }

        // Its lunges: shot off fast from drawn back, slowing as its jaws close on the end of the lane.
        private static float Lunge(float k) => 1f - (1f - k) * (1f - k);

        private IEnumerator Devour(int times)
        {
            for (int i = 0; i < times && Alive; i++)
            {
                Vector3 target = Heart(), dir = (target - Pos).Flat().normalized;
                Vector3 a = new Vector3(Pos.x, GroundY(Pos), Pos.z), b = a + dir * Mathf.Min(26f, (target - Pos).Flat().magnitude + 6f);
                b.y = GroundY(b);
                yield return Turn(target, 0.4f);
                yield return Warn("lane", a, b, 5f, 1.3f, breakable: true, during: Coil(Lean(target, 12f, back: 1.6f), 1.3f));
                var hit = new HashSet<int>();
                var hurt = new HashSet<Player>();
                Vector3 from = Pos, to = new Vector3(b.x, GroundY(b) + Foot + 0.8f, b.z);
                Send("angler.lunge", from, to, 700);
                for (float t = 0f; t < 0.7f && Alive; t += Time.deltaTime)
                {
                    Vector3 p = Vector3.Lerp(from, to, Lunge(t / 0.7f));
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

        // Blackout (second form): its lantern goes out for two seconds and only the lighthouse still shines. The dark is for
        // the eyes only (turrets and players are untouched by it); the one thing lit is the red lane, and when the lantern
        // flares back it comes down that lane out of the dark.
        private IEnumerator Blackout()
        {
            Vector3 target = Heart(), dir = (target - Pos).Flat().normalized;
            Vector3 a = new Vector3(Pos.x, GroundY(Pos), Pos.z), b = a + dir * Mathf.Min(24f, (target - Pos).Flat().magnitude + 6f);
            b.y = GroundY(b);
            yield return Turn(target, 0.4f);
            Shout("anglerBlackout");
            Send("angler.blackout", Pos, Pos, 2000);
            yield return Warn("lane", a, b, 4.5f, 2f, during: Coil(Lean(target, 12f, back: 1.6f), 2f));
            var hit = new HashSet<int>();
            var hurt = new HashSet<Player>();
            Vector3 from = Pos, to = new Vector3(b.x, GroundY(b) + Foot + 0.8f, b.z);
            Send("angler.lunge", from, to, 600);
            for (float t = 0f; t < 0.6f && Alive; t += Time.deltaTime)
            {
                Vector3 p = Vector3.Lerp(from, to, Lunge(t / 0.6f));
                Put(p, Facing(dir));
                HitOnce(hit, hurt, Circle(p, 3f), 90f, 28f, 40f, p, 10f);
                yield return null;
            }
            AutoTest.Count("angler_blackout");
            yield return Glide(new Vector3(Pos.x, GroundY(Pos) + Foot + Hover, Pos.z), 6f);
        }

        protected override IEnumerator SecondForm()
        {
            Shout("anglerPhase2");
            Send("phase2", Pos, Pos);
            Send("angler.lure", Pos, Pos, 1500);
            yield return Posed(1.5f, Coil(Lean(Core, 16f, back: 1f, rise: 0.6f), 1.5f));
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

        protected override bool Grounded => true;

        // On its hind legs (turned about its back hooves): up onto them fast, pawing the air there (BossSteed), and its
        // forehooves brought down hard in the last fifth.
        private IEnumerator Rear(float seconds, float deg)
        {
            Vector3 p0 = Pos;
            Quaternion r = T.rotation;
            for (float t = 0f; t < seconds && Alive; t += Time.deltaTime)
            {
                float k = t / seconds, up = k < 0.3f ? 1f - (1f - k / 0.3f) * (1f - k / 0.3f) : k < 0.8f ? 1f : 1f - (k - 0.8f) * (k - 0.8f) * 25f;
                Pose(p0, r, deg * up);
                yield return null;
            }
            Put(p0, r);
        }

        // Between moves it prances: a light hop aside, and round to face the fort again.
        protected override IEnumerator Prowl(float seconds)
        {
            Vector3 at = TowerPoint(30f), off = (Pos - at).Flat();
            float t0 = Time.time;
            if (seconds >= 1f && off.sqrMagnitude > 1f)
            {
                Vector3 side = Vector3.Cross(Vector3.up, off.normalized) * (Random.value < 0.5f ? 3.5f : -3.5f);
                foreach (var to in new[] { Pos + side, Pos - side })
                {
                    float y = LandY(to);
                    if ((to - Center).Flat().magnitude > 28f || y < WaterManager.GetWaterHeight(to) + 0.2f || Mathf.Abs(y - LandY(Pos)) > 2f) continue;
                    yield return Leap(Stand(to), 1.6f, 0.55f);
                    break;
                }
            }
            yield return Turn(at, Mathf.Max(0.1f, seconds - (Time.time - t0)));
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
            Vector3 to = SpotNear(TowerPoint(30f), 3.5f, 6.5f); // the horse closes in before it leaps
            yield return Leap(Stand(to), 5f, 0.9f);
            Send("steed.land", Pos, Pos);
            Hit(Circle(Pos, Reach + 1f), 30f, 12f, 0f, Pos, 6f);
            switch (Pick(4f, 3f, 3f))
            {
                case 0: yield return Charge(Phase2 ? 2 : 1); break;
                case 1: yield return Stomp(); break;
                default: yield return Cavalry(Phase2 ? 5 : 3); break;
            }
            yield return Prowl(Breath);
        }

        private IEnumerator Charge(int times)
        {
            Vector3 dir = (TowerPoint(30f) - Pos).Flat().normalized;
            for (int i = 0; i < times && Alive; i++)
            {
                Vector3 a = Pos, b = Across(a, dir, 34f);
                dir = (b - a).Flat().normalized;
                yield return Turn(b, 0.3f);
                Vector3 p0 = Pos;
                Quaternion r = T.rotation;
                Shout("steedCharge");
                float s = i == 0 ? 1.2f : 0.8f; // reared, then its forehooves slammed down to set off as the warning runs out
                yield return Warn("lane", new Vector3(a.x, GroundY(a), a.z), b, Mathf.Max(3.4f, 2f * (Reach * 0.6f + 1f)), s, breakable: true, // as wide as what it hits
                                  during: k => Pose(p0, r, 24f * Mathf.Max(0f, Windup(k, s))));
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

        protected override IEnumerator NewMove() => Stomp();

        private IEnumerator Stomp()
        {
            float r = 7.5f + Tier * 0.15f;
            Vector3 p0 = Pos, g = new Vector3(p0.x, GroundY(p0), p0.z);
            Quaternion rot = T.rotation;
            // Golden Fissure (second form): the stomp splits the ground in gold cracks running out past its edge; they stay
            // hot for a while and nip whoever walks on them (the stomp is the hard hit, the cracks only keep you moving).
            var cracks = new List<Vector3>();
            if (Phase2)
            {
                float a0 = Random.Range(0f, 360f);
                for (int i = 0; i < 6; i++)
                {
                    Vector3 e = g + Quaternion.Euler(0f, a0 + i * 60f + Random.Range(-12f, 12f), 0f) * Vector3.forward * (r + 3f);
                    e.y = GroundY(e);
                    cracks.Add(e);
                    WarnOnly("lane", g, e, 1.6f, 1.4f);
                }
                Shout("steedFissure");
            }
            Send("steed.rear", Pos, Pos);
            yield return Warn("circle", g, p0, r, 1.4f, breakable: true, during: k => Pose(p0, rot, 42f * Mathf.Max(0f, Windup(k, 1.4f))));
            Put(p0, rot);
            Hit(Circle(p0, r), 80f, 30f, 40f, p0, 12f);
            Send("steed.stomp", p0, p0, 0, Mathf.RoundToInt(r * 100f));
            AutoTest.Count("steed_stomp");
            foreach (var e in cracks)
            {
                Send("steed.fissure", g, e, 6000);
                Vector3 d = (e - g).Flat();
                for (float s = 1.8f; s <= d.magnitude; s += 1.8f) { Vector3 h = g + d.normalized * s; h.y = GroundY(h); Hazard(h, 1f, 6f, Scaled(2f), 0); }
            }
            if (cracks.Count > 0) AutoTest.Count("steed_fissure");
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

        protected override bool Grounded => true;
        protected override IEnumerator Prowl(float seconds) => Sidestep(seconds, TowerPoint(30f), 0.9f, 2.5f); // a slow, low shuffle

        // Bursting up out of the sand (fast, slowing as it tops out) and dropping back onto it.
        private IEnumerator Surface(Vector3 to, float seconds)
        {
            Vector3 from = Pos, over = to + Vector3.up * 1.5f;
            for (float t = 0f; t < seconds && Alive; t += Time.deltaTime) { float k = t / seconds; Put(Vector3.Lerp(from, over, 1f - (1f - k) * (1f - k))); yield return null; }
            for (float t = 0f; t < 0.3f && Alive; t += Time.deltaTime) { float k = t / 0.3f; Put(Vector3.Lerp(over, to, k * k)); yield return null; }
            Put(to);
        }

        protected override IEnumerator Act()
        {
            if (Random.value < 0.9f) yield return Burrow(SpotNear(TowerPoint(30f), 3.5f, 8f)); // ambush closer to the fort, not from the beach
            switch (Pick(4f, 3.5f, 2.5f, Phase2 ? 3f : 0f))
            {
                case 0: yield return Spires(Phase2 ? 5 : 3); break;
                case 1: yield return Venom(Phase2 ? 8 : 5); break;
                case 2: yield return Gaze(); break;
                default: yield return Geyser(); break;
            }
            yield return Prowl(Breath);
        }
        protected override IEnumerator NewMove() => Geyser();

        private Vector3 _hole; // where it last went under: Geyser's springs well up there

        private IEnumerator Burrow(Vector3 to)
        {
            to = Stand(to);
            Vector3 from = Pos, below = Pos + Vector3.down * (Height + 1.5f), under = to + Vector3.down * (Height + 1.5f);
            _hole = new Vector3(from.x, GroundY(from), from.z);
            Send("stone.dig", _hole, from);
            for (float t = 0f; t < 0.6f && Alive; t += Time.deltaTime) { float k = t / 0.6f; Put(Vector3.Lerp(from, below, k * k)); yield return null; } // digging in, then down
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
            Vector3 main = (TowerPoint(30f) - Pos).Flat().normalized, at = new Vector3(Pos.x, GroundY(Pos), Pos.z);
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
            var slam = Lean(at + main, 16f); // reared up facing the lines, then down on the ground they burst from
            yield return Warn("circle", at, at, Reach + 0.5f, 1.3f, breakable: true, during: Coil(slam, 1.3f));
            slam(-Over, 1f);
            foreach (var e in ends)
            {
                Send("stone.spires", at, e);
                Hit(Lane(at, e, 2.4f), 85f, 30f, 40f, at, 10f);
            }
            AutoTest.Count("stone_spires");
            yield return Recoil(slam, 0.6f);
        }

        private IEnumerator Venom(int n)
        {
            var spots = BossBrains.Spots(Pos, n, 22f);
            float r = Phase2 ? 3.2f : 2.4f;
            Send("stone.spines", Pos, Pos, 1000);
            foreach (var s in spots) WarnOnly("circle", s, s, r, 1.0f);
            var spit = Lean(spots.Count > 0 ? spots.Aggregate(Vector3.zero, (a, s) => a + s) / spots.Count : Pos + Forward, 12f, back: 0.4f);
            yield return Posed(1.0f, Coil(spit, 1.0f));
            spit(-Over, 1f);
            Vector3 mouth = Pos + Vector3.up * Height * 0.4f + Forward * Reach * 0.5f;
            foreach (var s in spots) Send("stone.spit", mouth, s);
            yield return Recoil(spit, 0.5f);
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
            Vector3 dir = (TowerPoint(30f) - Pos).Flat().normalized, at = new Vector3(Pos.x, GroundY(Pos), Pos.z);
            yield return Turn(Pos + dir, 0.4f);
            Shout("stoneGaze");
            Send("stone.eyes", Pos, Pos, 1500);
            var glare = Lean(Pos + dir, 10f, back: 0.3f);
            yield return Warn("cone", at, at + dir * 22f, 0f, 1.5f, breakable: true, extra: 35, during: Coil(glare, 1.5f));
            glare(-Over, 1f);
            Send("stone.gaze", Pos + Vector3.up * Height * 0.5f, at + dir * 22f, 0, 35);
            var cone = Cone(at, dir, 22f, 35f);
            float hold = 7f + Tier * 0.3f;
            foreach (var t in Pieces.Where(p => Catalog.IsTurret(p.Type) && cone(p.Pos)).ToList())
            {
                Bosses.Instance.Silence(t.Id, hold);
                Send("stone.petrify", t.Pos, Pos, Mathf.RoundToInt(hold * 1000f), t.Id);
                AutoTest.Count("stone_petrified");
            }
            HurtPlayers(cone, Mathf.Min(80, Scaled(24f)), Pos, 5f);
            AutoTest.Count("stone_gaze");
            yield return Recoil(glare, 0.5f);
        }

        // Geyser (second form): three venom springs well up in the hole it dug down through and burst every 1.5 s, three
        // times. Each burst is wound up on its own (it throbs while it pumps them), so breaking it shuts the springs at once.
        private IEnumerator Geyser()
        {
            Vector3 hole = _hole != Vector3.zero && (_hole - Center).Flat().magnitude < 28f ? _hole : new Vector3(Pos.x, GroundY(Pos), Pos.z);
            var springs = new List<Vector3>();
            float a0 = Random.Range(0f, 360f);
            for (int i = 0; i < 3; i++)
            {
                Vector3 s = hole + Quaternion.Euler(0f, a0 + i * 120f, 0f) * Vector3.forward * 2.6f;
                Vector3 off = (s - Pos).Flat(); // never under its own body (it may be standing by the hole, or not have dug at all)
                if (off.magnitude < Reach + 1.5f) s = Pos + (off.sqrMagnitude > 0.01f ? off.normalized : Vector3.forward) * (Reach + 1.5f);
                s.y = GroundY(s);
                springs.Add(s);
            }
            Shout("stoneGeyser");
            for (int burst = 0; burst < 3 && Alive; burst++)
            {
                foreach (var s in springs) Send("stone.vent", s, s, 1500);
                Send("stone.spines", Pos, Pos, 1200); // over before the next burst's: two throbs at once would leave it swollen
                for (int i = 1; i < springs.Count; i++) WarnOnly("circle", springs[i], springs[i], 2.2f, 1.5f);
                var pump = Lean(hole, 9f); // it heaves, and the springs burst as it bears down
                yield return Warn("circle", springs[0], springs[0], 2.2f, 1.5f, breakable: true, breakShare: 0.03f, during: Coil(pump, 1.5f)); // three short windows: a smaller share each
                pump(-Over, 1f);
                foreach (var s in springs)
                {
                    Hit(Circle(s, 2.2f), 55f, 20f, 25f, s, 9f);
                    Send("stone.geyser", s, s, 0, 220);
                }
                AutoTest.Count("stone_geyser");
            }
        }

        // The venom tide: its eyes light up, the ground shakes, its spines throb, and rock spires burst up round it (1.9 s).
        protected override IEnumerator SecondForm()
        {
            Shout("stonePhase2");
            Send("phase2", Pos, Pos);
            Send("stone.eyes", Pos, Pos, 1900);
            Send("stone.rumble", Pos, Pos, 1000);
            Send("stone.spines", Pos, Pos, 1000);
            var heave = Lean(Pos + Forward, 18f, rise: 0.5f);
            yield return Posed(1.0f, Coil(heave, 1.0f));
            heave(-Over, 1f);
            Send("stone.rise", Pos, Pos, 0, Mathf.RoundToInt((Reach + 1.5f) * 100f));
            yield return Recoil(heave, 0.9f);
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
            switch (Pick(4f, squad < 3 ? 3f : 0f, 3f, Phase2 ? 3f : 0f))
            {
                case 0: yield return Torpedo(); break;
                case 1: yield return Squadron(Phase2 ? 6 : 4); break;
                case 2: yield return Boom(); break;
                default: yield return DoubleBoom(); break;
            }
            yield return Prowl(Breath * 0.6f);
        }
        protected override IEnumerator NewMove() => DoubleBoom();
        protected override IEnumerator Sag(float seconds) { yield return seconds; } // it lies where it fell

        // Between moves it never hangs in the air: it swings out of its run onto its ring, a little ahead along it (in as
        // long as gliding back to it took before, or 0.8 s at least), and carries on round for its breath.
        protected override IEnumerator Prowl(float seconds)
        {
            float a = Mathf.Atan2(Pos.z - Center.z, Pos.x - Center.x);
            float glide = (OrbitPoint(a) - Pos).magnitude / 16f, fly = Mathf.Max(0.8f, glide);
            yield return FlyOnto(OrbitPoint(a + 0.3f), fly, Tangent(a + 0.3f) * 14f, 28f);
            _a = a + 0.3f;
            _bob = 0f;
            yield return Round(Mathf.Max(0f, seconds - (fly - glide)));
        }

        private float _bob, _rounded = -9f;
        private IEnumerator Orbit(float seconds)
        {
            if (Time.time - _rounded > 0.2f) // not still going round from its breath: back onto its ring first
            {
                _a = Mathf.Atan2(Pos.z - Center.z, Pos.x - Center.x);
                yield return Glide(OrbitPoint(_a), 16f);
                _bob = 0f;
            }
            yield return Round(seconds);
        }
        private IEnumerator Round(float seconds)
        {
            for (float t = 0f; t < seconds && Alive; t += Time.deltaTime)
            {
                _a += 14f / Ring * Time.deltaTime * SlowFactor;
                _bob += Time.deltaTime;
                Put(OrbitPoint(_a) + Vector3.up * Mathf.Sin(_bob * 1.3f) * 1.2f, Facing(Tangent(_a)) * Quaternion.Euler(0f, 0f, 28f));
                _rounded = Time.time;
                yield return null;
            }
        }
        private static Vector3 Tangent(float a) => new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));

        private IEnumerator Torpedo()
        {
            Vector3 target = TowerPoint(32f), run = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * (target - Pos).Flat().normalized;
            Vector3 a = target - run * 20f, b = target + run * 20f;
            a.y = GroundY(a); b.y = GroundY(b);
            Vector3 s0 = a - run * 10f;
            s0.y = GroundY(s0) + Foot + 14f; // where its run sets off
            yield return Glide(s0, 24f);
            Shout("skyfishDive");
            // It pulls up, nose high, hangs there coiled, and tips over into its dive as the warning runs out (25° nose down,
            // the way its run sets off).
            Quaternion r0 = T.rotation, r1 = Facing(run);
            yield return Warn("lane", a, b, 4.5f, 1.4f, breakable: true, during: k =>
            {
                float x = Windup(k, 1.4f, 0.2f);
                Put(s0 + Vector3.up * 1.2f * Mathf.Max(0f, x), Quaternion.Slerp(r0, r1, Ease(Mathf.Min(1f, k * 4f))) * Quaternion.Euler(x > 0f ? -14f * x : -25f * x / Over, 0f, 0f));
            });
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
            Vector3 aim = TowerPoint(28f);
            Vector3 a = aim - dir * 30f, b = aim + dir * 30f;
            a.y = GroundY(a); b.y = GroundY(b);
            Vector3 s0 = a - dir * 8f;
            s0.y = GroundY(s0) + Foot + 7f; // where its pass sets off
            Vector3 high = s0 - dir * 12f + Vector3.up * 15f, top = high + Vector3.up * 1.5f, m1 = dir * 50f * 0.6f;
            yield return Glide(high, 26f);
            Shout("skyfishBoom");
            // It climbs a little, nose up, coiled; then in the last 0.6 s it tips over and stoops, swooping out of the dive
            // onto the start of its pass at the pass's own speed (a Hermite curve into 50 m/s along it).
            Quaternion r0 = T.rotation;
            yield return Warn("lane", a, b, 7f, 1.8f, breakable: true, during: k =>
            {
                const float stoop = 0.6f / 1.8f;
                if (k < 1f - stoop)
                {
                    float x = Windup(k / (1f - stoop), 1.2f, 0f);
                    Put(high + Vector3.up * 1.5f * x, Quaternion.Slerp(r0, Facing(dir), Ease(Mathf.Min(1f, k * 4f))) * Quaternion.Euler(-15f * x, 0f, 0f));
                    return;
                }
                float v = (k - (1f - stoop)) / stoop, v2 = v * v, v3 = v2 * v;
                Vector3 p = (2f * v3 - 3f * v2 + 1f) * top + (3f * v2 - 2f * v3) * s0 + (v3 - v2) * m1;
                Vector3 d = 6f * (v - v2) * (s0 - top) + (3f * v2 - 2f * v) * m1;
                Quaternion along = d.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(d) * BossLooks.Turn(Kind) : Facing(dir);
                Put(p, Quaternion.Slerp(Facing(dir) * Quaternion.Euler(-15f, 0f, 0f), along, Ease(Mathf.Min(1f, v * 3f))));
            });
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

        // Sonic Boom x2 (second form): it stoops over one spot and breaks the sound barrier twice: a disc first, then 0.8 s
        // later the ring round it. Wherever you stand one of them is coming, so it makes you move: out, then back in.
        private IEnumerator DoubleBoom()
        {
            Vector3 c = TowerPoint(30f);
            c.y = GroundY(c);
            float r = 6f + Tier * 0.1f;
            yield return Glide(c + Vector3.up * (Foot + 10f), 24f);
            Shout("skyfishBoom2");
            WarnOnly("ring", c, c, r * 2f, 2.4f, Mathf.RoundToInt(r * 100f)); // its fill-in runs 0.8 s behind the disc's
            var stoop = Lean(c + Forward, 25f, rise: 1f); // nose up, climbing, then pitched over at the spot as the air breaks
            yield return Warn("circle", c, c, r, 1.6f, during: Coil(stoop, 1.6f));
            stoop(-Over, 1f);
            Hit(Circle(c, r), 90f, 26f, 40f, c, 12f);
            Send("sky.ring", c, c, 0, Mathf.RoundToInt(r * 100f));
            yield return Recoil(stoop, 0.8f);
            var outer = Circle(c, r * 2f);
            Hit(p => outer(p) && (p - c).Flat().sqrMagnitude > r * r, 90f, 26f, 40f, c, 12f);
            Send("sky.ring", c, c, Mathf.RoundToInt(r * 100f), Mathf.RoundToInt(r * 200f));
            AutoTest.Count("skyfish_boom2");
            yield return Glide(Pos + Vector3.up * 6f, 14f);
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

        // Full salvo: a victory roll, climbing and rolling right round, and the air torn open at the top (1.6 s).
        protected override IEnumerator SecondForm()
        {
            Shout("skyfishPhase2");
            Send("phase2", Pos, Pos);
            Vector3 from = Pos, to = Pos + Vector3.up * 8f;
            Quaternion r = T.rotation;
            for (float t = 0f; t < 1.6f && Alive; t += Time.deltaTime)
            {
                float k = t / 1.6f;
                Put(Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, k)), r * Quaternion.Euler(-30f * Mathf.Sin(k * Mathf.PI), 0f, 360f * Mathf.SmoothStep(0f, 1f, k)));
                yield return null;
            }
            Put(to, r);
            Send("sky.pass", to, to);
        }
    }

    // A flying fish of the squadron: wheels round a moment, picks a turret, dives on it and blows up.
    internal class Kamikaze : BossBrain
    {
        protected override IEnumerator Act()
        {
            float a = Random.value * 6.28f;
            Vector3 c = Pos - new Vector3(Mathf.Cos(a) * 3f, 0f, Mathf.Sin(a) * 3f); // wheeling round from where it is
            for (float t = 0f, end = Random.Range(1.2f, 2.4f); t < end && Alive; t += Time.deltaTime)
            {
                a += 2.4f * Time.deltaTime;
                Put(c + new Vector3(Mathf.Cos(a) * 3f, Mathf.Sin(t * 3f) * 0.3f, Mathf.Sin(a) * 3f), Facing(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a))));
                yield return null;
            }
            var turret = Best(p => Catalog.IsTurret(p.Type), p => -(p.Pos - Pos).Flat().magnitude + Random.value * 12f);
            Vector3 at = turret.HasValue ? turret.Value.Pos : Core;
            yield return Warn("circle", at, at, 2.2f, 1.0f, during: Coil(Lean(at, 20f, back: 1f, rise: 0.5f), 1.0f)); // turned on it, drawn back
            yield return Glide(at + Vector3.up * 0.6f, 24f, dive: true);
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
            switch (Pick(4f, 3f, 3f, Phase2 ? 2.5f : 0f, Phase2 ? 2.5f : 0f))
            {
                case 0: yield return Sweep(); break;
                case 1: yield return Grab(Phase2 ? 2 : 1); break;
                case 2: yield return Ink(); break;
                case 3: yield return Whirl(); break;
                default: yield return TwoArm(); break;
            }
            yield return Prowl(Breath);
        }
        protected override IEnumerator NewMove() => TwoArm();

        // Between moves it sways in the surf: out to one side and back across, bobbing, still facing the island.
        private float _sway = 1f;
        protected override IEnumerator Prowl(float seconds)
        {
            Vector3 p0 = Pos, side = Vector3.Cross(Vector3.up, (Center - p0).Flat().normalized) * ((_sway = -_sway) * 1.6f * Mathf.Min(1f, seconds / 2.5f));
            for (float t = 0f; t < seconds && Alive; t += Time.deltaTime)
            {
                float k = t / seconds, env = Mathf.Sin(Mathf.PI * k);
                Vector3 p = p0 + side * (Mathf.Sin(2f * Mathf.PI * k) * env) + Vector3.up * (0.35f * env * Mathf.Sin(t * 2.6f));
                Put(p, Quaternion.Slerp(T.rotation, Facing(Center - p), 1f - Mathf.Exp(-5f * Time.deltaTime)));
                yield return null;
            }
            if (Alive) Put(p0, Facing(Center - p0));
        }

        private IEnumerator Move(int gap)
        {
            Vector3 from = Pos, down = Pos + Vector3.down * (Height + 2f);
            Send("kraken.dive", from, from);
            for (float t = 0f; t < 0.8f && Alive; t += Time.deltaTime) { float k = t / 0.8f; Put(Vector3.Lerp(from, down, k * k)); yield return null; }
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
            Vector3 dir = (Center - Pos).Flat().normalized, s = Shore;
            float range = 21f, half = Phase2 ? 50f : 38f;
            Shout("krakenSweep");
            Send("kraken.tentacles", s, s + dir * range, 1400, Phase2 ? 5 : 3);
            var rear = Lean(Center, 14f, back: 0.8f, rise: 1.2f); // reared up and back out of the surf, then thrown at the beach
            yield return Warn("cone", s, s + dir * range, 0f, 1.4f, breakable: true, extra: Mathf.RoundToInt(half), during: Coil(rear, 1.4f));
            rear(-Over, 1f);
            Hit(Cone(s, dir, range, half), 95f, 30f, 45f, s, 12f);
            Send("kraken.sweep", s, s + dir * range, 0, Mathf.RoundToInt(half));
            AutoTest.Count("kraken_sweep");
            yield return Recoil(rear, 0.6f);
        }

        private IEnumerator Grab(int n)
        {
            var turrets = Pieces.Where(p => Catalog.IsTurret(p.Type) && (p.Pos - Pos).Flat().magnitude < 26f).OrderBy(p => (p.Pos - Pos).sqrMagnitude).Take(n).ToList();
            if (turrets.Count == 0) { yield return Sweep(); yield break; }
            Shout("krakenGrab", true);
            Vector3 s = Shore;
            foreach (var t in turrets) Send("kraken.reach", t.Pos, s, 2000, t.Id);
            foreach (var t in turrets.Skip(1)) WarnOnly("circle", t.Pos, t.Pos, 2.2f, 2f);
            var haul = Lean(turrets[0].Pos, 10f, back: 0.5f, rise: 1f); // leaning back on its arm as it takes hold, then a heave
            yield return Warn("circle", turrets[0].Pos, turrets[0].Pos, 2.2f, 2.0f, breakable: true, breakShare: 0.05f, during: Coil(haul, 2.0f));
            haul(-Over, 1f);
            var ids = new HashSet<int>(Pieces.Select(p => p.Id));
            foreach (var t in turrets)
            {
                if (!ids.Contains(t.Id)) continue; // gone already
                Send("kraken.grab", t.Pos, s, 0, t.Id); // (before it goes: every screen takes a copy of it to drag under)
                BuildSystem.Instance.ServerTakePiece(t.Id);
                AutoTest.Count("kraken_grab");
            }
            yield return Recoil(haul, 0.8f);
        }

        private IEnumerator Ink()
        {
            Vector3 at = Pos + (Center - Pos).Flat().normalized * 14f;
            at.y = GroundY(at);
            float r = 7f + Tier * 0.1f;
            Vector3 s = Shore;
            yield return Warn("circle", at, at, r, 1.0f, during: Coil(Lean(at, 10f, back: 0.5f, rise: 0.6f), 1.0f)); // a squirt
            Send("kraken.ink", at, s, 6000, Mathf.RoundToInt(r * 100f));
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
                    if (d.magnitude < 4f) Siege.HurtPlayer(p, Mathf.Min(80, Scaled(6f)), Vector3.up * 4f);
                    else Siege.HurtPlayer(p, 1, d.normalized * 7f + Vector3.up);
                }
                yield return 0.5f;
            }
            AutoTest.Count("kraken_whirl");
        }

        // Two-Arm Grab (second form): two tentacles at once on two targets, a turret or a player, and both squeeze together.
        // Unlike Grab nothing is dragged under: the squeeze hurts, and a player caught in it is hauled toward the sea.
        private int _grip; // players have no piece id: their coils are keyed below zero
        private IEnumerator TwoArm()
        {
            var picks = new List<(Vector3 at, int piece, int key)>();
            var near = Targets.Where(p => (p.Transform.position - Pos).Flat().magnitude < 26f).OrderBy(p => (p.Transform.position - Pos).sqrMagnitude).FirstOrDefault();
            if (near) { Vector3 at = near.Transform.position; at.y = GroundY(at); picks.Add((at, 0, -(++_grip))); }
            foreach (var t in Pieces.Where(p => Catalog.IsTurret(p.Type) && (p.Pos - Pos).Flat().magnitude < 26f).OrderBy(p => (p.Pos - Pos).sqrMagnitude).Take(2 - picks.Count).ToList())
                picks.Add((t.Pos, t.Id, t.Id));
            if (picks.Count == 0) { yield return Sweep(); yield break; }
            Shout("krakenTwoArm", true);
            Vector3 s = Shore;
            foreach (var g in picks) Send("kraken.reach", g.at, s, 2000, g.key);
            for (int i = 1; i < picks.Count; i++) WarnOnly("circle", picks[i].at, picks[i].at, 2.2f, 2f);
            var haul = Lean(picks[0].at, 10f, back: 0.5f, rise: 1f);
            yield return Warn("circle", picks[0].at, picks[0].at, 2.2f, 2.0f, breakable: true, breakShare: 0.05f, during: Coil(haul, 2.0f));
            haul(-Over, 1f);
            foreach (var g in picks)
            {
                Send("kraken.squeeze", g.at, s, 0, g.key);
                if (g.piece != 0 && Pieces.Any(p => p.Id == g.piece)) BuildSystem.Instance.ServerDamagePiece(g.piece, ScaledPiece(150f));
                HurtPlayers(Circle(g.at, 2.2f), Scaled(26f), g.at + (g.at - s).Flat().normalized * 3f, 12f); // thrown from the land side: toward the sea
                AutoTest.Count("kraken_twoarm");
            }
            yield return Recoil(haul, 0.8f);
        }

        // The blood tide: the sea boils red round it, four tentacles rear out of the surf behind it, and it heaves itself
        // higher out of the water (1.9 s, no harm).
        protected override IEnumerator SecondForm()
        {
            Shout("krakenPhase2");
            Send("phase2", Pos, Pos);
            Send("kraken.rise", Shore, Shore, 1700);
            Send("kraken.tentacles", Shore, Shore - (Center - Pos).Flat().normalized * 12f, 1300, 4); // lashing the sea, not the beach: a show
            Vector3 from = Pos, up = Pos + Vector3.up * 1.6f;
            for (float t = 0f; t < 1.9f && Alive; t += Time.deltaTime) { Put(Vector3.Lerp(from, up, Mathf.Sin(t / 1.9f * Mathf.PI))); yield return null; }
            Put(from);
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
            switch (Pick(copies < (Phase2 ? 4 : 2) ? 3f : 0f, 4f, Hp01 < 0.85f && _undos < 3 ? 2f : 0f, 3f, Phase2 ? 3f : 0f))
            {
                case 0: yield return Copy(Phase2 ? 4 : 2); break;
                case 1: yield return Delete(Phase2 ? 2 : 1); break;
                case 2: yield return Undo(); break;
                case 3: yield return Bolts(Phase2 ? 14 : 9); break;
                default: yield return Tiles(); break;
            }
            yield return Prowl(Breath);
        }
        protected override IEnumerator NewMove() => Tiles();
        protected override IEnumerator Prowl(float seconds) => Sidestep(seconds, Center, 1.2f, 3f, stutter: 0.09f); // in glitchy jumps

        private IEnumerator Copy(int n)
        {
            Shout("voxelCopy");
            Send("voxel.copy", Pos + Vector3.up * (Height + 1f), Pos, 900);
            yield return Posed(0.9f, Coil(Lean(Core, 6f, rise: 0.6f), 0.9f));
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
            var select = Lean(centres[0].c, 10f, rise: 0.8f);
            yield return Warn("circle", new Vector3(Pos.x, GroundY(Pos), Pos.z), Pos, 2f, 1.6f, breakable: true, during: Coil(select, 1.6f));
            select(-Over, 1f);
            foreach (var (c, to) in centres)
            {
                Vector3 f = (to - c).Flat().normalized;
                var inBox = Lane(c - f * half, c + f * half, half * 2f);
                foreach (var p in Pieces.Where(p => inBox(p.Pos)).ToList()) BuildSystem.Instance.ServerDamagePiece(p.Id, Mathf.Max(ScaledPiece(140f), Mathf.RoundToInt(p.MaxHp * 0.45f)));
                if (inBox(Core)) Siege.Instance.DamageCore(Scaled(60f));
                HurtPlayers(inBox, Mathf.Min(80, Scaled(25f)), c, 6f);
                Send("voxel.delete", c, to, 0, Mathf.RoundToInt(half * 100f));
            }
            AutoTest.Count("voxel_delete");
            yield return Recoil(select, 0.5f);
        }

        private IEnumerator Undo()
        {
            _undos++;
            Shout("voxelUndo", true);
            Send("voxel.undo", Pos, Pos, 3000);
            Vector3 g = new Vector3(Pos.x, GroundY(Pos), Pos.z);
            Quaternion r0 = T.rotation; // turning once round backwards: rewinding
            yield return Warn("ring", g, g, Reach + 3f, 3f, breakable: true, breakShare: 0.03f, extra: Mathf.RoundToInt((Reach + 1f) * 100f), during: k => Put(Pos, r0 * Quaternion.Euler(0f, -360f * Ease(k), 0f)));
            Put(Pos, r0);
            Body.ServerChangeHp(-Mathf.RoundToInt(MaxHp * 0.12f));
            Send("voxel.restored", Pos, Pos);
            AutoTest.Count("voxel_undo");
        }

        private IEnumerator Bolts(int n)
        {
            var spots = BossBrains.Spots(Pos, n, 24f);
            foreach (var s in spots) WarnOnly("circle", s, s, 1.3f, 0.8f);
            var aim = Lean(spots.Count > 0 ? spots.Aggregate(Vector3.zero, (sum, q) => sum + q) / spots.Count : Pos + Forward, 10f, back: 0.6f);
            yield return Posed(0.8f, Coil(aim, 0.8f));
            aim(-Over, 1f);
            foreach (var s in spots) Send("voxel.bolt", Pos + Forward * Reach * 0.7f, s);
            yield return Recoil(aim, 0.35f);
            foreach (var s in spots) Hit(Circle(s, 1.3f), 35f, 14f, 18f, s, 4f);
            AutoTest.Count("voxel_bolts");
        }

        // Glitch Tiles (second form): five tiles of corrupted ground flicker up over the fort; a turret standing on one loses
        // its lock for 1.5 s, its aim jumping from fish to fish at random (Bosses.Scramble). No damage: it's a nuisance.
        private IEnumerator Tiles()
        {
            const float half = 2.4f;
            var spots = BossBrains.Spots(Core, 5, 24f, 16f);
            if (spots.Count == 0) yield break;
            var turns = spots.Select(c => c + Quaternion.Euler(0f, Random.Range(0f, 90f), 0f) * Vector3.forward).ToList();
            for (int i = 1; i < spots.Count; i++) WarnOnly("box", spots[i], turns[i], half, 1.2f);
            Shout("voxelTiles");
            var cast = Lean(spots[0], 8f, rise: 0.6f);
            yield return Warn("box", spots[0], turns[0], half, 1.2f, during: Coil(cast, 1.2f));
            cast(-Over, 1f);
            int n = 0;
            for (int i = 0; i < spots.Count; i++)
            {
                Vector3 c = spots[i], f = (turns[i] - c).Flat().normalized;
                var inBox = Lane(c - f * half, c + f * half, half * 2f);
                Send("voxel.tile", c, turns[i], 1500, Mathf.RoundToInt(half * 100f));
                foreach (var p in Pieces.Where(p => Catalog.IsTurret(p.Type) && inBox(p.Pos)).ToList()) { Bosses.Instance.Scramble(p.Id, 1.5f); n++; }
            }
            AutoTest.Count("voxel_tiles");
            AutoTest.Count("voxel_scrambled", n);
            yield return Recoil(cast, 0.3f);
        }

        protected override IEnumerator SecondForm()
        {
            Shout("voxelPhase2");
            Send("voxel.fatal", Pos, Pos, 2500);
            // FATAL ERROR: it jumps about where it hangs, a frame here and a frame there, worse and worse, then snaps back.
            Vector3 p0 = Pos;
            Quaternion r0 = T.rotation;
            for (float t = 0f, next = 0f; t < 2.5f && Alive; t += Time.deltaTime)
            {
                if (t >= next)
                {
                    float m = Mathf.Lerp(0.15f, 0.7f, t / 2.5f);
                    Put(p0 + Random.insideUnitSphere * m, r0 * Quaternion.Euler(0f, Random.Range(-25f, 25f) * m, 0f));
                    next = t + Random.Range(0.06f, 0.14f);
                }
                yield return null;
            }
            if (Alive) Put(p0, r0);
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
                switch (Pick(4f, 3f, 3f, 3f))
                {
                    case 0: yield return Fire(); break;
                    case 1: yield return Thunder(8); break;
                    case 2: yield return GateLeap(); break;
                    default: yield return DragonBreath(); break;
                }
            yield return Prowl(Breath);
        }
        protected override IEnumerator NewMove() => DragonBreath();

        // Between moves it swims on: out of its move along a curve into its round, a little ahead (in as long as gliding
        // back took before, or 0.8 s at least), and round for its breath.
        protected override IEnumerator Prowl(float seconds)
        {
            float a = Mathf.Atan2(Pos.z - Center.z, Pos.x - Center.x);
            float glide = (SwimPoint(a, _t) - Pos).magnitude / 12f, fly = Mathf.Max(0.8f, glide);
            _a = a + 0.25f;
            _t += fly;
            Vector3 to = SwimPoint(_a, _t);
            yield return FlyOnto(to, fly, (SwimPoint(_a + 9f / 23f * 0.05f, _t + 0.05f) - to) / 0.05f);
            yield return Paddle(Mathf.Max(0f, seconds - (fly - glide)));
        }

        private float _paddled = -9f;
        private IEnumerator Swim(float seconds)
        {
            if (Time.time - _paddled > 0.2f) // not still swimming round from its breath: back into its round first
            {
                _a = Mathf.Atan2(Pos.z - Center.z, Pos.x - Center.x);
                yield return Glide(SwimPoint(_a, _t), 12f);
            }
            yield return Paddle(seconds);
        }
        private IEnumerator Paddle(float seconds)
        {
            Quaternion r0 = T.rotation;
            for (float t = 0f; t < seconds && Alive; t += Time.deltaTime)
            {
                _a += 9f / 23f * Time.deltaTime * SlowFactor;
                _t += Time.deltaTime;
                Vector3 p = SwimPoint(_a, _t), d = SwimPoint(_a + 0.05f, _t + 0.05f) - p;
                Put(p, Quaternion.Slerp(r0, Quaternion.LookRotation(d.normalized) * Quaternion.Euler(0f, 0f, Mathf.Sin(_t * 2f) * 12f) * BossLooks.Turn(Kind), Ease(Mathf.Min(1f, t / 0.3f))));
                _paddled = Time.time;
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
            Vector3 p0 = Pos;
            yield return Warn("cone", g, g + dir * len, 0f, 1.2f, breakable: true, extra: Mathf.RoundToInt(sweep), during: Coil(Lean(aim, 15f, back: 1f), 1.2f));
            Send("koi.jet", Mouth, g + dir * len, 1300, Mathf.RoundToInt(sweep));
            float next = 0f;
            for (float t = 0f; t < 1.3f && Alive; t += Time.deltaTime)
            {
                float x = Spring(-Over, t / 1.3f); // kicked back by its own jet, and turned along with it
                Put(p0 - dir * (1f * x), Facing(Quaternion.Euler(0f, Mathf.Lerp(-sweep, sweep, t / 1.3f), 0f) * dir) * Quaternion.Euler(-15f * x, 0f, 0f));
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
            Vector3 at = TowerPoint(30f);
            at.y = GroundY(at);
            float r = Phase2 ? 6.5f : 5.5f;
            yield return Glide(Vector3.Lerp(Pos, at, 0.4f) + Vector3.up * 18f, 16f);
            if (Phase2) Shout("koiDragonDive"); else Shout("koiLeap");
            // It rears up 2 m, turning head down onto the spot as it goes, hangs there coiled, and drops: already moving as
            // the warning runs out, and faster all the way down.
            Vector3 top = Pos;
            Quaternion r0 = T.rotation;
            yield return Warn("circle", at, at, r, 1.5f, breakable: true, during: k =>
            {
                Vector3 p = top + Vector3.up * (2f * Mathf.Max(0f, Windup(k, 1.5f)));
                Put(p, Quaternion.Slerp(r0, Quaternion.LookRotation((at - p).normalized) * BossLooks.Turn(Kind), Ease(Mathf.Min(1f, k * 3f))));
            });
            Vector3 from = Pos, to = at + Vector3.up * Foot * 0.4f;
            Quaternion down = Quaternion.LookRotation((to - from).normalized) * BossLooks.Turn(Kind);
            for (float t = 0f; t < 0.45f && Alive; t += Time.deltaTime) { float k = t / 0.45f; Put(Vector3.Lerp(from, to, 0.3f * k + 0.7f * k * k), down); yield return null; }
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
            yield return Posed(1.4f, Coil(Lean(Core, 12f, rise: 1f), 1.4f));
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
            var blow = Lean(aim, 18f, back: 1.2f);
            yield return Warn("cone", g, g + dir * range, 0f, 1.4f, breakable: true, extra: Mathf.RoundToInt(half), during: Coil(blow, 1.4f));
            blow(-Over, 1f);
            Send("koi.breath", Mouth, g + dir * range, 1200, Mathf.RoundToInt(half));
            var cone = Cone(g, dir, range, half);
            Hit(cone, 110f, 35f, 50f, g, 8f);
            foreach (var p in Pieces.Where(x => cone(x.Pos)).ToList()) { Hazard(p.Pos, 1.2f, 4f, Scaled(4f), Scaled(10f)); Send("koi.burn", p.Pos, p.Pos, 4000); }
            AutoTest.Count("koi_fire");
            yield return Recoil(blow, 0.5f);
        }

        private IEnumerator Thunder(int n)
        {
            var spots = BossBrains.Spots(Core, n, 24f, 18f);
            foreach (var s in spots) WarnOnly("circle", s, s, 2.6f, 1.5f);
            Shout("koiThunder");
            Send("koi.charge", Pos, Pos, 1500);
            var call = Lean(Core, 10f, rise: 1.5f); // rising, head thrown back, calling it down
            yield return Posed(1.5f, Coil(call, 1.5f));
            call(-Over, 1f);
            foreach (var s in spots) { Send("koi.thunder", s, s); Hit(Circle(s, 2.6f), 90f, 30f, 40f, s, 8f); }
            AutoTest.Count("koi_thunder");
            yield return Recoil(call, 0.4f);
        }

        // Dragon Breath (second form): dragonfire down one straight line for 1.2 s, its front running out from the mouth,
        // and fire left burning along it. Unlike Fire's wide cone it reaches far but narrow. Breakable.
        private IEnumerator DragonBreath()
        {
            Vector3 aim = Heart();
            yield return Turn(aim, 0.4f);
            Vector3 dir = (aim - Pos).Flat().normalized, g = new Vector3(Pos.x, GroundY(Pos), Pos.z);
            float len = Mathf.Clamp((aim - Pos).Flat().magnitude + 6f, 12f, 26f);
            Vector3 end = g + dir * len;
            end.y = GroundY(end);
            Shout("koiLine");
            var blow = Lean(aim, 16f, back: 1f);
            yield return Warn("lane", g, end, 3.4f, 1.3f, breakable: true, during: Coil(blow, 1.3f));
            Send("koi.flame", Mouth, end, 1200);
            var hit = new HashSet<int>();
            var hurt = new HashSet<Player>();
            float next = 0f;
            for (float t = 0f; t < 1.2f && Alive; t += Time.deltaTime)
            {
                blow(-Over * (1f + 0.15f * Mathf.Sin(t * 40f)), 1f); // held thrown at it, shaking with the blast
                if (t >= next) { next = t + 0.1f; HitOnce(hit, hurt, Lane(g, g + dir * Mathf.Max(1f, len * Mathf.Clamp01(t / 0.35f)), 3.4f), 115f, 35f, 50f, g, 8f); }
                yield return null;
            }
            for (float d = 2f; d <= len; d += 2.6f)
            {
                Vector3 p = g + dir * d;
                p.y = GroundY(p);
                Hazard(p, 1.3f, 4f, Scaled(4f), Scaled(8f));
                Send("koi.burn", p, p, 4000);
            }
            AutoTest.Count("koi_line");
            yield return Recoil(blow, 0.5f);
        }

        protected override IEnumerator SecondForm()
        {
            Shout("koiPhase2"); // through the gate on the way up...
            yield return Glide(Center + (Pos - Center).Flat().normalized * 16f + Vector3.up * (Sea + 20f - Center.y), 12f);
            Shout("koiDragon"); // ...a dragon at the top
            Send("koi.dragon", Pos, Pos, 2600);
            yield return Posed(2.6f, Coil(Lean(Core, 20f, rise: 1.5f), 2.6f, 0.3f)); // rearing back as it changes, then a lunge
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
            Vector3 to = Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 18f; // close enough for ordinary towers, not just snipers
            to.y = Sea + Alt;
            yield return Glide(to, 5f);
            yield return Turn(Core, 0.6f);
            if (Phase2 && !_judged && Hp01 <= 0.25f) { yield return Judgment(); yield break; }
            switch (Pick(4f, 3.5f, 3.5f, Phase2 ? 3f : 0f))
            {
                case 0: yield return Beam(); break;
                case 1: yield return Spears(Phase2 ? 3 : 2); break;
                case 2: yield return Echoes(Phase2 ? 3 : 2); break;
                default: yield return SecondPupil(); break;
            }
            yield return Prowl(Breath);
        }
        protected override IEnumerator NewMove() => SecondPupil();
        protected override IEnumerator Prowl(float seconds) => Sidestep(seconds, Core, 0.8f, 2.5f); // drifting round, its eye on the lighthouse

        private Vector3 Under => new Vector3(Pos.x, GroundY(Pos), Pos.z);

        private IEnumerator Beam()
        {
            Vector3 dir = (Core - Pos).Flat().normalized, g = Under;
            const float range = 36f, sweep = 32f;
            Shout("eyeGaze");
            Send("eye.open", Pos, Pos, 1600);
            Vector3 p0 = Pos;
            yield return Warn("cone", g, g + dir * range, 0f, 1.6f, breakable: true, breakShare: 0.035f, extra: Mathf.RoundToInt(sweep), during: Coil(Lean(Core, 10f, back: 1.2f), 1.6f));
            Send("eye.beam", Pos, g + dir * range, 2400, Mathf.RoundToInt(sweep));
            float next = 0f;
            for (float t = 0f; t < 2.4f && Alive; t += Time.deltaTime)
            {
                float x = Spring(-Over, t / 2.4f); // kicked back as it fires, its shell turning half the way its beam does
                Put(p0 - dir * (1.2f * x), Facing(Quaternion.Euler(0f, 0.5f * Mathf.Lerp(-sweep, sweep, t / 2.4f), 0f) * dir) * Quaternion.Euler(-10f * x, 0f, 0f));
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

        // Second Pupil (second form): a second pupil opens in the eye and both fire at once, two beams turning opposite ways
        // across the fort and crossing in the middle. Two seconds to break it before they light.
        private IEnumerator SecondPupil()
        {
            Vector3 dir = (Core - Pos).Flat().normalized, g = Under;
            const float range = 36f, sweep = 48f;
            Shout("eyePupil");
            Send("eye.pupil", Pos, Pos, 2000);
            var glare = Lean(Core, 10f, back: 1.2f);
            yield return Warn("cone", g, g + dir * range, 0f, 2f, breakable: true, breakShare: 0.035f, extra: Mathf.RoundToInt(sweep), during: Coil(glare, 2f));
            Send("eye.twin", Pos, g + dir * range, 2800, Mathf.RoundToInt(sweep));
            float next = 0f;
            for (float t = 0f; t < 2.8f && Alive; t += Time.deltaTime)
            {
                glare(Spring(-Over, t / 2.8f), 1f);
                if (t >= next)
                {
                    next = t + 0.15f;
                    float k = t / 2.8f;
                    var a = Lane(g, g + Quaternion.Euler(0f, Mathf.Lerp(-sweep, sweep, k), 0f) * dir * range, 2.4f);
                    var b = Lane(g, g + Quaternion.Euler(0f, Mathf.Lerp(sweep, -sweep, k), 0f) * dir * range, 2.4f);
                    Hit(p => a(p) || b(p), 26f, 12f, 15f, g, 4f);
                }
                yield return null;
            }
            AutoTest.Count("eye_pupil");
        }

        private IEnumerator Spears(int batches)
        {
            Shout("eyeSpears");
            for (int b = 0; b < batches && Alive; b++)
            {
                var spots = BossBrains.Spots(Core, 33, 30f, 26f);
                foreach (var s in spots) WarnOnly("circle", s, s, 1.5f, 1.6f);
                Send("eye.spears", Pos, Pos, 1600);
                var lift = Lean(Core, 6f, rise: 1.5f); // drawn up, then it drops a little as they fall
                yield return Posed(1.6f, Coil(lift, 1.6f));
                lift(-Over, 1f);
                foreach (var s in spots) { Send("eye.spear", s, s); Hit(Circle(s, 1.5f), 50f, 18f, 25f, s, 5f); }
                AutoTest.Count("eye_spears");
                yield return Recoil(lift, 0.5f);
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
            var call = Lean(Core, 8f, back: 1f);
            yield return Posed(1.6f, Coil(call, 1.6f));
            call(-Over, 1f);
            foreach (var h in hits) h();
            AutoTest.Count("eye_echoes");
            yield return Recoil(call, 0.6f);
        }

        // Its one Judgment covers the whole island (BOSS-FORMS §2.10): the ring round the lighthouse is struck as before,
        // everywhere else more lightly. One warning for each, both on the same eight seconds and the same break.
        private const float Field = 30f;
        private IEnumerator Judgment()
        {
            _judged = true;
            Shout("eyeJudgment", true);
            yield return Glide(Core + Vector3.up * (Alt + 4f), 10f);
            Send("eye.judgment", Pos, Core, 8000, Mathf.RoundToInt(Field * 100f));
            WarnOnly("circle", Center, Center, Field, 8f);
            var verdict = Lean(Core, 0f, rise: 3f); // rising slowly all eight seconds, shaking, then down on it
            yield return Warn("circle", Core, Core, 12f, 8f, breakable: true, breakShare: 0.06f, during: Coil(verdict, 8f, 0.25f));
            verdict(-Over, 1f);
            Siege.Instance.DamageCore(Mathf.RoundToInt(Siege.Instance.CoreMax * 0.3f));
            var ring = Circle(Core, 12f);
            var field = Circle(Center, Field);
            Hit(ring, 200f, 45f, 0f, Core, 16f);
            Hit(p => field(p) && !ring(p), 60f, 20f, 0f, Core, 10f);
            Send("eye.verdict", Core, Core, 0, Mathf.RoundToInt(Field * 100f));
            AutoTest.Count("eye_judgment");
            yield return Recoil(verdict, 1f);
        }

        protected override IEnumerator Fall()
        {
            Vector3 from = Pos, to = Pos - Vector3.up * 6f;
            for (float t = 0f; t < 0.5f && Body; t += Time.deltaTime) { float k = t / 0.5f; Put(Vector3.Lerp(from, to, k * k)); yield return null; }
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
