using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HtfFortress
{
    // The pieces the bosses' effects are made of, in the game's own look (faceted low-poly shapes, flat colours, glow that
    // the bloom picks up): jagged rock, crystal shards, spikes, rings of shock that roll out over the ground, flashes of
    // light, flying chunks, marks left on the ground, tubes that bend (tentacles). Every impact is layered from these: a
    // flash, a ring, debris, dust, a mark that fades.
    public static class BossVfx
    {
        // ---------- shapes ----------

        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();

        // Flat-shaded mesh from triangles (each face its own vertices, so each facet catches the light on its own).
        private static Mesh Faceted(string name, List<Vector3> tris)
        {
            var m = new Mesh { name = "BossVfx" + name };
            var idx = new int[tris.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            m.SetVertices(tris);
            m.SetTriangles(idx, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // A jagged spire of rock: `sides` round, three kinks up to a crooked point; 1 tall, about 1 across at the foot.
        public static Mesh Rock(int variant)
        {
            string key = "Rock" + variant;
            if (Meshes.TryGetValue(key, out var mesh)) return mesh;
            var rng = new System.Random(17 + variant * 31);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int sides = 5 + variant % 3;
            var rings = new List<Vector3[]>();
            float[] heights = { 0f, 0.35f, 0.68f };
            float[] radii = { 0.5f, 0.36f, 0.2f };
            Vector2 lean = new Vector2(R(-0.12f, 0.12f), R(-0.12f, 0.12f));
            for (int r = 0; r < heights.Length; r++)
            {
                var ring = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float a = (i + R(-0.2f, 0.2f)) * Mathf.PI * 2f / sides;
                    float rad = radii[r] * R(0.75f, 1.15f);
                    ring[i] = new Vector3(Mathf.Cos(a) * rad + lean.x * heights[r], heights[r] + R(-0.04f, 0.04f), Mathf.Sin(a) * rad + lean.y * heights[r]);
                }
                rings.Add(ring);
            }
            Vector3 tip = new Vector3(lean.x * 1.1f, 1f, lean.y * 1.1f);
            var tris = new List<Vector3>();
            for (int r = 0; r + 1 < rings.Count; r++)
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    tris.AddRange(new[] { rings[r][i], rings[r + 1][i], rings[r][j], rings[r][j], rings[r + 1][i], rings[r + 1][j] });
                }
            var top = rings[rings.Count - 1];
            for (int i = 0; i < sides; i++) tris.AddRange(new[] { top[i], tip, top[(i + 1) % sides] });
            return Meshes[key] = Faceted(key, tris);
        }

        // A crystal: a long six-sided double point, 1 tall, 0.4 across.
        public static Mesh Shard
        {
            get
            {
                if (Meshes.TryGetValue("Shard", out var mesh)) return mesh;
                var tris = new List<Vector3>();
                Vector3 bottom = Vector3.zero, top = Vector3.up;
                var mid = new Vector3[6];
                for (int i = 0; i < 6; i++) { float a = i * Mathf.PI / 3f; mid[i] = new Vector3(Mathf.Cos(a) * 0.2f, 0.28f, Mathf.Sin(a) * 0.2f); }
                for (int i = 0; i < 6; i++)
                {
                    int j = (i + 1) % 6;
                    tris.AddRange(new[] { mid[i], top, mid[j], mid[j], bottom, mid[i] });
                }
                return Meshes["Shard"] = Faceted("Shard", tris);
            }
        }

        // A thin spike along +z, 1 long, its base at the origin.
        public static Mesh Spike
        {
            get
            {
                if (Meshes.TryGetValue("Spike", out var mesh)) return mesh;
                var tris = new List<Vector3>();
                var b = new Vector3[5];
                for (int i = 0; i < 5; i++) { float a = i * Mathf.PI * 2f / 5f; b[i] = new Vector3(Mathf.Cos(a) * 0.08f, Mathf.Sin(a) * 0.08f, 0f); }
                for (int i = 0; i < 5; i++) { int j = (i + 1) % 5; tris.AddRange(new[] { b[i], b[j], Vector3.forward, b[j], b[i], Vector3.zero }); }
                return Meshes["Spike"] = Faceted("Spike", tris);
            }
        }

        // A chain link: a fat ring 1 across (lying in x/y), faceted.
        public static Mesh Link
        {
            get
            {
                if (Meshes.TryGetValue("Link", out var mesh)) return mesh;
                const int n = 10, m = 5;
                var tris = new List<Vector3>();
                Vector3 P(int i, int j)
                {
                    float a = i * Mathf.PI * 2f / n, b = j * Mathf.PI * 2f / m;
                    var c = new Vector3(Mathf.Cos(a) * 0.4f, Mathf.Sin(a) * 0.55f, 0f);
                    var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    return c + (d * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b)) * 0.12f;
                }
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < m; j++)
                        tris.AddRange(new[] { P(i, j), P(i + 1, j), P(i, j + 1), P(i, j + 1), P(i + 1, j), P(i + 1, j + 1) });
                return Meshes["Link"] = Faceted("Link", tris);
            }
        }

        // A flat ring (radius 1 at its outer edge, `inner` within), bright at the outer edge, see-through at the inner.
        public static Mesh Ring(float inner)
        {
            string key = "Ring" + Mathf.RoundToInt(inner * 100f);
            if (Meshes.TryGetValue(key, out var mesh)) return mesh;
            const int n = 48;
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(d * inner); c.Add(new Color(1f, 1f, 1f, 0f));
                v.Add(d); c.Add(Color.white);
                if (i < n) { int k = i * 2; t.AddRange(new[] { k, k + 1, k + 2, k + 2, k + 1, k + 3 }); }
            }
            mesh = new Mesh { name = "BossVfx" + key };
            mesh.SetVertices(v); mesh.SetColors(c); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return Meshes[key] = mesh;
        }

        // ---------- materials ----------

        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        // Lit, in the game's own shader (stone, flesh, iron): shared per colour.
        public static Material Solid(Color c) { string k = "Solid" + ColorUtility.ToHtmlStringRGB(c); if (!Mats.TryGetValue(k, out var m) || !m) Mats[k] = m = Models.Make(c); return m; }
        // Glowing in its own colour. Its light is all its own: the surface under it is near black, so the sun and the lights
        // round it hardly add to it (a lit colour plus a glow went white under the bloom); `glow` 1..8 runs from a soft
        // glow to just over the bloom's threshold, where the colour still shows through the halo.
        public static Material Glow(Color c, float glow = 4f)
        {
            string k = "Glow" + ColorUtility.ToHtmlStringRGB(c) + glow;
            if (Mats.TryGetValue(k, out var m) && m) return m;
            m = Object.Instantiate(WeaponFx.Emissive("BossVfxBase", Color.white, 1f));
            m.name = "Fortress" + k;
            // The game's particle material adds the vertex colour (white on our meshes and lines) to its base colour, which
            // made every glow white. Its add is weighted by _BaseColorAddSubDiff.x: none. (Switching it to multiply instead
            // picks a shader variant the game's build doesn't have, without the glow: everything came out black.)
            if (m.HasProperty("_BaseColorAddSubDiff")) m.SetVector("_BaseColorAddSubDiff", Vector4.zero);
            float e = Mathf.Lerp(0.6f, 1.3f, Mathf.InverseLerp(1f, 8f, glow));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c * 0.1f);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * e);
            return Mats[k] = m;
        }
        // See-through, unlit, coloured per vertex (rings, washes): one per use, since its alpha is animated.
        public static Material Wash(Color c) => new Material(Shader.Find("Sprites/Default")) { color = c, renderQueue = 3015 };

        // Glowing chips in one colour (their glow kept under the bloom's white, unlike the weapons' sparks): one system per colour.
        public static ParticleSystem Sparks(Color c) => WeaponFx.System("BossSparks" + ColorUtility.ToHtmlStringRGB(c), () => new WeaponFx.Spec
        {
            Mat = Glow(c, 3f), Mesh = WeaponFx.Cube, Life = new Vector2(0.25f, 0.55f), Speed = new Vector2(3f, 8f), Size = new Vector2(0.06f, 0.12f), Gravity = 1.2f, Drag = 1.5f,
            SizeOverLife = WeaponFx.Curve(0f, 1f, 0.6f, 0.8f, 1f, 0f), Shape = ParticleSystemShapeType.Cone, Angle = 50f, Radius = 0.02f, Max = 800,
        });
        public static void Burst(Vector3 at, Color c, int n, float size = 0.12f) => WeaponFx.Emit(Sparks(c), at, n, Random.onUnitSphere, size: size);

        // ---------- effects (each a coroutine on BossFx) ----------

        private static void Run(IEnumerator e) { if (BossFx.Instance) BossFx.Instance.StartCoroutine(e); }

        // A ring of shock rolling out over the ground from `at` to `radius`, in `c`.
        public static void Shock(Vector3 at, float radius, Color c, float seconds = 0.5f, float inner = 0.75f) => Run(ShockRun(at, radius, c, seconds, inner));
        private static IEnumerator ShockRun(Vector3 at, float radius, Color c, float seconds, float inner)
        {
            var t = WeaponFx.Piece(Ring(inner), Wash(c));
            var mat = t.GetComponent<MeshRenderer>().sharedMaterial;
            t.position = new Vector3(at.x, BossFx.Ground(at) + 0.12f, at.z);
            for (float s = 0f; s < seconds && t; s += Time.deltaTime)
            {
                float k = s / seconds, e = 1f - (1f - k) * (1f - k);
                t.localScale = new Vector3(radius * Mathf.Max(0.05f, e), 1f, radius * Mathf.Max(0.05f, e));
                mat.color = new Color(c.r, c.g, c.b, 0.9f * (1f - k * k));
                yield return null;
            }
            Object.Destroy(mat);
            if (t) Object.Destroy(t.gameObject);
        }

        // A flash: a hot core shrinking away inside a see-through halo of `c` that swells and fades, lighting the ground
        // round it for a moment: the heart of an impact.
        // The halo stops growing at HaloMax across (a legendary chest's 7.8 m, the steed's 5 m, were a wall of colour over
        // the screen up close); the light still reaches as far as the size asks.
        public const float HaloMax = 4f;
        public static float HaloSize(float size) => Mathf.Min(size, HaloMax);
        public static void Flash(Vector3 at, float size, Color c, float seconds = 0.25f) => Run(FlashRun(at, size, c, seconds));
        private static IEnumerator FlashRun(Vector3 at, float size, Color c, float seconds)
        {
            var core = WeaponFx.Piece(WeaponFx.Ball, Glow(Color.Lerp(c, Color.white, 0.3f), 7f));
            var halo = WeaponFx.Piece(WeaponFx.Ball, Wash(new Color(c.r, c.g, c.b, 0.45f)));
            var hm = halo.GetComponent<MeshRenderer>().sharedMaterial;
            core.position = halo.position = at;
            // A light only while few are lit (a nova flashes on every fish in reach at once).
            // Counted by the live ones, so a flash cut short (a scene change) never holds a slot.
            Light l = null;
            Lights.RemoveAll(x => !x);
            if (Lights.Count < MaxLights)
            {
                l = new GameObject("BossVfxLight").AddComponent<Light>();
                Lights.Add(l);
                l.type = LightType.Point; l.color = c; l.range = size * 4f; l.shadows = LightShadows.None; l.transform.position = at;
            }
            var cam = GameInfo.CurCamera;
            for (float s = 0f; s < seconds && core; s += Time.deltaTime)
            {
                float k = s / seconds, swell = 1f - (1f - k) * (1f - k);
                // Never round the camera either: standing in it tinted the whole screen.
                float across = cam ? Mathf.Min(HaloSize(size), Room(Vector3.Distance(cam.transform.position, at))) : HaloSize(size);
                core.localScale = Vector3.one * across * 0.35f * (1f - k);
                halo.localScale = Vector3.one * across * (0.45f + 0.55f * swell);
                hm.color = new Color(c.r, c.g, c.b, 0.45f * (1f - k));
                if (l) l.intensity = 3f * (1f - k);
                yield return null;
            }
            if (core) Object.Destroy(core.gameObject);
            if (halo) Object.Destroy(halo.gameObject);
            Object.Destroy(hm);
            if (l) Object.Destroy(l.gameObject);
        }
        public const int MaxLights = 4;
        private static readonly List<Light> Lights = new List<Light>();
        // How wide a flash `dist` from the camera may be and still leave it outside (0.4 m to spare).
        public static float Room(float dist) => 2f * Mathf.Max(0.1f, dist - 0.4f);

        // Chunks thrown out of the ground (lit, in `c`, falling back under gravity).
        public static void Debris(Vector3 at, int n, Color c, float speed = 6f, float size = 0.25f)
        {
            var ps = WeaponFx.System("BossDebris" + ColorUtility.ToHtmlStringRGB(c), () => new WeaponFx.Spec
            {
                Mat = Solid(c), Mesh = WeaponFx.Cube, Life = new Vector2(0.9f, 1.6f), Speed = new Vector2(3f, 8f), Size = new Vector2(0.12f, 0.3f),
                Gravity = 2.2f, Drag = 0.2f, SizeOverLife = WeaponFx.Curve(0f, 1f, 0.75f, 1f, 1f, 0f), Shape = ParticleSystemShapeType.Cone, Angle = 40f, Radius = 0.3f, Max = 600,
            });
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = (Vector3.up * 1.4f + Random.insideUnitSphere.Flat()).normalized;
                WeaponFx.Emit(ps, at + Vector3.up * 0.2f, 1, dir, size: size * Random.Range(0.6f, 1.4f), velocity: dir * speed * Random.Range(0.6f, 1.2f));
            }
        }

        // Puffs of dust round a spot (soft, slow).
        public static void Dust(Vector3 at, float radius, int n)
        {
            for (int i = 0; i < n; i++)
            {
                Vector3 p = at + Random.insideUnitSphere.Flat() * radius;
                p.y = BossFx.Ground(p) + 0.2f;
                WeaponFx.Emit(WeaponFx.Dust, p, 2, Vector3.up);
            }
        }

        // A mark left on the ground (dark, in a ring of `c`) that fades over `seconds`.
        public static void Scar(Vector3 at, float radius, Color c, float seconds = 6f) => Run(ScarRun(at, radius, c, seconds));
        private static IEnumerator ScarRun(Vector3 at, float radius, Color c, float seconds)
        {
            var go = BossFx.Decal("circle", at, at + Vector3.forward, radius, 0, c);
            var r = go.GetComponent<MeshRenderer>();
            var mat = r.sharedMaterial;
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            var cols = new List<Color>();
            mesh.GetColors(cols);
            for (int i = 0; i < cols.Count; i++) cols[i] = cols[i].a > 0.6f ? new Color(c.r * 0.6f, c.g * 0.6f, c.b * 0.6f, 0.5f) : new Color(0.08f, 0.06f, 0.05f, 0.55f);
            mesh.SetColors(cols);
            for (float s = 0f; s < seconds && go; s += Time.deltaTime) { mat.color = new Color(1f, 1f, 1f, Mathf.Clamp01((seconds - s) / 1.5f)); yield return null; }
            Object.Destroy(mat);
            if (go) Object.Destroy(go);
        }

        // Rock spires bursting up out of the ground at `points` one after another, glowing at the tip in `tip` (clear: none),
        // standing `hold` seconds, then crumbling back down.
        public static void Spires(IList<Vector3> points, float height, Color rock, Color tip, float hold = 1.6f, float every = 0.03f) => Run(SpiresRun(points, height, rock, tip, hold, every));
        private static IEnumerator SpiresRun(IList<Vector3> points, float height, Color rock, Color tip, float hold, float every)
        {
            var made = new List<(Transform t, Vector3 size)>();
            var rockMat = Solid(rock);
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                p.y = BossFx.Ground(p) - 0.3f;
                var s = WeaponFx.Piece(Rock(i % 6), rockMat);
                s.position = p;
                s.rotation = Quaternion.Euler(Random.Range(-14f, 14f), Random.value * 360f, Random.Range(-14f, 14f));
                var size = new Vector3(Random.Range(0.8f, 1.2f), height * Random.Range(0.75f, 1.2f), Random.Range(0.8f, 1.2f));
                made.Add((s, size));
                if (tip.a > 0f)
                {
                    var g = WeaponFx.Piece(Shard, Glow(tip, 3f), s);
                    g.localPosition = new Vector3(0f, 0.82f, 0f);
                    g.localScale = new Vector3(0.45f, 0.35f, 0.45f);
                }
                BossFx.Instance.StartCoroutine(Grow(s, size, 0.12f));
                Debris(p, 4, rock, 5f, 0.22f);
                WeaponFx.Emit(WeaponFx.Dust, p + Vector3.up * 0.3f, 4, Vector3.up);
                if (i % 3 == 0) Fx.SoundRandom("ItemImpactHeavy_V", 1, 3, p, 0.5f);
                if (every > 0f) yield return new WaitForSeconds(every);
            }
            yield return new WaitForSeconds(hold);
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                foreach (var (s, size) in made) if (s) s.localScale = new Vector3(size.x, size.y * (1f - t / 0.5f), size.z);
                yield return null;
            }
            foreach (var (s, _) in made) if (s) { Dust(s.position, 0.3f, 1); Object.Destroy(s.gameObject); }
        }

        private static IEnumerator Grow(Transform t, Vector3 to, float seconds)
        {
            for (float s = 0f; s < seconds && t; s += Time.deltaTime) { float k = s / seconds; t.localScale = new Vector3(to.x, to.y * (1f - (1f - k) * (1f - k)), to.z); yield return null; }
            if (t) t.localScale = to;
        }

        // An impact: a flash, a shock ring, chunks and dust, a mark on the ground.
        public static void Impact(Vector3 at, float radius, Color c, Color rock, bool scar = true)
        {
            Vector3 g = new Vector3(at.x, BossFx.Ground(at), at.z);
            Flash(g + Vector3.up * 0.6f, radius * 0.9f, c, 0.3f);
            Shock(g, radius * 1.6f, c, 0.45f);
            Debris(g, Mathf.RoundToInt(6 + radius * 4f), rock, 4f + radius * 1.5f, 0.2f + radius * 0.05f);
            Dust(g, radius * 0.8f, Mathf.RoundToInt(4 + radius * 3f));
            if (scar) Scar(g, radius * 0.9f, c, 5f);
        }

        // ---------- a bendable tube (tentacles) ----------

        // A tube along points (rebuilt whenever Set is called), thick at the root, thin at the tip, faceted like the game.
        public class Tube
        {
            public readonly Transform T;
            private readonly Mesh _mesh = new Mesh { name = "BossVfxTube" };
            private readonly int _sides;
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<int> _t = new List<int>();

            public Tube(Material mat, int sides = 7)
            {
                _sides = sides;
                T = new GameObject("BossVfxTube", typeof(MeshFilter), typeof(MeshRenderer)).transform;
                T.GetComponent<MeshFilter>().sharedMesh = _mesh;
                var r = T.GetComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                _mesh.MarkDynamic();
            }

            public void Set(IList<Vector3> path, float root, float tip)
            {
                _v.Clear(); _t.Clear();
                int n = path.Count;
                Vector3 normal = Vector3.forward;
                var rings = new List<Vector3[]>();
                for (int i = 0; i < n; i++)
                {
                    Vector3 dir = (path[Mathf.Min(n - 1, i + 1)] - path[Mathf.Max(0, i - 1)]).normalized;
                    if (dir.sqrMagnitude < 1e-6f) dir = Vector3.up;
                    normal = Vector3.Cross(Vector3.Cross(dir, normal), dir).normalized;
                    if (normal.sqrMagnitude < 1e-6f) normal = Vector3.Cross(dir, Vector3.right).normalized;
                    Vector3 bin = Vector3.Cross(dir, normal);
                    float rad = Mathf.Lerp(root, tip, i / (float)(n - 1));
                    var ring = new Vector3[_sides];
                    for (int k = 0; k < _sides; k++) { float a = k * Mathf.PI * 2f / _sides; ring[k] = path[i] + (normal * Mathf.Cos(a) + bin * Mathf.Sin(a)) * rad; }
                    rings.Add(ring);
                }
                for (int i = 0; i + 1 < n; i++)
                    for (int k = 0; k < _sides; k++)
                    {
                        int j = (k + 1) % _sides;
                        foreach (var p in new[] { rings[i][k], rings[i + 1][k], rings[i][j], rings[i][j], rings[i + 1][k], rings[i + 1][j] }) { _t.Add(_v.Count); _v.Add(p); }
                    }
                _mesh.Clear();
                _mesh.SetVertices(_v);
                _mesh.SetTriangles(_t, 0);
                _mesh.RecalculateNormals();
                _mesh.RecalculateBounds();
            }

            public void Destroy() { if (T) Object.Destroy(T.gameObject); Object.Destroy(_mesh); }
        }
    }
}
