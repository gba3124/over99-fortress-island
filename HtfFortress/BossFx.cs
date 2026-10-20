using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Object;
using UnityEngine;

namespace HtfFortress
{
    // What the bosses' moves look like, on every screen, from the host's BossFx events (Key names the move): the warnings
    // on the ground before a move lands (red, its edge in the boss's colour, throbbing faster as it comes), the moves
    // themselves, and the boss reeling or turning into its second form. Purely visual.
    public partial class BossFx : MonoBehaviour
    {
        public static BossFx Instance;
        private void Awake() => Instance = this;

        // Host: to everyone. `boss` tells the clients whose move it is (its colour, where its body is).
        public static void Send(string key, Creature boss, Vector3 a, Vector3 b, int arg = 0, int who = 0)
        {
            if (InstanceFinder.IsServerStarted)
                InstanceFinder.ServerManager.Broadcast(new FxEvent { Kind = FxKind.BossFx, Key = key, From = a, To = b, PieceId = boss ? boss.ObjectId : 0, Arg = arg, Who = who });
        }

        public static Transform BodyOf(int id)
        {
            var cm = InstanceFinder.ClientManager;
            return id != 0 && cm != null && cm.Objects.Spawned.TryGetValue(id, out NetworkObject nob) && nob ? nob.transform : null;
        }

        private static EnemyKind KindOf(int id) => Enemies.Instance && Enemies.Instance.ClientTagOf(id) is EnemyTag t ? t.Kind : EnemyKind.Runner;
        public static Color ColourOf(int id) => BossLooks.Colour(KindOf(id));

        public void Play(FxEvent e)
        {
            string k = e.Key ?? "";
            if (k.StartsWith("w.")) { StartCoroutine(Warning(k.Substring(2), e.From, e.To, e.Who % 100000 / 100f, e.Who / 100000, e.Arg / 1000f, ColourOf(e.PieceId))); return; }
            switch (k)
            {
                case "winding": StartCoroutine(Winding(e.PieceId, e.Arg / 1000f)); return;
                case "broken":
                    StartCoroutine(Dizzy(e.PieceId, e.Arg / 1000f)); Fx.Sound("Hitmarker", e.From, 1f, AudioDistance.Long); Burst(e.From, "Gold", 40, 0.18f);
                    if (BossEye.Of(e.PieceId) is BossEye eye) eye.Reel(e.Arg / 1000f); // the eye squints, shaking
                    return;
                case "stomp": WeaponFx.Emit(WeaponFx.Dust, e.From, 8, Vector3.up); Fx.SoundRandom("ItemImpactHeavy_V", 1, 3, e.From, 0.7f); return;
                case "phase2": StartCoroutine(Transform2(e.PieceId)); return;
                case "died": StartCoroutine(Death(e.PieceId, e.From, e.Arg / 100f)); return;
                case "dark": Night(e.Arg > 0); return;
                case "trophy": StartCoroutine(Trophy(e.From, (EnemyKind)e.Who)); return;
            }
            if (BossMoves.Play(this, e)) return;
            BuildSystem.Log.LogWarning("BossFx: no effect " + k);
        }

        // ---------- the warnings ----------

