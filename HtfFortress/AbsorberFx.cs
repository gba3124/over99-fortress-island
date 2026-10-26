using System.Collections;
using UnityEngine;

namespace HtfFortress
{
    // What the absorber's moves look like (Absorber.cs sends them as FxKind.Absorber, Key the move), on every screen: each
    // trophy's move drawn from its boss's own pieces at a player's size, without what the boss does to the players who see
    // it (no blinding flash, no ink, no heavy shaking).
    internal static partial class BossMoves
    {
        private static readonly Color[] Rainbow = { new Color(1f, 0.25f, 0.2f), new Color(1f, 0.75f, 0.2f), new Color(0.35f, 1f, 0.4f), new Color(0.3f, 0.6f, 1f), new Color(0.75f, 0.4f, 1f) };

        public static bool PlayAbsorber(BossFx host, FxEvent e)
        {
            H = host;
            Vector3 a = e.From, b = e.To;
            switch (e.Key)
            {
                case "suck": SuckIn(a, b); break;
                case "shot": Run(ItemShot(a, b, (byte)e.PieceId)); break;
                case "warden":
                    Run(Streak(a - Vector3.up * 0.6f, b - Vector3.up * 0.6f, 0.1f, Iron, 0.18f));
                    BossVfx.Impact(b, 1.6f, Fire, Sand, false);
                    BossFx.Burst(b + Vector3.up * 0.4f, "Orange", 28, 0.12f);
                    Sound("SpiderCrabSnip_V", 1, 3, b, 1f); Sound("MetallicItemImpact_V", 1, 3, b, 0.9f);
                    Shake(b, 0.2f);
                    break;
                case "urchin": Needles(a, b, Mathf.Max(1, e.PieceId)); break;
                case "angler": Run(Lantern(a, b)); break;
                case "steed":
                    BossVfx.Impact(a, 2.4f, Gold, Sand);
                    BossVfx.Shock(a, 5.5f, Gold, 0.55f, 0.85f);
                    BossVfx.Spires(Circle(a, 3.6f, 10), 1.3f, Sand * 0.9f, new Color(0f, 0f, 0f, 0f), 0.6f, 0f);
                    BossFx.Burst(a + Vector3.up, "Gold", 30, 0.14f);
                    Sound("ItemImpactHeavy_V", 1, 3, a, 1f); Sound("BoatLandCollisionMedium_V", 1, 3, a, 0.7f);
                    Shake(a, 0.35f);
                    break;
                case "stone":
                    BossVfx.Spires(Line(a, b, 1.2f, 0.4f), 2.2f, Basalt, Venom, 1.4f);
                    BossVfx.Shock(a, 3f, Venom, 0.4f);
                    Fx.Sound("LavaEruption_01", a, 0.7f, AudioDistance.Long);
                    break;
                case "sky":
                    Run(Streak(a, b, 0.3f, Color.white, 0.7f));
                    // Flying out along the gust as it grows (left at the muzzle it swelled to 5 m round the shooter's own camera).
                    WeaponFx.Emit(WeaponFx.Rings, a + (b - a).normalized * 0.6f, 1, (b - a).normalized, color: Color.white, size: 5f, velocity: (b - a) / 0.5f, life: 0.5f);
                    Fx.Sound("PunchSwoosh", a, 1f, AudioDistance.Long); Sound("Seagull_V", 1, 11, a, 0.8f);
                    break;
                case "bomb": Explode(a, 2.6f); break;
                case "kraken": Run(Grasp(b, 3f)); break;
                case "voxel": Run(Selection(a, b, e.PieceId / 100f, 0.8f)); Fx.Sound("TypingClick" + Random.Range(1, 4), a, 1f, AudioDistance.Long); break;
                case "voxelDelete": Run(Pixelate(a, b, e.PieceId / 100f)); BossVfx.Flash(a + Vector3.up, 3f, Pixel, 0.2f); Fx.Sound("Error", a, 0.9f, AudioDistance.Long); break;
                case "koi": Run(Breath(a, b, e.Arg / 1000f, 25)); Fx.Sound("Ignite", a, 0.7f, AudioDistance.Long); break;
                case "eye": Run(Gaze(a, b)); break;
                case "spear":
                    Run(Fly(a + Vector3.up * 30f + Random.insideUnitSphere.Flat() * 3f, a, BossVfx.Shard, BossVfx.Glow(Blood, 4f), new Vector3(0.6f, 2f, 0.6f), 0.55f, 0f, Blood,
                            () => { BossVfx.Impact(a, 1.4f, Blood, Basalt); Sound("ItemImpactHeavy_V", 1, 3, a, 0.7f); }, upright: true));
                    break;
                default: return false;
            }
            return true;
        }

        // Sucking in: motes drawn from the cone ahead into the muzzle.
        private static void SuckIn(Vector3 muzzle, Vector3 far)
        {
            Vector3 d = far - muzzle;
            float reach = d.magnitude;
            if (reach < 0.1f) return;
            d /= reach;
            Vector3 side = Vector3.Cross(Vector3.up, d).normalized;
            var ps = BossVfx.Sparks(new Color(0.85f, 0.95f, 1f));
            for (int i = 0; i < 12; i++)
            {
                Vector3 p = muzzle + Quaternion.AngleAxis(Random.Range(-28f, 28f), Vector3.up) * Quaternion.AngleAxis(Random.Range(-18f, 18f), side) * d * Random.Range(1.2f, reach);
                WeaponFx.Emit(ps, p, 1, muzzle - p, size: 0.07f, velocity: (muzzle - p) / 0.3f, life: 0.3f);
            }
        }