        // shape: circle (a, radius), ring (a, size = outer radius, extra = inner), lane (a to b, width), cone (apex a, toward b,
        // range |b - a|, extra = half angle), box (centre a, half size, turned toward b).
        // The red fills in from the middle (from the start of a lane, the point of a cone) as the move comes, a bright edge
        // running ahead of it, so where it's got to says how long is left; the rim throbs faster and faster.
        private IEnumerator Warning(string shape, Vector3 a, Vector3 b, float size, int extra, float seconds, Color edge)
        {
            var go = Decal(shape, a, b, size, extra, edge);
            if (!go) yield break;
            var mat = go.GetComponent<MeshRenderer>().sharedMaterial;
            var shape_ = go.GetComponent<DropMesh>();
            var cols = new List<Color>();
            shape_.Mesh.GetColors(cols);
            for (float t = 0f; t < seconds && go; t += Time.deltaTime)
            {
                float k = t / seconds, beat = Mathf.Lerp(3f, 14f, k * k), pulse = 0.5f + 0.5f * Mathf.Sin(t * beat);
                for (int i = 0; i < cols.Count; i++)
                {
                    if (shape_.Rim[i]) { var c = cols[i]; cols[i] = new Color(c.r, c.g, c.b, 0.7f + 0.3f * pulse); continue; }
                    float u = shape_.U[i];
                    float filled = Mathf.Clamp01((k * 1.03f - u) / 0.05f), front = Mathf.Exp(-((u - k) * (u - k)) / 0.0016f);
                    cols[i] = new Color(0.95f, 0.05f + 0.35f * front, 0.03f, 0.26f + 0.3f * filled + 0.35f * front);
                }
                shape_.Mesh.SetColors(cols);
                mat.color = new Color(1f, 1f, 1f, Mathf.Clamp01(t / 0.12f));
                yield return null;
            }
            if (!go) yield break;
            for (float t = 0f; t < 0.25f && go; t += Time.deltaTime) { mat.color = new Color(1f, 1f, 1f, 1f - t / 0.25f); yield return null; }
            Destroy(mat);
            Destroy(go);
        }

        // A flat mesh lying on the ground (or the sea), its vertices dropped onto whatever is under them; red inside, its
        // rim in the boss's colour.
        internal static GameObject Decal(string shape, Vector3 a, Vector3 b, float size, int extra, Color edge)
        {
            var pts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            var us = new List<float>();
            var rims = new List<bool>();
            Color fill = new Color(0.95f, 0.05f, 0.03f, 0.5f), rim = new Color(Mathf.Lerp(edge.r, 1f, 0.25f), Mathf.Lerp(edge.g, 0.15f, 0.25f), Mathf.Lerp(edge.b, 0.1f, 0.25f), 1f);
            Vector3 fwd = (b - a).Flat(); fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            // A grid of rows x cols, `at(u, v)` places a vertex (u along, v across, both 0..1); rimmed near the outer edge.
            // `fillAt`: how far into the move's fill-in a vertex is (0 where it starts, 1 where it ends; by default u).
            void Grid(int rows, int columns, System.Func<float, float, Vector3> at, System.Func<float, float, bool> onRim, bool wrap, System.Func<float, float, float> fillAt = null)
            {
                int start = pts.Count;
                for (int i = 0; i <= rows; i++)
                    for (int j = 0; j <= columns; j++)
                    {
                        float u = (float)i / rows, v = (float)j / columns;
                        Vector3 p = at(u, v);
                        p.y = Floor(p) + 0.08f;
                        pts.Add(p);
                        bool edgeHere = onRim(u, v);
                        cols.Add(edgeHere ? rim : fill);
                        rims.Add(edgeHere);
                        us.Add(fillAt != null ? fillAt(u, v) : u);
                    }
                int w = columns + 1;
                for (int i = 0; i < rows; i++)
                    for (int j = 0; j < columns; j++)
                    {
                        int q = start + i * w + j;
                        tris.AddRange(new[] { q, q + w, q + 1, q + 1, q + w, q + w + 1 });
                    }
            }
            switch (shape)
            {
                case "circle":
                case "ring":
                {
                    float r0 = shape == "ring" ? extra / 100f : 0f, band = Mathf.Max(0.6f, size - r0);
                    Grid(Mathf.Clamp(Mathf.CeilToInt(band / 0.3f), 4, 40), 40,
                         (u, v) => a + Quaternion.Euler(0f, v * 360f, 0f) * Vector3.forward * Mathf.Lerp(r0, size, u),
                         (u, v) => u > 1f - 0.28f / band || (r0 > 0f && u < 0.2f / band), true);
                    break;
                }
                case "lane":
                {
                    float len = (b - a).Flat().magnitude;
                    Grid(Mathf.Clamp(Mathf.CeilToInt(len / 0.8f), 2, 90), 10, (u, v) => a + fwd * len * u + right * (v - 0.5f) * size,
                         (u, v) => v < 0.05f || v > 0.95f || u > 1f - 0.3f / Mathf.Max(1f, len), false);
                    break;
                }
                case "cone":
                {
                    float range = (b - a).Flat().magnitude, half = Mathf.Max(5f, extra);
                    Grid(Mathf.Clamp(Mathf.CeilToInt(range / 0.6f), 3, 50), 28, (u, v) => a + Quaternion.AngleAxis((v - 0.5f) * 2f * half, Vector3.up) * fwd * range * u,
                         (u, v) => u > 1f - 0.3f / Mathf.Max(1f, range) || v < 0.02f || v > 0.98f, false);
                    break;
                }
                default: // box
                    Grid(Mathf.Clamp(Mathf.CeilToInt(size * 2f / 0.6f), 2, 40), Mathf.Clamp(Mathf.CeilToInt(size * 2f / 0.6f), 2, 40),
                         (u, v) => a + fwd * (u - 0.5f) * 2f * size + right * (v - 0.5f) * 2f * size,
                         (u, v) => u < 0.03f || u > 0.97f || v < 0.03f || v > 0.97f, false,
                         (u, v) => Mathf.Max(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f)) * 2f); // from the middle out
                    break;
            }
            var mesh = new Mesh { name = "BossWarning" };
            mesh.indexFormat = pts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(pts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject("BossWarning", typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            var m = new Material(Shader.Find("Sprites/Default")) { renderQueue = 3010 };
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var drop = go.AddComponent<DropMesh>();
            drop.Mesh = mesh; drop.U = us; drop.Rim = rims;
            return go;
        }

        // Land or sea, whichever is higher (a warning out on the water lies on it).
        public static float Ground(Vector3 p)
        {
            float y = WaterManager.GetWaterHeight(p);
            if (Physics.Raycast(new Vector3(p.x, p.y + 25f, p.z), Vector3.down, out var h, 60f, GameInfo.LevelLayer)) y = Mathf.Max(y, h.point.y);
            return y;
        }

        // Its mesh goes with it; for a warning, where each vertex is in the fill-in and which are the rim.
        // The ground under a mark lying on it: land or sea, never the fort's buildings (a warning over a turret or a wall
        // used to be draped up over it, a red or green curtain standing in the air).
        private static readonly RaycastHit[] Hits = new RaycastHit[12];
        public static float Floor(Vector3 p)
        {
            float y = WaterManager.GetWaterHeight(p);
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + 25f, p.z), Vector3.down, Hits, 60f, GameInfo.LevelLayer);
            for (int i = 0; i < n; i++)
            {
                bool built = false;
                for (var t = Hits[i].collider.transform; t && !built; t = t.parent) built = t.name == "FortressPiece";
                if (!built) y = Mathf.Max(y, Hits[i].point.y);
            }
            return y;
        }

        internal class DropMesh : MonoBehaviour { public Mesh Mesh; public List<float> U; public List<bool> Rim; private void OnDestroy() { if (Mesh) Destroy(Mesh); } }

        // ---------- winding up, reeling, the second form, the end ----------

        // Over its head while it winds up a move that can be broken: a gold ring closing in.
        private IEnumerator Winding(int id, float seconds)
        {
            var body = BodyOf(id);
            if (!body) yield break;
            var ring = WeaponFx.Piece(WeaponFx.Torus, BossVfx.Glow(new Color(1f, 0.8f, 0.25f), 6f));
            float top = Top(body);
            for (float t = 0f; t < seconds && ring && body; t += Time.deltaTime)
            {
                float k = t / seconds;
                ring.position = new Vector3(body.position.x, body.position.y + top + 0.6f, body.position.z);
                ring.rotation = Quaternion.Euler(90f, t * 180f, 0f);
                ring.localScale = Vector3.one * Mathf.Lerp(3.2f, 0.6f, k) * (1f + 0.08f * Mathf.Sin(t * 30f));
                yield return null;
            }
            if (ring) Destroy(ring.gameObject);
        }

        // Stars round its head.
        private IEnumerator Dizzy(int id, float seconds)
        {
            var body = BodyOf(id);
            if (!body) yield break;
            var stars = new List<Transform>();
            var mat = BossVfx.Glow(new Color(1f, 0.9f, 0.35f), 7f);
            for (int i = 0; i < 5; i++) { var s = WeaponFx.Piece(WeaponFx.Cube, mat); s.localScale = Vector3.one * 0.35f; stars.Add(s); }
            float top = Top(body);
            for (float t = 0f; t < seconds && body; t += Time.deltaTime)
            {
                for (int i = 0; i < stars.Count; i++)
                {
                    float a = t * 4f + i * Mathf.PI * 2f / stars.Count;
                    stars[i].position = body.position + new Vector3(Mathf.Cos(a) * 1.4f, top + 0.4f + 0.15f * Mathf.Sin(t * 6f + i), Mathf.Sin(a) * 1.4f);
                    stars[i].rotation = Quaternion.Euler(t * 300f, t * 200f, 45f);
                }
                yield return null;
            }
            foreach (var s in stars) if (s) Destroy(s.gameObject);
        }