        // A copy of the item flying out, tumbling, and bursting where it hits.
        private static IEnumerator ItemShot(Vector3 a, Vector3 b, byte id)
        {
            var look = Weapons.LookOf(id, 0.55f);
            float secs = Mathf.Max(0.08f, (b - a).magnitude / 35f);
            Fx.Sound("PunchSwoosh", a, 0.8f);
            for (float t = 0f; t < secs && look; t += Time.deltaTime)
            {
                look.position = Vector3.Lerp(a, b, t / secs);
                look.rotation = Quaternion.Euler(t * 900f, t * 600f, 0f);
                yield return null;
            }
            if (look) Object.Destroy(look.gameObject);
            BossFx.Burst(b, "Pink", 14, 0.08f);
            WeaponFx.Emit(WeaponFx.Dust, b, 4, Vector3.up);
            Sound("ItemImpactHeavy_V", 1, 3, b, 0.6f);
        }

        // Every needle at once: `n` of them shooting out all round from `at`, the first toward `ahead`.
        private static void Needles(Vector3 at, Vector3 ahead, int n)
        {
            Vector3 fwd = (ahead - at).Flat().normalized;
            BossVfx.Flash(at, 2.4f, Pink, 0.2f);
            BossVfx.Shock(at - Vector3.up, 4f, Pink, 0.35f);
            Fx.Sound("Pufferfish", at, 1f, AudioDistance.Long);
            for (int i = 0; i < n; i++)
            {
                Vector3 end = at + Quaternion.AngleAxis(360f * i / n, Vector3.up) * fwd * 14f;
                Run(Fly(at, end, BossVfx.Spike, BossVfx.Glow(Pink, 3f), new Vector3(0.45f, 0.45f, 0.9f), 0.3f, 0.3f, Pink, () => BossVfx.Flash(end, 0.6f, Pink, 0.15f)));
            }
        }

        // A lantern thrown out; it swells while motes are drawn in to it, then bursts (0.8 s in all, as the host times it).
        private static IEnumerator Lantern(Vector3 from, Vector3 at)
        {
            Vector3 top = at + Vector3.up * 1.6f;
            var mat = BossVfx.Glow(Cyan, 5f);
            yield return H.StartCoroutine(Fly(from, top, WeaponFx.Ball, mat, Vector3.one * 0.45f, 0.35f, 1.5f, Cyan, null));
            var lamp = WeaponFx.Piece(WeaponFx.Ball, mat);
            lamp.position = top;
            Fx.Sound("RadarPing", at, 1f, AudioDistance.Long);
            var motes = BossVfx.Sparks(Cyan);
            for (float t = 0f; t < 0.45f && lamp; t += Time.deltaTime)
            {
                lamp.localScale = Vector3.one * (0.45f + t * 1.2f);
                Vector3 p = at + Random.onUnitSphere * 7f;
                p.y = Mathf.Max(p.y, at.y + 0.3f);
                WeaponFx.Emit(motes, p, 1, top - p, size: 0.12f, velocity: (top - p) * 2.5f, life: 0.4f);
                yield return null;
            }
            if (lamp) Object.Destroy(lamp.gameObject);
            BossVfx.Flash(top, 4f, Cyan, 0.35f);
            for (int i = 0; i < 3; i++) WeaponFx.Emit(WeaponFx.Rings, top, 1, Random.onUnitSphere, color: Cyan, size: 5f + 3f * i);
            BossFx.Burst(top, "Cyan", 40, 0.14f);
            Fx.Sound("ShinyCreatureCaught_0" + Random.Range(1, 4), at, 1f, AudioDistance.Long);
        }

        // Three tentacles up out of the ground round `at`, coming down on it and holding there for `seconds`, then sinking.
        private static IEnumerator Grasp(Vector3 at, float seconds)
        {
            var arms = new System.Collections.Generic.List<(Tentacle t, Vector3 root)>();
            for (int i = 0; i < 3; i++)
            {
                Vector3 root = at + Quaternion.Euler(0f, i * 120f + Random.Range(-15f, 15f), 0f) * Vector3.forward * 3.4f;
                root.y = BossFx.Ground(root) - 0.2f;
                arms.Add((new Tentacle(3.6f), root));
                BossVfx.Impact(root, 1.2f, Crimson, Sand, false);
            }
            BossVfx.Shock(at, 4.5f, Crimson, 0.45f);
            Sound("ItemHitWaterHeavy_V", 1, 3, at, 0.9f);
            for (float t = 0f; t < seconds + 0.5f; t += Time.deltaTime)
            {
                float rise = t < seconds ? Mathf.Clamp01(t / 0.35f) : 1f - Mathf.Clamp01((t - seconds) / 0.5f);
                float lash = Mathf.Clamp01((t - 0.3f) / 0.2f);
                foreach (var (arm, root) in arms) arm.Pose(root, at, rise, lash, t);
                yield return null;
            }
            foreach (var (arm, _) in arms) arm.Destroy();
        }

        // The end's gaze: a white beam with every colour running down it.
        private static IEnumerator Gaze(Vector3 a, Vector3 b)
        {
            H.StartCoroutine(Beam(a, b, Color.white, 0.45f, 0.7f));
            Fx.Sound("Ignite", a, 1f, AudioDistance.Long);
            Fx.Sound("LavaWhaleNoise_0" + Random.Range(1, 5), b, 0.6f, AudioDistance.Long);
            float len = (b - a).magnitude;
            for (int k = 0; k < 6; k++)
            {
                for (float s = 0f; s < len; s += 2.5f)
                    BossVfx.Burst(Vector3.Lerp(a, b, s / len) + Random.insideUnitSphere * 0.3f, Rainbow[(k + (int)(s / 2.5f)) % Rainbow.Length], 2, 0.08f);
                yield return new WaitForSeconds(0.1f);
            }
            BossVfx.Impact(b, 1.8f, Color.white, Basalt);
        }
    }
}