        // Half health: it roars, the screen flashes its colour, a ring of light bursts out of it.
        private IEnumerator Transform2(int id)
        {
            var body = BodyOf(id);
            if (!body) yield break;
            Color c = ColourOf(id);
            Fx.Sound("BossFightIntro", body.position, 1f, AudioDistance.Long);
            float top = Top(body);
            for (int i = 0; i < 3; i++)
            {
                if (!body) yield break;
                Burst(body.position + Vector3.up * top * 0.6f, "Gold", 30, 0.2f);
                WeaponFx.Emit(WeaponFx.Rings, body.position + Vector3.up * 0.5f, 1, Vector3.up, color: c, size: 10f + 6f * i);
                yield return new WaitForSeconds(0.35f);
            }
        }

        // It goes down: a blast in its colour, sparks everywhere.
        private IEnumerator Death(int id, Vector3 at, float size)
        {
            Color c = ColourOf(id);
            Fx.Sound("Explosion", at, 1f, AudioDistance.Long);
            try { VFXManager.Play("LavaExplosion", at); } catch { }
            for (int i = 0; i < 4; i++)
            {
                Burst(at + Random.insideUnitSphere * size, "Gold", 30, 0.25f);
                WeaponFx.Emit(WeaponFx.Rings, at, 1, Vector3.up, color: c, size: 6f + 6f * i);
                WeaponFx.Game(i % 2 == 0 ? "Confetti" : "RedConfetti", at + Vector3.up * 2f, Vector3.up);
                yield return new WaitForSeconds(0.2f);
            }
        }

        // The sky over the fight: the anglerfish's night (the sun all but out, the light, the haze and the sky gone dark
        // blue, so only what glows shows: its lantern, the lighthouse, the turrets' fire) and the Eye's blood-red sky (the
        // sun, the haze and the sky gone red), each eased in and back out, and everything as it was once both are over.
        // The night wins over the red.
        private bool _night, _red;
        private float _nightK, _redK;
        private Coroutine _mood;
        public void Night(bool on) { _night = on; if (on && _mood == null) _mood = StartCoroutine(Mood()); }
        public void RedSky(bool on) { _red = on; if (on && _mood == null) _mood = StartCoroutine(Mood()); }
        public bool Moody => _mood != null; // auto-test

        private IEnumerator Mood()
        {
            var sun = RenderSettings.sun ? RenderSettings.sun : FindObjectsOfType<Light>().Where(l => l.type == LightType.Directional && l.enabled).OrderByDescending(l => l.intensity).FirstOrDefault();
            float sunI0 = sun ? sun.intensity : 1f, fogD0 = RenderSettings.fogDensity, fogEnd0 = RenderSettings.fogEndDistance;
            Color sunC0 = sun ? sun.color : Color.white, fogC0 = RenderSettings.fogColor;
            var amb0 = RenderSettings.ambientProbe;
            var sky0 = RenderSettings.skybox;
            var sky = sky0 ? new Material(sky0) : null;
            var skyColours = new List<(string name, Color was)>();
            if (sky)
            {
                RenderSettings.skybox = sky;
                for (int i = 0; i < sky.shader.GetPropertyCount(); i++)
                    if (sky.shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color) skyColours.Add((sky.shader.GetPropertyName(i), sky0.GetColor(sky.shader.GetPropertyName(i))));
            }
            float exposure0 = sky && sky.HasProperty("_Exposure") ? sky0.GetFloat("_Exposure") : -1f;
            Color nightTint = new Color(0.3f, 0.36f, 0.62f), redTint = new Color(1f, 0.32f, 0.26f);
            while (_night || _red || _nightK > 0f || _redK > 0f)
            {
                _nightK = Mathf.MoveTowards(_nightK, _night ? 1f : 0f, Time.deltaTime / 1.5f);
                _redK = Mathf.MoveTowards(_redK, _red ? 1f : 0f, Time.deltaTime / 1.2f);
                float n = Mathf.SmoothStep(0f, 1f, _nightK), r = Mathf.SmoothStep(0f, 1f, _redK) * (1f - n);
                Color tint = Color.Lerp(Color.Lerp(Color.white, redTint, r), nightTint, n);
                float dim = Mathf.Lerp(1f, 0.1f, n) * Mathf.Lerp(1f, 0.8f, r), skyDim = Mathf.Lerp(1f, 0.15f, n) * Mathf.Lerp(1f, 0.7f, r);
                if (sun) { sun.intensity = sunI0 * dim; sun.color = sunC0 * tint; }
                var amb = amb0;
                for (int ch = 0; ch < 3; ch++) for (int k = 0; k < 9; k++) amb[ch, k] *= tint[ch] * Mathf.Lerp(1f, 0.3f, n);
                RenderSettings.ambientProbe = amb;
                RenderSettings.fogColor = Color.Lerp(Color.Lerp(fogC0, new Color(0.4f, 0.05f, 0.05f), r), new Color(0.02f, 0.03f, 0.08f), n);
                RenderSettings.fogDensity = fogD0 * (1f + 2.5f * Mathf.Max(n, r)) + 0.01f * n;
                RenderSettings.fogEndDistance = fogEnd0 * Mathf.Lerp(1f, 0.4f, Mathf.Max(n, r));
                if (sky)
                {
                    foreach (var (name, was) in skyColours) sky.SetColor(name, new Color(was.r * tint.r * skyDim, was.g * tint.g * skyDim, was.b * tint.b * skyDim, was.a));
                    if (exposure0 >= 0f) sky.SetFloat("_Exposure", exposure0 * skyDim);
                }
                yield return null;
            }
            if (sun) { sun.intensity = sunI0; sun.color = sunC0; }
            RenderSettings.ambientProbe = amb0;
            RenderSettings.fogColor = fogC0;
            RenderSettings.fogDensity = fogD0;
            RenderSettings.fogEndDistance = fogEnd0;
            if (sky) { if (RenderSettings.skybox == sky) RenderSettings.skybox = sky0; Destroy(sky); }
            _mood = null;
        }

        // Where a boss's trophy lands: a pillar of its colour, gold sparks raining, a chime.
        private IEnumerator Trophy(Vector3 at, EnemyKind k)
        {
            Color c = BossLooks.Colour(k);
            var pillar = WeaponFx.Piece(WeaponFx.Cyl, BossVfx.Glow(c, 6f));
            Fx.Sound("ShinyCreatureCaught_0" + Random.Range(1, 4), at, 1f, AudioDistance.Long);
            Vector3 ground = new Vector3(at.x, Ground(at), at.z);
            for (float t = 0f; t < 2.5f && pillar; t += Time.deltaTime)
            {
                float k01 = t / 2.5f, w = 0.5f * (1f - k01) + 0.05f;
                pillar.position = ground + Vector3.up * 6f;
                pillar.localScale = new Vector3(w, 6f, w);
                if (Random.value < 0.5f) Burst(ground + Vector3.up * Random.Range(0.5f, 10f) + Random.insideUnitSphere.Flat() * 0.6f, "Gold", 2, 0.1f);
                yield return null;
            }
            if (pillar) Destroy(pillar.gameObject);
        }

        // ---------- bits shared by the moves ----------

        public static float Top(Transform body) => BossLooks.WorldBounds(body).max.y - body.position.y;

        public static void Burst(Vector3 at, string colour, int n, float size) => BossVfx.Burst(at, WeaponFx.SparkColour(colour), n, size);
    }
}
